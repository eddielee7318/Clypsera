using System;
using System.Drawing;
using System.Windows.Forms;

namespace ClipboardTrail
{
    internal sealed class OverlayForm : Form
    {
        private readonly Label meta;
        private readonly Label preview;
        private Point dragStart;
        public event EventHandler OpenRequested;
        public event EventHandler HideRequested;

        public OverlayForm()
        {
            Text = "Clypsera 悬浮窗";
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Color.FromArgb(35, 39, 47);
            ForeColor = Color.White;
            Opacity = 0.92;
            Width = 360;
            Height = 108;
            StartPosition = FormStartPosition.Manual;
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Right - Width - 24, area.Bottom - Height - 24);

            Panel stripe = new Panel { Dock = DockStyle.Left, Width = 5, BackColor = Color.FromArgb(65, 145, 245) };
            meta = new Label { Left = 18, Top = 12, Width = 325, Height = 22, Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold), ForeColor = Color.FromArgb(142, 202, 255) };
            preview = new Label { Left = 18, Top = 38, Width = 325, Height = 56, Font = new Font("Microsoft YaHei UI", 9F), AutoEllipsis = true };
            Label close = new Label { Text = "×", Left = 335, Top = 5, Width = 20, Height = 20, TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand, ForeColor = Color.Silver };
            close.Click += delegate
            {
                Hide();
                if (HideRequested != null) HideRequested(this, EventArgs.Empty);
            };
            Controls.Add(stripe);
            Controls.Add(meta);
            Controls.Add(preview);
            Controls.Add(close);
            foreach (Control control in new Control[] { this, meta, preview })
            {
                control.MouseDown += BeginDrag;
                control.MouseMove += ContinueDrag;
            }
            DoubleClick += RequestOpen;
            preview.DoubleClick += RequestOpen;
            UpdateItem(null, false);
            SettingsData appearance = AppData.LoadSettings();
            UiStyle.Prepare(this, appearance.AppTheme);
            Localizer.Apply(this);
        }

        private void RequestOpen(object sender, EventArgs e)
        {
            if (OpenRequested != null) OpenRequested(this, EventArgs.Empty);
        }

        public void UpdateItem(ClipboardItem item, bool paused)
        {
            if (paused)
            {
                meta.Text = Localizer.T("采集已暂停", "Capture paused");
                preview.Text = Localizer.T("右键托盘图标可继续采集", "Right-click the tray icon to resume");
                return;
            }
            if (item == null)
            {
                meta.Text = Localizer.T("Clypsera 正在等待复制内容", "Clypsera is waiting for copied text");
                preview.Text = Localizer.T("复制一段文字后，这里会显示来源、时间和预览。", "Copy some text to see its source, time, and preview here.");
                return;
            }
            meta.Text = item.CapturedAt.ToString("HH:mm:ss") + "  ·  " + item.SourceProcess + "  ·  " + item.Category;
            preview.Text = (item.Text ?? "").Replace("\r", " ").Replace("\n", " ");
        }

        private void BeginDrag(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) dragStart = e.Location;
        }

        private void ContinueDrag(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                Location = new Point(Location.X + e.X - dragStart.X, Location.Y + e.Y - dragStart.Y);
        }
    }
}
