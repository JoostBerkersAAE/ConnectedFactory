Namespace Models

''' <summary>
''' OPC UA connection settings
''' </summary>
Public Class OpcUaConnectionSettings
    
    Public Property ServerUrl As String = "opc.tcp://localhost:4840/AAE/MachineServer"
    Public Property Username As String = ""
    Public Property Password As String = ""
    Public Property ReconnectIntervalSeconds As Integer = 5
    Public Property PublishingIntervalMs As Integer = 1000
    Public Property DefaultSamplingIntervalMs As Integer = 1000
    Public Property MaxReconnectAttempts As Integer = 0 ' 0 = infinite
    Public Property EnableDetailedLogging As Boolean = True
    Public Property IntervalMultiplier As Double = 1.0
    
    ''' <summary>
    ''' Create default settings
    ''' </summary>
    Public Shared Function CreateDefault() As OpcUaConnectionSettings
        Return New OpcUaConnectionSettings()
    End Function
    
End Class

End Namespace


