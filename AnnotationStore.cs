using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ClipboardTrail
{
    internal sealed class AnnotationStore
    {
        private readonly object gate = new object();
        private readonly List<DocumentAnnotation> items = new List<DocumentAnnotation>();

        public AnnotationStore()
        {
            Reload();
        }

        public void Reload()
        {
            Directory.CreateDirectory(AppData.DirectoryPath);
            lock (gate) items.Clear();
            try
            {
                if (File.Exists(AppData.AnnotationPath))
                {
                    List<DocumentAnnotation> loaded = AppData.CreateSerializer().Deserialize<List<DocumentAnnotation>>(File.ReadAllText(AppData.AnnotationPath, Encoding.UTF8));
                    if (loaded != null) lock (gate) items.AddRange(loaded.Where(x => x != null));
                }
            }
            catch { }
        }

        public DocumentAnnotation Add(string nodeId, string kind, string quote, string note, int page, string historyItemId = null, int textStart = -1, int textLength = 0, string id = null)
        {
            DocumentAnnotation item = new DocumentAnnotation { Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id, NodeId = nodeId, Kind = kind, Quote = quote ?? "", Note = note ?? "", Page = page, CreatedAt = DateTime.Now, HistoryItemId = historyItemId, TextStart = Math.Max(0, textStart), TextLength = Math.Max(0, textLength), HasTextAnchor = textStart >= 0 };
            lock (gate) { items.Add(item); Save(); }
            return item;
        }

        public void UpdateAnchor(string id, int textStart, int textLength)
        {
            if (string.IsNullOrWhiteSpace(id) || textStart < 0) return;
            lock (gate)
            {
                DocumentAnnotation item = items.FirstOrDefault(x => x.Id == id);
                if (item == null) return;
                item.TextStart = textStart;
                item.TextLength = Math.Max(0, textLength);
                item.HasTextAnchor = true;
                Save();
            }
        }

        public void UpdatePage(string id, int page)
        {
            if (string.IsNullOrWhiteSpace(id) || page <= 0) return;
            lock (gate)
            {
                DocumentAnnotation item = items.FirstOrDefault(x => x.Id == id);
                if (item == null) return;
                item.Page = page;
                Save();
            }
        }

        public List<DocumentAnnotation> All()
        {
            lock (gate) return items.OrderBy(x => x.CreatedAt).Select(x => x.Clone()).ToList();
        }

        public void LinkHistory(string id, string historyItemId)
        {
            lock (gate)
            {
                DocumentAnnotation item = items.FirstOrDefault(x => x.Id == id);
                if (item == null) return;
                item.HistoryItemId = historyItemId;
                Save();
            }
        }

        public void DeleteByHistoryItemId(string historyItemId)
        {
            if (string.IsNullOrEmpty(historyItemId)) return;
            lock (gate) { items.RemoveAll(x => x.HistoryItemId == historyItemId); Save(); }
        }

        public static string BuildRecordText(DocumentAnnotation item)
        {
            if (item == null) return "";
            string kind = item.Kind == "highlight" ? Localizer.T("荧光笔", "Highlight") : item.Kind == "underline" ? Localizer.T("下划线", "Underline") : Localizer.T("批注", "Annotation");
            StringBuilder text = new StringBuilder();
            text.Append(Localizer.IsEnglish ? "[Book " : "【图书").Append(kind);
            if (item.Page > 0) text.Append(Localizer.IsEnglish ? " · Page " : " · 第 ").Append(item.Page).Append(Localizer.IsEnglish ? "" : " 页");
            text.Append(Localizer.IsEnglish ? "]" : "】");
            if (!string.IsNullOrWhiteSpace(item.Quote)) text.Append("\n").Append(item.Quote.Trim());
            if (!string.IsNullOrWhiteSpace(item.Note)) text.Append(Localizer.IsEnglish ? "\n\nNote: " : "\n\n批注：").Append(item.Note.Trim());
            return text.ToString();
        }

        public List<DocumentAnnotation> ForNode(string nodeId)
        {
            lock (gate) return items.Where(x => x.NodeId == nodeId).OrderBy(x => x.CreatedAt).Select(x => x.Clone()).ToList();
        }

        internal void ReplaceAll(IEnumerable<DocumentAnnotation> replacement)
        {
            lock (gate)
            {
                items.Clear();
                if (replacement != null) items.AddRange(replacement.Where(x => x != null).Select(x => x.Clone()));
                Save();
            }
        }

        public void Delete(string id)
        {
            lock (gate) { items.RemoveAll(x => x.Id == id); Save(); }
        }

        private void Save()
        {
            string temp = AppData.AnnotationPath + ".tmp";
            File.WriteAllText(temp, AppData.CreateSerializer().Serialize(items), new UTF8Encoding(false));
            if (File.Exists(AppData.AnnotationPath)) File.Delete(AppData.AnnotationPath);
            File.Move(temp, AppData.AnnotationPath);
        }
    }
}
