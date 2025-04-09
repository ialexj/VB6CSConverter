using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VB6Converter
{
    public record class ComReference(string Name, Guid Guid, int Lcid, int Major, int Minor, string WrapperTool, bool Isolated, bool EmbedInteropTypes)
    {
        public static readonly ComReference MicrosoftOfficeInteropAccessDao = new ComReference(
            "Microsoft.Office.Interop.Access.Dao",
            new Guid("4ac9e1da-5bad-4ac7-86e3-24f4cdceca28"), 0,
            12, 0, "tlbimp", false, true);
    }
}
