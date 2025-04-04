using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace VB6Converter;

public static class Program
{
    static async Task Main(string[] args)
    {
        Trace.Listeners.Add(new TextWriterTraceListener("Input.log"));

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        //await VisualBasic6Converter.ConvertProject(
            //@"C:\Users\aj\source\repos\OptiwareVB6\Optiware\Optiware98.vbp", 
            //["frmBancosBarra"],
            //"./Optiware98");

        VisualBasic6Converter.ConvertFile("Input.txt", ".");

        //Convert(@"C:\Users\aj\source\repos\OptiwareVB6\Optiware\modMain.bas", "Output.cs");
        //Convert(@"Input.txt", "Output.cs");
    }
}
