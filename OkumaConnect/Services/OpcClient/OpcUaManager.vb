Imports System
Imports System.Collections.Generic
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Linq
Imports Opc.Ua
Imports Opc.Ua.Client
Imports Opc.Ua.Configuration
Imports OkumaConnect.Models
Imports OkumaConnect.Services.Logging

Namespace Services.OpcClient

''' <summary>
''' OPC UA Manager for Okuma machine monitoring
''' Based on the working ConnectedFactory/Okuma implementation
''' </summary>
Public Class OpcUaManager
    Implements IDisposable

#Region "Private Fields"
    Private _session As Session
    Private _subscription As Subscription
    Private _applicationConfiguration As ApplicationConfiguration
    Private _connectionSettings As OpcUaConnectionSettings
    Private _logger As ILogger
    Private _isConnected As Boolean = False
    Private _nodeSubscriptions As New Dictionary(Of String, MonitoredItem)()
    Private _subscribedNodeIds As New HashSet(Of String)() ' Keep track of subscribed nodes for reconnect
    Private _reconnectTimer As Timer
    Private _isReconnecting As Boolean = False
    Private _disposed As Boolean = False
#End Region

#Region "Events"
    Public Event ConnectionStatusChanged(connected As Boolean)
    Public Event DataReceived(nodeId As String, value As Object, timestamp As DateTime)
    Public Event ErrorOccurred(message As String, exception As Exception)
#End Region

#Region "Properties"
    Public ReadOnly Property IsConnected As Boolean
        Get
            Return _isConnected AndAlso _session IsNot Nothing AndAlso _session.Connected
        End Get
    End Property
#End Region

#Region "Constructor"
    Public Sub New(connectionSettings As OpcUaConnectionSettings, logger As ILogger)
        _connectionSettings = connectionSettings
        _logger = logger
        
        Try
            InitializeApplicationConfigurationAsync().Wait()
        Catch ex As Exception
            _logger.LogError($"Failed to initialize application configuration: {ex.Message}")
        End Try
        
        _logger.LogInfo("🔧 OPC UA Manager initialized")
    End Sub
#End Region

