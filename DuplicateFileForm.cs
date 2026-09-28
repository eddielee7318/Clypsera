using System;
using System.Drawing;
using System.Windows.Forms;

namespace ClipboardTrail
{
    internal enum DuplicateImportAction { Replace, Merge, Skip }

    internal sealed class DuplicateFileDecision
    {
        public DuplicateImportAction Action { get; set; }
        public bool ApplyToAll { get; set; }
    }

    internal sealed class DuplicateFileForm : Form
    {
        private readonly CheckBox applyAll;
        public DuplicateFileDecision Decision { get; private set; }

        public DuplicateFileForm(string fileName)
        {
            Text = Localizer.T("发现重复文件", "Duplicate file found");
            Icon = Branding.CreateIcon();
            Font = new Font("Microsoft YaHei UI", 9F);
            Width = 650; Height = 320; MinimumSize = new Size(580, 300);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;

            Label title = new Label { Text = Localizer.T("文件夹中已经有同名文件", "A file with the same name is already in this folder"), Left = 24, Top = 20, Width = 590, Height = 32, Font = new Font(Font, FontStyle.Bold) };
            Label file = new Label { Text = fileName ?? "", Left = 24, Top = 58, Width = 590, Height = 28, AutoEllipsis = true, ForeColor = Color.FromArgb(43, 104, 170) };
            Label explanation = new Label { Text = Localizer.T("替换：更新现有文件。\n合并：仍只保留一个资料条目，同时把旧文件保存到版本目录，再使用新文件。\n不添加：保留现有资料，不导入这个文件。", "Replace: update the existing file.\nMerge: keep one library item, save the old file in its versions folder, then use the new file.\nSkip: keep the existing item and do not import this file."), Left = 24, Top = 96, Width = 590, Height = 82 };
            applyAll = new CheckBox { Text = Localizer.T("对本次导入中后续的重复文件使用同一选择", "Use this choice for the remaining duplicates in this import"), Left = 24, Top = 183, Width = 580, Height = 28, AutoSize = false };
            Button replace = new Button { Text = Localizer.T("替换", "Replace"), Left = 244, Top = 224, Width = 112, Height = 38 };
            Button merge = new Button { Text = Localizer.T("合并", "Merge"), Left = 365, Top = 224, Width = 112, Height = 38 };
            Button skip = new Button { Text = Localizer.T("不添加", "Skip"), Left = 486, Top = 224, Width = 112, Height = 38 };
            replace.Click += delegate { Finish(DuplicateImportAction.Replace); };
            merge.Click += delegate { Finish(DuplicateImportAction.Merge); };
            skip.Click += delegate { Finish(DuplicateImportAction.Skip); };
            Controls.Add(title); Controls.Add(file); Controls.Add(explanation); Controls.Add(applyAll); Controls.Add(replace); Controls.Add(merge); Controls.Add(skip);
            CancelButton = skip;
            UiStyle.Prepare(this, AppData.LoadSettings().AppTheme);
            Localizer.Apply(this);
        }

        private void Finish(DuplicateImportAction action)
        {
            Decision = new DuplicateFileDecision { Action = action, ApplyToAll = applyAll.Checked };
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
