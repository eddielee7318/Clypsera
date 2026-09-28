using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ClipboardTrail
{
    public sealed class HistoryStore
    {
        private readonly object gate = new object();
        private readonly List<ClipboardItem> items = new List<ClipboardItem>();

        public HistoryStore()
        {
            Load();
        }

        private void Load()
        {
            Directory.CreateDirectory(AppData.DirectoryPath);
            lock (gate) items.Clear();
            if (!File.Exists(AppData.JournalPath)) return;
            Dictionary<string, ClipboardItem> map = new Dictionary<string, ClipboardItem>();
            List<string> order = new List<string>();
            foreach (string line in File.ReadLines(AppData.JournalPath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    JournalRecord record = AppData.CreateSerializer().Deserialize<JournalRecord>(line);
                    if (record == null) continue;
                    if (record.Op == "add" && record.Item != null)
                    {
                        map[record.Item.Id] = record.Item;
                        order.Remove(record.Item.Id);
                        order.Add(record.Item.Id);
                    }
                    else if (record.Op == "category" && map.ContainsKey(record.Id))
                    {
                        map[record.Id].Category = record.Category;
                        map[record.Id].AiClassified = record.AiClassified;
                    }
                    else if (record.Op == "document" && map.ContainsKey(record.Id))
                        map[record.Id].DocumentId = record.DocumentId;
                    else if (record.Op == "content" && map.ContainsKey(record.Id))
                    {
                        map[record.Id].Text = record.Text;
                        map[record.Id].Rtf = record.Rtf;
                        map[record.Id].UpdatedAt = record.UpdatedAt;
                    }
                    else if (record.Op == "source" && map.ContainsKey(record.Id))
                    {
                        map[record.Id].SourceProcess = record.SourceProcess;
                        map[record.Id].SourceTitle = record.SourceTitle;
                    }
                    else if (record.Op == "note" && map.ContainsKey(record.Id))
                    {
                        map[record.Id].Note = record.Note;
                        map[record.Id].NoteQuote = record.NoteQuote;
                        map[record.Id].Annotations = string.IsNullOrWhiteSpace(record.Note) ? new List<TextAnnotation>() : new List<TextAnnotation> { new TextAnnotation { Id = "legacy-" + record.Id, Quote = record.NoteQuote ?? "", Note = record.Note ?? "", CreatedAt = record.UpdatedAt ?? map[record.Id].CapturedAt } };
                        map[record.Id].UpdatedAt = record.UpdatedAt;
                    }
                    else if (record.Op == "annotations" && map.ContainsKey(record.Id))
                    {
                        map[record.Id].Annotations = record.Annotations == null ? new List<TextAnnotation>() : record.Annotations.Where(x => x != null).Select(x => x.Clone()).ToList();
                        TextAnnotation latest = map[record.Id].Annotations.LastOrDefault();
                        map[record.Id].Note = latest == null ? "" : latest.Note;
                        map[record.Id].NoteQuote = latest == null ? "" : latest.Quote;
                        map[record.Id].UpdatedAt = record.UpdatedAt;
                    }
                    else if (record.Op == "ai-explanations" && map.ContainsKey(record.Id))
                    {
                        map[record.Id].AiExplanations = record.AiExplanations == null ? new List<SavedAiExplanation>() : record.AiExplanations.Where(x => x != null).Select(x => x.Clone()).ToList();
                        map[record.Id].UpdatedAt = record.UpdatedAt;
                    }
                    else if (record.Op == "delete")
                    {
                        map.Remove(record.Id);
                        order.Remove(record.Id);
                    }
                }
                catch { }
            }
            foreach (string id in order)
                if (map.ContainsKey(id))
                {
                    ClipboardItem item = map[id];
                    if (item.Annotations == null) item.Annotations = new List<TextAnnotation>();
                    if (item.Annotations.Count == 0 && !string.IsNullOrWhiteSpace(item.Note)) item.Annotations.Add(new TextAnnotation { Id = "legacy-" + item.Id, Quote = item.NoteQuote ?? "", Note = item.Note, CreatedAt = item.UpdatedAt ?? item.CapturedAt });
                    if (item.AiExplanations == null) item.AiExplanations = new List<SavedAiExplanation>();
                    items.Add(item);
                }
        }

        internal ClipboardItem Add(string text, SourceInfo source, int maxLength)
        {
            return AddAt(text, source, maxLength, DateTime.Now);
        }

        internal ClipboardItem AddAt(string text, SourceInfo source, int maxLength, DateTime capturedAt)
        {
            if (text == null) return null;
            text = text.Replace("\0", "");
            if (text.Length > maxLength) text = text.Substring(0, maxLength) + "\n…（内容过长，已截断）";
            ClipboardItem item = new ClipboardItem
            {
                Id = Guid.NewGuid().ToString("N"),
                Text = text,
                CapturedAt = capturedAt,
                SourceProcess = source == null ? "未知" : source.ProcessName,
                SourceTitle = source == null ? "" : source.WindowTitle,
                Category = LocalClassifier.Classify(text),
                AiClassified = false,
                Annotations = new List<TextAnnotation>(),
                AiExplanations = new List<SavedAiExplanation>()
            };
            lock (gate)
            {
                items.Add(item);
                Append(new JournalRecord { Op = "add", Item = item });
            }
            return item.Clone();
        }

        public void UpdateCategory(string id, string category, bool ai)
        {
            if (string.IsNullOrWhiteSpace(category)) return;
            lock (gate)
            {
                ClipboardItem item = items.FirstOrDefault(x => x.Id == id);
                if (item == null) return;
                item.Category = category.Trim();
                item.AiClassified = ai;
                Append(new JournalRecord { Op = "category", Id = id, Category = item.Category, AiClassified = ai });
            }
        }

        public void UpdateDocument(string id, string documentId)
        {
            lock (gate)
            {
                ClipboardItem item = items.FirstOrDefault(x => x.Id == id);
                if (item == null) return;
                item.DocumentId = documentId;
                Append(new JournalRecord { Op = "document", Id = id, DocumentId = documentId });
            }
        }

        public void UpdateContent(string id, string text, string rtf)
        {
            lock (gate)
            {
                ClipboardItem item = items.FirstOrDefault(x => x.Id == id);
                if (item == null) return;
                item.Text = text ?? "";
                item.Rtf = rtf;
                item.UpdatedAt = DateTime.Now;
                Append(new JournalRecord { Op = "content", Id = id, Text = item.Text, Rtf = rtf, UpdatedAt = item.UpdatedAt });
            }
        }

        public void UpdateSource(string id, string processName, string windowTitle)
        {
            lock (gate)
            {
                ClipboardItem item = items.FirstOrDefault(x => x.Id == id);
                if (item == null) return;
                item.SourceProcess = string.IsNullOrWhiteSpace(processName) ? "未知" : processName.Trim();
                item.SourceTitle = windowTitle ?? "";
                Append(new JournalRecord { Op = "source", Id = id, SourceProcess = item.SourceProcess, SourceTitle = item.SourceTitle });
            }
        }

        public void UpdateNote(string id, string quote, string note)
        {
            lock (gate)
            {
                ClipboardItem item = items.FirstOrDefault(x => x.Id == id);
                if (item == null) return;
                item.Note = note ?? "";
                item.NoteQuote = quote ?? "";
                item.Annotations = string.IsNullOrWhiteSpace(item.Note) ? new List<TextAnnotation>() : new List<TextAnnotation> { new TextAnnotation { Id = "legacy-" + id, Quote = item.NoteQuote, Note = item.Note, CreatedAt = DateTime.Now } };
                item.UpdatedAt = DateTime.Now;
                Append(new JournalRecord { Op = "note", Id = id, Note = item.Note, NoteQuote = item.NoteQuote, UpdatedAt = item.UpdatedAt });
            }
        }

        public TextAnnotation AddAnnotation(string id, string quote, string note)
        {
            lock (gate)
            {
                ClipboardItem item = items.FirstOrDefault(x => x.Id == id);
                if (item == null || string.IsNullOrWhiteSpace(note)) return null;
                if (item.Annotations == null) item.Annotations = new List<TextAnnotation>();
                TextAnnotation annotation = new TextAnnotation { Id = Guid.NewGuid().ToString("N"), Quote = quote ?? "", Note = note.Trim(), CreatedAt = DateTime.Now };
                item.Annotations.Add(annotation);
                item.Note = annotation.Note; item.NoteQuote = annotation.Quote; item.UpdatedAt = DateTime.Now;
                Append(new JournalRecord { Op = "annotations", Id = id, Annotations = item.Annotations.Select(x => x.Clone()).ToList(), UpdatedAt = item.UpdatedAt });
                return annotation.Clone();
            }
        }

        public void UpdateAnnotation(string itemId, string annotationId, string note)
        {
            lock (gate)
            {
                ClipboardItem item = items.FirstOrDefault(x => x.Id == itemId);
                TextAnnotation annotation = item == null || item.Annotations == null ? null : item.Annotations.FirstOrDefault(x => x.Id == annotationId);
                if (annotation == null || string.IsNullOrWhiteSpace(note)) return;
                annotation.Note = note.Trim(); item.Note = annotation.Note; item.NoteQuote = annotation.Quote; item.UpdatedAt = DateTime.Now;
                Append(new JournalRecord { Op = "annotations", Id = itemId, Annotations = item.Annotations.Select(x => x.Clone()).ToList(), UpdatedAt = item.UpdatedAt });
            }
        }

        public void DeleteAnnotation(string itemId, string annotationId)
        {
            lock (gate)
            {
                ClipboardItem item = items.FirstOrDefault(x => x.Id == itemId);
                if (item == null || item.Annotations == null) return;
                item.Annotations.RemoveAll(x => x.Id == annotationId);
                TextAnnotation latest = item.Annotations.LastOrDefault();
                item.Note = latest == null ? "" : latest.Note; item.NoteQuote = latest == null ? "" : latest.Quote; item.UpdatedAt = DateTime.Now;
                Append(new JournalRecord { Op = "annotations", Id = itemId, Annotations = item.Annotations.Select(x => x.Clone()).ToList(), UpdatedAt = item.UpdatedAt });
            }
        }

        public void AddAiExplanation(string id, string question, string answer)
        {
            if (string.IsNullOrWhiteSpace(answer)) return;
            lock (gate)
            {
                ClipboardItem item = items.FirstOrDefault(x => x.Id == id);
                if (item == null) return;
                if (item.AiExplanations == null) item.AiExplanations = new List<SavedAiExplanation>();
                item.AiExplanations.Add(new SavedAiExplanation { Id = Guid.NewGuid().ToString("N"), Question = question ?? "", Answer = answer.Trim(), CreatedAt = DateTime.Now });
                item.UpdatedAt = DateTime.Now;
                Append(new JournalRecord { Op = "ai-explanations", Id = id, AiExplanations = item.AiExplanations.Select(x => x.Clone()).ToList(), UpdatedAt = item.UpdatedAt });
            }
        }

        public void Delete(string id)
        {
            lock (gate)
            {
                ClipboardItem item = items.FirstOrDefault(x => x.Id == id);
                if (item == null) return;
                items.Remove(item);
                Append(new JournalRecord { Op = "delete", Id = id });
            }
        }

        public List<ClipboardItem> Snapshot()
        {
            lock (gate) return items.Select(x => x.Clone()).ToList();
        }

        public void Reload() { Load(); }

        internal void ReplaceAll(IEnumerable<ClipboardItem> replacement)
        {
            lock (gate)
            {
                items.Clear();
                if (replacement != null) items.AddRange(replacement.Where(x => x != null).Select(x => x.Clone()));
                string temp = AppData.JournalPath + ".undo.tmp";
                StringBuilder text = new StringBuilder();
                foreach (ClipboardItem item in items) text.AppendLine(AppData.CreateSerializer().Serialize(new JournalRecord { Op = "add", Item = item }));
                File.WriteAllText(temp, text.ToString(), new UTF8Encoding(false));
                if (File.Exists(AppData.JournalPath)) File.Delete(AppData.JournalPath);
                File.Move(temp, AppData.JournalPath);
            }
        }

        private void Append(JournalRecord record)
        {
            string line = AppData.CreateSerializer().Serialize(record);
            File.AppendAllText(AppData.JournalPath, line + Environment.NewLine, new UTF8Encoding(false));
        }
    }

    internal static class LocalClassifier
    {
        public static string Classify(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "空白";
            string value = text.Trim();
            string lower = value.ToLowerInvariant();
            Uri uri;
            if (Uri.TryCreate(value, UriKind.Absolute, out uri) && (uri.Scheme == "http" || uri.Scheme == "https")) return "链接";
            if ((value.StartsWith("{") && value.EndsWith("}")) || (value.StartsWith("[") && value.EndsWith("]"))) return "数据/JSON";
            if (lower.Contains("public class ") || lower.Contains("function ") || lower.Contains("const ") || lower.Contains("import ") || lower.Contains("def ") || lower.Contains("SELECT ".ToLowerInvariant())) return "代码";
            if (value.Contains("@") && value.Contains(".") && !value.Contains(" ") && value.Length < 200) return "邮箱/账号";
            if ((value.Contains(":\\") || value.StartsWith("\\\\")) && value.Length < 500) return "文件路径";
            if (value.Length > 500 || value.Split('\n').Length > 8) return "长文摘录";
            if (value.Length < 40 && value.Split('\n').Length <= 2) return "短文本";
            return "普通文本";
        }
    }
}
