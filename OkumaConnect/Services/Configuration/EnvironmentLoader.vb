Imports System
Imports System.IO
Imports System.Linq
Imports OkumaConnect.Services.Logging

Namespace Services.Configuration

''' <summary>
''' Environment Configuration Loader voor OkumaConnect
''' Laadt .env bestanden en stelt environment variabelen in
''' </summary>
Public Class EnvironmentLoader
    
    ''' <summary>
    ''' Laad .env bestand en stel environment variabelen in
    ''' </summary>
    Public Shared Sub LoadFromEnvFile(logger As ILogger, Optional envFilePath As String = Nothing)
        If String.IsNullOrEmpty(envFilePath) Then
            ' Try multiple locations for .env file
            Dim baseDir = AppDomain.CurrentDomain.BaseDirectory
            
            ' First try to find project root by looking for .vbproj file
            Dim projectRoot As String = Nothing
            Dim currentDir = New DirectoryInfo(baseDir)
            
            ' Walk up the directory tree to find project root
            While currentDir IsNot Nothing
                If currentDir.GetFiles("*.vbproj").Length > 0 Then
                    projectRoot = currentDir.FullName
                    Exit While
                End If
                currentDir = currentDir.Parent
            End While
            
            Dim possiblePaths = New String() {
                If(projectRoot IsNot Nothing, Path.Combine(projectRoot, "config", ".env"), Nothing), ' Project root config/.env (preferred)
                Path.Combine(baseDir, "config", ".env"),           ' Current dir config/.env
                Path.Combine(baseDir, "..", "..", "..", "config", ".env"), ' ../../../config/.env (from bin/Debug/net9.0)
                Path.Combine(baseDir, ".env")                      ' .env (fallback)
            }.Where(Function(p) p IsNot Nothing).ToArray()
            
            For Each path In possiblePaths
                If File.Exists(path) Then
                    envFilePath = path
                    Exit For
                End If
            Next
            
            If String.IsNullOrEmpty(envFilePath) OrElse Not File.Exists(envFilePath) Then
                logger.LogDebug($".env file not found in any of the expected locations")
                logger.LogDebug($"  Tried: {String.Join(", ", possiblePaths)}")
                Return
            End If
        End If
        
        If Not File.Exists(envFilePath) Then
            logger.LogDebug($".env file not found at: {envFilePath}")
            Return
        End If
        
        Try
            logger.LogInfo($"📁 Loading .env file from: {envFilePath}")
            Dim lines() As String = File.ReadAllLines(envFilePath)
            Dim loadedCount = 0
            
            For Each line As String In lines
                line = line.Trim()
                
                ' Skip lege regels en commentaar
                If String.IsNullOrEmpty(line) OrElse line.StartsWith("#") Then
                    Continue For
                End If
                
                ' Parse KEY=VALUE format
                Dim equalIndex As Integer = line.IndexOf("="c)
                If equalIndex > 0 Then
                    Dim key As String = line.Substring(0, equalIndex).Trim()
                    Dim value As String = line.Substring(equalIndex + 1).Trim()
                    
                    ' Verwijder quotes als aanwezig
                    If (value.StartsWith("""") AndAlso value.EndsWith("""")) OrElse 
                       (value.StartsWith("'") AndAlso value.EndsWith("'")) Then
                        value = value.Substring(1, value.Length - 2)
                    End If
                    
                    ' Stel environment variable in voor deze sessie
                    Environment.SetEnvironmentVariable(key, value)
                    loadedCount += 1
                    
                    logger.LogDebug($"  Set {key} = {If(key.ToUpper().Contains("PASSWORD"), "***", value)}")
                End If
            Next
            
            logger.LogInfo($"✅ Loaded {loadedCount} environment variables from .env file")
            
        Catch ex As Exception
            logger.LogError($"❌ Error loading .env file: {ex.Message}")
        End Try
    End Sub
    
    ''' <summary>
    ''' Get environment variable met fallback waarde
    ''' </summary>
    Public Shared Function GetEnvironmentVariable(key As String, defaultValue As String) As String
        Dim value = Environment.GetEnvironmentVariable(key)
        If String.IsNullOrEmpty(value) Then
            Return defaultValue
        End If
        Return value
    End Function
    
End Class

End Namespace
