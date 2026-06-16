using System;

namespace UltimateLootEditor.Util
{
    internal static class Logger
    {
        internal static bool Debug =
            string.Equals(Environment.GetEnvironmentVariable("ULE_DEBUG_LOGGING"), "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Environment.GetEnvironmentVariable("ULE_DEBUG_LOGGING"), "true", StringComparison.OrdinalIgnoreCase);

        public static void Info(string msg)
        {
            if (Debug) Console.WriteLine(msg);
        }

        public static void Status(string msg) => Console.WriteLine(msg);

        public static void Warn(string msg) => Console.WriteLine(msg);
        public static void Error(string msg, Exception? ex = null) =>
            Console.WriteLine($"{msg}{(ex is null ? "" : $" :: {ex.Message}")}");
    }
}
