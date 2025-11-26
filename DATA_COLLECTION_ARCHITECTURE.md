# OkumaConnect Data Collection Architecture

## Overview

OkumaConnect is a Windows-based service that bridges Okuma CNC machines with cloud infrastructure through a sophisticated data collection architecture. The system uses **OPC UA** as the trigger mechanism while maintaining **persistent OSPAPI connections** to the physical machines for actual data retrieval.

> **Critical Design Pattern**: The OPC UA server acts as the **control plane** (triggering data collection), while OSPAPI connections serve as the **data plane** (retrieving actual machine data). This separation prevents connection instability and blue screen issues.

---

## Architecture Components

### 1. OPC UA Server (Control Plane)
- **Purpose**: Trigger mechanism and configuration storage
- **Connection**: Single persistent connection to OPC UA server
- **Responsibilities**:
  - Store machine configurations (IP addresses, machine IDs)
  - Provide trigger nodes (`.extract` nodes) that signal when data should be collected
  - Store timestamps for incremental data collection
  - Receive collected data for storage/forwarding

### 2. OSPAPI Connections (Data Plane)
- **Purpose**: Direct communication with Okuma CNC machines
- **Connection**: Multiple persistent connections (one per machine)
- **Responsibilities**:
  - Retrieve real-time machine data
  - Access MacMan historical data
  - Execute machine commands (program management)

### 3. DataRouter (Connection Manager)
- **Purpose**: Centralized connection management and request routing
- **Responsibilities**:
  - Establish and maintain OSPAPI connections
  - Route data requests to appropriate services
  - Monitor connection health
  - Update connection status in OPC UA

---

## Connection Management Strategy

### Why Persistent Connections?

**Problem**: Creating new OSPAPI connections for each data request causes:
- Connection overhead and latency
- Resource exhaustion on the machine controller
- System instability (blue screens)
- Failed data collection attempts

**Solution**: Maintain persistent OSPAPI connections throughout the application lifecycle.

### Connection Lifecycle

```
Application Start
    ↓
Initialize OPC UA Connection
    ↓
Discover Machines from OPC UA
    ↓
Establish OSPAPI Connection per Machine
    ↓
Keep Connections Open
    ↓
Use Existing Connections for All Data Requests
    ↓
Application Shutdown → Disconnect All
```

### Connection Establishment Process

#### Step 1: OPC UA Discovery
```vb
' Browse OPC UA server to discover machines
Dim machineNodes = _opcuaManager.BrowseNodes("ns=2;s=Okuma.Machines")

' For each machine, read configuration
Dim machineIP = ReadNodeValue("ns=2;s=Okuma.Machines.{machineName}.MachineConfig.IPAddress")
```

#### Step 2: OSPAPI Connection
```vb
' Create ClassOspApi instance
Dim ospApi = New ClassOspApi()

' Connect to machine (this connection stays open!)
ospApi.ConnectData(machineIP, ClassOspApi.NCTYPE.TYPE_MC, True)

' Store connection for reuse
_machineConnections(machineName) = ospApi
```

#### Step 3: Connection Validation
```vb
' Check connection success
If String.IsNullOrEmpty(ospApi.ErrMsg) AndAlso 
   (String.IsNullOrEmpty(ospApi.Result) OrElse ospApi.Result = "0") Then
    
    ' Update OPC UA with connection status
    UpdateConnectionStatus(machineName, True)
    
    ' Connection is now ready for data collection
End If
```

### Connection Reuse Pattern

**Critical**: Every data collection request uses the **same connection instance**:

```vb
' ❌ WRONG - Creates new connection each time
Dim ospApi = New ClassOspApi()
ospApi.ConnectData(machineIP, ...)
Dim value = ospApi.GetByString(...)
ospApi.DisconnectData()

' ✅ CORRECT - Reuses existing connection
Dim ospApi = _machineConnections(machineName)  ' Get existing connection
Dim value = ospApi.GetByString(...)            ' Use it
' Connection stays open for next request
```

---

## Data Collection Flow

### Functional Overview

