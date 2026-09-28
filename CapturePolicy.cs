using System;
using System.Collections.Generic;
using System.Linq;

namespace ClipboardTrail
{
    internal static class CapturePolicy
    {
        public static bool Allows(SourceInfo source, SettingsData settings)
        {
            if (settings == null) return true;
            string process = Normalize(source == null ? "" : source.ProcessName);
            HashSet<string> blacklist = Parse(settings.CaptureBlacklist);
            if (blacklist.Contains(process)) return false;
            if (!settings.CaptureWhitelistEnabled) return true;
            HashSet<string> whitelist = Parse(settings.CaptureWhitelist);
            return whitelist.Contains(process);
        }

        public static HashSet<string> Parse(string value)
        {
            return new HashSet<string>((value ?? "")
                .Split(new char[] { ',', ';', '，', '；', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(Normalize)
                .Where(x => x.Length > 0), StringComparer.OrdinalIgnoreCase);
        }

        private static string Normalize(string value)
        {
            string normalized = (value ?? "").Trim();
            if (normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) normalized = normalized.Substring(0, normalized.Length - 4);
            return normalized.ToLowerInvariant();
        }
    }
}
