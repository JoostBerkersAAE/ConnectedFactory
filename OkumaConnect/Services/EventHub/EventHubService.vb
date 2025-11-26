Imports System
Imports System.Text
Imports System.Text.Json
Imports System.Threading.Tasks
Imports System.Collections.Generic
Imports Azure.Messaging.EventHubs
Imports Azure.Messaging.EventHubs.Producer
Imports OkumaConnect.Services.Logging
Imports OkumaConnect.Services.Configuration
Imports OkumaConnect.Services.DataCollection
Imports OkumaConnect.Models

Namespace Services.EventHub

''' <summary>
''' Azure Event Hub Service voor OkumaConnect
''' Verzendt machine data van OPC UA naar Azure Event Hub
''' Gebaseerd op QOkumaConnect implementatie
''' </summary>
Public Class EventHubService
    Implements IDisposable
    
    Private Shared _instance As EventHubService
    Private _eventHubClient As EventHubProducerClient
    Private _isInitialized As Boolean = False
    Private ReadOnly _logger As ILogger
    Private _disposed As Boolean = False
    
    ''' <summary>
    ''' Singleton pattern
    ''' </summary>
    Public Shared ReadOnly Property Instance As EventHubService
        Get
            If _instance Is Nothing Then
                Throw New InvalidOperationException("EventHubService not initialized. Call Initialize() first.")
            End If
            Return _instance
        End Get
    End Property
    
    ''' <summary>
    ''' Initialize the singleton instance
    ''' </summary>
    Public Shared Sub Initialize(logger As ILogger)
        If _instance Is Nothing Then
            _instance = New EventHubService(logger)
        End If
    End Sub
    
    Private Sub New(logger As ILogger)
        _logger = logger
        InitializeClient()
    End Sub
    
    ''' <summary>
    ''' Initialiseert de Event Hub client met configuratie uit environment variables
    ''' </summary>
    Private Sub InitializeClient()
        Try
            If _isInitialized Then
                Return
            End If
            
            If Not GetEventHubEnabled() Then
                _logger.LogInfo("⚠️ Event Hub is disabled via configuration")
                Return
            End If
            
            Dim connectionString = GetEventHubConnectionString()
            
            If String.IsNullOrEmpty(connectionString) Then
                _logger.LogWarning("⚠️ Event Hub connection string not found - Event Hub integration disabled")
                Return
            End If
            
            ' Check if connection string already contains EntityPath
            If connectionString.Contains("EntityPath=") Then
                ' Use connection string as-is (includes Event Hub name)
                _eventHubClient = New EventHubProducerClient(connectionString)
                _logger.LogInfo($"✅ Event Hub client initialized with EntityPath from connection string")
            Else
                ' Use separate Event Hub name
                Dim eventHubName = GetEventHubName()
                If String.IsNullOrEmpty(eventHubName) Then
                    _logger.LogWarning("⚠️ Event Hub name not found and not in connection string - Event Hub integration disabled")
                    Return
                End If
                _eventHubClient = New EventHubProducerClient(connectionString, eventHubName)
                _logger.LogInfo($"✅ Event Hub client initialized for: {eventHubName}")
            End If
            
            _isInitialized = True
            
        Catch ex As Exception
            _logger.LogError($"❌ Failed to initialize Event Hub client: {ex.Message}")
            _isInitialized = False
        End Try
    End Sub
    
    ''' <summary>
    ''' Send MacMan collection results to Event Hub
    ''' </summary>
    Public Async Function SendCollectionResultsAsync(machineId As Integer, machineIp As String, machineName As String, results As List(Of DataCollection.CollectionResult)) As Task(Of Integer)
        If Not _isInitialized OrElse _eventHubClient Is Nothing Then
            _logger.LogDebug("Event Hub not initialized - skipping collection results send")
            Return 0
        End If
        
        Try
            Dim eventDataList As New List(Of EventData)()
            Dim totalRecords = 0
            
            For Each result In results
                If result IsNot Nothing AndAlso result.Records IsNot Nothing Then
                    For Each record In result.Records
                        Dim eventData = CreateMacManEventData(machineId, machineIp, machineName, record)
                        If eventData IsNot Nothing Then
                            eventDataList.Add(eventData)
                            totalRecords += 1
                        End If
                    Next
                End If
            Next
            
            If eventDataList.Count > 0 Then
                ' Send all events in one batch
                Await _eventHubClient.SendAsync(eventDataList)
                _logger.LogDebug($"📤 Sent {eventDataList.Count} MacMan events to Event Hub")
            End If
            
            Return totalRecords
            
        Catch ex As Exception
            _logger.LogError($"❌ Error sending collection results to Event Hub: {ex.Message}")
            Return 0
        End Try
    End Function
    
    ''' <summary>
    ''' Create EventData from MacManRecord - exact copy from QOkumaConnect
    ''' </summary>
    Private Function CreateMacManEventData(machineId As Integer, machineIp As String, machineName As String, record As DataCollection.MacManRecord) As EventData
        Try
            If record Is Nothing OrElse record.Data Is Nothing Then
                Return Nothing
            End If
            
            Dim screenType = record.ScreenType
            
            ' Determine timestamp based on screen type
            Dim actualTimestamp As String
            
            ' For OPERATING_REPORT_DISPLAY, ALWAYS use current timestamp (as requested by user)
            If screenType = "OPERATING_REPORT_DISPLAY" Then
                actualTimestamp = DateTime.Now.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                Console.WriteLine($"🔍 EventHub using CURRENT timestamp for OPERATING_REPORT_DISPLAY: {actualTimestamp}")
            Else
                ' For other screen types, use ProcessedDate from record data (already correctly parsed in MacManCollectors)
                If record.Data.ContainsKey("ProcessedDate") Then
                    Dim processedDateValue = record.Data("ProcessedDate")
                    If TypeOf processedDateValue Is DateTime Then
                        Dim processedDateTime = DirectCast(processedDateValue, DateTime)
                        actualTimestamp = processedDateTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                        Console.WriteLine($"🔍 EventHub using ProcessedDate: {actualTimestamp}")
                    ElseIf TypeOf processedDateValue Is DateTime? Then
                        Dim processedDateTime = DirectCast(processedDateValue, DateTime?)
                        If processedDateTime.HasValue Then
                            actualTimestamp = processedDateTime.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                            Console.WriteLine($"🔍 EventHub using ProcessedDate (nullable): {actualTimestamp}")
                        Else
                            actualTimestamp = record.CollectionTimestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                            Console.WriteLine($"🔍 EventHub ProcessedDate is null, using CollectionTimestamp: {actualTimestamp}")
                        End If
                    Else
                        actualTimestamp = record.CollectionTimestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                        Console.WriteLine($"🔍 EventHub ProcessedDate wrong type, using CollectionTimestamp: {actualTimestamp}")
                    End If
                Else
                    ' Fallback to CollectionTimestamp if ProcessedDate not available
                    actualTimestamp = record.CollectionTimestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                    Console.WriteLine($"🔍 EventHub no ProcessedDate found, using CollectionTimestamp: {actualTimestamp}")
                End If
            End If
            
            ' Create tags dictionary (INCLUDE MainProgramName and ProgramName as tags)
            Dim tagsDict As New Dictionary(Of String, Object)
            tagsDict("machine_name") = machineName
            
            If record.Data.ContainsKey("MainProgramName") Then
                tagsDict("MainProgramName") = record.Data("MainProgramName")
            End If
            If record.Data.ContainsKey("ProgramName") Then
                tagsDict("ProgramName") = record.Data("ProgramName")
            End If
            
            ' Create fields dictionary (EXCLUDE internal fields like StartDay, StartTime, ProcessedDate, MainProgramName, ProgramName)
            Dim fieldsDict As New Dictionary(Of String, Object)
            Dim excludeFields As String() = {"StartDay", "StartTime", "ProcessedDate", "MainProgramName", "ProgramName", "Date", "Time"}
            
            For Each kvp In record.Data
                If Not excludeFields.Contains(kvp.Key) Then
                    fieldsDict(kvp.Key) = kvp.Value
                End If
            Next
            
            ' Get ProcessedDate for root level (always use current time as requested by user)
            Dim processedDate As String = DateTime.Now.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
            
            ' Create event object (ProcessedDate at ROOT level, not in fields!)
            Dim eventObject = New With {
                .machine_id = machineId,
                .machine_ip = machineIp,
                .timestamp = actualTimestamp,
                .measurement_type = screenType,
                .tags = tagsDict,
                .fields = fieldsDict,
                .ProcessedDate = processedDate
            }
            
            ' Serialize to JSON
            Dim jsonOptions = New JsonSerializerOptions() With {
                .WriteIndented = False,
                .PropertyNamingPolicy = Nothing
            }
            
            Dim jsonString = JsonSerializer.Serialize(eventObject, jsonOptions)
            
            ' Log detailed JSON for debugging (only first record of each batch to avoid spam)
            Static lastLoggedScreenType As String = ""
            If lastLoggedScreenType <> screenType Then
                _logger.LogInfo($"📤 Event Hub Message (MacMan Screen - {screenType}):")
                _logger.LogInfo($"   JSON: {jsonString}")
                lastLoggedScreenType = screenType
            End If
            
            Dim eventData = New EventData(Encoding.UTF8.GetBytes(jsonString))
            
            ' Add properties for routing/filtering
            eventData.Properties("machine_id") = machineId.ToString()
            eventData.Properties("machine_ip") = machineIp
            eventData.Properties("machine_name") = machineName
            eventData.Properties("measurement_type") = screenType
            
            Return eventData
        Catch ex As Exception
            _logger.LogError($"❌ Error creating MacMan event data: {ex.Message}")
            Return Nothing
        End Try
    End Function
    
    ''' <summary>
    ''' Send machine data to Event Hub
    ''' </summary>
    Public Async Function SendMachineDataAsync(machineId As Integer, machineIp As String, machineName As String, apiName As String, value As Object) As Task(Of Boolean)
        If Not _isInitialized OrElse _eventHubClient Is Nothing Then
            _logger.LogDebug("Event Hub not initialized - skipping data send")
            Return False
        End If
        
        Try
            ' Create event object
            Dim timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
            
            Dim eventObject = New With {
                .machine_id = machineId,
                .machine_ip = machineIp,
                .machine_name = machineName,
                .timestamp = timestamp,
                .measurement_type = apiName,
                .tags = New With {
                    .api_name = apiName
                },
                .fields = New With {
                    .value = value
                }
            }
            
            ' Serialize to JSON
            Dim jsonOptions = New JsonSerializerOptions() With {
                .WriteIndented = False,
                .PropertyNamingPolicy = Nothing
            }
            
            Dim jsonString = JsonSerializer.Serialize(eventObject, jsonOptions)
            
            ' Log detailed JSON for debugging
            _logger.LogInfo($"📤 Event Hub Message (Custom API):")
            _logger.LogInfo($"   JSON: {jsonString}")
            Dim eventData = New EventData(Encoding.UTF8.GetBytes(jsonString))
            
            ' Add properties for routing/filtering
            eventData.Properties("machine_id") = machineId.ToString()
            eventData.Properties("machine_ip") = machineIp
            eventData.Properties("machine_name") = machineName
            eventData.Properties("measurement_type") = apiName
            
            ' Send to Event Hub
            Await _eventHubClient.SendAsync({eventData})
            
            _logger.LogDebug($"📤 Sent to Event Hub: {machineName}.{apiName} = {value}")
            Return True
            
        Catch ex As Exception
            _logger.LogError($"❌ Error sending data to Event Hub: {ex.Message}")
            Return False
        End Try
    End Function
    
    ''' <summary>
    ''' Send custom event to Event Hub
    ''' </summary>
    Public Async Function SendCustomEventAsync(eventObject As Object) As Task(Of Boolean)
        If Not _isInitialized OrElse _eventHubClient Is Nothing Then
            _logger.LogDebug("Event Hub not initialized - skipping custom event send")
            Return False
        End If
        
        Try
            ' Serialize event to JSON
            Dim jsonOptions = New JsonSerializerOptions() With {
                .WriteIndented = False,
                .PropertyNamingPolicy = Nothing
            }
            Dim jsonString = JsonSerializer.Serialize(eventObject, jsonOptions)
            Dim eventData = New EventData(Encoding.UTF8.GetBytes(jsonString))
            
            ' Send to EventHub
            Await _eventHubClient.SendAsync({eventData})
            Return True
            
        Catch ex As Exception
            _logger.LogError($"❌ Error sending custom event to EventHub: {ex.Message}")
            Return False
        End Try
    End Function
    
    ''' <summary>
    ''' Controleert of Event Hub service beschikbaar is
    ''' </summary>
    Public ReadOnly Property IsAvailable As Boolean
        Get
            Return _isInitialized AndAlso _eventHubClient IsNot Nothing
        End Get
    End Property
    
    ''' <summary>
    ''' Configuration helpers - using EnvironmentLoader
    ''' </summary>
    Private Function GetEventHubEnabled() As Boolean
        Dim value As String = EnvironmentLoader.GetEnvironmentVariable("EVENTHUB_ENABLED", "false")
        Return String.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
    End Function
    
    Private Function GetEventHubConnectionString() As String
        Return EnvironmentLoader.GetEnvironmentVariable("EVENTHUB_CONNECTION_STRING", "")
    End Function
    
    Private Function GetEventHubName() As String
        Return EnvironmentLoader.GetEnvironmentVariable("EVENTHUB_NAME", "")
    End Function
    
#Region "IDisposable Support"
    Protected Overridable Sub Dispose(disposing As Boolean)
        If Not _disposed Then
            If disposing Then
                Try
                    _eventHubClient?.DisposeAsync().AsTask().Wait()
                Catch ex As Exception
                    _logger?.LogError($"Error disposing Event Hub client: {ex.Message}")
                End Try
            End If
            _disposed = True
        End If
    End Sub
    
    Public Sub Dispose() Implements IDisposable.Dispose
        Dispose(True)
        GC.SuppressFinalize(Me)
    End Sub
#End Region

End Class

End Namespace
