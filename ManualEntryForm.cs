using System;
using System.Drawing;
using System.Windows.Forms;

namespace ClipboardTrail
{
    internal sealed class ManualEntryForm : Form
    {
        private readonly TextBox content;
        public string ResultText { get; private set; }

        public ManualEntryForm(string initialText)
        {
            Text = Localizer.T("手动粘贴或录入", "Manual paste or entry");
            Icon = Branding.CreateIcon();
            Font = new Font("Microsoft YaHei UI", 9F);
            Width = 820; Height = 560; MinimumSize = new Size(680, 420);
            StartPosition = FormStartPosition.CenterParent;
            KeyPreview = true;

            Label hint = new Label
            {
                Dock = DockStyle.Fill,
                Text = Localizer.T("暂停采集时也可以在这里粘贴或输入内容。Ctrl+Enter 保存。", "Paste or type content here even while capture is paused. Press Ctrl+Enter to save."),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = false
            };
            content = new TextBox { Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, AcceptsTab = true, ScrollBars = ScrollBars.Both, WordWrap = true, Text = initialText ?? "", Font = new Font("Microsoft YaHei UI", 10.5F) };
            Button paste = new Button { Text = Localizer.T("粘贴", "Paste"), Dock = DockStyle.Fill, Margin = new Padding(0, 4, 8, 4) };
            Button clear = new Button { Text = Localizer.T("清空", "Clear"), Dock = DockStyle.Fill, Margin = new Padding(0, 4, 8, 4) };
            Button cancel = new Button { Text = Localizer.T("取消", "Cancel"), Dock = DockStyle.Fill, Margin = new Padding(0, 4, 8, 4), DialogResult = DialogResult.Cancel };
            Button save = new Button { Text = Localizer.T("保存到资料库", "Save to library"), Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 4) };
            paste.Click += delegate { try { if (Clipboard.ContainsText()) content.SelectedText = Clipboard.GetText(TextDataFormat.UnicodeText); } catch { } content.Focus(); };
            clear.Click += delegate { content.Clear(); content.Focus(); };
            save.Click += delegate { SaveAndClose(); };

            TableLayoutPanel buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
            buttons.Controls.Add(paste, 0, 0); buttons.Controls.Add(clear, 1, 0); buttons.Controls.Add(cancel, 3, 0); buttons.Controls.Add(save, 4, 0);

            TableLayoutPanel shell = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(16, 10, 16, 12) };
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            shell.Controls.Add(hint, 0, 0); shell.Controls.Add(content, 0, 1); shell.Controls.Add(buttons, 0, 2);
            Controls.Add(shell);
            AcceptButton = save; CancelButton = cancel;
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Control && e.KeyCode == Keys.Enter) { SaveAndClose(); e.SuppressKeyPress = true; } };
            Shown += delegate { content.Focus(); content.SelectionStart = content.TextLength; };
            UiStyle.Prepare(this, AppData.LoadSettings().AppTheme);
        }

        private void SaveAndClose()
        {
            string value = (content.Text ?? "").Trim();
            if (value.Length == 0) { MessageBox.Show(Localizer.T("请输入或粘贴一些文字。", "Enter or paste some text."), "Clypsera", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            ResultText = value; DialogResult = DialogResult.OK; Close();
        }
    }
}
