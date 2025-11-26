Namespace Services.Logging

''' <summary>
''' Interface for logging services
''' </summary>
Public Interface ILogger
    
    Sub LogInfo(message As String)
    Sub LogWarning(message As String)
    Sub LogError(message As String)
    Sub LogDebug(message As String)
    
End Interface

End Namespace