1. **OPC UA Subscription**: Application subscribes to trigger nodes
2. **Trigger Detection**: OPC UA server notifies when `.extract` node changes to `True`
3. **Connection Retrieval**: DataRouter retrieves existing OSPAPI connection
4. **Data Collection**: Service uses connection to collect machine data
5. **Data Processing**: Collected data is processed and sent to cloud
6. **Status Update**: OPC UA nodes are updated with results
7. **Trigger Reset**: `.extract` node is reset to `False`

### Technical Flow Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                         OPC UA Server                            │
│  (Persistent Connection - Control Plane)                         │
│                                                                   │
│  Machine Config:                                                 │
│  ├─ IPAddress: "192.168.1.100"                                  │
│  ├─ MachineId: "1"                                              │
│  └─ Enabled: True                                               │
│                                                                   │
│  Data Nodes:                                                     │
│  ├─ .Data.WorkCounter.extract = True  ← Trigger!               │
│  ├─ .Data.WorkCounter.value = 0                                │
│  └─ .Data.WorkCounter.lastupdated = 0                          │
└─────────────────────────────────────────────────────────────────┘
                            ↓
                    Notification Event
                            ↓
┌─────────────────────────────────────────────────────────────────┐
│                      MonitorManager                              │
│  OnOpcUaDataReceived(nodeId, value, timestamp)                  │
│                                                                   │
│  If nodeId.Contains(".extract") And value = True Then            │
│      Task.Run(Async Function()                                   │
│          Await _dataRouter.ProcessDataRequest(nodeId, "extract") │
│      End Function)                                               │
│  End If                                                          │
└─────────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────────┐
│                        DataRouter                                │
│  ProcessDataRequest(nodeId, requestType)                         │
│                                                                   │
│  1. Extract machine name from nodeId                             │
│  2. Get existing OSPAPI connection:                              │
│     machineApi = _machineConnections(machineName)                │
│  3. Get API configuration for this data point                    │
│  4. Route to appropriate service                                 │
└─────────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────────┐
│                    GeneralApiService                             │
│  GetDataAsync(nodeId, machineConnection, apiConfig)              │
│                                                                   │
│  ' Use EXISTING connection - no new connection created!          │
│  rawValue = machineConnection.GetByString(                       │
│      apiConfig.SubsystemIndex,                                   │
│      apiConfig.MajorIndex,                                       │
│      apiConfig.Subscript,                                        │
│      apiConfig.MinorIndex,                                       │
│      apiConfig.StyleCode                                         │
│  )                                                               │
│                                                                   │
│  ' Connection remains open for next request                      │
└─────────────────────────────────────────────────────────────────┘
                            ↓
                    Data Retrieved
                            ↓
┌─────────────────────────────────────────────────────────────────┐
│                   OSPAPI Connection                              │
│  (Persistent Connection - Data Plane)                            │
│                                                                   │
│  ┌─────────────────────────────────────────┐                   │
│  │  Okuma CNC Machine (192.168.1.100)      │                   │
│  │                                          │                   │
│  │  OSPAPI Interface:                       │                   │
│  │  ├─ GetByString() → Returns value       │                   │
│  │  ├─ SetByString() → Sets value          │                   │
│  │  └─ StartUpdate() → MacMan data sync    │                   │
│  │                                          │                   │
│  │  Connection stays OPEN continuously      │                   │
│  └─────────────────────────────────────────┘                   │
└─────────────────────────────────────────────────────────────────┘
                            ↓
                    Update OPC UA
                            ↓
┌─────────────────────────────────────────────────────────────────┐
│                      OPC UA Server                               │
│  (Results written back)                                          │
│                                                                   │
│  Data Nodes Updated:                                             │
│  ├─ .Data.WorkCounter.extract = False  ← Reset trigger         │
│  ├─ .Data.WorkCounter.value = 42       ← New value             │
│  └─ .Data.WorkCounter.lastupdated = 1234567890                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## Connection Management Implementation

### DataRouter: Connection Pool

The `DataRouter` class maintains a connection pool:

