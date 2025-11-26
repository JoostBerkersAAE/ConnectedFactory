Imports System.Collections.Generic
Imports System.Text.Json.Serialization

Namespace Models

''' <summary>
''' Root API configuration model
''' </summary>
Public Class ApiConfiguration
    
    <JsonPropertyName("Configurations")>
    Public Property Configurations As Dictionary(Of String, MachineTypeConfiguration)
    
    Public Sub New()
        Configurations = New Dictionary(Of String, MachineTypeConfiguration)()
    End Sub
    
    ''' <summary>
    ''' Get total count of all configuration items
    ''' </summary>
    Public Function GetTotalConfigurationCount() As Integer
        Dim total = 0
        If Configurations IsNot Nothing Then
            For Each machineType In Configurations.Values
                Dim seriesConfigs = machineType.GetSeriesConfigurations()
                If seriesConfigs IsNot Nothing Then
                    For Each series In seriesConfigs.Values
                        If series.General IsNot Nothing Then
                            total += series.General.Count
                        End If
                        If series.Custom IsNot Nothing Then
                            total += series.Custom.Count
                        End If
                    Next
                End If
            Next
        End If
        Return total
    End Function
    
End Class

''' <summary>
''' Machine type configuration (e.g., MC)
''' </summary>
Public Class MachineTypeConfiguration
    
    Public Property P300 As SeriesConfiguration
    
    ' Add other series as needed
    ' Public Property P200 As SeriesConfiguration
    ' Public Property P500 As SeriesConfiguration
    
    Public Sub New()
        P300 = New SeriesConfiguration()
    End Sub
    
    ''' <summary>
    ''' Get all series configurations as a dictionary for compatibility
    ''' </summary>
    Public Function GetSeriesConfigurations() As Dictionary(Of String, SeriesConfiguration)
        Dim dict = New Dictionary(Of String, SeriesConfiguration)()
        If P300 IsNot Nothing Then
            dict("P300") = P300
        End If
        ' Add other series here when needed
        Return dict
    End Function
    
End Class

''' <summary>
''' Series configuration (e.g., P300)
''' </summary>
Public Class SeriesConfiguration
    
    <JsonPropertyName("General")>
    Public Property General As List(Of ApiConfigurationItem)
    
    <JsonPropertyName("Custom")>
    Public Property Custom As List(Of ApiConfigurationItem)
    
    Public Sub New()
        General = New List(Of ApiConfigurationItem)()
        Custom = New List(Of ApiConfigurationItem)()
    End Sub
    
End Class

''' <summary>
''' Individual API configuration item
''' </summary>
Public Class ApiConfigurationItem
    
    <JsonPropertyName("ApiName")>
    Public Property ApiName As String
    
    <JsonPropertyName("Type")>
    Public Property Type As String
    
    <JsonPropertyName("SubsystemIndex")>
    Public Property SubsystemIndex As Integer
    
    <JsonPropertyName("MajorIndex")>
    Public Property MajorIndex As Integer
    
    <JsonPropertyName("MinorIndex")>
    Public Property MinorIndex As Integer
    
    <JsonPropertyName("StyleCode")>
    Public Property StyleCode As Integer?
    
    <JsonPropertyName("Subscript")>
    Public Property Subscript As Integer
    
    <JsonPropertyName("DataFieldName")>
    Public Property DataFieldName As String
    
    <JsonPropertyName("DataFieldDescription")>
    Public Property DataFieldDescription As String
    
    <JsonPropertyName("DataType")>
    Public Property DataType As String
    
    <JsonPropertyName("CollectionIntervalMs")>
    Public Property CollectionIntervalMs As Integer
    
    <JsonPropertyName("Enabled")>
    Public Property Enabled As Boolean
    
    <JsonPropertyName("MinimumChangeThreshold")>
    Public Property MinimumChangeThreshold As Double
    
End Class

End Namespace


