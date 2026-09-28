using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace ClipboardTrail
{
    internal static class DocumentConverter
    {
        public static string PrepareReadablePath(LibraryNode node, out bool isPdf)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.FilePath) || !File.Exists(node.FilePath)) throw new FileNotFoundException("归档文件不存在。", node == null ? "" : node.FilePath);
            string extension = Path.GetExtension(node.FilePath).ToLowerInvariant();
            isPdf = extension == ".pdf";
            if (isPdf) return node.FilePath;
            // Version the generated page so documents imported by older releases
            // are rebuilt with the current text-decoding fixes without touching
            // the archived source file.
            string readerFile = "reader-v17.html";
            string output = Path.Combine(Path.GetDirectoryName(node.FilePath), readerFile);
            if (File.Exists(output)) return output;
            if (extension == ".docx") WriteHtml(output, node.Name, ExtractDocx(node.FilePath));
            else if (extension == ".doc") WriteHtml(output, node.Name, ExtractLegacyDoc(node.FilePath));
            else if (extension == ".md" || extension == ".markdown") WriteHtml(output, node.Name, ConvertMarkdown(node.FilePath));
            else if (extension == ".txt") WriteHtml(output, node.Name, ConvertPlainText(node.FilePath));
            else if (extension == ".epub") PrepareEpub(node.FilePath, output, node.Name);
            else if (extension == ".mobi" || extension == ".azw3" || extension == ".aw3") PrepareMobi(node.FilePath, output, node.Name);
            else throw new InvalidOperationException("不支持的阅读格式：" + extension);
            return output;
        }

        private static string ExtractDocx(string path)
        {
            using (ZipArchive archive = ZipFile.OpenRead(path))
            {
                return DocxHtmlConverter.Convert(archive);
            }
        }

        private static string ExtractLegacyDoc(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            List<string> fragments = new List<string>();
            StringBuilder unicode = new StringBuilder();
            for (int i = 0; i + 1 < data.Length; i += 2)
            {
                char c = (char)(data[i] | (data[i + 1] << 8));
                if (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || IsCommonPunctuation(c)) unicode.Append(c);
                else { FlushFragment(unicode, fragments); unicode.Clear(); }
            }
            FlushFragment(unicode, fragments);
            StringBuilder ascii = new StringBuilder();
            for (int i = 0; i < data.Length; i++)
            {
                char c = (char)data[i];
                if (c >= 32 && c <= 126) ascii.Append(c);
                else { FlushFragment(ascii, fragments); ascii.Clear(); }
            }
            FlushFragment(ascii, fragments);
            AddEncodedWordFragments(data, Encoding.UTF8, fragments);
            try { AddEncodedWordFragments(data, Encoding.GetEncoding(936), fragments); } catch { }
            IEnumerable<string> useful = fragments.Select(x => RepairWordText(Regex.Replace(x, @"\s+", " ").Trim())).Where(x => x.Length >= 8).Distinct().OrderBy(x => WordTextPenalty(x)).Take(5000);
            string body = string.Join("\n", useful.Select(x => "<p>" + WebUtility.HtmlEncode(x) + "</p>").ToArray());
            if (body.Length == 0) body = "<p>该旧版 DOC 文件没有提取到可读文本。建议另存为 DOCX 后重新导入。</p>";
            return body;
        }

        private static string ConvertMarkdown(string path)
        {
            string markdown;
            using (StreamReader reader = new StreamReader(path, Encoding.UTF8, true)) markdown = reader.ReadToEnd();
            string[] lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            StringBuilder html = new StringBuilder();
            bool inCode = false;
            bool inUnorderedList = false;
            bool inOrderedList = false;
            foreach (string sourceLine in lines)
            {
                string line = sourceLine ?? "";
                if (line.TrimStart().StartsWith("```"))
                {
                    CloseMarkdownLists(html, ref inUnorderedList, ref inOrderedList);
                    html.Append(inCode ? "</code></pre>" : "<pre><code>");
                    inCode = !inCode;
                    continue;
                }
                if (inCode) { html.Append(WebUtility.HtmlEncode(line)).Append("\n"); continue; }
                if (string.IsNullOrWhiteSpace(line))
                {
                    CloseMarkdownLists(html, ref inUnorderedList, ref inOrderedList);
                    continue;
                }
                Match heading = Regex.Match(line, @"^(#{1,6})\s+(.+)$");
                if (heading.Success)
                {
                    CloseMarkdownLists(html, ref inUnorderedList, ref inOrderedList);
                    int level = heading.Groups[1].Value.Length;
                    html.Append("<h").Append(level).Append(">").Append(RenderMarkdownInline(heading.Groups[2].Value)).Append("</h").Append(level).Append(">");
                    continue;
                }
                if (Regex.IsMatch(line.Trim(), @"^([-*_])\1{2,}$"))
                {
                    CloseMarkdownLists(html, ref inUnorderedList, ref inOrderedList); html.Append("<hr>"); continue;
                }
                Match unordered = Regex.Match(line, @"^\s*[-+*]\s+(.+)$");
                if (unordered.Success)
                {
                    if (inOrderedList) { html.Append("</ol>"); inOrderedList = false; }
                    if (!inUnorderedList) { html.Append("<ul>"); inUnorderedList = true; }
                    html.Append("<li>").Append(RenderMarkdownInline(unordered.Groups[1].Value)).Append("</li>");
                    continue;
                }
                Match ordered = Regex.Match(line, @"^\s*\d+[.)]\s+(.+)$");
                if (ordered.Success)
                {
                    if (inUnorderedList) { html.Append("</ul>"); inUnorderedList = false; }
                    if (!inOrderedList) { html.Append("<ol>"); inOrderedList = true; }
                    html.Append("<li>").Append(RenderMarkdownInline(ordered.Groups[1].Value)).Append("</li>");
                    continue;
                }
                CloseMarkdownLists(html, ref inUnorderedList, ref inOrderedList);
                Match quote = Regex.Match(line, @"^\s*>\s?(.*)$");
                if (quote.Success) html.Append("<blockquote>").Append(RenderMarkdownInline(quote.Groups[1].Value)).Append("</blockquote>");
                else html.Append("<p>").Append(RenderMarkdownInline(line.Trim())).Append("</p>");
            }
            CloseMarkdownLists(html, ref inUnorderedList, ref inOrderedList);
            if (inCode) html.Append("</code></pre>");
            return html.Length == 0 ? "<p>Markdown 文档为空。</p>" : html.ToString();
        }

        private static string ConvertPlainText(string path)
        {
            string value = File.ReadAllText(path, DetectEncoding(path));
            if (value.Length == 0) return "<p>TXT 文档为空。</p>";
            return "<pre class='plain-text'>" + WebUtility.HtmlEncode(value) + "</pre>";
        }

        private static void CloseMarkdownLists(StringBuilder html, ref bool unordered, ref bool ordered)
        {
            if (unordered) { html.Append("</ul>"); unordered = false; }
            if (ordered) { html.Append("</ol>"); ordered = false; }
        }

        private static string RenderMarkdownInline(string text)
        {
            string value = WebUtility.HtmlEncode(text ?? "");
            value = Regex.Replace(value, @"`([^`]+)`", "<code>$1</code>");
            value = Regex.Replace(value, @"\[([^\]]+)\]\(([^\s\)]+)\)", delegate(Match match)
            {
                string href = WebUtility.HtmlDecode(match.Groups[2].Value);
                if (!href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !href.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) && !href.StartsWith("#")) return match.Groups[1].Value + " (" + match.Groups[2].Value + ")";
                return "<a href='" + WebUtility.HtmlEncode(href) + "'>" + match.Groups[1].Value + "</a>";
            });
            value = Regex.Replace(value, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
            value = Regex.Replace(value, @"__(.+?)__", "<strong>$1</strong>");
            value = Regex.Replace(value, @"(?<!\*)\*([^*]+)\*(?!\*)", "<em>$1</em>");
            return value;
        }

        private static bool IsCommonPunctuation(char c)
        {
            return "，。！？；：、“”‘’（）《》【】—…,.!?;:'\"()-".IndexOf(c) >= 0;
        }

        internal static string RepairWordText(string text)
        {
            string original = (text ?? "").Replace("\0", "").Replace("\u000B", "\n").Trim();
            if (original.Length == 0) return original;
            List<string> candidates = new List<string> { original, TextSafety.TryLocalRepair(original) };
            string western = TryRedecode(original, 1252); if (western != null) candidates.Add(western);
            string chinese = TryRedecode(original, 936); if (chinese != null) candidates.Add(chinese);
            string chineseFull = TryRedecode(original, 54936); if (chineseFull != null) candidates.Add(chineseFull);
            return candidates.Where(x => !string.IsNullOrWhiteSpace(x)).OrderBy(x => WordTextPenalty(x)).ThenByDescending(x => x.Length).First();
        }

        private static string TryRedecode(string text, int sourceCodePage)
        {
            try
            {
                Encoding source = Encoding.GetEncoding(sourceCodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
                Encoding utf8 = new UTF8Encoding(false, true);
                return utf8.GetString(source.GetBytes(text));
            }
            catch { return null; }
        }

        private static int WordTextPenalty(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return int.MaxValue;
            int score = text.Count(c => c == '\uFFFD' || (c < 32 && c != '\r' && c != '\n' && c != '\t')) * 100;
            string[] markers = new string[] { "Ã", "Â", "â€", "ä½", "å¥", "浣犲", "鐨", "锛", "銆", "鈥", "鏄", "杩", "绗", "闂", "鎴戜", "浜嗘" };
            foreach (string marker in markers)
            {
                int start = 0;
                while ((start = text.IndexOf(marker, start, StringComparison.Ordinal)) >= 0) { score += 18; start += marker.Length; }
            }
            return score;
        }

        private static void AddEncodedWordFragments(byte[] data, Encoding encoding, List<string> fragments)
        {
            string decoded;
            try { decoded = encoding.GetString(data); } catch { return; }
            StringBuilder value = new StringBuilder();
            foreach (char c in decoded)
            {
                if (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || IsCommonPunctuation(c)) value.Append(c);
                else { FlushFragment(value, fragments); value.Clear(); }
            }
            FlushFragment(value, fragments);
        }

        private static void FlushFragment(StringBuilder value, List<string> target)
        {
            string text = value.ToString().Trim();
            if (text.Length >= 6) target.Add(text);
        }

        private static void PrepareEpub(string source, string output, string title)
        {
            string extractRoot = Path.Combine(Path.GetDirectoryName(output), "epub-content");
            if (!Directory.Exists(extractRoot)) ExtractZipSafe(source, extractRoot);
            string containerPath = Path.Combine(extractRoot, "META-INF", "container.xml");
            if (!File.Exists(containerPath)) throw new InvalidDataException("EPUB 缺少 META-INF/container.xml。");
            XmlDocument container = new XmlDocument(); container.Load(containerPath);
            XmlNode rootfile = container.SelectSingleNode("//*[local-name()='rootfile']");
            if (rootfile == null || rootfile.Attributes["full-path"] == null) throw new InvalidDataException("EPUB 无法定位内容清单。");
            string opfRelative = rootfile.Attributes["full-path"].Value.Replace('/', Path.DirectorySeparatorChar);
            string opfPath = SafeCombine(extractRoot, opfRelative);
            XmlDocument opf = new XmlDocument(); opf.Load(opfPath);
            Dictionary<string, string> manifest = new Dictionary<string, string>();
            foreach (XmlNode item in opf.SelectNodes("//*[local-name()='manifest']/*[local-name()='item']"))
            {
                if (item.Attributes["id"] != null && item.Attributes["href"] != null) manifest[item.Attributes["id"].Value] = item.Attributes["href"].Value;
            }
            string contentRoot = Path.GetDirectoryName(opfPath);
            StringBuilder combined = new StringBuilder();
            foreach (XmlNode itemref in opf.SelectNodes("//*[local-name()='spine']/*[local-name()='itemref']"))
            {
                string id = itemref.Attributes["idref"] == null ? "" : itemref.Attributes["idref"].Value;
                if (!manifest.ContainsKey(id)) continue;
                string chapterPath = SafeCombine(contentRoot, Uri.UnescapeDataString(manifest[id]).Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(chapterPath)) continue;
                string html = File.ReadAllText(chapterPath, DetectEncoding(chapterPath));
                Match body = Regex.Match(html, @"(?is)<body[^>]*>(.*?)</body>");
                string section = body.Success ? body.Groups[1].Value : html;
                section = SanitizeHtml(section);
                section = RewriteRelativeLinks(section, chapterPath);
                combined.Append("<section class='chapter'>").Append(section).Append("</section><hr>");
            }
            if (combined.Length == 0) throw new InvalidDataException("EPUB 没有找到可读章节，文件可能受 DRM 保护或结构不完整。");
            WriteHtml(output, title, combined.ToString());
        }

        private static void PrepareMobi(string source, string output, string title)
        {
            try
            {
                string html = ExtractMobiHtml(source);
                WriteHtml(output, title, SanitizeHtml(html));
            }
            catch (NotSupportedException)
            {
                string calibre = FindCalibre();
                if (calibre == null) throw new NotSupportedException("该 MOBI/AZW3 使用了复杂压缩或 DRM。请安装 Calibre 后重新打开；Clypsera 会自动调用 ebook-convert。带 DRM 的电子书无法读取。");
                string converted = Path.Combine(Path.GetDirectoryName(output), "calibre-converted.epub");
                ProcessStartInfo info = new ProcessStartInfo(calibre, "\"" + source + "\" \"" + converted + "\"") { UseShellExecute = false, CreateNoWindow = true };
                using (Process process = Process.Start(info))
                {
                    if (!process.WaitForExit(120000) || process.ExitCode != 0 || !File.Exists(converted)) throw new InvalidOperationException("Calibre 转换电子书失败。");
                }
                PrepareEpub(converted, output, title);
            }
        }

        private static string ExtractMobiHtml(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            if (data.Length < 100) throw new InvalidDataException("MOBI/AZW3 文件过小。");
            int recordCount = ReadUInt16(data, 76);
            if (recordCount < 2 || data.Length < 78 + recordCount * 8) throw new InvalidDataException("无效的 PalmDB 记录表。");
            int first = (int)ReadUInt32(data, 78);
            int compression = ReadUInt16(data, first);
            int textLength = (int)ReadUInt32(data, first + 4);
            int textRecords = ReadUInt16(data, first + 8);
            int encryption = ReadUInt16(data, first + 12);
            if (encryption != 0) throw new NotSupportedException("带 DRM 的电子书无法读取。");
            if (compression != 1 && compression != 2) throw new NotSupportedException("暂不支持该 MOBI 压缩方式。");
            int encodingCode = first + 32 <= data.Length && Encoding.ASCII.GetString(data, first + 16, 4) == "MOBI" ? (int)ReadUInt32(data, first + 28) : 1252;
            List<byte> text = new List<byte>();
            for (int record = 1; record <= textRecords && record < recordCount; record++)
            {
                int start = (int)ReadUInt32(data, 78 + record * 8);
                int end = record + 1 < recordCount ? (int)ReadUInt32(data, 78 + (record + 1) * 8) : data.Length;
                if (start < 0 || end <= start || end > data.Length) continue;
                byte[] block = new byte[end - start]; Array.Copy(data, start, block, 0, block.Length);
                text.AddRange(compression == 2 ? DecompressPalmDoc(block) : block);
                if (text.Count >= textLength) break;
            }
            if (textLength > 0 && text.Count > textLength) text.RemoveRange(textLength, text.Count - textLength);
            Encoding encoding = encodingCode == 65001 ? Encoding.UTF8 : Encoding.GetEncoding(1252);
            string result = encoding.GetString(text.ToArray()).Replace("\0", "");
            if (string.IsNullOrWhiteSpace(result)) throw new InvalidDataException("电子书没有提取到可读文字。");
            return result;
        }

        private static byte[] DecompressPalmDoc(byte[] input)
        {
            List<byte> output = new List<byte>();
            int i = 0;
            while (i < input.Length)
            {
                byte c = input[i++];
                if (c == 0) output.Add(0);
                else if (c <= 8)
                {
                    int count = Math.Min(c, input.Length - i);
                    for (int j = 0; j < count; j++) output.Add(input[i++]);
                }
                else if (c <= 0x7F) output.Add(c);
                else if (c <= 0xBF)
                {
                    if (i >= input.Length) break;
                    int pair = (c << 8) | input[i++];
                    int distance = (pair >> 3) & 0x7FF;
                    int length = (pair & 7) + 3;
                    if (distance <= 0 || distance > output.Count) continue;
                    for (int j = 0; j < length; j++) output.Add(output[output.Count - distance]);
                }
                else { output.Add(0x20); output.Add((byte)(c ^ 0x80)); }
            }
            return output.ToArray();
        }

        private static int ReadUInt16(byte[] data, int offset) { if (offset + 2 > data.Length) return 0; return (data[offset] << 8) | data[offset + 1]; }
        private static uint ReadUInt32(byte[] data, int offset) { if (offset + 4 > data.Length) return 0; return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3]; }

        private static void ExtractZipSafe(string archivePath, string destination)
        {
            Directory.CreateDirectory(destination);
            string root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
            using (ZipArchive archive = ZipFile.OpenRead(archivePath))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string target = Path.GetFullPath(Path.Combine(destination, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("压缩包包含不安全路径。");
                    if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    entry.ExtractToFile(target, true);
                }
            }
        }

        private static string SafeCombine(string root, string relative)
        {
            string rootFull = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
            string value = Path.GetFullPath(Path.Combine(root, relative));
            if (!value.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) && !value.Equals(root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("电子书引用了不安全路径。");
            return value;
        }

        private static string RewriteRelativeLinks(string html, string chapterPath)
        {
            return Regex.Replace(html, "(?i)(src|href)\\s*=\\s*([\"'])([^\"']+)\\2", delegate(Match match)
            {
                string link = match.Groups[3].Value;
                if (link.StartsWith("#") || link.StartsWith("data:") || link.StartsWith("http:") || link.StartsWith("https:")) return match.Value;
                string absolute = new Uri(new Uri(Path.GetFullPath(chapterPath)), link).AbsoluteUri;
                return match.Groups[1].Value + "=" + match.Groups[2].Value + absolute + match.Groups[2].Value;
            });
        }

        private static string SanitizeHtml(string html)
        {
            html = Regex.Replace(html ?? "", @"(?is)<script[^>]*>.*?</script>", "");
            html = Regex.Replace(html, @"(?is)<(iframe|object|embed)[^>]*>.*?</\1>", "");
            html = Regex.Replace(html, @"(?i)\s+on[a-z]+\s*=\s*([""']).*?\1", "");
            return html;
        }

        private static Encoding DetectEncoding(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF) return new UTF8Encoding(true);
            if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE) return Encoding.Unicode;
            if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF) return Encoding.BigEndianUnicode;
            try
            {
                new UTF8Encoding(false, true).GetString(data);
                return new UTF8Encoding(false);
            }
            catch (DecoderFallbackException)
            {
                try { return Encoding.GetEncoding(936); }
                catch { return Encoding.Default; }
            }
        }

        private static void WriteHtml(string output, string title, string body)
        {
            string html = "<!doctype html><html><head><meta charset='utf-8'><title>" + WebUtility.HtmlEncode(title) + "</title><style>body{font-family:'Microsoft YaHei UI',sans-serif;line-height:1.85;max-width:920px;margin:32px auto;padding:0 28px;color:#242932;background:#fff}img{max-width:100%;height:auto}p{margin:.75em 0}h1,h2,h3{line-height:1.35}hr{border:0;border-top:1px solid #ddd;margin:2em 0}.chapter{margin-bottom:2em}mark{background:#fff59d}blockquote{margin:1em 0;padding:.35em 1em;border-left:4px solid #8ca2c5;background:#f4f6f9;color:#4b5566}pre{padding:14px;overflow:auto;background:#f3f5f7;border-radius:6px}pre.plain-text{white-space:pre-wrap;overflow-wrap:anywhere;font-family:'Microsoft YaHei UI',sans-serif;background:transparent;padding:0}code{font-family:Consolas,monospace;background:#f3f5f7;padding:.1em .3em;border-radius:3px}pre code{padding:0}li{margin:.3em 0}</style></head><body>" + body + "</body></html>";
            File.WriteAllText(output, html, new UTF8Encoding(false));
        }

        private static string FindCalibre()
        {
            string[] candidates = new string[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Calibre2", "ebook-convert.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Calibre2", "ebook-convert.exe")
            };
            return candidates.FirstOrDefault(File.Exists);
        }
    }
}
