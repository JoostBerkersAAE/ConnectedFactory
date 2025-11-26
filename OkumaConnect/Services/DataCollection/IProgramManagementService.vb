Imports System.Threading.Tasks
Imports OkumaConnect.Models

Namespace Services.DataCollection

    ''' <summary>
    ''' Interface for Program Management data collection
    ''' </summary>
    Public Interface IProgramManagementService
        Function GetDataAsync(nodeId As String) As Task(Of ApiDataResult)
    End Interface

End Namespace

