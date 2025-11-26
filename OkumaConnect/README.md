# OkumaConnect - OPC UA Configuration Monitor

Een schone, goed gestructureerde OPC UA client voor het monitoren van Okuma machine configuraties. Deze applicatie leest `api_config.json` configuraties en monitort OPC UA server wijzigingen in real-time.

## 🏗️ Project Structuur

```
OkumaConnect/
├── Core/                           # Kern applicatie logica
│   ├── ApplicationContext.vb       # Application context container
│   ├── ApplicationInitializer.vb   # Applicatie initialisatie
│   └── MonitorManager.vb          # Hoofd monitor manager
├── Services/                       # Service laag
│   ├── Configuration/              # Configuratie services
│   │   └── ConfigurationManager.vb # Config file management
│   ├── Logging/                    # Logging services
│   │   ├── ILogger.vb             # Logger interface
│   │   └── ConsoleLogger.vb       # Console + file logger
│   └── OpcClient/                  # OPC UA client services
│       └── OpcUaManager.vb        # OPC UA verbinding & monitoring
├── Models/                         # Data modellen
│   ├── ApiConfiguration.vb        # API config modellen
│   └── OpcUaConnectionSettings.vb # OPC UA instellingen
├── config/                         # Configuratie bestanden
│   ├── api_config.json            # API configuratie
│   └── env.example                # Environment variabelen voorbeeld
├── certificates/                   # OPC UA certificaten
├── logs/                          # Log bestanden
├── Program.vb                     # Main entry point
└── OkumaConnect.vbproj           # Project bestand
```

## 🚀 Functionaliteiten

### ✅ Geïmplementeerde Features

1. **📋 Configuratie Management**
   - Automatisch laden van `api_config.json`
   - File system watcher voor configuratie wijzigingen
   - Environment variabelen ondersteuning
   - Validatie van configuratie bestanden

2. **🔗 OPC UA Client**
   - Automatische verbinding met OPC UA server
   - Node discovery en subscription management
   - Automatische reconnectie bij verbindingsverlies
   - Certificaat management

3. **📊 Real-time Monitoring**
   - Live data ontvangst van OPC UA nodes
   - Configuratie-gebaseerde node subscriptions
   - Event-driven architectuur

4. **📝 Comprehensive Logging**
   - Console output met kleuren
   - Automatische file logging
   - Verschillende log levels (INFO, WARNING, ERROR, DEBUG)
   - Timestamped log entries

## ⚙️ Configuratie

### Environment Variabelen

Kopieer `config/env.example` naar `.env` en pas de waarden aan:

```env
# OPC UA Server
OPCUA_SERVER_URL=opc.tcp://localhost:4840/AAE/MachineServer
OPCUA_USERNAME=admin
OPCUA_PASSWORD=AAE@2024!

# Connection Settings
OPCUA_RECONNECT_INTERVAL_SECONDS=10
OPCUA_PUBLISHING_INTERVAL_MS=1000
OPCUA_DEFAULT_SAMPLING_INTERVAL_MS=1000
```

### API Configuratie

De `config/api_config.json` bevat de Okuma API configuratie:

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

## 🔧 Installatie & Gebruik

### Vereisten

- .NET 9.0 SDK
- OPC UA server (Okuma machine of simulator)

### Installatie

1. **Clone het project:**
   ```bash
   cd OkumaConnect
   ```

2. **Restore NuGet packages:**
   ```bash
   dotnet restore
   ```

3. **Configuratie setup:**
   ```bash
   # Kopieer environment voorbeeld
   copy config\env.example .env
   
   # Bewerk .env met jouw OPC UA server details
   notepad .env
   ```

4. **Build het project:**
   ```bash
   dotnet build
   ```

### Uitvoeren

```bash
dotnet run
```

### Output Voorbeeld

```
=== OkumaConnect - OPC UA Configuration Monitor ===
Started: 2024-11-04 14:30:15
Monitoring api_config.json and OPC UA server changes
Press Ctrl+C to stop

🔧 [1/4] Initializing logging system...
🔧 [2/4] Loading configuration...
📋 Loading API configuration from: config\api_config.json
✅ API configuration loaded successfully - 3 items
🔗 OPC UA settings loaded - Server: opc.tcp://localhost:4840/AAE/MachineServer
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

### Log Locaties

- **Console:** Real-time output met kleuren
- **File:** `logs/okuma_connect_YYYYMMDD.log`

### Log Levels

- **INFO:** Normale operaties en status updates
- **WARNING:** Potentiële problemen
- **ERROR:** Fouten die aandacht vereisen
- **DEBUG:** Gedetailleerde debugging informatie

## 🛠️ Ontwikkeling

### Code Structuur Principes

1. **Separation of Concerns:** Elke service heeft een specifieke verantwoordelijkheid
2. **Dependency Injection:** Services worden geïnjecteerd via constructors
3. **Event-Driven:** Gebruik van events voor loose coupling
4. **Error Handling:** Comprehensive exception handling op alle niveaus
5. **Logging:** Uitgebreide logging voor debugging en monitoring

### Uitbreidingen

Om nieuwe functionaliteit toe te voegen:

1. **Nieuwe Services:** Voeg toe aan `Services/` folder
2. **Data Models:** Voeg toe aan `Models/` folder
3. **Configuration:** Extend `ConfigurationManager`
4. **Monitoring:** Extend `MonitorManager`

## 🔧 Troubleshooting

### Veelvoorkomende Problemen

1. **Verbinding mislukt:**
   - Controleer OPC UA server URL in `.env`
   - Verificeer netwerk connectiviteit
   - Check certificaat configuratie

2. **Configuratie niet geladen:**
   - Controleer `api_config.json` syntax
   - Verificeer bestandspaden
   - Check file permissions

3. **Geen data ontvangen:**
   - Controleer node IDs in configuratie
   - Verificeer `Enabled: true` in config items
   - Check OPC UA server node structure

### Debug Mode

Voor gedetailleerde logging, set environment variabele:
```env
OPCUA_ENABLE_DETAILED_LOGGING=true
```

## 📋 Gebaseerd Op

Deze implementatie is gebaseerd op de werkende `ConnectedFactory/Okuma` versie, maar met:
- Schonere code structuur
- Betere separation of concerns
- Uitgebreidere logging
- Meer modulaire architectuur
- Verbeterde error handling

## 🔄 Updates

De applicatie monitort automatisch:
- `api_config.json` wijzigingen
- OPC UA server verbindingsstatus
- Node data wijzigingen

Bij configuratie wijzigingen worden subscriptions automatisch bijgewerkt zonder herstart.


