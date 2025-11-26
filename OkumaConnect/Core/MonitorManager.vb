Imports System
Imports System.Threading.Tasks
Imports OkumaConnect.Services.Configuration
Imports OkumaConnect.Services.OpcClient
Imports OkumaConnect.Services.Logging
Imports OkumaConnect.Services.DataCollection
Imports OkumaConnect.Services.EventHub
Imports OkumaConnect.Services.Scheduling
Imports OkumaConnect.Models

Namespace Core

''' <summary>
''' Main monitor manager that coordinates configuration monitoring and OPC UA operations
''' </summary>
Public Class MonitorManager
    Implements IDisposable
    
    Private ReadOnly _configurationManager As ConfigurationManager
    Private ReadOnly _logger As ILogger
    Private _opcuaManager As OpcUaManager
    Private _dataRouter As IDataRouter
    Private _isRunning As Boolean = False
    
    Public Sub New(configurationManager As ConfigurationManager, logger As ILogger)
        _configurationManager = configurationManager
        _logger = logger
        
        ' Subscribe to configuration changes
        AddHandler _configurationManager.ConfigurationChanged, AddressOf OnConfigurationChanged
    End Sub
    
    ''' <summary>
    ''' Start the monitoring process
    ''' </summary>
    Public Async Function StartAsync() As Task
        Try
            _logger.LogInfo("🚀 Starting OkumaConnect monitor...")
            _isRunning = True
            
            ' Step 1: Initialize OPC UA Manager
            InitializeOpcUaManager()
            
            ' Step 2: Start monitoring loop with connection attempts
            Await StartMonitoringLoop()
            
        Catch ex As Exception
            _logger.LogError($"❌ Failed to start monitor: {ex.Message}")
            ' Don't throw - let the monitoring loop handle reconnection
            _logger.LogInfo("🔄 Will continue attempting to connect...")
            If Not _isRunning Then
                _isRunning = True
                ' Move async call outside catch block
                Task.Run(AddressOf StartMonitoringLoop)
            End If
        End Try
    End Function
    
    ''' <summary>
    ''' Stop the monitoring process
    ''' </summary>
    Public Sub [Stop]()
        _isRunning = False
        _opcuaManager?.Disconnect()
        _logger.LogInfo("🛑 Monitor stopped")
    End Sub
    
    ''' <summary>
    ''' Initialize the OPC UA client
    ''' </summary>
    Private Sub InitializeOpcUaManager()
        Try
            Dim opcuaSettings = _configurationManager.GetOpcUaSettings()
            _opcuaManager = New OpcUaManager(opcuaSettings, _logger)
            
            ' Subscribe to OPC UA events
            AddHandler _opcuaManager.ConnectionStatusChanged, AddressOf OnOpcUaConnectionChanged
            AddHandler _opcuaManager.DataReceived, AddressOf OnOpcUaDataReceived
            AddHandler _opcuaManager.ErrorOccurred, AddressOf OnOpcUaError
            
            ' Initialize data collection services
            InitializeDataServices()
            
            _logger.LogInfo("✅ OPC UA Manager initialized")
            
        Catch ex As Exception
            _logger.LogError($"Failed to initialize OPC UA Client: {ex.Message}")
            Throw
        End Try
    End Sub
    
    ''' <summary>
    ''' Initialize data collection services
    ''' </summary>
    Private Sub InitializeDataServices()
        Try
            _logger.LogInfo("🔧 Initializing data collection services...")
            
            ' Initialize EventHub service first
            EventHubService.Initialize(_logger)
            
            ' Create service instances
            Dim generalApiService As IGeneralApiService = New GeneralApiService(_logger)
            Dim macManDataService As MacManDataService = New MacManDataService(_logger, _opcuaManager)
            Dim programManagementService As IProgramManagementService = New ProgramManagementService(_logger, _opcuaManager)
            
            ' Create data router
            _dataRouter = New DataRouter(_logger, _opcuaManager, _configurationManager, generalApiService, macManDataService, programManagementService)
            
            ' Set DataRouter reference in MacManDataService (to avoid circular dependency)
            macManDataService.SetDataRouter(_dataRouter)
            
            _logger.LogInfo("✅ Data collection services initialized")
            
        Catch ex As Exception
            _logger.LogError($"Failed to initialize data services: {ex.Message}")
            Throw
        End Try
    End Sub
    
    ''' <summary>
    ''' Try to connect to the OPC UA server without throwing exceptions
    ''' </summary>
    Private Async Function TryConnectToOpcUaServer() As Task(Of Boolean)
        Try
            Dim connected = Await _opcuaManager.ConnectAsync()
            If connected Then
                _logger.LogInfo("✅ Successfully connected to OPC UA server")
                Return True
            Else
                _logger.LogWarning("❌ Failed to connect to OPC UA server")
                Return False
            End If
            
        Catch ex As Exception
            _logger.LogError($"❌ Exception connecting to OPC UA server: {ex.Message}")
            Return False
        End Try
    End Function
    
    ''' <summary>
    ''' Initialize MacMan Extract Scheduler
    ''' </summary>
    Private Sub InitializeMacManExtractScheduler()
        Try
            _logger.LogInfo("⏰ Initializing MacMan Extract Scheduler...")
            
            ' Initialize the scheduler with logger and OPC UA manager
            Services.Scheduling.MacManExtractScheduler.Initialize(_logger, _opcuaManager)
            
            _logger.LogInfo("✅ MacMan Extract Scheduler initialized")
            
        Catch ex As Exception
            _logger.LogError($"❌ Failed to initialize MacMan Extract Scheduler: {ex.Message}")
            ' Don't throw - scheduler is optional, main functionality should continue
        End Try
    End Sub
    
    ''' <summary>
    ''' Subscribe to configuration-related nodes
    ''' </summary>
    Private Sub SubscribeToConfigurationNodes()
        Try
            _logger.LogInfo("📡 Setting up configuration node subscriptions...")
            
            ' First, discover and save all MachineConfig nodes
            _logger.LogInfo("📋 Step 1: Discovering and saving MachineConfig nodes...")
            Dim configCount = _opcuaManager.DiscoverAndSaveMachineConfigs()
            _logger.LogInfo($"✅ Processed {configCount} MachineConfig nodes")
            
            ' Then, discover and subscribe to Data Extract nodes
            _logger.LogInfo("🎯 Step 2: Discovering and subscribing to Data Extract nodes...")
            Dim extractCount = _opcuaManager.DiscoverAndSubscribeToDataExtractNodes()
            _logger.LogInfo($"✅ Subscribed to {extractCount} Data Extract nodes")
            
            ' Discover and subscribe to ProgramManagement.Ctrl nodes
            _logger.LogInfo("🎛️ Step 3: Discovering and subscribing to ProgramManagement.Ctrl nodes...")
            Dim ctrlCount = _opcuaManager.DiscoverAndSubscribeToProgramManagementCtrlNodes()
            _logger.LogInfo($"✅ Subscribed to {ctrlCount} ProgramManagement.Ctrl nodes")
            
            ' Continue with existing API configuration subscriptions
            _logger.LogInfo("⚙️ Step 4: Setting up API configuration subscriptions...")
            Dim apiConfig = _configurationManager.GetApiConfiguration()
            Dim subscriptionCount = 0
            
            ' Subscribe to enabled configuration items
            If apiConfig?.Configurations IsNot Nothing Then
                For Each machineType In apiConfig.Configurations.Values
                    Dim seriesConfigs = machineType.GetSeriesConfigurations()
                    If seriesConfigs IsNot Nothing Then
                        For Each series In seriesConfigs.Values
                            ' Subscribe to enabled general items
                            If series.General IsNot Nothing Then
                                For Each item In series.General
                                    If item.Enabled Then
                                        Dim nodeId = CreateNodeIdFromConfigItem(item)
                                        If _opcuaManager.SubscribeToNode(nodeId) Then
                                            subscriptionCount += 1
                                        End If
                                    End If
                                Next
                            End If
                            
                            ' Subscribe to enabled custom items
                            If series.Custom IsNot Nothing Then
                                For Each item In series.Custom
                                    If item.Enabled Then
                                        Dim nodeId = CreateNodeIdFromConfigItem(item)
                                        If _opcuaManager.SubscribeToNode(nodeId) Then
                                            subscriptionCount += 1
                                        End If
                                    End If
                                Next
                            End If
                        Next
                    End If
                Next
            End If
            
            _logger.LogInfo($"✅ Subscribed to {subscriptionCount} API configuration nodes")
            _logger.LogInfo($"🎉 Total subscriptions: {extractCount + ctrlCount + subscriptionCount} nodes (Data Extract: {extractCount}, ProgramManagement.Ctrl: {ctrlCount}, API Config: {subscriptionCount})")
            
        Catch ex As Exception
            _logger.LogError($"Failed to setup configuration subscriptions: {ex.Message}")
            Throw
        End Try
    End Sub
    
    ''' <summary>
    ''' Start the main monitoring loop with automatic connection attempts
    ''' </summary>
    Private Async Function StartMonitoringLoop() As Task
        _logger.LogInfo("🔄 Starting monitoring loop with automatic reconnection...")
        _logger.LogInfo("Press Ctrl+C to stop monitoring")
        
        ' Setup console cancel handler
        AddHandler Console.CancelKeyPress, Sub(sender, e)
            e.Cancel = True
            _logger.LogInfo("🛑 Shutdown requested...")
            [Stop]()
        End Sub
        
        Dim isInitialized As Boolean = False
        
        ' Main monitoring loop
        While _isRunning
            Try
                ' Check if we need to connect/reconnect
                If Not _opcuaManager.IsConnected Then
                    _logger.LogInfo("🔄 Attempting to connect to OPC UA server...")
                    
                    Dim connected = Await TryConnectToOpcUaServer()
                    
                    If connected AndAlso Not isInitialized Then
                        _logger.LogInfo("✅ Connected! Initializing services...")
                        
                        ' Initialize everything after successful connection
                        Try
                            SubscribeToConfigurationNodes()
                            InitializeMacManExtractScheduler()
                            
                            ' Initialize machine connections
                            Await CType(_dataRouter, DataRouter).InitializeMachineConnections()
                            
                            isInitialized = True
                            _logger.LogInfo("🎉 All services initialized successfully!")
                            
                        Catch initEx As Exception
                            _logger.LogError($"❌ Error during initialization: {initEx.Message}")
                            isInitialized = False
                        End Try
                        
                    ElseIf connected Then
                        _logger.LogInfo("✅ Reconnected to OPC UA server")
                        
                        ' CRITICAL: Re-setup all subscriptions after reconnect!
                        Try
                            _logger.LogInfo("🔄 Re-establishing subscriptions after reconnect...")
                            SubscribeToConfigurationNodes()
                            
                            ' Re-initialize machine connections if needed
                            Await CType(_dataRouter, DataRouter).InitializeMachineConnections()
                            
                            isInitialized = True
                            _logger.LogInfo("🎉 Subscriptions re-established successfully!")
                            
                        Catch reconnectEx As Exception
                            _logger.LogError($"❌ Error re-establishing subscriptions: {reconnectEx.Message}")
                            isInitialized = False
                        End Try
                    Else
                        _logger.LogWarning("❌ Connection failed, will retry in 5 seconds...")
                    End If
                Else
                    ' Connection is good, just monitor
                    If Not isInitialized Then
                        _logger.LogInfo("🔄 Connection exists but services not initialized, retrying...")
                        isInitialized = False
                    End If
                End If
                
            Catch ex As Exception
                _logger.LogError($"❌ Error in monitoring loop: {ex.Message}")
                _logger.LogInfo("🔄 Will retry in 5 seconds...")
                
                ' Reset initialization flag on error
                isInitialized = False
            End Try
            
            ' Wait 5 seconds before next check/retry
            If _isRunning Then
                Await Task.Delay(5000)
            End If
        End While
        
        _logger.LogInfo("🏁 Monitoring loop ended")
    End Function
    
    ''' <summary>
    ''' Create a node ID from a configuration item
    ''' </summary>
    Private Function CreateNodeIdFromConfigItem(item As ApiConfigurationItem) As String
        ' This is a simplified node ID creation - you may need to adjust based on your OPC UA server structure
        Return $"ns={item.SubsystemIndex};i={item.MajorIndex}"
    End Function
    
    ''' <summary>
    ''' Handle configuration changes
    ''' </summary>
    Private Sub OnConfigurationChanged()
        Try
            _logger.LogInfo("📝 Configuration changed, updating subscriptions...")
            
            ' Resubscribe to configuration nodes with new settings
            If _opcuaManager IsNot Nothing AndAlso _opcuaManager.IsConnected Then
                SubscribeToConfigurationNodes()
            End If
            
        Catch ex As Exception
            _logger.LogError($"Error handling configuration change: {ex.Message}")
        End Try
    End Sub
    
    ''' <summary>
    ''' Handle OPC UA connection status changes
    ''' </summary>
    Private Sub OnOpcUaConnectionChanged(connected As Boolean)
        If connected Then
            _logger.LogInfo("✅ OPC UA connection established")
        Else
            _logger.LogWarning("❌ OPC UA connection lost")
        End If
    End Sub
    
    ''' <summary>
    ''' Handle OPC UA data received
    ''' </summary>
    Private Sub OnOpcUaDataReceived(nodeId As String, value As Object, timestamp As DateTime)
        _logger.LogInfo($"📊 Data: {nodeId} = {value} [{timestamp:HH:mm:ss.fff}]")
        
        ' Check if this is an extract node trigger
        If nodeId.Contains(".extract") AndAlso TypeOf value Is Boolean AndAlso CBool(value) = True Then
            _logger.LogInfo($"🚀 Extract trigger detected for: {nodeId}")
            
            ' Trigger async data collection (fire and forget)
            Task.Run(Async Function()
                Try
                    Await _dataRouter.ProcessDataRequest(nodeId, "extract")
                Catch ex As Exception
                    _logger.LogError($"❌ Error processing data request: {ex.Message}")
                End Try
            End Function)
        ElseIf nodeId.Contains(".ProgramManagement.Ctrl") AndAlso TypeOf value Is Boolean Then
            Dim ctrlValue = CBool(value)
            
            If ctrlValue = True Then
                _logger.LogInfo($"🟢 ProgramManagement.Ctrl = TRUE detected for: {nodeId}")
                
                ' Trigger async program management (fire and forget)
                Task.Run(Async Function()
                    Try
                        Await _dataRouter.ProcessDataRequest(nodeId, "ctrl")
                    Catch ex As Exception
                        _logger.LogError($"❌ Error processing program management request: {ex.Message}")
                    End Try
                End Function)
                
            ElseIf ctrlValue = False Then
                _logger.LogInfo($"🔴 ProgramManagement.Ctrl = FALSE detected for: {nodeId}")
                
                ' Set Stat to False when Ctrl becomes False
                Task.Run(Async Function()
                    Try
                        Await HandleProgramManagementStop(nodeId)
                    Catch ex As Exception
                        _logger.LogError($"❌ Error handling program management stop: {ex.Message}")
                    End Try
                End Function)
            End If
        End If
    End Sub
    
    ''' <summary>
    ''' Handle ProgramManagement.Ctrl = False - Set Stat to False
    ''' </summary>
    Private Async Function HandleProgramManagementStop(ctrlNodeId As String) As Task
        Try
            _logger.LogInfo($"🛑 PROGRAM MANAGEMENT STOP: Processing Ctrl = False for: {ctrlNodeId}")
            
            ' Extract machine name from Ctrl node ID
            ' Pattern: ns=2;s=Okuma.Machines.1 - MU-10000H.ProgramManagement.Ctrl
            Dim machineName = ExtractMachineNameFromCtrlNode(ctrlNodeId)
            If String.IsNullOrEmpty(machineName) Then
                _logger.LogError($"❌ Could not extract machine name from: {ctrlNodeId}")
                Return
            End If
            
            ' Construct Stat node ID
            Dim statNodeId = $"ns=2;s=Okuma.Machines.{machineName}.ProgramManagement.Stat"
            
            _logger.LogInfo($"🔄 Setting ProgramManagement.Stat = FALSE for machine: {machineName}")
            _logger.LogDebug($"   Stat Node ID: {statNodeId}")
            
            ' Write False to Stat node
            If Await _opcuaManager.WriteNodeValue(statNodeId, False) Then
                _logger.LogInfo($"✅ Successfully set {statNodeId} = FALSE")
            Else
                _logger.LogError($"❌ Failed to write FALSE to {statNodeId}")
            End If
            
        Catch ex As Exception
            _logger.LogError($"❌ Error in HandleProgramManagementStop: {ex.Message}")
        End Try
    End Function
    
    ''' <summary>
    ''' Extract machine name from ProgramManagement.Ctrl node ID
    ''' </summary>
    Private Function ExtractMachineNameFromCtrlNode(ctrlNodeId As String) As String
        Try
            ' Pattern: ns=2;s=Okuma.Machines.<MachineName>.ProgramManagement.Ctrl
            If ctrlNodeId.Contains("Okuma.Machines.") AndAlso ctrlNodeId.Contains(".ProgramManagement.Ctrl") Then
                Dim startIndex = ctrlNodeId.IndexOf("Okuma.Machines.") + "Okuma.Machines.".Length
                Dim endIndex = ctrlNodeId.IndexOf(".ProgramManagement.Ctrl")
                
                If startIndex > 0 AndAlso endIndex > startIndex Then
                    Return ctrlNodeId.Substring(startIndex, endIndex - startIndex)
                End If
            End If
            
            Return Nothing
            
        Catch ex As Exception
            _logger.LogError($"Error extracting machine name from {ctrlNodeId}: {ex.Message}")
            Return Nothing
        End Try
    End Function
    
    ''' <summary>
    ''' Handle OPC UA errors
    ''' </summary>
    Private Sub OnOpcUaError(message As String, exception As Exception)
        _logger.LogError($"OPC UA Error: {message}")
        If exception IsNot Nothing Then
            _logger.LogError($"Exception details: {exception}")
        End If
    End Sub
    
    Public Sub Dispose() Implements IDisposable.Dispose
        [Stop]()
        
        ' Stop MacMan Extract Scheduler
        Try
            If Services.Scheduling.MacManExtractScheduler.Instance IsNot Nothing Then
                Services.Scheduling.MacManExtractScheduler.Instance.Dispose()
            End If
        Catch ex As Exception
            _logger?.LogError($"Error disposing MacMan Extract Scheduler: {ex.Message}")
        End Try
        
        _opcuaManager?.Dispose()
    End Sub
    
End Class

End Namespace