```vb
' Connection pool - one connection per machine
Private ReadOnly _machineConnections As New Dictionary(Of String, ClassOspApi)()
Private ReadOnly _connectionLock As New Object()

' Thread-safe connection retrieval
Private Function EnsureMachineConnection(machineName As String) As Task(Of ClassOspApi)
    ' Check if already connected
    If _machineConnections.ContainsKey(machineName) Then
        Return _machineConnections(machineName)  ' Reuse existing
    End If
    
    ' Thread-safe connection establishment
    SyncLock _connectionLock
        If Not _machineConnections.ContainsKey(machineName) Then
            ' Connect and store
            Dim ospApi = Await ConnectToMachine(machineName)
            _machineConnections(machineName) = ospApi
        End If
    End SyncLock
    
    Return _machineConnections(machineName)
End Function
```

### Connection Initialization

Connections are established at application startup:

```vb
Public Async Function InitializeMachineConnections() As Task
    ' Only initialize if OPC UA is connected
    If Not _opcuaManager.IsConnected Then
        Return
    End If
    
    ' Discover machines from OPC UA
    Dim discoveredMachines = DiscoverMachinesFromOpcUa()
    
    ' Connect to each machine
    For Each machineName In discoveredMachines
        Try
            Dim machineApi = Await EnsureMachineConnection(machineName)
            If machineApi IsNot Nothing Then
                _logger.LogInfo($"✅ Connected to {machineName}")
            End If
        Catch ex As Exception
            _logger.LogError($"❌ Failed to connect to {machineName}: {ex.Message}")
            ' Update disconnected status
            Await UpdateConnectionStatus(machineName, False)
        End Try
    Next
End Function
```

### Connection Status Tracking

Connection status is tracked in OPC UA:

```vb
Private Async Function UpdateConnectionStatus(machineName As String, isConnected As Boolean) As Task
    Dim timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
    
    If isConnected Then
        ' Update Connected timestamp
        Dim connectedNodeId = $"ns=2;s=Okuma.Machines.{machineName}.Connected"
        Await _opcuaManager.WriteNodeValue(connectedNodeId, CInt(timestamp))
        
        ' Reset DisConnected to 0
        Dim disconnectedNodeId = $"ns=2;s=Okuma.Machines.{machineName}.DisConnected"
        Await _opcuaManager.WriteNodeValue(disconnectedNodeId, CInt(0))
    Else
        ' Update DisConnected timestamp
        Dim disconnectedNodeId = $"ns=2;s=Okuma.Machines.{machineName}.DisConnected"
        Await _opcuaManager.WriteNodeValue(disconnectedNodeId, CInt(timestamp))
        
        ' Reset Connected to 0
        Dim connectedNodeId = $"ns=2;s=Okuma.Machines.{machineName}.Connected"
        Await _opcuaManager.WriteNodeValue(connectedNodeId, CInt(0))
    End If
End Function
```

---

## OSPAPI Technical Details

### Connection Establishment

```vb
Public Sub ConnectData(ByVal Remote As String, ByVal NcType As NCTYPE, ByVal UseRemoteAPI As Boolean)
    Dim ProgId As String
    
    ' Determine COM ProgID based on machine type
    If UseRemoteAPI Then
        If m_NcType = NCTYPE.TYPE_MC Then
            ProgId = "RXOSPAPI.DATAM"  ' Machining Center
        ElseIf m_NcType = NCTYPE.TYPE_LATHE Then
            ProgId = "RXOSPAPI.DATAL"  ' Lathe
        Else
            ProgId = "Rxobj"           ' Grinder
        End If
    End If
    
    ' Create COM object (this establishes the connection)
    m_obj = CreateObject(ProgId, Remote)
    
    ' Connection is now established and ready to use
End Sub
```

### Data Retrieval

```vb
Public Function GetByString(ByVal SubSystem As Short, ByVal MajorIndex As Short, 
                           ByVal SubScript As Short, ByVal MinorIndex As Short, 
                           ByVal Style As Short) As String
    ' Clear previous errors
    ClearError()
    
    ' For MacMan data, trigger update first
    If (SubSystem = 1) Or (SubSystem = 33) Or (SubSystem = 34) Then
        m_obj.StartUpdate(0, 1)
    End If
    
    ' Get value using existing connection
    value = m_obj.GetByString(SubSystem, MajorIndex, SubScript, MinorIndex, Style)
    
    ' Check for errors
    Result = m_obj.GetByLastError()
    If Result <> 0 Then
        ErrMsg = m_obj.GetErrMsg(SubSystem, Result)
    End If
    
    Return value
End Function
```

