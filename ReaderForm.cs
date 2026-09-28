using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ClipboardTrail
{
    internal sealed class ReaderForm : Form
    {
        private readonly LibraryNode node;
        private readonly SettingsData settings;
        private readonly HistoryStore historyStore;
        private readonly AnnotationStore annotationStore;
        private readonly WebView2 web;
        private readonly CheckedListBox annotations;
        private readonly Label status;
        private readonly NumericUpDown page;
        private readonly ToolStripDropDownButton readerThemeMenu;
        private TableLayoutPanel readerShell;
        private Panel pdfZoomPanel;
        private TrackBar pdfZoom;
        private Label pdfZoomValue;
        private string readablePath;
        private string navigationPath;
        private bool isPdf;
        private string pendingContextSelection;
        private CoreWebView2ContextMenuItem contextHighlight;
        private CoreWebView2ContextMenuItem contextUnderline;
        private CoreWebView2ContextMenuItem contextNote;
        private CoreWebView2ContextMenuItem contextExplain;
        private CoreWebView2ContextMenuItem contextDelete;
        private CoreWebView2ContextMenuItem contextSeparator;
        private readonly DocumentAnnotation initialAnnotation;
        private readonly List<CoreWebView2Frame> pdfFrames = new List<CoreWebView2Frame>();
        private Timer pdfPageTimer;
        private bool pdfPageReadBusy;
        private CheckBox selectAllAnnotations;
        private bool updatingAnnotationChecks;
        private bool readerNavigationReady;
        private bool initialNavigationStarted;

        public event EventHandler AnnotationChanged;
        public string NodeId { get { return node.Id; } }
        internal DocumentAnnotation NavigationTarget { get { return initialAnnotation; } }
        internal bool LastAnnotationJumpSucceeded { get; private set; }
        internal bool AnnotationJumpAttempted { get; private set; }
        internal bool InitialNavigationScheduled { get; private set; }

        public ReaderForm(LibraryNode node) : this(node, new HistoryStore()) { }

        public ReaderForm(LibraryNode node, HistoryStore historyStore) : this(node, historyStore, null) { }

        public ReaderForm(LibraryNode node, HistoryStore historyStore, DocumentAnnotation initialAnnotation)
        {
            this.node = node;
            this.historyStore = historyStore ?? new HistoryStore();
            this.initialAnnotation = initialAnnotation;
            settings = AppData.LoadSettings();
            annotationStore = new AnnotationStore();
            Text = "阅读 · " + node.Name;
            Icon = Branding.CreateIcon();
            Font = new Font("Microsoft YaHei UI", 9F);
            Width = 1280;
            Height = 820;
            MinimumSize = new Size(850, 580);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;

            ToolStrip tools = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(8, 7, 8, 7), AutoSize = false, Height = 52 };
            tools.Items.Add(CreateIconButton("🖍", Localizer.T("荧光笔", "Highlight"), async delegate { await AddAnnotationAsync("highlight"); }));
            tools.Items.Add(CreateIconButton("U̲", Localizer.T("下划线", "Underline"), async delegate { await AddAnnotationAsync("underline"); }));
            tools.Items.Add(CreateIconButton("📝", Localizer.T("批注", "Annotation"), async delegate { await AddAnnotationAsync("note"); }));
            tools.Items.Add(new ToolStripSeparator());
            readerThemeMenu = new ToolStripDropDownButton("🎨") { ToolTipText = Localizer.T("阅读主题", "Reading theme"), Font = new Font("Segoe UI Emoji", 11F), ShowDropDownArrow = false };
            readerThemeMenu.DropDownItems.Add(CreateReaderThemeItem(Localizer.T("🌿 护眼绿", "🌿 Eye-comfort green"), "eye"));
            readerThemeMenu.DropDownItems.Add(CreateReaderThemeItem(Localizer.T("📜 羊皮纸", "📜 Parchment"), "parchment"));
            readerThemeMenu.DropDownItems.Add(CreateReaderThemeItem(Localizer.T("🌙 黑色", "🌙 Dark"), "dark"));
            readerThemeMenu.DropDownItems.Add(CreateReaderThemeItem(Localizer.T("☀ 白色", "☀ White"), "white"));
            tools.Items.Add(readerThemeMenu);
            ToolStripDropDownButton moreTools = new ToolStripDropDownButton("⋯") { ToolTipText = Localizer.T("更多工具", "More tools"), Font = new Font("Segoe UI Symbol", 12F), ShowDropDownArrow = false };
            moreTools.DropDownItems.Add(Localizer.T("✨ 请解释", "✨ Explain with AI"), null, async delegate { await ExplainSelectionAsync(); });
            tools.Items.Add(moreTools);
            tools.Items.Add(CreateIconButton("↻", Localizer.T("重新载入", "Reload"), delegate { if (web.CoreWebView2 != null) web.Reload(); }));
            tools.Items.Add(CreateIconButton("📁", Localizer.T("打开归档目录", "Open archive folder"), delegate { try { System.Diagnostics.Process.Start(Path.GetDirectoryName(node.FilePath)); } catch { } }));
            tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(new ToolStripLabel(node.FileFormat + "  ·  " + node.Name) { Font = new Font(Font, FontStyle.Bold), AutoSize = false, Width = 430, TextAlign = ContentAlignment.MiddleLeft, ToolTipText = node.Name });
            UpdateReaderThemeMenu();

            SplitContainer split = new SplitContainer { Width = ClientSize.Width, Height = ClientSize.Height - 48, Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = Math.Max(500, ClientSize.Width - 350), Panel1MinSize = 500, Panel2MinSize = 320, FixedPanel = FixedPanel.Panel2 };
            web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.White };
            web.SizeChanged += async delegate { await ResizePdfViewportAsync(); };
            split.Panel1.Controls.Add(web);

            Panel side = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(247, 249, 252) };
            Panel sideHead = new Panel { Dock = DockStyle.Fill };
            Label sideTitle = new Label { Text = Localizer.T("批注与摘录", "Annotations and excerpts"), Left = 12, Top = 10, Width = 260, Height = 28, Font = new Font(Font, FontStyle.Bold), AutoEllipsis = false };
            Label pageLabel = new Label { Text = "当前页", Left = 12, Top = 44, Width = 55 };
            page = new NumericUpDown { Left = 70, Top = 40, Width = 74, Minimum = 0, Maximum = 100000 };
            selectAllAnnotations = new CheckBox { Text = Localizer.T("全选批注", "Select all"), Left = 12, Top = 72, Width = 160, Height = 28, AutoSize = false };
            sideHead.Controls.Add(sideTitle); sideHead.Controls.Add(pageLabel); sideHead.Controls.Add(page); sideHead.Controls.Add(selectAllAnnotations);
            annotations = new CheckedListBox { Dock = DockStyle.Fill, HorizontalScrollbar = false, CheckOnClick = true, IntegralHeight = false };
            ContextMenuStrip annotationMenu = new ContextMenuStrip();
            annotationMenu.Items.Add(Localizer.T("↪ 请跳转到批注位置", "↪ Jump to annotation"), null, async delegate { await JumpToSelectedAnnotationAsync(true); });
            annotationMenu.Items.Add(new ToolStripSeparator());
            annotationMenu.Items.Add(Localizer.T("🔎 查看详情", "🔎 View details"), null, delegate { ShowSelectedAnnotation(); });
            annotationMenu.Items.Add(Localizer.T("📋 复制摘录", "📋 Copy excerpt"), null, delegate { CopySelectedAnnotation(); });
            annotationMenu.Items.Add(new ToolStripSeparator());
            annotationMenu.Items.Add(Localizer.T("🗑 删除批注", "🗑 Delete annotation"), null, async delegate { await DeleteSelectedAnnotationAsync(); });
            annotations.ContextMenuStrip = annotationMenu;
            annotations.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Right) return;
                int index = annotations.IndexFromPoint(e.Location);
                if (index >= 0) annotations.SelectedIndex = index;
            };
            Panel sideFoot = new Panel { Dock = DockStyle.Fill };
            Button delete = new Button { Text = Localizer.T("🗑 删除", "🗑 Delete"), Left = 10, Top = 8, Width = 112, Height = 36 };
            Button export = new Button { Text = Localizer.T("⇩ 导出", "⇩ Export"), Left = 132, Top = 8, Width = 112, Height = 36 };
            status = new Label { Left = 10, Top = 50, Width = 310, Height = 46, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, ForeColor = Color.DimGray, AutoEllipsis = true };
            sideFoot.Controls.Add(delete); sideFoot.Controls.Add(export); sideFoot.Controls.Add(status);
            TableLayoutPanel sideLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
            sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 108)); sideLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));
            sideLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            sideLayout.Controls.Add(sideHead, 0, 0); sideLayout.Controls.Add(annotations, 0, 1); sideLayout.Controls.Add(sideFoot, 0, 2); side.Controls.Add(sideLayout);
            split.Panel2.Controls.Add(side);

            pdfZoomPanel = BuildPdfZoomPanel();
            readerShell = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
            readerShell.RowStyles.Add(new RowStyle(SizeType.Absolute, 56)); readerShell.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); readerShell.RowStyles.Add(new RowStyle(SizeType.Absolute, 0)); readerShell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tools.Dock = DockStyle.Fill; readerShell.Controls.Add(tools, 0, 0); readerShell.Controls.Add(split, 0, 1); readerShell.Controls.Add(pdfZoomPanel, 0, 2); Controls.Add(readerShell);

            delete.Click += async delegate { await DeleteSelectedAnnotationAsync(); };
            export.Click += delegate { ExportSelectedAnnotations(); };
            annotations.DoubleClick += delegate { ShowSelectedAnnotation(); };
            annotations.ItemCheck += delegate { BeginInvoke((MethodInvoker)UpdateAnnotationSelectAll); };
            selectAllAnnotations.CheckedChanged += delegate { if (!updatingAnnotationChecks) SetAllAnnotationChecks(selectAllAnnotations.Checked); };
            KeyDown += ReaderFormKeyDown;
            Shown += async delegate { await InitializeReaderAsync(); };
            RefreshAnnotations();
            UiStyle.Prepare(this, settings.AppTheme);
            Localizer.Apply(this);
        }

        private static ToolStripButton CreateIconButton(string icon, string tooltip, EventHandler action)
        {
            ToolStripButton button = new ToolStripButton(icon) { ToolTipText = tooltip, DisplayStyle = ToolStripItemDisplayStyle.Text, Font = new Font("Segoe UI Emoji", 11F), Padding = new Padding(3, 0, 3, 0) };
            button.Click += action;
            return button;
        }

        private Panel BuildPdfZoomPanel()
        {
            Panel panel = new Panel { Dock = DockStyle.Fill, Visible = false, Padding = new Padding(12, 6, 12, 6) };
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 1, ColumnCount = 6, Margin = Padding.Empty, Padding = Padding.Empty };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
            Label caption = new Label { Text = Localizer.T("缩放", "Zoom"), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font, FontStyle.Bold) };
            Button minus = new Button { Text = "−", Dock = DockStyle.Fill, Margin = new Padding(2, 0, 4, 0), FlatStyle = FlatStyle.Flat };
            Button plus = new Button { Text = "+", Dock = DockStyle.Fill, Margin = new Padding(4, 0, 2, 0), FlatStyle = FlatStyle.Flat };
            Button reset = new Button { Text = "100%", Dock = DockStyle.Fill, Margin = new Padding(5, 0, 5, 0), FlatStyle = FlatStyle.Flat };
            pdfZoom = new TrackBar { Dock = DockStyle.Fill, Minimum = 25, Maximum = 300, Value = 100, TickStyle = TickStyle.None, SmallChange = 1, LargeChange = 10, AutoSize = false, Height = 34, Margin = new Padding(4, 0, 4, 0) };
            pdfZoomValue = new Label { Text = "100%", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight };
            layout.Controls.Add(caption, 0, 0); layout.Controls.Add(minus, 1, 0); layout.Controls.Add(pdfZoom, 2, 0); layout.Controls.Add(reset, 3, 0); layout.Controls.Add(plus, 4, 0); layout.Controls.Add(pdfZoomValue, 5, 0);
            minus.Click += delegate { pdfZoom.Value = Math.Max(pdfZoom.Minimum, pdfZoom.Value - 10); };
            plus.Click += delegate { pdfZoom.Value = Math.Min(pdfZoom.Maximum, pdfZoom.Value + 10); };
            reset.Click += delegate { pdfZoom.Value = 100; };
            pdfZoom.ValueChanged += delegate { ApplyPdfZoom(); };
            ToolTip tip = new ToolTip(); tip.SetToolTip(pdfZoom, Localizer.T("连续拖动调整阅读大小（25%–300%）", "Drag continuously to zoom the document (25%–300%)"));
            panel.Controls.Add(layout); return panel;
        }

        private ToolStripMenuItem CreateReaderThemeItem(string text, string theme)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text) { Tag = theme };
            item.Click += async delegate { await ChangeReaderThemeAsync(theme); };
            return item;
        }

        private void UpdateReaderThemeMenu()
        {
            foreach (ToolStripItem value in readerThemeMenu.DropDownItems)
            {
                ToolStripMenuItem item = value as ToolStripMenuItem;
                if (item != null) item.Checked = Convert.ToString(item.Tag) == SelectedReaderTheme();
            }
        }

        private async Task InitializeReaderAsync()
        {
            try
            {
                status.Text = "正在准备文档…";
                readablePath = DocumentConverter.PrepareReadablePath(node, out isPdf);
                navigationPath = isPdf ? PreparePdfThemeHost(readablePath) : readablePath;
                if (isPdf && page.Value < 1) page.Value = 1;
                pdfZoomPanel.Visible = true;
                readerShell.RowStyles[2].Height = 52;
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(AppData.DirectoryPath, "webview2-cache"));
                await web.EnsureCoreWebView2Async(environment);
                web.CoreWebView2.Settings.AreDevToolsEnabled = false;
                web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                InitializeSelectionContextMenu(environment);
                web.CoreWebView2.FrameCreated += delegate(object sender, CoreWebView2FrameCreatedEventArgs args) { TrackPdfFrame(args.Frame); };
                web.NavigationCompleted += async delegate(object sender, CoreWebView2NavigationCompletedEventArgs args)
                {
                    readerNavigationReady = args.IsSuccess;
                    await ApplyReaderThemeAsync();
                    await ResizePdfViewportAsync();
                    ApplyPdfZoom();
                };
                readerNavigationReady = false;
                web.Source = new Uri(navigationPath);
                ApplyPdfZoom();
                if (isPdf) StartPdfPageTracking();
                status.Text = initialAnnotation == null ? (isPdf ? Localizer.T("PDF 已归档；软件会自动记录批注页码。", "PDF archived; annotation pages are recorded automatically.") : Localizer.T("文档已归档，可选择文字批注。", "Document archived. Select text to annotate.")) : Localizer.T("正在打开原资料并定位批注…", "Opening the source and locating the annotation…");
                if (initialAnnotation != null) await NavigateToInitialAnnotationAsync();
            }
            catch (Exception ex)
            {
                status.Text = "打开失败";
                MessageBox.Show(ex.Message, "无法打开文档", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task NavigateToInitialAnnotationAsync()
        {
            if (initialAnnotation == null || initialNavigationStarted) return;
            initialNavigationStarted = true;
            InitialNavigationScheduled = true;
            if (!await WaitForReaderReadyAsync())
            {
                status.Text = Localizer.T("原资料已打开，但阅读页面尚未完成载入。可在右侧批注上右键重试跳转。", "The source opened, but the reading page is not ready. Right-click the annotation to retry.");
                return;
            }
            status.Text = Localizer.T("原资料已打开，正在定位批注…", "Source opened. Locating the annotation…");
            // The PDF host completes before its built-in document frame. Waiting here
            // prevents the saved location from being sent while the cover is still loading.
            if (isPdf) await Task.Delay(900);
            await JumpToAnnotationAsync(initialAnnotation);
        }

        private void TrackPdfFrame(CoreWebView2Frame frame)
        {
            if (!isPdf || frame == null) return;
            pdfFrames.Add(frame);
            frame.FrameCreated += delegate(object sender, CoreWebView2FrameCreatedEventArgs args) { TrackPdfFrame(args.Frame); };
            frame.Destroyed += delegate { pdfFrames.Remove(frame); };
            frame.NavigationCompleted += async delegate { await RefreshPdfPageAsync(); };
        }

        private void StartPdfPageTracking()
        {
            if (!isPdf) return;
            if (pdfPageTimer != null) { pdfPageTimer.Stop(); pdfPageTimer.Dispose(); }
            pdfPageTimer = new Timer { Interval = 600 };
            pdfPageTimer.Tick += async delegate { await RefreshPdfPageAsync(); };
            pdfPageTimer.Start();
            FormClosed += delegate { if (pdfPageTimer != null) { pdfPageTimer.Stop(); pdfPageTimer.Dispose(); pdfPageTimer = null; } };
        }

        private async Task<int> RefreshPdfPageAsync()
        {
            if (!isPdf || web.CoreWebView2 == null || pdfPageReadBusy) return (int)page.Value;
            pdfPageReadBusy = true;
            try
            {
                string script = "(function(){function number(v){var m=String(v||'').match(/(?:page=)?(\\d+)/i);return m?parseInt(m[1],10):0;}var h=location.hash||'';var hp=h.match(/[?#&]page=(\\d+)/i);if(hp)return parseInt(hp[1],10);var roots=[document],seen=[],inputs=[];for(var r=0;r<roots.length;r++){var root=roots[r];if(seen.indexOf(root)>=0)continue;seen.push(root);try{var all=root.querySelectorAll('*');for(var i=0;i<all.length;i++){if(all[i].shadowRoot)roots.push(all[i].shadowRoot);if(all[i].tagName==='INPUT')inputs.push(all[i]);}}catch(e){}}for(var j=0;j<inputs.length;j++){var x=inputs[j],hint=((x.id||'')+' '+(x.name||'')+' '+(x.className||'')+' '+(x.getAttribute('aria-label')||'')+' '+(x.getAttribute('title')||'')).toLowerCase(),v=number(x.value);if(v>0&&(hint.indexOf('page')>=0||hint.indexOf('页')>=0||number(x.max)>1))return v;}return 0;})()";
                List<CoreWebView2Frame> frames = pdfFrames.ToList();
                foreach (CoreWebView2Frame frame in frames)
                {
                    try
                    {
                        if (frame.IsDestroyed() != 0) { pdfFrames.Remove(frame); continue; }
                        string result = await frame.ExecuteScriptAsync(script);
                        int detected;
                        if (int.TryParse(result, out detected) && detected > 0)
                        {
                            page.Value = Math.Min(page.Maximum, detected);
                            return detected;
                        }
                    }
                    catch { }
                }
                try
                {
                    string result = await web.ExecuteScriptAsync(script);
                    int detected;
                    if (int.TryParse(result, out detected) && detected > 0) { page.Value = Math.Min(page.Maximum, detected); return detected; }
                }
                catch { }
                return (int)page.Value;
            }
            finally { pdfPageReadBusy = false; }
        }

        private void InitializeSelectionContextMenu(CoreWebView2Environment environment)
        {
            contextHighlight = environment.CreateContextMenuItem(Localizer.T("🖍 荧光笔", "🖍 Highlight"), null, CoreWebView2ContextMenuItemKind.Command);
            contextUnderline = environment.CreateContextMenuItem(Localizer.T("U̲ 下划线", "U̲ Underline"), null, CoreWebView2ContextMenuItemKind.Command);
            contextNote = environment.CreateContextMenuItem(Localizer.T("📝 添加批注", "📝 Add annotation"), null, CoreWebView2ContextMenuItemKind.Command);
            contextExplain = environment.CreateContextMenuItem(Localizer.T("✨ 请解释", "✨ Explain with AI"), null, CoreWebView2ContextMenuItemKind.Command);
            contextDelete = environment.CreateContextMenuItem(Localizer.T("🗑 清除标记并删除批注", "🗑 Clear mark and delete annotation"), null, CoreWebView2ContextMenuItemKind.Command);
            contextSeparator = environment.CreateContextMenuItem("", null, CoreWebView2ContextMenuItemKind.Separator);
            contextHighlight.CustomItemSelected += async delegate { await AddAnnotationAsync("highlight", pendingContextSelection); };
            contextUnderline.CustomItemSelected += async delegate { await AddAnnotationAsync("underline", pendingContextSelection); };
            contextNote.CustomItemSelected += async delegate { await AddAnnotationAsync("note", pendingContextSelection); };
            contextExplain.CustomItemSelected += async delegate { await ExplainSelectionAsync(pendingContextSelection); };
            contextDelete.CustomItemSelected += async delegate { await DeleteSelectedAnnotationAsync(pendingContextSelection); };
            web.CoreWebView2.ContextMenuRequested += delegate(object sender, CoreWebView2ContextMenuRequestedEventArgs args)
            {
                if (!args.ContextMenuTarget.HasSelection) return;
                pendingContextSelection = args.ContextMenuTarget.SelectionText;
                contextDelete.IsEnabled = !isPdf || annotations.SelectedItem != null;
                args.MenuItems.Add(contextSeparator);
                args.MenuItems.Add(contextHighlight);
                args.MenuItems.Add(contextUnderline);
                args.MenuItems.Add(contextNote);
                args.MenuItems.Add(contextExplain);
                args.MenuItems.Add(contextDelete);
            };
        }

        private string SelectedReaderTheme()
        {
            string theme = settings.ReaderTheme;
            return theme == "parchment" || theme == "dark" || theme == "white" ? theme : "eye";
        }

        private async Task ChangeReaderThemeAsync(string theme)
        {
            settings.ReaderTheme = theme;
            AppData.SaveSettings(settings);
            UpdateReaderThemeMenu();
            await ApplyReaderThemeAsync();
        }

        private async Task ApplyReaderThemeAsync()
        {
            string theme = SelectedReaderTheme();
            Color background = theme == "eye" ? Color.FromArgb(199, 237, 204) : theme == "parchment" ? Color.FromArgb(244, 236, 216) : theme == "dark" ? Color.FromArgb(22, 25, 31) : Color.White;
            Color foreground = theme == "eye" ? Color.FromArgb(31, 54, 39) : theme == "parchment" ? Color.FromArgb(62, 52, 40) : theme == "dark" ? Color.FromArgb(228, 232, 238) : Color.FromArgb(35, 39, 47);
            web.DefaultBackgroundColor = background;
            if (web.CoreWebView2 == null) return;
            string bg = ColorTranslator.ToHtml(background);
            string fg = ColorTranslator.ToHtml(foreground);
            if (isPdf)
            {
                string filter = theme == "dark" ? "invert(0.90) hue-rotate(180deg) brightness(0.82) contrast(0.96)" : "none";
                string tint = theme == "eye" ? "rgba(168,220,177,0.42)" : theme == "parchment" ? "rgba(236,215,170,0.38)" : theme == "dark" ? "rgba(12,16,22,0.18)" : "transparent";
                string pdfScript = "(function(){var f=document.getElementById('clypsera-pdf'),t=document.getElementById('clypsera-pdf-tint');document.documentElement.style.background='" + bg + "';document.body.style.background='" + bg + "';if(f)f.style.filter='" + filter + "';if(t){t.style.background='" + tint + "';t.style.mixBlendMode='multiply';}})();";
                try { await web.ExecuteScriptAsync(pdfScript); } catch { }
                return;
            }
            string secondary = theme == "dark" ? "#252b35" : theme == "eye" ? "#b9dfc0" : theme == "parchment" ? "#e9dec5" : "#f3f5f7";
            string script = "(function(){var id='clipboardtrail-reader-theme',s=document.getElementById(id);if(!s){s=document.createElement('style');s.id=id;document.head.appendChild(s);}s.textContent='html,body{background:" + bg + " !important;color:" + fg + " !important} pre,code,blockquote{background:" + secondary + " !important;color:" + fg + " !important} a{color:" + (theme == "dark" ? "#80b7ff" : "#315f9a") + " !important}';})();";
            try { await web.ExecuteScriptAsync(script); } catch { }
        }

        private async Task AddAnnotationAsync(string kind, string selectedOverride = null)
        {
            if (string.IsNullOrEmpty(readablePath)) return;
            if (isPdf) await RefreshPdfPageAsync();
            string selected = selectedOverride;
            if (string.IsNullOrWhiteSpace(selected)) selected = await GetSelectedTextAsync();
            SelectionAnchor anchor = isPdf ? null : await CaptureSelectionAnchorAsync();
            if (string.IsNullOrWhiteSpace(selected) && Clipboard.ContainsText()) selected = Clipboard.GetText();
            string title = kind == "highlight" ? "添加荧光笔摘录" : kind == "underline" ? "添加下划线摘录" : "添加批注";
            using (AnnotationEditorForm form = new AnnotationEditorForm(title, selected, (int)page.Value, isPdf))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                if (isPdf && form.Page <= 0)
                {
                    MessageBox.Show(Localizer.T("PDF 无法从外层网页读取内置阅读器的光标位置。为了以后能直接跳回原页，请填写当前页码后再保存。", "The embedded PDF viewer does not expose its cursor position. Enter the current page number so Clypsera can return directly to it later."), "Clypsera", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                UndoService.Capture(Localizer.T("添加图书批注", "add reading annotation"), historyStore, new LibraryStore(), annotationStore);
                string annotationId = Guid.NewGuid().ToString("N");
                if (!isPdf && !string.IsNullOrWhiteSpace(selected) && (kind == "highlight" || kind == "underline"))
                {
                    string tag = kind == "highlight" ? "mark" : "u";
                    string idJson = AppData.CreateSerializer().Serialize(annotationId);
                    string script = "(function(id){var s=window.getSelection();if(!s||s.rangeCount===0||s.isCollapsed)return false;var r=s.getRangeAt(0),n=document.createElement('" + tag + "');n.setAttribute('data-clypsera','1');n.setAttribute('data-clypsera-id',id);n.appendChild(r.extractContents());r.insertNode(n);s.removeAllRanges();return true;})(" + idJson + ")";
                    await web.ExecuteScriptAsync(script);
                    await SaveAnnotatedHtmlAsync();
                }
                int textStart = anchor == null ? -1 : anchor.Start;
                int textLength = anchor == null ? 0 : anchor.Length;
                DocumentAnnotation annotation = annotationStore.Add(node.Id, kind, form.Quote, form.Note, form.Page, null, textStart, textLength, annotationId);
                ClipboardItem record = historyStore.AddAt(AnnotationStore.BuildRecordText(annotation), new SourceInfo { ProcessName = "Clypsera 阅读器", WindowTitle = node.Name }, 50000, annotation.CreatedAt);
                if (record != null)
                {
                    historyStore.UpdateCategory(record.Id, "图书批注", false);
                    historyStore.UpdateDocument(record.Id, node.Id);
                    annotationStore.LinkHistory(annotation.Id, record.Id);
                }
                RefreshAnnotations();
                status.Text = Localizer.T("批注已保存，并同步到主窗口记录。", "Annotation saved and synced to the main records.");
                RaiseAnnotationChanged();
            }
        }

        private async Task<SelectionAnchor> CaptureSelectionAnchorAsync()
        {
            if (web.CoreWebView2 == null || isPdf) return null;
            const string script = "(function(){var s=getSelection();if(!s||s.rangeCount===0||s.isCollapsed)return null;var r=s.getRangeAt(0),a=document.createRange(),b=document.createRange();a.selectNodeContents(document.body);a.setEnd(r.startContainer,r.startOffset);b.selectNodeContents(document.body);b.setEnd(r.endContainer,r.endOffset);return {Start:a.toString().length,Length:Math.max(0,b.toString().length-a.toString().length)};})()";
            try
            {
                string json = await web.ExecuteScriptAsync(script);
                if (string.IsNullOrWhiteSpace(json) || json == "null") return null;
                SelectionAnchor anchor = AppData.CreateSerializer().Deserialize<SelectionAnchor>(json);
                return anchor != null && anchor.Start >= 0 ? anchor : null;
            }
            catch { return null; }
        }

        private async Task ExplainSelectionAsync(string selectedOverride = null)
        {
            if (!settings.DeepSeekEnabled || string.IsNullOrWhiteSpace(AppData.UnprotectSecret(settings.ApiKeyEncrypted)))
            {
                MessageBox.Show(Localizer.T("请先在主界面的设置中启用 DeepSeek 并填写 API Key。", "Enable DeepSeek and enter an API key in the main window settings first."), "Clypsera", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string selected = selectedOverride;
            if (string.IsNullOrWhiteSpace(selected)) selected = await GetSelectedTextAsync();
            if (string.IsNullOrWhiteSpace(selected) && Clipboard.ContainsText()) selected = Clipboard.GetText(TextDataFormat.UnicodeText);
            selected = (selected ?? "").Trim();
            if (string.IsNullOrWhiteSpace(selected))
            {
                MessageBox.Show(Localizer.T("请先选中一个单词或一段文字。PDF 中也可以先复制所选文字，再点“请解释”。", "Select a word or passage first. In a PDF, you can copy the selection before choosing Explain."), "Clypsera", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (TextSafety.FindSensitiveReason(selected) != null || TextSafety.LooksLikeStandaloneCredential(selected))
            {
                MessageBox.Show(Localizer.T("所选内容疑似包含密码、验证码或密钥，已阻止发送给 AI。", "The selection may contain a password, code, or key and was not sent to AI."), "Clypsera", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            ClipboardItem focus = new ClipboardItem { Text = selected, SourceProcess = "Clypsera 阅读器", SourceTitle = node.Name, DocumentId = node.Id, CapturedAt = DateTime.Now };
            AiAssistantForm assistant = new AiAssistantForm(settings, historyStore, new LibraryStore(), focus, false, AiExplanation.BuildQuestion(selected));
            assistant.Show(this); assistant.Activate();
        }

        private async Task<string> GetSelectedTextAsync()
        {
            try
            {
                if (web.CoreWebView2 == null || isPdf) return "";
                string json = await web.ExecuteScriptAsync("window.getSelection ? window.getSelection().toString() : ''");
                return AppData.CreateSerializer().Deserialize<string>(json);
            }
            catch { return ""; }
        }

        internal string PreparePdfThemeHost(string pdfPath)
        {
            string cache = Path.Combine(AppData.DirectoryPath, "reader-cache");
            Directory.CreateDirectory(cache);
            string hostPath = Path.Combine(cache, "pdf-" + node.Id + ".html");
            string pdfUri = new Uri(pdfPath).AbsoluteUri.Replace("&", "&amp;").Replace("\"", "&quot;");
            string html = "<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,height=device-height,initial-scale=1\"><style>html,body{position:fixed;inset:0;width:100vw;height:100vh;margin:0;overflow:hidden;background:#fff}#clypsera-pdf{position:absolute;inset:0;width:100vw;height:100vh;border:0;display:block}#clypsera-pdf-tint{position:absolute;inset:0;width:100vw;height:100vh;z-index:5;pointer-events:none;background:transparent;mix-blend-mode:multiply}</style></head><body><iframe id=\"clypsera-pdf\" title=\"PDF\" data-source=\"" + pdfUri + "\" src=\"about:blank\"></iframe><div id=\"clypsera-pdf-tint\"></div><script>function fitPdf(){var f=document.getElementById('clypsera-pdf'),t=document.getElementById('clypsera-pdf-tint'),w=document.documentElement.clientWidth,h=document.documentElement.clientHeight;if(f){f.style.width=w+'px';f.style.height=h+'px'}if(t){t.style.width=w+'px';t.style.height=h+'px'}}function loadPdfPage(p){var f=document.getElementById('clypsera-pdf');if(!f)return false;var b=f.getAttribute('data-source');f.src=b+(p>0?'#page='+p:'');fitPdf();return true}addEventListener('resize',fitPdf);new ResizeObserver(fitPdf).observe(document.documentElement);var requested=parseInt(new URLSearchParams(location.search).get('page')||'0',10);loadPdfPage(requested);fitPdf();</script></body></html>";
            File.WriteAllText(hostPath, html, new System.Text.UTF8Encoding(false));
            return hostPath;
        }

        private async Task ResizePdfViewportAsync()
        {
            if (!isPdf || web.CoreWebView2 == null) return;
            try { await web.ExecuteScriptAsync("if(window.fitPdf){window.fitPdf();}else{var f=document.getElementById('clypsera-pdf');if(f){f.style.width=window.innerWidth+'px';f.style.height=window.innerHeight+'px';}}"); }
            catch { }
        }

        private async void ApplyPdfZoom()
        {
            if (pdfZoom == null || pdfZoomValue == null) return;
            pdfZoomValue.Text = pdfZoom.Value + "%";
            if (web.CoreWebView2 == null) return;
            try { web.ZoomFactor = pdfZoom.Value / 100.0; await ResizePdfViewportAsync(); }
            catch { }
        }

        private async Task SaveAnnotatedHtmlAsync()
        {
            try
            {
                string json = await web.ExecuteScriptAsync("document.documentElement.outerHTML");
                string html = AppData.CreateSerializer().Deserialize<string>(json);
                if (!string.IsNullOrWhiteSpace(html)) File.WriteAllText(readablePath, html, new System.Text.UTF8Encoding(false));
            }
            catch { }
        }

        private async Task JumpToSelectedAnnotationAsync(bool showFeedback = false)
        {
            AnnotationRow row = annotations.SelectedItem as AnnotationRow;
            if (row == null)
            {
                if (showFeedback) MessageBox.Show(Localizer.T("请先在右侧选择一条批注。", "Select an annotation on the right first."), "Clypsera", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            await JumpToAnnotationAsync(row.Item);
            if (showFeedback && !LastAnnotationJumpSucceeded)
            {
                MessageBox.Show(Localizer.T("当前批注没有可用的软件定位记录。请确认它属于当前文件；新建批注会自动保存位置。", "This annotation has no usable recorded location. Confirm that it belongs to this file; new annotations save their position automatically."), Localizer.T("无法直接定位", "Direct navigation unavailable"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private async Task JumpToAnnotationAsync(DocumentAnnotation annotation)
        {
            if (annotation == null) return;
            AnnotationJumpAttempted = true;
            LastAnnotationJumpSucceeded = false;
            if (!await WaitForReaderReadyAsync())
            {
                status.Text = Localizer.T("阅读页面仍在载入，请稍后再次跳转。", "The reading page is still loading. Try the jump again shortly.");
                return;
            }
            if (annotation.Page > 0) page.Value = Math.Min(page.Maximum, annotation.Page);
            try
            {
                if (isPdf)
                {
                    if (!string.IsNullOrWhiteSpace(annotation.Quote) && await JumpByRecordedQuoteWithRetryAsync(annotation.Quote))
                    {
                        LastAnnotationJumpSucceeded = true;
                        status.Text = Localizer.T("已按软件保存的摘录定位并高亮原文。", "Located and highlighted the saved excerpt.");
                        return;
                    }
                    if (annotation.Page <= 0)
                    {
                        status.Text = Localizer.T("该批注没有可用的摘录或页码定位记录。", "This annotation has no usable excerpt or page location.");
                        return;
                    }
                    LastAnnotationJumpSucceeded = await NavigatePdfPageAsync(annotation.Page);
                    status.Text = LastAnnotationJumpSucceeded ? Localizer.T("已重新载入批注页：第 ", "Reloaded annotation page: ") + annotation.Page + Localizer.T(" 页", "") : Localizer.T("PDF 目标页载入失败。", "The PDF target page could not be loaded.");
                    return;
                }

                SelectionAnchor anchor = annotation.HasTextAnchor ? new SelectionAnchor { Start = annotation.TextStart, Length = annotation.TextLength } : null;
                if (anchor == null)
                {
                    anchor = await ResolveLegacyAnchorAsync(annotation.Quote);
                    if (anchor != null) annotationStore.UpdateAnchor(annotation.Id, anchor.Start, anchor.Length);
                }
                if (anchor != null) LastAnnotationJumpSucceeded = await JumpToTextAnchorAsync(annotation.Id, anchor);
                if (!LastAnnotationJumpSucceeded && !string.IsNullOrWhiteSpace(annotation.Quote))
                {
                    SelectionAnchor rebuilt = await ResolveLegacyAnchorAsync(annotation.Quote);
                    if (rebuilt != null)
                    {
                        LastAnnotationJumpSucceeded = await JumpToTextAnchorAsync(annotation.Id, rebuilt);
                        if (LastAnnotationJumpSucceeded) annotationStore.UpdateAnchor(annotation.Id, rebuilt.Start, rebuilt.Length);
                    }
                }
                if (!LastAnnotationJumpSucceeded && !string.IsNullOrWhiteSpace(annotation.Quote)) LastAnnotationJumpSucceeded = await JumpByRecordedQuoteAsync(annotation.Quote);
                status.Text = LastAnnotationJumpSucceeded ? Localizer.T("已按批注锚点直接跳到原文位置。", "Jumped directly to the saved annotation anchor.") : Localizer.T("已打开原资料，但保存的锚点已超出当前文档范围。", "The source is open, but the saved anchor is outside the current document.");
            }
            catch { status.Text = Localizer.T("已打开原资料，定位暂未完成。", "The source is open, but positioning did not complete."); }
        }

        private async Task<bool> WaitForReaderReadyAsync()
        {
            try
            {
                DateTime deadline = DateTime.UtcNow.AddSeconds(10);
                while (!IsDisposed && web.CoreWebView2 == null && DateTime.UtcNow < deadline) await Task.Delay(100);
                if (web.CoreWebView2 == null) return false;
                while (!IsDisposed && string.IsNullOrWhiteSpace(readablePath) && DateTime.UtcNow < deadline) await Task.Delay(100);
                while (!IsDisposed && !readerNavigationReady && DateTime.UtcNow < deadline) await Task.Delay(100);
                return web.CoreWebView2 != null && !string.IsNullOrWhiteSpace(readablePath) && readerNavigationReady;
            }
            catch { return false; }
        }

        private async Task<bool> JumpByRecordedQuoteWithRetryAsync(string quote)
        {
            // A PDF's built-in frame can report the outer page as loaded while its text
            // layer is still being prepared. Retry the saved quote silently so older
            // annotations with an inaccurate page number can still reach their passage.
            for (int attempt = 0; attempt < 6; attempt++)
            {
                if (await JumpByRecordedQuoteAsync(quote)) return true;
                if (attempt < 5) await Task.Delay(450);
            }
            return false;
        }

        private async Task<bool> JumpByRecordedQuoteAsync(string quote)
        {
            if (web.CoreWebView2 == null || string.IsNullOrWhiteSpace(quote)) return false;
            string compact = string.Join(" ", quote.Split(new char[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
            List<string> candidates = new List<string>();
            if (compact.Length > 0) candidates.Add(compact.Length > 180 ? compact.Substring(0, 180) : compact);
            if (compact.Length > 100) candidates.Add(compact.Substring(0, 100));
            if (compact.Length > 48) candidates.Add(compact.Substring(0, 48));
            foreach (string candidate in candidates.Distinct())
            {
                try
                {
                    web.CoreWebView2.Find.Stop();
                    CoreWebView2FindOptions options = web.CoreWebView2.Environment.CreateFindOptions();
                    options.FindTerm = candidate;
                    options.IsCaseSensitive = false;
                    options.ShouldMatchWord = false;
                    options.ShouldHighlightAllMatches = false;
                    options.SuppressDefaultFindDialog = true;
                    await web.CoreWebView2.Find.StartAsync(options);
                    if (web.CoreWebView2.Find.MatchCount > 0) return true;
                }
                catch { }
            }
            try { web.CoreWebView2.Find.Stop(); } catch { }
            return false;
        }

        private async Task<bool> NavigatePdfPageAsync(int targetPage)
        {
            if (web.CoreWebView2 == null || string.IsNullOrWhiteSpace(navigationPath) || targetPage <= 0) return false;
            try
            {
                UriBuilder target = new UriBuilder(new Uri(navigationPath));
                target.Query = "page=" + targetPage + "&jump=" + DateTime.UtcNow.Ticks;
                TaskCompletionSource<bool> completed = new TaskCompletionSource<bool>();
                EventHandler<CoreWebView2NavigationCompletedEventArgs> handler = null;
                handler = delegate(object sender, CoreWebView2NavigationCompletedEventArgs args)
                {
                    web.NavigationCompleted -= handler;
                    completed.TrySetResult(args.IsSuccess);
                };
                web.NavigationCompleted += handler;
                page.Value = Math.Min(page.Maximum, targetPage);
                readerNavigationReady = false;
                web.Source = target.Uri;
                Task winner = await Task.WhenAny(completed.Task, Task.Delay(5000));
                if (winner != completed.Task)
                {
                    web.NavigationCompleted -= handler;
                    return false;
                }
                return completed.Task.Result;
            }
            catch { return false; }
        }

        private async Task<bool> JumpToTextAnchorAsync(string annotationId, SelectionAnchor anchor)
        {
            if (web.CoreWebView2 == null || anchor == null || anchor.Start < 0) return false;
            string idJson = AppData.CreateSerializer().Serialize(annotationId ?? "");
            string script =
                "(function(id,start,length){" +
                "function pulse(el){if(!el)return false;var st=document.getElementById('clypsera-jump-style');if(!st){st=document.createElement('style');st.id='clypsera-jump-style';st.textContent='.clypsera-jump-target{outline:4px solid #2589ff !important;outline-offset:4px !important;background:rgba(37,137,255,.22) !important;transition:background .25s ease,outline .25s ease}';document.head.appendChild(st);}document.querySelectorAll('.clypsera-jump-target').forEach(function(x){x.classList.remove('clypsera-jump-target');});el.classList.add('clypsera-jump-target');el.scrollIntoView({block:'center',inline:'nearest',behavior:'smooth'});setTimeout(function(){el.classList.remove('clypsera-jump-target');},3200);return true;}" +
                "var marked=null;if(id){document.querySelectorAll('[data-clypsera-id]').forEach(function(x){if(!marked&&x.getAttribute('data-clypsera-id')===id)marked=x;});}if(marked)return pulse(marked);" +
                "var w=document.createTreeWalker(document.body,NodeFilter.SHOW_TEXT),n,pos=0,sn=null,en=null,so=0,eo=0,end=start+Math.max(0,length);while(n=w.nextNode()){if(n.parentElement&&n.parentElement.closest('script,style'))continue;var next=pos+n.nodeValue.length;if(sn===null&&start>=pos&&start<=next){sn=n;so=Math.min(n.nodeValue.length,start-pos);}if(end>=pos&&end<=next){en=n;eo=Math.min(n.nodeValue.length,end-pos);break;}pos=next;}if(!sn)return false;if(!en){en=sn;eo=Math.min(sn.nodeValue.length,so+Math.max(0,length));}var r=document.createRange();r.setStart(sn,so);r.setEnd(en,eo);var s=getSelection();s.removeAllRanges();s.addRange(r);var el=sn.parentElement||document.body;return pulse(el);})(" + idJson + "," + anchor.Start + "," + anchor.Length + ")";
            try
            {
                string result = await web.ExecuteScriptAsync(script);
                return result == "true";
            }
            catch { return false; }
        }

        private async Task<SelectionAnchor> ResolveLegacyAnchorAsync(string quote)
        {
            if (web.CoreWebView2 == null || string.IsNullOrWhiteSpace(quote)) return null;
            string quoteJson = AppData.CreateSerializer().Serialize(quote.Trim());
            string script =
                "(function(q){var w=document.createTreeWalker(document.body,NodeFilter.SHOW_TEXT),n,nodes=[],raw='';while(n=w.nextNode()){if(n.parentElement&&n.parentElement.closest('script,style'))continue;nodes.push(n);raw+=n.nodeValue;}var p=raw.indexOf(q),len=q.length;if(p<0){var normalized='',map=[],space=false;for(var i=0;i<raw.length;i++){var ws=/\\s/.test(raw.charAt(i));if(ws){if(normalized.length&&!space){normalized+=' ';map.push(i);}}else{normalized+=raw.charAt(i);map.push(i);}space=ws;}var needle=q.replace(/\\s+/g,' ').trim(),np=normalized.indexOf(needle);if(np<0)return null;p=map[np];var last=map[np+needle.length-1];len=last-p+1;}return {Start:p,Length:len};})(" + quoteJson + ")";
            try
            {
                string json = await web.ExecuteScriptAsync(script);
                if (string.IsNullOrWhiteSpace(json) || json == "null") return null;
                SelectionAnchor anchor = AppData.CreateSerializer().Deserialize<SelectionAnchor>(json);
                return anchor != null && anchor.Start >= 0 ? anchor : null;
            }
            catch { return null; }
        }

        private void SetAllAnnotationChecks(bool value)
        {
            updatingAnnotationChecks = true;
            for (int i = 0; i < annotations.Items.Count; i++) annotations.SetItemChecked(i, value);
            updatingAnnotationChecks = false;
            UpdateAnnotationSelectAll();
        }

        private void UpdateAnnotationSelectAll()
        {
            if (selectAllAnnotations == null) return;
            bool all = annotations.Items.Count > 0;
            for (int i = 0; i < annotations.Items.Count && all; i++) all = annotations.GetItemChecked(i);
            updatingAnnotationChecks = true; selectAllAnnotations.Checked = all; updatingAnnotationChecks = false;
        }

        private List<AnnotationRow> SelectedAnnotationRows()
        {
            List<AnnotationRow> rows = new List<AnnotationRow>();
            for (int i = 0; i < annotations.Items.Count; i++) if (annotations.GetItemChecked(i) && annotations.Items[i] is AnnotationRow) rows.Add((AnnotationRow)annotations.Items[i]);
            if (rows.Count == 0 && annotations.SelectedItem is AnnotationRow) rows.Add((AnnotationRow)annotations.SelectedItem);
            return rows;
        }

        private void ExportSelectedAnnotations()
        {
            List<AnnotationRow> rows = SelectedAnnotationRows();
            if (rows.Count == 0) { MessageBox.Show(Localizer.T("请先勾选批注。", "Select annotations first."), "Clypsera", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            using (SaveFileDialog dialog = new SaveFileDialog
            {
                Title = Localizer.T("导出所选批注", "Export selected annotations"),
                FileName = node.Name + "-批注",
                Filter = "TXT (*.txt)|*.txt|Markdown (*.md)|*.md|PDF (*.pdf)|*.pdf|Word (*.docx)|*.docx|CSV (*.csv)|*.csv|JSON (*.json)|*.json",
                AddExtension = true,
                OverwritePrompt = true,
                InitialDirectory = ExistingDefaultExportFolder()
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                string[] formats = new string[] { "txt", "md", "pdf", "docx", "csv", "json" };
                string format = formats[Math.Max(0, Math.Min(formats.Length - 1, dialog.FilterIndex - 1))];
                List<ClipboardItem> items = rows.Select(x => new ClipboardItem
                {
                    Id = x.Item.Id,
                    Text = AnnotationStore.BuildRecordText(x.Item),
                    CapturedAt = x.Item.CreatedAt,
                    SourceProcess = "Clypsera 阅读器",
                    SourceTitle = node.Name,
                    Category = "图书批注",
                    DocumentId = node.Id,
                    Note = x.Item.Note,
                    NoteQuote = x.Item.Quote
                }).ToList();
                try { ExportWriter.WriteMerged(dialog.FileName, format, items, new LibraryStore()); status.Text = Localizer.T("所选批注已导出。", "Selected annotations exported."); }
                catch (Exception ex) { MessageBox.Show(ex.Message, Localizer.T("导出失败", "Export failed"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        private string ExistingDefaultExportFolder()
        {
            try { return !string.IsNullOrWhiteSpace(settings.DefaultExportFolder) && Directory.Exists(settings.DefaultExportFolder) ? settings.DefaultExportFolder : ""; }
            catch { return ""; }
        }

        private async void ReaderFormKeyDown(object sender, KeyEventArgs e)
        {
            if (!e.Control || e.KeyCode != Keys.Z) return;
            string description;
            if (UndoService.Undo(out description))
            {
                historyStore.Reload(); annotationStore.Reload(); RefreshAnnotations();
                await SynchronizeStoredMarkupAsync();
                status.Text = Localizer.T("已撤销：", "Undone: ") + description;
                RaiseAnnotationChanged();
            }
            else status.Text = Localizer.T("没有可以撤销的操作。", "There is nothing to undo.");
            e.Handled = true; e.SuppressKeyPress = true;
        }

        private void RefreshAnnotations()
        {
            HashSet<string> checkedIds = new HashSet<string>();
            for (int i = 0; i < annotations.Items.Count; i++)
            {
                AnnotationRow old = annotations.Items[i] as AnnotationRow;
                if (old != null && annotations.GetItemChecked(i)) checkedIds.Add(old.Item.Id);
            }
            updatingAnnotationChecks = true;
            annotations.BeginUpdate(); annotations.Items.Clear();
            int targetIndex = -1;
            foreach (DocumentAnnotation item in annotationStore.ForNode(node.Id))
            {
                int index = annotations.Items.Add(new AnnotationRow(item), checkedIds.Contains(item.Id));
                if (initialAnnotation != null && (item.Id == initialAnnotation.Id || (!string.IsNullOrWhiteSpace(initialAnnotation.HistoryItemId) && item.HistoryItemId == initialAnnotation.HistoryItemId))) targetIndex = index;
            }
            annotations.EndUpdate(); annotations.SelectedIndex = targetIndex;
            updatingAnnotationChecks = false; UpdateAnnotationSelectAll();
        }

        public async void ReloadAnnotations()
        {
            annotationStore.Reload();
            RefreshAnnotations();
            await SynchronizeStoredMarkupAsync();
        }

        public async void ReloadAfterUndo()
        {
            historyStore.Reload(); annotationStore.Reload(); RefreshAnnotations();
            await SynchronizeStoredMarkupAsync();
            status.Text = Localizer.T("已撤销上一步操作。", "The last action was undone.");
        }

        private async Task DeleteSelectedAnnotationAsync(string quoteOverride = null)
        {
            AnnotationRow row = annotations.SelectedItem as AnnotationRow;
            if (row == null && !string.IsNullOrWhiteSpace(quoteOverride))
            {
                DocumentAnnotation match = annotationStore.ForNode(node.Id).FirstOrDefault(x => string.Equals((x.Quote ?? "").Trim(), quoteOverride.Trim(), StringComparison.Ordinal));
                if (match != null) row = new AnnotationRow(match);
            }
            if (row == null) return;
            if (MessageBox.Show(Localizer.T("确定删除这条批注吗？外部的批注记录也会一并删除。", "Delete this annotation? Its record in the main window will also be removed."), "Clypsera", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            UndoService.Capture(Localizer.T("删除图书批注", "delete reading annotation"), historyStore, new LibraryStore(), annotationStore);
            annotationStore.Delete(row.Item.Id);
            if (!string.IsNullOrEmpty(row.Item.HistoryItemId)) historyStore.Delete(row.Item.HistoryItemId);
            await SynchronizeStoredMarkupAsync();
            RefreshAnnotations();
            status.Text = Localizer.T("批注和外部记录已删除。", "Annotation and main record deleted.");
            RaiseAnnotationChanged();
        }

        private async Task SynchronizeStoredMarkupAsync()
        {
            if (isPdf || web.CoreWebView2 == null || string.IsNullOrWhiteSpace(readablePath)) return;
            var targets = annotationStore.ForNode(node.Id)
                .Where(x => (x.Kind == "highlight" || x.Kind == "underline") && !string.IsNullOrWhiteSpace(x.Quote))
                .Select(x => new { id = x.Id, kind = x.Kind, quote = x.Quote.Trim(), start = x.TextStart, length = x.TextLength, anchored = x.HasTextAnchor }).ToList();
            string json = AppData.CreateSerializer().Serialize(targets);
            string script = "(function(items){function unwrap(n){var p=n.parentNode;while(n.firstChild)p.insertBefore(n.firstChild,n);p.removeChild(n);p.normalize();}document.querySelectorAll('mark[data-clypsera=\"1\"],u[data-clypsera=\"1\"]').forEach(unwrap);function wrap(x){var w=document.createTreeWalker(document.body,NodeFilter.SHOW_TEXT),n,a=[],s='';while(n=w.nextNode()){if(n.parentElement&&n.parentElement.closest('script,style'))continue;a.push({n:n,b:s.length,e:s.length+n.nodeValue.length});s+=n.nodeValue;}var i=x.anchored?x.start:s.indexOf(x.quote),len=x.anchored?x.length:x.quote.length;if(i<0)return;var e=i+Math.max(0,len),st=null,en=null,so=0,eo=0;for(var j=0;j<a.length;j++){if(st===null&&i>=a[j].b&&i<=a[j].e){st=a[j].n;so=i-a[j].b;}if(e>=a[j].b&&e<=a[j].e){en=a[j].n;eo=e-a[j].b;break;}}if(!st)return;if(!en){en=st;eo=Math.min(st.nodeValue.length,so+Math.max(0,len));}try{var r=document.createRange();r.setStart(st,Math.min(so,st.nodeValue.length));r.setEnd(en,Math.min(eo,en.nodeValue.length));var el=document.createElement(x.kind==='underline'?'u':'mark');el.setAttribute('data-clypsera','1');el.setAttribute('data-clypsera-id',x.id);el.appendChild(r.extractContents());r.insertNode(el);}catch(ex){}}items.forEach(wrap);return true;})(" + json + ")";
            try { await web.ExecuteScriptAsync(script); await SaveAnnotatedHtmlAsync(); } catch { }
        }

        private void CopySelectedAnnotation()
        {
            AnnotationRow row = annotations.SelectedItem as AnnotationRow; if (row == null) return;
            string text = string.IsNullOrWhiteSpace(row.Item.Quote) ? row.Item.Note : row.Item.Quote;
            if (!string.IsNullOrWhiteSpace(text)) Clipboard.SetText(text);
        }

        private void RaiseAnnotationChanged()
        {
            if (AnnotationChanged != null) AnnotationChanged(this, EventArgs.Empty);
        }

        private void ShowSelectedAnnotation()
        {
            AnnotationRow row = annotations.SelectedItem as AnnotationRow; if (row == null) return;
            DocumentAnnotation item = row.Item;
            MessageBox.Show((item.Page > 0 ? "第 " + item.Page + " 页\n\n" : "") + "摘录：\n" + item.Quote + "\n\n批注：\n" + item.Note, "批注详情", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    internal sealed class SelectionAnchor
    {
        public int Start { get; set; }
        public int Length { get; set; }
    }

    internal sealed class AnnotationRow
    {
        public DocumentAnnotation Item { get; private set; }
        public AnnotationRow(DocumentAnnotation item) { Item = item; }
        public override string ToString()
        {
            string kind = Item.Kind == "highlight" ? "🖍 荧光笔" : Item.Kind == "underline" ? "U̲ 下划线" : "📝 批注";
            string text = string.IsNullOrWhiteSpace(Item.Note) ? Item.Quote : Item.Note;
            if (text.Length > 10) text = text.Substring(0, 10) + "…";
            if (Localizer.IsEnglish)
            {
                kind = Item.Kind == "highlight" ? "🖍 Highlight" : Item.Kind == "underline" ? "U̲ Underline" : "📝 Annotation";
                return (Item.Page > 0 ? "[Page " + Item.Page + "] " : "") + kind + " · " + text;
            }
            return (Item.Page > 0 ? "[第" + Item.Page + "页] " : "") + kind + " · " + text;
        }
    }
}
