using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace VB6Converter.CSharpModel
{
    public interface ICSharpStatement { }

    public class CSharpGenericStatement(string text) : ICSharpStatement
    {
        public string Text { get; } = text;

        public override string ToString() => Text;
    }

    public class CSharpBlockStatement() : ICSharpStatement, IEnumerable<ICSharpStatement>
    {
        public static readonly CSharpBlockStatement Empty = new();

        public CSharpBlockStatement(IEnumerable<ICSharpStatement> statements) : this()
        {
            Statements.AddRange(statements);
        }

        public List<ICSharpStatement> Statements { get; } = [];

        public IEnumerable<CSharpDeclarationStatement> GetDeclarations() => Statements.OfType<CSharpDeclarationStatement>();


        public IEnumerator<ICSharpStatement> GetEnumerator()
        {
            return ((IEnumerable<ICSharpStatement>)Statements).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable)Statements).GetEnumerator();
        }

        public override string ToString()
        {
            var b = new StatementBuilder();

            if (Statements.Count == 0) {
                return "{}";
            }
            else if (Statements.Count == 1) {
                b.StartBlock(Statements[0].ToString());    
            }
            else {
                b.StartBlock("{");
                foreach (var statement in Statements) {
                    b.AppendLine(statement.ToString());
                }
                b.EndBlock("}");
            }

            return b.ToString();
        }

    }
}
