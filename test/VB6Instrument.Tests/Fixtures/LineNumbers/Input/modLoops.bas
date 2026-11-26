Attribute VB_Name = "modLoops"
Option Explicit

Public Sub Loops(ByVal n As Long)
    Dim i As Long, total As Long
    For i = 1 To n
        total = total + i
    Next i
    Do While total < 100
        total = total * 2
    Loop
    Do
        total = total - 1
    Loop Until total < 10
End Sub
