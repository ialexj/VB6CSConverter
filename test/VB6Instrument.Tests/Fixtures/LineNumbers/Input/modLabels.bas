Attribute VB_Name = "modLabels"
Option Explicit

Public Sub Retry()
    Dim attempts As Long
    attempts = 0
RetryPoint:
    attempts = attempts + 1
    If attempts < 3 Then GoTo RetryPoint
    GoSub Helper
    Exit Sub
Helper:
    attempts = attempts * 2
    Return
End Sub

Public Sub IndentedLabel()
    Dim x As Long
InnerLabel: x = 1
    GoTo InnerLabel
End Sub
