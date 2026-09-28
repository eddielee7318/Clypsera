using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace ClipboardTrail
{
    internal static class AppData
    {
        public static readonly string DirectoryPath = ResolveDirectoryPath();
        public static readonly string JournalPath = Path.Combine(DirectoryPath, "history.jsonl");
        public static readonly string SettingsPath = Path.Combine(DirectoryPath, "settings.json");
        public static readonly string LibraryPath = Path.Combine(DirectoryPath, "library.json");
        public static readonly string AnnotationPath = Path.Combine(DirectoryPath, "annotations.json");
        public static readonly string LibraryFilesPath = Path.Combine(DirectoryPath, "library-files");
        public static readonly string LanguagePath = Path.Combine(DirectoryPath, "language.txt");

        private static string ResolveDirectoryPath()
        {
            string overridePath = Environment.GetEnvironmentVariable("CLIPBOARDTRAIL_DATA_DIR");
            return string.IsNullOrWhiteSpace(overridePath)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipboardTrail")
                : overridePath;
        }

        public static JavaScriptSerializer CreateSerializer()
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = 16 * 1024 * 1024;
            serializer.RecursionLimit = 32;
            return serializer;
        }

        public static SettingsData LoadSettings()
        {
            Directory.CreateDirectory(DirectoryPath);
            try
            {
                if (File.Exists(SettingsPath))
                {
                    SettingsData value = CreateSerializer().Deserialize<SettingsData>(File.ReadAllText(SettingsPath, Encoding.UTF8));
                    if (value != null)
                    {
                        if (string.IsNullOrWhiteSpace(value.DeepSeekEndpoint)) value.DeepSeekEndpoint = "https://api.deepseek.com/chat/completions";
                        if (string.IsNullOrWhiteSpace(value.DeepSeekModel)) value.DeepSeekModel = "deepseek-chat";
                        if (value.LocalApiPort < 1024 || value.LocalApiPort > 65535) value.LocalApiPort = 17654;
                        if (value.MaxTextLength < 100) value.MaxTextLength = 10000;
                        if (string.IsNullOrWhiteSpace(value.Language)) value.Language = "zh-CN";
                        if (string.IsNullOrWhiteSpace(value.AppTheme)) value.AppTheme = "light";
                        if (string.IsNullOrWhiteSpace(value.ReaderTheme)) value.ReaderTheme = "eye";
                        if (value.UiFontSize != "small" && value.UiFontSize != "medium" && value.UiFontSize != "large") value.UiFontSize = "medium";
                        if (value.CaptureWhitelist == null) value.CaptureWhitelist = "";
                        if (value.CaptureBlacklist == null) value.CaptureBlacklist = "1password, bitwarden, keepass";
                        if (value.DefaultExportFolder == null) value.DefaultExportFolder = "";
                        string preferred = LoadPreferredLanguage();
                        if (!string.IsNullOrWhiteSpace(preferred)) value.Language = preferred;
                        return value;
                    }
                }
            }
            catch { }
            SettingsData defaults = SettingsData.CreateDefault();
            string language = LoadPreferredLanguage();
            if (!string.IsNullOrWhiteSpace(language)) defaults.Language = language;
            return defaults;
        }

        public static void SaveSettings(SettingsData settings)
        {
            Directory.CreateDirectory(DirectoryPath);
            string json = CreateSerializer().Serialize(settings);
            string temp = SettingsPath + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            if (File.Exists(SettingsPath)) File.Delete(SettingsPath);
            File.Move(temp, SettingsPath);
            File.WriteAllText(LanguagePath, settings.Language == "en-US" ? "en-US" : "zh-CN", new UTF8Encoding(false));
        }

        private static string LoadPreferredLanguage()
        {
            try
            {
                if (!File.Exists(LanguagePath)) return null;
                string value = File.ReadAllText(LanguagePath, Encoding.UTF8).Trim();
                return value == "en-US" ? "en-US" : value == "zh-CN" ? "zh-CN" : null;
            }
            catch { return null; }
        }

        public static string ProtectSecret(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            byte[] source = Encoding.UTF8.GetBytes(plain);
            try
            {
                byte[] protectedBytes = ProtectedData.Protect(source, null, DataProtectionScope.CurrentUser);
                return "U:" + Convert.ToBase64String(protectedBytes);
            }
            catch (CryptographicException)
            {
                byte[] protectedBytes = ProtectedData.Protect(source, null, DataProtectionScope.LocalMachine);
                return "M:" + Convert.ToBase64String(protectedBytes);
            }
        }

        public static string UnprotectSecret(string encrypted)
        {
            if (string.IsNullOrEmpty(encrypted)) return "";
            try
            {
                DataProtectionScope scope = DataProtectionScope.CurrentUser;
                if (encrypted.StartsWith("U:", StringComparison.Ordinal)) encrypted = encrypted.Substring(2);
                else if (encrypted.StartsWith("M:", StringComparison.Ordinal))
                {
                    encrypted = encrypted.Substring(2);
                    scope = DataProtectionScope.LocalMachine;
                }
                byte[] bytes = Convert.FromBase64String(encrypted);
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(bytes, null, scope));
            }
            catch { return ""; }
        }
    }
}
