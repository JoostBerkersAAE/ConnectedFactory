Namespace Models

    ''' <summary>
    ''' Result data from API calls
    ''' </summary>
    Public Class ApiDataResult
        Public Property Value As Object
        Public Property Timestamp As Long
        Public Property Success As Boolean
        Public Property ErrorMessage As String
        Public Property DataType As String
        
        Public Sub New()
            Success = True
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        End Sub
        
        Public Sub New(value As Object, Optional dataType As String = "unknown")
            Me.New()
            Me.Value = value
            Me.DataType = dataType
        End Sub
    End Class

End Namespace

