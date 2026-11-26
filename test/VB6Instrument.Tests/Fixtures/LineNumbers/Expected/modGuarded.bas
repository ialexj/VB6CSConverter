Attribute VB_Name = "modGuarded"
Option Explicit

Public Sub Guarded()
10        On Error GoTo Bail
          Dim x As Long
20        x = 1
30        Exit Sub
Bail:
40        x = 0
End Sub
