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
    public void TrailingColon() => ConversionShouldSucceed(
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
    public void Test4() => ConversionShouldSucceed(
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
    public void Assignment() => ConversionShouldSucceed(
        """   
        Private Sub Test()
            MethodCall arg:="Str1" & "Str2"
        End Sub
        """);

    [TestMethod]
    public void InlineIfStatements() => ConversionShouldSucceed(
        """
        Sub Test()
            If value Then DoSomething: DoAnotherThing Else DoSomethingElse: DoSomethingMore
        End Sub
        """);

    [TestMethod]
    public void InlineIfStatements2() => ConversionShouldSucceed(
        """
        Sub Test()
            If chkNotasEncomenda(0).value = 1 Then sFrn = "Where " & sFrn: fCriar = True Else sFind = sFrn: sFrn = ""
        End Sub
        """);



    [TestMethod]
    public void InlineStatements() => ConversionShouldSucceed(
        """
        Sub Test()
            A = 1: B = 2
        End Sub
        """);

    [TestMethod]
    public void CaseInline() => ConversionShouldSucceed(
        """
        Sub Test()
               Select Case i
                   Case TEST: DoSomething
               End Select
        End Sub
        """);

    [TestMethod]
    public void CaseWithNewline() => ConversionShouldSucceed(
        """
        Sub Test()
            Select Case tipo
                Case 0:
                    DoSomething
            End Select
        End Sub
        """);

    [TestMethod]
    public void CaseWithNewlineNoColon() => ConversionShouldSucceed(
        """
        Sub Test()
            Select Case tipo
                Case 0
                    DoSomething
            End Select
        End Sub
        """);

    [TestMethod]
    public void CaseWithComment() => ConversionShouldSucceed(
        """
        Sub Test()
            Select Case tipo
                Case 0: ' Comment
                    DoSomething
            End Select
        End Sub
        """);

    [TestMethod]
    public void With() => ValidateBodyMatches(
        """
        With rsFicha
            !oficDataConsulta = mskPrescricaoO(optPMData).Text
        End With       
        """,
        """
        rsFicha["oficDataConsulta"] = mskPrescricaoO[optPMData].Text;
        """);

    [TestMethod]
    public void What() => ConversionShouldSucceed(
        """
        Private Sub Test()
                'Imprimir Linha
                                  'SAMS
        980                       sTableBody = "|| Total do Documento|" & _
                                               "|" & _
                                               Format$(dDocumentoIVABeneficiario, "Standard") & "|" & _
                                               Format$(dDocumentoIVAEntidade, "Standard") & "|" & _
                                               Format$(lDocumentoIVATotal, "Standard") & "|" & _
                                               Format$(dDocumentoValorBeneficiario, "Standard") & "|" & _
                                               Format$(dDocumentoValorEntidade, "Standard") & "|" & _
                                               Format$(dDocumentoValorEntidade2, "Standard")    '& "|" & _
                                                                                                Format$(dDocumentoValorTotalPTE, "Standard")
        990                       .FontBold = True
        1000                      .AddTable sTableFormat, sTableHeader, sTableBody, &HE0E0E0, &HE0E0E0, True
        1010                      .FontBold = False
        End Sub 
        """);

    [TestMethod]
    public void What2() => ConversionShouldSucceed(
        """
        Public Function Carrega(ByVal docID As Long) As Boolean
                  Dim iErro As Integer
        10        On Error GoTo erro

                  ' --- Cabe�alho ---

                  Dim rsCabecalhoDocumentos As Recordset
        20        Set rsCabecalhoDocumentos = dbo.OpenRecordset( _
                      "SELECT * FROM CabecalhoDocumentos WHERE CabDocID = " & docID, _
                      dbOpenForwardOnly)

        30        With rsCabecalhoDocumentos
        40            If .EOF Then
        50                .Close
        60                Err.Raise Erro_NaoEncontrado, "clsDocClienteHeader.Carrega", "Um documento com o identificador " & docID & " n�o foi encontrado."
        70            End If

        80            DocumentoID = !cabDocID
        90            DocumentoNumero = !CabDocNumDoc
        100           Serie = IfNull(!CabDocSerie, "")

        110           DocumentoTipo = !CabDocTipoDoc
        120           DocumentoData = !CabdocData
        130           DocumentoEstado = !cabdocEstado

        140           DocumentoArmazemID = !cabdocIDArmazem
        150           DocumentoOperadorID = !cabdocIDUser

        160           DocumentoCabecalhoPosID = !cabdocIDCabPos
        170           ClienteUtente = IfNull(!cabdocGeral, "")

        180           m_Hash = IfNull(!CabDocHash, "")

        190           DocumentoIDOrigem = IfNull(!CabDocIDDocOrigem, 0)

        200           m_Destinatario = !CabDocDestinatario
        210           m_DocEntidadeID = IfNull(!CabDocIDDocEntidade, 0)

        220           UsaCompsSeparadas = !cabdocAutoFacturada

        230           TotalDocumento = !CabDocTotal
        240           TotalEntidade = !cabdocTotComp
        250           TotalDescontos = !cabdocTotDesconto
        260           TotalBeneficiario = 0 ' calculado de linhas
        270           TotalIVA = !cabdocTotalIva

        280           DocumentoAcertos = !cabdocTotDesconto ' Ser� subtraido o desconto das linhas, deixando s� o acerto
        290           ArrolamentoID = IfNull(!cabdocIdArrolamento, 0)
        300           .Close
        310       End With

                  ' --- Dados do POS/Cliente ---

        320       LoadClientePOS DocumentoCabecalhoPosID

                  ' --- Documentos de benefici�rio ---

        330       If m_Destinatario = DocCliente_DestinoEntidade Then
        340           Set m_DocBeneficiarioIDs = DocCliente_ObtemDocumentosBeneficiario(DocumentoID)
        350       Else
        360           Set m_DocBeneficiarioIDs = New Collection
        370       End If

                  ' --- Vendas associadas ao documento ---

        380       Set m_VendaIDs = DocCliente_ObtemVendas(DocumentoID)

                  ' --- Linhas ---

                  Dim rsLinhas As Recordset
        390       Set rsLinhas = dbo.OpenRecordset( _
                      "SELECT * FROM qryDocumentoClienteLinhas WHERE Activo = True AND DocumentoID = " & DocumentoID, _
                      dbOpenForwardOnly)

                  ' Fallback para notas de cr�dito/transito que ainda guardam artigos nos MovimentosArtigos.
        400       If rsLinhas.EOF Then
        410           Select Case DocumentoTipo
                          Case DOC_CLIENTE_NOTA_CREDITO
        420                   rsLinhas.Close
        430                   Set rsLinhas = dbo.OpenRecordset( _
                                  "SELECT * FROM qryDocumentoClienteMovimentoArtigos WHERE DocumentoID = " & DocumentoID & " AND (MovimentoTipo = 1 OR MovimentoTipo = 5)", _
                                  dbOpenForwardOnly)

        440               Case DOC_CLIENTE_NOTA_TRANSITO
        450                   rsLinhas.Close
        460                   Set rsLinhas = dbo.OpenRecordset( _
                                  "SELECT * FROM qryDocumentoClienteMovimentoArtigos WHERE DocumentoID = " & DocumentoID & " AND MovimentoTipo = 2", _
                                  dbOpenForwardOnly)
        470           End Select
        480       End If

        490       With rsLinhas
                      Dim l As clsDocClienteLinha

                      Dim dDescPercent As Double
        500           Do While Not .EOF
        510               dDescPercent = 0

        520               If !Quantidade > 0 And !PrecoUnitario > 0 Then
        530                   If PesquisaEntidade(ClienteEntidadeID) Then ' Procura por pre-desconto
        540                       If !Quantidade * !PrecoUnitario - !Comparticipacao > 0 Then
        550                           dDescPercent = (!DescontoValor * 100) / (!Quantidade * !PrecoUnitario - !Comparticipacao)
        560                       End If
        570                   Else
        580                       dDescPercent = (!DescontoValor * 100) / (!Quantidade * !PrecoUnitario)
        590                   End If
        600               End If

        610               Set l = New clsDocClienteLinha
        620               l.Referencia = !Referencia & vbNullString
        630               l.descricao = !descricao & vbNullString
        640               l.Quantidade = !Quantidade
        650               l.TaxaIVA = !TaxaIVA
        660               l.TaxaIVAUtilizador = !TaxaIVA ' TODO
        670               l.PrecoUnitario = !PrecoUnitario
        680               l.DescontoValor = !DescontoValor
        690               l.DescontoPercentagem = dDescPercent
        700               l.Comparticipacao = !Comparticipacao
        710               l.Comparticipacao2 = IfNull(!Comparticipacao2, 0)
        720               l.Total = !Total
        730               l.IvaID = !IvaID
        740               l.Isencao = IfNull(!Isencao, "")
        750               l.FamiliaID = IfNull(!FamiliaID, 0)
        760               l.ExistenciaID = !ExistenciaID
        770               l.FamiliaNome = IfNull(!FamiliaNome, "")

        780               linhas.Add l

                          ' Subtrai desconto dos acertos.
                          ' No fim o que resta � a diferen�a entre o cabe�alho e as linhas
        790               DocumentoAcertos = DocumentoAcertos - !DescontoValor

                          ' Calcula total beneficiario
        800               TotalBeneficiario = TotalBeneficiario + l.ValorBeneficiario

        810               .MoveNext
        820           Loop
        830       End With

        840       Carrega = True
        850       Exit Function
        erro:
        860       iErro = ErrorHandle("clsDocClienteHeader", "Carrega", docID)
        870       Select Case iErro
                      Case ERRO_RESUME: Resume 0
        880           Case ERRO_RESUME_NEXT: Resume Next
        890           Case ERRO_RESUME_END: Sair
        900       End Select
        End Function
        """);

}
