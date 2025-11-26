# OkumaConnect - Architectuur Documentatie

## 🏗️ Overzicht

OkumaConnect is ontworpen als een modulaire, event-driven applicatie voor het monitoren van Okuma machine configuraties via OPC UA. De architectuur volgt clean code principes en separation of concerns.

## 📐 Architectuur Diagram

```
┌─────────────────────────────────────────────────────────────┐
│                    OkumaConnect                             │
├─────────────────────────────────────────────────────────────┤
│  Program.vb (Entry Point)                                  │
│  └── ApplicationInitializer                                │
│      └── ApplicationContext                                │
├─────────────────────────────────────────────────────────────┤
│                      Core Layer                             │
│  ┌─────────────────┐  ┌─────────────────┐                 │
│  │ MonitorManager  │  │ ApplicationCtx  │                 │
│  │ - Orchestrates  │  │ - DI Container  │                 │
│  │ - Coordinates   │  │ - Lifecycle     │                 │
│  └─────────────────┘  └─────────────────┘                 │
├─────────────────────────────────────────────────────────────┤
│                    Services Layer                           │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────┐ │
│  │ Configuration   │  │    OpcClient    │  │   Logging   │ │
│  │ - Config Mgmt   │  │ - OPC UA Conn   │  │ - Console   │ │
│  │ - File Watch    │  │ - Subscriptions │  │ - File Log  │ │
│  │ - Validation    │  │ - Reconnection  │  │ - Levels    │ │
│  └─────────────────┘  └─────────────────┘  └─────────────┘ │
├─────────────────────────────────────────────────────────────┤
│                     Models Layer                            │
│  ┌─────────────────┐  ┌─────────────────┐                 │
│  │ ApiConfiguration│  │ OpcUaSettings   │                 │
│  │ - JSON Models   │  │ - Connection    │                 │
│  │ - Validation    │  │ - Timeouts      │                 │
│  └─────────────────┘  └─────────────────┘                 │
├─────────────────────────────────────────────────────────────┤
│                 External Dependencies                       │
│  ┌─────────────────┐  ┌─────────────────┐                 │
│  │   OPC UA SDK    │  │  File System    │                 │
│  │ - OPC Foundation│  │ - Config Files  │                 │
│  │ - Certificates  │  │ - Log Files     │                 │
│  └─────────────────┘  └─────────────────┘                 │
└─────────────────────────────────────────────────────────────┘
```

## 🔄 Data Flow

### 1. Startup Flow
```
Program.Main()
    ↓
ApplicationInitializer.Initialize()
    ↓
┌─ Logger Setup
├─ ConfigurationManager.LoadConfiguration()
│   ├─ Load api_config.json
│   ├─ Load OPC UA settings
│   └─ Setup file watchers
├─ Validation
└─ MonitorManager.New()
    ↓
MonitorManager.StartAsync()
    ├─ Initialize OpcUaManager
    ├─ Connect to OPC UA server
    ├─ Subscribe to config nodes
    └─ Start monitoring loop
```

### 2. Configuration Change Flow
```
File System Change (api_config.json)
    ↓
FileSystemWatcher.Changed Event
    ↓
ConfigurationManager.OnConfigurationFileChanged()
    ↓
ConfigurationManager.LoadConfiguration()
    ↓
ConfigurationChanged Event
    ↓
MonitorManager.OnConfigurationChanged()
    ↓
Update OPC UA Subscriptions
```

### 3. OPC UA Data Flow
```
OPC UA Server Data Change
    ↓
OpcUaManager.OnMonitoredItemNotification()
    ↓
DataReceived Event
    ↓
MonitorManager.OnOpcUaDataReceived()
    ↓
Logger.LogInfo() + Processing Logic
```

## 🧩 Component Details

### Core Layer

#### ApplicationInitializer
- **Verantwoordelijkheid:** Bootstrap de applicatie
- **Dependencies:** Geen
- **Lifecycle:** Eenmalig bij startup
- **Key Methods:**
  - `Initialize()`: Setup alle services in juiste volgorde

#### ApplicationContext
- **Verantwoordelijkheid:** Dependency container en lifecycle management
- **Dependencies:** Alle services
- **Lifecycle:** Hele applicatie levensduur
- **Key Methods:**
  - `Cleanup()`: Resource cleanup bij shutdown

#### MonitorManager
- **Verantwoordelijkheid:** Orchestratie van monitoring proces
- **Dependencies:** ConfigurationManager, OpcUaManager, Logger
- **Lifecycle:** Hele monitoring sessie
- **Key Methods:**
  - `StartAsync()`: Start monitoring proces
  - `Stop()`: Graceful shutdown

### Services Layer

