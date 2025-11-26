Imports System
Imports System.Threading.Tasks
Imports OkumaConnect.Core

''' <summary>
''' OkumaConnect - Clean OPC UA Client for Okuma Machine Monitoring
''' Reads api_config.json and monitors OPC UA server for configuration changes
''' </summary>
Module Program
    
    ''' <summary>
    ''' Main entry point
    ''' </summary>
    Sub Main(args As String())
        ' Print application header
        PrintApplicationHeader()
        
        Dim context As ApplicationContext = Nothing
        
        Try
            ' Initialize application
            Dim initializer = New ApplicationInitializer()
            context = initializer.Initialize()
            
            ' Start monitoring (blocking call)
            context.Monitor.StartAsync().Wait()
            
        Catch ex As Exception
            Console.WriteLine($"❌ Application failed to start: {ex.Message}")
            Console.WriteLine($"Details: {ex}")
        Finally
            ' Always cleanup resources
            context?.Cleanup()
        End Try
    End Sub
    
    ''' <summary>
    ''' Print application header
    ''' </summary>
    Private Sub PrintApplicationHeader()
        Console.WriteLine("=== OkumaConnect - OPC UA Configuration Monitor ===")
        Console.WriteLine($"Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
        Console.WriteLine("Monitoring api_config.json and OPC UA server changes")
        Console.WriteLine("Press Ctrl+C to stop")
        Console.WriteLine()
    End Sub
    
End Module