### MacMan Data Collection

MacMan data requires a special update sequence:

```vb
' Step 1: Start MacMan data update (once for all data types)
Dim updateResult = machineApi.StartUpdate(0, 0)

' Step 2: Wait for update to complete
Dim waitResult = machineApi.WaitUpdateEnd()

' Step 3: Collect data using GetByString
' (Connection remains open throughout)
For Each dataType In macManDataTypes
    Dim collector = MacManDataCollectorFactory.CreateCollector(dataType, machineApi, ...)
    Dim records = collector.CollectDataWithoutUpdate(lastTimestamp, batchSize, True)
    
    ' Send to EventHub
    Await EventHubService.SendCollectionResultsAsync(machineId, machineIP, machineName, records)
    
    ' Update OPC UA timestamp
    Await UpdateLastProcessedTimestamps(machineName, newTimestamps)
Next
```

---

## Data Collection Services

### 1. GeneralApiService

**Purpose**: Collect general machine data (counters, status, etc.)

**Process**:
1. Receive trigger from OPC UA (`.extract = True`)
2. Get existing OSPAPI connection from DataRouter
3. Retrieve API configuration (SubsystemIndex, MajorIndex, etc.)
4. Call `GetByString()` on existing connection
5. Convert raw value to appropriate data type
6. Update OPC UA nodes with results

**Key Point**: Uses existing connection - no connection overhead.

### 2. MacManDataService

**Purpose**: Collect historical MacMan screen data

**Process**:
1. Receive trigger from OPC UA (`.MacManData.extract = True`)
2. Read last processed timestamps from OPC UA
3. Get existing OSPAPI connection from DataRouter
4. Perform **single** MacMan update (`StartUpdate` → `WaitUpdateEnd`)
5. Collect data for each MacMan type (alarms, reports, etc.)
6. Send each batch to EventHub immediately
7. Update OPC UA timestamps incrementally
8. Reset extract trigger

**Key Point**: Single update for all data types, connection stays open.

### 3. ProgramManagementService

**Purpose**: Execute program management commands

**Process**:
1. Receive trigger from OPC UA (`.ProgramManagement.Ctrl = True`)
2. Get existing OSPAPI connection from DataRouter
3. Execute program selection commands
4. Update OPC UA status nodes
5. Reset control trigger

**Key Point**: Uses same connection pool as data collection.

---

## Preventing Blue Screens

### Root Cause
Blue screens occur when:
- Too many connection attempts in short time
- Connections not properly released
- Resource exhaustion on machine controller
- Network instability with frequent reconnects

### Prevention Strategies

#### 1. Connection Pooling
```vb
' ✅ Single connection per machine, reused for all requests
Private ReadOnly _machineConnections As New Dictionary(Of String, ClassOspApi)()

' ❌ Creating new connection for each request (causes blue screens)
' For Each request
'     Dim api = New ClassOspApi()
'     api.ConnectData(...)
'     api.GetByString(...)
'     api.DisconnectData()
' Next
```

#### 2. Thread-Safe Access
```vb
' Prevent multiple threads from creating duplicate connections
Private ReadOnly _connectionLock As New Object()

SyncLock _connectionLock
    If Not _machineConnections.ContainsKey(machineName) Then
        ' Only one thread creates connection
        _machineConnections(machineName) = Await ConnectToMachine(machineName)
    End If
End SyncLock
```

#### 3. Connection State Tracking
```vb
' Track connection state to avoid reconnection attempts
Private Function IsMachineConnected(machineName As String) As Boolean
    SyncLock _connectionLock
        Return _machineConnections.ContainsKey(machineName)
    End SyncLock
End Function

' Only connect if not already connected
If Not IsMachineConnected(machineName) Then
    Await EnsureMachineConnection(machineName)
End If
```

#### 4. Proper Cleanup
```vb
' Only disconnect on application shutdown
Public Sub Dispose()
    SyncLock _connectionLock
        For Each kvp In _machineConnections
            Try
                kvp.Value.DisconnectData()
            Catch ex As Exception
                _logger.LogError($"Error disconnecting {kvp.Key}: {ex.Message}")
            End Try
        Next
        _machineConnections.Clear()
    End SyncLock
End Sub
```

