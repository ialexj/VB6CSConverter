using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace VB6Converter;

public static class Program
{
    static async Task Main(string[] args)
    {
        //Trace.Listeners.Add(new TextWriterTraceListener("Input.log"));

        await VB6ToCSharpConverter.ConvertProject(
            @"C:\Users\aj\source\repos\OptiwareVB6\Optiware\Optiware98.vbp",
            null,
            //["frmPosMain"],
            //["modCommon", "modMain", "BaseDados"],
            "./Optiware98");
    }
}
