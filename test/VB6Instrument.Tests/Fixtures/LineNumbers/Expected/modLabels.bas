Attribute VB_Name = "modLabels"
Option Explicit

Public Sub Retry()
          Dim attempts As Long
10        attempts = 0
RetryPoint:
20        attempts = attempts + 1
30        If attempts < 3 Then GoTo RetryPoint
40        GoSub Helper
50        Exit Sub
Helper:
60        attempts = attempts * 2
70        Return
End Sub

Public Sub IndentedLabel()
          Dim x As Long
10 InnerLabel: x = 1
20        GoTo InnerLabel
End Sub
