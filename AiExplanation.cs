using System;
using System.Text.RegularExpressions;

namespace ClipboardTrail
{
    internal static class AiExplanation
    {
        public static bool IsDictionaryWord(string text)
        {
            return Regex.IsMatch((text ?? "").Trim(), @"^[A-Za-z][A-Za-z'’-]{0,48}$");
        }

        public static string BuildQuestion(string text)
        {
            string value = (text ?? "").Trim();
            if (IsDictionaryWord(value))
                return Localizer.IsEnglish
                    ? "Explain “" + value + "” like a concise dictionary entry: pronunciation, parts of speech, core definitions, common collocations, and one short example sentence."
                    : "请按词典格式解释英文单词“" + value + "”：给出音标、词性、核心中文释义、常见搭配，以及一个简短例句和译文。";
            return Localizer.IsEnglish
                ? "Explain the selected passage clearly: its meaning, reasoning, context, and any difficult wording."
                : "请解释所选这段话的含义、上下文逻辑和可能的难点，用清楚简洁的中文回答。";
        }
    }
}
