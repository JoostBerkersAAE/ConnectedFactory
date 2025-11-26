Imports System
Imports System.IO
Imports System.Text.Json
Imports OkumaConnect.Models
Imports OkumaConnect.Services.Logging

Namespace Services.Configuration

''' <summary>
''' Configuration manager for loading and managing API and OPC UA configurations
''' </summary>
Public Class ConfigurationManager
    Implements IDisposable
    
    Private ReadOnly _logger As ILogger
    Private _apiConfiguration As ApiConfiguration
    Private _opcuaSettings As OpcUaConnectionSettings
    Private _configFileWatcher As FileSystemWatcher
    
    Public Event ConfigurationChanged()
    
    Public Sub New(logger As ILogger)
        _logger = logger
        _opcuaSettings = OpcUaConnectionSettings.CreateDefault()
    End Sub
    
    ''' <summary>
    ''' Load all configuration files
    ''' </summary>
    Public Sub LoadConfiguration()
        LoadApiConfiguration()
        LoadOpcUaSettings()
        SetupConfigurationWatcher()
    End Sub
    
    ''' <summary>
    ''' Get the current API configuration
    ''' </summary>
    Public Function GetApiConfiguration() As ApiConfiguration
        Return _apiConfiguration
    End Function
    
    ''' <summary>
    ''' Get the current OPC UA settings
    ''' </summary>
    Public Function GetOpcUaSettings() As OpcUaConnectionSettings
        Return _opcuaSettings
    End Function
    
    ''' <summary>
    ''' Load API configuration from api_config.json
    ''' </summary>
    Private Sub LoadApiConfiguration()
        Try
            ' Search for api_config.json in multiple locations
            Dim configPaths() As String = {
                "config\api_config.json",
                "..\config\api_config.json",
                "..\..\ConnectedFactory\Okuma\config\api_config.json"
            }
            
            Dim configPath As String = Nothing
            For Each path In configPaths
                If File.Exists(path) Then
                    configPath = path
                    Exit For
                End If
            Next
            
            If configPath Is Nothing Then
                _logger.LogWarning("api_config.json not found in any expected location")
                _apiConfiguration = CreateDefaultApiConfiguration()
                Return
            End If
            
            _logger.LogInfo($"📋 Loading API configuration from: {configPath}")
            
            Dim jsonContent = File.ReadAllText(configPath)
            Dim options As New JsonSerializerOptions With {
                .PropertyNameCaseInsensitive = True,
                .AllowTrailingCommas = True
            }
            
            _apiConfiguration = JsonSerializer.Deserialize(Of ApiConfiguration)(jsonContent, options)
            
            If _apiConfiguration Is Nothing Then
                _logger.LogWarning("Failed to deserialize API configuration, using default")
                _apiConfiguration = CreateDefaultApiConfiguration()
            Else
                _logger.LogInfo($"✅ API configuration loaded successfully - {_apiConfiguration.GetTotalConfigurationCount()} items")
                
                ' Debug: Log all loaded configuration items
                _logger.LogDebug("🔍 DEBUG: Loaded API Configuration Items:")
                If _apiConfiguration.Configurations IsNot Nothing Then
                    For Each machineTypeKvp In _apiConfiguration.Configurations
                        _logger.LogDebug($"  Machine Type: {machineTypeKvp.Key}")
                        Dim seriesConfigs = machineTypeKvp.Value.GetSeriesConfigurations()
                        If seriesConfigs IsNot Nothing Then
                            For Each seriesKvp In seriesConfigs
                                _logger.LogDebug($"    Series: {seriesKvp.Key}")
                                If seriesKvp.Value.General IsNot Nothing Then
                                    For Each item In seriesKvp.Value.General
                                        _logger.LogDebug($"      General: {item.ApiName} / {item.DataFieldName}")
                                    Next
                                End If
                                If seriesKvp.Value.Custom IsNot Nothing Then
                                    For Each item In seriesKvp.Value.Custom
                                        _logger.LogDebug($"      Custom: {item.ApiName} / {item.DataFieldName}")
                                    Next
                                End If
                            Next
                        End If
                    Next
                End If
            End If
            
        Catch ex As Exception
            _logger.LogError($"Error loading API configuration: {ex.Message}")
            _apiConfiguration = CreateDefaultApiConfiguration()
        End Try
    End Sub
    
    ''' <summary>
    ''' Load OPC UA settings from environment variables
    ''' </summary>
    Private Sub LoadOpcUaSettings()
        Try
            ' Load from environment variables using the same method as ConnectedFactory
            _opcuaSettings.ServerUrl = EnvironmentLoader.GetEnvironmentVariable("OPCUA_SERVER_URL", "opc.tcp://localhost:4840/AAE/MachineServer")
            _opcuaSettings.Username = EnvironmentLoader.GetEnvironmentVariable("OPCUA_USERNAME", "")
            _opcuaSettings.Password = EnvironmentLoader.GetEnvironmentVariable("OPCUA_PASSWORD", "")
            
            ' Load additional settings
            Dim reconnectInterval = EnvironmentLoader.GetEnvironmentVariable("OPCUA_RECONNECT_INTERVAL_SECONDS", "10")
            If Integer.TryParse(reconnectInterval, _opcuaSettings.ReconnectIntervalSeconds) Then
                ' Successfully parsed
            End If
            
            Dim publishingInterval = EnvironmentLoader.GetEnvironmentVariable("OPCUA_PUBLISHING_INTERVAL_MS", "1000")
            If Integer.TryParse(publishingInterval, _opcuaSettings.PublishingIntervalMs) Then
                ' Successfully parsed
            End If
            
            Dim samplingInterval = EnvironmentLoader.GetEnvironmentVariable("OPCUA_DEFAULT_SAMPLING_INTERVAL_MS", "1000")
            If Integer.TryParse(samplingInterval, _opcuaSettings.DefaultSamplingIntervalMs) Then
                ' Successfully parsed
            End If
            
            Dim maxReconnectAttempts = EnvironmentLoader.GetEnvironmentVariable("OPCUA_MAX_RECONNECT_ATTEMPTS", "0")
            If Integer.TryParse(maxReconnectAttempts, _opcuaSettings.MaxReconnectAttempts) Then
                ' Successfully parsed
            End If
            
            Dim detailedLogging = EnvironmentLoader.GetEnvironmentVariable("OPCUA_ENABLE_DETAILED_LOGGING", "true")
            _opcuaSettings.EnableDetailedLogging = String.Equals(detailedLogging, "true", StringComparison.OrdinalIgnoreCase)
            
            Dim intervalMultiplier = EnvironmentLoader.GetEnvironmentVariable("OPCUA_INTERVAL_MULTIPLIER", "1.0")
            If Double.TryParse(intervalMultiplier, _opcuaSettings.IntervalMultiplier) Then
                ' Successfully parsed
            End If
            
            _logger.LogInfo($"🔗 OPC UA settings loaded - Server: {_opcuaSettings.ServerUrl}")
            _logger.LogInfo($"   Username: {If(String.IsNullOrEmpty(_opcuaSettings.Username), "Anonymous", _opcuaSettings.Username)}")
            _logger.LogInfo($"   Reconnect Interval: {_opcuaSettings.ReconnectIntervalSeconds}s")
            _logger.LogInfo($"   Publishing Interval: {_opcuaSettings.PublishingIntervalMs}ms")
            
        Catch ex As Exception
            _logger.LogError($"Error loading OPC UA settings: {ex.Message}")
        End Try
    End Sub
    
    ''' <summary>
    ''' Setup file system watcher for configuration changes
    ''' </summary>
    Private Sub SetupConfigurationWatcher()
        Try
            Dim configDir = "config"
            If Not Directory.Exists(configDir) Then
                Directory.CreateDirectory(configDir)
            End If
            
            _configFileWatcher = New FileSystemWatcher(configDir, "*.json") With {
                .NotifyFilter = NotifyFilters.LastWrite Or NotifyFilters.Size,
                .EnableRaisingEvents = True
            }
            
            AddHandler _configFileWatcher.Changed, AddressOf OnConfigurationFileChanged
            _logger.LogInfo("📁 Configuration file watcher setup completed")
            
        Catch ex As Exception
            _logger.LogWarning($"Could not setup configuration file watcher: {ex.Message}")
        End Try
    End Sub
    
    ''' <summary>
    ''' Handle configuration file changes
    ''' </summary>
    Private Sub OnConfigurationFileChanged(sender As Object, e As FileSystemEventArgs)
        Try
            _logger.LogInfo($"📝 Configuration file changed: {e.Name}")
            
            ' Reload configuration after a short delay to avoid multiple rapid changes
            Threading.Thread.Sleep(1000)
            LoadConfiguration()
            
            RaiseEvent ConfigurationChanged()
            
        Catch ex As Exception
            _logger.LogError($"Error handling configuration file change: {ex.Message}")
        End Try
    End Sub
    
    ''' <summary>
    ''' Create a default API configuration
    ''' </summary>
    Private Function CreateDefaultApiConfiguration() As ApiConfiguration
        Dim config = New ApiConfiguration()
        
        ' Add a basic MC/P300 configuration
        Dim mcConfig = New MachineTypeConfiguration()
        Dim p300Config = New SeriesConfiguration()
        
        ' Add a sample configuration item
        p300Config.General.Add(New ApiConfigurationItem With {
            .ApiName = "WorkCounterA_Counted",
            .Type = "general",
            .SubsystemIndex = 0,
            .MajorIndex = 3066,
            .MinorIndex = 0,
            .StyleCode = 8,
            .Subscript = 0,
            .DataFieldName = "WorkCounterA_Counted",
            .DataFieldDescription = "Work counter A counted value",
            .DataType = "float",
            .CollectionIntervalMs = 5000,
            .Enabled = True,
            .MinimumChangeThreshold = 0.00001
        })
        
        mcConfig.P300 = p300Config
        config.Configurations("MC") = mcConfig
        
        Return config
    End Function
    
    Public Sub Dispose() Implements IDisposable.Dispose
        _configFileWatcher?.Dispose()
    End Sub
    
End Class

End Namespace

