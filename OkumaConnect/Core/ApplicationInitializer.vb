Imports System
Imports System.IO
Imports OkumaConnect.Services.Logging
Imports OkumaConnect.Services.Configuration
Imports OkumaConnect.Models

Namespace Core

''' <summary>
''' Application initializer that sets up all services and components
''' </summary>
Public Class ApplicationInitializer

    ''' <summary>
    ''' Initialize the application with all required services
    ''' </summary>
    Public Function Initialize() As ApplicationContext
        Dim context = New ApplicationContext()
        
        Try
            ' Step 1: Initialize logging
            Console.WriteLine("🔧 [1/5] Initializing logging system...")
            context.Logger = New ConsoleLogger()
            context.Logger.LogInfo("OkumaConnect starting up...")
            
            ' Step 2: Load environment variables from .env file
            Console.WriteLine("🔧 [2/5] Loading environment variables...")
            EnvironmentLoader.LoadFromEnvFile(context.Logger)
            
            ' Step 3: Initialize configuration manager
            Console.WriteLine("🔧 [3/5] Loading configuration...")
            context.ConfigurationManager = New ConfigurationManager(context.Logger)
            context.ConfigurationManager.LoadConfiguration()
            
            ' Step 4: Validate configuration
            Console.WriteLine("🔧 [4/5] Validating configuration...")
            ValidateConfiguration(context.ConfigurationManager, context.Logger)
            
            ' Step 5: Initialize monitor manager
            Console.WriteLine("🔧 [5/5] Initializing monitor manager...")
            context.Monitor = New MonitorManager(context.ConfigurationManager, context.Logger)
            
            context.Logger.LogInfo("✅ Application initialization completed successfully")
            Console.WriteLine("✅ Application initialization completed")
            Console.WriteLine()
            
            Return context
            
        Catch ex As Exception
            Console.WriteLine($"❌ Initialization failed: {ex.Message}")
            context?.Cleanup()
            Throw
        End Try
    End Function
    
    ''' <summary>
    ''' Validate the loaded configuration
    ''' </summary>
    Private Sub ValidateConfiguration(configManager As ConfigurationManager, logger As ILogger)
        ' Validate OPC UA settings
        Dim opcuaSettings = configManager.GetOpcUaSettings()
        If String.IsNullOrEmpty(opcuaSettings.ServerUrl) Then
            Throw New InvalidOperationException("OPC UA Server URL is required")
        End If
        
        ' Validate API configuration
        Dim apiConfig = configManager.GetApiConfiguration()
        If apiConfig Is Nothing OrElse apiConfig.Configurations Is Nothing Then
            Throw New InvalidOperationException("API configuration is invalid or missing")
        End If
        
        logger.LogInfo($"Configuration validated - OPC UA: {opcuaSettings.ServerUrl}")
        logger.LogInfo($"API Configuration loaded with {apiConfig.GetTotalConfigurationCount()} items")
    End Sub

End Class

End Namespace

