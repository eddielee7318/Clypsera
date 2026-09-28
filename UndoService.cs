using System;
using System.Collections.Generic;

namespace ClipboardTrail
{
    internal static class UndoService
    {
        private sealed class UndoSnapshot
        {
            public string Description;
            public List<ClipboardItem> History;
            public List<LibraryNode> Library;
            public List<DocumentAnnotation> Annotations;
        }

        private static readonly object Gate = new object();
        private static readonly Stack<UndoSnapshot> Items = new Stack<UndoSnapshot>();

        internal static void Capture(string description, HistoryStore history, LibraryStore library, AnnotationStore annotations)
        {
            UndoSnapshot snapshot = new UndoSnapshot
            {
                Description = description ?? "操作",
                History = (history ?? new HistoryStore()).Snapshot(),
                Library = (library ?? new LibraryStore()).Snapshot(),
                Annotations = (annotations ?? new AnnotationStore()).All()
            };
            lock (Gate)
            {
                Items.Push(snapshot);
                while (Items.Count > 40)
                {
                    UndoSnapshot[] values = Items.ToArray();
                    Items.Clear();
                    for (int i = Math.Min(39, values.Length - 1); i >= 0; i--) Items.Push(values[i]);
                }
            }
        }

        internal static bool Undo(out string description)
        {
            UndoSnapshot snapshot;
            lock (Gate)
            {
                if (Items.Count == 0) { description = ""; return false; }
                snapshot = Items.Pop();
            }
            new HistoryStore().ReplaceAll(snapshot.History);
            new LibraryStore().ReplaceAll(snapshot.Library);
            new AnnotationStore().ReplaceAll(snapshot.Annotations);
            description = snapshot.Description;
            return true;
        }

        internal static void Clear() { lock (Gate) Items.Clear(); }
    }
}
