using System;
using System.Drawing;
using System.Windows.Forms;

namespace ClipboardTrail
{
    internal sealed class AnnotationEditorForm : Form
    {
        private readonly TextBox quote;
        private readonly TextBox note;
        private readonly NumericUpDown page;
        public string Quote { get { return quote.Text.Trim(); } }
        public string Note { get { return note.Text.Trim(); } }
        public int Page { get { return (int)page.Value; } }

        public AnnotationEditorForm(string title, string selectedText, int currentPage, bool requirePage = false)
        {
            Text = title;
            Font = new Font("Microsoft YaHei UI", 9F);
            Width = 660;
            Height = 520;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(540, 420);
            Label quoteLabel = new Label { Text = Localizer.T("选中文字或摘录", "Selected text or excerpt"), Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft, AutoEllipsis = false };
            quote = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, Text = selectedText ?? "", Font = new Font("Microsoft YaHei UI", 10F) };
            Label noteLabel = new Label { Text = Localizer.T("批注内容", "Annotation"), Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft, AutoEllipsis = false };
            note = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Microsoft YaHei UI", 10F) };
            Label pageLabel = new Label { Text = requirePage ? Localizer.T("软件记录页码", "Recorded page") : Localizer.T("页码（不知道可填 0）", "Page number (0 if unknown)"), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = false };
            page = new NumericUpDown { Dock = DockStyle.Fill, Minimum = 0, Maximum = 100000, Value = Math.Max(0, currentPage), Margin = new Padding(4, 8, 12, 8), Enabled = !requirePage };
            Button cancel = new Button { Text = Localizer.T("取消", "Cancel"), Dock = DockStyle.Fill, Margin = new Padding(4, 6, 8, 6), DialogResult = DialogResult.Cancel };
            Button save = new Button { Text = Localizer.T("保存批注", "Save annotation"), Dock = DockStyle.Fill, Margin = new Padding(4, 6, 0, 6) };
            TableLayoutPanel footer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 1, ColumnCount = 5, Margin = Padding.Empty, Padding = Padding.Empty };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Localizer.IsEnglish ? 210 : 170)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 142));
            footer.Controls.Add(pageLabel, 0, 0); footer.Controls.Add(page, 1, 0); footer.Controls.Add(cancel, 3, 0); footer.Controls.Add(save, 4, 0);
            TableLayoutPanel shell = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1, Padding = new Padding(16, 8, 16, 12) };
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 128)); shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            shell.Controls.Add(quoteLabel, 0, 0); shell.Controls.Add(quote, 0, 1); shell.Controls.Add(noteLabel, 0, 2); shell.Controls.Add(note, 0, 3); shell.Controls.Add(footer, 0, 4); Controls.Add(shell);
            save.Click += delegate { if (Quote.Length == 0 && Note.Length == 0) { MessageBox.Show("请输入摘录或批注。", "Clypsera"); return; } if (requirePage && Page <= 0) { MessageBox.Show(Localizer.T("请输入当前 PDF 页码，以便以后直接跳转。", "Enter the current PDF page so Clypsera can return to it directly."), "Clypsera", MessageBoxButtons.OK, MessageBoxIcon.Information); return; } DialogResult = DialogResult.OK; Close(); };
            AcceptButton = save; CancelButton = cancel;
            SettingsData appearance = AppData.LoadSettings();
            UiStyle.Prepare(this, appearance.AppTheme);
            Localizer.Apply(this);
        }
    }
}
