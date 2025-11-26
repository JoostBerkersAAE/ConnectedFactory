Imports System.Threading.Tasks
Imports OkumaConnect.Services.Logging

Namespace Services.DataCollection

    ''' <summary>
    ''' Interface for routing data collection requests
    ''' </summary>
    Public Interface IDataRouter
        Function ProcessDataRequest(nodeId As String, requestType As String) As Task
    End Interface

End Namespace
