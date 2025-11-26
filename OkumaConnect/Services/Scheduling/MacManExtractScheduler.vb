Imports System
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Collections.Generic
Imports OkumaConnect.Services.Logging
Imports OkumaConnect.Services.Configuration
Imports OkumaConnect.Services.OpcClient

Namespace Services.Scheduling

''' <summary>
''' Service voor het automatisch triggeren van MacMan data extractie
''' Zet periodiek alle MacManData.extract nodes naar True
''' </summary>
Public Class MacManExtractScheduler
    Implements IDisposable
    
    Private Shared _instance As MacManExtractScheduler
    Private ReadOnly _logger As ILogger
    Private ReadOnly _opcUaManager As OpcUaManager
    Private _timer As Timer
    Private _isRunning As Boolean = False
    Private _disposed As Boolean = False
    Private ReadOnly _intervalMinutes As Integer
    
    ''' <summary>
    ''' Singleton pattern
    ''' </summary>
    Public Shared ReadOnly Property Instance As MacManExtractScheduler
        Get
            If _instance Is Nothing Then
                Throw New InvalidOperationException("MacManExtractScheduler not initialized. Call Initialize() first.")
            End If
            Return _instance
        End Get
    End Property
    
    ''' <summary>
    ''' Initialize the singleton instance
    ''' </summary>
    Public Shared Sub Initialize(logger As ILogger, opcUaManager As OpcUaManager)
        If _instance Is Nothing Then
            _instance = New MacManExtractScheduler(logger, opcUaManager)
        End If
    End Sub
    
    Private Sub New(logger As ILogger, opcUaManager As OpcUaManager)
        _logger = logger
        _opcUaManager = opcUaManager
        
        ' Get interval from environment variable (in minutes)
        _intervalMinutes = GetExtractIntervalMinutes()
        
        If _intervalMinutes > 0 Then
            _logger.LogInfo($"🕐 MacManExtractScheduler initialized with {_intervalMinutes} minute interval")
            StartScheduler()
        Else
            _logger.LogInfo($"🚫 MacManExtractScheduler disabled (interval = 0)")
        End If
    End Sub
    
    ''' <summary>
    ''' Start the scheduler
    ''' </summary>
    Private Sub StartScheduler()
        If _intervalMinutes <= 0 Then
            _logger.LogInfo("MacManExtractScheduler not started - interval is 0")
            Return
        End If
        
        Try
            ' Convert minutes to milliseconds
            Dim intervalMs As Integer = _intervalMinutes * 60 * 1000
            
            ' Create timer that starts immediately and repeats every interval
            _timer = New Timer(AddressOf TimerCallback, Nothing, 0, intervalMs)
            _isRunning = True
            
            _logger.LogInfo($"✅ MacManExtractScheduler started - will trigger every {_intervalMinutes} minutes")
            
        Catch ex As Exception
            _logger.LogError($"❌ Failed to start MacManExtractScheduler: {ex.Message}")
        End Try
    End Sub
    
    ''' <summary>
    ''' Timer callback - triggered every interval
    ''' </summary>
    Private Sub TimerCallback(state As Object)
        If _disposed Then Return
        
        ' Fire and forget async operation
        Task.Run(Async Function()
                     Try
                         _logger.LogInfo($"🔄 MacManExtractScheduler: Starting extract trigger cycle...")
                         
                         Await TriggerAllMachineExtracts()
                         
                         _logger.LogInfo($"✅ MacManExtractScheduler: Extract trigger cycle completed")
                         
                     Catch ex As Exception
                         _logger.LogError($"❌ MacManExtractScheduler error: {ex.Message}")
                     End Try
                 End Function)
    End Sub
    
    ''' <summary>
    ''' Trigger extract for all machines
    ''' </summary>
    Private Async Function TriggerAllMachineExtracts() As Task
        Try
            ' Get all machine nodes from OPC UA
            Dim machineNodes = Await GetAllMachineNodes()
            
            If machineNodes.Count = 0 Then
                _logger.LogWarning("⚠️ No machine nodes found for extract triggering")
                Return
            End If
            
            _logger.LogInfo($"📡 Found {machineNodes.Count} machine nodes to trigger")
            
            ' Trigger extract for each machine
            Dim tasks As New List(Of Task)()
            For Each machineNode In machineNodes
                tasks.Add(TriggerMachineExtract(machineNode))
            Next
            
            ' Wait for all triggers to complete
            Await Task.WhenAll(tasks)
            
        Catch ex As Exception
            _logger.LogError($"❌ Error triggering machine extracts: {ex.Message}")
        End Try
    End Function
    
    ''' <summary>
    ''' Get all machine nodes from OPC UA by browsing the server
    ''' </summary>
    Private Async Function GetAllMachineNodes() As Task(Of List(Of String))
        Dim machineNodes As New List(Of String)()
        
        Try
            ' Browse for all machine nodes under ns=2;s=Okuma.Machines
            Dim rootNodeId = "ns=2;s=Okuma.Machines"
            
            _logger.LogInfo($"🔍 Browsing for machines under: {rootNodeId}")
            
            ' Get all machine folders from OPC UA server
            Dim machineNodeIds = _opcUaManager.BrowseNodes(rootNodeId)
            
            If machineNodeIds Is Nothing OrElse machineNodeIds.Count = 0 Then
                _logger.LogWarning($"⚠️ No machine nodes found under {rootNodeId}")
                Return machineNodes
            End If
            
            _logger.LogInfo($"📡 Found {machineNodeIds.Count} potential machine nodes")
            
            ' For each machine node, look for the MacManData.extract node
            For Each machineNodeId In machineNodeIds
                Try
                    ' Skip system nodes and other non-machine nodes
                    If IsSystemOrNonMachineNode(machineNodeId) Then
                        _logger.LogDebug($"⏭️ Skipping system/non-machine node: {machineNodeId}")
                        Continue For
                    End If
                    
                    ' Try to construct the extract node path
                    ' Expected format: ns=2;s=Okuma.Machines.{MachineName}.Data.MacManData.extract
                    Dim extractNodeId = $"{machineNodeId}.Data.MacManData.extract"
                    
                    ' Verify the extract node exists by trying to read it
                    ' Use a more robust validation approach
                    If Await IsValidExtractNode(extractNodeId) Then
                        machineNodes.Add(extractNodeId)
                        _logger.LogDebug($"✅ Found valid extract node: {extractNodeId}")
                    Else
                        _logger.LogDebug($"⚠️ Invalid or non-existent extract node: {extractNodeId}")
                    End If
                    
                Catch ex As Exception
                    ' This machine doesn't have a MacManData.extract node, skip it
                    _logger.LogDebug($"⚠️ Machine {machineNodeId} has no MacManData.extract node: {ex.Message}")
                End Try
            Next
            
            _logger.LogInfo($"📋 Found {machineNodes.Count} machines with MacManData.extract nodes")
            
        Catch ex As Exception
            _logger.LogError($"❌ Error getting machine nodes: {ex.Message}")
        End Try
        
        Return machineNodes
    End Function
    
    ''' <summary>
    ''' Check if a node is a system node or other non-machine node that should be skipped
    ''' </summary>
    Private Function IsSystemOrNonMachineNode(nodeId As String) As Boolean
        If String.IsNullOrEmpty(nodeId) Then Return True
        
        ' Convert to lowercase for case-insensitive comparison
        Dim lowerNodeId = nodeId.ToLower()
        
        ' Skip system nodes and other known non-machine nodes
        Return lowerNodeId.Contains("_system") OrElse
               lowerNodeId.Contains("system") OrElse
               lowerNodeId.Contains("_config") OrElse
               lowerNodeId.Contains("config") OrElse
               lowerNodeId.Contains("_global") OrElse
               lowerNodeId.Contains("global") OrElse
               lowerNodeId.Contains("_server") OrElse
               lowerNodeId.Contains("server")
    End Function
    
    ''' <summary>
    ''' Validate if an extract node exists and is writable
    ''' </summary>
    Private Async Function IsValidExtractNode(extractNodeId As String) As Task(Of Boolean)
        Try
            ' First try to read the node to see if it exists
            Dim testValue = Await _opcUaManager.ReadNodeValue(extractNodeId)
            
            ' If we get here without exception, the node exists
            ' Now check if it's a boolean node (extract nodes should be boolean)
            If testValue IsNot Nothing Then
                ' Check if it's a boolean or can be converted to boolean
                If TypeOf testValue Is Boolean OrElse 
                   (TypeOf testValue Is String AndAlso (testValue.ToString().ToLower() = "true" OrElse testValue.ToString().ToLower() = "false")) OrElse
                   (IsNumeric(testValue) AndAlso (Convert.ToInt32(testValue) = 0 OrElse Convert.ToInt32(testValue) = 1)) Then
                    Return True
                End If
            Else
                ' Node exists but value is null - this is still valid for extract nodes
                Return True
            End If
            
            Return False
            
        Catch ex As Exception
            ' If we can't read the node, it doesn't exist or is not accessible
            _logger.LogDebug($"Extract node validation failed for {extractNodeId}: {ex.Message}")
            Return False
        End Try
    End Function
    
    ''' <summary>
    ''' Trigger extract for a specific machine
    ''' </summary>
    Private Async Function TriggerMachineExtract(extractNodeId As String) As Task
        Try
            _logger.LogDebug($"🔄 Triggering extract for: {extractNodeId}")
            
            ' Set the extract node to True using WriteNodeValue method
            Dim success = Await _opcUaManager.WriteNodeValue(extractNodeId, True)
            
            If success Then
                _logger.LogDebug($"✅ Successfully triggered extract for: {extractNodeId}")
            Else
                _logger.LogWarning($"⚠️ Failed to trigger extract for: {extractNodeId}")
            End If
            
        Catch ex As Exception
            _logger.LogError($"❌ Error triggering extract for {extractNodeId}: {ex.Message}")
        End Try
    End Function
    
    
    ''' <summary>
    ''' Get extract interval from environment variable
    ''' </summary>
    Private Function GetExtractIntervalMinutes() As Integer
        Try
            Dim intervalStr = EnvironmentLoader.GetEnvironmentVariable("MACMAN_EXTRACT_INTERVAL_MINUTES", "1")
            
            Dim interval As Integer
            If Integer.TryParse(intervalStr, interval) Then
                Return Math.Max(0, interval) ' Ensure non-negative
            Else
                _logger.LogWarning($"⚠️ Invalid MACMAN_EXTRACT_INTERVAL_MINUTES value: '{intervalStr}', using default: 1")
                Return 1
            End If
            
        Catch ex As Exception
            _logger.LogError($"❌ Error reading extract interval: {ex.Message}, using default: 1")
            Return 1
        End Try
    End Function
    
    ''' <summary>
    ''' Stop the scheduler
    ''' </summary>
    Public Sub StopScheduler()
        Try
            If _timer IsNot Nothing Then
                _timer.Dispose()
                _timer = Nothing
            End If
            
            _isRunning = False
            _logger.LogInfo("🛑 MacManExtractScheduler stopped")
            
        Catch ex As Exception
            _logger.LogError($"❌ Error stopping MacManExtractScheduler: {ex.Message}")
        End Try
    End Sub
    
    ''' <summary>
    ''' Check if scheduler is running
    ''' </summary>
    Public ReadOnly Property IsRunning As Boolean
        Get
            Return _isRunning AndAlso _timer IsNot Nothing
        End Get
    End Property
    
    ''' <summary>
    ''' Get current interval in minutes
    ''' </summary>
    Public ReadOnly Property IntervalMinutes As Integer
        Get
            Return _intervalMinutes
        End Get
    End Property
    
#Region "IDisposable Support"
    Protected Overridable Sub Dispose(disposing As Boolean)
        If Not _disposed Then
            If disposing Then
                StopScheduler()
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
