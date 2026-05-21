using System;
using System.IO;

namespace ComQuery;

/// <summary>
/// Simple diagnostic logger that writes to stderr so it does not pollute the JSON stdout stream.
/// </summary>
internal static class Log
{
    public static void Warning(string message) => Console.Error.WriteLine($"[WARN] {message}");
    public static void Warning(string message, Exception ex) => Console.Error.WriteLine($"[WARN] {message}: {ex.Message}");
    public static void Information(string message) => Console.Error.WriteLine($"[INFO] {message}");
}
