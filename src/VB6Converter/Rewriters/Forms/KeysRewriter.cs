using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.Rewriters.Forms;

public class KeysRewriter : CSharpSyntaxRewriter
{
    static MemberAccessExpressionSyntax Key(string enumValue) 
        => MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName("Keys"), IdentifierName(enumValue))
        .WithUsingForms();

    static readonly Dictionary<string, MemberAccessExpressionSyntax> _keys = new(StringComparer.InvariantCultureIgnoreCase) {
        ["vbKeyLButton"] = Key("LButton"),
        ["vbKeyRButton"] = Key("RButton"),
        ["vbKeyMButton"] = Key("MButton"),

        ["vbKeyCancel"] = Key("Cancel"),
        ["vbKeyBack"] = Key("Back"),
        ["vbKeyTab"] = Key("Tab"),
        ["vbKeyClear"] = Key("Clear"),
        ["vbKeyReturn"] = Key("Return"),
        ["vbKeyShift"] = Key("Shift"),
        ["vbKeyControl"] = Key("Control"),
        ["vbKeyMenu"] = Key("Menu"),
        ["vbKeyPause"] = Key("Pause"),
        ["vbKeyCapital"] = Key("Capital"),
        ["vbKeyEscape"] = Key("Escape"),
        ["vbKeySpace"] = Key("Space"),
        ["vbKeyPageUp"] = Key("PageUp"),
        ["vbKeyPageDown"] = Key("PageDown"),
        ["vbKeyEnd"] = Key("End"),
        ["vbKeyHome"] = Key("Home"),
        ["vbKeyLeft"] = Key("Left"),
        ["vbKeyUp"] = Key("Up"),
        ["vbKeyRight"] = Key("Right"),
        ["vbKeyDown"] = Key("Down"),
        ["vbKeySelect"] = Key("Select"),
        ["vbKeyPrint"] = Key("Print"),
        ["vbKeyExecute"] = Key("Execute"),
        ["vbKeySnapshot"] = Key("Snapshot"),
        ["vbKeyInsert"] = Key("Insert"),
        ["vbKeyDelete"] = Key("Delete"),
        ["vbKeyHelp"] = Key("Help"),
        ["vbKeyNumlock"] = Key("Numlock"),

        ["vbKey0"] = Key("D0"),
        ["vbKey1"] = Key("D1"),
        ["vbKey2"] = Key("D2"),
        ["vbKey3"] = Key("D3"),
        ["vbKey4"] = Key("D4"),
        ["vbKey5"] = Key("D5"),
        ["vbKey6"] = Key("D6"),
        ["vbKey7"] = Key("D7"),
        ["vbKey8"] = Key("D8"),
        ["vbKey9"] = Key("D9"),

        ["vbKeyNumpad0"] = Key("Numpad0"),
        ["vbKeyNumpad1"] = Key("Numpad1"),
        ["vbKeyNumpad2"] = Key("Numpad2"),
        ["vbKeyNumpad3"] = Key("Numpad3"),
        ["vbKeyNumpad4"] = Key("Numpad4"),
        ["vbKeyNumpad5"] = Key("Numpad5"),
        ["vbKeyNumpad6"] = Key("Numpad6"),
        ["vbKeyNumpad7"] = Key("Numpad7"),
        ["vbKeyNumpad8"] = Key("Numpad8"),
        ["vbKeyNumpad9"] = Key("Numpad9"),

        ["vbKeyMultiply"] = Key("Multiply"),
        ["vbKeyAdd"] = Key("Add"),
        ["vbKeySeparator"] = Key("Separator"),
        ["vbKeySubtract"] = Key("Subtract"),
        ["vbKeyDecimal"] = Key("Decimal"),
        ["vbKeyDivide"] = Key("Divide"),

        ["vbKeyF1"] = Key("F1"),
        ["vbKeyF2"] = Key("F2"),
        ["vbKeyF3"] = Key("F3"),
        ["vbKeyF4"] = Key("F4"),
        ["vbKeyF5"] = Key("F5"),
        ["vbKeyF6"] = Key("F6"),
        ["vbKeyF7"] = Key("F7"),
        ["vbKeyF8"] = Key("F8"),
        ["vbKeyF9"] = Key("F9"),
        ["vbKeyF10"] = Key("F10"),
        ["vbKeyF11"] = Key("F11"),
        ["vbKeyF12"] = Key("F12"),
        ["vbKeyF13"] = Key("F13"),
        ["vbKeyF14"] = Key("F14"),
        ["vbKeyF15"] = Key("F15"),
        ["vbKeyF16"] = Key("F16"),

        ["vbKeyA"] = Key("A"),
        ["vbKeyB"] = Key("B"),
        ["vbKeyC"] = Key("C"),
        ["vbKeyD"] = Key("D"),
        ["vbKeyE"] = Key("E"),
        ["vbKeyF"] = Key("F"),
        ["vbKeyG"] = Key("G"),
        ["vbKeyH"] = Key("H"),
        ["vbKeyI"] = Key("I"),
        ["vbKeyJ"] = Key("J"),
        ["vbKeyK"] = Key("K"),
        ["vbKeyL"] = Key("L"),
        ["vbKeyM"] = Key("M"),
        ["vbKeyN"] = Key("N"),
        ["vbKeyO"] = Key("O"),
        ["vbKeyP"] = Key("P"),
        ["vbKeyQ"] = Key("Q"),
        ["vbKeyR"] = Key("R"),
        ["vbKeyS"] = Key("S"),
        ["vbKeyT"] = Key("T"),
        ["vbKeyU"] = Key("U"),
        ["vbKeyV"] = Key("V"),
        ["vbKeyW"] = Key("W"),
        ["vbKeyX"] = Key("X"),
        ["vbKeyY"] = Key("Y"),
        ["vbKeyZ"] = Key("Z")
    };

    public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
        => Log.Rewrite(this, node, node => {
            if (_keys.TryGetValue(node.Identifier.Text, out var key)) {
                return key;
            }

            return base.VisitIdentifierName(node);
        });
}
