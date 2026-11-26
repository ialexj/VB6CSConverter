Attribute VB_Name = "modLoops"
Option Explicit

Public Sub Loops(ByVal n As Long)
          Dim i As Long, total As Long
10        For i = 1 To n
20            total = total + i
30        Next i
40        Do While total < 100
50            total = total * 2
60        Loop
70        Do
80            total = total - 1
90        Loop Until total < 10
End Sub
