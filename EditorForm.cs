using System;
using System.Drawing;
using System.Windows.Forms;

namespace ClipboardTrail
{
    internal sealed class EditorForm : Form
    {
        private readonly RichTextBox editor;
        public string ResultText { get; private set; }
        public string ResultRtf { get; private set; }

        public EditorForm(ClipboardItem item)
        {
            Text = "编辑内容";
            Font = new Font("Microsoft YaHei UI", 9F);
            Width = 760;
            Height = 550;
            MinimumSize = new Size(520, 380);
            StartPosition = FormStartPosition.CenterParent;

            ToolStrip tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(6, 4, 6, 4) };
            ToolStripButton highlight = new ToolStripButton("🖍") { ToolTipText = Localizer.T("荧光笔", "Highlight"), Font = new Font("Segoe UI Emoji", 11F) };
            ToolStripButton underline = new ToolStripButton("U̲") { ToolTipText = Localizer.T("下划线", "Underline"), Font = new Font("Segoe UI", 11F, FontStyle.Bold) };
            ToolStripButton clear = new ToolStripButton("⌫") { ToolTipText = Localizer.T("清除所选格式", "Clear selected formatting"), Font = new Font("Segoe UI Symbol", 11F) };
            tools.Items.Add(highlight); tools.Items.Add(underline); tools.Items.Add(clear);
            Controls.Add(tools);

            editor = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                Font = new Font("Microsoft YaHei UI", 11F),
                DetectUrls = true,
                HideSelection = false,
                AcceptsTab = true,
                WordWrap = true
            };
            if (!string.IsNullOrWhiteSpace(item.Rtf))
            {
                try { editor.Rtf = item.Rtf; }
                catch { editor.Text = item.Text ?? ""; }
            }
            else editor.Text = item.Text ?? "";

            Panel buttons = new Panel { Dock = DockStyle.Bottom, Height = 54, BackColor = Color.FromArgb(245, 247, 250) };
            Button cancel = new Button { Text = "取消", Width = 90, Height = 32, Anchor = AnchorStyles.Top | AnchorStyles.Right, Left = 550, Top = 11, DialogResult = DialogResult.Cancel };
            Button save = new Button { Text = "保存", Width = 90, Height = 32, Anchor = AnchorStyles.Top | AnchorStyles.Right, Left = 650, Top = 11 };
            buttons.Controls.Add(cancel); buttons.Controls.Add(save);
            Controls.Add(buttons);
            Controls.Add(editor);
            editor.BringToFront();
            tools.BringToFront();

            highlight.Click += delegate
            {
                if (editor.SelectionLength == 0) return;
                editor.SelectionBackColor = editor.SelectionBackColor == Color.Yellow ? editor.BackColor : Color.Yellow;
            };
            underline.Click += delegate
            {
                if (editor.SelectionLength == 0) return;
                Font current = editor.SelectionFont ?? editor.Font;
                editor.SelectionFont = new Font(current, current.Style ^ FontStyle.Underline);
            };
            clear.Click += delegate
            {
                if (editor.SelectionLength == 0) return;
                editor.SelectionBackColor = editor.BackColor;
                Font current = editor.SelectionFont ?? editor.Font;
                editor.SelectionFont = new Font(current, current.Style & ~FontStyle.Underline);
            };
            save.Click += delegate
            {
                ResultText = editor.Text;
                ResultRtf = editor.Rtf;
                DialogResult = DialogResult.OK;
                Close();
            };
            AcceptButton = save;
            CancelButton = cancel;
            SettingsData appearance = AppData.LoadSettings();
            UiStyle.Prepare(this, appearance.AppTheme);
            Localizer.Apply(this);
        }
    }
}
