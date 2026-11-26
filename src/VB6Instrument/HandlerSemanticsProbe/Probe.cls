VERSION 1.0 CLASS
BEGIN
  MultiUse = -1  'True
END
Attribute VB_Name = "Probe"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = True
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = True
Option Explicit

Public Function RunProbe() As String
    Dim report As String
    report = report & "Fact1  Err.Description assignable in handler: " & modProbe.CheckDescriptionAssignable() & vbCrLf
    report = report & "Fact2  fall-through preservation: " & modProbe.CheckFallThroughPreservation() & vbCrLf
    report = report & "Fact2  decorated Erl (tool shape): " & modProbe.CheckDecoratedErl() & vbCrLf
    report = report & "Fact3  Err.Clear swallow exits normally: " & modProbe.CheckSwallowClear() & vbCrLf
    report = report & "Fact4  call ThrowDecorated() from the host and inspect the exception message."
    RunProbe = report
End Function

Public Sub ThrowDecorated()
    modProbe.ThrowDecorated
End Sub
