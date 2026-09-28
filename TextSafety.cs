using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ClipboardTrail
{
    internal static class TextSafety
    {
        private static readonly string[] SensitiveWords = new string[]
        {
            "密码", "口令", "验证码", "支付密码", "银行卡", "信用卡", "身份证", "私钥", "助记词", "secret", "password", "passwd", "api_key", "apikey", "access_token", "private key", "BEGIN RSA PRIVATE KEY", "BEGIN OPENSSH PRIVATE KEY"
        };

        public static string FindSensitiveReason(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "内容为空";
            string lower = text.ToLowerInvariant();
            string word = SensitiveWords.FirstOrDefault(x => lower.Contains(x.ToLowerInvariant()));
            if (word != null) return "检测到敏感标记：“" + word + "”";
            if (Regex.IsMatch(text, @"\b\d{6}\b") && (lower.Contains("code") || lower.Contains("otp") || text.Contains("验证"))) return "疑似一次性验证码";
            if (Regex.IsMatch(text.Replace(" ", ""), @"\b\d{15,19}\b")) return "疑似银行卡号或长账号";
            if (Regex.IsMatch(text, @"(?i)\b(sk|pk|ak)-[a-z0-9_\-]{16,}\b")) return "疑似 API 密钥";
            return null;
        }

        public static bool LooksGarbled(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            int suspicious = GarbleScore(text);
            return suspicious >= Math.Max(2, text.Length / 20);
        }

        private static int GarbleScore(string text)
        {
            return (text ?? "").Count(c => c == 'Ã' || c == 'Â' || c == 'ä' || c == 'å' || c == 'ç' || c == 'é' || c == '�');
        }

        public static bool LooksLikeStandaloneCredential(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            string value = text.Trim();
            if (value.Length < 4 || value.Length > 128 || value.Any(char.IsWhiteSpace)) return false;
            Uri uri;
            if (Uri.TryCreate(value, UriKind.Absolute, out uri)) return false;
            if (Regex.IsMatch(value, @"^\d{4,8}$")) return true;
            bool letter = value.Any(char.IsLetter);
            bool digit = value.Any(char.IsDigit);
            bool symbol = value.Any(c => !char.IsLetterOrDigit(c));
            return value.Length >= 8 && digit && (letter || symbol) && (symbol || value.Any(char.IsUpper));
        }

        public static string TryLocalRepair(string text)
        {
            if (!LooksGarbled(text)) return text;
            try
            {
                byte[] bytes = Encoding.GetEncoding(1252).GetBytes(text);
                string repaired = Encoding.UTF8.GetString(bytes);
                return GarbleScore(repaired) < GarbleScore(text) && !LooksGarbled(repaired) ? repaired : text;
            }
            catch { return text; }
        }
    }
}
