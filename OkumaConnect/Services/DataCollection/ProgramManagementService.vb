Imports System
Imports System.IO
Imports System.Threading.Tasks
Imports System.Text
Imports System.Net.NetworkInformation
Imports OkumaConnect.Models
Imports OkumaConnect.Services.Logging
Imports OkumaConnect.Services.OpcClient

Namespace Services.DataCollection

    ''' <summary>
    ''' Service voor het afhandelen van ProgramManagement operaties
    ''' Beheert de volledige workflow van programma kopiëren tot OPC UA updates
    ''' </summary>
    Public Class ProgramManagementService
        Implements IProgramManagementService, IDisposable
        
        Private ReadOnly _logger As ILogger
        Private ReadOnly _opcuaManager As OpcUaManager
        
        ' Machine connection management for ClassOspApi
        Private ReadOnly _machineConnections As New Dictionary(Of String, ClassOspApi)()
        Private ReadOnly _connectionLock As New Object()
        
        Public Sub New(logger As ILogger, opcuaManager As OpcUaManager)
            _logger = logger
            _opcuaManager = opcuaManager
            _logger.LogInfo("🎛️ ProgramManagementService initialized with ClassOspApi integration")
        End Sub

        Public Async Function GetDataAsync(nodeId As String) As Task(Of ApiDataResult) Implements IProgramManagementService.GetDataAsync
            Try
                _logger.LogInfo($"🎛️ ProgramManagement.Ctrl triggered for: {nodeId}")
                
                ' Extract machine information from nodeId
                ' Pattern: ns=2;s=Okuma.Machines.1 - MU-10000H.ProgramManagement.Ctrl
                Dim machineName = ExtractMachineName(nodeId)
                Dim machineId = ExtractMachineId(machineName)
                
                ' Get machine IP from OPC UA (similar to DataRouter)
                Dim machineIP = Await GetMachineIPAddress(machineName)
                
                ' Read current ProgramManagement values from OPC UA
                Dim filepathNodeId = $"ns=2;s=Okuma.Machines.{machineName}.ProgramManagement.Filepath"
                Dim idNodeId = $"ns=2;s=Okuma.Machines.{machineName}.ProgramManagement.Id"
                Dim mainFileNodeId = $"ns=2;s=Okuma.Machines.{machineName}.ProgramManagement.MainFile"
                
                _logger.LogDebug($"📖 Reading ProgramManagement parameters...")
                Dim filepathValue = Await _opcuaManager.ReadNodeValue(filepathNodeId)
                Dim programIdValue = Await _opcuaManager.ReadNodeValue(idNodeId)
                Dim mainFileValue = Await _opcuaManager.ReadNodeValue(mainFileNodeId)
                
                Dim filepath = If(filepathValue?.ToString(), "")
                Dim programId = If(programIdValue?.ToString(), "")
                Dim mainFileFromOpcua = If(mainFileValue?.ToString(), "")
                
                _logger.LogInfo($"📋 PROGRAM PARAMETERS for machine {machineId}:")
                _logger.LogInfo($"   📄 Filepath: {If(String.IsNullOrEmpty(filepath), "NULL", filepath)}")
                _logger.LogInfo($"   🆔 Program ID: {If(String.IsNullOrEmpty(programId), "NULL", programId)}")
                _logger.LogInfo($"   📁 MainFile: {If(String.IsNullOrEmpty(mainFileFromOpcua), "NULL", mainFileFromOpcua)}")
                
                ' Start the ProgramManagement workflow
                Dim workflowResult = Await StartProgramManagement(machineId, machineName, machineIP, filepath, programId, mainFileFromOpcua)
                
                Return New ApiDataResult With {
                    .Value = workflowResult.Success,
                    .DataType = "Boolean",
                    .Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    .ErrorMessage = workflowResult.ErrorMessage
                }
                
            Catch ex As Exception
                _logger.LogError($"❌ Error in ProgramManagement service: {ex.Message}")
                Return New ApiDataResult With {
                    .Value = False,
                    .DataType = "Boolean", 
                    .Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                }
            End Try
        End Function
        
        ''' <summary>
        ''' Start ProgramManagement workflow (Ctrl = True)
        ''' </summary>
        Public Async Function StartProgramManagement(machineId As String, machineName As String, machineIpAddress As String, filepath As String, programId As String, mainFileFromOpcua As String) As Task(Of (Success As Boolean, ErrorMessage As String))
            Dim unexpectedException As Exception = Nothing
            Dim result As Boolean = False
            
            Try
                _logger.LogInfo($"🚀 PROGRAM MANAGEMENT START: Initiating workflow for machine {machineId} ({machineName})")
                _logger.LogInfo($"   🌐 IP Address: {machineIpAddress}")
                _logger.LogInfo($"   📄 Filepath: {filepath}")
                _logger.LogInfo($"   🆔 Program ID: {programId}")
                _logger.LogInfo($"   📁 MainFile: {mainFileFromOpcua}")
                _logger.LogInfo($"   ⏰ Started at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
                
                Dim errorMessage As String = ""
                Dim hasError As Boolean = False
                
                ' Stap 1: Programma bestand kopiëren
                Dim copySuccess = Await CopyProgramFile(machineId, machineName, machineIpAddress, filepath, programId)
                If Not copySuccess Then
                    errorMessage = $"File copy failed: Source file does not exist - {filepath}"
                    hasError = True
                    _logger.LogError($"❌ PROGRAM MANAGEMENT ERROR: File copy failed for machine {machineId}")
                End If
                
                ' Stap 2: API call verzenden (alleen als file copy succesvol was)
                Dim apiSuccess As Boolean = True
                If copySuccess Then
                    apiSuccess = Await SendApiCall(machineId, machineName, machineIpAddress, filepath, programId, mainFileFromOpcua)
                    If Not apiSuccess Then
                        errorMessage = $"API call failed for machine {machineIpAddress}"
                        hasError = True
                        _logger.LogError($"❌ PROGRAM MANAGEMENT ERROR: API call failed for machine {machineId}")
                    End If
                End If
                
                ' Stap 3: OPC UA server updaten
                ' - Altijd Stat = True zetten
                ' - Bij error ook Exception node updaten
                Dim opcuaStatSuccess = Await UpdateOpcuaStatus(machineId, machineName, True)
                If Not opcuaStatSuccess Then
                    _logger.LogError($"❌ PROGRAM MANAGEMENT ERROR: OPC UA Stat update failed for machine {machineId}")
                End If
                
                ' Update Exception node als er een error was
                If hasError Then
                    Dim opcuaExceptionSuccess = Await UpdateOpcuaException(machineId, machineName, errorMessage)
                    If Not opcuaExceptionSuccess Then
                        _logger.LogError($"❌ PROGRAM MANAGEMENT ERROR: OPC UA Exception update failed for machine {machineId}")
                    End If
                    
                    _logger.LogError($"❌ PROGRAM MANAGEMENT COMPLETED WITH ERRORS for machine {machineId}: {errorMessage}")
                    Return (Success:=False, ErrorMessage:=errorMessage)
                Else
                    _logger.LogInfo($"✅ PROGRAM MANAGEMENT SUCCESS: Complete workflow finished for machine {machineId}")
                    Return (Success:=True, ErrorMessage:="")
                End If
                
            Catch ex As Exception
                _logger.LogError($"❌ PROGRAM MANAGEMENT ERROR for machine {machineId}: {ex.Message}")
                _logger.LogError($"   Exception type: {ex.GetType().Name}")
                If ex.InnerException IsNot Nothing Then
                    _logger.LogError($"   Inner exception: {ex.InnerException.Message}")
                End If
                
                ' Schrijf exception naar bestand
                WriteExceptionToFile(ex, "StartProgramManagement", machineId, $"Filepath: {filepath}, ProgramId: {programId}, MainFile: {mainFileFromOpcua}, IP: {machineIpAddress}")
                
                ' Sla exception op voor verwerking buiten try-catch
                unexpectedException = ex
            End Try
            
            ' Handle unexpected exception buiten try-catch
            If unexpectedException IsNot Nothing Then
                Dim exceptionMessage = $"Unexpected error in ProgramManagement: {unexpectedException.Message}"
                Await HandleProgramManagementException(machineId, machineName, exceptionMessage)
                Return (Success:=False, ErrorMessage:=exceptionMessage)
            End If
            
            ' This should not be reached, but just in case
            Return (Success:=False, ErrorMessage:="Unknown error occurred")
        End Function
        
        ''' <summary>
        ''' Helper methode om OPC UA nodes te updaten na een exception (buiten catch block)
        ''' </summary>
        Private Async Function HandleProgramManagementException(machineId As String, machineName As String, exceptionMessage As String) As Task(Of Boolean)
            Try
                ' Zet Stat nog steeds op True
                Await UpdateOpcuaStatus(machineId, machineName, True)
                
                ' Update Exception node met error message
                Await UpdateOpcuaException(machineId, machineName, exceptionMessage)
            Catch opcuaEx As Exception
                _logger.LogError($"❌ Could not update OPC UA nodes after exception: {opcuaEx.Message}")
            End Try
            
            Return False
        End Function
        
        ''' <summary>
        ''' Stap 1: Kopieer programma bestand van schijf X naar C:\temp\<ip_address>\
        ''' </summary>
        Private Async Function CopyProgramFile(machineId As String, machineName As String, machineIpAddress As String, filepath As String, programId As String) As Task(Of Boolean)
            Try
                _logger.LogInfo($"📁 STEP 1 - FILE COPY: Starting file copy for machine {machineId}")
                _logger.LogInfo($"   Machine: {machineId} ({machineName}) - IP: {machineIpAddress}")
                _logger.LogInfo($"   Source: {filepath}")
                _logger.LogInfo($"   Target: C:\temp\{machineIpAddress}\")
                _logger.LogInfo($"   Program ID: {programId}")
                
                ' Valideer input parameters
                If String.IsNullOrEmpty(filepath) Then
                    _logger.LogWarning($"⚠️ STEP 1 WARNING: Filepath is empty for machine {machineId}")
                    Console.WriteLine($"📁 FILE COPY: No filepath provided for machine {machineId} - skipping file copy")
                    Return True ' Continue workflow even without file copy
                End If
                
                ' Construct source and target paths
                Dim sourcePath = filepath
                Dim targetDirectory = Path.Combine("C:\temp", machineIpAddress)
                Dim fileName = Path.GetFileName(filepath)
                If String.IsNullOrEmpty(fileName) Then
                    fileName = $"program_{programId}_{machineId}.nc"
                End If
                Dim targetPath = Path.Combine(targetDirectory, fileName)
                
                _logger.LogInfo($"📂 FILE COPY DETAILS:")
                _logger.LogInfo($"   Source File: {sourcePath}")
                _logger.LogInfo($"   Target File: {targetPath}")
                _logger.LogInfo($"   File Name: {fileName}")
                
                Console.WriteLine($"📁 COPYING: Program file for machine {machineId}")
                Console.WriteLine($"   Machine: {machineName} (IP: {machineIpAddress})")
                Console.WriteLine($"   From: {sourcePath}")
                Console.WriteLine($"   To: {targetPath}")
                Console.WriteLine($"   Program ID: {programId}")
                
                ' Ensure target directory exists
                Try
                    If Not Directory.Exists(targetDirectory) Then
                        Directory.CreateDirectory(targetDirectory)
                        _logger.LogInfo($"📂 Created target directory: {targetDirectory}")
                    End If
                Catch ex As Exception
                    _logger.LogError($"❌ STEP 1 FAILED: Could not create target directory {targetDirectory}: {ex.Message}")
                    WriteExceptionToFile(ex, "CopyProgramFile - Create Directory", machineId, $"Target Directory: {targetDirectory}")
                    Return False
                End Try
                
                ' Check if source file exists
                If Not File.Exists(sourcePath) Then
                    _logger.LogError($"❌ STEP 1 FAILED: Source file does not exist: {sourcePath}")
                    Console.WriteLine($"❌ FILE COPY ERROR: Source file not found: {sourcePath}")
                    
                    ' Log this as an error and return false - do not create dummy file
                    _logger.LogError($"   Machine: {machineId} ({machineName})")
                    _logger.LogError($"   Expected source path: {sourcePath}")
                    _logger.LogError($"   Program ID: {programId}")
                    
                    Console.WriteLine($"   Machine: {machineName} (IP: {machineIpAddress})")
                    Console.WriteLine($"   Program ID: {programId}")
                    Console.WriteLine($"   Please verify the source file path is correct")
                    
                    Return False
                Else
                    ' Copy the actual file
                    Try
                        Await Task.Run(Sub() File.Copy(sourcePath, targetPath, True))
                        _logger.LogInfo($"📋 File copied successfully from {sourcePath} to {targetPath}")
                        Console.WriteLine($"✅ File copied successfully to: {targetPath}")
                    Catch ex As Exception
                        _logger.LogError($"❌ STEP 1 FAILED: File copy error: {ex.Message}")
                        WriteExceptionToFile(ex, "CopyProgramFile - File Copy", machineId, $"Source: {sourcePath}, Target: {targetPath}")
                        Return False
                    End Try
                End If
                
                ' Verify file was created/copied
                If File.Exists(targetPath) Then
                    Dim fileInfo = New FileInfo(targetPath)
                    _logger.LogInfo($"✅ STEP 1 COMPLETED: File copy successful for machine {machineId}")
                    _logger.LogInfo($"   Target file: {targetPath}")
                    _logger.LogInfo($"   File size: {fileInfo.Length} bytes")
                    _logger.LogInfo($"   Created: {fileInfo.CreationTime:yyyy-MM-dd HH:mm:ss}")
                    
                    Console.WriteLine($"✅ FILE COPY SUCCESS: {fileName} ({fileInfo.Length} bytes)")
                    Return True
                Else
                    _logger.LogError($"❌ STEP 1 FAILED: Target file was not created: {targetPath}")
                    Return False
                End If
                
            Catch ex As Exception
                _logger.LogError($"❌ STEP 1 ERROR: File copy error for machine {machineId}: {ex.Message}")
                _logger.LogError($"   Exception type: {ex.GetType().Name}")
                Console.WriteLine($"❌ FILE COPY ERROR: {ex.Message}")
                
                ' Schrijf exception naar bestand
                WriteExceptionToFile(ex, "CopyProgramFile - General Error", machineId, $"Filepath: {filepath}, ProgramId: {programId}")
                
                Return False
            End Try
        End Function
        
        ''' <summary>
        ''' Stap 2: Verzend API call naar machine met SelectMainProgramM
        ''' </summary>
        Private Async Function SendApiCall(machineId As String, machineName As String, machineIpAddress As String, filepath As String, programId As String, mainFileFromOpcua As String) As Task(Of Boolean)
            Try
                _logger.LogInfo($"📡 STEP 2 - API CALL: Sending SelectMainProgramM to machine {machineId}")
                _logger.LogInfo($"   Target Machine: {machineId} ({machineName}) - IP: {machineIpAddress}")
                _logger.LogInfo($"   Program File: {filepath}")
                _logger.LogInfo($"   Program ID: {programId}")
                _logger.LogInfo($"   MainFile from OPC UA: {mainFileFromOpcua}")
                
                ' Get or create machine connection
                Dim machineConnection = Await GetMachineConnection(machineName, machineIpAddress)
                If machineConnection Is Nothing Then
                    _logger.LogError($"❌ STEP 2 FAILED: Could not establish connection to machine {machineId}")
                    Return False
                End If
                
                ' Prepare parameters for SelectMainProgramM
                ' Use MainFile from OPC UA instead of extracting from filepath
                If String.IsNullOrEmpty(mainFileFromOpcua) Then
                    _logger.LogError($"❌ STEP 2 FAILED: No MainFile provided from OPC UA for machine {machineId}")
                    Console.WriteLine($"❌ SELECTMAINPROGRAM ERROR: No MainFile provided from OPC UA for machine {machineId}")
                    Return False
                End If
                
                Dim mainFile = mainFileFromOpcua
                
                Dim subFile = "" ' Empty for main program
                Dim programName = "" ' Empty for this implementation
                Dim mode As Short = 0 ' Mode 0 as requested
                
                _logger.LogInfo($"📋 SELECTMAINPROGRAM PARAMETERS:")
                _logger.LogInfo($"   Original Filepath: {If(String.IsNullOrEmpty(filepath), "NULL", filepath)}")
                _logger.LogInfo($"   MainFile from OPC UA: {mainFile}")
                _logger.LogInfo($"   Sub File: {If(String.IsNullOrEmpty(subFile), "NULL", subFile)}")
                _logger.LogInfo($"   Program Name: {If(String.IsNullOrEmpty(programName), "NULL", programName)}")
                _logger.LogInfo($"   Mode: {mode}")
                
                Console.WriteLine($"📡 API CALL: SelectMainProgramM for machine {machineId}")
                Console.WriteLine($"   Machine: {machineName} (IP: {machineIpAddress})")
                Console.WriteLine($"   Original Filepath: {If(String.IsNullOrEmpty(filepath), "NULL", filepath)}")
                Console.WriteLine($"   MainFile from OPC UA: {mainFile}")
                Console.WriteLine($"   Sub File: {If(String.IsNullOrEmpty(subFile), "NULL", subFile)}")
                Console.WriteLine($"   Program Name: {If(String.IsNullOrEmpty(programName), "NULL", programName)}")
                Console.WriteLine($"   Mode: {mode}")
                
                ' Execute SelectMainProgramM
                
                Dim result = machineConnection.SelectMainProgramM(mainFile, subFile, programName, mode)
                
                If result = 0 Then
                    _logger.LogInfo($"✅ STEP 2 COMPLETED: SelectMainProgramM successful for machine {machineId}")
                    _logger.LogInfo($"   Target IP: {machineIpAddress}")
                    _logger.LogInfo($"   Program: {programName} loaded successfully")
                    _logger.LogInfo($"   Machine ready for next step")
                    
                    Console.WriteLine($"✅ SELECTMAINPROGRAM SUCCESS: {mainFile} loaded to machine {machineId}")
                    Console.WriteLine($"   Result Code: {result}")
                    Console.WriteLine($"   Method Log: {machineConnection.MethodLog}")
                    
                    Return True
                Else
                    _logger.LogError($"❌ STEP 2 FAILED: SelectMainProgramM returned error code {result}")
                    _logger.LogError($"   Error Message: {machineConnection.ErrMsg}")
                    _logger.LogError($"   Error Data: {machineConnection.ErrData}")
                    _logger.LogError($"   Error String: {machineConnection.ErrStr}")
                    _logger.LogError($"   Method Log: {machineConnection.MethodLog}")
                    
                    Console.WriteLine($"❌ SELECTMAINPROGRAM ERROR: Machine {machineId} returned error {result}")
                    Console.WriteLine($"   Error: {machineConnection.ErrMsg}")
                    Console.WriteLine($"   Method: {machineConnection.MethodLog}")
                    
                    Return False
                End If
                
            Catch ex As Exception
                _logger.LogError($"❌ STEP 2 ERROR: SelectMainProgramM exception for machine {machineId}: {ex.Message}")
                
                ' Schrijf exception naar bestand
                WriteExceptionToFile(ex, "SendApiCall - SelectMainProgramM", machineId, $"IP: {machineIpAddress}, Filepath: {filepath}, ProgramId: {programId}, MainFile: {mainFileFromOpcua}")
                
                Return False
            End Try
        End Function
        
        ''' <summary>
        ''' Update OPC UA Exception node met error message
        ''' </summary>
        Private Async Function UpdateOpcuaException(machineId As String, machineName As String, exceptionMessage As String) As Task(Of Boolean)
            Try
                _logger.LogInfo($"⚠️ OPC UA EXCEPTION UPDATE: Setting Exception message for machine {machineId}")
                _logger.LogInfo($"   Machine: {machineId} ({machineName})")
                _logger.LogInfo($"   Node: ProgramManagement.Exception")
                _logger.LogInfo($"   Message: {exceptionMessage}")
                
                ' Construct exception node ID
                Dim exceptionNodeId = $"ns=2;s=Okuma.Machines.{machineName}.ProgramManagement.Exception"
                _logger.LogDebug($"   Full Node ID: {exceptionNodeId}")
                
                ' Write exception message to OPC UA server
                Dim writeSuccess = Await _opcuaManager.WriteNodeValue(exceptionNodeId, exceptionMessage)
                
                If writeSuccess Then
                    _logger.LogInfo($"✅ OPC UA EXCEPTION UPDATE SUCCESS: Exception message written for machine {machineId}")
                    _logger.LogInfo($"   ProgramManagement.Exception = {exceptionMessage}")
                    
                    Console.WriteLine($"⚠️ OPC UA EXCEPTION UPDATED: Machine {machineId} Exception = {exceptionMessage}")
                    Console.WriteLine($"   Node: {exceptionNodeId}")
                    Console.WriteLine($"   Status: Successfully written to OPC UA server")
                    
                    Return True
                Else
                    _logger.LogError($"❌ OPC UA EXCEPTION UPDATE FAILED: Could not write exception to OPC UA server for machine {machineId}")
                    _logger.LogError($"   Node: {exceptionNodeId}")
                    _logger.LogError($"   Attempted message: {exceptionMessage}")
                    Return False
                End If
                
            Catch ex As Exception
                _logger.LogError($"❌ OPC UA EXCEPTION UPDATE ERROR for machine {machineId}: {ex.Message}")
                
                ' Schrijf exception naar bestand
                WriteExceptionToFile(ex, "UpdateOpcuaException", machineId, $"Exception Message: {exceptionMessage}")
                
                Return False
            End Try
        End Function
        
        ''' <summary>
        ''' Stap 3: Update OPC UA server met nieuwe status
        ''' </summary>
        Private Async Function UpdateOpcuaStatus(machineId As String, machineName As String, status As Boolean) As Task(Of Boolean)
            Try
                Dim statusText = If(status, "TRUE", "FALSE")
                _logger.LogInfo($"🔄 STEP 3 - OPC UA UPDATE: Setting Stat = {statusText} for machine {machineId}")
                _logger.LogInfo($"   Machine: {machineId} ({machineName})")
                _logger.LogInfo($"   Node: ProgramManagement.Stat")
                _logger.LogInfo($"   New Value: {statusText}")
                
                ' Construct node ID
                Dim statNodeId = $"ns=2;s=Okuma.Machines.{machineName}.ProgramManagement.Stat"
                _logger.LogDebug($"   Full Node ID: {statNodeId}")
                
                ' Write to OPC UA server
                Dim writeSuccess = Await _opcuaManager.WriteNodeValue(statNodeId, status)
                
                If writeSuccess Then
                    _logger.LogInfo($"✅ STEP 3 COMPLETED: OPC UA update successful for machine {machineId}")
                    _logger.LogInfo($"   ProgramManagement.Stat = {statusText}")
                    _logger.LogInfo($"   Machine status synchronized")
                    
                    Console.WriteLine($"🔄 OPC UA UPDATED: Machine {machineId} ProgramManagement.Stat = {statusText}")
                    Console.WriteLine($"   Node: {statNodeId}")
                    Console.WriteLine($"   Status: Successfully written to OPC UA server")
                    
                    Return True
                Else
                    _logger.LogError($"❌ STEP 3 FAILED: Could not write to OPC UA server for machine {machineId}")
                    _logger.LogError($"   Node: {statNodeId}")
                    _logger.LogError($"   Attempted value: {statusText}")
                    Return False
                End If
                
            Catch ex As Exception
                _logger.LogError($"❌ STEP 3 ERROR: OPC UA update failed for machine {machineId}: {ex.Message}")
                
                ' Schrijf exception naar bestand
                WriteExceptionToFile(ex, "UpdateOpcuaStatus", machineId, $"Status: {status}, Node: ProgramManagement.Stat")
                
                Return False
            End Try
        End Function
        
        ''' <summary>
        ''' Extract machine name from nodeId
        ''' </summary>
        Private Function ExtractMachineName(nodeId As String) As String
            Try
                ' Pattern: ns=2;s=Okuma.Machines.<MachineName>.ProgramManagement.Ctrl
                If nodeId.Contains("Okuma.Machines.") AndAlso nodeId.Contains(".ProgramManagement.") Then
                    Dim startIndex = nodeId.IndexOf("Okuma.Machines.") + "Okuma.Machines.".Length
                    Dim endIndex = nodeId.IndexOf(".ProgramManagement.")
                    
                    If startIndex > 0 AndAlso endIndex > startIndex Then
                        Return nodeId.Substring(startIndex, endIndex - startIndex)
                    End If
                End If
                
                Return "Unknown"
                
            Catch ex As Exception
                _logger.LogError($"Error extracting machine name from {nodeId}: {ex.Message}")
                Return "Unknown"
            End Try
        End Function
        
        ''' <summary>
        ''' Extract machine ID from machine name (first part before " - ")
        ''' </summary>
        Private Function ExtractMachineId(machineName As String) As String
            Try
                If machineName.Contains(" - ") Then
                    Return machineName.Split(New String() {" - "}, StringSplitOptions.None)(0)
                End If
                Return machineName
            Catch ex As Exception
                _logger.LogError($"Error extracting machine ID from {machineName}: {ex.Message}")
                Return machineName
            End Try
        End Function
        
        ''' <summary>
        ''' Get machine IP address from OPC UA configuration
        ''' </summary>
        Private Async Function GetMachineIPAddress(machineName As String) As Task(Of String)
            Try
                Dim machineConfigNodeId = $"ns=2;s=Okuma.Machines.{machineName}.MachineConfig.IPAddress"
                Dim ipAddress = Await _opcuaManager.ReadNodeValue(machineConfigNodeId)
                
                If ipAddress IsNot Nothing Then
                    Dim ipStr = ipAddress.ToString().Trim()
                    _logger.LogInfo($"🌐 Using IP address for {machineName}: {ipStr}")
                    Return ipStr
                End If
                
                _logger.LogWarning($"⚠️ No IP address configured for machine {machineName}")
                Return "127.0.0.1" ' Fallback
                
            Catch ex As Exception
                _logger.LogError($"❌ Error reading IP address for machine {machineName}: {ex.Message}")
                Return "127.0.0.1" ' Fallback
            End Try
        End Function
        
        ''' <summary>
        ''' Get or create machine connection for ClassOspApi
        ''' </summary>
        Private Async Function GetMachineConnection(machineName As String, machineIpAddress As String) As Task(Of ClassOspApi)
            Try
                SyncLock _connectionLock
                    ' Check if connection already exists
                    If _machineConnections.ContainsKey(machineName) Then
                        Dim existingConnection = _machineConnections(machineName)
                        If existingConnection IsNot Nothing Then
                            _logger.LogDebug($"🔗 Using existing connection for machine: {machineName}")
                            Return existingConnection
                        End If
                    End If
                End SyncLock
                
                _logger.LogInfo($"🔗 Creating new ClassOspApi connection for machine: {machineName}")
                _logger.LogInfo($"   IP Address: {machineIpAddress}")
                
                ' Create new ClassOspApi instance
                Dim ospApi = New ClassOspApi()
                
                ' Connect to machine (assuming MC type and remote API)
                ' You may need to adjust NCTYPE based on machine configuration
                ospApi.ConnectData(machineIpAddress, ClassOspApi.NCTYPE.TYPE_MC, True)
                ospApi.ConnectCommand(machineIpAddress, ClassOspApi.NCTYPE.TYPE_MC, True)
                
                _logger.LogInfo($"✅ ClassOspApi connection established for machine: {machineName}")
                
                ' Store connection for reuse
                SyncLock _connectionLock
                    _machineConnections(machineName) = ospApi
                End SyncLock
                
                Return ospApi
                
            Catch ex As Exception
                _logger.LogError($"❌ Failed to create ClassOspApi connection for machine {machineName}: {ex.Message}")
                _logger.LogError($"   IP Address: {machineIpAddress}")
                _logger.LogError($"   Exception: {ex.GetType().Name}")
                
                ' Write exception to file
                WriteExceptionToFile(ex, "GetMachineConnection", machineName, $"IP: {machineIpAddress}")
                
                Return Nothing
            End Try
        End Function
        
        ''' <summary>
        ''' Schrijft exception details naar een bestand in dezelfde folder met Tag Exception
        ''' </summary>
        Private Sub WriteExceptionToFile(ex As Exception, context As String, machineId As String, Optional additionalInfo As String = "")
            Try
                ' Bepaal de folder van het huidige assembly
                Dim currentFolder = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)
                If String.IsNullOrEmpty(currentFolder) Then
                    currentFolder = Environment.CurrentDirectory
                End If
                
                ' Maak bestandsnaam met timestamp
                Dim timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss")
                Dim fileName = $"Exception_{timestamp}_{machineId}.txt"
                Dim filePath = Path.Combine(currentFolder, fileName)
                
                ' Bouw exception details op
                Dim exceptionDetails = New StringBuilder()
                exceptionDetails.AppendLine("=== PROGRAM MANAGEMENT SERVICE EXCEPTION ===")
                exceptionDetails.AppendLine($"Tag: Exception")
                exceptionDetails.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
                exceptionDetails.AppendLine($"Machine ID: {machineId}")
                exceptionDetails.AppendLine($"Context: {context}")
                exceptionDetails.AppendLine($"Additional Info: {additionalInfo}")
                exceptionDetails.AppendLine()
                
                exceptionDetails.AppendLine("=== EXCEPTION DETAILS ===")
                exceptionDetails.AppendLine($"Exception Type: {ex.GetType().FullName}")
                exceptionDetails.AppendLine($"Message: {ex.Message}")
                exceptionDetails.AppendLine($"Source: {ex.Source}")
                exceptionDetails.AppendLine()
                
                exceptionDetails.AppendLine("=== STACK TRACE ===")
                exceptionDetails.AppendLine(ex.StackTrace)
                exceptionDetails.AppendLine()
                
                ' Inner exceptions
                Dim innerEx = ex.InnerException
                Dim innerCount = 1
                While innerEx IsNot Nothing
                    exceptionDetails.AppendLine($"=== INNER EXCEPTION {innerCount} ===")
                    exceptionDetails.AppendLine($"Type: {innerEx.GetType().FullName}")
                    exceptionDetails.AppendLine($"Message: {innerEx.Message}")
                    exceptionDetails.AppendLine($"Stack Trace: {innerEx.StackTrace}")
                    exceptionDetails.AppendLine()
                    innerEx = innerEx.InnerException
                    innerCount += 1
                End While
                
                exceptionDetails.AppendLine("=== ENVIRONMENT INFO ===")
                exceptionDetails.AppendLine($"Machine Name: {Environment.MachineName}")
                exceptionDetails.AppendLine($"User: {Environment.UserName}")
                exceptionDetails.AppendLine($"OS Version: {Environment.OSVersion}")
                exceptionDetails.AppendLine($"CLR Version: {Environment.Version}")
                exceptionDetails.AppendLine($"Working Directory: {Environment.CurrentDirectory}")
                exceptionDetails.AppendLine()
                
                exceptionDetails.AppendLine("=== END OF EXCEPTION REPORT ===")
                
                ' Schrijf naar bestand
                File.WriteAllText(filePath, exceptionDetails.ToString())
                
                _logger.LogInfo($"📝 Exception written to file: {filePath}")
                Console.WriteLine($"📝 Exception logged to: {fileName}")
                
            Catch fileEx As Exception
                ' Als het schrijven naar bestand faalt, log alleen naar console/logger
                _logger.LogError($"❌ Failed to write exception to file: {fileEx.Message}")
                Console.WriteLine($"❌ Could not write exception to file: {fileEx.Message}")
            End Try
        End Sub

        #Region "IDisposable"
        Private _disposed As Boolean = False
        
        Protected Overridable Sub Dispose(disposing As Boolean)
            If Not _disposed Then
                If disposing Then
                    ' Dispose all machine connections
                    SyncLock _connectionLock
                        For Each kvp In _machineConnections
                            Try
                                If kvp.Value IsNot Nothing Then
                                    kvp.Value.DisconnectData()
                                    kvp.Value.DisconnectCommand()
                                    _logger.LogDebug($"🔌 Disconnected ClassOspApi for machine: {kvp.Key}")
                                End If
                            Catch ex As Exception
                                _logger.LogError($"❌ Error disconnecting machine {kvp.Key}: {ex.Message}")
                            End Try
                        Next
                        _machineConnections.Clear()
                    End SyncLock
                    
                    _logger.LogInfo("🧹 ProgramManagementService disposed with ClassOspApi connections")
                End If
                _disposed = True
            End If
        End Sub
        
        Public Sub Dispose() Implements IDisposable.Dispose
            Dispose(True)
            GC.SuppressFinalize(Me)
        End Sub
        #End Region

    End Class

End Namespace
