using Antlr4.Runtime.Tree;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using VB6Parser;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static VB6Converter.Conversion.ValueConverter;
using static VB6Converter.RoslynHelpers;
using static VB6Parser.VisualBasic6Parser;

namespace VB6Converter.Conversion;
public static class IOConverter
{
    public static IEnumerable<StatementSyntax> GetIOStatements(IIOStatementContext stmt, CallContext ctx)
    {
        switch (stmt) {
            case EraseStmtContext erase:
                return erase.valueStmt().Select(v => ExpressionStatement(
                    InvocationExpression(
                        ParseExpression("System.IO.File.Delete"),
                        ArgumentList(GetValue(v, ctx)))));

            case InputStmtContext inputCtx: {
                var fileNum = GetValue(inputCtx.valueStmt(0), ctx);
                return inputCtx.valueStmt().Skip(1).Select(v =>
                    ExpressionStatement(
                        InvocationExpression(
                            ParseExpression("Microsoft.VisualBasic.FileSystem.Input"),
                            ArgumentList(
                                Argument(fileNum),
                                Argument(GetValue(v, ctx)).WithRefKindKeyword(Token(SyntaxKind.RefKeyword))))));
            }

            // Single-statement return
            default:
                return [GetIOStatement(stmt, ctx)];
        }
    }

