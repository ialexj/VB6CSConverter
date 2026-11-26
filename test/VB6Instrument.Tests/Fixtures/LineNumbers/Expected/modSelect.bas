Attribute VB_Name = "modSelect"
Option Explicit

Public Sub Pick(ByVal code As Long)
          Dim name As String
10        Select Case code
              Case 1
20                name = "one"
30            Case 2, 3
40                name = "few"
50            Case Else
60                name = "many"
70        End Select
End Sub

Public Sub Nested(ByVal a As Long, ByVal b As Long)
10        Select Case a
              Case 1
20                Select Case b
                      Case 1
30                        a = 1
40                    Case 2
50                        a = 2
60                End Select
70            Case 2
80                a = 3
90        End Select
End Sub
