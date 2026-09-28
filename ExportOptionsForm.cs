using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ClipboardTrail
{
    internal sealed class ExportOptionsForm : Form
    {
        private readonly ComboBox scope;
        private readonly ComboBox format;
        private readonly ComboBox mode;
        private readonly CheckBox includeAnnotations;
        private readonly CheckBox includeAiExplanations;

        public string Scope { get; private set; }
        public string Format { get; private set; }
        public bool Separate { get; private set; }
        public bool IncludeAnnotations { get; private set; }
        public bool IncludeAiExplanations { get; private set; }

        public ExportOptionsForm(bool hasCurrent, int selectedCount, int folderCount, int viewCount, string preferredScope)
        {
            Text = Localizer.T("导出记录", "Export records");
            Font = new Font("Microsoft YaHei UI", 9F);
            Width = 520;
            Height = 465;
            MinimumSize = new Size(460, 435);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Padding = new Padding(28, 22, 28, 20);

            Label title = new Label { Text = Localizer.T("导出中心", "Export Center"), Left = 28, Top = 22, Width = 430, Height = 42, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Font = new Font(Font.FontFamily, 15F, FontStyle.Bold) };
            Label hint = new Label { Text = Localizer.T("选择记录范围、文件格式和组织方式。", "Choose the record scope, file format, and organization."), Left = 28, Top = 65, Width = 430, Height = 32, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, ForeColor = Color.DimGray };
            Controls.Add(hint); Controls.Add(title);

            TableLayoutPanel fields = new TableLayoutPanel { Left = 28, Top = 101, Width = 430, Height = 172, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, ColumnCount = 2, RowCount = 3, Padding = new Padding(0, 8, 0, 0) };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 3; i++) fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            scope = CreateCombo(); format = CreateCombo(); mode = CreateCombo();
            AddField(fields, 0, Localizer.T("导出范围", "Scope"), scope);
            AddField(fields, 1, Localizer.T("文件格式", "Format"), format);
            AddField(fields, 2, Localizer.T("组织方式", "Organization"), mode);
            Controls.Add(fields);

            if (hasCurrent) scope.Items.Add(new ExportChoice("current", Localizer.T("当前条目（1 条）", "Current item (1)")));
            if (selectedCount > 0) scope.Items.Add(new ExportChoice("selected", Localizer.T("已选条目（" + selectedCount + " 条）", "Selected items (" + selectedCount + ")")));
            scope.Items.Add(new ExportChoice("folder", Localizer.T("当前分类（" + folderCount + " 条）", "Current category (" + folderCount + ")")));
            scope.Items.Add(new ExportChoice("view", Localizer.T("当前搜索 / 筛选结果（" + viewCount + " 条）", "Current search / filter results (" + viewCount + ")")));
            format.Items.AddRange(new object[] { new ExportChoice("txt", "TXT"), new ExportChoice("md", "Markdown (.md)"), new ExportChoice("pdf", "PDF"), new ExportChoice("docx", "Word (.docx)"), new ExportChoice("csv", "CSV"), new ExportChoice("json", "JSON") });
            mode.Items.AddRange(new object[] { new ExportChoice("merged", Localizer.T("合并为一个文件", "Merge into one file")), new ExportChoice("separate", Localizer.T("每条记录一个文件，放入同一文件夹", "One file per record in one folder")) });
            SelectValue(scope, preferredScope); if (scope.SelectedIndex < 0 && scope.Items.Count > 0) scope.SelectedIndex = 0;
            format.SelectedIndex = 0; mode.SelectedIndex = 0;

            includeAnnotations = new CheckBox { Text = Localizer.T("包含文字批注", "Include text annotations"), Left = 146, Top = 282, Width = 280, Height = 28, Checked = true, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            includeAiExplanations = new CheckBox { Text = Localizer.T("包含已保存的 AI 解释", "Include saved AI explanations"), Left = 146, Top = 315, Width = 280, Height = 28, Checked = true, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            Controls.Add(includeAnnotations); Controls.Add(includeAiExplanations);

            FlowLayoutPanel bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 10, 0, 0) };
            Button cancel = new Button { Text = Localizer.T("取消", "Cancel"), Width = 92, Height = 36, DialogResult = DialogResult.Cancel, Margin = new Padding(10, 0, 0, 0) };
            Button ok = new Button { Text = Localizer.T("继续导出", "Continue"), Width = 108, Height = 36, Margin = Padding.Empty };
            ok.Click += delegate { Confirm(); };
            bottom.Controls.Add(ok); bottom.Controls.Add(cancel); Controls.Add(bottom);
            AcceptButton = ok; CancelButton = cancel;
            UiStyle.Prepare(this, AppData.LoadSettings().AppTheme);
        }

        private static ComboBox CreateCombo() { return new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 7, 0, 8) }; }
        private static void AddField(TableLayoutPanel panel, int row, string label, ComboBox combo)
        {
            panel.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(panel.Font, FontStyle.Bold) }, 0, row);
            panel.Controls.Add(combo, 1, row);
        }
        private static void SelectValue(ComboBox combo, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            for (int i = 0; i < combo.Items.Count; i++) { ExportChoice choice = combo.Items[i] as ExportChoice; if (choice != null && choice.Value == value) { combo.SelectedIndex = i; return; } }
        }
        private void Confirm()
        {
            ExportChoice scopeChoice = scope.SelectedItem as ExportChoice;
            ExportChoice formatChoice = format.SelectedItem as ExportChoice;
            ExportChoice modeChoice = mode.SelectedItem as ExportChoice;
            if (scopeChoice == null || formatChoice == null || modeChoice == null) return;
            Scope = scopeChoice.Value; Format = formatChoice.Value; Separate = modeChoice.Value == "separate";
            IncludeAnnotations = includeAnnotations.Checked; IncludeAiExplanations = includeAiExplanations.Checked;
            DialogResult = DialogResult.OK; Close();
        }
    }

    internal sealed class ExportChoice
    {
        public string Value { get; private set; }
        private readonly string label;
        public ExportChoice(string value, string text) { Value = value; label = text; }
        public override string ToString() { return label; }
    }
}
