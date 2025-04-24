namespace VB6Converter
{
    using Antlr4.Runtime;
    using System.IO;

    public class ErrorListener<S> : IAntlrErrorListener<S>
    {
        public bool had_error;

        public void SyntaxError(TextWriter output, IRecognizer recognizer, S offendingSymbol, int line,
            int col, string msg, RecognitionException e)
        {
            had_error = true;
        }
    }
}