#### ConfigurationManager
- **Verantwoordelijkheid:** Configuratie laden en beheren
- **Features:**
  - JSON deserialization
  - File system watching
  - Environment variable support
  - Configuration validation
- **Events:** `ConfigurationChanged`

#### OpcUaManager
- **Verantwoordelijkheid:** OPC UA client operaties
- **Features:**
  - Connection management
  - Certificate handling
  - Subscription management
  - Automatic reconnection
- **Events:** `ConnectionStatusChanged`, `DataReceived`, `ErrorOccurred`

#### ConsoleLogger
- **Verantwoordelijkheid:** Logging naar console en bestand
- **Features:**
  - Colored console output
  - File rotation
  - Thread-safe logging
  - Multiple log levels

### Models Layer

#### ApiConfiguration
- **Verantwoordelijkheid:** Strongly-typed configuratie modellen
- **Features:**
  - JSON serialization attributes
  - Validation methods
  - Hierarchical structure (MC → P300 → General/Custom)

#### OpcUaConnectionSettings
- **Verantwoordelijkheid:** OPC UA verbinding parameters
- **Features:**
  - Default values
  - Environment variable mapping
  - Validation

## 🔧 Design Patterns

### 1. Dependency Injection
```vb
' Constructor injection
Public Sub New(configurationManager As ConfigurationManager, logger As ILogger)
    _configurationManager = configurationManager
    _logger = logger
End Sub
```

### 2. Observer Pattern
```vb
' Events voor loose coupling
Public Event ConfigurationChanged()
Public Event DataReceived(nodeId As String, value As Object, timestamp As DateTime)
```

### 3. Factory Pattern
```vb
' Default object creation
Public Shared Function CreateDefault() As OpcUaConnectionSettings
    Return New OpcUaConnectionSettings()
End Function
```

### 4. Strategy Pattern
```vb
' Interface-based logging
Public Interface ILogger
    Sub LogInfo(message As String)
    Sub LogError(message As String)
End Interface
```

## 🛡️ Error Handling Strategy

### 1. Layered Exception Handling
- **Application Level:** Global exception handler in Program.Main()
- **Service Level:** Service-specific exception handling
- **Operation Level:** Method-level try-catch blocks

### 2. Graceful Degradation
```vb
' Fallback naar defaults bij configuratie fouten
If configPath Is Nothing Then
    _logger.LogWarning("api_config.json not found, using defaults")
    _apiConfiguration = CreateDefaultApiConfiguration()
    Return
End If
```

### 3. Retry Logic
```vb
' Automatische reconnectie bij OPC UA verbindingsverlies
Private Async Function ReconnectAsync() As Task
    ' Exponential backoff retry logic
End Function
```

## 📊 Performance Considerations

### 1. Asynchronous Operations
- OPC UA verbindingen zijn async
- File I/O operaties zijn async waar mogelijk
- Non-blocking monitoring loop

### 2. Resource Management
- IDisposable implementatie voor alle services
- Proper cleanup van OPC UA sessions
- File handle management voor logging

### 3. Memory Management
- Event handler cleanup bij dispose
- Dictionary cleanup voor subscriptions
- Timer disposal

## 🔒 Security Considerations

### 1. OPC UA Security
- Certificate-based authentication
- Secure channel encryption
- Certificate validation

### 2. Configuration Security
- Environment variables voor gevoelige data
- No hardcoded credentials
- File permission checks

## 🧪 Testing Strategy

### 1. Unit Testing
- Service layer unit tests
- Model validation tests
- Configuration parsing tests

### 2. Integration Testing
- OPC UA connection tests
- File system watcher tests
- End-to-end monitoring tests

### 3. Mock Strategy
```vb
' Interface-based mocking
Public Interface ILogger
Public Interface IOpcUaManager
```

## 🔄 Extensibility

### 1. Adding New Services
1. Create interface in Services folder
2. Implement concrete class
3. Register in ApplicationInitializer
4. Inject dependencies via constructor

### 2. Adding New Configuration Types
1. Extend ApiConfiguration model
2. Update ConfigurationManager parsing
3. Add validation logic

### 3. Adding New Data Processors
1. Subscribe to DataReceived event
2. Implement processing logic
3. Add to MonitorManager

## 📈 Monitoring & Observability

### 1. Logging Levels
- **DEBUG:** Detailed execution flow
- **INFO:** Normal operations
- **WARNING:** Potential issues
- **ERROR:** Actual problems

### 2. Key Metrics
- Connection uptime
- Data reception rate
- Configuration reload frequency
- Error rates

### 3. Health Checks
- OPC UA connection status
- Configuration file validity
- Certificate expiration
- Disk space for logs