#### 5. Error Handling
```vb
' Don't disconnect on errors - keep connection alive
Try
    Dim value = machineApi.GetByString(...)
Catch ex As Exception
    _logger.LogError($"Error getting data: {ex.Message}")
    ' Connection stays open - don't disconnect!
    ' Return error result instead
    Return New ApiDataResult() With {
        .Success = False,
        .ErrorMessage = ex.Message
    }
End Try
```

---

## Configuration Management

### API Configuration (api_config.json)

Defines what data to collect and how:

```json
{
  "Configurations": {
    "MC": {
      "P300": {
        "General": [
          {
            "ApiName": "WorkCounterA_Counted",
            "Type": "General",
            "SubsystemIndex": 2,
            "MajorIndex": 1,
            "MinorIndex": 0,
            "StyleCode": 0,
            "Subscript": 0,
            "DataFieldName": "WorkCounterA_Counted",
            "DataType": "int",
            "CollectionIntervalMs": 1000,
            "Enabled": true
          }
        ]
      }
    }
  }
}
```

### OPC UA Node Structure

```
ns=2;s=Okuma.Machines
├─ 1 - MU-10000H
│  ├─ MachineConfig
│  │  ├─ Enabled
│  │  ├─ IPAddress
│  │  └─ MachineId
│  ├─ Connected (timestamp)
│  ├─ DisConnected (timestamp)
│  ├─ Data
│  │  ├─ WorkCounterA_Counted
│  │  │  ├─ extract (trigger)
│  │  │  ├─ value (result)
│  │  │  └─ lastupdated (timestamp)
│  │  └─ MacManData
│  │     ├─ extract (trigger)
│  │     └─ LastProcessed
│  │        ├─ ALARM_HISTORY_DISPLAY
│  │        ├─ MACHINING_REPORT_DISPLAY
│  │        └─ ...
│  └─ ProgramManagement
│     ├─ Ctrl (trigger)
│     ├─ Stat (status)
│     └─ Exception (error message)
```

---

## Best Practices

### DO ✅

1. **Maintain persistent connections** throughout application lifecycle
2. **Reuse connections** for all data collection requests
3. **Use thread-safe access** to connection pool
4. **Track connection status** in OPC UA
5. **Handle errors gracefully** without disconnecting
6. **Use OPC UA as trigger mechanism** only
7. **Batch MacMan updates** (single update for all types)
8. **Update timestamps incrementally** as data is collected

### DON'T ❌

1. **Don't create new connections** for each request
2. **Don't disconnect** on errors or between requests
3. **Don't use OPC UA** for actual machine data retrieval
4. **Don't perform multiple MacMan updates** for same collection cycle
5. **Don't ignore connection failures** - track and report status
6. **Don't block OPC UA notifications** with long-running operations
7. **Don't forget to cleanup** connections on shutdown
8. **Don't retry connections** too aggressively (causes blue screens)

---

## Troubleshooting

### Blue Screen Issues

**Symptoms**:
- Machine controller crashes
- Blue screen on machine display
- Connection refused errors

**Diagnosis**:
```vb
' Check connection pool size
_logger.LogInfo($"Active connections: {_machineConnections.Count}")

' Check for connection leaks
For Each kvp In _machineConnections
    _logger.LogInfo($"Machine: {kvp.Key}, Connected: {kvp.Value IsNot Nothing}")
Next
```

**Solutions**:
1. Verify connection pooling is working
2. Check for duplicate connection attempts
3. Ensure proper thread synchronization
4. Review error handling - don't disconnect on errors
5. Reduce connection retry frequency

### Connection Failures

**Symptoms**:
- Cannot establish initial connection
- Connection lost during operation
- Timeout errors

**Diagnosis**:
```vb
' Check OPC UA connection status
_logger.LogInfo($"OPC UA Connected: {_opcuaManager.IsConnected}")

' Check machine IP configuration
Dim machineIP = Await _opcuaManager.ReadNodeValue($"ns=2;s=Okuma.Machines.{machineName}.MachineConfig.IPAddress")
_logger.LogInfo($"Machine IP: {machineIP}")

' Check OSPAPI error details
_logger.LogInfo($"OSPAPI Result: {ospApi.Result}")
_logger.LogInfo($"OSPAPI Error: {ospApi.ErrMsg}")
_logger.LogInfo($"OSPAPI ErrData: {ospApi.ErrData}")
```

