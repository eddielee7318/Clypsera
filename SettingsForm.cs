using System;
using System.Drawing;
using System.Windows.Forms;

namespace ClipboardTrail
{
    internal sealed class SettingsForm : Form
    {
        private readonly CheckBox deepSeekEnabled;
        private readonly TextBox endpoint;
        private readonly TextBox model;
        private readonly TextBox apiKey;
        private readonly CheckBox localApi;
        private readonly NumericUpDown port;
        private readonly NumericUpDown maxLength;
        private readonly CheckBox startWindows;
        private readonly CheckBox overlayVisible;
        private readonly ComboBox language;
        private readonly ComboBox appTheme;
        private readonly ComboBox fontSize;
        private readonly CheckBox whitelistEnabled;
        private readonly TextBox whitelist;
        private readonly TextBox blacklist;
        private readonly TextBox defaultExportFolder;
        private readonly SettingsData original;

        public SettingsData Result { get; private set; }

        public SettingsForm(SettingsData settings)
        {
            original = settings;
            Text = "Clypsera 设置";
            Font = new Font("Microsoft YaHei UI", 9F);
            Width = 920;
            Height = 1040;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            int y = 22;
            Controls.Add(Section("外观与语言", y)); y += 44;
            Controls.Add(new Label { Text = "界面语言", Left = 28, Top = y + 5, Width = 156 });
            language = new ComboBox { Left = 190, Top = y, Width = 190, DropDownStyle = ComboBoxStyle.DropDownList };
            language.Items.AddRange(new object[] { "中文（简体）", "English" });
            language.SelectedIndex = settings.Language == "en-US" ? 1 : 0;
            Controls.Add(language);
            Controls.Add(new Label { Text = "软件外观", Left = 455, Top = y + 5, Width = 156 });
            appTheme = new ComboBox { Left = 620, Top = y, Width = 220, DropDownStyle = ComboBoxStyle.DropDownList };
            appTheme.Items.AddRange(new object[] { "日间模式", "夜间模式" });
            appTheme.SelectedIndex = settings.AppTheme == "dark" ? 1 : 0;
            Controls.Add(appTheme); y += 48;
            Controls.Add(new Label { Text = Localizer.T("界面字号", "Interface text size"), Left = 28, Top = y + 5, Width = 156 });
            fontSize = new ComboBox { Left = 190, Top = y, Width = 190, DropDownStyle = ComboBoxStyle.DropDownList };
            fontSize.Items.AddRange(new object[] { Localizer.T("小", "Small"), Localizer.T("中（默认）", "Medium (default)"), Localizer.T("大", "Large") });
            fontSize.SelectedIndex = settings.UiFontSize == "small" ? 0 : settings.UiFontSize == "large" ? 2 : 1;
            Controls.Add(fontSize); y += 56;

            Controls.Add(Section("DeepSeek 自动分类（会把最多 4000 字发送给所填接口）", y)); y += 44;
            deepSeekEnabled = new CheckBox { Text = "启用 DeepSeek 自动分类", Left = 28, Top = y, AutoSize = true, Checked = settings.DeepSeekEnabled }; Controls.Add(deepSeekEnabled); y += 40;
            endpoint = AddField("接口地址", settings.DeepSeekEndpoint, y); y += 48;
            model = AddField("模型", settings.DeepSeekModel, y); y += 48;
            apiKey = AddField("API Key", AppData.UnprotectSecret(settings.ApiKeyEncrypted), y); apiKey.UseSystemPasswordChar = true; y += 58;

            Controls.Add(Section("本机集成接口（只监听 127.0.0.1）", y)); y += 44;
            localApi = new CheckBox { Text = "启用只读本地 API", Left = 28, Top = y, AutoSize = true, Checked = settings.EnableLocalApi }; Controls.Add(localApi);
            Label portLabel = new Label { Text = "端口", Left = 580, Top = y + 3, Width = 70 };
            port = new NumericUpDown { Left = 655, Top = y, Width = 120, Minimum = 1024, Maximum = 65535, Value = settings.LocalApiPort };
            Controls.Add(portLabel); Controls.Add(port); y += 52;

            Controls.Add(Section("采集", y)); y += 44;
            Label maxLabel = new Label { Text = "每条最多保存字符数", Left = 28, Top = y + 3, Width = 290 };
            maxLength = new NumericUpDown { Left = 325, Top = y, Width = 125, Minimum = 100, Maximum = 1000000, Increment = 1000, Value = Math.Max(100, Math.Min(1000000, settings.MaxTextLength)) };
            startWindows = new CheckBox { Text = "随 Windows 启动", Left = 540, Top = y, AutoSize = true, Checked = settings.StartWithWindows };
            Controls.Add(maxLabel); Controls.Add(maxLength); Controls.Add(startWindows); y += 38;
            overlayVisible = new CheckBox { Text = Localizer.T("复制时显示悬浮提示（关闭后不会被下一次复制重新唤起）", "Show the floating notice when copying (it stays off after closing)"), Left = 28, Top = y, Width = 820, Height = 34, AutoSize = false, Checked = settings.OverlayVisible };
            Controls.Add(overlayVisible); y += 44;
            whitelistEnabled = new CheckBox { Text = Localizer.T("启用程序白名单（只记录下列程序）", "Enable app allowlist (capture only these apps)"), Left = 28, Top = y, Width = 820, Height = 34, AutoSize = false, Checked = settings.CaptureWhitelistEnabled };
            Controls.Add(whitelistEnabled); y += 44;
            whitelist = AddField(Localizer.T("白名单程序", "Allowed apps"), settings.CaptureWhitelist, y); y += 46;
            whitelist.Enabled = whitelistEnabled.Checked;
            whitelistEnabled.CheckedChanged += delegate { whitelist.Enabled = whitelistEnabled.Checked; };
            blacklist = AddField(Localizer.T("黑名单程序", "Blocked apps"), settings.CaptureBlacklist, y); y += 38;
            Controls.Add(new Label { Text = Localizer.T("进程名用逗号分隔，如 chrome 或 chrome.exe；黑名单优先。", "Comma-separated process names, e.g. chrome or chrome.exe. Blocklist wins."), Left = 38, Top = y, Width = 820, Height = 60, ForeColor = Color.DimGray });
            y += 62;

            Controls.Add(Section(Localizer.T("默认导出位置", "Default export location"), y)); y += 40;
            Controls.Add(new Label { Text = Localizer.T("默认导出文件夹", "Default export folder"), Left = 28, Top = y + 5, Width = 172 });
            defaultExportFolder = new TextBox { Left = 205, Top = y, Width = 430, Text = settings.DefaultExportFolder ?? "", ReadOnly = true };
            Button chooseExportFolder = new Button { Text = Localizer.T("选择文件夹…", "Choose folder…"), Left = 645, Top = y - 2, Width = 145, Height = 32 };
            Button clearExportFolder = new Button { Text = Localizer.T("清除", "Clear"), Left = 800, Top = y - 2, Width = 70, Height = 32 };
            chooseExportFolder.Click += delegate
            {
                using (FolderBrowserDialog dialog = new FolderBrowserDialog { Description = Localizer.T("选择以后默认打开的导出文件夹", "Choose the folder exports should open by default"), SelectedPath = DirectoryExists(defaultExportFolder.Text) ? defaultExportFolder.Text : "" })
                    if (dialog.ShowDialog(this) == DialogResult.OK) defaultExportFolder.Text = dialog.SelectedPath;
            };
            clearExportFolder.Click += delegate { defaultExportFolder.Clear(); };
            Controls.Add(defaultExportFolder); Controls.Add(chooseExportFolder); Controls.Add(clearExportFolder);

            Button cancel = new Button { Text = "取消", Width = 98, Height = 36, Left = 672, Top = 925, DialogResult = DialogResult.Cancel };
            Button save = new Button { Text = "保存", Width = 98, Height = 36, Left = 780, Top = 925 };
            save.Click += SaveClicked;
            Controls.Add(cancel); Controls.Add(save);
            CancelButton = cancel;
            AcceptButton = save;
            UiStyle.Prepare(this, settings.AppTheme);
            Localizer.Apply(this);
        }

