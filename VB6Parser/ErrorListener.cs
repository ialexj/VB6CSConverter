// Template generated code from Antlr4BuildTasks.Template v 8.17
namespace VB6Parser
{
    using Antlr4.Runtime;
    using System.IO;

    public class ErrorListener<S>(string prefix) : ConsoleErrorListener<S>
    {
        public bool had_error;

        public override void SyntaxError(TextWriter output, IRecognizer recognizer, S offendingSymbol, int line,
            int col, string msg, RecognitionException e)
        {
            had_error = true;
            //output.WriteLine(prefix + " line " + line + ":" + col + " " + msg);
        }
    }
}