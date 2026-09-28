using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;

namespace ClipboardTrail
{
    public static class ExportWriter
    {
        public static string Extension(string format)
        {
            string value = (format ?? "txt").ToLowerInvariant();
            return value == "markdown" ? "md" : value;
        }

        public static string DialogFilter(string format)
        {
            switch (Extension(format))
            {
                case "md": return "Markdown (*.md)|*.md";
                case "pdf": return "PDF (*.pdf)|*.pdf";
                case "docx": return "Word (*.docx)|*.docx";
                case "csv": return "CSV (*.csv)|*.csv";
                case "json": return "JSON (*.json)|*.json";
                default: return "TXT (*.txt)|*.txt";
            }
        }

        public static void WriteMerged(string path, string format, IList<ClipboardItem> items, LibraryStore library, bool includeAnnotations = true, bool includeAiExplanations = true)
        {
            if (items == null || items.Count == 0) throw new InvalidOperationException("没有可导出的记录。");
            EnsureParent(path);
            Write(path, format, PrepareItems(items, includeAnnotations, includeAiExplanations), library);
        }

        public static void WriteSeparate(string directory, string format, IList<ClipboardItem> items, LibraryStore library, bool includeAnnotations = true, bool includeAiExplanations = true)
        {
            if (items == null || items.Count == 0) throw new InvalidOperationException("没有可导出的记录。");
            Directory.CreateDirectory(directory);
            string extension = Extension(format);
            IList<ClipboardItem> prepared = PrepareItems(items, includeAnnotations, includeAiExplanations);
            for (int i = 0; i < prepared.Count; i++)
            {
                ClipboardItem item = prepared[i];
                string preview = FirstMeaningfulLine(item.Text);
                string name = (i + 1).ToString("000") + "-" + item.CapturedAt.ToString("yyyyMMdd-HHmmss") + "-" + SafeFileName(preview);
                if (name.Length > 105) name = name.Substring(0, 105).TrimEnd();
                Write(Path.Combine(directory, name + "." + extension), format, new List<ClipboardItem> { item }, library);
            }
        }

        private static IList<ClipboardItem> PrepareItems(IList<ClipboardItem> items, bool includeAnnotations, bool includeAiExplanations)
        {
            List<ClipboardItem> result = new List<ClipboardItem>();
            foreach (ClipboardItem original in items)
            {
                ClipboardItem item = original.Clone();
                List<TextAnnotation> annotations = item.Annotations == null ? new List<TextAnnotation>() : item.Annotations.Where(x => x != null && !string.IsNullOrWhiteSpace(x.Note)).ToList();
                if (annotations.Count == 0 && !string.IsNullOrWhiteSpace(item.Note)) annotations.Add(new TextAnnotation { Quote = item.NoteQuote ?? "", Note = item.Note, CreatedAt = item.UpdatedAt ?? item.CapturedAt });
                List<SavedAiExplanation> explanations = item.AiExplanations == null ? new List<SavedAiExplanation>() : item.AiExplanations.Where(x => x != null && (!string.IsNullOrWhiteSpace(x.Question) || !string.IsNullOrWhiteSpace(x.Answer))).ToList();
                StringBuilder supplement = new StringBuilder();
                if (includeAnnotations && annotations.Count > 0)
                {
                    supplement.AppendLine().AppendLine().AppendLine("【文字批注】");
                    for (int i = 0; i < annotations.Count; i++)
                    {
                        TextAnnotation note = annotations[i];
                        supplement.AppendLine((i + 1) + ". 原文：" + (string.IsNullOrWhiteSpace(note.Quote) ? "（整条记录）" : note.Quote));
                        supplement.AppendLine("   批注：" + note.Note);
                    }
                }
                if (includeAiExplanations && explanations.Count > 0)
                {
                    supplement.AppendLine().AppendLine("【已保存的 AI 解释】");
                    for (int i = 0; i < explanations.Count; i++)
                    {
                        SavedAiExplanation explanation = explanations[i];
                        supplement.AppendLine((i + 1) + ". 问：" + (explanation.Question ?? ""));
                        supplement.AppendLine("   答：" + (explanation.Answer ?? ""));
                    }
                }
                item.Text = (item.Text ?? "") + supplement;
                item.Note = ""; item.NoteQuote = "";
                item.Annotations = includeAnnotations ? annotations : new List<TextAnnotation>();
                item.AiExplanations = includeAiExplanations ? explanations : new List<SavedAiExplanation>();
                result.Add(item);
            }
            return result;
        }

