Attribute VB_Name = "modSelect"
Option Explicit

Public Sub Pick(ByVal code As Long)
    Dim name As String
    Select Case code
        Case 1
            name = "one"
        Case 2, 3
            name = "few"
        Case Else
            name = "many"
    End Select
End Sub

Public Sub Nested(ByVal a As Long, ByVal b As Long)
    Select Case a
        Case 1
            Select Case b
                Case 1
                    a = 1
                Case 2
                    a = 2
            End Select
        Case 2
            a = 3
    End Select
End Sub