    static StatementSyntax GetIOStatement(IIOStatementContext stmt, CallContext ctx)
    {
        switch (stmt) {
            case OpenStmtContext open:
                return GetOpen(open, ctx);

            case PrintStmtContext print:
                return GetPrint(print, ctx);

            case CloseStmtContext close: {
                    var fileNums = close.valueStmt().Select(v => GetValue(v, ctx)).ToArray();
                    return ExpressionStatement(
                        InvocationExpression(ParseExpression("Microsoft.VisualBasic.FileSystem.FileClose"), ArgumentList(fileNums)));
                }

            case LineInputStmtContext lineInput: {
                    var fileNum = GetValue(lineInput.valueStmt(0), ctx);
                    var variable = GetValue(lineInput.valueStmt(1), ctx);
                    return ExpressionStatement(
                        AssignmentExpression(
                            SyntaxKind.SimpleAssignmentExpression,
                            variable,
                            InvocationExpression(ParseExpression("Microsoft.VisualBasic.FileSystem.LineInput"), ArgumentList(fileNum))));
                }

            case WriteStmtContext writeStmt: {
                    var fileNum = GetValue(writeStmt.valueStmt(), ctx);

                    var items = writeStmt.outputList()?.outputList_Expression()
                        .Where(o => o.valueStmt() is not null)
                        .Select(o => GetValue(o.valueStmt(), ctx))
                        .ToArray() ?? [];

                    return ExpressionStatement(
                        InvocationExpression(
                            ParseExpression("Microsoft.VisualBasic.FileSystem.Write"),
                            ArgumentList(new[] { Argument(fileNum) }.Concat(items.Select(a => Argument(a))).ToArray())));
                }

            case GetStmtContext getCtx: {
                    var getVals = getCtx.valueStmt();
                    var (getRecord, getVar) = getVals.Length == 3
                        ? (GetValue(getVals[1], ctx), GetValue(getVals[2], ctx))
                        : (null, GetValue(getVals[1], ctx));

                    var getArgs = new List<ArgumentSyntax> {
                        Argument(GetValue(getVals[0], ctx)),
                        Argument(getVar).WithRefKindKeyword(Token(SyntaxKind.RefKeyword)),
                    };

                    if (getRecord is not null)
                        getArgs.Add(Argument(getRecord));

                    return ExpressionStatement(
                        InvocationExpression(ParseExpression("Microsoft.VisualBasic.FileSystem.FileGet"), ArgumentList(getArgs.ToArray())));
                }

            case PutStmtContext putCtx: {
                    var putVals = putCtx.valueStmt();
                    var (putRecord, putVar) = putVals.Length == 3
                        ? (GetValue(putVals[1], ctx), GetValue(putVals[2], ctx))
                        : (null, GetValue(putVals[1], ctx));

                    var putArgs = new List<ArgumentSyntax> {
                        Argument(GetValue(putVals[0], ctx)),
                        Argument(putVar),
                    };

                    if (putRecord is not null)
                        putArgs.Add(Argument(putRecord));

                    return ExpressionStatement(
                        InvocationExpression(ParseExpression("Microsoft.VisualBasic.FileSystem.FilePut"), ArgumentList(putArgs.ToArray())));
                }

            case SeekStmtContext seekCtx:
                return ExpressionStatement(
                    InvocationExpression(ParseExpression("Microsoft.VisualBasic.FileSystem.Seek"), ArgumentList(
                        GetValue(seekCtx.valueStmt(0), ctx),
                        GetValue(seekCtx.valueStmt(1), ctx))));

            case KillStmtContext kill: {
                    var value = GetValue(kill.valueStmt(), ctx);
                    return ExpressionStatement(
                        InvocationExpression(ParseExpression("Microsoft.VisualBasic.FileSystem.Kill"), ArgumentList(value)));
                }

            case FilecopyStmtContext filecopy:
                return ExpressionStatement(
                    InvocationExpression(
                        ParseExpression("Microsoft.VisualBasic.FileSystem.FileCopy"),
                        ArgumentList(
                            GetValue(filecopy.valueStmt(0), ctx),
                            GetValue(filecopy.valueStmt(1), ctx))));

            case NameStmtContext nameSt:
                return ExpressionStatement(
                    InvocationExpression(
                        ParseExpression("Microsoft.VisualBasic.FileSystem.Rename"),
                        ArgumentList(
                            GetValue(nameSt.valueStmt(0), ctx),
                            GetValue(nameSt.valueStmt(1), ctx))));

            case ResetStmtContext reset:
                return ExpressionStatement(
                    InvocationExpression(
                        ParseExpression("Microsoft.VisualBasic.FileSystem.Reset"),
                        ArgumentList()));

            case WidthStmtContext widthSt:
                return ExpressionStatement(
                    InvocationExpression(
                        ParseExpression("Microsoft.VisualBasic.FileSystem.FileWidth"),
                        ArgumentList(
                            GetValue(widthSt.valueStmt(0), ctx),
                            GetValue(widthSt.valueStmt(1), ctx))));

            case MkdirStmtContext mkdir:
                return ExpressionStatement(
                    InvocationExpression(
                        ParseExpression("Microsoft.VisualBasic.FileSystem.MkDir"),
                        ArgumentList(GetValue(mkdir.valueStmt(), ctx))));

            case RmdirStmtContext rmdir:
                return ExpressionStatement(
                    InvocationExpression(
                        ParseExpression("Microsoft.VisualBasic.FileSystem.RmDir"),
                        ArgumentList(GetValue(rmdir.valueStmt(), ctx))));

            case ChDirStmtContext chdir:
                return ExpressionStatement(
                    InvocationExpression(
                        ParseExpression("Microsoft.VisualBasic.FileSystem.ChDir"),
                        ArgumentList(GetValue(chdir.valueStmt(), ctx))));

            case ChDriveStmtContext chdrive:
                return ExpressionStatement(
                    InvocationExpression(
                        ParseExpression("Microsoft.VisualBasic.FileSystem.ChDrive"),
                        ArgumentList(GetValue(chdrive.valueStmt(), ctx))));

            case SetattrStmtContext setattr:
                return ExpressionStatement(
                    InvocationExpression(
                        ParseExpression("Microsoft.VisualBasic.FileSystem.SetAttr"),
                        ArgumentList(
                            GetValue(setattr.valueStmt(0), ctx),
                            GetValue(setattr.valueStmt(1), ctx))));

            case LockStmtContext lockSt: {
                    var lockArgs = lockSt.valueStmt().Select(v => Argument(GetValue(v, ctx))).ToArray();
                    return ExpressionStatement(
                        InvocationExpression(
                            ParseExpression("Microsoft.VisualBasic.FileSystem.Lock"),
                            ArgumentList(lockArgs)));
                }

            case UnlockStmtContext unlockSt: {
                    var unlockArgs = unlockSt.valueStmt().Select(v => Argument(GetValue(v, ctx))).ToArray();
                    return ExpressionStatement(
                        InvocationExpression(
                            ParseExpression("Microsoft.VisualBasic.FileSystem.Unlock"),
                            ArgumentList(unlockArgs)));
                }

            default:
                return EmptyStatement()
                    .WithError(TransformError.Create(stmt, "Unknown IO statement"));
        }
    }

