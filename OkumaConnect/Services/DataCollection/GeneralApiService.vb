Imports System
Imports System.Threading.Tasks
Imports OkumaConnect.Models
Imports OkumaConnect.Services.Logging

Namespace Services.DataCollection

    ''' <summary>
    ''' General API service for collecting machine data using ClassOspApi.GetByString
    ''' </summary>
    Public Class GeneralApiService
        Implements IGeneralApiService

        Private ReadOnly _logger As ILogger

        Public Sub New(logger As ILogger)
            _logger = logger
        End Sub

        ''' <summary>
        ''' Get data from General API using ClassOspApi.GetByString
        ''' </summary>
        Public Async Function GetDataAsync(nodeId As String, machineConnection As ClassOspApi, apiConfig As ApiConfigurationItem) As Task(Of ApiDataResult) Implements IGeneralApiService.GetDataAsync
            Try
                _logger.LogInfo($"📊 General API: Collecting real data for {nodeId}")
                _logger.LogInfo($"   API Config: {apiConfig.ApiName} - SubSystem:{apiConfig.SubsystemIndex}, Major:{apiConfig.MajorIndex}, Minor:{apiConfig.MinorIndex}, Style:{apiConfig.StyleCode}, Subscript:{apiConfig.Subscript}")
                
                ' Validate inputs
                If machineConnection Is Nothing Then
                    Throw New ArgumentNullException("machineConnection", "Machine connection is required")
                End If
                
                If apiConfig Is Nothing Then
                    Throw New ArgumentNullException("apiConfig", "API configuration is required")
                End If
                
                If Not apiConfig.Enabled Then
                    _logger.LogWarning($"⚠️ API configuration {apiConfig.ApiName} is disabled")
                    Return New ApiDataResult() With {
                        .Success = False,
                        .ErrorMessage = "API configuration is disabled",
                        .Value = 0
                    }
                End If
                
                ' Call ClassOspApi.GetByString with configuration parameters
                Dim rawValue As String = Nothing
                Await Task.Run(Sub()
                    _logger.LogDebug($"🔧 Calling GetByString({apiConfig.SubsystemIndex}, {apiConfig.MajorIndex}, {apiConfig.Subscript}, {apiConfig.MinorIndex}, {apiConfig.StyleCode})")
                    rawValue = machineConnection.GetByString(
                        CShort(apiConfig.SubsystemIndex),
                        CShort(apiConfig.MajorIndex),
                        CShort(apiConfig.Subscript),
                        CShort(apiConfig.MinorIndex),
                        CShort(apiConfig.StyleCode)
                    )
                    _logger.LogDebug($"🔧 GetByString result: '{rawValue}'")
                    _logger.LogDebug($"🔧 Error info - Result: '{machineConnection.Result}', ErrMsg: '{machineConnection.ErrMsg}', ErrData: {machineConnection.ErrData}")
                End Sub)
                
                ' Check for API errors
                If Not String.IsNullOrEmpty(machineConnection.ErrMsg) Then
                    _logger.LogError($"❌ API Error for {nodeId}: {machineConnection.ErrMsg} (Code: {machineConnection.Result}, Data: {machineConnection.ErrData})")
                    Return New ApiDataResult() With {
                        .Success = False,
                        .ErrorMessage = $"API Error: {machineConnection.ErrMsg} (Code: {machineConnection.Result})",
                        .Value = 0
                    }
                End If
                
                ' Convert raw string value to appropriate data type
                Dim convertedValue As Object = ConvertValueByDataType(rawValue, apiConfig.DataType, apiConfig.ApiName)
                
                Dim result = New ApiDataResult(convertedValue, "real") With {
                    .Success = True
                }
                
                _logger.LogInfo($"✅ General API: Retrieved value {convertedValue} for {nodeId} ({apiConfig.ApiName})")
                
                Return result
                
            Catch ex As Exception
                _logger.LogError($"❌ General API error for {nodeId}: {ex.Message}")
                _logger.LogError($"   Stack trace: {ex.StackTrace}")
                
                Return New ApiDataResult() With {
                    .Success = False,
                    .ErrorMessage = ex.Message,
                    .Value = 0
                }
            End Try
        End Function

        ''' <summary>
        ''' Convert raw string value to appropriate data type based on configuration
        ''' Handles Okuma API strings with leading/trailing spaces
        ''' </summary>
        Private Function ConvertValueByDataType(rawValue As String, dataType As String, apiName As String) As Object
            Try
                If String.IsNullOrEmpty(rawValue) Then
                    _logger.LogWarning($"⚠️ Empty value received for {apiName}")
                    Return 0
                End If
                
                ' Clean the raw value - Okuma API often returns values with leading/trailing spaces
                Dim cleanValue = rawValue.Trim()
                _logger.LogDebug($"🔄 Converting '{rawValue}' → '{cleanValue}' to {dataType} for {apiName}")
                
                If String.IsNullOrEmpty(cleanValue) Then
                    _logger.LogWarning($"⚠️ Empty value after trimming for {apiName}")
                    Return 0
                End If
                
                Select Case dataType?.ToLower()
                    Case "float", "double", "decimal"
                        Dim doubleValue As Double
                        If Double.TryParse(cleanValue, doubleValue) Then
                            Return doubleValue
                        Else
                            _logger.LogWarning($"⚠️ Could not parse '{cleanValue}' as float for {apiName}")
                            Return 0.0
                        End If
                        
                    Case "int", "integer", "long"
                        Dim intValue As Integer
                        If Integer.TryParse(cleanValue, intValue) Then
                            Return intValue
                        Else
                            _logger.LogWarning($"⚠️ Could not parse '{cleanValue}' as integer for {apiName}")
                            Return 0
                        End If
                        
                    Case "bool", "boolean"
                        Dim boolValue As Boolean
                        If Boolean.TryParse(cleanValue, boolValue) Then
                            Return boolValue
                        Else
                            ' Try numeric conversion (0 = false, non-zero = true)
                            Dim numValue As Double
                            If Double.TryParse(cleanValue, numValue) Then
                                Return numValue <> 0
                            Else
                                _logger.LogWarning($"⚠️ Could not parse '{cleanValue}' as boolean for {apiName}")
                                Return False
                            End If
                        End If
                        
                    Case "string", "text"
                        ' For strings, return the cleaned value (trimmed)
                        Return cleanValue
                        
                    Case Else
                        ' Default to cleaned string if data type is unknown
                        _logger.LogWarning($"⚠️ Unknown data type '{dataType}' for {apiName}, returning as cleaned string")
                        Return cleanValue
                End Select
                
            Catch ex As Exception
                _logger.LogError($"❌ Error converting value '{rawValue}' for {apiName}: {ex.Message}")
                Return 0
            End Try
        End Function

    End Class

End Namespace

