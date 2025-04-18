using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static VB6Converter.Tests.Validations;

namespace VB6Converter.Tests;

[TestClass]
public class DiagnosticTests
{
    [TestMethod]
    public void TrailingColon() => ValidateShouldNotFail(
        """
        Sub Test()
            FindFirst

            x = Y(A):            
        End Sub
        """);

    //[TestMethod]
    //public void Macros() => ValidateShouldNotFail(
    //    """
    //    #Const afLogfile = 1

    //    Private Sub Test()
    //        #If afDebug And afLogfile Then
    //            If iLogFile = 0 Then
    //                iLogFile = FreeFile
    //                ' Warning: multiple instances can overwrite log file
    //                Open App.EXEName & ".DBG" For Output Shared As iLogFile
    //                ' Challenge: Rewrite to give each instance its own log file
    //            End If
    //            Print #iLogFile, sMsg
    //        #End If
    //    End Sub
    //    """);

    [TestMethod]
    public void Test4() => ValidateShouldNotFail(
        """
        Begin VB.Form frmEditorMain 
            Caption         =   "Editor"
            ClientHeight    =   6495
            FillColor       =   &H00C0C0C0&
            BeginProperty Font 
                Name            =   "Terminal"
                Size            =   6
                Charset         =   255
                Weight          =   700
                Shortcut        =   +{F5}
                Shortcut        =   ^{F5}
            EndProperty
        End 
        """);

    [TestMethod]
    public void Label() => ValidateMemberMatches(
        """
        Private Sub Test()
        Error:
                ErrorHandle 
        End Sub
        """,
        """
        private static void Test()
        {
            Error:
                ErrorHandle();
        }
        """);

    [TestMethod]
    public void LabelInline() => ValidateMemberMatches(
        """
        Private Sub Test()
        Error:     ErrorHandle 
        End Sub
        """,
        """
        private static void Test()
        {
            Error:
                ErrorHandle();
        }
        """);

    [TestMethod]
    public void Assignment() => ValidateShouldNotFail(
        """   
        Private Sub Test()
            MethodCall arg:="Str1" & "Str2"
        End Sub
        """);

    [TestMethod]
    public void InlineIfStatements() => ValidateShouldNotFail(
        """
        Sub Test()
            If value Then DoSomething: DoAnotherThing Else DoSomethingElse: DoSomethingMore
        End Sub
        """);

    [TestMethod]
    public void InlineIfStatements2() => ValidateShouldNotFail(
        """
        Sub Test()
            If chkNotasEncomenda(0).value = 1 Then sFrn = "Where " & sFrn: fCriar = True Else sFind = sFrn: sFrn = ""
        End Sub
        """);



    [TestMethod]
    public void InlineStatements() => ValidateShouldNotFail(
        """
        Sub Test()
            A = 1: B = 2
        End Sub
        """);

    [TestMethod]
    public void CaseInline() => ValidateShouldNotFail(
        """
        Sub Test()
               Select Case i
                   Case TEST: DoSomething
               End Select
        End Sub
        """);

    [TestMethod]
    public void CaseWithNewline() => ValidateShouldNotFail(
        """
        Sub Test()
            Select Case tipo
                Case 0:
                    DoSomething
            End Select
        End Sub
        """);

    [TestMethod]
    public void CaseWithNewlineNoColon() => ValidateShouldNotFail(
        """
        Sub Test()
            Select Case tipo
                Case 0
                    DoSomething
            End Select
        End Sub
        """);

    [TestMethod]
    public void CaseWithComment() => ValidateShouldNotFail(
        """
        Sub Test()
            Select Case tipo
                Case 0: ' Comment
                    DoSomething
            End Select
        End Sub
        """);
}
