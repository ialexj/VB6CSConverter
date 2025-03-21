using System.Text;

namespace VB6Converter.CSharpModel
{
    public record class CSharpVariable(string Name, CSharpType Type, int ArrayDimensions = 0)
    {
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(Type);

            if (ArrayDimensions > 0) {
                sb.Append('[');
                for (var i = 1; i < ArrayDimensions; i++) {
                    sb.Append(',');
                }
                sb.Append(']');
            }

            sb.Append(' ');
            sb.Append(Name);
            return sb.ToString();
        }
    }
}