#Region "Public Methods"
    ''' <summary>
    ''' Connect to OPC UA server
    ''' </summary>
    Public Async Function ConnectAsync() As Task(Of Boolean)
        Try
            _logger.LogInfo($"🔗 Connecting to OPC UA server: {_connectionSettings.ServerUrl}")
            
            ' Select endpoint using the same method as working version
            Dim selectedEndpoint As EndpointDescription = Nothing
            Try
                ' Try to discover endpoints first
                Dim discoveryClient As DiscoveryClient = Nothing
                Try
                    discoveryClient = DiscoveryClient.Create(New Uri(_connectionSettings.ServerUrl))
                    Dim endpoints As EndpointDescriptionCollection = discoveryClient.GetEndpoints(Nothing)
                    
                    If endpoints IsNot Nothing AndAlso endpoints.Count > 0 Then
                        _logger.LogInfo($"Found {endpoints.Count} endpoints")
                        ' Look for an endpoint that supports anonymous user tokens
                        For Each endpoint As EndpointDescription In endpoints
                            _logger.LogInfo($"Endpoint: {endpoint.EndpointUrl}, Security: {endpoint.SecurityPolicyUri}")
                            If endpoint.UserIdentityTokens IsNot Nothing Then
                                Dim tokenTypes = String.Join(", ", endpoint.UserIdentityTokens.Select(Function(t) t.TokenType.ToString()))
                                _logger.LogInfo($"  Supported identity types: {tokenTypes}")
                                For Each token In endpoint.UserIdentityTokens
                                    If token.TokenType = UserTokenType.Anonymous Then
                                        selectedEndpoint = endpoint
                                        _logger.LogInfo($"  Selected endpoint with anonymous support")
                                        Exit For
                                    End If
                                Next
                                If selectedEndpoint IsNot Nothing Then Exit For
                            Else
                                _logger.LogInfo("  No user identity tokens defined")
                            End If
                        Next
                        
                        ' If no anonymous endpoint found, try the first available endpoint
                        If selectedEndpoint Is Nothing Then
                            selectedEndpoint = endpoints(0)
                            _logger.LogInfo("No anonymous endpoint found, using first available endpoint")
                        End If
                    End If
                Finally
                    discoveryClient?.Dispose()
                End Try
                
                ' Fallback to the original method if discovery failed
                If selectedEndpoint Is Nothing Then
                    _logger.LogInfo("Discovery failed, falling back to SelectEndpoint")
                    selectedEndpoint = CoreClientUtils.SelectEndpoint(_connectionSettings.ServerUrl, False)
                End If
            Catch ex As Exception
                _logger.LogError($"Endpoint selection failed: {ex.Message}")
                Return False
            End Try
            
            If selectedEndpoint Is Nothing Then
                _logger.LogError("No endpoint found")
                Return False
            End If
            
            _logger.LogInfo($"Selected endpoint: {selectedEndpoint.EndpointUrl} with security: {selectedEndpoint.SecurityPolicyUri}")
            
            ' Create endpoint configuration
            Dim endpointConfiguration As EndpointConfiguration = EndpointConfiguration.Create(_applicationConfiguration)
            Dim configuredEndpoint = New ConfiguredEndpoint(Nothing, selectedEndpoint, endpointConfiguration)
            
            ' Create user identity based on endpoint support
            Dim userIdentity As IUserIdentity = Nothing
            
            ' Check what the selected endpoint supports
            If selectedEndpoint.UserIdentityTokens IsNot Nothing Then
                Dim supportsAnonymous = selectedEndpoint.UserIdentityTokens.Any(Function(t) t.TokenType = UserTokenType.Anonymous)
                Dim supportsUserName = selectedEndpoint.UserIdentityTokens.Any(Function(t) t.TokenType = UserTokenType.UserName)
                
                _logger.LogInfo($"Endpoint supports - Anonymous: {supportsAnonymous}, UserName: {supportsUserName}")
                _logger.LogInfo($"Connection settings - Username: '{_connectionSettings.Username}', Password: '{If(String.IsNullOrEmpty(_connectionSettings.Password), "EMPTY", "SET")}'")
                
                If supportsUserName AndAlso Not String.IsNullOrEmpty(_connectionSettings.Username) Then
                    ' Use username/password authentication - create UserIdentity properly
                    userIdentity = New UserIdentity(_connectionSettings.Username, _connectionSettings.Password)
                    _logger.LogInfo($"Using username authentication: {_connectionSettings.Username}")
                ElseIf supportsAnonymous Then
                    ' Use anonymous authentication
                    userIdentity = Nothing ' Anonymous is represented by Nothing/null
                    _logger.LogInfo("Using anonymous user identity")
                Else
                    ' Default to anonymous
                    userIdentity = Nothing
                    _logger.LogInfo("Using default anonymous user identity")
                End If
            Else
                userIdentity = Nothing
                _logger.LogInfo("No user identity tokens specified, using anonymous")
            End If
            
            _logger.LogInfo($"Final user identity type: {If(userIdentity?.GetType().Name, "NULL (Anonymous)")}")
            
            ' Create session with certificate event handler
            _session = Await Session.Create(
                _applicationConfiguration,
                configuredEndpoint,
                False,
                "OkumaConnect",
                60000,
                userIdentity,
                Nothing
            )
            
            ' Add session event handlers for better connection monitoring
            If _session IsNot Nothing Then
                AddHandler _session.KeepAlive, AddressOf OnSessionKeepAlive
                AddHandler _session.SessionClosing, AddressOf OnSessionClosing
            End If
            
            If _session IsNot Nothing AndAlso _session.Connected Then
                _isConnected = True
                _logger.LogInfo("✅ Successfully connected to OPC UA server")
                
                ' Setup subscription
                CreateSubscription()
                
                ' Re-subscribe to all previously subscribed nodes (for reconnect scenarios)
                RestoreSubscriptions()
                
                RaiseEvent ConnectionStatusChanged(True)
                Return True
            End If
            
        Catch ex As Exception
            _logger.LogError($"❌ Connection failed: {ex.Message}")
            RaiseEvent ErrorOccurred("Connection failed", ex)
        End Try
        
        Return False
    End Function
    
    ''' <summary>
    ''' Disconnect from server
    ''' </summary>
    Public Sub Disconnect()
        Try
            CleanupSession()
            _logger.LogInfo("🔌 Disconnected from OPC UA server")
            RaiseEvent ConnectionStatusChanged(False)
        Catch ex As Exception
            _logger.LogError($"Error during disconnect: {ex.Message}")
        End Try
    End Sub
    
    ''' <summary>
    ''' Subscribe to a specific node
    ''' </summary>
    Public Function SubscribeToNode(nodeId As String) As Boolean
        Try
            If Not IsConnected OrElse _subscription Is Nothing Then
                _logger.LogWarning($"Cannot subscribe to {nodeId} - not connected or no subscription")
                Return False
            End If
            
            If _nodeSubscriptions.ContainsKey(nodeId) Then
                _logger.LogDebug($"Already subscribed to {nodeId}")
                Return True
            End If
            
            Dim monitoredItem = New MonitoredItem(_subscription.DefaultItem) With {
                .StartNodeId = nodeId,
                .AttributeId = Attributes.Value,
                .SamplingInterval = _connectionSettings.DefaultSamplingIntervalMs,
                .QueueSize = 10,
                .DiscardOldest = True
            }
            
            AddHandler monitoredItem.Notification, AddressOf OnMonitoredItemNotification
            
            _subscription.AddItem(monitoredItem)
            _subscription.ApplyChanges()
            
            _nodeSubscriptions(nodeId) = monitoredItem
            _subscribedNodeIds.Add(nodeId) ' Keep track for reconnect
            _logger.LogInfo($"📡 Subscribed to node: {nodeId}")
            Return True
            
        Catch ex As Exception
            _logger.LogError($"Failed to subscribe to node {nodeId}: {ex.Message}")
            Return False
        End Try
    End Function
    
    ''' <summary>
    ''' Browse and discover nodes in the server
    ''' </summary>
    Public Function BrowseNodes(startingNodeId As String) As List(Of String)
        Dim discoveredNodes = New List(Of String)()
        
        Try
            If Not IsConnected Then
                _logger.LogWarning("Cannot browse nodes - not connected")
                Return discoveredNodes
            End If
            
            Dim nodesToBrowse = New BrowseDescriptionCollection()
            nodesToBrowse.Add(New BrowseDescription() With {
                .NodeId = NodeId.Parse(startingNodeId),
                .BrowseDirection = BrowseDirection.Forward,
                .ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                .IncludeSubtypes = True,
                .NodeClassMask = CUInt(NodeClass.Variable Or NodeClass.Object),
                .ResultMask = CUInt(BrowseResultMask.All)
            })
            
            Dim results As BrowseResultCollection = Nothing
            Dim diagnosticInfos As DiagnosticInfoCollection = Nothing
            
            _session.Browse(
                Nothing,
                Nothing,
                CUInt(1000),
                nodesToBrowse,
                results,
                diagnosticInfos
            )
            
            If results IsNot Nothing AndAlso results.Count > 0 Then
                For Each result In results
                    If StatusCode.IsGood(result.StatusCode) Then
                        For Each reference In result.References
                            discoveredNodes.Add(reference.NodeId.ToString())
                        Next
                    End If
                Next
            End If
            
            _logger.LogInfo($"🔍 Discovered {discoveredNodes.Count} nodes from {startingNodeId}")
            
        Catch ex As Exception
            _logger.LogError($"Error browsing nodes: {ex.Message}")
        End Try
        
        Return discoveredNodes
    End Function
    
    ''' <summary>
    ''' Discover and subscribe to all Data Extract nodes (ns=2;s=Okuma.Machines.1*.Data.*.extract)
    ''' </summary>
    Public Function DiscoverAndSubscribeToDataExtractNodes() As Integer
        Try
            If Not IsConnected Then
                _logger.LogWarning("Cannot discover Data Extract nodes: not connected")
                Return 0
            End If
            
            _logger.LogInfo("🔍 Discovering Data Extract nodes (ns=2;s=Okuma.Machines.1*.Data.*.extract)...")
            
            Dim rootNodeId = "ns=2;s=Okuma.Machines"
            Dim foundNodes = New List(Of String)()
            
            ' Browse root node to find all machine nodes
            Dim machineNodes = BrowseNodes(rootNodeId)
            If machineNodes Is Nothing OrElse machineNodes.Count = 0 Then
                _logger.LogWarning($"No machine nodes found under {rootNodeId}")
                Return 0
            End If
            
            _logger.LogInfo($"📋 Found {machineNodes.Count} machine nodes")
            
            ' Search for machines starting with "1" and their Data.*.extract nodes
            For Each machineNodeId In machineNodes
                Try
                    ' Check if machine node contains "Machines.1" (machines starting with 1)
                    ' Pattern: ns=2;s=Okuma.Machines.1 - MU-10000H or similar
                    If machineNodeId.Contains("Machines.1") Then
                        _logger.LogInfo($"🎯 Found machine starting with 1: {machineNodeId}")
                        
                        ' Browse machine children to find Data folder
                        Dim machineChildren = BrowseNodes(machineNodeId)
                        If machineChildren IsNot Nothing Then
                            _logger.LogInfo($"   Found {machineChildren.Count} children under machine")
                            For Each childNode In machineChildren
                                _logger.LogDebug($"   Child: {childNode}")
                                If childNode.EndsWith(".Data") Then
                                    _logger.LogInfo($"📁 Found Data folder: {childNode}")
                                    
                                    ' Browse Data folder to find extract nodes
                                    Dim dataChildren = BrowseNodes(childNode)
                                    If dataChildren IsNot Nothing Then
                                        _logger.LogInfo($"     Found {dataChildren.Count} items in Data folder")
                                        For Each dataChild In dataChildren
                                            _logger.LogDebug($"     Data child: {dataChild}")
                                            If dataChild.Contains(".extract") Then
                                                foundNodes.Add(dataChild)
                                                _logger.LogInfo($"🎯 Found extract node: {dataChild}")
                                            Else
                                                ' Browse each data item to find extract nodes
                                                _logger.LogDebug($"     Browsing data item: {dataChild}")
                                                Dim extractChildren = BrowseNodes(dataChild)
                                                If extractChildren IsNot Nothing Then
                                                    For Each extractChild In extractChildren
                                                        _logger.LogDebug($"       Extract child: {extractChild}")
                                                        If extractChild.Contains(".extract") Then
                                                            foundNodes.Add(extractChild)
                                                            _logger.LogInfo($"🎯 Found extract node: {extractChild}")
                                                        End If
                                                    Next
                                                End If
                                            End If
                                        Next
                                    Else
                                        _logger.LogWarning($"     No children found in Data folder: {childNode}")
                                    End If
                                End If
                            Next
                        Else
                            _logger.LogWarning($"   No children found under machine: {machineNodeId}")
                        End If
                    End If
                Catch ex As Exception
                    _logger.LogWarning($"Error browsing machine {machineNodeId}: {ex.Message}")
                End Try
            Next
            
            If foundNodes.Count = 0 Then
                _logger.LogWarning("No Data Extract nodes found")
                Return 0
            End If
            
            ' Subscribe to found nodes
            _logger.LogInfo($"🔔 Subscribing to {foundNodes.Count} Data Extract nodes...")
            Dim successCount = 0
            
            For Each nodeId In foundNodes
                Try
                    If SubscribeToNode(nodeId) Then
                        successCount += 1
                        _logger.LogInfo($"✅ Subscribed to Data Extract: {nodeId}")
                    Else
                        _logger.LogWarning($"❌ Failed to subscribe to: {nodeId}")
                    End If
                Catch ex As Exception
                    _logger.LogError($"Error subscribing to {nodeId}: {ex.Message}")
                End Try
            Next
            
            _logger.LogInfo($"✅ Successfully subscribed to {successCount}/{foundNodes.Count} Data Extract nodes")
            Return successCount
            
        Catch ex As Exception
            _logger.LogError($"Error during Data Extract node discovery: {ex.Message}")
            Return 0
        End Try
    End Function
    
    ''' <summary>
    ''' Discover and save all MachineConfig nodes (ns=2;s=Okuma.Machines.*.MachineConfig)
    ''' </summary>
    Public Function DiscoverAndSaveMachineConfigs() As Integer
        Try
            If Not IsConnected Then
                _logger.LogWarning("Cannot discover MachineConfig nodes: not connected")
                Return 0
            End If
            
            _logger.LogInfo("🔍 Discovering and saving MachineConfig nodes (ns=2;s=Okuma.Machines.*.MachineConfig)...")
            
            Dim rootNodeId = "ns=2;s=Okuma.Machines"
            Dim foundConfigs = New List(Of String)()
            
            ' Browse root node to find all machine nodes
            Dim machineNodes = BrowseNodes(rootNodeId)
            If machineNodes Is Nothing OrElse machineNodes.Count = 0 Then
                _logger.LogWarning($"No machine nodes found under {rootNodeId}")
                Return 0
            End If
            
            _logger.LogInfo($"📋 Found {machineNodes.Count} machine nodes")
            
            ' Search for MachineConfig nodes under each machine
            For Each machineNodeId In machineNodes
                Try
                    _logger.LogInfo($"🔍 Browsing machine: {machineNodeId}")
                    
                    Dim machineChildren = BrowseNodes(machineNodeId)
                    If machineChildren IsNot Nothing Then
                        For Each childNode In machineChildren
                            If childNode.Contains("MachineConfig") Then
                                foundConfigs.Add(childNode)
                                _logger.LogInfo($"🎯 Found MachineConfig: {childNode}")
                                
                                ' Read and print the MachineConfig values
                                PrintMachineConfigValues(childNode).Wait()
                            End If
                        Next
                    End If
                Catch ex As Exception
                    _logger.LogWarning($"Error browsing machine {machineNodeId}: {ex.Message}")
                End Try
            Next
            
            _logger.LogInfo($"✅ Found and processed {foundConfigs.Count} MachineConfig nodes")
            Return foundConfigs.Count
            
        Catch ex As Exception
            _logger.LogError($"Error during MachineConfig discovery: {ex.Message}")
            Return 0
        End Try
    End Function
    
    ''' <summary>
    ''' Discover and subscribe to all ProgramManagement.Ctrl nodes (ns=2;s=Okuma.Machines.*.ProgramManagement.Ctrl)
    ''' </summary>
    Public Function DiscoverAndSubscribeToProgramManagementCtrlNodes() As Integer
        Try
            If Not IsConnected Then
                _logger.LogWarning("Cannot discover ProgramManagement.Ctrl nodes: not connected")
                Return 0
            End If
            
            _logger.LogInfo("🔍 Discovering ProgramManagement.Ctrl nodes (ns=2;s=Okuma.Machines.*.ProgramManagement.Ctrl)...")
            
            Dim rootNodeId = "ns=2;s=Okuma.Machines"
            Dim foundNodes = New List(Of String)()
            
            ' Browse root node to find all machine nodes
            Dim machineNodes = BrowseNodes(rootNodeId)
            If machineNodes Is Nothing OrElse machineNodes.Count = 0 Then
                _logger.LogWarning($"No machine nodes found under {rootNodeId}")
                Return 0
            End If
            
            _logger.LogInfo($"📋 Found {machineNodes.Count} machine nodes")
            
            ' Search for all machines and their ProgramManagement.Ctrl nodes
            For Each machineNodeId In machineNodes
                Try
                    _logger.LogInfo($"🎯 Checking machine: {machineNodeId}")
                    
                    ' Browse machine children to find ProgramManagement folder
                    Dim machineChildren = BrowseNodes(machineNodeId)
                    If machineChildren IsNot Nothing Then
                        _logger.LogInfo($"   Found {machineChildren.Count} children under machine")
                        For Each childNode In machineChildren
                            _logger.LogDebug($"   Child: {childNode}")
                            If childNode.EndsWith(".ProgramManagement") Then
                                _logger.LogInfo($"📁 Found ProgramManagement folder: {childNode}")
                                
                                ' Browse ProgramManagement folder to find Ctrl node
                                Dim progMgmtChildren = BrowseNodes(childNode)
                                If progMgmtChildren IsNot Nothing Then
                                    _logger.LogInfo($"     Found {progMgmtChildren.Count} items in ProgramManagement folder")
                                    For Each progMgmtChild In progMgmtChildren
                                        _logger.LogDebug($"     ProgramManagement child: {progMgmtChild}")
                                        If progMgmtChild.EndsWith(".Ctrl") Then
                                            foundNodes.Add(progMgmtChild)
                                            _logger.LogInfo($"🎯 Found Ctrl node: {progMgmtChild}")
                                        End If
                                    Next
                                Else
                                    _logger.LogWarning($"     No children found in ProgramManagement folder: {childNode}")
                                End If
                            End If
                        Next
                    Else
                        _logger.LogWarning($"   No children found under machine: {machineNodeId}")
                    End If
                Catch ex As Exception
                    _logger.LogWarning($"Error browsing machine {machineNodeId}: {ex.Message}")
                End Try
            Next
            
            If foundNodes.Count = 0 Then
                _logger.LogWarning("No ProgramManagement.Ctrl nodes found")
                Return 0
            End If
            
            ' Subscribe to found nodes
            _logger.LogInfo($"🔔 Subscribing to {foundNodes.Count} ProgramManagement.Ctrl nodes...")
            Dim successCount = 0
            
            For Each nodeId In foundNodes
                Try
                    If SubscribeToNode(nodeId) Then
                        successCount += 1
                        _logger.LogInfo($"✅ Subscribed to ProgramManagement.Ctrl: {nodeId}")
                    Else
                        _logger.LogWarning($"❌ Failed to subscribe to: {nodeId}")
                    End If
                Catch ex As Exception
                    _logger.LogError($"Error subscribing to {nodeId}: {ex.Message}")
                End Try
            Next
            
            _logger.LogInfo($"✅ Successfully subscribed to {successCount}/{foundNodes.Count} ProgramManagement.Ctrl nodes")
            Return successCount
            
        Catch ex As Exception
            _logger.LogError($"Error during ProgramManagement.Ctrl node discovery: {ex.Message}")
            Return 0
        End Try
    End Function
    
    ''' <summary>
    ''' Print all values in a MachineConfig node
    ''' </summary>
    Private Async Function PrintMachineConfigValues(machineConfigNodeId As String) As Task
        Try
            _logger.LogInfo($"📋 Reading MachineConfig values for: {machineConfigNodeId}")
            
            ' Browse MachineConfig children
            Dim configChildren = BrowseNodes(machineConfigNodeId)
            If configChildren IsNot Nothing Then
                For Each configChild In configChildren
                    Try
                        ' Read the value of each config item
                        Dim value = Await ReadNodeValue(configChild)
                        If value IsNot Nothing Then
                            _logger.LogInfo($"   {configChild} = {value}")
                        Else
                            _logger.LogInfo($"   {configChild} = <null>")
                        End If
                    Catch ex As Exception
                        _logger.LogWarning($"   Error reading {configChild}: {ex.Message}")
                    End Try
                Next
            End If
            
        Catch ex As Exception
            _logger.LogError($"Error printing MachineConfig values for {machineConfigNodeId}: {ex.Message}")
        End Try
    End Function
    
    ''' <summary>
    ''' Read a value from an OPC UA node
    ''' </summary>
    Public Async Function ReadNodeValue(nodeId As String) As Task(Of Object)
        If Not IsConnected Then
            _logger.LogWarning($"Cannot read node {nodeId}: Not connected to OPC UA server")
            Return Nothing
        End If

        Try
            Dim nodeToRead = New ReadValueId() With {
                .NodeId = New NodeId(nodeId),
                .AttributeId = Attributes.Value
            }

            Dim nodesToRead As New ReadValueIdCollection From {nodeToRead}
            Dim results As DataValueCollection = Nothing
            Dim diagnosticInfos As DiagnosticInfoCollection = Nothing

            Await Task.Run(Sub()
                _session.Read(Nothing, 0, TimestampsToReturn.Both, nodesToRead, results, diagnosticInfos)
            End Sub)

            If results IsNot Nothing AndAlso results.Count > 0 Then
                Dim result = results(0)
                If StatusCode.IsGood(result.StatusCode) Then
                    Return result.Value
                Else
                    _logger.LogDebug($"Failed to read node {nodeId}: {result.StatusCode}")
                    Return Nothing
                End If
            Else
                _logger.LogDebug($"No results returned when reading node {nodeId}")
                Return Nothing
            End If

        Catch ex As Exception
            _logger.LogDebug($"Error reading node {nodeId}: {ex.Message}")
            Return Nothing
        End Try
    End Function
    
    ''' <summary>
    ''' Write a value to an OPC UA node
    ''' </summary>
    Public Async Function WriteNodeValue(nodeId As String, value As Object) As Task(Of Boolean)
        If Not IsConnected Then
            _logger.LogWarning($"Cannot write to node {nodeId}: Not connected to OPC UA server")
            Return False
        End If

        Try
            Dim dataValue As New DataValue()
            dataValue.Value = value
            
            Dim writeValue As New WriteValue() With {
                .NodeId = New NodeId(nodeId),
                .AttributeId = Attributes.Value,
                .Value = dataValue
            }

            Dim writeValueCollection As New WriteValueCollection From {writeValue}
            Dim results As StatusCodeCollection = Nothing
            Dim diagnosticInfos As DiagnosticInfoCollection = Nothing

            Await Task.Run(Sub()
                _session.Write(Nothing, writeValueCollection, results, diagnosticInfos)
            End Sub)

            If results IsNot Nothing AndAlso results.Count > 0 Then
                If StatusCode.IsGood(results(0)) Then
                    _logger.LogDebug($"Successfully wrote value {value} to node {nodeId}")
                    Return True
                Else
                    ' Check if it's a BadTypeMismatch (expected for DateTime conversions)
                    If results(0).ToString().Contains("BadTypeMismatch") Then
                        _logger.LogDebug($"Type mismatch for node {nodeId}: {results(0)} (this is expected for DateTime conversions)")
                    Else
                        _logger.LogError($"Failed to write to node {nodeId}: {results(0)}")
                    End If
                    Return False
                End If
            Else
                _logger.LogError($"No results returned when writing to node {nodeId}")
                Return False
            End If

        Catch ex As Exception
            _logger.LogError($"Error writing to node {nodeId}: {ex.Message}")
            Return False
        End Try
    End Function
