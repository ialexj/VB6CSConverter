Attribute VB_Name = "modGuarded"
Option Explicit

Public Sub Guarded()
    On Error GoTo Bail
    Dim x As Long
    x = 1
    Exit Sub
Bail:
    x = 0
End Sub
