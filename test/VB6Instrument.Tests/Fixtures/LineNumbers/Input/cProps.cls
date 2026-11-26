Version 1.0 Class
Begin
  MultiUse = -1  'True
End
Attribute VB_Name = "cProps"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Option Explicit

Private m_value As Long

Public Property Get Value() As Long
    Value = m_value
End Property

Public Property Let Value(ByVal rhs As Long)
    m_value = rhs
End Property

Public Property Set Item(ByVal key As String, ByVal rhs As Object)
    Set m_items(key) = rhs
End Property
