Attribute VB_Name = "modDirective"
Option Explicit

Public Sub WithDirective()
#If DEBUG Then
    Debug.Print "debug"
#Else
    Debug.Print "release"
#End If
    Dim x As Long
    x = 1
End Sub
