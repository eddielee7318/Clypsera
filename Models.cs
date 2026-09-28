using System;
using System.Collections.Generic;

namespace ClipboardTrail
{
    public sealed class ClipboardItem
    {
        public string Id { get; set; }
        public string Text { get; set; }
        public DateTime CapturedAt { get; set; }
        public string SourceProcess { get; set; }
        public string SourceTitle { get; set; }
        public string Category { get; set; }
        public bool AiClassified { get; set; }
        public string DocumentId { get; set; }
        public string Rtf { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string Note { get; set; }
        public string NoteQuote { get; set; }
        public List<TextAnnotation> Annotations { get; set; }
        public List<SavedAiExplanation> AiExplanations { get; set; }

        public ClipboardItem Clone()
        {
            ClipboardItem clone = (ClipboardItem)MemberwiseClone();
            clone.Annotations = Annotations == null ? new List<TextAnnotation>() : Annotations.ConvertAll(x => x == null ? null : x.Clone());
            clone.AiExplanations = AiExplanations == null ? new List<SavedAiExplanation>() : AiExplanations.ConvertAll(x => x == null ? null : x.Clone());
            return clone;
        }
    }

    public sealed class JournalRecord
    {
        public string Op { get; set; }
        public string Id { get; set; }
        public ClipboardItem Item { get; set; }
        public string Category { get; set; }
        public bool AiClassified { get; set; }
        public string DocumentId { get; set; }
        public string Text { get; set; }
        public string Rtf { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string SourceProcess { get; set; }
        public string SourceTitle { get; set; }
        public string Note { get; set; }
        public string NoteQuote { get; set; }
        public List<TextAnnotation> Annotations { get; set; }
        public List<SavedAiExplanation> AiExplanations { get; set; }
    }

    public sealed class TextAnnotation
    {
        public string Id { get; set; }
        public string Quote { get; set; }
        public string Note { get; set; }
        public DateTime CreatedAt { get; set; }
        public TextAnnotation Clone() { return (TextAnnotation)MemberwiseClone(); }
    }

    public sealed class SavedAiExplanation
    {
        public string Id { get; set; }
        public string Question { get; set; }
        public string Answer { get; set; }
        public DateTime CreatedAt { get; set; }
        public SavedAiExplanation Clone() { return (SavedAiExplanation)MemberwiseClone(); }
    }

    public sealed class LibraryNode
    {
        public string Id { get; set; }
        public string ParentId { get; set; }
        public string Name { get; set; }
        public string NodeType { get; set; }
        public DateTime CreatedAt { get; set; }
        public string FilePath { get; set; }
        public string FileFormat { get; set; }
        public string OriginalFileName { get; set; }
        // Live-folder metadata. The root stores SyncSourcePath; every mirrored
        // descendant stores its path relative to that root and keeps a stable ID.
        public string SyncMode { get; set; }
        public string SyncSourcePath { get; set; }
        public string SyncRootId { get; set; }
        public string SyncRelativePath { get; set; }
        public long SyncLength { get; set; }
        public DateTime? SyncLastWriteUtc { get; set; }

        public bool IsFolder { get { return NodeType == "folder"; } }
        public bool IsDocument { get { return NodeType == "document" || NodeType == "book"; } }
        public bool IsBook { get { return NodeType == "book"; } }
        public bool IsLiveFolder { get { return IsFolder && SyncMode == "live" && !string.IsNullOrWhiteSpace(SyncSourcePath); } }
        public bool IsLiveFolderMember { get { return !string.IsNullOrWhiteSpace(SyncRootId); } }

        public LibraryNode Clone() { return (LibraryNode)MemberwiseClone(); }
    }

    internal sealed class LiveFolderSyncResult
    {
        public string RootId { get; set; }
        public int AddedFiles { get; set; }
        public int AddedFolders { get; set; }
        public int UpdatedFiles { get; set; }
        public int RemovedFiles { get; set; }
        public int RemovedFolders { get; set; }
        public List<string> RemovedNodeIds { get; set; }
        public List<string> Errors { get; set; }
        public bool SourceUnavailable { get; set; }
        public bool HasChanges { get { return AddedFiles + AddedFolders + UpdatedFiles + RemovedFiles + RemovedFolders > 0; } }

        public LiveFolderSyncResult()
        {
            RemovedNodeIds = new List<string>();
            Errors = new List<string>();
        }
    }

    internal sealed class AiRouteResult
    {
        public string Category { get; set; }
        public string DocumentId { get; set; }
    }

    internal sealed class DocumentChoice
    {
        public string Id { get; set; }
        public string Path { get; set; }
    }

    internal sealed class AiMessage
    {
        public string Role { get; set; }
        public string Content { get; set; }
    }

    internal sealed class PasteReviewResult
    {
        public string Action { get; set; }
        public string Text { get; set; }
        public string Reason { get; set; }
    }

    public sealed class DocumentAnnotation
    {
        public string Id { get; set; }
        public string NodeId { get; set; }
        public string Kind { get; set; }
        public string Quote { get; set; }
        public string Note { get; set; }
        public int Page { get; set; }
        public DateTime CreatedAt { get; set; }
        public string HistoryItemId { get; set; }
        // Character offsets in the generated reading document. These are captured
        // when the annotation is created, so navigation never needs the browser's
        // Find dialog or keyboard input.
        public int TextStart { get; set; }
        public int TextLength { get; set; }
        public bool HasTextAnchor { get; set; }

        public DocumentAnnotation Clone() { return (DocumentAnnotation)MemberwiseClone(); }
    }

    public sealed class SettingsData
    {
        public bool OverlayVisible { get; set; }
        public bool DeepSeekEnabled { get; set; }
        public string DeepSeekEndpoint { get; set; }
        public string DeepSeekModel { get; set; }
        public string ApiKeyEncrypted { get; set; }
        public bool EnableLocalApi { get; set; }
        public int LocalApiPort { get; set; }
        public int MaxTextLength { get; set; }
        public bool StartWithWindows { get; set; }
        public string Language { get; set; }
        public string AppTheme { get; set; }
        public string ReaderTheme { get; set; }
        public string UiFontSize { get; set; }
        public bool CaptureWhitelistEnabled { get; set; }
        public string CaptureWhitelist { get; set; }
        public string CaptureBlacklist { get; set; }
        public string DefaultExportFolder { get; set; }

        public static SettingsData CreateDefault()
        {
            return new SettingsData
            {
                OverlayVisible = true,
                DeepSeekEnabled = false,
                DeepSeekEndpoint = "https://api.deepseek.com/chat/completions",
                DeepSeekModel = "deepseek-chat",
                ApiKeyEncrypted = "",
                EnableLocalApi = false,
                LocalApiPort = 17654,
                MaxTextLength = 10000,
                StartWithWindows = false,
                Language = "zh-CN",
                AppTheme = "light",
                ReaderTheme = "eye",
                UiFontSize = "medium",
                CaptureWhitelistEnabled = false,
                CaptureWhitelist = "",
                CaptureBlacklist = "1password, bitwarden, keepass",
                DefaultExportFolder = ""
            };
        }
    }
}
