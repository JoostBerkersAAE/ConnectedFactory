Imports System
Imports System.Threading.Tasks
Imports System.Collections.Generic
Imports OkumaConnect.Models
Imports OkumaConnect.Services.Logging

Namespace Services.DataCollection

    ''' <summary>
    ''' Base class for MacMan data collectors - simplified version for OkumaConnect
    ''' Based on ConnectedFactory/Okuma/Collectors/MacMan/MacManData.vb
    ''' </summary>
    Public MustInherit Class MacManDataCollectorBase
        Protected ReadOnly DataApi As ClassOspApi
        Protected ReadOnly MachineIP As String
        Protected ReadOnly ScreenType As String
        Protected ReadOnly CollectionInterval As Integer
        Protected ReadOnly _logger As ILogger
        
        Public Sub New(dataApi As ClassOspApi, machineIP As String, screenType As String, logger As ILogger, Optional collectionInterval As Integer = 30)
            Me.DataApi = dataApi
            Me.MachineIP = machineIP
            Me.ScreenType = screenType
            Me._logger = logger
            Me.CollectionInterval = collectionInterval
        End Sub
        
        ''' <summary>
        ''' Collect data from the machine
        ''' </summary>
        Public MustOverride Function CollectData(Optional lastProcessedDate As DateTime? = Nothing, Optional batchSize As Integer = 50, Optional skipLocalTracking As Boolean = False) As CollectionResult
        
        ''' <summary>
        ''' Collect data from the machine without doing MacMan update (update already done centrally)
        ''' Default implementation calls CollectData but skips the UpdateMacManData call
        ''' </summary>
        Public Overridable Function CollectDataWithoutUpdate(Optional lastProcessedDate As DateTime? = Nothing, Optional batchSize As Integer = 50, Optional skipLocalTracking As Boolean = False) As CollectionResult
            ' Default implementation - individual collectors can override if needed
            Return CollectDataInternal(lastProcessedDate, batchSize, skipLocalTracking, skipUpdate:=True)
        End Function
        
        ''' <summary>
        ''' Internal data collection method that can skip the update step
        ''' </summary>
        Protected Overridable Function CollectDataInternal(Optional lastProcessedDate As DateTime? = Nothing, Optional batchSize As Integer = 50, Optional skipLocalTracking As Boolean = False, Optional skipUpdate As Boolean = False) As CollectionResult
            ' Default implementation just calls the regular CollectData
            ' Individual collectors should override this method to support skipUpdate
            Return CollectData(lastProcessedDate, batchSize, skipLocalTracking)
        End Function
        
        ''' <summary>
        ''' Update MacMan data using proper StartUpdate and WaitUpdateEnd sequence
        ''' This should be called once before collecting all data
        ''' </summary>
        Protected Function UpdateMacManData() As Boolean
            Try
                _logger.LogInfo($"🔄 [{MachineIP}] Starting MacMan data update...")
                
                ' Step 1: Start the update process
                Dim updateResult = DataApi.StartUpdate(0, 0)
                _logger.LogInfo($"🔄 [{MachineIP}] StartUpdate result: {updateResult}")
                
                ' Step 2: Wait for update to complete
                Dim waitResult = DataApi.WaitUpdateEnd()
                _logger.LogInfo($"🔄 [{MachineIP}] WaitUpdateEnd result: {waitResult}")
                
                ' Check if update was successful (assuming 0 means success)
                If updateResult = 0 AndAlso waitResult = 0 Then
                    _logger.LogInfo($"✅ [{MachineIP}] MacMan data update completed successfully")
                    Return True
                Else
                    _logger.LogWarning($"⚠️ [{MachineIP}] MacMan data update completed with warnings - StartUpdate: {updateResult}, WaitUpdateEnd: {waitResult}")
                    Return True ' Still proceed with data collection even if there are warnings
                End If
                
            Catch ex As Exception
                _logger.LogError($"❌ [{MachineIP}] Error updating MacMan data: {ex.Message}")
                Return False
            End Try
        End Function
        
        ''' <summary>
        ''' Update date tracking from collected records (like QOkumaConnect)
        ''' </summary>
        Protected Sub UpdateDateTracking(result As CollectionResult)
            Try
                If result.Success AndAlso result.Records.Count > 0 Then
                    ' Find the newest date in records - prioritize ProcessedDate over CollectionTimestamp
                    Dim newestDate As DateTime? = Nothing
                    Dim dateFields As String() = {"ProcessedDate", "StartDay", "EndDay", "AlarmDate", "OperationDate", "Date", "Timestamp"}
                    
                    For Each record In result.Records
                        ' Try date fields from data first (ProcessedDate has priority)
                        For Each dateField In dateFields
                            If record.Data.ContainsKey(dateField) Then
                                Try
                                    Dim dateValue = record.Data(dateField)
                        Dim recordDate As DateTime? = Nothing
                        
                                    ' Try different date formats
                                    If TypeOf dateValue Is DateTime Then
                                        recordDate = CType(dateValue, DateTime)
                                    ElseIf TypeOf dateValue Is DateTime? Then
                                        recordDate = CType(dateValue, DateTime?)
                                    ElseIf TypeOf dateValue Is String Then
                                        Dim dateStr = dateValue.ToString()
                                        ' Try parsing different date formats
                                        Dim tempDate As DateTime
                                        If DateTime.TryParseExact(dateStr, "yyyyMMdd", Nothing, Globalization.DateTimeStyles.None, tempDate) OrElse
                                           DateTime.TryParseExact(dateStr, "yyyy-MM-dd", Nothing, Globalization.DateTimeStyles.None, tempDate) OrElse
                                           DateTime.TryParse(dateStr, tempDate) Then
                                            recordDate = tempDate
                                        End If
                        End If
                        
                                    If recordDate IsNot Nothing AndAlso (newestDate Is Nothing OrElse recordDate > newestDate) Then
                                        newestDate = recordDate
                            End If
                                Catch ex As Exception
                                    ' Skip invalid date values
                                    Continue For
                                End Try
                            End If
                        Next
                        
                        ' Fallback to CollectionTimestamp only if no data field date was found for this record
                        If newestDate Is Nothing Then
                            If newestDate Is Nothing OrElse record.CollectionTimestamp > newestDate Then
                                newestDate = record.CollectionTimestamp
                        End If
                    End If
                Next
                
                    ' Always use the newest found date (at least CollectionTimestamp)
                    If newestDate IsNot Nothing Then
                        result.LastProcessedDate = newestDate
                        _logger.LogInfo($"📅 Updated last processed date to: {newestDate.Value.ToString("yyyy-MM-dd HH:mm:ss")}")
                    Else
                        ' Fallback to current time
                        newestDate = DateTime.Now
                        result.LastProcessedDate = newestDate
                        _logger.LogWarning($"⚠️ No date found in records, using current time: {newestDate.Value.ToString("yyyy-MM-dd HH:mm:ss")}")
            End If
                End If
            Catch ex As Exception
                _logger.LogError($"❌ Error updating date tracking: {ex.Message}")
            End Try
        End Sub
        
        ''' <summary>
        ''' Check if record is newer than last processed date
        ''' </summary>
        Protected Overridable Function IsRecordNewer(recordDate As DateTime?, lastProcessedDate As DateTime?) As Boolean
            If lastProcessedDate Is Nothing Then Return True
            If recordDate Is Nothing Then Return False
            ' Use > for strict newer comparison (default behavior like QOkumaConnect)
            Return recordDate > lastProcessedDate
        End Function
        
        ''' <summary>
        ''' Get record date for timestamp checking - must be implemented by each collector
        ''' </summary>
        Protected Overridable Function GetRecordDate(recordIndex As Integer) As DateTime?
            ' Default implementation returns Nothing - each collector should override
            Return Nothing
        End Function
        
        ''' <summary>
        ''' Get maximum number of records available - must be implemented by each collector
        ''' </summary>
        Protected Overridable Function GetMaxRecords() As Integer
            ' Default implementation returns 0 - each collector should override
            Return 0
        End Function
    End Class
    
    ''' <summary>
    ''' Collection result structure
    ''' </summary>
    Public Class CollectionResult
        Public Property Success As Boolean = True
        Public Property RecordsCollected As Integer = 0
        Public Property LastProcessedDate As Object ' Can be DateTime, DateTime?, or Date
        Public Property ErrorMessage As String = ""
        Public Property Records As New List(Of MacManRecord)()
    End Class
    
    ''' <summary>
    ''' MacMan record structure
    ''' </summary>
    Public Class MacManRecord
        Public Property MachineIP As String
        Public Property ScreenType As String
        Public Property RecordIndex As Integer
        Public Property CollectionTimestamp As DateTime
        Public Property Data As New Dictionary(Of String, Object)()
        
        Public Sub New()
            Data = New Dictionary(Of String, Object)()
            CollectionTimestamp = DateTime.Now
        End Sub
    End Class
    
    ''' <summary>
    ''' Factory for creating MacMan data collectors
    ''' </summary>
    Public Class MacManDataCollectorFactory
        Public Shared Function CreateCollector(screenType As String, dataApi As ClassOspApi, machineIP As String, logger As ILogger, Optional collectionInterval As Integer = 30) As MacManDataCollectorBase
            Select Case screenType.ToUpper()
                Case "MACHINING_REPORT_DISPLAY"
                    Return New MachiningReportCollector(dataApi, machineIP, logger, collectionInterval)
                Case "ALARM_HISTORY_DISPLAY"
                    Return New AlarmHistoryCollector(dataApi, machineIP, logger, collectionInterval)
                Case "OPERATION_HISTORY_DISPLAY"
                    Return New OperationHistoryCollector(dataApi, machineIP, logger, collectionInterval)
                Case "OPERATING_REPORT_DISPLAY"
                    Return New OperatingReportCollector(dataApi, machineIP, logger, collectionInterval)
                Case "NC_STATUS_AT_ALARM_DISPLAY"
                    Return New NCStatusAtAlarmCollector(dataApi, machineIP, logger, collectionInterval)
                Case "OPERATINGSTATUS"
                    Return New OperatingStatusCollector(dataApi, machineIP, logger, collectionInterval)
                Case Else
                    ' Return a generic collector for unknown types
                    Return New GenericMacManCollector(dataApi, machineIP, logger, collectionInterval, screenType)
            End Select
        End Function
    End Class
    
    ''' <summary>
    ''' Generic MacMan collector for unknown screen types
    ''' </summary>
    Public Class GenericMacManCollector
        Inherits MacManDataCollectorBase
        
        Public Sub New(dataApi As ClassOspApi, machineIP As String, logger As ILogger, collectionInterval As Integer, screenType As String)
            MyBase.New(dataApi, machineIP, screenType, logger, collectionInterval)
        End Sub
        
        Public Overrides Function CollectData(Optional lastProcessedDate As DateTime? = Nothing, Optional batchSize As Integer = 50, Optional skipLocalTracking As Boolean = False) As CollectionResult
            Dim result As New CollectionResult()
            
            Try
                _logger.LogInfo($"🔄 Collecting generic MacMan data for {ScreenType} from {MachineIP}...")
                
                If Not UpdateMacManData() Then
                    result.Success = False
                    result.ErrorMessage = "Failed to update MacMan data"
                    Return result
                End If
                
                ' Create a simple record for unknown screen types
                Dim record As New MacManRecord() With {
                    .MachineIP = MachineIP,
                    .ScreenType = ScreenType,
                    .RecordIndex = 0
                }
                
                ' Add basic data
                record.Data("CollectionTime") = DateTime.Now
                record.Data("ProcessedDate") = DateTime.Now
                record.Data("ScreenType") = ScreenType
                
                ' Set CollectionTimestamp like QOkumaConnect
                record.CollectionTimestamp = DateTime.Now
                
                result.Records.Add(record)
                result.RecordsCollected = 1
                result.Success = True
                
                UpdateDateTracking(result)
                
                Console.WriteLine($"✅ Generic MacMan collection completed: {result.RecordsCollected} records")
                
            Catch ex As Exception
                result.Success = False
                result.ErrorMessage = $"Generic MacMan collection failed: {ex.Message}"
                Console.WriteLine($"❌ {result.ErrorMessage}")
            End Try
            
            Return result
        End Function
    End Class
    
    ''' <summary>
    ''' Machining Report Collector - Exact implementation like QOkumaConnect
    ''' </summary>
    Public Class MachiningReportCollector
        Inherits MacManDataCollectorBase
        
        Public Sub New(dataApi As ClassOspApi, machineIP As String, logger As ILogger, Optional collectionInterval As Integer = 30)
            MyBase.New(dataApi, machineIP, "MACHINING_REPORT_DISPLAY", logger, collectionInterval)
        End Sub
        
        ' Override IsRecordNewer to use >= instead of > for MACHINING_REPORT_DISPLAY (like QOkumaConnect)
        Protected Overrides Function IsRecordNewer(recordDate As DateTime?, lastProcessedDate As DateTime?) As Boolean
            If lastProcessedDate Is Nothing Then Return True
            If recordDate Is Nothing Then Return False
            Return recordDate >= lastProcessedDate ' Use >= instead of > for MACHINING_REPORT_DISPLAY
        End Function
        
        Public Overrides Function CollectData(Optional lastProcessedDate As DateTime? = Nothing, Optional batchSize As Integer = 50, Optional skipLocalTracking As Boolean = False) As CollectionResult
            Return CollectDataInternal(lastProcessedDate, batchSize, skipLocalTracking, skipUpdate:=False)
        End Function
        
        Protected Overrides Function CollectDataInternal(Optional lastProcessedDate As DateTime? = Nothing, Optional batchSize As Integer = 50, Optional skipLocalTracking As Boolean = False, Optional skipUpdate As Boolean = False) As CollectionResult
            Dim result As New CollectionResult()
            
            Try
                _logger.LogInfo($"🔄 Collecting Machining Report data from {MachineIP}...")
                
                ' Only update MacMan data if not skipped (for efficiency when called from MacManDataService)
                If Not skipUpdate Then
                    If Not UpdateMacManData() Then
                        result.Success = False
                        result.ErrorMessage = "Failed to update MacMan data"
                        Return result
                    End If
                Else
                    _logger.LogInfo($"⏩ Skipping MacMan data update (already done centrally)")
                End If
                
                ' Use PERIOD mode (majorIndexOffset = 2) like QOkumaConnect
                Dim majorIndexOffset As Integer = 2
                
                ' Get maximum records (use real API like QOkumaConnect)
                Dim maxRecords = GetMaxRecords()
                Dim recordsToProcess = Math.Min(maxRecords, batchSize)
                
                Console.WriteLine($"📊 Processing {recordsToProcess} of {maxRecords} available machining report records...")
                
                For i As Integer = 0 To recordsToProcess - 1
                    Try
                        Dim record As New MacManRecord() With {
                            .MachineIP = MachineIP,
                            .ScreenType = ScreenType,
                            .RecordIndex = i
                        }
                        
                        ' Collect ALL fields exactly like QOkumaConnect
                        Dim recordIndex = i ' Local copy to avoid lambda closure issue
                        
                        ' Collect all data fields exactly like QOkumaConnect
                        record.Data("MainProgramName") = DataApi.GetByString(1, 5001 + majorIndexOffset * 2, recordIndex, 0, 9).Trim()
                        record.Data("ProgramName") = DataApi.GetByString(1, 5002 + majorIndexOffset * 2, recordIndex, 0, 9).Trim()
                        
                        ' Get StartDay and StartTime with debug info
                        Dim rawStartDay = DataApi.GetByString(1, 5057 + majorIndexOffset * 2, recordIndex, 0, 9)
                        Dim rawStartTime = DataApi.GetByString(1, 5058 + majorIndexOffset * 2, recordIndex, 0, 9)
                        record.Data("StartDay") = rawStartDay.Trim()
                        record.Data("StartTime") = rawStartTime.Trim()
                        
                        record.Data("RunningTime") = DataApi.GetByString(1, 3042 + majorIndexOffset * 12, recordIndex, 0, 9).Trim()
                        record.Data("OperatingTime") = DataApi.GetByString(1, 3043 + majorIndexOffset * 12, recordIndex, 0, 9).Trim()
                        record.Data("CuttingTime") = DataApi.GetByString(1, 3044 + majorIndexOffset * 12, recordIndex, 0, 9).Trim()
                        record.Data("NumberOfWork") = DataApi.GetByString(1, 2042 + majorIndexOffset * 6, recordIndex, 0, 9).Trim()
                        
                        ' Parse record date from StartDay/StartTime (like QOkumaConnect)
                        Dim startDay = record.Data("StartDay").ToString()
                        Dim startTime = record.Data("StartTime").ToString()
                        Dim recordDate = ParseRecordDateTime(startDay, startTime)
                        ' Use the actual parsed date, not current time (like QOkumaConnect)
                        record.Data("ProcessedDate") = If(recordDate.HasValue, recordDate.Value, DateTime.Now)
                        
                        ' Set CollectionTimestamp like QOkumaConnect
                        record.CollectionTimestamp = DateTime.Now
                        
                        ' Check if record is newer using the overridden IsRecordNewer method (>= for MACHINING_REPORT_DISPLAY)
                        Dim isNewer = IsRecordNewer(recordDate, lastProcessedDate)
                        Dim isEqual = (recordDate IsNot Nothing AndAlso lastProcessedDate IsNot Nothing AndAlso recordDate = lastProcessedDate)
                        
                        _logger.LogInfo($"      Record {i}: Date={If(recordDate.HasValue, recordDate.Value.ToString("yyyy-MM-dd HH:mm:ss"), "N/A")}, LastProcessed={If(lastProcessedDate.HasValue, lastProcessedDate.Value.ToString("yyyy-MM-dd HH:mm:ss"), "N/A")}, IsNewer={isNewer}, IsEqual={isEqual}")
                        
                        ' Add record (like QOkumaConnect - always add, then check for stopping)
                        result.Records.Add(record)
                        
                        ' Stop if record is not newer and not equal (but include current record)
                        If Not isNewer AndAlso Not isEqual Then
                            _logger.LogInfo($"⏹️ Stopped at record {i + 1}/{recordsToProcess} - found older record ({If(recordDate.HasValue, recordDate.Value.ToString("yyyy-MM-dd HH:mm:ss"), "N/A")})")
                            Exit For
                        ElseIf isEqual Then
                            Console.WriteLine($"🟰 Found equal timestamp record {i + 1}/{recordsToProcess} - including and stopping ({recordDate.Value.ToString("yyyy-MM-dd HH:mm:ss")})")
                            Exit For
                        End If
                        
                    Catch ex As Exception
                        Console.WriteLine($"⚠️ Error processing machining report record {i}: {ex.Message}")
                    End Try
                Next
                
                result.RecordsCollected = result.Records.Count
                result.Success = True
                
                UpdateDateTracking(result)
                
                Console.WriteLine($"✅ Machining Report: Collected {result.RecordsCollected} records")
                
            Catch ex As Exception
                result.Success = False
                result.ErrorMessage = ex.Message
                Console.WriteLine($"❌ Error in Machining Report collection: {ex.Message}")
            End Try
            
            Return result
        End Function
        
        ''' <summary>
        ''' Parse datum/tijd uit StartDay en StartTime strings (exact copy from QOkumaConnect)
        ''' </summary>
        Private Function ParseRecordDateTime(startDay As String, startTime As String) As DateTime?
            Try
                ' Parse datum en tijd (implementatie afhankelijk van MacMan formaat)
                If String.IsNullOrEmpty(startDay) OrElse String.IsNullOrEmpty(startTime) Then
                    Console.WriteLine($"⚠️ Empty StartDay ('{startDay}') or StartTime ('{startTime}'), stopping collection")
                    Return Nothing
                End If
                
                ' Try to parse different date/time formats
                Dim parsedDateTime As DateTime
                
                ' Format 1: YYYYMMDD and HHMMSS format (like in QOkumaConnect docs: "20241219" and "143000")
                If startDay.Length = 8 AndAlso startTime.Length >= 6 Then
                    Try
                        Dim year = Integer.Parse(startDay.Substring(0, 4))
                        Dim month = Integer.Parse(startDay.Substring(4, 2))
                        Dim day = Integer.Parse(startDay.Substring(6, 2))
                        Dim hour = Integer.Parse(startTime.Substring(0, 2))
                        Dim minute = Integer.Parse(startTime.Substring(2, 2))
                        Dim second = Integer.Parse(startTime.Substring(4, 2))
                        
                        parsedDateTime = New DateTime(year, month, day, hour, minute, second)
                        Return parsedDateTime
                    Catch ex As Exception
                        Console.WriteLine($"⚠️ Failed to parse YYYYMMDD HHMMSS format: {ex.Message}")
                    End Try
                End If
                
                ' Format 2: "2025/09/02" and "14:25:49" (from API response)
                Dim dateTimeString = $"{startDay} {startTime}"
                If DateTime.TryParse(dateTimeString, parsedDateTime) Then
                    Return parsedDateTime
                End If
                
                ' Format 3: "yyyy/MM/dd HH:mm:ss" explicit format
                If DateTime.TryParseExact(dateTimeString, "yyyy/MM/dd HH:mm:ss", Nothing, Globalization.DateTimeStyles.None, parsedDateTime) Then
                    Return parsedDateTime
                End If
                
                ' Format 4: "MM/dd/yyyy HH:mm:ss" format
                If DateTime.TryParseExact(dateTimeString, "MM/dd/yyyy HH:mm:ss", Nothing, Globalization.DateTimeStyles.None, parsedDateTime) Then
                    Return parsedDateTime
                End If
                
                Console.WriteLine($"⚠️ Could not parse date/time: '{startDay}' + '{startTime}', stopping collection")
                Return Nothing
                
            Catch ex As Exception
                Console.WriteLine($"⚠️ Error parsing record date: {ex.Message}, stopping collection")
                Return Nothing
            End Try
        End Function
        
        Protected Overrides Function GetMaxRecords() As Integer
            Try
                Dim majorIndexOffset As Integer = 2
                Dim maxRecordsStr = DataApi.GetByString(1, 2092 + majorIndexOffset, 0, 0, 9).Trim()
                If String.IsNullOrEmpty(maxRecordsStr) Then
                    Return 0
                End If
                
                Dim maxRecords As Integer
                If Integer.TryParse(maxRecordsStr, maxRecords) Then
                    Return maxRecords
                Else
                    Return 0
                End If
            Catch ex As Exception
                Return 0
            End Try
        End Function
    End Class
    
    ''' <summary>
    ''' Simplified Alarm History Collector
    ''' </summary>
    Public Class AlarmHistoryCollector
        Inherits MacManDataCollectorBase
        
        Public Sub New(dataApi As ClassOspApi, machineIP As String, logger As ILogger, Optional collectionInterval As Integer = 30)
            MyBase.New(dataApi, machineIP, "ALARM_HISTORY_DISPLAY", logger, collectionInterval)
        End Sub
        
        Public Overrides Function CollectData(Optional lastProcessedDate As DateTime? = Nothing, Optional batchSize As Integer = 50, Optional skipLocalTracking As Boolean = False) As CollectionResult
            Return CollectDataInternal(lastProcessedDate, batchSize, skipLocalTracking, skipUpdate:=False)
        End Function
        
        Protected Overrides Function CollectDataInternal(Optional lastProcessedDate As DateTime? = Nothing, Optional batchSize As Integer = 50, Optional skipLocalTracking As Boolean = False, Optional skipUpdate As Boolean = False) As CollectionResult
            Dim result As New CollectionResult()
            
            Try
                Console.WriteLine($"🔄 Collecting Alarm History data from {MachineIP}...")
                
                ' Only update MacMan data if not skipped (for efficiency when called from MacManDataService)
                If Not skipUpdate Then
                    If Not UpdateMacManData() Then
                        result.Success = False
                        result.ErrorMessage = "Failed to update MacMan data"
                        Return result
                    End If
                Else
                    _logger.LogInfo($"⏩ Skipping MacMan data update (already done centrally)")
                End If
                
                ' Get real max records from API like QOkumaConnect
                Dim maxRecords = GetMaxRecords()
                Dim recordsToProcess = Math.Min(maxRecords, batchSize)
                
                Console.WriteLine($"📊 Processing {recordsToProcess} of {maxRecords} available alarm records...")
                
                ' Collect records - stop early if records are not newer (sorted by date DESC like QOkumaConnect)
                Dim recordsProcessed As Integer = 0
                For i As Integer = 0 To recordsToProcess - 1
                    Try
                        ' Get record date first to check if it's newer (do this first for efficiency like QOkumaConnect)
                        Dim recordDate = GetRecordDate(i)
                        
                        ' Stop the loop if record is not newer than last processed date
                        ' (records are sorted with newest first, so all following are also older)
                        If Not IsRecordNewer(recordDate, lastProcessedDate) Then
                            Console.WriteLine($"⏹️ Stopped at record {i + 1}/{recordsToProcess} - found older alarm record ({If(recordDate.HasValue, recordDate.Value.ToString("yyyy-MM-dd HH:mm:ss"), "N/A")})")
                            Exit For
                        End If
                        
                        Dim record As New MacManRecord() With {
                            .MachineIP = MachineIP,
                            .ScreenType = ScreenType,
                            .RecordIndex = i
                        }
                        recordsProcessed += 1
                        
                        ' Collect alarm fields like QOkumaConnect
                        record.Data("AlarmNo") = DataApi.GetByString(1, 5011, i, 0, 9).Trim()
                        record.Data("AlarmCode") = DataApi.GetByString(1, 5012, i, 0, 9).Trim()
                        record.Data("AlarmMessage") = DataApi.GetByString(1, 5013, i, 0, 9).Trim()
                        
                        ' Get real alarm date/time from API (same indices as QOkumaConnect)
                        Dim alarmDateStr = DataApi.GetByString(1, 5063, i, 0, 9).Trim()
                        Dim alarmTimeStr = DataApi.GetByString(1, 5064, i, 0, 9).Trim()
                        
                        record.Data("Date") = alarmDateStr
                        record.Data("Time") = alarmTimeStr
                        
                        ' Set ProcessedDate from the already parsed recordDate (like QOkumaConnect)
                        record.Data("ProcessedDate") = recordDate
                        record.Data("RawAlarmDate") = alarmDateStr
                        record.Data("RawAlarmTime") = alarmTimeStr
                        
                        ' Set CollectionTimestamp like QOkumaConnect
                        record.CollectionTimestamp = DateTime.Now
                        
                        result.Records.Add(record)
                        
                    Catch ex As Exception
                        Console.WriteLine($"⚠️ Error processing alarm record {i}: {ex.Message}")
                    End Try
                Next
                
                result.RecordsCollected = result.Records.Count
                result.Success = True
                
                UpdateDateTracking(result)
                
                Console.WriteLine($"✅ Alarm History: Collected {result.RecordsCollected} records")
                
                ' Print data as array of dictionaries
                If result.Records.Count > 0 Then
                    Console.WriteLine($"📋 ALARM_HISTORY_DISPLAY Data Array:")
                    For i As Integer = 0 To result.Records.Count - 1
                        Dim dataRecord = DirectCast(result.Records(i), MacManRecord)
                        Console.WriteLine($"   [{i}] = {{")
                        For Each kvp In dataRecord.Data
                            Console.WriteLine($"      ""{kvp.Key}"": ""{kvp.Value}""")
                        Next
                        Console.WriteLine($"   }}")
                    Next
                End If
                
            Catch ex As Exception
                result.Success = False
                result.ErrorMessage = $"Alarm history collection failed: {ex.Message}"
                Console.WriteLine($"❌ {result.ErrorMessage}")
            End Try
            
            Return result
        End Function
        
        Protected Overrides Function GetRecordDate(recordIndex As Integer) As DateTime?
            Try
                ' Use the correct major indices for ALARM_HISTORY_DISPLAY date and time (same as QOkumaConnect)
                Dim dateStr = DataApi.GetByString(1, 5063, recordIndex, 0, 9).Trim()
                Dim timeStr = DataApi.GetByString(1, 5064, recordIndex, 0, 9).Trim()
                
                Console.WriteLine($"🔍 GetRecordDate for alarm {recordIndex}: Date='{dateStr}', Time='{timeStr}'")
                
                If String.IsNullOrEmpty(dateStr) OrElse String.IsNullOrEmpty(timeStr) Then
                    Console.WriteLine($"⚠️ Empty date/time for alarm record {recordIndex}, stopping collection")
                    Return Nothing
                End If
                
                ' Parse alarm date and time (format may vary)
                Dim parsedDate = ParseAlarmDateTime(dateStr, timeStr)
                Console.WriteLine($"🔍 Parsed alarm date for record {recordIndex}: {parsedDate}")
                Return parsedDate
                
            Catch ex As Exception
                Console.WriteLine($"❌ Error getting alarm record date for {recordIndex}: {ex.Message}, stopping collection")
                Return Nothing
            End Try
        End Function
        
        Protected Overrides Function GetMaxRecords() As Integer
            Try
                ' Use correct API index for ALARM_HISTORY_DISPLAY max records (same as QOkumaConnect)
                Dim maxRecordsStr = DataApi.GetByString(1, 2094, 0, 0, 9).Trim()
                If String.IsNullOrEmpty(maxRecordsStr) Then
                    Return 0
                End If
                
                Dim maxRecords As Integer
                If Integer.TryParse(maxRecordsStr, maxRecords) Then
                    Return maxRecords
                Else
                    Return 0
                End If
            Catch ex As Exception
                Return 0
            End Try
        End Function
        
        Private Function ParseAlarmDateTime(dateStr As String, timeStr As String) As DateTime?
            Try
                Console.WriteLine($"🔍 ParseAlarmDateTime: Parsing date='{dateStr}', time='{timeStr}'")
                
                If String.IsNullOrEmpty(dateStr) OrElse String.IsNullOrEmpty(timeStr) Then
                    Console.WriteLine($"⚠️ Empty date or time string, stopping collection")
                    Return Nothing
                End If
                
                ' Try different date formats (same logic as QOkumaConnect)
                Dim parsedDate As DateTime
                Dim dateTimeString = $"{dateStr} {timeStr}"
                
                ' Try standard format: "yyyy/MM/dd HH:mm:ss"
                If DateTime.TryParseExact(dateTimeString, "yyyy/MM/dd HH:mm:ss", Nothing, Globalization.DateTimeStyles.None, parsedDate) Then
                    Console.WriteLine($"✅ Parsed alarm datetime (format 1): {parsedDate}")
                    Return parsedDate
                End If
                
                ' Try alternative format: "yyyy-MM-dd HH:mm:ss"
                If DateTime.TryParseExact(dateTimeString, "yyyy-MM-dd HH:mm:ss", Nothing, Globalization.DateTimeStyles.None, parsedDate) Then
                    Console.WriteLine($"✅ Parsed alarm datetime (format 2): {parsedDate}")
                    Return parsedDate
                End If
                
                ' Try general parsing
                If DateTime.TryParse(dateTimeString, parsedDate) Then
                    Console.WriteLine($"✅ Parsed alarm datetime (general): {parsedDate}")
                    Return parsedDate
                End If
                
                Console.WriteLine($"❌ Could not parse alarm datetime: '{dateTimeString}', stopping collection")
                Return Nothing
                
            Catch ex As Exception
                Console.WriteLine($"❌ Exception parsing alarm datetime: {ex.Message}, stopping collection")
                Return Nothing
            End Try
        End Function
    End Class
    
    ''' <summary>
    ''' Simplified Operation History Collector
    ''' </summary>
    Public Class OperationHistoryCollector
        Inherits MacManDataCollectorBase
        
        Public Sub New(dataApi As ClassOspApi, machineIP As String, logger As ILogger, Optional collectionInterval As Integer = 30)
            MyBase.New(dataApi, machineIP, "OPERATION_HISTORY_DISPLAY", logger, collectionInterval)
        End Sub
        
        Public Overrides Function CollectData(Optional lastProcessedDate As DateTime? = Nothing, Optional batchSize As Integer = 50, Optional skipLocalTracking As Boolean = False) As CollectionResult
            Dim result As New CollectionResult()
            
            Try
                Console.WriteLine($"🔄 Collecting Operation History data from {MachineIP}...")
                
                If Not UpdateMacManData() Then
                    result.Success = False
                    result.ErrorMessage = "Failed to update MacMan data"
                    Return result
                End If
                
                ' Get real max records from API like QOkumaConnect
                Dim maxRecords = GetMaxRecords()
                Dim recordsToProcess = Math.Min(maxRecords, batchSize)
                
                Console.WriteLine($"📊 Processing {recordsToProcess} of {maxRecords} available operation history records...")
                
                ' Collect records - stop early if records are not newer (sorted by date DESC like QOkumaConnect)
                Dim recordsProcessed As Integer = 0
                For i As Integer = 0 To recordsToProcess - 1
                    Try
                        ' Get record date first to check if it's newer (do this first for efficiency like QOkumaConnect)
                        Dim recordDate = GetRecordDate(i)
                        
                        ' Stop the loop if record is not newer than last processed date
                        ' (records are sorted with newest first, so all following are also older)
                        If Not IsRecordNewer(recordDate, lastProcessedDate) Then
                            Console.WriteLine($"⏹️ Stopped at record {i + 1}/{recordsToProcess} - found older operation history record ({If(recordDate.HasValue, recordDate.Value.ToString("yyyy-MM-dd HH:mm:ss"), "N/A")})")
                            Exit For
                        End If
                        
                Dim record As New MacManRecord() With {
                    .MachineIP = MachineIP,
                    .ScreenType = ScreenType,
                            .RecordIndex = i
                        }
                        recordsProcessed += 1
                        
                        ' Get real operation history data from API (same indices as QOkumaConnect)
                        Dim operationDateStr = DataApi.GetByString(1, 5065, i, 0, 9).Trim()
                        Dim operationTimeStr = DataApi.GetByString(1, 5066, i, 0, 9).Trim()
                        Dim panelOperateStr = DataApi.GetByString(1, 5067, i, 0, 9).Trim()
                
                record.Data("Date") = operationDateStr
                record.Data("Time") = operationTimeStr
                record.Data("PanelOperateStrings") = panelOperateStr
                
                        ' Set ProcessedDate from the already parsed recordDate (like QOkumaConnect)
                        record.Data("ProcessedDate") = recordDate
                record.Data("RawOperationDate") = operationDateStr
                record.Data("RawOperationTime") = operationTimeStr
                        
                        ' Set CollectionTimestamp like QOkumaConnect
                        record.CollectionTimestamp = DateTime.Now
                
                result.Records.Add(record)
                        
                    Catch ex As Exception
                        Console.WriteLine($"⚠️ Error processing operation history record {i}: {ex.Message}")
                    End Try
                Next
                
                result.RecordsCollected = result.Records.Count
                result.Success = True
                
                UpdateDateTracking(result)
                
                Console.WriteLine($"✅ Operation History: Collected {result.RecordsCollected} records")
                
                ' Print data as array of dictionaries
                If result.Records.Count > 0 Then
                    Console.WriteLine($"📋 OPERATION_HISTORY_DISPLAY Data Array:")
                    For i As Integer = 0 To result.Records.Count - 1
                        Dim dataRecord = DirectCast(result.Records(i), MacManRecord)
                        Console.WriteLine($"   [{i}] = {{")
                        For Each kvp In dataRecord.Data
                            Console.WriteLine($"      ""{kvp.Key}"": ""{kvp.Value}""")
                        Next
                        Console.WriteLine($"   }}")
                    Next
                End If
                
            Catch ex As Exception
                result.Success = False
                result.ErrorMessage = $"Operation history collection failed: {ex.Message}"
                Console.WriteLine($"❌ {result.ErrorMessage}")
            End Try
            
            Return result
        End Function
        
        Protected Overrides Function GetRecordDate(recordIndex As Integer) As DateTime?
            Try
                ' Use the correct major indices for OPERATION_HISTORY_DISPLAY date and time (same as QOkumaConnect)
                Dim dateStr = DataApi.GetByString(1, 5065, recordIndex, 0, 9).Trim()
                Dim timeStr = DataApi.GetByString(1, 5066, recordIndex, 0, 9).Trim()
                
                Console.WriteLine($"🔍 GetRecordDate for operation history {recordIndex}: Date='{dateStr}', Time='{timeStr}'")
                
                If String.IsNullOrEmpty(dateStr) OrElse String.IsNullOrEmpty(timeStr) Then
                    Console.WriteLine($"⚠️ Empty date/time for operation history record {recordIndex}, stopping collection")
                    Return Nothing
                End If
                
                ' Parse operation history date and time
                Dim parsedDate = ParseOperationHistoryDateTime(dateStr, timeStr)
                Console.WriteLine($"🔍 Parsed operation history date for record {recordIndex}: {parsedDate}")
                Return parsedDate
            Catch ex As Exception
                Console.WriteLine($"❌ Error getting operation history record date for {recordIndex}: {ex.Message}, stopping collection")
                Return Nothing
            End Try
        End Function
        
        Protected Overrides Function GetMaxRecords() As Integer
            Try
                ' Use the correct major index for OPERATION_HISTORY_DISPLAY max records (same as QOkumaConnect)
                Dim maxRecordsStr = DataApi.GetByString(1, 2095, 0, 0, 9).Trim()
                If String.IsNullOrEmpty(maxRecordsStr) Then
                    Console.WriteLine("Warning: Empty max records string for operation history, using fallback")
                    Return 100 ' Fallback value
                End If
                
                Dim maxRecords As Integer
                If Integer.TryParse(maxRecordsStr, maxRecords) Then
                    Console.WriteLine($"🔍 Operation History max records: {maxRecords}")
                    Return maxRecords
                Else
                    Console.WriteLine($"Warning: Could not parse max records for operation history: '{maxRecordsStr}', using fallback")
                    Return 100 ' Fallback value
                End If
            Catch ex As Exception
                Console.WriteLine($"Warning: Could not get max records for operation history: {ex.Message}")
                Return 100 ' Fallback value
            End Try
        End Function
        
        Private Function ParseOperationHistoryDateTime(dateStr As String, timeStr As String) As DateTime?
            Try
                Console.WriteLine($"🔍 ParseOperationHistoryDateTime: Parsing date='{dateStr}', time='{timeStr}'")
                
                If String.IsNullOrEmpty(dateStr) OrElse String.IsNullOrEmpty(timeStr) Then
                    Console.WriteLine($"⚠️ Empty date or time string, stopping collection")
                    Return Nothing
                End If
                
                ' Try different date formats (same logic as QOkumaConnect)
                Dim parsedDate As DateTime
                Dim dateTimeString = $"{dateStr} {timeStr}"
                
                ' Try standard format: "yyyy/MM/dd HH:mm:ss"
                If DateTime.TryParseExact(dateTimeString, "yyyy/MM/dd HH:mm:ss", Nothing, Globalization.DateTimeStyles.None, parsedDate) Then
                    Console.WriteLine($"✅ Parsed operation history datetime (format 1): {parsedDate}")
                    Return parsedDate
                End If
                
                ' Try alternative format: "yyyy-MM-dd HH:mm:ss"
                If DateTime.TryParseExact(dateTimeString, "yyyy-MM-dd HH:mm:ss", Nothing, Globalization.DateTimeStyles.None, parsedDate) Then
                    Console.WriteLine($"✅ Parsed operation history datetime (format 2): {parsedDate}")
                    Return parsedDate
                End If
                
                ' Try general parsing
                If DateTime.TryParse(dateTimeString, parsedDate) Then
                    Console.WriteLine($"✅ Parsed operation history datetime (general): {parsedDate}")
                    Return parsedDate
                End If
                
                Console.WriteLine($"❌ Could not parse operation history datetime: '{dateTimeString}', stopping collection")
                Return Nothing
                
            Catch ex As Exception
                Console.WriteLine($"❌ Exception parsing operation history datetime: {ex.Message}, stopping collection")
                Return Nothing
            End Try
        End Function
    End Class
    
    ''' <summary>
    ''' Simplified Operating Report Collector
    ''' </summary>
    Public Class OperatingReportCollector
        Inherits MacManDataCollectorBase
        
        Public Sub New(dataApi As ClassOspApi, machineIP As String, logger As ILogger, Optional collectionInterval As Integer = 30)
            MyBase.New(dataApi, machineIP, "OPERATING_REPORT_DISPLAY", logger, collectionInterval)
        End Sub
        
        Public Overrides Function CollectData(Optional lastProcessedDate As DateTime? = Nothing, Optional batchSize As Integer = 50, Optional skipLocalTracking As Boolean = False) As CollectionResult
            Dim result As New CollectionResult()
            
            Try
                Console.WriteLine($"🔄 Collecting Operating Report data from {MachineIP}...")
                
                If Not UpdateMacManData() Then
                    result.Success = False
                    result.ErrorMessage = "Failed to update MacMan data"
                    Return result
                End If
                
                ' OPERATING_REPORT_DISPLAY: Always use index 0 only, no loop needed
                Console.WriteLine($"📊 Collecting Operating Report data (single record at index 0)...")
                
                Try
                    Dim record As New MacManRecord() With {
                        .MachineIP = MachineIP,
                        .ScreenType = ScreenType,
                        .RecordIndex = 0
                    }
                    
                    ' Always collect data from index 0 (no timestamp check needed)
                    Dim recordDate = GetRecordDate(0)
                    
                    ' Operating Report PERIOD specifieke velden (volgens de API tabel) - altijd index 0
                    record.Data("Date") = DataApi.GetByString(1, 5056, 0, 0, 9).Trim() ' OPERATING_REPORT.PERIOD DATE
                    record.Data("RunningTime") = DataApi.GetByString(1, 3027, 0, 0, 9).Trim() ' OPERATING_REPORT.PERIOD RUNNING TIME
                    record.Data("OperatingTime") = DataApi.GetByString(1, 3028, 0, 0, 9).Trim() ' OPERATING_REPORT.PERIOD OPERATING TIME
                    record.Data("CuttingTime") = DataApi.GetByString(1, 3029, 0, 0, 9).Trim() ' OPERATING_REPORT.PERIOD CUTTING TIME
                    record.Data("NotOperatingTime") = DataApi.GetByString(1, 3030, 0, 0, 9).Trim() ' OPERATING_REPORT.PERIOD NOT OPERATING TIME
                    record.Data("InProSetupTime") = DataApi.GetByString(1, 3031, 0, 0, 9).Trim() ' OPERATING_REPORT.PERIOD IN-PRO SETUP TIME
                    record.Data("NoOperatorTime") = DataApi.GetByString(1, 3032, 0, 0, 9).Trim() ' OPERATING_REPORT.PERIOD NO OPERATOR TIME
                    record.Data("PartWaitingTime") = DataApi.GetByString(1, 3033, 0, 0, 9).Trim() ' OPERATING_REPORT.PERIOD PART WAITING TIME
                    record.Data("MaintenanceTime") = DataApi.GetByString(1, 3034, 0, 0, 9).Trim() ' OPERATING_REPORT.PERIOD MAINTENANCE TIME
                    record.Data("OtherTime") = DataApi.GetByString(1, 3035, 0, 0, 9).Trim() ' OPERATING_REPORT.PERIOD OTHER TIME
                    record.Data("SpindleRunTime") = DataApi.GetByString(1, 3036, 0, 0, 9).Trim() ' OPERATING_REPORT.PERIOD SPINDLE RUN TIME
                    record.Data("ExternalInputTime") = DataApi.GetByString(1, 3037, 0, 0, 9).Trim() ' OPERATING_REPORT.PERIOD EXTERNAL INPUT TIME
                    record.Data("AlarmOnTime") = DataApi.GetByString(1, 3038, 0, 0, 9).Trim() ' OPERATING_REPORT.PERIOD ALARM ON TIME
                    
                    record.Data("ProcessedDate") = DateTime.Now
                    record.CollectionTimestamp = DateTime.Now
                    result.Records.Add(record)
                    result.RecordsCollected = 1
                    
                Catch ex As Exception
                    Console.WriteLine($"⚠️ Error processing operating report record: {ex.Message}")
                End Try
                
                result.RecordsCollected = result.Records.Count
                result.Success = True
                
                UpdateDateTracking(result)
                
                Console.WriteLine($"✅ Operating Report: Collected {result.RecordsCollected} records")
                
                ' Print data as array of dictionaries
                If result.Records.Count > 0 Then
                    Console.WriteLine($"📋 OPERATING_REPORT_DISPLAY Data Array:")
                    For i As Integer = 0 To result.Records.Count - 1
                        Dim dataRecord = DirectCast(result.Records(i), MacManRecord)
                        Console.WriteLine($"   [{i}] = {{")
                        For Each kvp In dataRecord.Data
                            Console.WriteLine($"      ""{kvp.Key}"": ""{kvp.Value}""")
                        Next
                        Console.WriteLine($"   }}")
                    Next
                End If
                
            Catch ex As Exception
                result.Success = False
                result.ErrorMessage = $"Operating report collection failed: {ex.Message}"
                Console.WriteLine($"❌ {result.ErrorMessage}")
            End Try
            
            Return result
        End Function
        
        Protected Overrides Function GetRecordDate(recordIndex As Integer) As DateTime?
            Try
                ' Get date from OPERATING_REPORT.PERIOD DATE (index 5056)
                Dim dateStr = DataApi.GetByString(1, 5056, recordIndex, 0, 9).Trim()
                
                Console.WriteLine($"🔍 GetRecordDate for operating report {recordIndex}: Date='{dateStr}'")
                
                If String.IsNullOrEmpty(dateStr) Then
                    Console.WriteLine($"⚠️ Empty date for operating report record {recordIndex}")
                    Return DateTime.Now ' Always return current time for OPERATING_REPORT_DISPLAY
                End If
                
                Dim recordDate As DateTime
                If DateTime.TryParse(dateStr, recordDate) Then
                    Console.WriteLine($"✅ Parsed operating report date: {recordDate}")
                    Return recordDate
                End If
                
                Console.WriteLine($"⚠️ Could not parse operating report date: '{dateStr}'")
                Return DateTime.Now ' Always return current time for OPERATING_REPORT_DISPLAY
            Catch ex As Exception
                Console.WriteLine($"❌ Error getting operating report date: {ex.Message}")
                Return DateTime.Now ' Always return current time for OPERATING_REPORT_DISPLAY
            End Try
        End Function
        
        Protected Overrides Function GetMaxRecords() As Integer
            Try
                ' Get the number of available records for operating report (typically 1)
                ' Use a fallback since operating reports usually have just 1 summary record
                Return 1 ' Operating reports typically have only 1 summary record
            Catch ex As Exception
                Return 1 ' Fallback value
            End Try
        End Function
        
        Protected Overrides Function IsRecordNewer(recordDate As DateTime?, lastProcessedDate As DateTime?) As Boolean
            ' OPERATING_REPORT_DISPLAY moet altijd doorgestuurd worden (user requirement)
            Return True ' Always return True to always collect and send data
        End Function
    End Class
    
    ''' <summary>
    ''' Simplified NC Status at Alarm Collector
    ''' </summary>
    Public Class NCStatusAtAlarmCollector
        Inherits MacManDataCollectorBase
        
        Public Sub New(dataApi As ClassOspApi, machineIP As String, logger As ILogger, Optional collectionInterval As Integer = 30)
            MyBase.New(dataApi, machineIP, "NC_STATUS_AT_ALARM_DISPLAY", logger, collectionInterval)
        End Sub
        
        Public Overrides Function CollectData(Optional lastProcessedDate As DateTime? = Nothing, Optional batchSize As Integer = 50, Optional skipLocalTracking As Boolean = False) As CollectionResult
            Dim result As New CollectionResult()
            
            Try
                Console.WriteLine($"🔄 Collecting NC Status at Alarm data from {MachineIP}...")
                
                If Not UpdateMacManData() Then
                    result.Success = False
                    result.ErrorMessage = "Failed to update MacMan data"
                    Return result
                End If
                
                ' Get real max records from API like QOkumaConnect
                Dim maxRecords = GetMaxRecords()
                Dim recordsToProcess = Math.Min(maxRecords, batchSize)
                
                Console.WriteLine($"📊 Processing {recordsToProcess} of {maxRecords} available NC status at alarm records...")
                
                ' Collect records - stop early if records are not newer (sorted by date DESC like QOkumaConnect)
                Dim recordsProcessed As Integer = 0
                For i As Integer = 0 To recordsToProcess - 1
                    Try
                        ' Get record date first to check if it's newer (do this first for efficiency like QOkumaConnect)
                        Dim recordDate = GetRecordDate(i)
                        
                        ' Stop the loop if record is not newer than last processed date
                        ' (records are sorted with newest first, so all following are also older)
                        If Not IsRecordNewer(recordDate, lastProcessedDate) Then
                            Console.WriteLine($"⏹️ Stopped at record {i + 1}/{recordsToProcess} - found older NC status record ({If(recordDate.HasValue, recordDate.Value.ToString("yyyy-MM-dd HH:mm:ss"), "N/A")})")
                            Exit For
                        End If
                        
                        Dim record As New MacManRecord() With {
                            .MachineIP = MachineIP,
                            .ScreenType = ScreenType,
                            .RecordIndex = i
                        }
                        recordsProcessed += 1
                        
                        ' Collect NC status at alarm fields (same indices as QOkumaConnect)
                        record.Data("AlarmNo") = DataApi.GetByString(1, 5007, i, 0, 9).Trim()
                        record.Data("AlarmCode") = DataApi.GetByString(1, 5008, i, 0, 9).Trim()
                        record.Data("AlarmMessage") = DataApi.GetByString(1, 5009, i, 0, 9).Trim()
                        
                        ' Get real NC status date/time from API (same indices as QOkumaConnect)
                        Dim ncStatusDateStr = DataApi.GetByString(1, 5068, i, 0, 9).Trim()
                        Dim ncStatusTimeStr = DataApi.GetByString(1, 5069, i, 0, 9).Trim()
                        
                        record.Data("Date") = ncStatusDateStr
                        record.Data("Time") = ncStatusTimeStr
                        
                        ' Set ProcessedDate from the already parsed recordDate (like QOkumaConnect)
                        record.Data("ProcessedDate") = recordDate
                        record.Data("RawNCStatusDate") = ncStatusDateStr
                        record.Data("RawNCStatusTime") = ncStatusTimeStr
                        
                        ' Set CollectionTimestamp like QOkumaConnect
                        record.CollectionTimestamp = DateTime.Now
                        
                        result.Records.Add(record)
                        
                    Catch ex As Exception
                        Console.WriteLine($"⚠️ Error processing NC status alarm record {i}: {ex.Message}")
                    End Try
                Next
                
                result.RecordsCollected = result.Records.Count
                result.Success = True
                
                UpdateDateTracking(result)
                
                Console.WriteLine($"✅ NC Status at Alarm: Collected {result.RecordsCollected} records")
                
                ' Print data as array of dictionaries
                If result.Records.Count > 0 Then
                    Console.WriteLine($"📋 NC_STATUS_AT_ALARM_DISPLAY Data Array:")
                    For i As Integer = 0 To result.Records.Count - 1
                        Dim dataRecord = DirectCast(result.Records(i), MacManRecord)
                        Console.WriteLine($"   [{i}] = {{")
                        For Each kvp In dataRecord.Data
                            Console.WriteLine($"      ""{kvp.Key}"": ""{kvp.Value}""")
                        Next
                        Console.WriteLine($"   }}")
                    Next
                End If
                
            Catch ex As Exception
                result.Success = False
                result.ErrorMessage = $"NC status at alarm collection failed: {ex.Message}"
                Console.WriteLine($"❌ {result.ErrorMessage}")
            End Try
            
            Return result
        End Function
        
        Protected Overrides Function GetRecordDate(recordIndex As Integer) As DateTime?
            Try
                ' Use the correct major indices for NC_STATUS_AT_ALARM_DISPLAY date and time (same as QOkumaConnect)
                Dim dateStr = DataApi.GetByString(1, 5068, recordIndex, 0, 9).Trim()
                Dim timeStr = DataApi.GetByString(1, 5069, recordIndex, 0, 9).Trim()
                
                Console.WriteLine($"🔍 GetRecordDate for NC status alarm {recordIndex}: Date='{dateStr}', Time='{timeStr}'")
                
                If String.IsNullOrEmpty(dateStr) OrElse String.IsNullOrEmpty(timeStr) Then
                    Console.WriteLine($"⚠️ Empty date/time for NC status alarm record {recordIndex}, stopping collection")
                    Return Nothing
                End If
                
                ' Parse NC status at alarm date and time
                Dim parsedDate = ParseNCStatusAlarmDateTime(dateStr, timeStr)
                Console.WriteLine($"🔍 Parsed NC status alarm date for record {recordIndex}: {parsedDate}")
                Return parsedDate
                
            Catch ex As Exception
                Console.WriteLine($"❌ Error getting NC status alarm record date for {recordIndex}: {ex.Message}, stopping collection")
                Return Nothing
            End Try
        End Function
        
        Protected Overrides Function GetMaxRecords() As Integer
            Try
                ' Use the correct major index for NC_STATUS_AT_ALARM_DISPLAY max records (same as QOkumaConnect)
                Dim maxRecordsStr = DataApi.GetByString(1, 2096, 0, 0, 9).Trim()
                If String.IsNullOrEmpty(maxRecordsStr) Then
                    Console.WriteLine("Warning: Empty max records string for NC status at alarm, using fallback")
                    Return 256 ' Fallback value
                End If
                
                Dim maxRecords As Integer
                If Integer.TryParse(maxRecordsStr, maxRecords) Then
                    Console.WriteLine($"🔍 NC Status at Alarm max records: {maxRecords}")
                    Return maxRecords
                Else
                    Console.WriteLine($"Warning: Could not parse max records for NC status at alarm: '{maxRecordsStr}', using fallback")
                    Return 256 ' Fallback value
                End If
            Catch ex As Exception
                Console.WriteLine($"Warning: Could not get max records for NC status at alarm: {ex.Message}")
                Return 256 ' Fallback value
            End Try
        End Function
        
        Private Function ParseNCStatusAlarmDateTime(dateStr As String, timeStr As String) As DateTime?
            Try
                Console.WriteLine($"🔍 ParseNCStatusAlarmDateTime: Parsing date='{dateStr}', time='{timeStr}'")
                
                If String.IsNullOrEmpty(dateStr) OrElse String.IsNullOrEmpty(timeStr) Then
                    Console.WriteLine($"⚠️ Empty date or time string, stopping collection")
                    Return Nothing
                End If
                
                ' Try different date formats (same logic as QOkumaConnect)
                Dim parsedDate As DateTime
                Dim dateTimeString = $"{dateStr} {timeStr}"
                
                ' Try standard format: "yyyy/MM/dd HH:mm:ss"
                If DateTime.TryParseExact(dateTimeString, "yyyy/MM/dd HH:mm:ss", Nothing, Globalization.DateTimeStyles.None, parsedDate) Then
                    Console.WriteLine($"✅ Parsed NC status alarm datetime (format 1): {parsedDate}")
                    Return parsedDate
                End If
                
                ' Try alternative format: "yyyy-MM-dd HH:mm:ss"
                If DateTime.TryParseExact(dateTimeString, "yyyy-MM-dd HH:mm:ss", Nothing, Globalization.DateTimeStyles.None, parsedDate) Then
                    Console.WriteLine($"✅ Parsed NC status alarm datetime (format 2): {parsedDate}")
                    Return parsedDate
                End If
                
                ' Try general parsing
                If DateTime.TryParse(dateTimeString, parsedDate) Then
                    Console.WriteLine($"✅ Parsed NC status alarm datetime (general): {parsedDate}")
                    Return parsedDate
                End If
                
                Console.WriteLine($"❌ Could not parse NC status alarm datetime: '{dateTimeString}', stopping collection")
                Return Nothing
                
            Catch ex As Exception
                Console.WriteLine($"❌ Exception parsing NC status alarm datetime: {ex.Message}, stopping collection")
                Return Nothing
            End Try
        End Function
    End Class
    
    ''' <summary>
    ''' Simplified Operating Status Collector
    ''' </summary>
    Public Class OperatingStatusCollector
        Inherits MacManDataCollectorBase
        
        Public Sub New(dataApi As ClassOspApi, machineIP As String, logger As ILogger, Optional collectionInterval As Integer = 5)
            MyBase.New(dataApi, machineIP, "OPERATINGSTATUS", logger, collectionInterval)
        End Sub
        
        Public Overrides Function CollectData(Optional lastProcessedDate As DateTime? = Nothing, Optional batchSize As Integer = 50, Optional skipLocalTracking As Boolean = False) As CollectionResult
            Dim result As New CollectionResult()
            
            Try
                Console.WriteLine($"🔄 Collecting Operating Status from {MachineIP}...")
                
                If Not UpdateMacManData() Then
                    result.Success = False
                    result.ErrorMessage = "Failed to update MacMan data"
                    Return result
                End If
                
                ' Get status values
                Dim alarmStatus As String = DataApi.GetByString(14, 1, 0, 0, 8).Trim()
                Dim programStatus As String = DataApi.GetByString(14, 1004, 0, 0, 8).Trim()
                
                ' Calculate operating status
                Dim operatingStatus As String = CalculateOperatingStatus(alarmStatus, programStatus)
                
                ' Create status record
                Dim record As New MacManRecord() With {
                    .MachineIP = MachineIP,
                    .ScreenType = ScreenType,
                    .RecordIndex = 0
                }
                
                record.Data("value") = operatingStatus
                record.Data("raw_value") = alarmStatus
                record.Data("ProcessedDate") = DateTime.Now
                
                ' Set CollectionTimestamp like QOkumaConnect
                record.CollectionTimestamp = DateTime.Now
                
                result.Records.Add(record)
                result.RecordsCollected = 1
                result.Success = True
                
                UpdateDateTracking(result)
                
                Console.WriteLine($"✅ Operating Status: {operatingStatus}")
                
            Catch ex As Exception
                result.Success = False
                result.ErrorMessage = $"Operating status collection failed: {ex.Message}"
                Console.WriteLine($"❌ {result.ErrorMessage}")
            End Try
            
            Return result
        End Function
        
        Private Function CalculateOperatingStatus(alarmStatus As String, programStatus As String) As String
            Try
                Dim alarmValue As Integer = 0
                Dim programValue As Integer = 0
                
                Integer.TryParse(alarmStatus, alarmValue)
                Integer.TryParse(programStatus, programValue)
                
                If alarmValue = 1 Then
                    Return "alarm"
                ElseIf programValue = 1 Then
                    Return "running"
                Else
                    Return "stand-by"
                End If
                
            Catch ex As Exception
                Console.WriteLine($"⚠️ Error calculating operating status: {ex.Message}")
                Return "unknown"
            End Try
        End Function
    End Class

End Namespace
