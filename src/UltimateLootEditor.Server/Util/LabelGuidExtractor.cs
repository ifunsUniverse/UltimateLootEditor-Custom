using System.Text.RegularExpressions;

namespace UltimateLootEditor.Util
{
    internal static class LabelGuidExtractor
    {
        private static readonly Regex TailGuid = new(@"\[(?<g>[0-9a-fA-F-]{36})\]\s*$", RegexOptions.Compiled);
        public static string ExtractGuid(string label)
        {
            if (string.IsNullOrEmpty(label)) return string.Empty;
            var m = TailGuid.Match(label);
            return m.Success ? m.Groups["g"].Value.ToLowerInvariant() : string.Empty;
        }
    }
}
