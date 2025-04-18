using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VB6Converter;
internal class Logging
{
    public static LoggerFactory LoggerFactory { get; } = new LoggerFactory();
}
