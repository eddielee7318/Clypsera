using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace ClipboardTrail
{
    internal sealed class OperationProgressForm : Form
    {
        private readonly Label status;
        private readonly Label detail;
        private readonly ProgressBar progress;
        private readonly Timer timer;
        private readonly Stopwatch elapsed;
        private int ceiling;

        public OperationProgressForm(string title, string theme)
        {
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            ControlBox = false;
            ClientSize = new Size(430, 132);
            Padding = new Padding(22, 18, 22, 16);

            status = new Label { Dock = DockStyle.Top, Height = 30, Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            progress = new ProgressBar { Dock = DockStyle.Top, Height = 14, Minimum = 0, Maximum = 100, Style = ProgressBarStyle.Continuous, Margin = new Padding(0, 8, 0, 0) };
            detail = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft, ForeColor = Color.DimGray };
            Controls.Add(detail);
            Controls.Add(progress);
            Controls.Add(status);

            elapsed = Stopwatch.StartNew();
            timer = new Timer { Interval = 650 };
            timer.Tick += delegate
            {
                if (progress.Value < ceiling) progress.Value++;
                UpdateDetail();
            };
            timer.Start();
            FormClosed += delegate { timer.Stop(); timer.Dispose(); elapsed.Stop(); };
            UiStyle.Prepare(this, theme);
        }

        public void SetStage(int percent, int waitCeiling, string message)
        {
            int value = Math.Max(0, Math.Min(100, percent));
            ceiling = Math.Max(value, Math.Min(100, waitCeiling));
            progress.Value = Math.Max(progress.Value, value);
            status.Text = message ?? "";
            UpdateDetail();
            Refresh();
            Application.DoEvents();
        }

        private void UpdateDetail()
        {
            detail.Text = Localizer.T("阶段进度 ", "Stage progress ") + progress.Value + "%  ·  " +
                Localizer.T("已用时间 ", "Elapsed ") + elapsed.Elapsed.ToString(@"mm\:ss") +
                Localizer.T("（在线响应时间可能波动）", " (online response time may vary)");
        }
    }
}
