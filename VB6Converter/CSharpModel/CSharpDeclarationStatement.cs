using System.Text;

namespace VB6Converter.CSharpModel
{
    public class CSharpDeclarationStatement(
        CSharpVariable variable, ICSharpExpression value, 
        CSharpDeclarationOption option = CSharpDeclarationOption.Default,
        CSharpVisibility visibility = CSharpVisibility.Private) 
        : CSharpStatement
    {
        public CSharpVisibility Visibility { get; } = visibility;

        public CSharpDeclarationOption Option { get; } = option;

        public CSharpVariable Variable { get; } = variable;

        public ICSharpExpression Value { get; } = value;

        public override string ToString()
        {
            var sb = new StringBuilder();

            if (Visibility != CSharpVisibility.Private) {
                sb.Append(Visibility.ToCodeString());
                sb.Append(' ');
            }

            if (Option != CSharpDeclarationOption.Default) {
                sb.Append(Option.ToCodeString());
                sb.Append(' ');
            }

            sb.Append(Variable);

            if (Value != null) {
                sb.Append(" = ");
                sb.Append(Value);
            }

            sb.Append(';');
            return sb.ToString();
        }
    }

    public enum CSharpDeclarationOption
    {
        Default,
        Static,
        Const
    }

    public static class CSharpDeclarationOptions
    {
        public static string ToCodeString(this CSharpDeclarationOption option)
            => option switch {
                CSharpDeclarationOption.Static => "static",
                CSharpDeclarationOption.Const => "const",
                _ => ""
            };
    }
}
