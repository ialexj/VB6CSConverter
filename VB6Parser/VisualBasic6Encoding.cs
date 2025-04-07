using System.Text;

namespace VB6Parser
{
    public static class VisualBasic6Encoding
    {
        static VisualBasic6Encoding()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public static Encoding Encoding => Encoding.GetEncoding(1252);
    }
}
