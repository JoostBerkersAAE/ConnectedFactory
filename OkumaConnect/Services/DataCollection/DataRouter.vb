Imports System
Imports System.Threading.Tasks
Imports OkumaConnect.Services.Logging
Imports OkumaConnect.Services.OpcClient
Imports OkumaConnect.Services.Configuration
Imports OkumaConnect.Models

Namespace Services.DataCollection

    ''' <summary>
    ''' Main data router that handles different types of data collection requests
    ''' </summary>
    Public Class DataRouter
        Implements IDataRouter

        Private ReadOnly _logger As ILogger
        Private ReadOnly _opcuaManager As OpcUaManager
        Private ReadOnly _configurationManager As ConfigurationManager
        Private ReadOnly _generalApiService As IGeneralApiService
        Private ReadOnly _macManDataService As IMacManDataService
        Private ReadOnly _programManagementService As IProgramManagementService
        
        ' Machine connection management
        Private ReadOnly _machineConnections As New Dictionary(Of String, ClassOspApi)()
        Private ReadOnly _connectionLock As New Object()

        Public Sub New(logger As ILogger, opcuaManager As OpcUaManager, 
                      configurationManager As ConfigurationManager,
                      generalApiService As IGeneralApiService,
                      macManDataService As IMacManDataService,
                      programManagementService As IProgramManagementService)
            _logger = logger
            _opcuaManager = opcuaManager
            _configurationManager = configurationManager
            _generalApiService = generalApiService
            _macManDataService = macManDataService
            _programManagementService = programManagementService
            
            ' Initialize machine connections at startup
            Task.Run(AddressOf InitializeMachineConnections)
        End Sub

        ''' <summary>
        ''' Process data request based on node type
        ''' </summary>
        Public Async Function ProcessDataRequest(nodeId As String, requestType As String) As Task Implements IDataRouter.ProcessDataRequest
            Try
                _logger.LogInfo($"🔄 Processing data request for: {nodeId} (Type: {requestType})")

                ' Determine the type of data collection based on node pattern
                If nodeId.Contains("MacManData") AndAlso nodeId.Contains(".extract") Then
                    ' MacMan data collection (specific check first)
                    Await ProcessMacManDataRequest(nodeId)
                ElseIf nodeId.Contains(".Data.") AndAlso nodeId.Contains(".extract") Then
                    ' General API call for other Data extract nodes
                    Await ProcessGeneralApiRequest(nodeId)
                ElseIf nodeId.Contains(".ProgramManagement.Ctrl") Then
                    ' Program management control
                    Await ProcessProgramManagementRequest(nodeId)
                Else
                    _logger.LogWarning($"⚠️ Unknown data type for node: {nodeId}")
                End If

            Catch ex As Exception
                _logger.LogError($"❌ Error processing data request for {nodeId}: {ex.Message}")
            End Try
        End Function

        ''' <summary>
        ''' Process General API request
        ''' </summary>
        Private Async Function ProcessGeneralApiRequest(nodeId As String) As Task
            Try
                ' Extract and log machine information
                Dim machineName = ExtractMachineName(nodeId)
                _logger.LogInfo($"📊 Processing General API request for machine: {machineName}")
                _logger.LogInfo($"📊 Node: {nodeId}")
                
                ' Log machine configuration
                Await LogMachineConfiguration(machineName)
                
                ' Check and ensure machine connection
                Dim machineApi = Await EnsureMachineConnection(machineName)
                If machineApi Is Nothing Then
                    _logger.LogError($"❌ Cannot connect to machine: {machineName}")
                    Return
                End If
                
                ' Get API configuration for this node
                Dim apiConfig = GetApiConfigurationForNode(nodeId)
                If apiConfig Is Nothing Then
                    _logger.LogError($"❌ No API configuration found for node: {nodeId}")
                    Return
                End If
                
                ' Call the General API service with machine connection and config
                Dim apiData = Await _generalApiService.GetDataAsync(nodeId, machineApi, apiConfig)
                
                ' Update OPC UA nodes with the collected data
                Await UpdateOpcUaNodes(nodeId, apiData)
                
            Catch ex As Exception
                _logger.LogError($"❌ Error in General API request: {ex.Message}")
            End Try
        End Function

        ''' <summary>
        ''' Process MacMan data request
        ''' </summary>
        Private Async Function ProcessMacManDataRequest(nodeId As String) As Task
            Try
                _logger.LogInfo($"🖥️ Processing MacMan data request for: {nodeId}")
                
                ' Call the MacMan data service
                Dim macManData = Await _macManDataService.GetDataAsync(nodeId)
                
                ' Update OPC UA nodes with the collected data
                Await UpdateOpcUaNodes(nodeId, macManData)
                
            Catch ex As Exception
                _logger.LogError($"❌ Error in MacMan data request: {ex.Message}")
            End Try
        End Function

        ''' <summary>
        ''' Process Program Management request
        ''' </summary>
        Private Async Function ProcessProgramManagementRequest(nodeId As String) As Task
            Try
                _logger.LogInfo($"🎛️ Processing Program Management request for: {nodeId}")
                
                ' Call the Program Management service
                Dim programData = Await _programManagementService.GetDataAsync(nodeId)
                
                ' Update OPC UA nodes with the collected data
                Await UpdateOpcUaNodes(nodeId, programData)
                
            Catch ex As Exception
                _logger.LogError($"❌ Error in Program Management request: {ex.Message}")
            End Try
        End Function

        ''' <summary>
        ''' Update OPC UA nodes with collected data
        ''' </summary>
        Private Async Function UpdateOpcUaNodes(nodeId As String, data As ApiDataResult) As Task
            Try
                Dim baseNodePath As String
                
                ' Handle different node types
                If nodeId.Contains(".ProgramManagement.Ctrl") Then
                    ' For ProgramManagement: only update Exception node
                    baseNodePath = nodeId.Replace(".Ctrl", "")
                    _logger.LogDebug($"🎛️ ProgramManagement node: {nodeId} → {baseNodePath}")
                    
                    ' For ProgramManagement: only update Exception field
                    Dim exceptionNodeId = $"{baseNodePath}.Exception"
                    Dim exceptionMessage = If(CBool(data.Value), "", If(String.IsNullOrEmpty(data.ErrorMessage), "ProgramManagement workflow completed with errors", data.ErrorMessage))
                    
                    If Await _opcuaManager.WriteNodeValue(exceptionNodeId, exceptionMessage) Then
                        _logger.LogInfo($"✅ Updated {exceptionNodeId} = '{exceptionMessage}'")
                    Else
                        _logger.LogError($"❌ Failed to write exception to {exceptionNodeId}")
                    End If
                    
                    _logger.LogInfo($"🎉 ProgramManagement Exception field updated for: {baseNodePath}")
                    
                ElseIf nodeId.Contains("MacManData") AndAlso nodeId.Contains(".extract") Then
                    ' For MacManData: only reset extract field (timestamps are updated by MacManDataService itself)
                    baseNodePath = nodeId.Replace(".extract", "")
                    _logger.LogDebug($"🖥️ MacManData node: {nodeId} → {baseNodePath}")
                    
                    ' Update extract field to False (reset trigger)
                    Dim extractNodeId = $"{baseNodePath}.extract"
                    If Await _opcuaManager.WriteNodeValue(extractNodeId, False) Then
                        _logger.LogInfo($"✅ Reset {extractNodeId} = False")
                    Else
                        _logger.LogError($"❌ Failed to reset extract to False for {extractNodeId}")
                    End If
                    
                    _logger.LogInfo($"🎉 MacManData extract field reset for: {baseNodePath}")
                    _logger.LogInfo($"   Note: LastProcessed timestamps updated by MacManDataService")
                    
                Else
                    ' For other Data nodes: update extract, lastupdated and value
                    baseNodePath = nodeId.Replace(".extract", "")
                    _logger.LogDebug($"📊 Data node: {nodeId} → {baseNodePath}")
                    
                    ' Update extract field to False (reset trigger)
                    Dim extractNodeId = $"{baseNodePath}.extract"
                    If Await _opcuaManager.WriteNodeValue(extractNodeId, False) Then
                        _logger.LogInfo($"✅ Reset {extractNodeId} = False")
                    Else
                        _logger.LogError($"❌ Failed to reset extract to False for {extractNodeId}")
                    End If
                    
                    ' Update lastupdated field with current timestamp (as Integer)
                    Dim lastUpdatedNodeId = $"{baseNodePath}.lastupdated"
                    Dim timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    
                    ' Use Integer as default (based on OPC UA server compatibility)
                    If Await _opcuaManager.WriteNodeValue(lastUpdatedNodeId, CInt(timestamp)) Then
                        _logger.LogInfo($"✅ Updated {lastUpdatedNodeId} = {timestamp}")
                    Else
                        _logger.LogError($"❌ Failed to write timestamp to {lastUpdatedNodeId}")
                    End If
                    
                    ' Update value field with collected data
                    Dim valueNodeId = $"{baseNodePath}.value"
                    Await _opcuaManager.WriteNodeValue(valueNodeId, data.Value)
                    _logger.LogInfo($"✅ Updated {valueNodeId} = {data.Value}")
                    
                    _logger.LogInfo($"🎉 Successfully updated all Data fields for: {baseNodePath}")
                End If
                
            Catch ex As Exception
                _logger.LogError($"❌ Error updating OPC UA nodes: {ex.Message}")
            End Try
        End Function

        ''' <summary>
        ''' Extract machine name from node ID
        ''' Example: ns=2;s=Okuma.Machines.1 - MU-10000H.Data.WorkCounterA_Counted.extract -> "1 - MU-10000H"
        ''' </summary>
        Private Function ExtractMachineName(nodeId As String) As String
            Try
                ' Pattern: ns=2;s=Okuma.Machines.<MachineName>.Data.<DataItem>.extract
                If nodeId.Contains("Okuma.Machines.") AndAlso nodeId.Contains(".Data.") Then
                    Dim startIndex = nodeId.IndexOf("Okuma.Machines.") + "Okuma.Machines.".Length
                    Dim endIndex = nodeId.IndexOf(".Data.")
                    
                    If startIndex > 0 AndAlso endIndex > startIndex Then
                        Return nodeId.Substring(startIndex, endIndex - startIndex)
                    End If
                End If
                
                Return "Unknown"
                
            Catch ex As Exception
                _logger.LogError($"Error extracting machine name from {nodeId}: {ex.Message}")
                Return "Unknown"
            End Try
        End Function

        ''' <summary>
        ''' Log complete machine configuration
        ''' </summary>
        Private Async Function LogMachineConfiguration(machineName As String) As Task
            Try
                _logger.LogInfo($"🏭 ===== MACHINE CONFIGURATION: {machineName} =====")
                
                ' Build the machine config node ID
                Dim machineConfigNodeId = $"ns=2;s=Okuma.Machines.{machineName}.MachineConfig"
                _logger.LogInfo($"📋 Reading configuration from: {machineConfigNodeId}")
                
                ' Read all configuration values
                Dim configItems = New List(Of String) From {
                    "Enabled",
                    "IPAddress", 
                    "MachineId"
                }
                
                For Each configItem In configItems
                    Try
                        Dim configNodeId = $"{machineConfigNodeId}.{configItem}"
                        Dim value = Await _opcuaManager.ReadNodeValue(configNodeId)
                        
                        If value IsNot Nothing Then
                            _logger.LogInfo($"   📌 {configItem}: {value}")
                        Else
                            _logger.LogInfo($"   📌 {configItem}: <null>")
                        End If
                        
                    Catch ex As Exception
                        _logger.LogWarning($"   ⚠️ Could not read {configItem}: {ex.Message}")
                    End Try
                Next
                
                _logger.LogInfo($"🏭 ===== END CONFIGURATION: {machineName} =====")
                
            Catch ex As Exception
                _logger.LogError($"Error logging machine configuration for {machineName}: {ex.Message}")
            End Try
        End Function

        ''' <summary>
        ''' Initialize machine connections at startup - only when OPC UA is connected
        ''' </summary>
        Public Async Function InitializeMachineConnections() As Task
            Try
                ' Only initialize if OPC UA is connected
                If Not _opcuaManager.IsConnected Then
                    _logger.LogWarning("⚠️ Skipping machine initialization - OPC UA not connected")
                    Return
                End If
                
                _logger.LogInfo("🔧 Discovering machines from OPC UA server...")
                
                ' Discover machines dynamically from OPC UA server instead of hardcoding
                Dim discoveredMachines = DiscoverMachinesFromOpcUa()
                
                If discoveredMachines.Count = 0 Then
                    _logger.LogWarning("⚠️ No machines discovered from OPC UA server")
                    Return
                End If
                
                _logger.LogInfo($"🔍 Discovered {discoveredMachines.Count} machines: {String.Join(", ", discoveredMachines)}")
                
                For Each machineName In discoveredMachines
                    Dim machineApi As ClassOspApi = Nothing
                    Try
                        _logger.LogInfo($"🔧 Attempting initialization connection to: {machineName}")
                        machineApi = Await EnsureMachineConnection(machineName)
                        If machineApi IsNot Nothing Then
                            _logger.LogInfo($"✅ Successfully connected to {machineName} during initialization")
                        Else
                            _logger.LogWarning($"⚠️ Could not connect to {machineName} during initialization")
                        End If
                    Catch ex As Exception
                        _logger.LogError($"❌ Error connecting to {machineName} during init: {ex.Message}")
                        machineApi = Nothing
                    End Try
                    
                    ' Ensure DisConnected status is updated for any failed connection during init
                    If machineApi Is Nothing Then
                        Try
                            Await UpdateConnectionStatus(machineName, False)
                        Catch statusEx As Exception
                            _logger.LogError($"❌ Failed to update DisConnected status during init: {statusEx.Message}")
                        End Try
                    End If
                Next
                
                _logger.LogInfo("✅ Machine connection initialization completed")
                
            Catch ex As Exception
                _logger.LogError($"❌ Error initializing machine connections: {ex.Message}")
            End Try
        End Function

        ''' <summary>
        ''' Ensure machine is connected, connect if not
        ''' </summary>
        Private Async Function EnsureMachineConnection(machineName As String) As Task(Of ClassOspApi)
            Try
                ' Check if already connected (first quick check without lock)
                If _machineConnections.ContainsKey(machineName) Then
                    _logger.LogDebug($"🔗 Machine {machineName} already connected")
                    Return _machineConnections(machineName)
                End If
                
                ' Use lock to prevent multiple simultaneous connection attempts to same machine
                SyncLock _connectionLock
                    ' Double-check if connected (race condition protection)
                    If _machineConnections.ContainsKey(machineName) Then
                        _logger.LogDebug($"🔗 Machine {machineName} connected during wait")
                        Return _machineConnections(machineName)
                    End If
                    
                    ' Check if connection is already in progress
                    Dim connectionKey = $"connecting_{machineName}"
                    If _machineConnections.ContainsKey(connectionKey) Then
                        _logger.LogInfo($"⏳ Connection to {machineName} already in progress, waiting...")
                        Return Nothing
                    End If
                    
                    ' Mark connection as in progress
                    _machineConnections(connectionKey) = Nothing
                End SyncLock
                
                Try
                    ' Always try to connect - no retry logic in DataRouter
                    _logger.LogInfo($"🔌 Attempting to connect to machine: {machineName}")
                    Dim result = Await ConnectToMachine(machineName)
                    
                    ' Clean up the "connecting" marker
                    SyncLock _connectionLock
                        Dim connectionKey = $"connecting_{machineName}"
                        If _machineConnections.ContainsKey(connectionKey) Then
                            _machineConnections.Remove(connectionKey)
                        End If
                    End SyncLock
                    
                    Return result
                    
                Finally
                    ' Ensure we always clean up the "connecting" marker
                    SyncLock _connectionLock
                        Dim connectionKey = $"connecting_{machineName}"
                        If _machineConnections.ContainsKey(connectionKey) Then
                            _machineConnections.Remove(connectionKey)
                        End If
                    End SyncLock
                End Try
                
            Catch ex As Exception
                _logger.LogError($"❌ Error ensuring machine connection for {machineName}: {ex.Message}")
                Return Nothing
            End Try
        End Function

        ''' <summary>
        ''' Connect to a specific machine using ClassOspApi
        ''' </summary>
        Private Async Function ConnectToMachine(machineName As String) As Task(Of ClassOspApi)
            Try
                ' Get machine IP address from OPC UA
                Dim machineIP = Await GetMachineIPAddress(machineName)
                If String.IsNullOrEmpty(machineIP) Then
                    _logger.LogError($"❌ No IP address found for machine: {machineName}")
                    ' Update DisConnected status when IP is invalid/missing
                    Await UpdateConnectionStatus(machineName, False)
                    Return Nothing
                End If
                
                _logger.LogInfo($"🌐 Connecting to machine {machineName} at IP: {machineIP}")
                
                ' Create new ClassOspApi instance
                Dim ospApi = New ClassOspApi()
                
                Dim connectionSuccessful = False
                Dim connectionException As Exception = Nothing
                
                Try
                    ' Connect to the machine (assuming MC type for now)
                    Await Task.Run(Sub()
                        _logger.LogInfo($"🔧 Calling ConnectData({machineIP}, TYPE_MC, True)")
                        ospApi.ConnectData(machineIP, ClassOspApi.NCTYPE.TYPE_MC, True)
                        _logger.LogInfo($"🔧 ConnectData completed. Result: {ospApi.Result}")
                        _logger.LogInfo($"🔧 ErrMsg: '{ospApi.ErrMsg}'")
                        _logger.LogInfo($"🔧 ErrData: {ospApi.ErrData}")
                        _logger.LogInfo($"🔧 ErrStr: '{ospApi.ErrStr}'")
                        _logger.LogInfo($"🔧 MethodLog: '{ospApi.MethodLog}'")
                    End Sub)
                    
                    ' Check if connection was successful - improved validation
                    ' ConnectData is successful if no exception was thrown and no error message exists
                    ' Result can be empty string, "0", or Nothing when successful
                    connectionSuccessful = String.IsNullOrEmpty(ospApi.ErrMsg) AndAlso 
                                         (String.IsNullOrEmpty(ospApi.Result) OrElse ospApi.Result = "0")
                    
                Catch connectEx As Exception
                    _logger.LogError($"❌ Exception during ConnectData call: {connectEx.Message}")
                    _logger.LogError($"   Stack trace: {connectEx.StackTrace}")
                    connectionException = connectEx
                    connectionSuccessful = False
                End Try
                
                ' Handle connection result outside try-catch
                If connectionSuccessful Then
                    _logger.LogInfo($"✅ Successfully connected to machine: {machineName}")
                    _logger.LogInfo($"   Connection details: {ospApi.MethodLog}")
                    
                    SyncLock _connectionLock
                        _machineConnections(machineName) = ospApi
                    End SyncLock
                    
                    ' Update Connected timestamp in OPC UA
                    Await UpdateConnectionStatus(machineName, True)
                    
                    Return ospApi
                Else
                    If connectionException Is Nothing Then
                        _logger.LogError($"❌ Failed to connect to machine {machineName}")
                        _logger.LogError($"   Error Message: {ospApi.ErrMsg}")
                        _logger.LogError($"   Error Code: {ospApi.Result}")
                        _logger.LogError($"   Error Data: {ospApi.ErrData}")
                        _logger.LogError($"   Error String: {ospApi.ErrStr}")
                        _logger.LogError($"   Method Log: {ospApi.MethodLog}")
                    End If
                    
                    ' Update DisConnected timestamp in OPC UA
                    Await UpdateConnectionStatus(machineName, False)
                    
                    ospApi.DisconnectData()
                    Return Nothing
                End If
                
            Catch ex As Exception
                _logger.LogError($"❌ Exception connecting to machine {machineName}: {ex.Message}")
                _logger.LogError($"   Stack trace: {ex.StackTrace}")
                Return Nothing
            End Try
            
            ' If we reach here, there was an outer exception, update status
            Try
                Await UpdateConnectionStatus(machineName, False)
            Catch statusEx As Exception
                _logger.LogError($"❌ Failed to update connection status: {statusEx.Message}")
            End Try
        End Function

        ''' <summary>
        ''' Update machine connection status in OPC UA
        ''' </summary>
        Private Async Function UpdateConnectionStatus(machineName As String, isConnected As Boolean) As Task
            Try
                Dim timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                
                If isConnected Then
                    ' Update Connected timestamp and set DisConnected to 0
                    Dim connectedNodeId = $"ns=2;s=Okuma.Machines.{machineName}.Connected"
                    If Await _opcuaManager.WriteNodeValue(connectedNodeId, CInt(timestamp)) Then
                        _logger.LogInfo($"✅ Updated {connectedNodeId} = {timestamp}")
                    Else
                        _logger.LogError($"❌ Failed to write Connected timestamp to {connectedNodeId}")
                    End If
                    
                    ' Set DisConnected to 0 when connected
                    Dim disconnectedNodeId = $"ns=2;s=Okuma.Machines.{machineName}.DisConnected"
                    If Await _opcuaManager.WriteNodeValue(disconnectedNodeId, CInt(0)) Then
                        _logger.LogInfo($"✅ Reset {disconnectedNodeId} = 0")
                    Else
                        _logger.LogError($"❌ Failed to reset DisConnected to 0 for {disconnectedNodeId}")
                    End If
                Else
                    ' Update DisConnected timestamp and set Connected to 0
                    Dim disconnectedNodeId = $"ns=2;s=Okuma.Machines.{machineName}.DisConnected"
                    If Await _opcuaManager.WriteNodeValue(disconnectedNodeId, CInt(timestamp)) Then
                        _logger.LogInfo($"✅ Updated {disconnectedNodeId} = {timestamp}")
                    Else
                        _logger.LogError($"❌ Failed to write DisConnected timestamp to {disconnectedNodeId}")
                    End If
                    
                    ' Set Connected to 0 when disconnected
                    Dim connectedNodeId = $"ns=2;s=Okuma.Machines.{machineName}.Connected"
                    If Await _opcuaManager.WriteNodeValue(connectedNodeId, CInt(0)) Then
                        _logger.LogInfo($"✅ Reset {connectedNodeId} = 0")
                    Else
                        _logger.LogError($"❌ Failed to reset Connected to 0 for {connectedNodeId}")
                    End If
                End If
                
            Catch ex As Exception
                _logger.LogError($"❌ Error updating connection status for {machineName}: {ex.Message}")
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
                    ' No parsing - use IP as-is from OPC UA
                    Return ipStr
                End If
                
                _logger.LogWarning($"⚠️ No IP address configured for machine {machineName}")
                Return Nothing
                
            Catch ex As Exception
                _logger.LogError($"❌ Error reading IP address for machine {machineName}: {ex.Message}")
                Return Nothing
            End Try
        End Function

        ''' <summary>
        ''' Check if machine is connected
        ''' </summary>
        Private Function IsMachineConnected(machineName As String) As Boolean
            SyncLock _connectionLock
                Return _machineConnections.ContainsKey(machineName)
            End SyncLock
        End Function
        
        ''' <summary>
        ''' Discover machines from OPC UA server dynamically
        ''' </summary>
        Private Function DiscoverMachinesFromOpcUa() As List(Of String)
            Dim machines As New List(Of String)()
            
            Try
                If Not _opcuaManager.IsConnected Then
                    _logger.LogWarning("⚠️ Cannot discover machines - OPC UA not connected")
                    Return machines
                End If
                
                _logger.LogInfo("🔍 Browsing OPC UA server for machine nodes...")
                
                ' Browse for machine nodes under Okuma.Machines
                Dim machineNodes = _opcuaManager.BrowseNodes("ns=2;s=Okuma.Machines")
                
                If machineNodes IsNot Nothing Then
                    For Each node In machineNodes
                        ' Extract machine name from node ID
                        ' Pattern: ns=2;s=Okuma.Machines.<MachineName>
                        If node.Contains("Okuma.Machines.") Then
                            Dim machineName = ExtractMachineNameFromNodeId(node)
                            If Not String.IsNullOrEmpty(machineName) AndAlso Not machines.Contains(machineName) Then
                                machines.Add(machineName)
                                _logger.LogInfo($"🔍 Found machine: {machineName}")
                            End If
                        End If
                    Next
                End If
                
                _logger.LogInfo($"🔍 Machine discovery completed - found {machines.Count} machines")
                
            Catch ex As Exception
                _logger.LogError($"❌ Error discovering machines from OPC UA: {ex.Message}")
            End Try
            
            Return machines
        End Function
        
        ''' <summary>
        ''' Extract machine name from OPC UA node ID
        ''' </summary>
        Private Function ExtractMachineNameFromNodeId(nodeId As String) As String
            Try
                ' Pattern: ns=2;s=Okuma.Machines.<MachineName>
                If nodeId.Contains("Okuma.Machines.") Then
                    Dim startIndex = nodeId.IndexOf("Okuma.Machines.") + "Okuma.Machines.".Length
                    Dim remainingPart = nodeId.Substring(startIndex)
                    
                    ' If there's a dot after the machine name, extract only the machine name part
                    Dim dotIndex = remainingPart.IndexOf(".")
                    If dotIndex > 0 Then
                        Return remainingPart.Substring(0, dotIndex)
                    Else
                        Return remainingPart
                    End If
                End If
                
                Return Nothing
                
            Catch ex As Exception
                _logger.LogError($"Error extracting machine name from {nodeId}: {ex.Message}")
                Return Nothing
            End Try
        End Function
        
        ''' <summary>
        ''' Get machine connection for data collection services
        ''' Public method for MacManDataService to access existing connections
        ''' </summary>
        Public Function GetMachineConnection(machineName As String) As ClassOspApi
            Try
                SyncLock _connectionLock
                    If _machineConnections.ContainsKey(machineName) Then
                        Dim connection = _machineConnections(machineName)
                        _logger.LogDebug($"🔗 Providing machine connection for {machineName}")
                        Return connection
                    Else
                        _logger.LogWarning($"⚠️ No connection found for machine {machineName}")
                        Return Nothing
                    End If
                End SyncLock
            Catch ex As Exception
                _logger.LogError($"❌ Error getting machine connection for {machineName}: {ex.Message}")
                Return Nothing
            End Try
        End Function

        ''' <summary>
        ''' Disconnect from a machine
        ''' </summary>
        Private Async Function DisconnectMachine(machineName As String) As Task
            Try
                SyncLock _connectionLock
                    If _machineConnections.ContainsKey(machineName) Then
                        Dim ospApi = _machineConnections(machineName)
                        ospApi.DisconnectData()
                        _machineConnections.Remove(machineName)
                        _logger.LogInfo($"🔌 Disconnected from machine: {machineName}")
                    End If
                End SyncLock
                
                ' Update DisConnected timestamp in OPC UA
                Await UpdateConnectionStatus(machineName, False)
                
            Catch ex As Exception
                _logger.LogError($"❌ Error disconnecting from machine {machineName}: {ex.Message}")
            End Try
        End Function
        
        ''' <summary>
        ''' Get API configuration for a specific node ID
        ''' </summary>
        Private Function GetApiConfigurationForNode(nodeId As String) As ApiConfigurationItem
            Try
                ' Extract the data field name from nodeId
                ' Example: ns=2;s=Okuma.Machines.1 - MU-10000H.Data.WorkCounterA_Counted.extract
                ' We want to extract "WorkCounterA_Counted"
                
                Dim dataFieldName As String = Nothing
                If nodeId.Contains(".Data.") AndAlso nodeId.Contains(".extract") Then
                    Dim parts = nodeId.Split("."c)
                    For i = 0 To parts.Length - 1
                        If parts(i) = "Data" AndAlso i + 1 < parts.Length Then
                            dataFieldName = parts(i + 1)
                            Exit For
                        End If
                    Next
                End If
                
                If String.IsNullOrEmpty(dataFieldName) Then
                    _logger.LogWarning($"⚠️ Could not extract data field name from nodeId: {nodeId}")
                    Return Nothing
                End If
                
                _logger.LogDebug($"🔍 Looking for API config with DataFieldName: {dataFieldName}")
                
                ' Search through API configuration
                Dim apiConfig = _configurationManager.GetApiConfiguration()
                _logger.LogDebug($"🔍 API Config loaded: {apiConfig IsNot Nothing}")
                If apiConfig IsNot Nothing Then
                    _logger.LogDebug($"🔍 Configurations count: {apiConfig.Configurations?.Count}")
                End If
                If apiConfig?.Configurations IsNot Nothing Then
                    For Each machineType In apiConfig.Configurations.Values
                        Dim seriesConfigs = machineType.GetSeriesConfigurations()
                        If seriesConfigs IsNot Nothing Then
                            For Each series In seriesConfigs.Values
                                ' Search in General configurations
                                If series.General IsNot Nothing Then
                                    For Each configItem In series.General
                                        If configItem.DataFieldName = dataFieldName OrElse configItem.ApiName = dataFieldName Then
                                            _logger.LogInfo($"✅ Found API config: {configItem.ApiName} for {dataFieldName}")
                                            Return configItem
                                        End If
                                    Next
                                End If
                                
                                ' Search in Custom configurations
                                If series.Custom IsNot Nothing Then
                                    For Each configItem In series.Custom
                                        If configItem.DataFieldName = dataFieldName OrElse configItem.ApiName = dataFieldName Then
                                            _logger.LogInfo($"✅ Found API config: {configItem.ApiName} for {dataFieldName}")
                                            Return configItem
                                        End If
                                    Next
                                End If
                            Next
                        End If
                    Next
                End If
                
                _logger.LogWarning($"⚠️ No API configuration found for data field: {dataFieldName}")
                Return Nothing
                
            Catch ex As Exception
                _logger.LogError($"❌ Error getting API configuration for node {nodeId}: {ex.Message}")
                Return Nothing
            End Try
        End Function

    End Class

End Namespace
