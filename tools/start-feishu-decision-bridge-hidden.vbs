Option Explicit

Const EXIT_INVALID = 64

Dim arguments, fileSystem, shell, pwshPath, startPath, command, exitCode
Set arguments = WScript.Arguments
If arguments.Count <> 2 Then WScript.Quit EXIT_INVALID

pwshPath = arguments(0)
startPath = arguments(1)
If Not IsSafeAbsoluteFile(pwshPath, "pwsh.exe") Then WScript.Quit EXIT_INVALID
If Not IsSafeAbsoluteFile(startPath, "start-feishu-decision-bridge.ps1") Then WScript.Quit EXIT_INVALID

command = QuoteArgument(pwshPath) & " -NoProfile -NonInteractive -ExecutionPolicy Bypass -File " & QuoteArgument(startPath)
Set shell = CreateObject("WScript.Shell")
exitCode = shell.Run(command, 0, True)
WScript.Quit exitCode

Function IsSafeAbsoluteFile(value, expectedName)
  Dim index, code
  IsSafeAbsoluteFile = False
  If Len(value) = 0 Or InStr(value, Chr(34)) > 0 Then Exit Function
  For index = 1 To Len(value)
    code = AscW(Mid(value, index, 1))
    If code < 0 Then code = code + 65536
    If code < 32 Or code = 127 Then Exit Function
  Next
  Set fileSystem = CreateObject("Scripting.FileSystemObject")
  If Not fileSystem.FileExists(value) Then Exit Function
  If StrComp(fileSystem.GetAbsolutePathName(value), value, vbTextCompare) <> 0 Then Exit Function
  If StrComp(fileSystem.GetFileName(value), expectedName, vbTextCompare) <> 0 Then Exit Function
  IsSafeAbsoluteFile = True
End Function

Function QuoteArgument(value)
  QuoteArgument = Chr(34) & value & Chr(34)
End Function
