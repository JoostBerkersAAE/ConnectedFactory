Imports System
Imports System.IO

Namespace Services.Logging

''' <summary>
''' Console and file logger implementation
''' </summary>
Public Class ConsoleLogger
    Implements ILogger
    
    Private ReadOnly _logFilePath As String
    Private ReadOnly _lockObject As New Object()
    
    Public Sub New()
        ' Create logs directory if it doesn't exist
        Dim logsDir = "logs"
        If Not Directory.Exists(logsDir) Then
            Directory.CreateDirectory(logsDir)
        End If
        
        ' Set log file path with timestamp
        _logFilePath = Path.Combine(logsDir, $"okuma_connect_{DateTime.Now:yyyyMMdd}.log")
    End Sub
    
    Public Sub LogInfo(message As String) Implements ILogger.LogInfo
        WriteLog("INFO", message, ConsoleColor.White)
    End Sub
    
    Public Sub LogWarning(message As String) Implements ILogger.LogWarning
        WriteLog("WARN", message, ConsoleColor.Yellow)
    End Sub
    
    Public Sub LogError(message As String) Implements ILogger.LogError
        WriteLog("ERROR", message, ConsoleColor.Red)
    End Sub
    
    Public Sub LogDebug(message As String) Implements ILogger.LogDebug
        WriteLog("DEBUG", message, ConsoleColor.Gray)
    End Sub
    
    Private Sub WriteLog(level As String, message As String, color As ConsoleColor)
        Dim timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
        Dim logMessage = $"[{timestamp}] [{level}] {message}"
        
        SyncLock _lockObject
            ' Write to console with color
            Dim originalColor = Console.ForegroundColor
            Console.ForegroundColor = color
            Console.WriteLine(logMessage)
            Console.ForegroundColor = originalColor
            
            ' Write to file
            Try
                File.AppendAllText(_logFilePath, logMessage & Environment.NewLine)
            Catch ex As Exception
                ' If file logging fails, just continue with console logging
                Console.WriteLine($"[WARNING] Failed to write to log file: {ex.Message}")
            End Try
        End SyncLock
    End Sub
    
End Class

End Namespace