    public static StatementSyntax GetOpen(OpenStmtContext open, CallContext ctx)
    {
        var path = GetValue(open.valueStmt(0), ctx);
        var fileNum = GetValue(open.valueStmt(1), ctx);

        string GetMode()
        {
            if (open.APPEND() != null) return "Microsoft.VisualBasic.OpenMode.Append";
            if (open.BINARY() != null) return "Microsoft.VisualBasic.OpenMode.Binary";
            if (open.INPUT() != null) return "Microsoft.VisualBasic.OpenMode.Input";
            if (open.OUTPUT() != null) return "Microsoft.VisualBasic.OpenMode.Output";
            return "Microsoft.VisualBasic.OpenMode.Random";
        }

        string GetAccess()
        {
            if (open.READ_WRITE() != null) return "Microsoft.VisualBasic.OpenAccess.ReadWrite";
            if (open.READ() != null) return "Microsoft.VisualBasic.OpenAccess.Read";
            if (open.WRITE() != null) return "Microsoft.VisualBasic.OpenAccess.Write";
            return null;
        }

        string GetShare()
        {
            if (open.LOCK_READ_WRITE() != null) return "Microsoft.VisualBasic.OpenShare.LockReadWrite";
            if (open.LOCK_WRITE() != null) return "Microsoft.VisualBasic.OpenShare.LockWrite";
            if (open.LOCK_READ() != null) return "Microsoft.VisualBasic.OpenShare.LockRead";
            if (open.SHARED() != null) return "Microsoft.VisualBasic.OpenShare.Shared";
            return null;
        }

        var args = new List<ArgumentSyntax> {
            Argument(fileNum),
            Argument(path),
            Argument(ParseName(GetMode()))
        };

        var access = GetAccess();
        var share = GetShare();
        if (access is not null || share is not null) {
            args.Add(Argument(ParseName(access ?? "Microsoft.VisualBasic.OpenAccess.Default")));
            if (share is not null) args.Add(Argument(ParseName(share)));
        }

        if (open.LEN() is not null && open.valueStmt().Length > 2) {
            args.Add(Argument(GetValue(open.valueStmt(2), ctx))
                .WithNameColon(NameColon(IdentifierName("RecordLength"))));
        }

        return ExpressionStatement(
            InvocationExpression(
                ParseExpression("Microsoft.VisualBasic.FileSystem.FileOpen"),
                ArgumentList(args.ToArray())));
    }

    public static StatementSyntax GetPrint(PrintStmtContext print, CallContext ctx)
    {
        using var _ = new TraceMethod(print);

        var fileNum = GetValue(print.valueStmt(), ctx);
        var outputList = print.outputList();

        ExpressionSyntax GetOutputExpression(OutputList_ExpressionContext o)
        {
            if (o.SPC() is not null) {
                var spcArgs = o.argsCall();
                if (spcArgs?.argCall().Length > 0) {
                    return InvocationExpression(ParseExpression("Microsoft.VisualBasic.FileSystem.SPC"),
                        ArgumentList(GetValue(spcArgs.argCall(0).valueStmt(), ctx)));
                }
                return InvocationExpression(ParseExpression("Microsoft.VisualBasic.FileSystem.SPC"), ArgumentList());
            }
            if (o.TAB() is not null) {
                var tabArgs = o.argsCall();
                if (tabArgs?.argCall().Length > 0) {
                    return InvocationExpression(ParseExpression("Microsoft.VisualBasic.FileSystem.TAB"),
                        ArgumentList(GetValue(tabArgs.argCall(0).valueStmt(), ctx)));
                }
                return InvocationExpression(ParseExpression("Microsoft.VisualBasic.FileSystem.TAB"), ArgumentList());
            }
            if (o.valueStmt() is ValueStmtContext value) {
                return GetValue(value, ctx);
            }
            return LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(""))
                .WithError(TransformError.Create(o, "Print without value"));
        }

        // Trailing semicolon suppresses newline → use Print; otherwise use PrintLine
        var lastSep = outputList?.children?.OfType<ITerminalNode>()
            .LastOrDefault(t => t.GetText() is ";" or ",");
        var methodName = lastSep?.GetText() == ";" ? "Print" : "PrintLine";

        var items = outputList?.outputList_Expression()
            .Select(GetOutputExpression).ToArray() ?? [];

        return ExpressionStatement(
            InvocationExpression(
                ParseExpression($"Microsoft.VisualBasic.FileSystem.{methodName}"),
                ArgumentList(new[] { Argument(fileNum) }.Concat(items.Select(a => Argument(a))).ToArray())));
    }
}
