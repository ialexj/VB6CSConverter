// Template generated code from Antlr4BuildTasks.Template v 8.17
namespace VB6Parser
{
    using Antlr4.Runtime;
    using Antlr4.Runtime.Misc;
    using System.IO;

    public class ErrorListener<S>() : IAntlrErrorListener<S>
    {
        public List<string> Errors { get; } = [];

        public bool HasErrors => Errors.Count > 0;

        public void SyntaxError(
            TextWriter output, IRecognizer recognizer, S offendingSymbol, 
            int line, int col, string msg, 
            RecognitionException e)
        {
            Errors.Add($"{line}:{col} {msg} ({Utils.EscapeWhitespace(offendingSymbol.ToString(), false)}");
        }
    }
}