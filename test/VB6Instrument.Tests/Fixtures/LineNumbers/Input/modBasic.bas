Attribute VB_Name = "modBasic"
Option Explicit

Public Sub CloseSale()
    Dim amount As Double
    amount = 10.5
    If amount > 0 Then
        amount = amount * 2
    Else
        amount = 0
    End If
End Sub

Private Function Compute(ByVal x As Long) As Long
    Dim result As Long
    result = x * 2
    Compute = result
End Function
