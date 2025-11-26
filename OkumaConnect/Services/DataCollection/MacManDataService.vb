Imports System.Threading.Tasks
Imports System.Collections.Generic
Imports OkumaConnect.Models
Imports OkumaConnect.Services.Logging
Imports OkumaConnect.Services.OpcClient
Imports OkumaConnect.Services.EventHub

Namespace Services.DataCollection

    ''' <summary>
    ''' MacMan data service - Collects MacMan screen data based on OPC UA timestamps
    ''' Uses existing machine connections from DataRouter
    ''' </summary>
    Public Class MacManDataService
        Implements IMacManDataService

        Private ReadOnly _logger As ILogger
        Private ReadOnly _opcuaManager As OpcUaManager
        Private _dataRouter As DataRouter

        Public Sub New(logger As ILogger, opcuaManager As OpcUaManager)
            _logger = logger
            _opcuaManager = opcuaManager
            _logger.LogInfo("🖥️ MacManDataService initialized")
        End Sub
        
        ''' <summary>
        ''' Set the DataRouter reference after initialization (to avoid circular dependency)
        ''' </summary>
        Public Sub SetDataRouter(dataRouter As DataRouter)
            _dataRouter = dataRouter
            _logger.LogInfo("🔗 DataRouter reference set for connection sharing")
        End Sub

        Public Async Function GetDataAsync(nodeId As String) As Task(Of ApiDataResult) Implements IMacManDataService.GetDataAsync
            Try
                _logger.LogInfo($"🖥️ MACMAN DATA COLLECTION: Starting for {nodeId}")
                
                ' Extract machine information from nodeId
                ' Pattern: ns=2;s=Okuma.Machines.1 - MU-10000H.Data.MacManData.extract
                Dim machineName = ExtractMachineName(nodeId)
                If String.IsNullOrEmpty(machineName) Then
                    Return New ApiDataResult(False, "String") With {
                        .Success = False,
                        .ErrorMessage = "Could not extract machine name from nodeId"
                    }
                End If
                
                _logger.LogInfo($"🎯 Machine: {machineName}")
                
                ' Step 1: Discover and read all LastProcessed timestamps
                Dim timestamps = Await DiscoverAndReadTimestamps(machineName)
                If timestamps.Count = 0 Then
                    _logger.LogWarning($"⚠️ No MacMan timestamps found for machine: {machineName}")
                    _logger.LogInfo($"🔄 Using fallback date (1970-01-01) for all MacMan data types")
                    
                    ' Use fallback date for all known MacMan data types
                    Dim fallbackDate As DateTime = New DateTime(1970, 1, 1)
                    timestamps = New Dictionary(Of String, DateTime?)() From {
                        {"ALARM_HISTORY_DISPLAY", fallbackDate},
                        {"MACHINING_REPORT_DISPLAY", fallbackDate},
                        {"NC_STATUS_AT_ALARM_DISPLAY", fallbackDate},
                        {"OPERATING_REPORT_DISPLAY", fallbackDate},
                        {"OPERATION_HISTORY_DISPLAY", fallbackDate}
                    }
                End If
                
                _logger.LogInfo($"📊 Found {timestamps.Count} MacMan data types with timestamps")
                
                ' Step 2: Get machine connection for data collection
                Dim machineId = ExtractMachineId(machineName)
                Dim machineIP = Await GetMachineIPAddress(machineName)
                
                If String.IsNullOrEmpty(machineIP) Then
                    _logger.LogError($"❌ No IP address found for machine: {machineName}")
                    Return New ApiDataResult(0, "Integer") With {
                        .Success = False,
                        .ErrorMessage = "Machine IP address not found"
                    }
                End If
                
                ' Step 3: Get machine connection first and do ONE update for all data types
                Dim machineApi = GetMachineConnection(machineName)
                If machineApi Is Nothing Then
                    _logger.LogError($"❌ Machine {machineName} not connected - DataRouter should have established connection first")
                    Return New ApiDataResult(0, "Integer") With {
                        .Success = False,
                        .ErrorMessage = "Machine not connected"
                    }
                End If
                
                ' Do ONE MacMan data update for all data types (more efficient)
                _logger.LogInfo($"🔄 Performing single MacMan data update for all data types...")
                Dim updateSuccess = PerformMacManUpdate(machineApi, machineIP)
                If Not updateSuccess Then
                    _logger.LogError($"❌ MacMan data update failed for machine {machineName}")
                    Return New ApiDataResult(0, "Integer") With {
                        .Success = False,
                        .ErrorMessage = "MacMan data update failed"
                    }
                End If
                
                ' Step 4: Process each collector individually - collect data and update OPC UA per collector
                Dim totalRecords = 0
                Dim collectionResults = New List(Of String)
                
                For Each kvp In timestamps
                    Dim dataType = kvp.Key
                    Dim lastTimestamp = kvp.Value
                    
                    Try
                        _logger.LogInfo($"🔄 Processing {dataType} (LastProcessed: {If(lastTimestamp.HasValue, lastTimestamp.Value.ToString("yyyy-MM-dd HH:mm:ss"), "None")}) [Local Time]")
                        
                        ' Use real MacManDataCollectorFactory to collect data
                        Dim collectionResult = Await CollectRealData(dataType, lastTimestamp, machineApi, machineIP)
                        
                        If collectionResult.Success Then
                            totalRecords += collectionResult.RecordsCollected
                            collectionResults.Add($"{dataType}: {collectionResult.RecordsCollected} records")
                            
                            ' Send to EventHub if we have new records (exactly like QOkumaConnect)
                            If collectionResult.RecordsCollected > 0 AndAlso collectionResult.Records IsNot Nothing Then
                                Try
                                    _logger.LogInfo($"📤 Sending {collectionResult.RecordsCollected} {dataType} records to EventHub...")
                                    
                                    ' Convert to List(Of CollectionResult) as expected by EventHub service
                                    Dim eventHubResults = New List(Of CollectionResult)()
                                    eventHubResults.Add(DirectCast(collectionResult, CollectionResult))
                                    
                                    Dim sentCount = Await EventHubService.Instance.SendCollectionResultsAsync(
                                        machineId, machineIP, machineName, eventHubResults)
                                    
                                    _logger.LogInfo($"✅ Sent {sentCount} events to EventHub for {dataType}")
                                    
                                Catch ex As Exception
                                    _logger.LogError($"❌ Error sending {dataType} to EventHub: {ex.Message}")
                                    ' Continue processing - EventHub failure shouldn't stop data collection
                                End Try
                            End If
                            
                            ' Update OPC UA immediately for this collector if we have new data
                            Dim lastProcessedDate As DateTime? = Nothing
                            If collectionResult.LastProcessedDate IsNot Nothing Then
                                ' Handle both DateTime and DateTime? types
                                If TypeOf collectionResult.LastProcessedDate Is DateTime Then
                                    lastProcessedDate = DirectCast(collectionResult.LastProcessedDate, DateTime)
                                ElseIf TypeOf collectionResult.LastProcessedDate Is DateTime? Then
                                    lastProcessedDate = DirectCast(collectionResult.LastProcessedDate, DateTime?)
                                End If
                            End If
                            
                            If lastProcessedDate.HasValue Then
                                Dim updatedTimestamps = New Dictionary(Of String, DateTime?)()
                                updatedTimestamps(dataType) = lastProcessedDate.Value
                                _logger.LogInfo($"🔄 Updating OPC UA timestamp for {dataType}: {lastProcessedDate.Value:yyyy-MM-dd HH:mm:ss.fff} [Local Time]")
                                Await UpdateLastProcessedTimestamps(machineName, updatedTimestamps)
                            Else
                                _logger.LogInfo($"ℹ️ No new data for {dataType} - no timestamp update needed")
                            End If
                        Else
                            _logger.LogError($"❌ {dataType} collection failed: {collectionResult.ErrorMessage}")
                            collectionResults.Add($"{dataType}: ERROR - {collectionResult.ErrorMessage}")
                        End If
                        
                    Catch ex As Exception
                        _logger.LogError($"❌ Error processing {dataType}: {ex.Message}")
                        collectionResults.Add($"{dataType}: EXCEPTION - {ex.Message}")
                    End Try
                Next
                
                _logger.LogInfo($"🎉 MACMAN DATA COLLECTION COMPLETED: {totalRecords} total records")
                
                Return New ApiDataResult(totalRecords, "Integer") With {
                    .Success = True,
                    .ErrorMessage = String.Join("; ", collectionResults)
                }
                
            Catch ex As Exception
                _logger.LogError($"❌ MacMan Data Service error: {ex.Message}")
                Return New ApiDataResult(False, "Boolean") With {
                    .Success = False,
                    .ErrorMessage = ex.Message
                }
            End Try
        End Function
        
        ''' <summary>
        ''' Discover and read all MacMan LastProcessed timestamps for a machine
        ''' </summary>
        Private Async Function DiscoverAndReadTimestamps(machineName As String) As Task(Of Dictionary(Of String, DateTime?))
            Dim timestamps = New Dictionary(Of String, DateTime?)()
            
            Try
                _logger.LogInfo($"🔍 Discovering MacMan data types for machine: {machineName}")
                
                ' Base path for MacMan data
                Dim macManBasePath = $"ns=2;s=Okuma.Machines.{machineName}.Data.MacManData.LastProcessed"
                
                ' Known MacMan data types from the OPC UA structure
                Dim knownDataTypes = New String() {
                    "ALARM_HISTORY_DISPLAY",
                    "MACHINING_REPORT_DISPLAY", 
                    "NC_STATUS_AT_ALARM_DISPLAY",
                    "OPERATING_REPORT_DISPLAY",
                    "OPERATION_HISTORY_DISPLAY"
                }
                
                _logger.LogInfo($"📋 Checking {knownDataTypes.Length} known MacMan data types...")
                
                For Each dataType In knownDataTypes
                    Try
                        Dim timestampNodeId = $"{macManBasePath}.{dataType}"
                        _logger.LogDebug($"   Reading: {timestampNodeId}")
                        
                        Dim timestampValue = Await _opcuaManager.ReadNodeValue(timestampNodeId)
                        
                        If timestampValue IsNot Nothing Then
                            Dim timestamp As DateTime? = Nothing
                            
                            ' Try to parse timestamp (could be DateTime, Long, String, etc.)
                            If TypeOf timestampValue Is DateTime Then
                                timestamp = DirectCast(timestampValue, DateTime)
                            ElseIf TypeOf timestampValue Is Long Then
                                ' Unix timestamp - convert to local time to match machine timestamps
                                timestamp = DateTimeOffset.FromUnixTimeSeconds(CLng(timestampValue)).ToLocalTime().DateTime
                            ElseIf TypeOf timestampValue Is String Then
                                Dim parsedDate As DateTime
                                If DateTime.TryParse(timestampValue.ToString(), parsedDate) Then
                                    timestamp = parsedDate
                                End If
                            End If
                            
                            ' Use fallback date if timestamp is null or invalid
                            If Not timestamp.HasValue Then
                                timestamp = New DateTime(1970, 1, 1)
                                _logger.LogInfo($"✅ {dataType}: {timestamp.Value.ToString("yyyy-MM-dd HH:mm:ss")} (fallback)")
                            Else
                                _logger.LogInfo($"✅ {dataType}: {timestamp.Value.ToString("yyyy-MM-dd HH:mm:ss")}")
                            End If
                            
                            timestamps(dataType) = timestamp
                        Else
                            ' No timestamp node found - use fallback
                            Dim fallbackTimestamp = New DateTime(1970, 1, 1)
                            timestamps(dataType) = fallbackTimestamp
                            _logger.LogInfo($"✅ {dataType}: {fallbackTimestamp.ToString("yyyy-MM-dd HH:mm:ss")} (fallback - no node)")
                        End If
                        
                    Catch ex As Exception
                        _logger.LogWarning($"⚠️ Could not read timestamp for {dataType}: {ex.Message}")
                        ' Use fallback date on error
                        Dim fallbackTimestamp = New DateTime(1970, 1, 1)
                        timestamps(dataType) = fallbackTimestamp
                        _logger.LogInfo($"✅ {dataType}: {fallbackTimestamp.ToString("yyyy-MM-dd HH:mm:ss")} (fallback - error)")
                    End Try
                Next
                
                _logger.LogInfo($"📊 Successfully read {timestamps.Count} MacMan timestamps")
                
            Catch ex As Exception
                _logger.LogError($"❌ Error discovering MacMan timestamps: {ex.Message}")
            End Try
            
            Return timestamps
        End Function
        
        ''' <summary>
        ''' Extract machine name from MacMan nodeId
        ''' </summary>
        Private Function ExtractMachineName(nodeId As String) As String
            Try
                ' Pattern: ns=2;s=Okuma.Machines.<MachineName>.Data.MacManData.extract
                If nodeId.Contains("Okuma.Machines.") AndAlso nodeId.Contains(".Data.MacManData.") Then
                    Dim startIndex = nodeId.IndexOf("Okuma.Machines.") + "Okuma.Machines.".Length
                    Dim endIndex = nodeId.IndexOf(".Data.MacManData.")
                    
                    If startIndex > 0 AndAlso endIndex > startIndex Then
                        Return nodeId.Substring(startIndex, endIndex - startIndex)
                    End If
                End If
                
                Return Nothing
                
            Catch ex As Exception
                _logger.LogError($"Error extracting machine name from {nodeId}: {ex.Message}")
                Return Nothing
            End Try
        End Function
        
        ''' <summary>
        ''' Extract machine ID from machine name (first part before " - ")
        ''' </summary>
        Private Function ExtractMachineId(machineName As String) As String
            Try
                If machineName.Contains(" - ") Then
                    Return machineName.Split(New String() {" - "}, StringSplitOptions.None)(0)
                End If
                Return machineName
            Catch ex As Exception
                _logger.LogError($"Error extracting machine ID from {machineName}: {ex.Message}")
                Return machineName
            End Try
        End Function
        
        ''' <summary>
        ''' Get machine IP address from OPC UA configuration
        ''' </summary>
        Private Async Function GetMachineIPAddress(machineName As String) As Task(Of String)
            Try
                Dim machineConfigNodeId = $"ns=2;s=Okuma.Machines.{machineName}.MachineConfig.IPAddress"
                Dim ipAddress = Await _opcuaManager.ReadNodeValue(machineConfigNodeId)
                
                If ipAddress IsNot Nothing Then
                    Dim ipStr = ipAddress.ToString().Trim()
                    _logger.LogInfo($"🌐 Using IP address for {machineName}: {ipStr}")
                    Return ipStr
                End If
                
                _logger.LogWarning($"⚠️ No IP address configured for machine {machineName}")
                Return "127.0.0.1" ' Fallback like ProgramManagementService
                
            Catch ex As Exception
                _logger.LogError($"❌ Error reading IP address for machine {machineName}: {ex.Message}")
                Return "127.0.0.1" ' Fallback
            End Try
        End Function
        
        ''' <summary>
        ''' Perform MacMan data update using proper StartUpdate and WaitUpdateEnd sequence
        ''' This is done once before collecting all data types for efficiency
        ''' </summary>
        Private Function PerformMacManUpdate(machineApi As ClassOspApi, machineIP As String) As Boolean
            Try
                _logger.LogInfo($"🔄 [{machineIP}] Starting MacMan data update...")
                
                ' Step 1: Start the update process
                Dim updateResult = machineApi.StartUpdate(0, 0)
                _logger.LogInfo($"🔄 [{machineIP}] StartUpdate result: {updateResult}")
                
                ' Step 2: Wait for update to complete
                Dim waitResult = machineApi.WaitUpdateEnd()
                _logger.LogInfo($"🔄 [{machineIP}] WaitUpdateEnd result: {waitResult}")
                
                ' Check if update was successful (assuming 0 means success)
                If updateResult = 0 AndAlso waitResult = 0 Then
                    _logger.LogInfo($"✅ [{machineIP}] MacMan data update completed successfully")
                    Return True
                Else
                    _logger.LogWarning($"⚠️ [{machineIP}] MacMan data update completed with warnings - StartUpdate: {updateResult}, WaitUpdateEnd: {waitResult}")
                    Return True ' Still proceed with data collection even if there are warnings
                End If
                
            Catch ex As Exception
                _logger.LogError($"❌ [{machineIP}] Error updating MacMan data: {ex.Message}")
                Return False
            End Try
        End Function
        
        ''' <summary>
        ''' Get existing machine connection from DataRouter
        ''' DataRouter is responsible for establishing and managing connections
        ''' </summary>
        Private Function GetMachineConnection(machineName As String) As ClassOspApi
            Try
                If _dataRouter Is Nothing Then
                    _logger.LogError($"❌ DataRouter not set - cannot get machine connection for {machineName}")
                    Return Nothing
                End If
                
                _logger.LogDebug($"🔗 Requesting machine connection for {machineName} from DataRouter")
                
                ' Use DataRouter's public method to get existing connection
                Dim connection = _dataRouter.GetMachineConnection(machineName)
                
                If connection IsNot Nothing Then
                    _logger.LogDebug($"✅ Got machine connection for {machineName} from DataRouter")
                Else
                    _logger.LogWarning($"⚠️ No connection available for {machineName} - DataRouter should establish connection first")
                End If
                
                Return connection
                
            Catch ex As Exception
                _logger.LogError($"❌ Error getting machine connection for {machineName}: {ex.Message}")
                Return Nothing
            End Try
        End Function
        
        ''' <summary>
        ''' Collect real data using MacManDataCollectorFactory (like MacManScreenCollector)
        ''' </summary>
        Private Async Function CollectRealData(dataType As String, lastTimestamp As DateTime?, machineApi As ClassOspApi, machineIP As String) As Task(Of Object)
            Try
                ' Create collector using factory
                Dim collector = MacManDataCollectorFactory.CreateCollector(dataType, machineApi, machineIP, _logger)
                
                If collector Is Nothing Then
                    _logger.LogError($"❌ No collector found for {dataType}")
                    Return New With {
                        .Success = False,
                        .RecordsCollected = 0,
                        .LastProcessedDate = lastTimestamp,
                        .ErrorMessage = $"No collector found for {dataType}",
                        .Records = New List(Of Object)()
                    }
                End If
                
                ' Collect data with timestamp filtering and skip central update
                Dim batchSize = 1000
                Dim collectionResult = Await Task.Run(Function() collector.CollectDataWithoutUpdate(lastTimestamp, batchSize, True))
                
                _logger.LogInfo($"✅ {dataType}: {collectionResult.RecordsCollected} records collected")
                
                Return collectionResult
                
            Catch ex As Exception
                _logger.LogError($"❌ Error in CollectRealData for {dataType}: {ex.Message}")
                Return New With {
                    .Success = False,
                    .RecordsCollected = 0,
                    .LastProcessedDate = lastTimestamp,
                    .ErrorMessage = ex.Message,
                    .Records = New List(Of Object)()
                }
            End Try
        End Function
        
        ''' <summary>
        ''' Update LastProcessed timestamps in OPC UA (similar to how MacManScreenCollector updates timestamps)
        ''' Uses multiple datatype attempts like the real implementation
        ''' </summary>
        Private Async Function UpdateLastProcessedTimestamps(machineName As String, updatedTimestamps As Dictionary(Of String, DateTime?)) As Task
            Try
                _logger.LogInfo($"🔄 Updating LastProcessed timestamps for machine: {machineName}")
                
                For Each kvp In updatedTimestamps
                    Dim dataType = kvp.Key
                    Dim newTimestamp = kvp.Value
                    
                    If newTimestamp.HasValue Then
                        Try
                            Dim timestampNodeId = $"ns=2;s=Okuma.Machines.{machineName}.Data.MacManData.LastProcessed.{dataType}"
                            Dim timestampValue = newTimestamp.Value
                            
                            _logger.LogInfo($"🔄 Attempting to write timestamp for {dataType}: {timestampValue:yyyy-MM-dd HH:mm:ss.fff}")
                            
                            ' Try multiple data types like the real implementation does
                            Dim writeSuccess = False
                            
                            ' Try 1: String format first (most likely to succeed based on logs)
                            ' Use local time format to match machine timestamps (no Z suffix)
                            Dim timestampString = timestampValue.ToString("yyyy-MM-ddTHH:mm:ss.fff")
                            If Await _opcuaManager.WriteNodeValue(timestampNodeId, timestampString) Then
                                writeSuccess = True
                                _logger.LogInfo($"✅ Updated {timestampNodeId} = {timestampString} (String)")
                            Else
                                _logger.LogDebug($"   String write failed, trying DateTime format...")
                                
                                ' Try 2: DateTime format as fallback
                                If Await _opcuaManager.WriteNodeValue(timestampNodeId, timestampValue) Then
                                    writeSuccess = True
                                    _logger.LogInfo($"✅ Updated {timestampNodeId} = {timestampValue:yyyy-MM-dd HH:mm:ss.fff} (DateTime)")
                                Else
                                    _logger.LogDebug($"   DateTime write failed, trying Unix timestamp...")
                                    
                                    ' Try 3: Unix timestamp (Long) - use local time
                                    Dim unixTimestamp = New DateTimeOffset(timestampValue, TimeZoneInfo.Local.GetUtcOffset(timestampValue)).ToUnixTimeSeconds()
                                    If Await _opcuaManager.WriteNodeValue(timestampNodeId, unixTimestamp) Then
                                        writeSuccess = True
                                        _logger.LogInfo($"✅ Updated {timestampNodeId} = {unixTimestamp} (Unix Long)")
                                    Else
                                        _logger.LogDebug($"   Unix Long write failed, trying Integer...")
                                        
                                        ' Try 4: Unix timestamp (Integer)
                                        If Await _opcuaManager.WriteNodeValue(timestampNodeId, CInt(unixTimestamp)) Then
                                            writeSuccess = True
                                            _logger.LogInfo($"✅ Updated {timestampNodeId} = {CInt(unixTimestamp)} (Unix Integer)")
                                        End If
                                    End If
                                End If
                            End If
                            
                            If Not writeSuccess Then
                                _logger.LogError($"❌ Failed to update timestamp for {dataType} - all data types failed")
                            End If
                            
                        Catch ex As Exception
                            _logger.LogError($"❌ Error updating timestamp for {dataType}: {ex.Message}")
                        End Try
                    Else
                        _logger.LogDebug($"   Skipping {dataType}: No new timestamp to update")
                    End If
                Next
                
                _logger.LogInfo($"✅ LastProcessed timestamp updates completed")
                
            Catch ex As Exception
                _logger.LogError($"❌ Error updating LastProcessed timestamps: {ex.Message}")
            End Try
        End Function

    End Class

End Namespace