**Solutions**:
1. Verify network connectivity to machine
2. Check IP address configuration in OPC UA
3. Verify RXOSPAPI.EXE is accessible
4. Check Windows firewall settings
5. Ensure machine controller is responding

### Data Collection Issues

**Symptoms**:
- No data received
- Incorrect data values
- Timestamps not updating

**Diagnosis**:
```vb
' Check trigger detection
_logger.LogInfo($"Extract trigger: {nodeId} = {value}")

' Check connection availability
Dim machineApi = _machineConnections(machineName)
_logger.LogInfo($"Connection available: {machineApi IsNot Nothing}")

' Check API configuration
Dim apiConfig = GetApiConfigurationForNode(nodeId)
_logger.LogInfo($"API Config found: {apiConfig IsNot Nothing}")
If apiConfig IsNot Nothing Then
    _logger.LogInfo($"SubSystem: {apiConfig.SubsystemIndex}, Major: {apiConfig.MajorIndex}")
End If
```

**Solutions**:
1. Verify OPC UA subscription is active
2. Check API configuration matches machine capabilities
3. Verify machine is in correct state for data collection
4. Check OSPAPI error messages
5. Ensure proper data type conversion

---

## Performance Considerations

### Connection Overhead

**Without Connection Pooling**:
- Connection time: ~2-5 seconds per request
- Total time for 10 data points: ~20-50 seconds
- Risk of connection failures and blue screens

**With Connection Pooling**:
- Connection time: ~2-5 seconds (once at startup)
- Data retrieval time: ~100-500ms per request
- Total time for 10 data points: ~1-5 seconds
- Stable, reliable operation

### MacMan Data Collection

**Inefficient Approach** (multiple updates):
```vb
For Each dataType In macManDataTypes
    machineApi.StartUpdate(0, 0)      ' Update 1
    machineApi.WaitUpdateEnd()
    CollectData(dataType)
    
    machineApi.StartUpdate(0, 0)      ' Update 2
    machineApi.WaitUpdateEnd()
    CollectData(dataType)
    ' ... (5 updates total = 10-25 seconds)
Next
```

**Efficient Approach** (single update):
```vb
' Single update for all data types
machineApi.StartUpdate(0, 0)
machineApi.WaitUpdateEnd()

' Collect all data types (2-5 seconds total)
For Each dataType In macManDataTypes
    CollectData(dataType)
    SendToEventHub(data)
    UpdateTimestamp(dataType)
Next
```

### Concurrent Requests

The system handles concurrent requests efficiently:

```vb
' Multiple triggers can fire simultaneously
Task.Run(Async Function()
    ' Each request uses the same connection
    Await _dataRouter.ProcessDataRequest(nodeId1, "extract")
End Function)

Task.Run(Async Function()
    ' Thread-safe connection access
    Await _dataRouter.ProcessDataRequest(nodeId2, "extract")
End Function)
```

---

## Summary

### Key Architectural Decisions

1. **Separation of Concerns**
   - OPC UA: Control plane (triggers, configuration, results)
   - OSPAPI: Data plane (machine communication)

2. **Connection Management**
   - Persistent connections throughout application lifecycle
   - Connection pooling with thread-safe access
   - Status tracking in OPC UA

3. **Trigger-Based Collection**
   - OPC UA subscriptions for real-time triggers
   - Asynchronous request processing
   - Immediate result feedback

4. **Error Resilience**
   - Graceful error handling without disconnecting
   - Connection status monitoring
   - Automatic reconnection on OPC UA level

### Critical Success Factors

1. **Never disconnect OSPAPI connections** between requests
2. **Always reuse existing connections** from the pool
3. **Use thread-safe access** to connection dictionary
4. **Track connection status** and report to OPC UA
5. **Handle errors gracefully** without breaking connections
6. **Batch MacMan updates** for efficiency
7. **Process triggers asynchronously** to avoid blocking

This architecture ensures **stable, reliable, and efficient** data collection from Okuma CNC machines while preventing system instability and blue screen issues.

