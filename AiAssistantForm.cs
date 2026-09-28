using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace ClipboardTrail
{
    internal sealed class AiAssistantForm : Form
    {
        private readonly SettingsData settings;
        private readonly HistoryStore store;
        private readonly LibraryStore library;
        private readonly ClipboardItem selectedItem;
        private readonly bool searchMode;
        private readonly RichTextBox transcript;
        private readonly TextBox input;
        private readonly Button send;
        private readonly Button saveInquiry;
        private readonly List<AiMessage> conversation = new List<AiMessage>();
        private Dictionary<string, string> resultMap = new Dictionary<string, string>();
        private string lastQuestion;
        private string lastAnswer;
        private readonly string initialQuestion;

        public event Action<List<string>> SearchResultsUpdated;
        public event EventHandler InquirySaved;

        public AiAssistantForm(SettingsData settings, HistoryStore store, LibraryStore library, ClipboardItem selectedItem, bool searchMode, string initialQuestion = null)
        {
            this.settings = settings;
            this.store = store;
            this.library = library;
            this.selectedItem = selectedItem;
            this.searchMode = searchMode;
            this.initialQuestion = initialQuestion;
            Text = searchMode ? Localizer.T("AI 搜索 · 临时对话", "AI Search · Temporary chat") : Localizer.T("AI 问询 · 可保存问答", "Ask AI · Q&A can be saved");
            Font = new Font("Microsoft YaHei UI", 9F);
            Width = 560;
            Height = 650;
            MinimumSize = new Size(560, 500);
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            ShowInTaskbar = true;

            Panel top = new Panel { Dock = DockStyle.Top, Height = 62, BackColor = Color.FromArgb(39, 46, 61) };
            Label title = new Label { Text = searchMode ? Localizer.T("AI 描述式搜索", "AI Semantic Search") : Localizer.T("针对选中内容提问", "Ask about the selection"), Left = 16, Top = 10, Width = 360, Height = 22, ForeColor = Color.White, Font = new Font(Font, FontStyle.Bold) };
            Label hint = new Label { Text = searchMode ? Localizer.T("对话和搜索结果不会写入历史", "This chat and its search results are temporary") : Localizer.T("只有点击“保存问答”才会写入历史", "Only Save Q&A writes this conversation to history"), Left = 16, Top = 34, Width = 500, Height = 20, ForeColor = Color.FromArgb(190, 201, 220) };
            top.Controls.Add(title); top.Controls.Add(hint);

            transcript = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.White, Font = new Font("Microsoft YaHei UI", 10F), DetectUrls = true, ScrollBars = RichTextBoxScrollBars.Vertical };
            Panel compose = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(244, 247, 251), Padding = Padding.Empty };
            TableLayoutPanel composeLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
            composeLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); composeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            composeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            input = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, Margin = new Padding(12, 9, 12, 5) };
            TableLayoutPanel buttons = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 1, ColumnCount = 5, Margin = Padding.Empty, Padding = new Padding(8, 5, 8, 9) };
            buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86)); buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 102)); buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108)); buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));
            send = new Button { Text = "发送  Ctrl+Enter", Dock = DockStyle.Fill, Margin = new Padding(4, 0, 0, 0) };
            Button summary = new Button { Text = "总结", Dock = DockStyle.Fill, Margin = new Padding(4, 0, 4, 0) };
            Button clear = new Button { Text = "清空对话", Dock = DockStyle.Fill, Margin = new Padding(4, 0, 4, 0) };
            saveInquiry = new Button { Text = "保存问答", Dock = DockStyle.Fill, Margin = new Padding(4, 0, 4, 0), Visible = !searchMode, Enabled = false };
            buttons.Controls.Add(summary, 0, 0); buttons.Controls.Add(clear, 1, 0); buttons.Controls.Add(saveInquiry, 2, 0); buttons.Controls.Add(send, 4, 0);
            composeLayout.Controls.Add(input, 0, 0); composeLayout.Controls.Add(buttons, 0, 1); compose.Controls.Add(composeLayout);
            TableLayoutPanel shell = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 62)); shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 146));
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            shell.Controls.Add(top, 0, 0); shell.Controls.Add(transcript, 0, 1); shell.Controls.Add(compose, 0, 2); Controls.Add(shell);

            send.Click += async delegate { await SendAsync(); };
            input.KeyDown += async delegate(object sender, KeyEventArgs e) { if (e.Control && e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await SendAsync(); } };
            summary.Click += async delegate
            {
                input.Text = searchMode ? Localizer.T("请总结当前找到的相关记录，并指出共同主题。", "Summarize the records found so far and identify their common themes.") : Localizer.T("请用简洁、容易理解的语言总结这段内容。", "Summarize this content in clear, concise language.");
                await SendAsync();
            };
            clear.Click += delegate
            {
                conversation.Clear(); transcript.Clear(); lastQuestion = null; lastAnswer = null; saveInquiry.Enabled = false;
                if (searchMode && SearchResultsUpdated != null) SearchResultsUpdated(null);
            };
            saveInquiry.Click += delegate { SaveConversation(); };
            AppendSystem(searchMode ? Localizer.T("请描述你想找的内容，例如：“找出最近复制过的、关于合同风险的段落”。", "Describe what you want to find, for example: Find recently copied passages about contract risks.") : Localizer.T("你可以询问含义、要求解释或生成总结。当前内容不会因为提问而改变。 ", "Ask for an explanation or summary. The selected content will not be changed."));
            if (!string.IsNullOrWhiteSpace(this.initialQuestion)) Shown += async delegate { input.Text = this.initialQuestion; await SendAsync(); };
            UiStyle.Prepare(this, settings.AppTheme);
            Localizer.Apply(this);
        }

        private async System.Threading.Tasks.Task SendAsync()
        {
            string question = input.Text.Trim();
            if (question.Length == 0 || !send.Enabled) return;
            input.Clear(); send.Enabled = false; input.Enabled = false;
            Append(Localizer.T("你", "You"), question, Color.FromArgb(27, 91, 165));
            lastQuestion = question;
            conversation.Add(new AiMessage { Role = "user", Content = question });
            OperationProgressForm progress = new OperationProgressForm(searchMode ? Localizer.T("AI 正在搜索", "AI is searching") : Localizer.T("AI 正在回答", "AI is answering"), settings.AppTheme);
            try
            {
                progress.Show(this);
                progress.SetStage(12, 24, Localizer.T("正在准备安全上下文…", "Preparing the safe context…"));
                string system = searchMode ? BuildSearchPrompt() : BuildInquiryPrompt();
                List<AiMessage> recent = conversation.Skip(Math.Max(0, conversation.Count - 10)).ToList();
                progress.SetStage(26, 88, searchMode ? Localizer.T("正在分析资料库并查找相关内容…", "Analyzing the library for relevant content…") : Localizer.T("正在分析所选内容并生成回答…", "Analyzing the selection and preparing an answer…"));
                string answer = await new AiTextClient(settings).CompleteAsync(system, recent, 900);
                progress.SetStage(94, 98, Localizer.T("正在整理显示结果…", "Preparing the result for display…"));
                conversation.Add(new AiMessage { Role = "assistant", Content = answer });
                lastAnswer = answer;
                if (searchMode)
                {
                    List<string> ids = ParseResultIds(answer);
                    if (SearchResultsUpdated != null) SearchResultsUpdated(ids);
                    answer = RemoveResultHeader(answer);
                }
                Append("DeepSeek", answer, Color.FromArgb(26, 122, 78));
                if (!searchMode) saveInquiry.Enabled = true;
            }
            catch (Exception ex) { Append(Localizer.T("错误", "Error"), ex.Message, Color.Firebrick); }
            finally
            {
                if (!progress.IsDisposed) progress.Close();
                progress.Dispose();
                send.Enabled = true; input.Enabled = true; input.Focus();
            }
        }

        private string BuildSearchPrompt()
        {
            resultMap = new Dictionary<string, string>();
            StringBuilder index = new StringBuilder();
            int number = 1;
            foreach (ClipboardItem item in store.Snapshot().OrderByDescending(x => x.CapturedAt).Where(IsSafeForAi).Take(120))
            {
                string token = "R" + number;
                resultMap[token] = item.Id;
                string text = (item.Text ?? "").Replace("\r", " ").Replace("\n", " ");
                if (text.Length > 500) text = text.Substring(0, 500) + "…";
                index.Append("[").Append(token).Append("] 时间=").Append(item.CapturedAt.ToString("yyyy-MM-dd HH:mm"))
                    .Append("；文档=").Append(library.GetPath(item.DocumentId)).Append("；来源=").Append(item.SourceProcess).Append("/").Append(item.SourceTitle)
                    .Append("；内容=").Append(text).Append("\n");
                number++;
            }
            return Localizer.IsEnglish
                ? "You are a private clipboard-library search assistant. Find semantically relevant records from the index. The first line must be RESULTS:R1,R2, or RESULTS:NONE. Explain in English from the second line. Never invent records. This AI search chat is temporary.\n\nRecord index:\n" + index
                : "你是私人剪贴板资料库的搜索助手。根据用户的自然语言描述，从索引中找出语义相关记录并回答，可结合之前的对话继续缩小范围。回答第一行必须严格写 RESULTS:R1,R2（没有匹配则 RESULTS:NONE），从第二行开始用中文解释结果。不得编造索引以外的记录。AI搜索对话不会保存。\n\n记录索引：\n" + index;
        }

        private static bool IsSafeForAi(ClipboardItem item)
        {
            if (item == null || TextSafety.FindSensitiveReason(item.Text) != null || TextSafety.LooksLikeStandaloneCredential(item.Text)) return false;
            string source = ((item.SourceProcess ?? "") + " " + (item.SourceTitle ?? "")).ToLowerInvariant();
            return !source.Contains("password") && !source.Contains("keepass") && !source.Contains("bitwarden") && !source.Contains("1password") && !source.Contains("密码");
        }

        private string BuildInquiryPrompt()
        {
            string text = selectedItem == null ? "" : selectedItem.Text ?? "";
            if (text.Length > 12000) text = text.Substring(0, 12000) + "\n…（后文已截断）";
            return Localizer.IsEnglish
                ? "You are a reading and writing assistant. Answer only about the selected record. Explain, analyze, or summarize in English, and say when information is insufficient.\nSource: " + (selectedItem == null ? "Unknown" : selectedItem.SourceProcess + " / " + selectedItem.SourceTitle) + "\nContent:\n" + text
                : "你是阅读与写作助手。只围绕下面这条选中记录回答问题，可解释含义、梳理逻辑或总结。若信息不足要明确说明。\n来源：" + (selectedItem == null ? "未知" : selectedItem.SourceProcess + " / " + selectedItem.SourceTitle) + "\n内容：\n" + text;
        }

        private List<string> ParseResultIds(string answer)
        {
            string first = (answer ?? "").Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            int colon = first.IndexOf(':');
            if (colon < 0) colon = first.IndexOf('：');
            if (colon < 0) return new List<string>();
            string raw = first.Substring(colon + 1).Trim();
            if (raw.Equals("NONE", StringComparison.OrdinalIgnoreCase)) return new List<string>();
            List<string> ids = new List<string>();
            foreach (string token in raw.Split(new char[] { ',', '，', ' ', ';', '；' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string key = token.Trim().Trim('[', ']', '`').ToUpperInvariant();
                if (resultMap.ContainsKey(key) && !ids.Contains(resultMap[key])) ids.Add(resultMap[key]);
            }
            return ids;
        }

        private static string RemoveResultHeader(string value)
        {
            int newline = value.IndexOfAny(new char[] { '\r', '\n' });
            return newline < 0 ? value : value.Substring(newline).Trim();
        }

        private void SaveConversation()
        {
            if (searchMode || string.IsNullOrWhiteSpace(lastAnswer)) return;
            string body = "问题：\n" + lastQuestion + "\n\n回答：\n" + lastAnswer;
            UndoService.Capture(Localizer.T("保存 AI 问答", "save AI Q&A"), store, library, new AnnotationStore());
            if (selectedItem != null && !string.IsNullOrWhiteSpace(selectedItem.Id)) store.AddAiExplanation(selectedItem.Id, lastQuestion, lastAnswer);
            ClipboardItem saved = store.Add(body, new SourceInfo { ProcessName = "DeepSeek", WindowTitle = "AI 问询" }, 50000);
            store.UpdateCategory(saved.Id, "AI问答", true);
            if (selectedItem != null && !string.IsNullOrEmpty(selectedItem.DocumentId)) store.UpdateDocument(saved.Id, selectedItem.DocumentId);
            saveInquiry.Enabled = false;
            AppendSystem(Localizer.T("这轮问答已保存为一条新记录。", "This Q&A was saved as a new record."));
            if (InquirySaved != null) InquirySaved(this, EventArgs.Empty);
        }

        private void AppendSystem(string text) { Append(Localizer.T("提示", "Tip"), text, Color.DimGray); }
        private void Append(string role, string text, Color color)
        {
            transcript.SelectionStart = transcript.TextLength;
            transcript.SelectionColor = color;
            transcript.SelectionFont = new Font(transcript.Font, FontStyle.Bold);
            transcript.AppendText(role + "\n");
            transcript.SelectionColor = settings.AppTheme == "dark" ? Color.FromArgb(232, 236, 242) : Color.FromArgb(35, 39, 47);
            transcript.SelectionFont = transcript.Font;
            transcript.AppendText((text ?? "") + "\n\n");
            transcript.SelectionStart = transcript.TextLength;
            transcript.ScrollToCaret();
        }
    }
}
