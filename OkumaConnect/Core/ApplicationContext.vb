Imports System
Imports OkumaConnect.Services.Logging
Imports OkumaConnect.Services.Configuration
Imports OkumaConnect.Core

Namespace Core

''' <summary>
''' Application context that holds all initialized services and components
''' </summary>
Public Class ApplicationContext
    Implements IDisposable

    Public Property Logger As ILogger
    Public Property ConfigurationManager As ConfigurationManager
    Public Property Monitor As MonitorManager

    ''' <summary>
    ''' Cleanup all resources
    ''' </summary>
    Public Sub Cleanup()
        Try
            Monitor?.Dispose()
            ConfigurationManager?.Dispose()
            Logger?.LogInfo("Application cleanup completed")
        Catch ex As Exception
            Console.WriteLine($"Error during cleanup: {ex.Message}")
        End Try
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Cleanup()
    End Sub

End Class

End Namespace


