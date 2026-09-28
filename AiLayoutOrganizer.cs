using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace ClipboardTrail
{
    internal static class AiLayoutOrganizer
    {
        public const string SystemPrompt = "You restore paragraph layout for PDF and ebook text. The user message contains immutable numbered text units. Never rewrite or repeat their text. Return only a JSON array of paragraph groups, for example [[1,2,3],[4],[5,6]]. Every unit number must appear exactly once, in the original ascending order. Group accidental line wraps into the same paragraph; keep headings alone; start numbered sections and genuinely new ideas in new paragraphs. Do not return Markdown, prose, keys, or comments.";

        public static string BuildPlanRequest(string original)
        {
            List<string> units = BuildUnits(original);
            StringBuilder request = new StringBuilder();
            request.Append("Arrange all ").Append(units.Count).Append(" immutable units into readable paragraphs. Return JSON groups only.\n");
            for (int i = 0; i < units.Count; i++) request.Append('[').Append(i + 1).Append("] ").Append(units[i]).Append('\n');
            return request.ToString();
        }

        public static bool TryApplyPlan(string original, string response, out string organized, out string error)
        {
            organized = null;
            error = null;
            List<string> units = BuildUnits(original);
            if (units.Count == 0) { organized = original ?? ""; return true; }
            object root;
            try
            {
                string json = StripCodeFence(response ?? "").Trim();
                root = AppData.CreateSerializer().DeserializeObject(json);
                IDictionary dictionary = root as IDictionary;
                if (dictionary != null && dictionary.Contains("paragraphs")) root = dictionary["paragraphs"];
            }
            catch
            {
                error = "AI 没有返回可识别的段落编号。";
                return false;
            }
            object[] groups = root as object[];
            if (groups == null || groups.Length == 0)
            {
                error = "AI 没有返回段落分组。";
                return false;
            }
            List<List<int>> plan = new List<List<int>>();
            int expected = 1;
            foreach (object groupValue in groups)
            {
                object[] groupArray = groupValue as object[];
                if (groupArray == null || groupArray.Length == 0) { error = "AI 返回了空段落。"; return false; }
                List<int> group = new List<int>();
                foreach (object idValue in groupArray)
                {
                    int id;
                    try { id = Convert.ToInt32(idValue); }
                    catch { error = "AI 返回了无效的段落编号。"; return false; }
                    if (id != expected) { error = "AI 返回的编号有缺失、重复或顺序变化。"; return false; }
                    group.Add(id);
                    expected++;
                }
                plan.Add(group);
            }
            if (expected - 1 != units.Count) { error = "AI 没有使用全部原文编号。"; return false; }

            StringBuilder result = new StringBuilder((original ?? "").Length + plan.Count * 2);
            for (int paragraphIndex = 0; paragraphIndex < plan.Count; paragraphIndex++)
            {
                if (paragraphIndex > 0) result.Append("\n\n");
                List<int> paragraph = plan[paragraphIndex];
                for (int unitIndex = 0; unitIndex < paragraph.Count; unitIndex++)
                {
                    string unit = units[paragraph[unitIndex] - 1];
                    if (unitIndex > 0 && NeedsSpace(result[result.Length - 1], unit[0])) result.Append(' ');
                    result.Append(unit);
                }
            }
            organized = result.ToString();
            string accepted, validationError;
            if (!TryAccept(original, organized, out accepted, out validationError))
            {
                organized = null;
                error = validationError;
                return false;
            }
            return true;
        }

        private static List<string> BuildUnits(string original)
        {
            string text = Normalize(original);
            List<string> units = new List<string>();
            StringBuilder current = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\n')
                {
                    AddUnit(units, current);
                    continue;
                }
                current.Append(c);
                bool sentenceEnd = c == '。' || c == '！' || c == '？' || c == '；' || c == '!' || c == '?' || c == ';' || (c == '.' && !IsDecimalPoint(text, i));
                if (sentenceEnd) AddUnit(units, current);
            }
            AddUnit(units, current);
            return units;
        }

        private static void AddUnit(List<string> units, StringBuilder value)
        {
            string unit = value.ToString().Trim();
            value.Clear();
            if (unit.Length > 0) units.Add(unit);
        }

        private static bool IsDecimalPoint(string text, int index)
        {
            return index > 0 && index + 1 < text.Length && char.IsDigit(text[index - 1]) && char.IsDigit(text[index + 1]);
        }

        private static bool NeedsSpace(char previous, char next)
        {
            if (previous == '-' || previous == '/' || previous == '—') return false;
            if (IsCjk(previous) && IsCjk(next)) return false;
            if ("（([《【“‘".IndexOf(previous) >= 0 || "，。！？；：、,.!?;:）)]》】”’".IndexOf(next) >= 0) return false;
            return true;
        }

        private static bool IsCjk(char value)
        {
            return (value >= '\u3400' && value <= '\u9fff') || (value >= '\uf900' && value <= '\ufaff');
        }

        public static bool TryAccept(string original, string response, out string organized, out string error)
        {
            organized = StripCodeFence(response ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
            string source = (original ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
            if (!string.Equals(ContentSignature(source), ContentSignature(organized), StringComparison.Ordinal))
            {
                organized = null;
                error = "AI 返回内容改变了原文字词、标点或顺序，已为安全起见取消应用。";
                return false;
            }
            error = null;
            return true;
        }

        public static bool NeedsImprovement(string value)
        {
            string text = Normalize(value);
            if (text.Length < 180) return false;
            int blankParagraphs = Regex.Matches(text, @"\n\s*\n").Count;
            if (blankParagraphs == 0 && text.Length >= 260) return true;
            if (Regex.IsMatch(text, @"(?im)(?<!\n\n)\n\s*(discussion|conclusion|introduction|references|摘要|引言|结论)\s*\n")) return true;
            if (Regex.IsMatch(text, @"(?m)(?<!\n\n)\n\s*\d{1,3}\s+[A-Z\u4e00-\u9fff]")) return true;
            string[] lines = text.Split('\n');
            int shortLines = 0, nonEmpty = 0;
            foreach (string line in lines)
            {
                int length = line.Trim().Length;
                if (length == 0) continue;
                nonEmpty++;
                if (length < 90) shortLines++;
            }
            return nonEmpty >= 6 && shortLines * 100 / nonEmpty >= 65;
        }

        public static string ImproveUnchangedLayout(string value)
        {
            string text = Normalize(value);
            string[] lines = text.Split('\n');
            StringBuilder structured = new StringBuilder(text.Length + 64);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                bool boundary = IsStructuralLine(line);
                if (boundary && structured.Length > 0 && !EndsWithBlankLine(structured)) structured.Append('\n');
                structured.Append(line);
                if (i < lines.Length - 1) structured.Append('\n');
                if (boundary && i < lines.Length - 1 && !EndsWithBlankLine(structured)) structured.Append('\n');
            }
            return BreakLongParagraphs(structured.ToString());
        }

        private static string BreakLongParagraphs(string text)
        {
            StringBuilder result = new StringBuilder(text.Length + 64);
            int sinceBreak = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                result.Append(c);
                if (c == '\n')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n') sinceBreak = 0;
                    continue;
                }
                sinceBreak++;
                bool sentenceEnd = c == '。' || c == '！' || c == '？' || c == '.' || c == '!' || c == '?';
                if (sentenceEnd && sinceBreak >= 320 && i + 1 < text.Length && text[i + 1] != '\n')
                {
                    result.Append("\n\n");
                    sinceBreak = 0;
                }
            }
            return result.ToString();
        }

        private static bool IsStructuralLine(string value)
        {
            string line = (value ?? "").Trim();
            if (line.Length == 0 || line.Length > 100) return false;
            if (Regex.IsMatch(line, @"^(discussion|conclusion|introduction|references|abstract|summary|摘要|引言|结论|参考文献)$", RegexOptions.IgnoreCase)) return true;
            if (Regex.IsMatch(line, @"^\d{1,3}[.)]?\s+[A-Z\u4e00-\u9fff]")) return true;
            return line.Length <= 60 && Regex.IsMatch(line, @"^[A-Z][A-Z\s/&-]{3,}$");
        }

        private static bool EndsWithBlankLine(StringBuilder value)
        {
            return value.Length >= 2 && value[value.Length - 1] == '\n' && value[value.Length - 2] == '\n';
        }

        private static string Normalize(string value)
        {
            return (value ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
        }

        private static string ContentSignature(string value)
        {
            string text = value ?? "";
            StringBuilder signature = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                if (!char.IsWhiteSpace(text[i])) { signature.Append(text[i]); continue; }
                int next = i + 1;
                while (next < text.Length && char.IsWhiteSpace(text[next])) next++;
                int previous = i - 1;
                while (previous >= 0 && char.IsWhiteSpace(text[previous])) previous--;
                if (previous >= 0 && next < text.Length && IsAsciiWordCharacter(text[previous]) && IsAsciiWordCharacter(text[next])) signature.Append('\u0001');
                i = next - 1;
            }
            return signature.ToString();
        }

        private static bool IsAsciiWordCharacter(char value)
        {
            return value <= 127 && (char.IsLetterOrDigit(value) || value == '_' || value == '\'');
        }

        private static string StripCodeFence(string value)
        {
            string text = (value ?? "").Trim('\r', '\n');
            if (!text.StartsWith("```", StringComparison.Ordinal) || !text.EndsWith("```", StringComparison.Ordinal)) return text;
            int firstBreak = text.IndexOf('\n');
            int lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
            return firstBreak >= 0 && lastFence > firstBreak ? text.Substring(firstBreak + 1, lastFence - firstBreak - 1).Trim('\r', '\n') : text;
        }
    }
}