        private static void Write(string path, string format, IList<ClipboardItem> items, LibraryStore library)
        {
            switch (Extension(format))
            {
                case "md": File.WriteAllText(path, BuildMarkdown(items, library), new UTF8Encoding(false)); break;
                case "pdf": WritePdf(path, items, library); break;
                case "docx": WriteDocx(path, items, library); break;
                case "csv": File.WriteAllText(path, "\uFEFF" + BuildCsv(items, library), Encoding.UTF8); break;
                case "json": File.WriteAllText(path, BuildJson(items, library), new UTF8Encoding(false)); break;
                default: File.WriteAllText(path, "\uFEFF" + BuildText(items, library), Encoding.UTF8); break;
            }
        }

        private static string BuildText(IList<ClipboardItem> items, LibraryStore library)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("Clypsera 导出").AppendLine("导出时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).AppendLine("记录数量：" + items.Count).AppendLine();
            for (int i = 0; i < items.Count; i++)
            {
                ClipboardItem x = items[i];
                text.AppendLine("记录 " + (i + 1).ToString("000") + " · " + x.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss"));
                text.AppendLine("来源程序：" + NullText(x.SourceProcess));
                text.AppendLine("来源窗口：" + NullText(x.SourceTitle));
                text.AppendLine("归档位置：" + library.GetPath(x.DocumentId));
                text.AppendLine("主题：" + NullText(x.Category));
                if (!string.IsNullOrWhiteSpace(x.NoteQuote)) text.AppendLine("批注原文：" + x.NoteQuote);
                if (!string.IsNullOrWhiteSpace(x.Note)) text.AppendLine("文字批注：" + x.Note);
                text.AppendLine(new string('-', 52));
                text.AppendLine(x.Text ?? "").AppendLine();
            }
            return text.ToString();
        }

        private static string BuildMarkdown(IList<ClipboardItem> items, LibraryStore library)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("# Clypsera 导出").AppendLine();
            text.AppendLine("- 导出时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            text.AppendLine("- 记录数量：" + items.Count).AppendLine();
            for (int i = 0; i < items.Count; i++)
            {
                ClipboardItem x = items[i];
                text.AppendLine("## 记录 " + (i + 1).ToString("000") + " · " + x.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss")).AppendLine();
                text.AppendLine("- 来源程序：" + MarkdownInline(x.SourceProcess));
                text.AppendLine("- 来源窗口：" + MarkdownInline(x.SourceTitle));
                text.AppendLine("- 归档位置：" + MarkdownInline(library.GetPath(x.DocumentId)));
                text.AppendLine("- 主题：" + MarkdownInline(x.Category)).AppendLine();
                if (!string.IsNullOrWhiteSpace(x.NoteQuote)) text.AppendLine("> 批注原文：" + x.NoteQuote.Replace("\n", " ")).AppendLine();
                if (!string.IsNullOrWhiteSpace(x.Note)) text.AppendLine("**文字批注：** " + x.Note).AppendLine();
                text.AppendLine(x.Text ?? "").AppendLine();
            }
            return text.ToString();
        }

        private static string BuildCsv(IList<ClipboardItem> items, LibraryStore library)
        {
            StringBuilder csv = new StringBuilder("编号,时间,来源程序,来源窗口,归档位置,主题,批注原文,文字批注,文本\r\n");
            for (int i = 0; i < items.Count; i++)
            {
                ClipboardItem x = items[i];
                csv.AppendLine(string.Join(",", Csv((i + 1).ToString()), Csv(x.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss")), Csv(x.SourceProcess), Csv(x.SourceTitle), Csv(library.GetPath(x.DocumentId)), Csv(x.Category), Csv(x.NoteQuote), Csv(x.Note), Csv(x.Text)));
            }
            return csv.ToString();
        }

        private static string BuildJson(IList<ClipboardItem> items, LibraryStore library)
        {
            List<Dictionary<string, object>> records = new List<Dictionary<string, object>>();
            foreach (ClipboardItem x in items)
            {
                records.Add(new Dictionary<string, object>
                {
                    { "id", x.Id }, { "capturedAt", x.CapturedAt }, { "sourceProcess", x.SourceProcess }, { "sourceTitle", x.SourceTitle },
                    { "location", library.GetPath(x.DocumentId) }, { "locationId", x.DocumentId }, { "category", x.Category }, { "noteQuote", x.NoteQuote }, { "note", x.Note }, { "annotations", x.Annotations }, { "aiExplanations", x.AiExplanations }, { "text", x.Text }, { "updatedAt", x.UpdatedAt }
                });
            }
            return AppData.CreateSerializer().Serialize(new Dictionary<string, object> { { "product", "Clypsera" }, { "exportedAt", DateTime.Now }, { "count", records.Count }, { "records", records } });
        }

        private static void WriteDocx(string path, IList<ClipboardItem> items, LibraryStore library)
        {
            using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/><Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/></Types>");
                WriteEntry(archive, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
                WriteEntry(archive, "word/_rels/document.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
                WriteEntry(archive, "word/styles.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:styles xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=\"Microsoft YaHei\" w:eastAsia=\"Microsoft YaHei\"/><w:sz w:val=\"22\"/></w:rPr></w:rPrDefault></w:docDefaults><w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/><w:pPr><w:spacing w:after=\"120\" w:line=\"300\" w:lineRule=\"auto\"/></w:pPr></w:style><w:style w:type=\"paragraph\" w:styleId=\"Title\"><w:name w:val=\"Title\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/><w:pPr><w:spacing w:after=\"240\"/></w:pPr><w:rPr><w:b/><w:sz w:val=\"36\"/></w:rPr></w:style></w:styles>");
                StringBuilder body = new StringBuilder();
                AddWordTitle(body, "Clypsera 记录导出");
                AddWordParagraph(body, "导出时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "    记录数量：" + items.Count, false);
                for (int i = 0; i < items.Count; i++)
                {
                    if (i > 0) body.Append("<w:p><w:r><w:br w:type=\"page\"/></w:r></w:p>");
                    ClipboardItem x = items[i];
                    AddWordParagraph(body, "记录 " + (i + 1).ToString("000") + " · " + x.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss"), true);
                    AddWordParagraph(body, "来源程序：" + NullText(x.SourceProcess), false);
                    AddWordParagraph(body, "来源窗口：" + NullText(x.SourceTitle), false);
                    AddWordParagraph(body, "归档位置：" + library.GetPath(x.DocumentId), false);
                    AddWordParagraph(body, "主题：" + NullText(x.Category), false);
                    if (!string.IsNullOrWhiteSpace(x.NoteQuote)) AddWordParagraph(body, "批注原文：" + x.NoteQuote, false);
                    if (!string.IsNullOrWhiteSpace(x.Note)) AddWordParagraph(body, "文字批注：" + x.Note, true);
                    foreach (string line in NormalizeLines(x.Text)) AddWordParagraph(body, line, false);
                }
                string document = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>" + body + "<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"1080\" w:right=\"1080\" w:bottom=\"1080\" w:left=\"1080\"/></w:sectPr></w:body></w:document>";
                WriteEntry(archive, "word/document.xml", document);
            }
        }

        private static void AddWordParagraph(StringBuilder body, string value, bool bold)
        {
            body.Append("<w:p><w:r>"); if (bold) body.Append("<w:rPr><w:b/><w:sz w:val=\"28\"/></w:rPr>");
            body.Append("<w:t xml:space=\"preserve\">").Append(SecurityElement.Escape(value ?? "")).Append("</w:t></w:r></w:p>");
        }

        private static void AddWordTitle(StringBuilder body, string value)
        {
            body.Append("<w:p><w:pPr><w:pStyle w:val=\"Title\"/></w:pPr><w:r><w:t xml:space=\"preserve\">").Append(SecurityElement.Escape(value ?? "")).Append("</w:t></w:r></w:p>");
        }

        private static void WriteEntry(ZipArchive archive, string name, string value)
        {
            ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
            using (StreamWriter writer = new StreamWriter(entry.Open(), new UTF8Encoding(false))) writer.Write(value);
        }

        private static void WritePdf(string path, IList<ClipboardItem> items, LibraryStore library)
        {
            List<string> lines = new List<string> { "Clypsera 导出", "导出时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "    记录数量：" + items.Count, "" };
            for (int i = 0; i < items.Count; i++)
            {
                ClipboardItem x = items[i];
                lines.Add("记录 " + (i + 1).ToString("000") + " · " + x.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss"));
                lines.Add("来源程序：" + NullText(x.SourceProcess)); lines.Add("来源窗口：" + NullText(x.SourceTitle));
                lines.Add("归档位置：" + library.GetPath(x.DocumentId)); lines.Add("主题：" + NullText(x.Category)); lines.Add("");
                if (!string.IsNullOrWhiteSpace(x.NoteQuote)) lines.Add("批注原文：" + x.NoteQuote);
                if (!string.IsNullOrWhiteSpace(x.Note)) lines.Add("文字批注：" + x.Note);
                if (!string.IsNullOrWhiteSpace(x.NoteQuote) || !string.IsNullOrWhiteSpace(x.Note)) lines.Add("");
                lines.AddRange(NormalizeLines(x.Text)); lines.Add(""); lines.Add(new string('-', 52)); lines.Add("");
            }
            List<byte[]> images = RenderPdfPages(lines);
            WriteImagePdf(path, images, 1240, 1754);
        }

        private static List<byte[]> RenderPdfPages(List<string> rawLines)
        {
            List<string> wrapped = new List<string>();
            using (Bitmap measureBitmap = new Bitmap(1, 1))
            using (Graphics graphics = Graphics.FromImage(measureBitmap))
            using (Font font = new Font("Microsoft YaHei UI", 20F, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                foreach (string line in rawLines) wrapped.AddRange(WrapLine(graphics, font, line ?? "", 1080));
            }
            const int linesPerPage = 55;
            List<byte[]> pages = new List<byte[]>();
            int pageCount = Math.Max(1, (wrapped.Count + linesPerPage - 1) / linesPerPage);
            for (int page = 0; page < pageCount; page++)
            {
                using (Bitmap bitmap = new Bitmap(1240, 1754, PixelFormat.Format24bppRgb))
                using (Graphics graphics = Graphics.FromImage(bitmap))
                using (Font font = new Font("Microsoft YaHei UI", 20F, FontStyle.Regular, GraphicsUnit.Pixel))
                using (Font footer = new Font("Microsoft YaHei UI", 15F, FontStyle.Regular, GraphicsUnit.Pixel))
                {
                    graphics.Clear(Color.White); graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                    float y = 76;
                    foreach (string line in wrapped.Skip(page * linesPerPage).Take(linesPerPage)) { graphics.DrawString(line, font, Brushes.Black, 80, y); y += 29; }
                    string pageText = "Clypsera · " + (page + 1) + " / " + pageCount;
                    graphics.DrawString(pageText, footer, Brushes.Gray, 80, 1685);
                    using (MemoryStream stream = new MemoryStream()) { bitmap.Save(stream, ImageFormat.Jpeg); pages.Add(stream.ToArray()); }
                }
            }
            return pages;
        }

        private static List<string> WrapLine(Graphics graphics, Font font, string line, float width)
        {
            if (line.Length == 0) return new List<string> { "" };
            List<string> result = new List<string>(); string current = "";
            foreach (char character in line)
            {
                string candidate = current + character;
                if (current.Length > 0 && graphics.MeasureString(candidate, font).Width > width) { result.Add(current); current = character.ToString(); }
                else current = candidate;
            }
            result.Add(current); return result;
        }

        private static void WriteImagePdf(string path, IList<byte[]> images, int pixelWidth, int pixelHeight)
        {
            int pagesObject = 2; int objectCount = 2 + images.Count * 3;
            List<byte[]> objects = new List<byte[]> { null, null };
            StringBuilder kids = new StringBuilder();
            for (int i = 0; i < images.Count; i++) kids.Append((5 + i * 3) + " 0 R ");
            objects[0] = Ascii("<< /Type /Catalog /Pages 2 0 R >>");
            objects[1] = Ascii("<< /Type /Pages /Kids [" + kids + "] /Count " + images.Count + " >>");
            for (int i = 0; i < images.Count; i++)
            {
                int imageId = 3 + i * 3, contentId = 4 + i * 3, pageId = 5 + i * 3;
                byte[] jpeg = images[i];
                objects.Add(StreamObject("<< /Type /XObject /Subtype /Image /Width " + pixelWidth + " /Height " + pixelHeight + " /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length " + jpeg.Length + " >>", jpeg));
                byte[] command = Ascii("q 595 0 0 842 0 0 cm /Im0 Do Q");
                objects.Add(StreamObject("<< /Length " + command.Length + " >>", command));
                objects.Add(Ascii("<< /Type /Page /Parent " + pagesObject + " 0 R /MediaBox [0 0 595 842] /Resources << /XObject << /Im0 " + imageId + " 0 R >> >> /Contents " + contentId + " 0 R >>"));
            }
            using (FileStream file = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                WriteBytes(file, Ascii("%PDF-1.4\n%CLYP\n")); long[] offsets = new long[objectCount + 1];
                for (int i = 0; i < objects.Count; i++) { int id = i + 1; offsets[id] = file.Position; WriteBytes(file, Ascii(id + " 0 obj\n")); WriteBytes(file, objects[i]); WriteBytes(file, Ascii("\nendobj\n")); }
                long xref = file.Position; WriteBytes(file, Ascii("xref\n0 " + (objectCount + 1) + "\n0000000000 65535 f \n"));
                for (int i = 1; i <= objectCount; i++) WriteBytes(file, Ascii(offsets[i].ToString("0000000000") + " 00000 n \n"));
                WriteBytes(file, Ascii("trailer\n<< /Size " + (objectCount + 1) + " /Root 1 0 R >>\nstartxref\n" + xref + "\n%%EOF"));
            }
        }

        private static byte[] StreamObject(string dictionary, byte[] data)
        {
            using (MemoryStream stream = new MemoryStream()) { WriteBytes(stream, Ascii(dictionary + "\nstream\n")); WriteBytes(stream, data); WriteBytes(stream, Ascii("\nendstream")); return stream.ToArray(); }
        }
        private static byte[] Ascii(string text) { return Encoding.ASCII.GetBytes(text); }
        private static void WriteBytes(Stream stream, byte[] data) { stream.Write(data, 0, data.Length); }
        private static string Csv(string value) { return "\"" + (value ?? "").Replace("\"", "\"\"") + "\""; }
        private static string NullText(string value) { return string.IsNullOrWhiteSpace(value) ? "—" : value; }
        private static string MarkdownInline(string value) { return NullText(value).Replace("\\", "\\\\").Replace("`", "\\`").Replace("\r", " ").Replace("\n", " "); }
        private static IEnumerable<string> NormalizeLines(string value) { return (value ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'); }
        private static string FirstMeaningfulLine(string value) { return NormalizeLines(value).Select(x => x.Trim()).FirstOrDefault(x => x.Length > 0) ?? "记录"; }
        private static string SafeFileName(string value)
        {
            string result = value ?? "记录"; foreach (char invalid in Path.GetInvalidFileNameChars()) result = result.Replace(invalid, '_');
            result = result.Trim().TrimEnd('.'); return result.Length == 0 ? "记录" : result;
        }
        private static void EnsureParent(string path) { string parent = Path.GetDirectoryName(path); if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent); }
    }
}
