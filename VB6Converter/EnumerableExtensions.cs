using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VB6Converter
{
    public static class EnumerableExtensions
    {
        public static IEnumerable<T> Intersperse<T>(this IEnumerable<T> source, T element)
        {
            bool first = true;
            foreach (T value in source) {
                if (!first) yield return element;
                yield return value;
                first = false;
            }
        }
    }
}
