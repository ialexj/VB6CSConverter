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
    public void Test() => ValidateShouldNotFail(
        """
        Private Sub ImprimirFitaCaixa(ByVal lIDControleCx As Long, lIdCaixa As Long)
             
            rsControleCaixa.FindFirst "ctrlcxId =" & lIDControleCx

            .CurrentY = PosicaoY(.CurrentY):
            
        End Sub
        """);

    [TestMethod]
    public void Test2() => ValidateShouldNotFail(
        """
        Private Sub Test()
            If chkNotasEncomenda(0).value = 1 Then sFrn = "Where " & sFrn: fCriar = True Else sFind = sFrn: sFrn = ""
        End Sub
        """);

    [TestMethod]
    public void Test3() => ValidateShouldNotFail(
        """
        #Const afLogfile = 1

        Private Sub Test()
            #If afDebug And afLogfile Then
                If iLogFile = 0 Then
                    iLogFile = FreeFile
                    ' Warning: multiple instances can overwrite log file
                    Open App.EXEName & ".DBG" For Output Shared As iLogFile
                    ' Challenge: Rewrite to give each instance its own log file
                End If
                Print #iLogFile, sMsg
            #End If
        End Sub
        """);

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
    public void Test5() => ValidateShouldNotFail(
        """
        Private Function MinuteToHour() As Boolean


        Error:
                ErrorHandle 

        End Function
        """);
}
