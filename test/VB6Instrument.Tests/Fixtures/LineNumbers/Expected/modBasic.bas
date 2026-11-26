Attribute VB_Name = "modBasic"
Option Explicit

Public Sub CloseSale()
          Dim amount As Double
10        amount = 10.5
20        If amount > 0 Then
30            amount = amount * 2
40        Else
50            amount = 0
60        End If
End Sub

Private Function Compute(ByVal x As Long) As Long
          Dim result As Long
10        result = x * 2
20        Compute = result
End Function
