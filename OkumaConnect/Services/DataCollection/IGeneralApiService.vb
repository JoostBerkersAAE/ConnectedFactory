Imports System.Threading.Tasks
Imports OkumaConnect.Models

Namespace Services.DataCollection

    ''' <summary>
    ''' Interface for General API data collection
    ''' </summary>
    Public Interface IGeneralApiService
        Function GetDataAsync(nodeId As String, machineConnection As ClassOspApi, apiConfig As ApiConfigurationItem) As Task(Of ApiDataResult)
    End Interface

End Namespace

