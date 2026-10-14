' message.vbs <info|warn|error|confirm> "text"   ("|" starts a new line)
' Exit code is the button pressed (6 = Yes, 7 = No).
Dim kind, text, flags
kind = WScript.Arguments(0)
text = Replace(WScript.Arguments(1), "|", vbCrLf)
Select Case kind
    Case "error":   flags = vbCritical
    Case "warn":    flags = vbExclamation
    Case "confirm": flags = vbYesNo + vbQuestion + vbDefaultButton2
    Case Else:      flags = vbInformation
End Select
WScript.Quit MsgBox(text, flags + vbSystemModal, "Inventory Manager Setup")
