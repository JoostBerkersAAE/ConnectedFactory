

Public Class ClassOspApi

    Public Enum NCTYPE
        TYPE_LATHE = 0
        TYPE_MC = 1
        TYPE_GRINDER = 2
    End Enum

    Private m_obj As Object
    Private m_obj_com As Object
    Private m_NcType As NCTYPE

    Public Result As String
    Public ErrMsg As String
    Public ErrData As Integer
    Public ErrStr As String

    Public MethodLog As String

    '' http://msdn.microsoft.com/ja-jp/library/hks5e2k6(VS.90).aspx
    '' http://msdn.microsoft.com/en-us/library/hks5e2k6(VS.90).aspx
    Public Sub New()
        Debug.Print("ClassOspApi.New()")
    End Sub

    Protected Overrides Sub Finalize()
        DisconnectData()
        MyBase.Finalize()
    End Sub

    Public Sub ConnectData(ByVal Remote As String, ByVal NcType As NCTYPE, ByVal UseRemoteAPI As Boolean)

        Try


            Dim ProgId As String

            m_NcType = NcType

            DisconnectData()
            If UseRemoteAPI Then

                If m_NcType = NCTYPE.TYPE_MC Then
                    ProgId = "RXOSPAPI.DATAM"
                ElseIf m_NcType = NCTYPE.TYPE_LATHE Then
                    ProgId = "RXOSPAPI.DATAL"
                Else
                    ProgId = "Rxobj"
                End If
            Else
                Remote = ""
                If m_NcType = NCTYPE.TYPE_MC Then
                    ProgId = "NCDatM.OSPDatM"
                ElseIf m_NcType = NCTYPE.TYPE_LATHE Then

                    ProgId = "NCDATL.OSPDatL"
                Else
                    ProgId = "NCDatG.OSPDatG"
                End If
            End If
            'Dim k As NCDATMLib.OSPDatM

            MethodLog = "CreateObject(" + ProgId + "," + Remote + ")"

            m_obj = CreateObject(ProgId, Remote)

        Catch ex As Exception
            ErrMsg = "CreateObject() FAILED. " + ex.Message + vbCrLf
            Throw ex
        End Try

    End Sub

    Public Sub DisconnectData()
        If Not (m_obj Is Nothing) Then
            System.Runtime.InteropServices.Marshal.ReleaseComObject(m_obj)
            m_obj = Nothing
        End If
    End Sub
    Public Sub DisconnectCommand()
        If Not (m_obj_com Is Nothing) Then
            System.Runtime.InteropServices.Marshal.ReleaseComObject(m_obj_com)
            m_obj_com = Nothing
        End If
    End Sub

    Private Sub ClearError()
        Result = 0
        ErrMsg = ""
        ErrData = 0
        ErrStr = ""
    End Sub

    Private Sub SetMethodLog(ByVal Method As String, ByVal ss As Short, ByVal maj As Integer, ByVal scr As Integer, ByVal min As Integer, ByVal sty As Short, Optional ByVal value As String = "")
        MethodLog = Method + "(" + ss.ToString() + "," + maj.ToString() + "," + scr.ToString() + "," _
                                 + min.ToString() + "," + sty.ToString()
        If value.Length > 0 Then
            MethodLog += ("," + value + ")")
        Else
            MethodLog += ")"
        End If

    End Sub

    Public Function GetByString(ByVal SubSystem As Short, ByVal MajorIndex As Short, ByVal SubScript As Short, ByVal MinorIndex As Short, ByVal Style As Short) As String

        Try
            Dim value As String = ""

            ClearError()

            SetMethodLog("GetByString", SubSystem, MajorIndex, SubScript, MinorIndex, Style)

            ' MacMan Data SubSystem?
            If (SubSystem = 1) Or (SubSystem = 33) Or (SubSystem = 34) Then
                m_obj.StartUpdate(0, 1)
            End If

            value = m_obj.GetByString(SubSystem, MajorIndex, SubScript, MinorIndex, Style)
            Result = m_obj.GetByLastError()
            If Result <> 0 Then
                ErrMsg = m_obj.GetErrMsg(SubSystem, Result)
                ErrData = m_obj.GetErrData(SubSystem, Result)
                ErrStr = m_obj.GetErrStr(SubSystem, Result)
            End If

            Return value

        Catch ex As Exception
            ErrMsg = "GetByString() FAILED. " + ex.Message
            Throw ex
        End Try

    End Function

    Public Function SetByString(ByVal SubSystem As Short, ByVal MajorIndex As Short, ByVal SubScript As Short, ByVal MinorIndex As Short, ByVal Style As Short, ByVal Value As String) As Integer

        Try
            ClearError()

            SetMethodLog("SetByString", SubSystem, MajorIndex, SubScript, MinorIndex, Style, Value)

            '-----------------------------------------
            ' Release interlock for setting.
            m_obj.NcRunInterLockRelease(1)

            Result = m_obj.SetByString(SubSystem, MajorIndex, SubScript, MinorIndex, Style, Value)
            If Result <> 0 Then
                ErrMsg = m_obj.GetErrMsg(SubSystem, Result)
                ErrData = m_obj.GetErrData(SubSystem, Result)
                ErrStr = m_obj.GetErrStr(SubSystem, Result)
            End If

            Return Result

        Catch ex As Exception
            ErrMsg = "SetByString() FAILED. " + Err.Description
            Throw ex
        End Try

    End Function

    Public Function AddByString(ByVal SubSystem As Short, ByVal MajorIndex As Short, ByVal SubScript As Short, ByVal MinorIndex As Short, ByVal Style As Short, ByVal Value As String) As Integer

        Try
            ClearError()

            SetMethodLog("AddByString", SubSystem, MajorIndex, SubScript, MinorIndex, Style, Value)

            '-----------------------------------------
            ' Release interlock for setting.
            m_obj.NcRunInterLockRelease(1)

            Result = m_obj.AddByString(SubSystem, MajorIndex, SubScript, MinorIndex, Style, Value)
            If Result <> 0 Then
                ErrMsg = m_obj.GetErrMsg(SubSystem, Result)
                ErrData = m_obj.GetErrData(SubSystem, Result)
                ErrStr = m_obj.GetErrStr(SubSystem, Result)
            End If

            Return Result

        Catch ex As Exception
            ErrMsg = "AddByString() FAILED. " + Err.Description
            Throw ex
        End Try

    End Function

    Public Function CalByString(ByVal SubSystem As Short, ByVal MajorIndex As Short, ByVal SubScript As Short, ByVal MinorIndex As Short, ByVal Style As Short, ByVal Value As String) As Integer

        Try
            ClearError()

            SetMethodLog("CalByString", SubSystem, MajorIndex, SubScript, MinorIndex, Style, Value)

            '-----------------------------------------
            ' Release interlock for setting.
            m_obj.NcRunInterLockRelease(1)

            Result = m_obj.CalByString(SubSystem, MajorIndex, SubScript, MinorIndex, Style, Value)
            If Result <> 0 Then
                ErrMsg = m_obj.GetErrMsg(SubSystem, Result)
                ErrData = m_obj.GetErrData(SubSystem, Result)
                ErrStr = m_obj.GetErrStr(SubSystem, Result)
            End If

            Return Result

        Catch ex As Exception
            ErrMsg = "CalByString() FAILED. " + Err.Description
            Throw ex
        End Try

    End Function

    Public Function GetByString2(ByVal SubSystem As Short, ByVal MajorIndex As Integer, ByVal SubScript As Integer, ByVal MinorIndex As Integer, ByVal Style As Short) As String

        Try
            Dim value As String = ""

            ClearError()

            SetMethodLog("GetByString2", SubSystem, MajorIndex, SubScript, MinorIndex, Style)

            ' MacMan Data SubSystem?
            If (SubSystem = 1) Or (SubSystem = 33) Or (SubSystem = 34) Then
                m_obj.StartUpdate(0, 1)
            End If

            value = m_obj.GetByString2(SubSystem, MajorIndex, SubScript, MinorIndex, Style)
            Result = m_obj.GetByLastError()
            If Result <> 0 Then
                ErrMsg = m_obj.GetErrMsg(SubSystem, Result)
                ErrData = m_obj.GetErrData(SubSystem, Result)
                ErrStr = m_obj.GetErrStr(SubSystem, Result)
            End If

            Return value

        Catch ex As Exception
            ErrMsg = "GetByString2() FAILED. " + Err.Description
            Throw ex
        End Try

    End Function

    Public Function SetByString2(ByVal SubSystem As Short, ByVal MajorIndex As Integer, ByVal SubScript As Integer, ByVal MinorIndex As Integer, ByVal Style As Short, ByVal Value As String) As Integer

        Try
            ClearError()

            SetMethodLog("SetByString2", SubSystem, MajorIndex, SubScript, MinorIndex, Style, Value)

            '-----------------------------------------
            ' Release interlock for setting.
            m_obj.NcRunInterLockRelease(1)

            Result = m_obj.SetByString2(SubSystem, MajorIndex, SubScript, MinorIndex, Style, Value)
            If Result <> 0 Then
                ErrMsg = m_obj.GetErrMsg(SubSystem, Result)
                ErrData = m_obj.GetErrData(SubSystem, Result)
                ErrStr = m_obj.GetErrStr(SubSystem, Result)
            End If

            Return Result

        Catch ex As Exception
            ErrMsg = "SetByString2() FAILED. " + Err.Description
            Throw ex
        End Try

    End Function

    Public Function AddByString2(ByVal SubSystem As Short, ByVal MajorIndex As Integer, ByVal SubScript As Integer, ByVal MinorIndex As Integer, ByVal Style As Short, ByVal Value As String) As Integer

        Try
            ClearError()

            SetMethodLog("AddByString2", SubSystem, MajorIndex, SubScript, MinorIndex, Style, Value)

            '-----------------------------------------
            ' Release interlock for setting.
            m_obj.NcRunInterLockRelease(1)

            Result = m_obj.AddByString2(SubSystem, MajorIndex, SubScript, MinorIndex, Style, Value)
            If Result <> 0 Then
                ErrMsg = m_obj.GetErrMsg(SubSystem, Result)
                ErrData = m_obj.GetErrData(SubSystem, Result)
                ErrStr = m_obj.GetErrStr(SubSystem, Result)
            End If

            Return Result

        Catch ex As Exception
            ErrMsg = "AddByString2() FAILED. " + Err.Description
            Throw ex
        End Try

    End Function

    Public Function CalByString2(ByVal SubSystem As Short, ByVal MajorIndex As Integer, ByVal SubScript As Integer, ByVal MinorIndex As Integer, ByVal Style As Short, ByVal Value As String) As Integer

        Try
            ClearError()

            SetMethodLog("CalByString2", SubSystem, MajorIndex, SubScript, MinorIndex, Style, Value)

            '-----------------------------------------
            ' Release interlock for setting.
            m_obj.NcRunInterLockRelease(1)

            Result = m_obj.CalByString2(SubSystem, MajorIndex, SubScript, MinorIndex, Style, Value)
            If Result <> 0 Then
                ErrMsg = m_obj.GetErrMsg(SubSystem, Result)
                ErrData = m_obj.GetErrData(SubSystem, Result)
                ErrStr = m_obj.GetErrStr(SubSystem, Result)
            End If

            Return Result

        Catch ex As Exception
            ErrMsg = "CalByString2() FAILED. " + Err.Description
            Throw ex
        End Try

    End Function

    '----------------------------------------------------------------------------------------------
    Public Sub ConnectCommand(ByVal Remote As String, ByVal NcType As NCTYPE, ByVal UseRemoteAPI As Boolean)

        Try
            Dim ProgId As String

            m_NcType = NcType

            DisconnectCommand()
            If UseRemoteAPI Then
                If m_NcType = NCTYPE.TYPE_MC Then
                    ProgId = "RXOSPAPI.CMDM"
                Else
                    ProgId = "RXOSPAPI.CMDL"
                End If
            Else
                Remote = ""
                If m_NcType = NCTYPE.TYPE_MC Then
                    ProgId = "NCCmdM.OSPCMDM"
                Else
                    ProgId = "NCCMDL.OSPCMDL"
                End If
            End If

            MethodLog = "CreateObject(" + ProgId + "," + Remote + ")"

            m_obj_com = CreateObject(ProgId, Remote)

        Catch ex As Exception
            ErrMsg = "CreateObject() FAILED. " + ex.Message + vbCrLf
            Throw ex
        End Try

    End Sub

    Public Function SelectMainProgramL(ByVal mainfile As String, ByVal subfile As String, ByVal ssbfile As String, ByVal prgname As String, ByVal sp As Short) As Integer

        Try
            ClearError()

            MethodLog = "SelectMainProgramL(" + mainfile + "," + subfile + "," + ssbfile + "," + prgname + "," + sp.ToString() + ")"

            '-----------------------------------------
            ' Release interlock for setting.
            m_obj_com.NcRunInterLockRelease(1)

            Result = m_obj_com.SelectMainProgram(mainfile, subfile, ssbfile, prgname, sp)


            If Result <> 0 Then
                ErrMsg = m_obj_com.GetLastErrorMsg()
                ErrData = m_obj_com.GetLastErrorData()
                ErrStr = m_obj_com.GetLastErrorStr()
            End If

            Return Result

        Catch ex As Exception
            ErrMsg = "SelectMainProgramL() FAILED. " + Err.Description
            Throw ex
        End Try

    End Function


    Public Function getVersion() As String
        Dim value As String
        Try
            ClearError()

            MethodLog = "getVersion()"

            '-----------------------------------------
            ' Release interlock for setting.
            m_obj_com.NcRunInterLockRelease(1)

            value = m_obj_com.getVersion()

            Result = m_obj_com.GetLastErrorData()

            If Result <> 0 Then
                ErrMsg = m_obj_com.GetLastErrorMsg()
                ErrData = m_obj_com.GetLastErrorData()
                ErrStr = m_obj_com.GetLastErrorStr()
            End If

            Return value

        Catch ex As Exception
            ErrMsg = "getVersion() FAILED. " + Err.Description
            Throw ex
        End Try

    End Function


    Public Function AttachTool() As String

        Try
            ClearError()

            MethodLog = "getVersionL()"

            '-----------------------------------------
            ' Release interlock for setting.
            m_obj_com.NcRunInterLockRelease(1)

            Result = m_obj_com.AttachTool(22, "3", 3, 2)


            If Result <> 0 Then
                ErrMsg = m_obj_com.GetLastErrorMsg()
                ErrData = m_obj_com.GetLastErrorData()
                ErrStr = m_obj_com.GetLastErrorStr()
            End If

            Return Result

        Catch ex As Exception
            ErrMsg = "SelectMainProgramL() FAILED. " + Err.Description
            Throw ex
        End Try

    End Function

    Public Function SelectMainProgramM(ByVal mainfile As String, ByVal subfile As String, ByVal prgname As String, ByVal md As Short) As Integer

        Try
            ClearError()

            MethodLog = "SelectMainProgramM(" + mainfile + "," + subfile + "," + prgname + "," + md.ToString() + ")"

            '-----------------------------------------
            ' Release interlock for setting.
            m_obj_com.NcRunInterLockRelease(1)

            Result = m_obj_com.SelectMainProgram(mainfile, subfile, prgname, md)
            If Result <> 0 Then
                ErrMsg = m_obj_com.GetLastErrorMsg()
                ErrData = m_obj_com.GetLastErrorData()
                ErrStr = m_obj_com.GetLastErrorStr()
            End If

            Return Result

        Catch ex As Exception
            ErrMsg = "SelectMainProgramL() FAILED. " + Err.Description
            Throw ex
        End Try

    End Function



    Public Function RegisterToolM(ByVal toolno As Integer, ByVal Kind As Short, ByVal Diameter As Short, ByVal Weight As Short, ByVal Heigth As Short, ByVal Speed As String, ByVal ATC As UShort, ByVal Coolant As Short) As Integer

        Try
            ClearError()

            'MethodLog = "SelectMainProgramM(" + mainfile + "," + subfile + "," + prgname + "," + md.ToString() + ")"

            '-----------------------------------------
            ' Release interlock for setting.
            m_obj_com.NcRunInterLockRelease(1)

            Result = m_obj_com.RegisterSimpleTool(toolno, Kind, Diameter, Weight, Heigth, Speed, ATC, Coolant)
            If Result <> 0 Then
                ErrMsg = m_obj_com.GetLastErrorMsg()
                ErrData = m_obj_com.GetLastErrorData()
                ErrStr = m_obj_com.GetLastErrorStr()
            End If

            Return Result

        Catch ex As Exception
            ErrMsg = "SelectMainProgramL() FAILED. " + Err.Description
            Throw ex
        End Try

    End Function

    Public Function AttacheToolM(ByVal ToolOperationType As Byte, ByVal TargetNo As Short, ByVal ToolNo As Integer) As Integer

        Try
            ClearError()

            '  MethodLog = "DeatacheToolL(" + mainfile + "," + subfile + "," + prgname + "," + md.ToString() + ")"

            '-----------------------------------------
            ' Release interlock for setting.
            m_obj_com.NcRunInterLockRelease(1)

            Result = m_obj_com.AttachTool(ToolOperationType, TargetNo, ToolNo)
            If Result <> 0 Then
                ErrMsg = m_obj_com.GetLastErrorMsg()
                ErrData = m_obj_com.GetLastErrorData()
                ErrStr = m_obj_com.GetLastErrorStr()
            End If

            Return Result

        Catch ex As Exception
            ErrMsg = "AttacheToolM() FAILED. " + Err.Description
            Throw ex
        End Try

    End Function
    Public Function DetacheToolM(ByVal ToolOperationType As Byte, ByVal TargetNo As Short) As Integer

        Try
            ClearError()

            '  MethodLog = "DeatacheToolL(" + mainfile + "," + subfile + "," + prgname + "," + md.ToString() + ")"

            '-----------------------------------------
            ' Release interlock for setting.
            m_obj_com.NcRunInterLockRelease(1)

            Result = m_obj_com.DetachTool(ToolOperationType, TargetNo)
            If Result <> 0 Then
                ErrMsg = m_obj_com.GetLastErrorMsg()
                ErrData = m_obj_com.GetLastErrorData()
                ErrStr = m_obj_com.GetLastErrorStr()
            End If

            Return Result

        Catch ex As Exception
            ErrMsg = "DetacheToolM() FAILED. " + Err.Description
            Throw ex
        End Try

    End Function


