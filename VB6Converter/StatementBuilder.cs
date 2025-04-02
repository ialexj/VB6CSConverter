using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VB6Converter;

class StatementBuilder
{
    readonly StringBuilder _sb = new();
    int _indent = 0;
    bool isLineStart = true;

    public bool IsLineStart => isLineStart;

    public void StartBlock(string str)
    {
        AppendLine(str);
        _indent++;
    }

    public void AppendLine()
    {
        _sb.AppendLine(AddIndents(string.Empty));
        isLineStart = true;
    }

    public void AppendLine(string str)
    {
        _sb.AppendLine(AddIndents(str));
        isLineStart = true;
    }

    public void Append(string str)
    {
        _sb.Append(AddIndents(str));
    }


    public void EndBlock()
    {
        _indent--;
    }

    public void EndBlock(string str)
    {
        EndBlock();
        AppendLine(str);
    }

    string AddIndents(string str)
    {
        if (_indent > 0) {
            if (isLineStart) {
                str = GetIndent() + str;
                isLineStart = false;
            }

            str = str.Replace(Environment.NewLine, Environment.NewLine + GetIndent());
        }

        return str;
    }

    string GetIndent() => new(' ', _indent * 2);

    public override string ToString() => _sb.ToString();
}
