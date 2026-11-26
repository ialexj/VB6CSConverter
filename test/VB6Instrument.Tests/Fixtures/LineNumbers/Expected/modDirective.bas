Attribute VB_Name = "modDirective"
Option Explicit

Public Sub WithDirective()
      #If DEBUG Then
10        Debug.Print "debug"
      #Else
20        Debug.Print "release"
      #End If
          Dim x As Long
30        x = 1
End Sub
