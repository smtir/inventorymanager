' shortcuts.vbs create "<path to InventoryManager.exe>"   |   shortcuts.vbs remove
' Desktop and Start menu shortcuts for all users.
Dim sh, fso, mode, folder, path, link, target
Set sh = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
mode = WScript.Arguments(0)
For Each folder In Array(sh.SpecialFolders("AllUsersDesktop"), sh.SpecialFolders("AllUsersPrograms"))
    path = folder & "\Inventory Manager.lnk"
    If mode = "create" Then
        target = WScript.Arguments(1)
        Set link = sh.CreateShortcut(path)
        link.TargetPath = target
        link.WorkingDirectory = fso.GetParentFolderName(target)
        link.IconLocation = target & ",0"
        link.Description = "Inventory Manager - products, sales and stock"
        link.Save
    ElseIf fso.FileExists(path) Then
        fso.DeleteFile path, True
    End If
Next