        private Label Section(string text, int top)
        {
            return new Label { Text = text, Left = 20, Top = top, Width = 840, Height = 30, Font = new Font(Font, FontStyle.Bold), ForeColor = Color.FromArgb(32, 99, 170) };
        }

        private TextBox AddField(string label, string value, int top)
        {
            Controls.Add(new Label { Text = label, Left = 28, Top = top + 4, Width = 156 });
            TextBox box = new TextBox { Left = 190, Top = top, Width = 650, Text = value ?? "" };
            Controls.Add(box);
            return box;
        }

        private static bool DirectoryExists(string path)
        {
            try { return !string.IsNullOrWhiteSpace(path) && System.IO.Directory.Exists(path); }
            catch { return false; }
        }

        private void SaveClicked(object sender, EventArgs e)
        {
            if (whitelistEnabled.Checked && CapturePolicy.Parse(whitelist.Text).Count == 0)
            {
                MessageBox.Show(Localizer.T("开启白名单后，请至少填写一个程序进程名。", "Enter at least one process name when the allowlist is enabled."), "Clypsera", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Uri uri;
            if (deepSeekEnabled.Checked && (!Uri.TryCreate(endpoint.Text.Trim(), UriKind.Absolute, out uri) || uri.Scheme != "https"))
            {
                MessageBox.Show(Localizer.T("启用 DeepSeek 时，接口地址必须是 HTTPS 地址。", "When DeepSeek is enabled, the endpoint must use HTTPS."), "Clypsera", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Result = new SettingsData
            {
                OverlayVisible = overlayVisible.Checked,
                DeepSeekEnabled = deepSeekEnabled.Checked,
                DeepSeekEndpoint = endpoint.Text.Trim(),
                DeepSeekModel = model.Text.Trim(),
                ApiKeyEncrypted = AppData.ProtectSecret(apiKey.Text.Trim()),
                EnableLocalApi = localApi.Checked,
                LocalApiPort = (int)port.Value,
                MaxTextLength = (int)maxLength.Value,
                StartWithWindows = startWindows.Checked,
                Language = language.SelectedIndex == 1 ? "en-US" : "zh-CN",
                AppTheme = appTheme.SelectedIndex == 1 ? "dark" : "light",
                ReaderTheme = original.ReaderTheme,
                UiFontSize = fontSize.SelectedIndex == 0 ? "small" : fontSize.SelectedIndex == 2 ? "large" : "medium",
                CaptureWhitelistEnabled = whitelistEnabled.Checked,
                CaptureWhitelist = whitelist.Text.Trim(),
                CaptureBlacklist = blacklist.Text.Trim(),
                DefaultExportFolder = defaultExportFolder.Text.Trim()
            };
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
