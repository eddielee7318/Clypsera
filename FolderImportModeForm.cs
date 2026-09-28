using System.Drawing;
using System.Windows.Forms;

namespace ClipboardTrail
{
    internal sealed class FolderImportModeForm : Form
    {
        public bool ImportAsLiveFolder { get; private set; }

        public FolderImportModeForm(string folderPath, string theme)
        {
            Text = Localizer.T("选择文件夹类型", "Choose folder type");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(560, 262);
            Padding = new Padding(22, 18, 22, 18);

            Label title = new Label { Text = Localizer.T("这个文件夹需要持续同步吗？", "Should this folder stay synchronized?"), Dock = DockStyle.Top, Height = 34, Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold) };
            Label path = new Label { Text = folderPath, Dock = DockStyle.Top, Height = 42, AutoEllipsis = true, ForeColor = Color.DimGray };
            Button live = new Button { Text = Localizer.T("🔄 活动文件夹（持续同步）", "🔄 Live folder (continuous sync)"), Dock = DockStyle.Top, Height = 48, FlatStyle = FlatStyle.Flat };
            Label liveHint = new Label { Text = Localizer.T("启动时和运行期间自动同步新增、修改与删除；源文件本身不会被软件删除。", "Automatically mirrors additions, changes, and deletions at startup and while running; source files are never deleted."), Dock = DockStyle.Top, Height = 40, ForeColor = Color.DimGray };
            Button snapshot = new Button { Text = Localizer.T("📁 普通文件夹（仅导入一次）", "📁 Regular folder (one-time import)"), Dock = DockStyle.Top, Height = 48, FlatStyle = FlatStyle.Flat };
            live.Click += delegate { ImportAsLiveFolder = true; DialogResult = DialogResult.OK; Close(); };
            snapshot.Click += delegate { ImportAsLiveFolder = false; DialogResult = DialogResult.OK; Close(); };
            Controls.Add(snapshot);
            Controls.Add(liveHint);
            Controls.Add(live);
            Controls.Add(path);
            Controls.Add(title);
            AcceptButton = live;
            UiStyle.Prepare(this, theme);
        }
    }
}