#Region "TEST"
    Public Function AttacheToolL(ByVal SsIndex As Integer, ByVal ToolOperationType As Byte, ByVal TargetNo As Integer, ByVal ToolNo As Integer) As Integer

        Try
            ClearError()


            '-----------------------------------------
            ' Release interlock for setting.
            m_obj_com.NcRunInterLockRelease(1)

            Result = m_obj_com.AttachTool(SsIndex, ToolOperationType, TargetNo, ToolNo)
            If Result <> 0 Then
                ErrMsg = m_obj_com.GetLastErrorMsg()
                ErrData = m_obj_com.GetLastErrorData()
                ErrStr = m_obj_com.GetLastErrorStr()
            End If

            Return Result

        Catch ex As Exception
            ErrMsg = "AttacheToolL() FAILED. " + Err.Description
            Throw ex
        End Try

    End Function

    Public Function DetacheToolL(ByVal SsIndex As Integer, ByVal ToolOperationType As Byte, ByVal TargetNo As Integer) As Integer

        Try
            ClearError()

            '-----------------------------------------
            ' Release interlock for setting.
            m_obj_com.NcRunInterLockRelease(1)

            Result = m_obj_com.ToolComment(SsIndex, ToolOperationType, TargetNo)
            If Result <> 0 Then
                ErrMsg = m_obj_com.GetLastErrorMsg()
                ErrData = m_obj_com.GetLastErrorData()
                ErrStr = m_obj_com.GetLastErrorStr()
            End If

            Return Result

        Catch ex As Exception
            ErrMsg = "DetacheToolL() FAILED. " + Err.Description
            Throw ex
        End Try

    End Function

    Public Function StartUpdate(Optional type As Long = 0, Optional autowait As Long = 0) As Integer
        Try
            ClearError()

            m_obj.NcRunInterlockRelease(1)

            Result = m_obj.StartUpdate(type, autowait)
            If Result = 0 Then
                ErrMsg = m_obj.GetErrMsg(1, Result)
                ErrData = m_obj.GetErrData(1, Result)
                ErrStr = m_obj.GetErrStr(1, Result)
                'Lathe
                'ErrMsg = m_obj.GetErrMsg(33, Result)
                'ErrData = m_obj.GetErrData(33, Result)
                'ErrStr = m_obj.GetErrStr(33, Result)
            End If

            Return Result

        Catch ex As Exception
            Throw ex
        End Try
    End Function

    ''' <summary>
    ''' When update is completed thoroughly, 0 is returned
    ''' </summary>
    ''' <param name="waittime">-1= wait until task is done ; 0 no wait time, 1 or more 0 = designated time in mS</param>
    ''' <returns></returns>
    Public Function WaitUpdateEnd(Optional waittime As Long = -1) As Integer
        Try
            ClearError()

            m_obj.NcRunInterlockRelease(1)

            Result = m_obj.WaitUpdateEnd(waittime)

            If Result <> 0 Then
                'MC
                'ErrMsg = m_obj.GetErrMsg(1, Result)
                'ErrData = m_obj.GetErrData(1, Result)
                'ErrStr = m_obj.GetErrStr(1, Result)
                'Lathe
                ErrMsg = m_obj.GetErrMsg(33, Result)
                ErrData = m_obj.GetErrData(33, Result)
                ErrStr = m_obj.GetErrStr(33, Result)
            End If

            Return Result

        Catch ex As Exception
            Throw ex
        End Try
    End Function

#End Region
End Class

