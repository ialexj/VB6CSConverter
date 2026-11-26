Attribute VB_Name = "modMultiSig"
Option Explicit

Public Sub WideSignature(ByVal first As Long, _
                               ByVal second As Long, _
                               Optional ByVal third As Variant)
          Dim x As Long
10        x = first + second
End Sub

Public Function WideCall(ByVal a As Long) As Long
10        WideCall = WideSignature(a, _
                                   a, _
                                   a)
End Function
