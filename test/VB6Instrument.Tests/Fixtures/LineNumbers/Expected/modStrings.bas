Attribute VB_Name = "modStrings"
Option Explicit

Public Sub Messages()
          Dim s As String
10        s = "End Sub"
20        s = "don't: do ""this"""
30        s = "a:b"
          Rem a rem comment
40        s = s & ": ok"
End Sub

Public Sub Apostrophes()
          Dim s As String
10        s = "it's fine"
End Sub
