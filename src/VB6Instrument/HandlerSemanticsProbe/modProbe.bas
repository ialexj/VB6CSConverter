Attribute VB_Name = "modProbe"
Option Explicit

' Handler-semantics probe (vb6instrument spec section 6).
'
' RESULT (recorded 2026-11-25, see README):
'   Fact 1: True   - Err.Description is assignable inside an active handler.
'   Fact 2: Fall-to-End WITHOUT Resume does NOT propagate (the decorated
'           error was swallowed) - the raise strategy is therefore the
'           tool's default. Undecorated propagation itself preserves
'           Number/Source/Description; caller-side Erl of a propagated
'           error reads 0.
'   Fact 3: True   - Err.Clear + fall-to-End exits a swallow handler cleanly.
'   Fact 4: verified end-to-end via ThrowDecorated (COM host exception).
'
' The guard blocks below are hand-written copies of the exact shape the
' tool emits (raise strategy), so this probe also documents the emitted
' code.

Public Function CheckDescriptionAssignable() As Boolean
    On Error GoTo Handler
    Err.Raise 5, "probe", "original"
    Exit Function
Handler:
    Err.Description = "assigned inside handler"
    CheckDescriptionAssignable = (Err.Description = "assigned inside handler")
End Function

' Inner member has NO handler: the error falls to End and propagates
' undecorated, so the caller can inspect what propagation preserved.
Public Function CheckFallThroughPreservation() As String
    Dim report As String
    On Error GoTo Outer
    InnerUndecorated
    CheckFallThroughPreservation = "ERROR: no propagation"
    Exit Function
Outer:
    report = "Number=" & Err.Number
    report = report & " Source=" & Err.Source
    report = report & " Description=" & Err.Description
    report = report & " CallerErl=" & Erl
    CheckFallThroughPreservation = report
    Err.Clear
End Function

Private Sub InnerUndecorated()
10  Err.Raise 5, "probe.source", "inner description"
End Sub

' Inner member guarded with the exact tool-emitted re-raise handler
' (raise strategy ? probe result: fall-to-End does NOT propagate).
' The decoration captures Erl at handler entry.
Public Function CheckDecoratedErl() As String
    On Error GoTo Outer
    InnerDecorated
    CheckDecoratedErl = "ERROR: no propagation"
    Exit Function
Outer:
    CheckDecoratedErl = Err.Description
    Err.Clear
End Function

Private Sub InnerDecorated()
    On Error GoTo GuardError_InnerDecorated
10  Err.Raise 5, "probe.source", "inner description"
    Exit Sub
GuardError_InnerDecorated:
    Dim guardErl As Long: guardErl = Erl
    Err.Raise Err.Number, Err.Source, "HandlerSemanticsProbe.InnerDecorated (" & guardErl & ") -> " & Err.Description
End Sub

' Swallow handler: Err.Clear then fall to End must be a clean normal exit.
Public Function CheckSwallowClear() As Boolean
    On Error GoTo Outer
    InnerSwallow
    CheckSwallowClear = (Err.Number = 0)
    Exit Function
Outer:
    CheckSwallowClear = False
End Function

Private Sub InnerSwallow()
    On Error GoTo GuardError_InnerSwallow
10  Err.Raise 5, "probe", "swallowed"
    Exit Sub
GuardError_InnerSwallow:
    Err.Clear
End Sub

' Fact 4: call this from the COM host and inspect the exception message.
' Expected (outermost frame first), with the raise strategy:
'   HandlerSemanticsProbe.ThrowDecorated (10) -> HandlerSemanticsProbe.InnerRaises (10) -> inner description
Public Sub ThrowDecorated()
    On Error GoTo GuardError_ThrowDecorated
10  InnerRaises
    Exit Sub
GuardError_ThrowDecorated:
    Dim guardErl As Long: guardErl = Erl
    Err.Raise Err.Number, Err.Source, "HandlerSemanticsProbe.ThrowDecorated (" & guardErl & ") -> " & Err.Description
End Sub

Private Sub InnerRaises()
    On Error GoTo GuardError_InnerRaises
10  Err.Raise 5, "probe.source", "inner description"
    Exit Sub
GuardError_InnerRaises:
    Dim guardErl As Long: guardErl = Erl
    Err.Raise Err.Number, Err.Source, "HandlerSemanticsProbe.InnerRaises (" & guardErl & ") -> " & Err.Description
End Sub
