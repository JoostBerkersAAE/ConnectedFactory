# OkumaConnect - OPC UA Configuration Monitor

A clean, well-structured OPC UA client for monitoring Okuma machine configurations. This application reads `api_config.json` configurations and monitors OPC UA server changes in real-time.

## 🏗️ Project Structure

```
OkumaConnect/
├── Core/                           # Core application logic
│   ├── ApplicationContext.vb       # Application context container
│   ├── ApplicationInitializer.vb   # Application initialization
│   └── MonitorManager.vb          # Main monitor manager
├── Services/                       # Service layer
│   ├── Configuration/              # Configuration services
│   │   └── ConfigurationManager.vb # Config file management
│   ├── Logging/                    # Logging services
│   │   ├── ILogger.vb             # Logger interface
│   │   └── ConsoleLogger.vb       # Console + file logger
│   └── OpcClient/                  # OPC UA client services
│       └── OpcUaManager.vb        # OPC UA connection & monitoring
├── Models/                         # Data models
│   ├── ApiConfiguration.vb        # API config models
│   └── OpcUaConnectionSettings.vb # OPC UA settings
├── config/                         # Configuration files
│   ├── api_config.json            # API configuration
│   └── env.example                # Environment variables example
├── certificates/                   # OPC UA certificates
├── logs/                          # Log files
├── Program.vb                     # Main entry point
└── OkumaConnect.vbproj           # Project file
```

## 🚀 Features

### ✅ Implemented Features

1. **📋 Configuration Management**
   - Automatic loading of `api_config.json`
   - File system watcher for configuration changes
   - Environment variable support
   - Configuration file validation

2. **🔗 OPC UA Client**
   - Automatic connection to OPC UA server
   - Node discovery and subscription management
   - Automatic reconnection on connection loss
   - Certificate management

3. **📊 Real-time Monitoring**
   - Live data reception from OPC UA nodes
   - Configuration-based node subscriptions
   - Event-driven architecture

4. **📝 Comprehensive Logging**
   - Colored console output
   - Automatic file logging
   - Multiple log levels (INFO, WARNING, ERROR, DEBUG)
   - Timestamped log entries

## ⚙️ Configuration

### Environment Variables

Copy `config/env.example` to `.env` and adjust the values:

```env
# OPC UA Server
OPCUA_SERVER_URL=opc.tcp://your-server:4840/YourPath
OPCUA_USERNAME=your_username
OPCUA_PASSWORD=your_password

# Connection Settings
OPCUA_RECONNECT_INTERVAL_SECONDS=10
OPCUA_PUBLISHING_INTERVAL_MS=1000
OPCUA_DEFAULT_SAMPLING_INTERVAL_MS=1000
```

### API Configuration

The `config/api_config.json` contains the Okuma API configuration:

```json
{
  "Configurations": {
    "MC": {
      "P300": {
        "General": [
          {
            "ApiName": "WorkCounterA_Counted",
            "Type": "general",
            "SubsystemIndex": 0,
            "MajorIndex": 3066,
            "MinorIndex": 0,
            "StyleCode": 8,
            "DataType": "float",
            "CollectionIntervalMs": 5000,
            "Enabled": true
          }
        ]
      }
    }
  }
}
```

## 🔧 Installation & Usage

### Requirements

- .NET 9.0 SDK
- OPC UA server (Okuma machine or simulator)

### Installation

1. **Clone the project:**
   ```bash
   cd OkumaConnect
   ```

2. **Restore NuGet packages:**
   ```bash
   dotnet restore
   ```

3. **Configuration setup:**
   ```bash
   # Copy environment example
   copy config\env.example .env
   
   # Edit .env with your OPC UA server details
   notepad .env
   ```

4. **Build the project:**
   ```bash
   dotnet build
   ```

### Running

```bash
dotnet run
```

### Output Example

```
=== OkumaConnect - OPC UA Configuration Monitor ===
Started: 2024-11-04 14:30:15
Monitoring api_config.json and OPC UA server changes
Press Ctrl+C to stop

🔧 [1/4] Initializing logging system...
🔧 [2/4] Loading configuration...
📋 Loading API configuration from: config\api_config.json
✅ API configuration loaded successfully - 3 items
🔗 OPC UA settings loaded - Server: opc.tcp://your-server:4840/YourPath
🔧 [3/4] Validating configuration...
🔧 [4/4] Initializing monitor manager...
✅ Application initialization completed

🚀 Starting OkumaConnect monitor...
🔧 OPC UA Manager initialized
✅ OPC UA Manager initialized
🔗 Connecting to OPC UA server...
🔐 Creating application certificate...
✅ Successfully connected to OPC UA server
📊 Subscription created successfully
✅ Connected to OPC UA server
📡 Setting up configuration node subscriptions...
📡 Subscribed to node: ns=0;i=3066
📡 Subscribed to node: ns=0;i=1006
📡 Subscribed to node: ns=0;i=2034
✅ Subscribed to 3 configuration nodes
🔄 Starting monitoring loop...
Press Ctrl+C to stop monitoring

📊 Data: ns=0;i=3066 = 125.5 [14:30:25.123]
📊 Data: ns=0;i=1006 = 1 [14:30:26.456]
```

## 🔍 Logging

### Log Locations

- **Console:** Real-time output with colors
- **File:** `logs/okuma_connect_YYYYMMDD.log`

### Log Levels

- **INFO:** Normal operations and status updates
- **WARNING:** Potential issues
- **ERROR:** Errors requiring attention
- **DEBUG:** Detailed debugging information

## 🛠️ Development

### Code Structure Principles

1. **Separation of Concerns:** Each service has a specific responsibility
2. **Dependency Injection:** Services are injected via constructors
3. **Event-Driven:** Use of events for loose coupling
4. **Error Handling:** Comprehensive exception handling at all levels
5. **Logging:** Extensive logging for debugging and monitoring

### Extensions

To add new functionality:

1. **New Services:** Add to `Services/` folder
2. **Data Models:** Add to `Models/` folder
3. **Configuration:** Extend `ConfigurationManager`
4. **Monitoring:** Extend `MonitorManager`

## 🔧 Troubleshooting

### Common Issues

1. **Connection failed:**
   - Check OPC UA server URL in `.env`
   - Verify network connectivity
   - Check certificate configuration

2. **Configuration not loaded:**
   - Check `api_config.json` syntax
   - Verify file paths
   - Check file permissions

3. **No data received:**
   - Check node IDs in configuration
   - Verify `Enabled: true` in config items
   - Check OPC UA server node structure

### Debug Mode

For detailed logging, set environment variable:
```env
OPCUA_ENABLE_DETAILED_LOGGING=true
```

## 🔄 Updates

The application automatically monitors:
- `api_config.json` changes
- OPC UA server connection status
- Node data changes

Configuration changes automatically update subscriptions without restart.

## 📄 License

This project is provided as-is for monitoring Okuma machine configurations via OPC UA.
