# EventHub Setup voor OkumaConnect

## Overzicht

OkumaConnect kan nu MacMan data doorsturen naar Azure Event Hub, precies zoals QOkumaConnect dat doet. De implementatie is volledig gebaseerd op de QOkumaConnect EventHub service.

## Configuratie

### 1. Environment Variables

Maak een `.env` bestand in de OkumaConnect root directory met de volgende configuratie:

```env
# Event Hub Configuration (required for EventHub integration)
EVENTHUB_ENABLED=true
EVENTHUB_CONNECTION_STRING=Endpoint=sb://your-namespace.servicebus.windows.net/;SharedAccessKeyName=your-policy;SharedAccessKey=your-key;EntityPath=your-eventhub
EVENTHUB_NAME=machine-data

# Data Collection Configuration
DATA_COLLECTION_ENABLED=true
VERBOSE_LOGGING_ENABLED=false

# OPC UA Configuration
OPCUA_SERVER_URL=opc.tcp://localhost:4840
```

### 2. Connection String Formaten

**Optie 1: Connection string met EntityPath (aanbevolen)**
```
EVENTHUB_CONNECTION_STRING=Endpoint=sb://namespace.servicebus.windows.net/;SharedAccessKeyName=policy;SharedAccessKey=key;EntityPath=eventhub-name
```

**Optie 2: Connection string + aparte Event Hub naam**
```env
EVENTHUB_CONNECTION_STRING=Endpoint=sb://namespace.servicebus.windows.net/;SharedAccessKeyName=policy;SharedAccessKey=key
EVENTHUB_NAME=machine-data
```

## Data Format

De data wordt in exact hetzelfde formaat verzonden als QOkumaConnect:

### MacMan Data Event Format

```json
{
  "machine_id": 1,
  "machine_ip": "10.112.0.20",
  "timestamp": "2025-11-11T12:34:56.789Z",
  "measurement_type": "MACHINING_REPORT_DISPLAY",
  "tags": {
    "machine_name": "Okuma Machine",
    "MainProgramName": "PROGRAM.MIN",
    "ProgramName": "SUB001"
  },
  "fields": {
    "RunningTime": "1066",
    "OperatingTime": "850",
    "CuttingTime": "720",
    "NumberOfWork": "5"
  },
  "ProcessedDate": "2025-11-11T12:34:56.789Z"
}
```

### Event Properties (voor routing/filtering)

Elk event heeft properties die gebruikt kunnen worden voor routing:
- `machine_id`: Machine ID als string
- `machine_ip`: IP adres van de machine
- `machine_name`: Naam van de machine
- `measurement_type`: Type MacMan screen (MACHINING_REPORT_DISPLAY, etc.)

## Implementatie Details

### Geïmplementeerde Features

1. **EventHub Service**: Volledig gekopieerd van QOkumaConnect
2. **MacMan Data Integration**: EventHub calls toegevoegd aan MacManDataService
3. **Exact Same Data Format**: Identieke JSON structuur als QOkumaConnect
4. **Error Handling**: EventHub fouten stoppen data collectie niet
5. **Logging**: Gedetailleerde logging van EventHub operaties

### Data Flow

```
MacMan Data Collection → EventHub Send → OPC UA Timestamp Update
```

1. MacMan data wordt verzameld per screen type
2. Data wordt naar EventHub gestuurd (fire-and-forget)
3. OPC UA timestamps worden bijgewerkt
4. Eventuele EventHub fouten worden gelogd maar stoppen de flow niet

### Ondersteunde MacMan Screen Types

- `ALARM_HISTORY_DISPLAY`
- `MACHINING_REPORT_DISPLAY`
- `NC_STATUS_AT_ALARM_DISPLAY`
- `OPERATING_REPORT_DISPLAY`
- `OPERATION_HISTORY_DISPLAY`

## Testing

1. Start OkumaConnect met EventHub configuratie
2. Trigger MacMan data collectie via OPC UA
3. Controleer logs voor EventHub berichten:
   ```
   📤 Event Hub Message (MacMan Screen - MACHINING_REPORT_DISPLAY):
   JSON: {"machine_id":1,"machine_ip":"10.112.0.20",...}
   ✅ Sent 5 events to EventHub for MACHINING_REPORT_DISPLAY
   ```

## Troubleshooting

### EventHub Disabled
```
⚠️ Event Hub is disabled via configuration
```
→ Zet `EVENTHUB_ENABLED=true` in .env file

### Connection String Missing
```
⚠️ Event Hub connection string not found - Event Hub integration disabled
```
→ Voeg `EVENTHUB_CONNECTION_STRING` toe aan .env file

### EventHub Send Errors
```
❌ Error sending MACHINING_REPORT_DISPLAY to EventHub: [error]
```
→ Controleer connection string en Event Hub configuratie
→ Data collectie gaat door ondanks EventHub fouten