#End Region

#Region "Private Methods"
    ''' <summary>
    ''' Initialize application configuration
    ''' </summary>
    Private Async Function InitializeApplicationConfigurationAsync() As Task
        _applicationConfiguration = New ApplicationConfiguration() With {
            .ApplicationName = "Okuma Machine Monitor",
            .ApplicationType = ApplicationType.Client,
            .ApplicationUri = Utils.Format("urn:{0}:OkumaMachineMonitor", Utils.GetHostName()),
            .SecurityConfiguration = New SecurityConfiguration With {
                .ApplicationCertificate = New CertificateIdentifier With {
                    .StoreType = "Directory",
                    .StorePath = ".\certificates\own",
                    .SubjectName = "CN=OkumaMachineMonitor"
                },
                .TrustedPeerCertificates = New CertificateTrustList With {
                    .StoreType = "Directory",
                    .StorePath = ".\certificates\trusted"
                },
                .RejectedCertificateStore = New CertificateTrustList With {
                    .StoreType = "Directory",
                    .StorePath = ".\certificates\rejected"
                },
                .AutoAcceptUntrustedCertificates = True,
                .RejectSHA1SignedCertificates = False,
                .RejectUnknownRevocationStatus = False,
                .MinimumCertificateKeySize = 1024
            },
            .TransportQuotas = New TransportQuotas With {
                .OperationTimeout = 30000,
                .MaxStringLength = 1048576,
                .MaxByteStringLength = 1048576,
                .MaxArrayLength = 65536,
                .MaxMessageSize = 8388608,
                .MaxBufferSize = 131072
            },
            .ClientConfiguration = New ClientConfiguration With {
                .DefaultSessionTimeout = 60000
            }
        }
        
        Try
            CreateCertificateDirectories()
            
            ' Set certificate validation handler to application configuration
            AddHandler _applicationConfiguration.CertificateValidator.CertificateValidation, AddressOf OnCertificateValidation
            
            ' Validate and create certificate if needed
            Await _applicationConfiguration.Validate(ApplicationType.Client)
            
            ' Check if certificate exists, if not create it
            If _applicationConfiguration.SecurityConfiguration.ApplicationCertificate IsNot Nothing Then
                Dim hasCertificate = Await _applicationConfiguration.SecurityConfiguration.ApplicationCertificate.Find(True)
                If hasCertificate Is Nothing Then
                    _logger.LogInfo("🔐 Creating application certificate...")
                    
                    ' Create a self-signed certificate (using the working method from ConnectedFactory)
                    hasCertificate = CertificateFactory.CreateCertificate(
                        _applicationConfiguration.SecurityConfiguration.ApplicationCertificate.StoreType,
                        _applicationConfiguration.SecurityConfiguration.ApplicationCertificate.StorePath,
                        Nothing,
                        _applicationConfiguration.ApplicationUri,
                        _applicationConfiguration.ApplicationName,
                        _applicationConfiguration.SecurityConfiguration.ApplicationCertificate.SubjectName,
                        Nothing,
                        2048,
                        DateTime.UtcNow.AddDays(-1),
                        24,
                        256,
                        False,
                        Nothing,
                        Nothing
                    )
                    _applicationConfiguration.SecurityConfiguration.ApplicationCertificate.Certificate = hasCertificate
                    _logger.LogInfo("✅ Application certificate created successfully")
                Else
                    _logger.LogInfo("🔐 Using existing application certificate")
                End If
            End If
            
        Catch ex As Exception
            _logger.LogWarning($"Certificate configuration warning: {ex.Message}")
        End Try
    End Function
    
    ''' <summary>
    ''' Create subscription for monitoring nodes
    ''' </summary>
    Private Sub CreateSubscription()
        Try
            If _session Is Nothing OrElse Not _session.Connected Then
                Return
            End If
            
            _subscription = New Subscription(_session.DefaultSubscription) With {
                .PublishingEnabled = True,
                .PublishingInterval = _connectionSettings.PublishingIntervalMs,
                .KeepAliveCount = 10,
                .LifetimeCount = 1000,
                .MaxNotificationsPerPublish = 1000,
                .Priority = 0
            }
            
            _session.AddSubscription(_subscription)
            _subscription.Create()
            
            _logger.LogInfo("📊 Subscription created successfully")
            
        Catch ex As Exception
            _logger.LogError($"Error creating subscription: {ex.Message}")
        End Try
    End Sub
    
    ''' <summary>
    ''' Restore all previously subscribed nodes after reconnection
    ''' </summary>
    Private Sub RestoreSubscriptions()
        Try
            If _subscribedNodeIds.Count = 0 Then
                _logger.LogDebug("No previous subscriptions to restore")
                Return
            End If
            
            _logger.LogInfo($"🔄 Restoring {_subscribedNodeIds.Count} subscriptions after reconnect...")
            
            Dim restoredCount = 0
            Dim nodeIdsCopy = _subscribedNodeIds.ToList() ' Create copy to avoid modification during iteration
            
            For Each nodeId In nodeIdsCopy
                Try
                    ' Clear the old entry first
                    If _nodeSubscriptions.ContainsKey(nodeId) Then
                        _nodeSubscriptions.Remove(nodeId)
                    End If
                    
                    ' Re-subscribe to the node
                    If InternalSubscribeToNode(nodeId) Then
                        restoredCount += 1
                        _logger.LogDebug($"✅ Restored subscription: {nodeId}")
                    Else
                        _logger.LogWarning($"❌ Failed to restore subscription: {nodeId}")
                    End If
                    
                Catch ex As Exception
                    _logger.LogError($"Error restoring subscription for {nodeId}: {ex.Message}")
                End Try
            Next
            
            _logger.LogInfo($"🎉 Restored {restoredCount}/{_subscribedNodeIds.Count} subscriptions successfully")
            
        Catch ex As Exception
            _logger.LogError($"Error during subscription restoration: {ex.Message}")
        End Try
    End Sub
    
    ''' <summary>
    ''' Internal subscribe method without adding to _subscribedNodeIds (to avoid duplicates during restore)
    ''' </summary>
    Private Function InternalSubscribeToNode(nodeId As String) As Boolean
        Try
            If Not IsConnected OrElse _subscription Is Nothing Then
                Return False
            End If
            
            Dim monitoredItem = New MonitoredItem(_subscription.DefaultItem) With {
                .StartNodeId = nodeId,
                .AttributeId = Attributes.Value,
                .SamplingInterval = _connectionSettings.DefaultSamplingIntervalMs,
                .QueueSize = 10,
                .DiscardOldest = True
            }
            
            AddHandler monitoredItem.Notification, AddressOf OnMonitoredItemNotification
            
            _subscription.AddItem(monitoredItem)
            _subscription.ApplyChanges()
            
            _nodeSubscriptions(nodeId) = monitoredItem
            Return True
            
        Catch ex As Exception
            _logger.LogError($"Failed to internally subscribe to node {nodeId}: {ex.Message}")
            Return False
        End Try
    End Function
    
    ''' <summary>
    ''' Handle session keep alive events
    ''' </summary>
    Private Sub OnSessionKeepAlive(sender As Object, e As KeepAliveEventArgs)
        Try
            If e.Status IsNot Nothing AndAlso StatusCode.IsNotGood(e.Status.StatusCode) Then
                _logger.LogWarning($"Keep alive failed: {e.Status.StatusCode}")
                
                If Not _isReconnecting Then
                    _isReconnecting = True
                    Task.Run(AddressOf ReconnectAsync)
                End If
            End If
        Catch ex As Exception
            _logger.LogError($"Error in keep alive handler: {ex.Message}")
        End Try
    End Sub
    
    ''' <summary>
    ''' Handle session closing events
    ''' </summary>
    Private Sub OnSessionClosing(sender As Object, e As EventArgs)
        _logger.LogWarning("🔌 Session is closing")
        _isConnected = False
        RaiseEvent ConnectionStatusChanged(False)
    End Sub
    
    ''' <summary>
    ''' Handle monitored item notifications
    ''' </summary>
    Private Sub OnMonitoredItemNotification(sender As Object, e As MonitoredItemNotificationEventArgs)
        Try
            Dim monitoredItem = TryCast(sender, MonitoredItem)
            If monitoredItem Is Nothing Then Return
            
            Dim dataChange = TryCast(e.NotificationValue, MonitoredItemNotification)
            If dataChange IsNot Nothing Then
                Dim nodeId = monitoredItem.StartNodeId.ToString()
                Dim value = dataChange.Value.Value
                Dim timestamp = dataChange.Value.SourceTimestamp
                
                _logger.LogInfo($"📡 Data received: {nodeId} = {value} at {timestamp:HH:mm:ss.fff}")
                
                RaiseEvent DataReceived(nodeId, value, timestamp)
            End If
            
        Catch ex As Exception
            _logger.LogError($"Error in monitored item notification handler: {ex.Message}")
        End Try
    End Sub
    
    ''' <summary>
    ''' Reconnect to the server
    ''' </summary>
    Private Async Function ReconnectAsync() As Task
        Try
            _logger.LogInfo("🔄 Attempting to reconnect...")
            
            CleanupSession()
            
            ' Wait before reconnecting
            Await Task.Delay(_connectionSettings.ReconnectIntervalSeconds * 1000)
            
            Dim success = Await ConnectAsync()
            If success Then
                _logger.LogInfo("✅ Reconnection successful")
            Else
                _logger.LogWarning("❌ Reconnection failed, will retry...")
                ' Setup timer for next reconnection attempt
                _reconnectTimer = New Timer(AddressOf OnReconnectTimer, Nothing, _connectionSettings.ReconnectIntervalSeconds * 1000, Timeout.Infinite)
            End If
            
        Catch ex As Exception
            _logger.LogError($"Error during reconnection: {ex.Message}")
        Finally
            _isReconnecting = False
        End Try
    End Function
    
    ''' <summary>
    ''' Timer callback for reconnection attempts
    ''' </summary>
    Private Sub OnReconnectTimer(state As Object)
        If Not _isReconnecting Then
            Task.Run(AddressOf ReconnectAsync)
        End If
    End Sub
    
    ''' <summary>
    ''' Create certificate directories
    ''' </summary>
    Private Sub CreateCertificateDirectories()
        Try
            Dim directories() As String = {".\certificates\own", ".\certificates\trusted", ".\certificates\rejected"}
            For Each dir As String In directories
                If Not IO.Directory.Exists(dir) Then
                    IO.Directory.CreateDirectory(dir)
                    _logger.LogDebug($"Created certificate directory: {dir}")
                End If
            Next
        Catch ex As Exception
            _logger.LogWarning($"Failed to create certificate directories: {ex.Message}")
        End Try
    End Sub
    
    ''' <summary>
    ''' Handle certificate validation
    ''' </summary>
    Private Sub OnCertificateValidation(sender As Object, e As CertificateValidationEventArgs)
        Try
            ' For development purposes, accept all certificates
            ' In production, you should implement proper certificate validation
            e.Accept = True
            
            ' Only log certificate validation once per certificate subject
            Static acceptedCertificates As New HashSet(Of String)
            Dim certSubject = e.Certificate.Subject
            
            If Not acceptedCertificates.Contains(certSubject) Then
                acceptedCertificates.Add(certSubject)
                _logger.LogInfo($"🔐 Certificate accepted: {certSubject}")
                
                If e.Error IsNot Nothing AndAlso e.Error.StatusCode <> StatusCodes.Good Then
                    _logger.LogDebug($"Certificate validation info: {e.Error} (auto-accepted for development)")
                End If
            End If
        Catch ex As Exception
            _logger.LogError($"Certificate validation error: {ex.Message}")
            e.Accept = True ' Accept anyway for development
        End Try
    End Sub
    
    ''' <summary>
    ''' Cleanup session and subscriptions
    ''' </summary>
    Private Sub CleanupSession()
        Try
            _isConnected = False
            
            If _subscription IsNot Nothing Then
                _subscription.Delete(True)
                _subscription.Dispose()
                _subscription = Nothing
            End If
            
            If _session IsNot Nothing Then
                _session.Close()
                _session.Dispose()
                _session = Nothing
            End If
            
            ' Clear current subscriptions but keep _subscribedNodeIds for reconnect
            _nodeSubscriptions.Clear()
            ' NOTE: We DON'T clear _subscribedNodeIds here so we can restore them on reconnect
            
        Catch ex As Exception
            _logger.LogError($"Error during session cleanup: {ex.Message}")
        End Try
    End Sub
#End Region

#Region "IDisposable"
    Public Sub Dispose() Implements IDisposable.Dispose
        If Not _disposed Then
            _reconnectTimer?.Dispose()
            CleanupSession()
            _subscribedNodeIds.Clear() ' Clear on final dispose
            _disposed = True
        End If
    End Sub
#End Region

End Class

End Namespace