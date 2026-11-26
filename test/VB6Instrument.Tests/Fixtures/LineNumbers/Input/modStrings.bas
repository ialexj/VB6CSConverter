Attribute VB_Name = "modStrings"
Option Explicit

Public Sub Messages()
    Dim s As String
    s = "End Sub"
    s = "don't: do ""this"""
    s = "a:b"
    Rem a rem comment
    s = s & ": ok"
End Sub

Public Sub Apostrophes()
    Dim s As String
    s = "it's fine"
End Sub
