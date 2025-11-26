Imports System.Threading.Tasks
Imports OkumaConnect.Models

Namespace Services.DataCollection

    ''' <summary>
    ''' Interface for MacMan data collection
    ''' </summary>
    Public Interface IMacManDataService
        Function GetDataAsync(nodeId As String) As Task(Of ApiDataResult)
    End Interface

End Namespace

