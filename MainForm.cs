using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ClipboardTrail
{
    public sealed class MainForm : Form
    {
        private const string AllTag = "__all__";
        private const string UnfiledTag = "__unfiled__";

        private readonly HistoryStore store;
        private readonly LibraryStore library;
        private SettingsData settings;
        private readonly NotifyIcon tray;
        private readonly DataGridView grid;
        private readonly TreeView tree;
        private readonly RichTextBox viewer;
        private readonly Label viewerTitle;
        private readonly Label viewerMeta;
        private readonly Label referenceLabel;
        private TableLayoutPanel viewerLayout;
        private Panel viewerAnnotationPanel;
        private FlowLayoutPanel viewerAnnotationList;
        private readonly HashSet<string> expandedViewerAnnotations = new HashSet<string>();
        private string currentViewerItemId;
        private DateTime? columnTimeSince;
        private string columnSourceFilter;
        private string columnDocumentFilter;
        private string columnTextFilter;
        private int recordSortColumn = 1;
        private bool recordSortDescending = true;
        private ContextMenuStrip activeRecordColumnMenu;
        private bool openingRecordColumnMenu;
        private Label recordHeaderLabel;
        private string recordHeaderBaseText = "";
        private TextBox search;
        private ComboBox searchScope;
        private ComboBox categoryFilter;
        private readonly SplitContainer leftSplit;
        private readonly SplitContainer contentSplit;
        private ToolStripMenuItem leftPaneButton;
        private ToolStripMenuItem middlePaneButton;
        private ToolStripMenuItem rightPaneButton;
        private ToolStripMenuItem viewPauseMenu;
        private ToolStripMenuItem viewOverlayMenu;
        private Button leftCollapseHandle;
        private readonly ToolTip leftCollapseTip = new ToolTip();
        private readonly ToolStripMenuItem pauseMenu;
        private readonly ToolStripMenuItem overlayMenu;
        private readonly OverlayForm overlay;
        private readonly Timer captureTimer;
        private readonly Timer liveSyncDebounceTimer;
        private readonly Timer liveSyncPollTimer;
        private readonly Dictionary<string, FileSystemWatcher> liveFolderWatchers = new Dictionary<string, FileSystemWatcher>();
        private readonly HashSet<string> pendingLiveSyncRoots = new HashSet<string>();
        private bool liveSyncBusy;
        private SourceInfo pendingSource;
        private bool paused;
        private bool exiting;
        private bool ignoreNextClipboard;
        private bool rebuildingTree;
        private readonly bool headlessMode;
        private LocalApiServer apiServer;
        private HashSet<string> aiSearchIds;
        private AiAssistantForm aiAssistant;
        private bool safePasteBusy;
        private readonly HashSet<string> checkedItemIds = new HashSet<string>();
        private CheckBox selectAllRecords;
        private bool updatingSelectAll;
        private MenuStrip mainTools;
        private TableLayoutPanel shell;
        private TableLayoutPanel libraryLayout;
        private TableLayoutPanel recordLayout;
        private Panel recordSearchPanel;
        private Button aiSearchButton;
        private Button batchActionButton;
        private Button clearSearchButton;
        private const int SafePasteHotkeyId = 0x5A31;
        internal string LastRequestedReaderNodeId { get; private set; }
        internal DocumentAnnotation LastRequestedNavigationTarget { get; private set; }

        private sealed class ViewerStyleRun
        {
            public int LogicalStart;
            public int LogicalLength;
            public Color BackColor;
            public Color ForeColor;
            public string FontName;
            public float FontSize;
            public FontStyle FontStyle;
        }

        public MainForm(bool showAtStart, bool headless)
        {
            headlessMode = headless;
            paused = true;
            settings = AppData.LoadSettings();
            Localizer.Use(settings.Language);
            store = new HistoryStore();
            library = new LibraryStore();
            MigrateAnnotationRecords();
            Text = "Clypsera · 我的剪贴板资料库";
            Icon = Branding.CreateIcon();
            Font = new Font("Microsoft YaHei UI", 9F);
            Width = 1800;
            Height = 980;
            MinimumSize = new Size(1360, 720);
            StartPosition = FormStartPosition.CenterScreen;
            AllowDrop = true;
            KeyPreview = true;

            mainTools = BuildMainMenu();
            shell = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            mainTools.Dock = DockStyle.Fill;
            shell.Controls.Add(mainTools, 0, 0);
            leftSplit = new SplitContainer { Name = "MainLeftSplit", Width = ClientSize.Width, Height = ClientSize.Height - mainTools.Height, Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 8, SplitterDistance = 360, Panel1MinSize = 300, Panel2MinSize = 900 };
            shell.Controls.Add(leftSplit, 0, 1);
            Controls.Add(shell);

            tree = BuildLibraryPanel(leftSplit.Panel1);
            contentSplit = new SplitContainer { Name = "MainContentSplit", Width = Math.Max(900, leftSplit.Panel2.ClientSize.Width), Height = leftSplit.Panel2.ClientSize.Height, Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 8, SplitterDistance = 650, Panel1MinSize = 420, Panel2MinSize = 420 };
            leftSplit.Panel2.Controls.Add(contentSplit);
            grid = BuildRecordPanel(contentSplit.Panel1);
            viewer = BuildViewerPanel(contentSplit.Panel2, out viewerTitle, out viewerMeta, out referenceLabel);

            ContextMenuStrip trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("打开资料库", null, delegate { ShowMain(); });
            overlayMenu = new ToolStripMenuItem("显示悬浮窗", null, delegate { ToggleOverlay(); }) { Checked = settings.OverlayVisible };
            pauseMenu = new ToolStripMenuItem("开始采集", null, delegate { TogglePause(); });
            trayMenu.Items.Add(overlayMenu); trayMenu.Items.Add(pauseMenu);
            trayMenu.Items.Add("安全粘贴  Ctrl+Shift+V", null, delegate { ShowSafePasteHelp(); });
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("设置", null, delegate { OpenSettings(); });
            trayMenu.Items.Add("退出", null, delegate { ExitApplication(); });
            Localizer.ApplyMenu(trayMenu);
            tray = new NotifyIcon { Text = "Clypsera · 等待手动开始采集", Icon = Branding.CreateIcon(), Visible = !headless, ContextMenuStrip = trayMenu };
            tray.DoubleClick += delegate { ShowMain(); };

            overlay = new OverlayForm();
            overlay.OpenRequested += delegate { ShowMain(); };
            overlay.HideRequested += delegate { SetOverlayVisible(false); };
            overlay.UpdateItem(store.Snapshot().LastOrDefault(), false);

            captureTimer = new Timer { Interval = 100 };
            captureTimer.Tick += CaptureTimerTick;
            liveSyncDebounceTimer = new Timer { Interval = 900 };
            liveSyncDebounceTimer.Tick += LiveSyncDebounceTick;
            liveSyncPollTimer = new Timer { Interval = 60000 };
            liveSyncPollTimer.Tick += delegate { QueueAllLiveFolders(); };
            FormClosing += MainFormClosing;
            KeyDown += MainFormKeyDown;
            Shown += delegate
            {
                if (showAtStart && !headlessMode) WindowState = FormWindowState.Maximized;
                BalanceInitialLayout();
                if (!showAtStart)
                {
                    Hide();
                    if (!headless) tray.ShowBalloonTip(2500, "Clypsera 已启动", "剪贴板采集尚未开启。需要时请从托盘或“视图”菜单选择“开始采集”。", ToolTipIcon.Info);
                }
            };
            Load += async delegate
            {
                NativeMethods.AddClipboardFormatListener(Handle);
                bool hotkeyReady = NativeMethods.RegisterHotKey(Handle, SafePasteHotkeyId, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, (uint)Keys.V);
                if (!hotkeyReady && !headlessMode) tray.ShowBalloonTip(4000, "Clypsera", "Ctrl+Shift+V 已被其他软件占用，安全粘贴快捷键未启用。", ToolTipIcon.Warning);
                if (!headlessMode)
                {
                    ConfigureLiveFolderWatchers();
                    liveSyncPollTimer.Start();
                    await SyncAllLiveFoldersAtStartupAsync();
                }
            };
            FormClosed += delegate
            {
                NativeMethods.RemoveClipboardFormatListener(Handle);
                NativeMethods.UnregisterHotKey(Handle, SafePasteHotkeyId);
                liveSyncDebounceTimer.Stop(); liveSyncPollTimer.Stop();
                DisposeLiveFolderWatchers();
            };
            RebuildTree(AllTag);
            RefreshCategories();
            RefreshGrid();
            ApplyLocalApi();
            if (!headlessMode) ApplyStartupSetting();
            UiStyle.Prepare(this, settings.AppTheme);
            Localizer.Apply(this);
            ApplyResponsiveLayout();
            UpdateViewer();
        }

        private MenuStrip BuildMainMenu()
        {
            MenuStrip menu = new MenuStrip { AutoSize = false, Height = 42, Padding = new Padding(8, 6, 8, 6), BackColor = Color.FromArgb(248, 249, 251), Font = new Font("Microsoft YaHei UI", 9.5F) };
            searchScope = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            searchScope.Items.AddRange(new object[] { Localizer.T("关键词", "Keyword"), Localizer.T("书名", "Book title"), Localizer.T("文件名", "File name") });
            searchScope.SelectedIndex = 0;
            search = new TextBox();
            categoryFilter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };

            ToolStripMenuItem file = new ToolStripMenuItem("文件");
            file.DropDownItems.Add("导入文档…", null, delegate { ChooseFilesToImport(); });
            file.DropDownItems.Add(Localizer.T("导入文件夹…", "Import folder…"), null, delegate { ChooseFolderToImport(); });
            file.DropDownItems.Add("导出…", null, delegate { Export(null); });
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add("设置…", null, delegate { OpenSettings(); });
            file.DropDownItems.Add("退出", null, delegate { ExitApplication(); });

            ToolStripMenuItem create = new ToolStripMenuItem("新建");
            create.DropDownItems.Add("文件夹", null, delegate { CreateLibraryNode("folder"); });
            create.DropDownItems.Add("文档", null, delegate { CreateLibraryNode("document"); });
            create.DropDownItems.Add("书籍", null, delegate { CreateLibraryNode("book"); });
            create.DropDownItems.Add(new ToolStripSeparator());
            create.DropDownItems.Add(Localizer.T("手动粘贴或录入…", "Manual paste or entry…"), null, delegate { OpenManualEntry(); });

            ToolStripMenuItem searchMenu = new ToolStripMenuItem("搜索");
            searchMenu.DropDownItems.Add("关键词搜索…", null, delegate { OpenLocalSearch("关键词"); });
            searchMenu.DropDownItems.Add("书名搜索…", null, delegate { OpenLocalSearch("书名"); });
            searchMenu.DropDownItems.Add("文件名搜索…", null, delegate { OpenLocalSearch("文件名"); });
            ToolStripMenuItem topicMenu = new ToolStripMenuItem("主题筛选");
            topicMenu.DropDownOpening += delegate { PopulateTopicMenu(topicMenu); };
            searchMenu.DropDownItems.Add(topicMenu);
            searchMenu.DropDownItems.Add("清除本地筛选", null, delegate { ClearLocalSearch(); });
            searchMenu.DropDownItems.Add(new ToolStripSeparator());
            searchMenu.DropDownItems.Add("AI 描述搜索…", null, delegate { OpenAiAssistant(true); });

            ToolStripMenuItem organize = new ToolStripMenuItem("整理");
            organize.DropDownItems.Add("移动到文件夹或文档…", null, delegate { MoveSelectedToLibraryNode(); });
            organize.DropDownItems.Add("修改主题…", null, delegate { EditCategory(); });
            organize.DropDownItems.Add("AI 自动归档", null, async delegate { await ClassifySelectedAsync(); });
            organize.DropDownItems.Add("AI 问询…", null, delegate { OpenAiAssistant(false); });
            organize.DropDownItems.Add(new ToolStripSeparator());
            organize.DropDownItems.Add(Localizer.T("撤销上一步  Ctrl+Z", "Undo last action  Ctrl+Z"), null, delegate { UndoLastAction(); });
            organize.DropDownItems.Add("安全粘贴说明", null, delegate { ShowSafePasteHelp(); });

            ToolStripMenuItem reading = new ToolStripMenuItem("阅读批注");
            reading.DropDownItems.Add("打开选中文件", null, delegate { OpenSelectedLibraryFile(); });
            reading.DropDownItems.Add(Localizer.T("打开当前记录原文", "Open current record source"), null, delegate { OpenSelectedRecordSource(); });
            reading.DropDownItems.Add("编辑当前记录…", null, delegate { EditSelected(); });
            reading.DropDownItems.Add(new ToolStripSeparator());
            reading.DropDownItems.Add(Localizer.T("🖍 荧光笔", "🖍 Highlight"), null, delegate { FormatViewer("highlight"); });
            reading.DropDownItems.Add(Localizer.T("U̲ 下划线", "U̲ Underline"), null, delegate { FormatViewer("underline"); });
            reading.DropDownItems.Add(Localizer.T("✨ AI 整理排版", "✨ AI tidy layout"), null, async delegate { await OrganizeViewerLayoutAsync(); });
            reading.DropDownItems.Add(Localizer.T("✨ 请解释", "✨ Explain with AI"), null, delegate { OpenViewerExplanation(); });
            reading.DropDownItems.Add(Localizer.T("⌫ 清除格式", "⌫ Clear format"), null, delegate { FormatViewer("clear"); });

            ToolStripMenuItem view = new ToolStripMenuItem("视图");
            leftPaneButton = new ToolStripMenuItem("显示分类栏") { CheckOnClick = true, Checked = true };
            middlePaneButton = new ToolStripMenuItem("显示记录栏") { CheckOnClick = true, Checked = true };
            rightPaneButton = new ToolStripMenuItem("显示查看栏") { CheckOnClick = true, Checked = true };
            view.DropDownItems.Add(leftPaneButton); view.DropDownItems.Add(middlePaneButton); view.DropDownItems.Add(rightPaneButton);
            view.DropDownItems.Add(new ToolStripSeparator());
            viewOverlayMenu = new ToolStripMenuItem("显示悬浮窗") { CheckOnClick = true, Checked = settings.OverlayVisible };
            viewPauseMenu = new ToolStripMenuItem("开始采集");
            view.DropDownItems.Add(viewOverlayMenu); view.DropDownItems.Add(viewPauseMenu);

            ToolStripMenuItem help = new ToolStripMenuItem("帮助");
            help.DropDownItems.Add(Localizer.T("零基础使用手册（PDF）", "Beginner guide (PDF)"), null, delegate { OpenBeginnerGuide(); });
            help.DropDownItems.Add(new ToolStripSeparator());
            help.DropDownItems.Add("安全粘贴快捷键  Ctrl+Shift+V", null, delegate { ShowSafePasteHelp(); });
            help.DropDownItems.Add("关于 Clypsera", null, delegate { MessageBox.Show(Localizer.T("Clypsera v26\n用于收集、整理和阅读个人资料的桌面工具。\n\n使用方法：帮助 → 零基础使用手册（PDF）\n作者：eddielee7318 BJC", "Clypsera v26\nA desktop tool for collecting, organizing, and reading personal material.\n\nGuide: Help → Beginner guide (PDF)\nCreated by eddielee7318 BJC"), Localizer.T("关于 Clypsera", "About Clypsera"), MessageBoxButtons.OK, MessageBoxIcon.Information); });
            help.DropDownItems.Add(new ToolStripSeparator());
            help.DropDownItems.Add(new ToolStripMenuItem(Localizer.T("作者 · eddielee7318 BJC", "Created by · eddielee7318 BJC")) { Enabled = false });

            menu.Items.Add(file); menu.Items.Add(create); menu.Items.Add(searchMenu); menu.Items.Add(organize); menu.Items.Add(reading); menu.Items.Add(view); menu.Items.Add(help);
            foreach (ToolStripItem item in menu.Items) item.Padding = new Padding(10, 4, 10, 4);
            search.TextChanged += delegate { aiSearchIds = null; RefreshGrid(); };
            searchScope.SelectedIndexChanged += delegate { aiSearchIds = null; RefreshGrid(); };
            categoryFilter.SelectedIndexChanged += delegate { RefreshGrid(); };
            leftPaneButton.CheckedChanged += delegate { ApplyPaneVisibility(); };
            middlePaneButton.CheckedChanged += delegate { ApplyPaneVisibility(); };
            rightPaneButton.CheckedChanged += delegate { ApplyPaneVisibility(); };
            viewOverlayMenu.Click += delegate { ToggleOverlay(); };
            viewPauseMenu.Click += delegate { TogglePause(); };
            return menu;
        }

        private void OpenBeginnerGuide()
        {
            string guidePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Clypsera-v26-零基础使用手册.pdf");
            if (!File.Exists(guidePath))
            {
                MessageBox.Show(Localizer.T("没有在程序目录找到零基础使用手册。请重新运行 v26 安装包，或单独打开随安装包提供的 PDF。", "The beginner guide was not found in the program folder. Run the v26 installer again, or open the separately supplied PDF."), Localizer.T("使用手册", "User guide"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try { Process.Start(new ProcessStartInfo(guidePath) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(Localizer.T("无法打开使用手册：", "Could not open the guide: ") + ex.Message, Localizer.T("使用手册", "User guide"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private TreeView BuildLibraryPanel(Control parent)
        {
            Panel header = new Panel { Name = "LibraryPaneHeader", Dock = DockStyle.Fill, BackColor = Color.FromArgb(244, 247, 251), Padding = new Padding(12, 7, 8, 7) };
            Label title = new Label { Text = "我的分类", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font, FontStyle.Bold), AutoEllipsis = true };
            TreeView value = new TreeView { Name = "LibraryTree", Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, HideSelection = false, FullRowSelect = true, AllowDrop = true, Font = new Font("Microsoft YaHei UI", 9.5F), ShowNodeToolTips = true };
            libraryLayout = new TableLayoutPanel { Name = "LibraryPane", Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
            libraryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52)); libraryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            libraryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.Dock = DockStyle.Fill;
            libraryLayout.Controls.Add(header, 0, 0); libraryLayout.Controls.Add(value, 0, 1); parent.Controls.Add(libraryLayout);
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("新建文件夹", null, delegate { CreateLibraryNode("folder"); });
            menu.Items.Add("新建文档", null, delegate { CreateLibraryNode("document"); });
            menu.Items.Add("新建书籍", null, delegate { CreateLibraryNode("book"); });
            menu.Items.Add("导入文档或电子书", null, delegate { ChooseFilesToImport(); });
            menu.Items.Add(Localizer.T("导入整个文件夹", "Import entire folder"), null, delegate { ChooseFolderToImport(); });
            menu.Items.Add("导出此分类…", null, delegate { Export("folder"); });
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem syncNow = new ToolStripMenuItem(Localizer.T("🔄 立即同步活动文件夹", "🔄 Sync live folder now"), null, async delegate { await SyncSelectedLiveFolderAsync(); });
            ToolStripMenuItem makeRegular = new ToolStripMenuItem(Localizer.T("停止持续同步（转为普通文件夹）", "Stop syncing (make regular folder)"), null, delegate { DisableSelectedLiveFolder(); });
            menu.Items.Add(syncNow);
            menu.Items.Add(makeRegular);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("打开阅读", null, delegate { OpenSelectedLibraryFile(); });
            menu.Items.Add("重命名", null, delegate { RenameLibraryNode(); });
            menu.Items.Add("删除", null, delegate { DeleteLibraryNode(); });
            menu.Opening += delegate
            {
                LibraryNode selected = value.SelectedNode == null ? null : value.SelectedNode.Tag as LibraryNode;
                LibraryNode liveRoot = selected == null ? null : library.GetLiveRootForNode(selected.Id);
                syncNow.Enabled = liveRoot != null;
                makeRegular.Enabled = liveRoot != null;
            };
            Button actions = CreateHeaderMenuButton(menu);
            TableLayoutPanel headerLayout = CreateHeaderLayout();
            headerLayout.Controls.Add(title, 0, 0); headerLayout.Controls.Add(actions, 1, 0);
            header.Controls.Add(headerLayout);
            value.ContextMenuStrip = menu;
            value.AfterSelect += delegate { if (!rebuildingTree) { RefreshGrid(); UpdateViewer(); } };
            value.NodeMouseDoubleClick += delegate(object sender, TreeNodeMouseClickEventArgs e) { LibraryNode node = e.Node.Tag as LibraryNode; if (node != null && !string.IsNullOrWhiteSpace(node.FilePath)) OpenLibraryFile(node); else if (node == null || node.IsFolder) e.Node.Toggle(); };
            value.ItemDrag += TreeItemDrag;
            value.DragEnter += TreeDragEnter;
            value.DragOver += TreeDragOver;
            value.DragDrop += TreeDragDrop;
            return value;
        }

        private DataGridView BuildRecordPanel(Control parent)
        {
            Panel header = new Panel { Name = "RecordsPaneHeader", Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(12, 7, 8, 7) };
            recordHeaderLabel = new Label { Text = "剪贴板记录", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font, FontStyle.Bold), AutoEllipsis = true };
            DataGridView value = new DataGridView { Name = "RecordsGrid", Dock = DockStyle.Fill, ReadOnly = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, AutoGenerateColumns = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = true, BackgroundColor = Color.White, BorderStyle = BorderStyle.None, RowHeadersVisible = false, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None, RowTemplate = { Height = 40 }, ColumnHeadersHeight = 38, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing, ScrollBars = ScrollBars.Both, AllowDrop = true };
            value.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "✓", DataPropertyName = "Marked", Width = 48, MinimumWidth = 44, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable, ToolTipText = Localizer.T("勾选后可批量操作", "Check for batch actions") });
            value.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = Localizer.T("时间 ▾", "Time ▾"), DataPropertyName = "Time", Width = 170, MinimumWidth = 140, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable, ToolTipText = Localizer.T("点击筛选或排序", "Click to filter or sort") });
            value.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = Localizer.T("来源 ▾", "Source ▾"), DataPropertyName = "Source", Width = 135, MinimumWidth = 105, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable, ToolTipText = Localizer.T("点击筛选或排序", "Click to filter or sort") });
            DataGridViewTextBoxColumn documentColumn = new DataGridViewTextBoxColumn { HeaderText = Localizer.T("文档 ▾", "Document ▾"), DataPropertyName = "Document", Width = 150, MinimumWidth = 110, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable, ToolTipText = Localizer.T("点击筛选或排序", "Click to filter or sort") };
            value.Columns.Add(documentColumn);
            value.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = Localizer.T("文本内容 ▾", "Text ▾"), DataPropertyName = "Preview", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 180, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable, ToolTipText = Localizer.T("点击筛选或排序", "Click to filter or sort") });
            Panel searchPanel = BuildRecordSearchPanel();
            recordLayout = new TableLayoutPanel { Name = "RecordsPane", Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
            recordLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58)); recordLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70)); recordLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            recordLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.Dock = DockStyle.Fill;
            recordLayout.Controls.Add(header, 0, 0); recordLayout.Controls.Add(searchPanel, 0, 1); recordLayout.Controls.Add(value, 0, 2); parent.Controls.Add(recordLayout);
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add(Localizer.T("打开原资料并定位", "Open source and jump"), null, delegate { OpenSelectedRecordSource(); });
            menu.Items.Add("在右栏查看", null, delegate { UpdateViewer(); });
            menu.Items.Add("编辑内容", null, delegate { EditSelected(); });
            menu.Items.Add("移动到文件夹或文档", null, delegate { MoveSelectedToLibraryNode(); });
            menu.Items.Add("手动修改主题", null, delegate { EditCategory(); });
            menu.Items.Add("使用 DeepSeek 整理", null, async delegate { await ClassifySelectedAsync(); });
            menu.Items.Add("复制到剪贴板", null, delegate { CopySelected(); });
            menu.Items.Add("导出所选条目…", null, delegate { Export("selected"); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("删除所选条目", null, delegate { DeleteSelected(); });
            Button actions = CreateHeaderMenuButton(menu);
            Panel recordTitleArea = new Panel { Dock = DockStyle.Fill };
            leftCollapseHandle = new Button { Name = "LeftPaneGrip", Text = "◀", Dock = DockStyle.Left, Width = 42, FlatStyle = FlatStyle.Flat, Margin = Padding.Empty, Font = new Font("Segoe UI Symbol", 9F) };
            leftCollapseHandle.FlatAppearance.BorderSize = 0;
            leftCollapseTip.SetToolTip(leftCollapseHandle, Localizer.T("收起分类栏", "Collapse library pane"));
            leftCollapseHandle.Click += delegate { leftPaneButton.Checked = leftSplit.Panel1Collapsed; };
            selectAllRecords = new CheckBox { Text = Localizer.T("全选", "Select all"), Dock = DockStyle.Right, Width = Localizer.IsEnglish ? 150 : 112, TextAlign = ContentAlignment.MiddleLeft, AutoSize = false };
            selectAllRecords.CheckedChanged += delegate { if (!updatingSelectAll) MarkVisibleItems(selectAllRecords.Checked); };
            recordTitleArea.Controls.Add(recordHeaderLabel); recordTitleArea.Controls.Add(selectAllRecords); recordTitleArea.Controls.Add(leftCollapseHandle);
            TableLayoutPanel headerLayout = CreateHeaderLayout();
            headerLayout.Controls.Add(recordTitleArea, 0, 0); headerLayout.Controls.Add(actions, 1, 0);
            header.Controls.Add(headerLayout);
            value.ContextMenuStrip = menu;
            value.SelectionChanged += delegate { UpdateViewer(); };
            value.CurrentCellDirtyStateChanged += delegate { if (value.IsCurrentCellDirty && value.CurrentCell != null && value.CurrentCell.ColumnIndex == 0) value.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            value.CellContentClick += delegate(object sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0 && e.ColumnIndex == 0) value.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            value.CellValueChanged += delegate(object sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0 || e.ColumnIndex != 0) return;
                ClipboardRow row = value.Rows[e.RowIndex].DataBoundItem as ClipboardRow; if (row == null) return;
                if (row.Marked) checkedItemIds.Add(row.Item.Id); else checkedItemIds.Remove(row.Item.Id);
                UpdateRecordHeaderCount();
            };
            value.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0 || e.ColumnIndex <= 0) return;
                ClipboardRow row = value.Rows[e.RowIndex].DataBoundItem as ClipboardRow;
                if (row == null) return;
                value.CurrentCell = value.Rows[e.RowIndex].Cells[e.ColumnIndex];
                OpenRecordSource(row.Item);
            };
            value.ColumnHeaderMouseClick += delegate(object sender, DataGridViewCellMouseEventArgs e) { if (e.ColumnIndex == 0) MarkVisibleItems(!FilteredItems().All(x => checkedItemIds.Contains(x.Id))); else ShowRecordColumnMenu(e.ColumnIndex); };
            value.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Space && value.CurrentCell != null && value.CurrentCell.ColumnIndex == 0) { ToggleGridCheck(value.CurrentCell.RowIndex); e.Handled = true; e.SuppressKeyPress = true; } };
            value.MouseDown += GridMouseDown;
            value.CellMouseDown += delegate(object sender, DataGridViewCellMouseEventArgs e)
            {
                if (e.Button == MouseButtons.Right && e.RowIndex >= 0 && !value.Rows[e.RowIndex].Selected) { value.ClearSelection(); value.Rows[e.RowIndex].Selected = true; }
            };
            value.DragEnter += FileDragEnter;
            value.DragDrop += FileDragDrop;
            return value;
        }

        private void ShowRecordColumnMenu(int columnIndex)
        {
            if (columnIndex < 1 || columnIndex > 4 || openingRecordColumnMenu || IsDisposed || grid == null || grid.IsDisposed || !grid.IsHandleCreated) return;
            if (activeRecordColumnMenu != null)
            {
                ContextMenuStrip existing = activeRecordColumnMenu;
                activeRecordColumnMenu = null;
                try { if (!existing.IsDisposed) existing.Close(ToolStripDropDownCloseReason.AppClicked); } catch (ObjectDisposedException) { }
                return;
            }
            openingRecordColumnMenu = true;
            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem ascending = new ToolStripMenuItem(Localizer.T("△ 升序排列", "△ Sort ascending")) { Checked = recordSortColumn == columnIndex && !recordSortDescending };
            ToolStripMenuItem descending = new ToolStripMenuItem(Localizer.T("▽ 降序排列", "▽ Sort descending")) { Checked = recordSortColumn == columnIndex && recordSortDescending };
            ascending.Click += delegate { recordSortColumn = columnIndex; recordSortDescending = false; RefreshGrid(); };
            descending.Click += delegate { recordSortColumn = columnIndex; recordSortDescending = true; RefreshGrid(); };
            menu.Items.Add(ascending); menu.Items.Add(descending); menu.Items.Add(new ToolStripSeparator());
            if (columnIndex == 1)
            {
                AddColumnFilterItem(menu, Localizer.T("全部时间", "All dates"), !columnTimeSince.HasValue, delegate { columnTimeSince = null; RefreshGrid(); });
                AddColumnFilterItem(menu, Localizer.T("今天", "Today"), columnTimeSince == DateTime.Today, delegate { columnTimeSince = DateTime.Today; RefreshGrid(); });
                DateTime seven = DateTime.Today.AddDays(-6);
                DateTime thirty = DateTime.Today.AddDays(-29);
                AddColumnFilterItem(menu, Localizer.T("最近 7 天", "Last 7 days"), columnTimeSince == seven, delegate { columnTimeSince = seven; RefreshGrid(); });
                AddColumnFilterItem(menu, Localizer.T("最近 30 天", "Last 30 days"), columnTimeSince == thirty, delegate { columnTimeSince = thirty; RefreshGrid(); });
            }
            else if (columnIndex == 2)
            {
                AddColumnFilterItem(menu, Localizer.T("全部来源", "All sources"), string.IsNullOrEmpty(columnSourceFilter), delegate { columnSourceFilter = null; RefreshGrid(); });
                foreach (string source in store.Snapshot().Select(x => x.SourceProcess).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(x => x).Take(40))
                {
                    string captured = source;
                    AddColumnFilterItem(menu, captured, string.Equals(columnSourceFilter, captured, StringComparison.CurrentCultureIgnoreCase), delegate { columnSourceFilter = captured; RefreshGrid(); });
                }
            }
            else if (columnIndex == 3)
            {
                AddColumnFilterItem(menu, Localizer.T("全部文档", "All documents"), string.IsNullOrEmpty(columnDocumentFilter), delegate { columnDocumentFilter = null; RefreshGrid(); });
                List<string> documents = store.Snapshot().Select(x => DisplayLibraryPath(library.GetPath(x.DocumentId))).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(x => x).Take(40).ToList();
                foreach (string document in documents)
                {
                    string captured = document;
                    AddColumnFilterItem(menu, captured, string.Equals(columnDocumentFilter, captured, StringComparison.CurrentCultureIgnoreCase), delegate { columnDocumentFilter = captured; RefreshGrid(); });
                }
            }
            else
            {
                ToolStripMenuItem contains = new ToolStripMenuItem(Localizer.T("⌕ 筛选包含文字…", "⌕ Filter text…"));
                contains.Click += delegate
                {
                    string value = Prompt.Show(Localizer.T("只显示文本内容中包含：", "Only show text containing:"), Localizer.T("文本列筛选", "Text filter"), columnTextFilter ?? "");
                    if (value == null) return;
                    columnTextFilter = value.Trim(); RefreshGrid();
                };
                menu.Items.Add(contains);
                AddColumnFilterItem(menu, Localizer.T("清除文本列筛选", "Clear text filter"), string.IsNullOrEmpty(columnTextFilter), delegate { columnTextFilter = null; RefreshGrid(); });
            }
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem clear = new ToolStripMenuItem(Localizer.T("× 清除本列筛选", "× Clear this column filter"));
            clear.Click += delegate { ClearRecordColumnFilter(columnIndex); RefreshGrid(); };
            menu.Items.Add(clear);
            menu.Closed += delegate
            {
                if (ReferenceEquals(activeRecordColumnMenu, menu)) activeRecordColumnMenu = null;
                try
                {
                    if (!IsDisposed && IsHandleCreated) BeginInvoke((MethodInvoker)delegate { if (!menu.IsDisposed) menu.Dispose(); });
                    else if (!menu.IsDisposed) menu.Dispose();
                }
                catch (InvalidOperationException) { if (!menu.IsDisposed) menu.Dispose(); }
            };
            try
            {
                UiStyle.ApplyTheme(menu, settings.AppTheme);
                Rectangle header = grid.GetCellDisplayRectangle(columnIndex, -1, true);
                activeRecordColumnMenu = menu;
                menu.Show(grid, new Point(Math.Max(0, header.Left), Math.Max(grid.ColumnHeadersHeight, header.Bottom)));
            }
            catch (ObjectDisposedException) { if (ReferenceEquals(activeRecordColumnMenu, menu)) activeRecordColumnMenu = null; if (!menu.IsDisposed) menu.Dispose(); }
            catch (InvalidOperationException) { if (ReferenceEquals(activeRecordColumnMenu, menu)) activeRecordColumnMenu = null; if (!menu.IsDisposed) menu.Dispose(); }
            finally { openingRecordColumnMenu = false; }
        }

        private static void AddColumnFilterItem(ContextMenuStrip menu, string text, bool selected, EventHandler clicked)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text) { Checked = selected };
            item.Click += clicked; menu.Items.Add(item);
        }

        private void ClearRecordColumnFilter(int columnIndex)
        {
            if (columnIndex == 1) columnTimeSince = null;
            else if (columnIndex == 2) columnSourceFilter = null;
            else if (columnIndex == 3) columnDocumentFilter = null;
            else if (columnIndex == 4) columnTextFilter = null;
        }

        private Panel BuildRecordSearchPanel()
        {
            Panel panel = new Panel { Name = "RecordsSearchPanel", Dock = DockStyle.Fill, Padding = new Padding(10, 8, 8, 7), BackColor = Color.FromArgb(247, 249, 252) };
            FlowLayoutPanel flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, FlowDirection = FlowDirection.LeftToRight, Padding = Padding.Empty };
            searchScope.Width = 110; searchScope.Height = 32; searchScope.Margin = new Padding(0, 0, 8, 0);
            search.Width = 180; search.Height = 32; search.Margin = new Padding(0, 0, 8, 0);
            search.HandleCreated += delegate { NativeMethods.SetCueBanner(search.Handle, Localizer.T("输入搜索内容…", "Search records…")); };
            categoryFilter.Width = 130; categoryFilter.Height = 32; categoryFilter.Margin = new Padding(0, 0, 8, 0);
            Button ai = CreateSearchButton(Localizer.T("AI搜索", "AI Search"), Localizer.T("说明搜索 / AI 描述搜索", "Semantic search / AI search"), 112);
            Button clear = CreateSearchButton("×", Localizer.T("清除本地筛选", "Clear local filters"), 42);
            ContextMenuStrip batchMenu = new ContextMenuStrip();
            batchMenu.Items.Add(Localizer.T("☑ 全选当前结果", "☑ Check all results"), null, delegate { MarkVisibleItems(true); });
            batchMenu.Items.Add(Localizer.T("☐ 清除全部勾选", "☐ Clear all checks"), null, delegate { MarkVisibleItems(false); });
            batchMenu.Items.Add(new ToolStripSeparator());
            batchMenu.Items.Add(Localizer.T("📁 移动勾选条目", "📁 Move checked items"), null, delegate { MoveSelectedToLibraryNode(); });
            batchMenu.Items.Add(Localizer.T("🏷 修改勾选主题", "🏷 Edit checked topic"), null, delegate { EditCategory(); });
            batchMenu.Items.Add(Localizer.T("⇩ 导出勾选条目", "⇩ Export checked items"), null, delegate { Export("selected"); });
            batchMenu.Items.Add(new ToolStripSeparator());
            batchMenu.Items.Add(Localizer.T("🗑 删除勾选条目", "🗑 Delete checked items"), null, delegate { DeleteSelected(); });
            Button batch = CreateSearchButton(Localizer.T("已选操作 ▼", "Checked ▼"), Localizer.T("批量操作", "Batch actions"), Localizer.IsEnglish ? 150 : 138);
            ai.Click += delegate { OpenAiAssistant(true); }; clear.Click += delegate { ClearLocalSearch(); };
            batch.Click += delegate { batchMenu.Show(batch, new Point(0, batch.Height)); };
            UiStyle.ApplyTheme(batchMenu, settings.AppTheme);
            flow.Controls.Add(searchScope); flow.Controls.Add(search); flow.Controls.Add(categoryFilter); flow.Controls.Add(ai); flow.Controls.Add(clear); flow.Controls.Add(batch);
            panel.Controls.Add(flow);
            recordSearchPanel = panel; aiSearchButton = ai; clearSearchButton = clear; batchActionButton = batch;
            return panel;
        }

        private static Button CreateSearchButton(string text, string tooltip, int width)
        {
            Button button = new Button { Text = text, Width = width, Height = 32, Margin = new Padding(0, 0, 8, 0), FlatStyle = FlatStyle.Flat };
            button.FlatAppearance.BorderColor = Color.FromArgb(205, 210, 218); ToolTip tip = new ToolTip(); tip.SetToolTip(button, tooltip); return button;
        }

        private RichTextBox BuildViewerPanel(Control parent, out Label title, out Label meta, out Label reference)
        {
            Panel head = new Panel { Name = "ViewerPaneHeader", Dock = DockStyle.Fill, BackColor = Color.FromArgb(247, 249, 252), Padding = new Padding(14, 7, 8, 6) };
            Panel textHead = new Panel { Dock = DockStyle.Fill };
            title = new Label { Text = "查看", Dock = DockStyle.Top, Height = 48, Font = new Font(Font, FontStyle.Bold), AutoEllipsis = false, TextAlign = ContentAlignment.MiddleLeft };
            meta = new Label { Text = "请选择一条记录", Dock = DockStyle.Fill, ForeColor = Color.DimGray, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
            textHead.Controls.Add(meta); textHead.Controls.Add(title);
            RichTextBox value = new RichTextBox { Name = "ViewerText", Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, ReadOnly = true, DetectUrls = true, HideSelection = false, Font = new Font("Microsoft YaHei UI", 10.5F), BackColor = Color.White, WordWrap = true };
            Panel referencePanel = new Panel { Name = "ViewerReferencePanel", Dock = DockStyle.Fill, BackColor = Color.FromArgb(247, 249, 252), Padding = new Padding(12, 8, 12, 6) };
            Panel referenceLine = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Color.FromArgb(170, 176, 186) };
            reference = new Label { Dock = DockStyle.Fill, Text = "参考来源：请选择一条记录", ForeColor = Color.FromArgb(75, 81, 92), AutoEllipsis = false, TextAlign = ContentAlignment.MiddleLeft };
            referencePanel.Controls.Add(reference); referencePanel.Controls.Add(referenceLine); referenceLine.BringToFront();
            viewerAnnotationPanel = new Panel { Name = "ViewerAnnotationPanel", Dock = DockStyle.Fill, Padding = new Padding(10, 4, 6, 4), Visible = false };
            viewerAnnotationList = new FlowLayoutPanel { Name = "ViewerAnnotationList", Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false, FlowDirection = FlowDirection.TopDown, Padding = new Padding(0, 0, SystemInformation.VerticalScrollBarWidth + 8, 0), Margin = Padding.Empty };
            viewerAnnotationList.SizeChanged += delegate { ResizeViewerAnnotationCards(); };
            viewerAnnotationPanel.Controls.Add(viewerAnnotationList);

            viewerLayout = new TableLayoutPanel { Name = "ViewerPane", Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
            viewerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 112)); viewerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); viewerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0)); viewerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 116));
            viewerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            ContextMenuStrip actionsMenu = new ContextMenuStrip();
            actionsMenu.Items.Add(Localizer.T("打开原资料并定位", "Open source and jump"), null, delegate { OpenSelectedRecordSource(); });
            actionsMenu.Items.Add("编辑内容…", null, delegate { EditSelected(); });
            actionsMenu.Items.Add(new ToolStripSeparator());
            actionsMenu.Items.Add(Localizer.T("🖍 荧光笔", "🖍 Highlight"), null, delegate { FormatViewer("highlight"); });
            actionsMenu.Items.Add(Localizer.T("U̲ 下划线", "U̲ Underline"), null, delegate { FormatViewer("underline"); });
            actionsMenu.Items.Add(Localizer.T("📝 添加批注", "📝 Add annotation"), null, delegate { AnnotateViewer(); });
            actionsMenu.Items.Add(Localizer.T("✨ AI 整理排版", "✨ AI tidy layout"), null, async delegate { await OrganizeViewerLayoutAsync(); });
            actionsMenu.Items.Add(Localizer.T("✨ 请解释", "✨ Explain with AI"), null, delegate { OpenViewerExplanation(); });
            actionsMenu.Items.Add(Localizer.T("⌫ 清除格式", "⌫ Clear format"), null, delegate { FormatViewer("clear"); });
            actionsMenu.Items.Add(new ToolStripSeparator());
            actionsMenu.Items.Add(Localizer.T("📋 复制所选文字", "📋 Copy selection"), null, delegate { if (value.SelectionLength > 0) Clipboard.SetText(value.SelectedText); });
            Button actions = CreateHeaderMenuButton(actionsMenu);
            TableLayoutPanel headerLayout = CreateHeaderLayout();
            headerLayout.Controls.Add(textHead, 0, 0); headerLayout.Controls.Add(actions, 1, 0);
            head.Controls.Add(headerLayout);
            head.Dock = DockStyle.Fill;
            viewerLayout.Controls.Add(head, 0, 0); viewerLayout.Controls.Add(value, 0, 1); viewerLayout.Controls.Add(viewerAnnotationPanel, 0, 2); viewerLayout.Controls.Add(referencePanel, 0, 3); parent.Controls.Add(viewerLayout);
            value.ContextMenuStrip = actionsMenu;
            value.DoubleClick += delegate { OpenSelectedRecordSource(); };
            value.LinkClicked += delegate(object sender, LinkClickedEventArgs e) { try { System.Diagnostics.Process.Start(e.LinkText); } catch { } };
            return value;
        }

        private static Button CreateHeaderMenuButton(ContextMenuStrip menu)
        {
            Button button = new Button { Text = "▼", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, BackColor = Color.White, Margin = new Padding(2, 2, 0, 2), Font = new Font("Segoe UI Symbol", 8.5F), AccessibleName = Localizer.T("操作菜单", "Action menu") };
            button.FlatAppearance.BorderColor = Color.FromArgb(205, 210, 218);
            button.Click += delegate { menu.Show(button, new Point(0, button.Height)); };
            return button;
        }

        private static TableLayoutPanel CreateHeaderLayout()
        {
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            return layout;
        }

        private float InterfaceScale()
        {
            return settings.UiFontSize == "small" ? 0.9F : settings.UiFontSize == "large" ? 1.15F : 1F;
        }

        private void ApplyResponsiveLayout()
        {
            float scale = InterfaceScale();
            int menuHeight = (int)Math.Round(46 * scale);
            mainTools.Height = menuHeight;
            mainTools.Padding = new Padding(8, Math.Max(6, (int)Math.Round(8 * scale)), 8, Math.Max(6, (int)Math.Round(8 * scale)));
            foreach (ToolStripItem item in mainTools.Items) item.Padding = new Padding((int)Math.Round(10 * scale), (int)Math.Round(5 * scale), (int)Math.Round(10 * scale), (int)Math.Round(5 * scale));
            shell.RowStyles[0].Height = menuHeight;
            libraryLayout.RowStyles[0].Height = (int)Math.Round(58 * scale);
            recordLayout.RowStyles[0].Height = (int)Math.Round(64 * scale);
            recordLayout.RowStyles[1].Height = settings.UiFontSize == "large" ? 138 : settings.UiFontSize == "small" ? 112 : 124;
            viewerLayout.RowStyles[0].Height = (int)Math.Round(118 * scale);
            viewerLayout.RowStyles[3].Height = (int)Math.Round(120 * scale);
            if (viewerAnnotationPanel.Visible) viewerLayout.RowStyles[2].Height = (int)Math.Round(220 * scale);
            selectAllRecords.Width = Localizer.IsEnglish ? (int)Math.Round(170 * scale) : (int)Math.Round(120 * scale);
            leftCollapseHandle.Width = (int)Math.Round(42 * scale);
            searchScope.Width = 160;
            search.Width = 125;
            categoryFilter.Width = 205;
            aiSearchButton.Width = 148;
            batchActionButton.Width = 170;
            clearSearchButton.Width = 40;
            foreach (Button button in new Button[] { aiSearchButton, batchActionButton, clearSearchButton }) button.Height = (int)Math.Round(38 * scale);
            recordSearchPanel.Padding = new Padding(10, Math.Max(8, (int)Math.Round(10 * scale)), 8, 7);
        }

        private void BalanceInitialLayout()
        {
            try
            {
                int left = Math.Max(320, Math.Min(420, ClientSize.Width * 25 / 100));
                if (left < leftSplit.Width - leftSplit.Panel2MinSize - leftSplit.SplitterWidth) leftSplit.SplitterDistance = left;
                int available = contentSplit.ClientSize.Width;
                int middle = Math.Max(contentSplit.Panel1MinSize, Math.Min(available - contentSplit.Panel2MinSize - contentSplit.SplitterWidth, available * 58 / 100));
                if (middle > 0) contentSplit.SplitterDistance = middle;
            }
            catch { }
        }

        private void MigrateAnnotationRecords()
        {
            AnnotationStore annotations = new AnnotationStore();
            List<LibraryNode> nodes = library.Snapshot();
            List<ClipboardItem> existing = store.Snapshot();
            string[] oldReaderNames = new string[] { "简记阅读器", "剪迹阅读器", "剪痕阅读器", "ClipboardTrail Reader", "Clypsera Reader" };
            foreach (ClipboardItem oldRecord in existing.Where(x => oldReaderNames.Contains(x.SourceProcess))) store.UpdateSource(oldRecord.Id, "Clypsera 阅读器", oldRecord.SourceTitle);
            HashSet<string> historyIds = new HashSet<string>(existing.Select(x => x.Id));
            foreach (DocumentAnnotation annotation in annotations.All())
            {
                if (!string.IsNullOrEmpty(annotation.HistoryItemId) && historyIds.Contains(annotation.HistoryItemId)) continue;
                LibraryNode owner = nodes.FirstOrDefault(x => x.Id == annotation.NodeId);
                if (owner == null) continue;
                ClipboardItem record = store.AddAt(AnnotationStore.BuildRecordText(annotation), new SourceInfo { ProcessName = "Clypsera 阅读器", WindowTitle = owner.Name }, 50000, annotation.CreatedAt);
                if (record == null) continue;
                store.UpdateCategory(record.Id, "图书批注", false);
                store.UpdateDocument(record.Id, owner.Id);
                annotations.LinkHistory(annotation.Id, record.Id);
                historyIds.Add(record.Id);
            }
        }

        private void OpenLocalSearch(string scope)
        {
            string englishScope = scope == "书名" ? "book title" : scope == "文件名" ? "file name" : "keyword";
            string value = Prompt.Show(Localizer.IsEnglish ? "Enter the " + englishScope + " to find:" : "输入要查找的" + scope + "：", Localizer.IsEnglish ? "Local search" : scope + "搜索", search.Text);
            if (value == null) return;
            SelectSearchScope(scope);
            search.Text = value.Trim();
        }

        private void PopulateTopicMenu(ToolStripMenuItem topicMenu)
        {
            topicMenu.DropDownItems.Clear();
            foreach (object value in categoryFilter.Items)
            {
                string category = Convert.ToString(value);
                ToolStripMenuItem item = new ToolStripMenuItem(Localizer.Translate(category)) { Checked = Convert.ToString(categoryFilter.SelectedItem) == category };
                item.Click += delegate { categoryFilter.SelectedItem = category; };
                topicMenu.DropDownItems.Add(item);
            }
            if (topicMenu.DropDownItems.Count == 0) topicMenu.DropDownItems.Add(new ToolStripMenuItem("暂无主题") { Enabled = false });
            UiStyle.ApplyTheme(topicMenu.DropDown, settings.AppTheme);
        }

        private void ClearLocalSearch()
        {
            aiSearchIds = null;
            search.Clear();
            columnTimeSince = null; columnSourceFilter = null; columnDocumentFilter = null; columnTextFilter = null;
            if (categoryFilter.Items.Contains(AllTopicsText())) categoryFilter.SelectedItem = AllTopicsText();
            RefreshGrid();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_CLIPBOARDUPDATE)
            {
                if (ignoreNextClipboard) ignoreNextClipboard = false;
                else if (!paused) { pendingSource = NativeMethods.GetForegroundSource(); captureTimer.Stop(); captureTimer.Start(); }
            }
            else if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam.ToInt32() == SafePasteHotkeyId) SafePasteAsync();
            base.WndProc(ref m);
        }

        private void CaptureTimerTick(object sender, EventArgs e)
        {
            captureTimer.Stop();
            try
            {
                if (!CapturePolicy.Allows(pendingSource, settings)) return;
                if (!Clipboard.ContainsText()) return;
                string text = Clipboard.GetText(TextDataFormat.UnicodeText);
                if (string.IsNullOrEmpty(text)) return;
                ClipboardItem item = store.Add(text, pendingSource, settings.MaxTextLength);
                if (settings.OverlayVisible) overlay.UpdateItem(item, false);
                RefreshCategories(); RebuildTree(CurrentTreeKey()); RefreshGrid();
                if (settings.DeepSeekEnabled) BeginAiClassification(item);
            }
            catch { captureTimer.Interval = 300; captureTimer.Start(); }
            finally { captureTimer.Interval = 100; }
        }

        private async void BeginAiClassification(ClipboardItem item)
        {
            string aiSource = ((item.SourceProcess ?? "") + " " + (item.SourceTitle ?? "")).ToLowerInvariant();
            if (TextSafety.FindSensitiveReason(item.Text) != null || TextSafety.LooksLikeStandaloneCredential(item.Text) || aiSource.Contains("password") || aiSource.Contains("keepass") || aiSource.Contains("bitwarden") || aiSource.Contains("1password") || aiSource.Contains("密码")) return;
            try
            {
                AiRouteResult result = await new DeepSeekClient(settings).ClassifyAndRouteAsync(item.Text, library.GetDocumentChoices());
                store.UpdateCategory(item.Id, result.Category, true);
                if (!string.IsNullOrEmpty(result.DocumentId)) store.UpdateDocument(item.Id, result.DocumentId);
                RefreshCategories(); RebuildTree(CurrentTreeKey()); RefreshGrid(); if (settings.OverlayVisible) overlay.UpdateItem(store.Snapshot().LastOrDefault(), paused);
            }
            catch { }
        }

        private List<ClipboardItem> FilteredItems()
        {
            IEnumerable<ClipboardItem> query = store.Snapshot().OrderByDescending(x => x.CapturedAt);
            object tag = tree.SelectedNode == null ? AllTag : tree.SelectedNode.Tag;
            string special = tag as string;
            LibraryNode selectedNode = tag as LibraryNode;
            if (special == UnfiledTag) query = query.Where(x => string.IsNullOrEmpty(x.DocumentId));
            else if (selectedNode != null && selectedNode.IsDocument) query = query.Where(x => x.DocumentId == selectedNode.Id);
            else if (selectedNode != null && selectedNode.IsFolder)
            {
                HashSet<string> ids = new HashSet<string>(library.GetNodeIdsInBranch(selectedNode.Id));
                query = query.Where(x => !string.IsNullOrEmpty(x.DocumentId) && ids.Contains(x.DocumentId));
            }
            if (aiSearchIds != null) query = query.Where(x => aiSearchIds.Contains(x.Id));
            string q = search.Text.Trim();
            if (q.Length > 0)
            {
                string scope = SelectedSearchScope();
                List<LibraryNode> nodes = library.Snapshot();
                if (scope == "书名")
                {
                    HashSet<string> books = new HashSet<string>(nodes.Where(x => x.IsBook && Contains(x.Name, q)).Select(x => x.Id));
                    query = query.Where(x => books.Contains(x.DocumentId) || Contains(x.SourceTitle, q));
                }
                else if (scope == "文件名")
                {
                    HashSet<string> documents = new HashSet<string>(nodes.Where(x => x.IsDocument && Contains(library.GetPath(x.Id), q)).Select(x => x.Id));
                    query = query.Where(x => documents.Contains(x.DocumentId) || Contains(x.SourceTitle, q));
                }
                else query = query.Where(x => Contains(x.Text, q) || Contains(x.SourceProcess, q) || Contains(x.SourceTitle, q) || Contains(x.Category, q) || Contains(library.GetPath(x.DocumentId), q));
            }
            string category = categoryFilter.SelectedItem as string;
            if (!string.IsNullOrEmpty(category) && category != AllTopicsText()) query = query.Where(x => x.Category == category);
            if (columnTimeSince.HasValue) query = query.Where(x => x.CapturedAt >= columnTimeSince.Value);
            if (!string.IsNullOrWhiteSpace(columnSourceFilter)) query = query.Where(x => string.Equals(x.SourceProcess, columnSourceFilter, StringComparison.CurrentCultureIgnoreCase));
            if (!string.IsNullOrWhiteSpace(columnDocumentFilter)) query = query.Where(x => string.Equals(DisplayLibraryPath(library.GetPath(x.DocumentId)), columnDocumentFilter, StringComparison.CurrentCultureIgnoreCase));
            if (!string.IsNullOrWhiteSpace(columnTextFilter)) query = query.Where(x => Contains(x.Text, columnTextFilter));
            Func<ClipboardItem, object> key;
            if (recordSortColumn == 2) key = x => x.SourceProcess ?? "";
            else if (recordSortColumn == 3) key = x => DisplayLibraryPath(library.GetPath(x.DocumentId));
            else if (recordSortColumn == 4) key = x => x.Text ?? "";
            else key = x => x.CapturedAt;
            query = recordSortDescending ? query.OrderByDescending(key) : query.OrderBy(key);
            return query.ToList();
        }

        private static bool Contains(string value, string query) { return (value ?? "").IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0; }

        private void RefreshGrid()
        {
            string selected = SelectedItem() == null ? null : SelectedItem().Id;
            List<ClipboardItem> filtered = FilteredItems();
            UpdateRecordColumnHeaders();
            checkedItemIds.RemoveWhere(id => !store.Snapshot().Any(x => x.Id == id));
            grid.DataSource = filtered.Select(x => new ClipboardRow(x, DisplayLibraryPath(library.GetPath(x.DocumentId)), checkedItemIds.Contains(x.Id))).ToList();
            if (recordHeaderLabel != null)
            {
                List<string> filters = new List<string>();
                if (search.TextLength > 0) filters.Add(SelectedSearchScope() + "：“" + search.Text + "”");
                string topic = categoryFilter.SelectedItem as string;
                if (!string.IsNullOrEmpty(topic) && topic != AllTopicsText()) filters.Add("主题：“" + topic + "”");
                if (aiSearchIds != null) filters.Add("AI 搜索结果");
                recordHeaderBaseText = Localizer.IsEnglish
                    ? "Clipboard Records  ·  " + filtered.Count + " items" + (filters.Count == 0 ? "" : "  ·  filtered")
                    : "剪贴板记录  ·  " + filtered.Count + " 条" + (filters.Count == 0 ? "" : "  ·  " + string.Join("，", filters.ToArray()));
                recordHeaderLabel.Text = recordHeaderBaseText;
                UpdateRecordHeaderCount();
            }
            if (selected != null)
                foreach (DataGridViewRow row in grid.Rows)
                {
                    ClipboardRow value = row.DataBoundItem as ClipboardRow;
                    if (value != null && value.Item.Id == selected) { row.Selected = true; break; }
                }
            UpdateViewer();
        }

        private void UpdateRecordColumnHeaders()
        {
            if (grid == null || grid.Columns.Count < 5) return;
            grid.Columns[1].HeaderText = Localizer.T("时间", "Time") + (columnTimeSince.HasValue ? " •" : "") + " ▾";
            grid.Columns[2].HeaderText = Localizer.T("来源", "Source") + (!string.IsNullOrWhiteSpace(columnSourceFilter) ? " •" : "") + " ▾";
            grid.Columns[3].HeaderText = Localizer.T("文档", "Document") + (!string.IsNullOrWhiteSpace(columnDocumentFilter) ? " •" : "") + " ▾";
            grid.Columns[4].HeaderText = Localizer.T("文本内容", "Text") + (!string.IsNullOrWhiteSpace(columnTextFilter) ? " •" : "") + " ▾";
        }

        private void UpdateRecordHeaderCount()
        {
            if (recordHeaderLabel == null) return;
            string checkedText = checkedItemIds.Count == 0 ? "" : Localizer.IsEnglish ? "  ·  " + checkedItemIds.Count + " checked" : "  ·  已勾选 " + checkedItemIds.Count + " 条";
            recordHeaderLabel.Text = recordHeaderBaseText + checkedText;
            if (selectAllRecords != null)
            {
                List<ClipboardItem> visible = FilteredItems();
                updatingSelectAll = true;
                selectAllRecords.Checked = visible.Count > 0 && visible.All(x => checkedItemIds.Contains(x.Id));
                updatingSelectAll = false;
            }
        }

        private void RefreshCategories()
        {
            string current = categoryFilter.SelectedItem as string;
            if (current == AllTopicsText()) current = null;
            List<string> values = store.Snapshot().Select(x => x.Category).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().OrderBy(x => x).ToList();
            values.Insert(0, AllTopicsText());
            categoryFilter.BeginUpdate(); categoryFilter.Items.Clear(); categoryFilter.Items.AddRange(values.Cast<object>().ToArray());
            categoryFilter.SelectedItem = current != null && values.Contains(current) ? current : AllTopicsText(); categoryFilter.EndUpdate();
        }

        private ClipboardItem SelectedItem()
        {
            if (grid.SelectedRows.Count == 0) return null;
            ClipboardRow row = grid.SelectedRows[0].DataBoundItem as ClipboardRow;
            return row == null ? null : row.Item;
        }

        private List<ClipboardItem> SelectedItems()
        {
            if (checkedItemIds.Count > 0) return store.Snapshot().Where(x => checkedItemIds.Contains(x.Id)).OrderByDescending(x => x.CapturedAt).ToList();
            return grid.SelectedRows.Cast<DataGridViewRow>()
                .Select(x => x.DataBoundItem as ClipboardRow)
                .Where(x => x != null)
                .Select(x => x.Item)
                .OrderByDescending(x => x.CapturedAt)
                .ToList();
        }

        private string AllTopicsText() { return Localizer.T("全部主题", "All topics"); }

        private string SelectedSearchScope()
        {
            string value = Convert.ToString(searchScope.SelectedItem);
            if (value == "书名" || value == "Book title") return "书名";
            if (value == "文件名" || value == "File name") return "文件名";
            return "关键词";
        }

        private void SelectSearchScope(string scope)
        {
            for (int i = 0; i < searchScope.Items.Count; i++)
            {
                string value = Convert.ToString(searchScope.Items[i]);
                if ((scope == "书名" && (value == "书名" || value == "Book title")) || (scope == "文件名" && (value == "文件名" || value == "File name")) || (scope == "关键词" && (value == "关键词" || value == "Keyword"))) { searchScope.SelectedIndex = i; return; }
            }
        }

        private void MarkVisibleItems(bool mark)
        {
            if (mark) foreach (ClipboardItem item in FilteredItems()) checkedItemIds.Add(item.Id); else checkedItemIds.Clear();
            RefreshGrid();
        }

        private void UpdateViewer()
        {
            ClipboardItem item = SelectedItem();
            if (item == null) { currentViewerItemId = null; viewer.Clear(); viewerTitle.Text = Localizer.T("查看", "Viewer"); viewerMeta.Text = Localizer.T("请选择一条记录", "Select a record"); referenceLabel.Text = Localizer.T("参考来源：请选择一条记录", "Reference: select a record"); UpdateViewerAnnotationCard(null); return; }
            if (currentViewerItemId != item.Id) { currentViewerItemId = item.Id; expandedViewerAnnotations.Clear(); }
            viewerTitle.Text = CompactLibraryPath(DisplayLibraryPath(library.GetPath(item.DocumentId)));
            viewerMeta.Text = item.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss") + "  ·  " + item.SourceProcess + (string.IsNullOrWhiteSpace(item.SourceTitle) ? "" : "  ·  " + item.SourceTitle);
            string sourceTitle = string.IsNullOrWhiteSpace(item.SourceTitle) ? "未提供文件或窗口标题" : "《" + item.SourceTitle + "》";
            referenceLabel.Text = Localizer.IsEnglish
                ? "Reference  [1] " + (string.IsNullOrWhiteSpace(item.SourceTitle) ? "Untitled source" : item.SourceTitle) + "; app: " + item.SourceProcess + "; captured: " + item.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss") + "; filed in: " + DisplayLibraryPath(library.GetPath(item.DocumentId))
                : "参考来源  [1] " + sourceTitle + "；应用：" + item.SourceProcess + "；采集：" + item.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss") + "；归档：" + library.GetPath(item.DocumentId);
            UpdateViewerAnnotationCard(item);
            if (!string.IsNullOrWhiteSpace(item.Rtf))
            {
                try { viewer.Rtf = item.Rtf; AdaptViewerHighlightColors(item); return; }
                catch { }
            }
            viewer.Text = item.Text ?? "";
            AdaptViewerHighlightColors(item);
        }

        private void UpdateViewerAnnotationCard(ClipboardItem item)
        {
            if (viewerAnnotationPanel == null || viewerLayout == null) return;
            List<TextAnnotation> notes = ItemAnnotations(item);
            bool hasNotes = notes.Count > 0;
            viewerAnnotationPanel.Visible = hasNotes;
            viewerLayout.RowStyles[2].Height = hasNotes ? (int)Math.Round(220 * InterfaceScale()) : 0;
            viewerAnnotationList.SuspendLayout(); viewerAnnotationList.Controls.Clear();
            foreach (TextAnnotation annotation in notes) viewerAnnotationList.Controls.Add(BuildViewerAnnotationCard(annotation));
            viewerAnnotationList.ResumeLayout(); ResizeViewerAnnotationCards();
        }

        private static List<TextAnnotation> ItemAnnotations(ClipboardItem item)
        {
            if (item == null) return new List<TextAnnotation>();
            if (item.Annotations != null && item.Annotations.Count > 0) return item.Annotations.Where(x => x != null && !string.IsNullOrWhiteSpace(x.Note)).ToList();
            return string.IsNullOrWhiteSpace(item.Note) ? new List<TextAnnotation>() : new List<TextAnnotation> { new TextAnnotation { Id = "legacy-" + item.Id, Quote = item.NoteQuote ?? "", Note = item.Note, CreatedAt = item.UpdatedAt ?? item.CapturedAt } };
        }

        private Panel BuildViewerAnnotationCard(TextAnnotation annotation)
        {
            bool expanded = expandedViewerAnnotations.Contains(annotation.Id);
            Panel card = new Panel { Height = expanded ? 142 : 42, Margin = new Padding(0, 0, 0, 7), BorderStyle = BorderStyle.FixedSingle, Tag = annotation.Id };
            Panel head = new Panel { Dock = DockStyle.Top, Height = 40 };
            string quote = string.IsNullOrWhiteSpace(annotation.Quote) ? Localizer.T("整条记录", "Whole record") : "“" + CompactText(annotation.Quote, 42) + "”";
            Button toggle = new Button { Text = "📝 " + quote + (expanded ? "  ▴" : Localizer.T(" · 点击展开  ▾", " · Expand  ▾")), Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(7, 0, 3, 0) };
            Button explain = new Button { Text = "✨", Dock = DockStyle.Right, Width = 40, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI Emoji", 9F) };
            Button edit = new Button { Text = "✎", Dock = DockStyle.Right, Width = 40, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI Symbol", 10F) };
            Button delete = new Button { Text = "×", Dock = DockStyle.Right, Width = 40, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI Symbol", 10F) };
            toggle.FlatAppearance.BorderSize = 0; explain.FlatAppearance.BorderSize = 0; edit.FlatAppearance.BorderSize = 0; delete.FlatAppearance.BorderSize = 0;
            string annotationId = annotation.Id;
            toggle.Click += delegate { ToggleViewerAnnotation(annotationId); };
            explain.Click += delegate { OpenViewerAnnotationExplanation(annotationId); };
            edit.Click += delegate { EditViewerAnnotation(annotationId); };
            delete.Click += delegate { DeleteViewerAnnotation(annotationId); };
            head.Controls.Add(toggle); head.Controls.Add(delete); head.Controls.Add(edit); head.Controls.Add(explain);
            card.Controls.Add(head);
            if (expanded)
            {
                Label body = new Label { Dock = DockStyle.Fill, Padding = new Padding(9, 7, 9, 7), AutoEllipsis = false, TextAlign = ContentAlignment.TopLeft, Text = Localizer.IsEnglish ? "Quoted text: " + (string.IsNullOrWhiteSpace(annotation.Quote) ? "Whole record" : annotation.Quote) + "\n\nAnnotation: " + annotation.Note : "批注原文：" + (string.IsNullOrWhiteSpace(annotation.Quote) ? "整条记录" : annotation.Quote) + "\n\n批注：" + annotation.Note };
                card.Controls.Add(body); body.BringToFront(); head.BringToFront();
            }
            return card;
        }

        private void ResizeViewerAnnotationCards()
        {
            if (viewerAnnotationList == null) return;
            int width = Math.Max(180, viewerAnnotationList.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - viewerAnnotationList.Padding.Right - 4);
            foreach (Control card in viewerAnnotationList.Controls) card.Width = width;
        }

        private void ToggleViewerAnnotation(string annotationId)
        {
            ClipboardItem item = SelectedItem(); if (item == null) return;
            TextAnnotation annotation = ItemAnnotations(item).FirstOrDefault(x => x.Id == annotationId); if (annotation == null) return;
            if (!expandedViewerAnnotations.Add(annotationId)) expandedViewerAnnotations.Remove(annotationId);
            UpdateViewerAnnotationCard(item);
            if (!string.IsNullOrWhiteSpace(annotation.Quote))
            {
                int index = viewer.Text.IndexOf(annotation.Quote, StringComparison.CurrentCultureIgnoreCase);
                if (index >= 0) { viewer.Select(index, annotation.Quote.Length); viewer.ScrollToCaret(); }
            }
        }

        private static string CompactText(string text, int max)
        {
            string value = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            return value.Length <= max ? value : value.Substring(0, max - 1) + "…";
        }

        private static string DisplayLibraryPath(string path)
        {
            if (!Localizer.IsEnglish || string.IsNullOrEmpty(path)) return path;
            return path == "未归档" ? "Unfiled" : path.StartsWith("未归档 / ") ? "Unfiled / " + path.Substring(6) : path;
        }

        private static string CompactLibraryPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            return string.Join(" / ", path.Split(new string[] { " / " }, StringSplitOptions.None).Select(CompactLibraryName));
        }

        private static string CompactLibraryName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return name;
            string[] archiveMarkers = new string[] { " (z-library", " (z-lib", " (1lib." };
            foreach (string marker in archiveMarkers)
            {
                int index = name.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (index > 0) return name.Substring(0, index).Trim();
            }
            return name;
        }

        private void FormatViewer(string mode)
        {
            ClipboardItem item = SelectedItem();
            if (item == null || viewer.SelectionLength == 0) { MessageBox.Show("请先在右栏选中一段文字。", "Clypsera", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            UndoService.Capture(Localizer.T("右栏文字格式", "viewer text formatting"), store, library, new AnnotationStore());
            if (mode == "highlight")
            {
                if (IsHighlightColor(viewer.SelectionBackColor)) { viewer.SelectionBackColor = viewer.BackColor; viewer.SelectionColor = ViewerTextColor(); }
                else { viewer.SelectionBackColor = ViewerHighlightColor(); viewer.SelectionColor = ViewerHighlightTextColor(); }
            }
            else
            {
                Font current = viewer.SelectionFont ?? viewer.Font;
                if (mode == "underline") viewer.SelectionFont = new Font(current, current.Style ^ FontStyle.Underline);
                else { viewer.SelectionBackColor = viewer.BackColor; viewer.SelectionColor = ViewerTextColor(); viewer.SelectionFont = new Font(current, current.Style & ~FontStyle.Underline); }
            }
            store.UpdateContent(item.Id, viewer.Text, viewer.Rtf); RefreshGrid();
        }

        private Color ViewerHighlightColor() { return settings.AppTheme == "dark" ? Color.FromArgb(92, 73, 12) : Color.FromArgb(255, 235, 59); }
        private Color ViewerHighlightTextColor() { return settings.AppTheme == "dark" ? Color.FromArgb(255, 247, 199) : Color.FromArgb(30, 41, 59); }
        private Color ViewerAnnotationColor() { return settings.AppTheme == "dark" ? Color.FromArgb(30, 58, 95) : Color.FromArgb(219, 234, 254); }
        private Color ViewerAnnotationTextColor() { return settings.AppTheme == "dark" ? Color.FromArgb(219, 234, 254) : Color.FromArgb(30, 64, 175); }
        private Color ViewerTextColor() { return settings.AppTheme == "dark" ? Color.FromArgb(229, 231, 235) : Color.FromArgb(30, 41, 59); }
        private static bool IsHighlightColor(Color color)
        {
            return color.ToArgb() == Color.Yellow.ToArgb() || color.ToArgb() == Color.FromArgb(255, 235, 59).ToArgb() || color.ToArgb() == Color.FromArgb(92, 73, 12).ToArgb();
        }

        private void AdaptViewerHighlightColors(ClipboardItem item)
        {
            if (viewer.TextLength == 0) return;
            int oldStart = viewer.SelectionStart, oldLength = viewer.SelectionLength;
            IntPtr viewerHandle = viewer.IsHandleCreated ? viewer.Handle : IntPtr.Zero;
            int oldFirstVisibleLine = NativeMethods.GetFirstVisibleLine(viewerHandle);
            Color targetBack = ViewerHighlightColor(), targetHighlightText = ViewerHighlightTextColor(), targetText = ViewerTextColor();
            NativeMethods.SuspendRedraw(viewerHandle);
            try
            {
                for (int i = 0; i < viewer.TextLength; i++)
                {
                    viewer.Select(i, 1);
                    if (IsHighlightColor(viewer.SelectionBackColor))
                    {
                        viewer.SelectionBackColor = targetBack;
                        viewer.SelectionColor = targetHighlightText;
                    }
                    else
                    {
                        viewer.SelectionBackColor = viewer.BackColor;
                        viewer.SelectionColor = targetText;
                    }
                }

                // Text annotations are metadata, so derive their blue marker each time.
                // This keeps them distinct from yellow highlights and also means deleting
                // an annotation removes its marker on the next refresh.
                foreach (TextAnnotation annotation in ItemAnnotations(item))
                {
                    string quote = (annotation.Quote ?? "").Trim();
                    if (quote.Length == 0) continue;
                    int index = viewer.Text.IndexOf(quote, StringComparison.CurrentCultureIgnoreCase);
                    if (index < 0) continue;
                    viewer.Select(index, quote.Length);
                    viewer.SelectionBackColor = ViewerAnnotationColor();
                    viewer.SelectionColor = ViewerAnnotationTextColor();
                }
            }
            finally
            {
                int safeStart = Math.Min(oldStart, viewer.TextLength);
                viewer.Select(safeStart, Math.Min(oldLength, viewer.TextLength - safeStart));
                NativeMethods.RestoreFirstVisibleLine(viewerHandle, oldFirstVisibleLine);
                NativeMethods.ResumeRedraw(viewerHandle);
                if (viewerHandle != IntPtr.Zero) viewer.Invalidate();
            }
        }

        private void AnnotateViewer()
        {
            ClipboardItem item = SelectedItem(); if (item == null) return;
            string quote = viewer.SelectionLength > 0 ? viewer.SelectedText.Trim() : "";
            string note = Prompt.Show(Localizer.T("请输入对所选文字或本条记录的批注：", "Enter an annotation for the selection or record:"), Localizer.T("添加文字批注", "Add text annotation"), "");
            if (note == null || string.IsNullOrWhiteSpace(note)) return;
            UndoService.Capture(Localizer.T("文字批注", "text annotation"), store, library, new AnnotationStore());
            store.AddAnnotation(item.Id, quote, note.Trim()); RefreshGrid();
        }

        private async Task OrganizeViewerLayoutAsync()
        {
            ClipboardItem item = SelectedItem();
            if (item == null) return;
            if (!settings.DeepSeekEnabled || string.IsNullOrWhiteSpace(AppData.UnprotectSecret(settings.ApiKeyEncrypted)))
            {
                MessageBox.Show(Localizer.T("请先在设置中启用 DeepSeek 并填写 API Key。", "Enable DeepSeek and enter an API key in Settings first."), "Clypsera", MessageBoxButtons.OK, MessageBoxIcon.Information);
                OpenSettings();
                return;
            }
            string original = viewer.Text ?? "";
            if (string.IsNullOrWhiteSpace(original)) return;
            if (TextSafety.FindSensitiveReason(original) != null || TextSafety.LooksLikeStandaloneCredential(original))
            {
                MessageBox.Show(Localizer.T("这条记录疑似包含密码、验证码或密钥，已阻止发送给 AI。", "This item may contain a password, verification code, or key, so it was not sent to AI."), "Clypsera", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string requestText = AiLayoutOrganizer.BuildPlanRequest(original);
            string response;
            string organized = null, validationError = null;
            OperationProgressForm progress = new OperationProgressForm(Localizer.T("AI 正在整理排版", "AI is tidying the layout"), settings.AppTheme);
            try
            {
                UseWaitCursor = true;
                progress.Show(this);
                progress.SetStage(8, 18, Localizer.T("正在拆分并锁定原文字词…", "Splitting and locking the original text…"));
                progress.SetStage(20, 68, Localizer.T("正在请 AI 规划段落（AI 只会看到带编号的原文块）…", "Asking AI to plan paragraphs from numbered immutable units…"));
                response = await new AiTextClient(settings).CompleteAsync(
                    AiLayoutOrganizer.SystemPrompt,
                    new List<AiMessage> { new AiMessage { Role = "user", Content = requestText } },
                    Math.Min(8192, Math.Max(1200, original.Length * 2)));
                progress.SetStage(72, 78, Localizer.T("正在核对编号完整性和顺序…", "Checking ID completeness and order…"));
                if (!AiLayoutOrganizer.TryApplyPlan(original, response, out organized, out validationError))
                {
                    progress.SetStage(45, 88, Localizer.T("AI 段落方案不完整，正在自动纠正…", "The paragraph plan was incomplete; correcting it automatically…"));
                    List<AiMessage> retry = new List<AiMessage>
                    {
                        new AiMessage { Role = "user", Content = requestText },
                        new AiMessage { Role = "assistant", Content = response },
                        new AiMessage { Role = "user", Content = "Your previous JSON plan was invalid: " + validationError + " Return only a corrected JSON array. Include every unit ID exactly once, in ascending order. Do not repeat any source text." }
                    };
                    response = await new AiTextClient(settings).CompleteAsync(
                        AiLayoutOrganizer.SystemPrompt,
                        retry,
                        Math.Min(8192, Math.Max(1200, original.Length * 2)));
                    progress.SetStage(90, 94, Localizer.T("正在复核纠正后的段落方案…", "Validating the corrected paragraph plan…"));
                    AiLayoutOrganizer.TryApplyPlan(original, response, out organized, out validationError);
                }
                progress.SetStage(96, 98, Localizer.T("正在用锁定的原文生成预览…", "Building the preview from locked original text…"));
            }
            catch (Exception ex)
            {
                MessageBox.Show(Localizer.T("AI 排版失败：", "AI layout failed: ") + ex.Message, "Clypsera", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            finally
            {
                UseWaitCursor = false;
                if (!progress.IsDisposed) progress.Close();
                progress.Dispose();
            }

            if (organized == null)
            {
                // The service only returns paragraph IDs. If those IDs are still
                // incomplete after one retry, use the local whitespace-only repair.
                // Source characters are never taken from the AI response.
                organized = AiLayoutOrganizer.NeedsImprovement(original) ? AiLayoutOrganizer.ImproveUnchangedLayout(original) : original.Replace("\r\n", "\n").Replace('\r', '\n');
            }
            string normalizedOriginal = original.Replace("\r\n", "\n").Replace('\r', '\n');
            if (string.Equals(normalizedOriginal, organized, StringComparison.Ordinal) && AiLayoutOrganizer.NeedsImprovement(original))
            {
                // Some models are overly conservative and echo visibly hard-wrapped PDF
                // text unchanged. Add safe paragraph boundaries locally in that case;
                // this fallback can only insert newline characters.
                organized = AiLayoutOrganizer.ImproveUnchangedLayout(original);
            }
            if (string.Equals(normalizedOriginal, organized, StringComparison.Ordinal))
            {
                MessageBox.Show(Localizer.T("当前排版无需调整。", "The current layout does not need changes."), "Clypsera", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            ClipboardItem stillSelected = SelectedItem();
            if (stillSelected == null || stillSelected.Id != item.Id)
            {
                MessageBox.Show(Localizer.T("等待 AI 时所选记录已经改变，本次排版没有应用。", "The selected item changed while AI was working, so the layout was not applied."), "Clypsera", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(Localizer.T("AI 已生成新排版。只会调整排版空白与段落，不会改变原文字词、标点、顺序或书内定位。是否应用？\n\n应用后可按 Ctrl+Z 撤销。", "AI has prepared a new layout. Only layout whitespace and paragraphs will change; wording, punctuation, order, and source navigation remain intact. Apply it?\n\nPress Ctrl+Z to undo afterward."), Localizer.T("应用 AI 排版", "Apply AI layout"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            try
            {
                UndoService.Capture(Localizer.T("AI 整理排版", "AI tidy layout"), store, library, new AnnotationStore());
                ApplyOrganizedViewerText(organized);
                store.UpdateContent(item.Id, viewer.Text, viewer.Rtf);
                RefreshGrid();
                MessageBox.Show(Localizer.T("排版已整理，原文字词和批注定位均已保留。", "Layout updated. The original wording and annotation navigation were preserved."), "Clypsera", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { MessageBox.Show(Localizer.T("应用排版失败：", "Could not apply layout: ") + ex.Message, "Clypsera", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void ApplyOrganizedViewerText(string organized)
        {
            IntPtr handle = viewer.IsHandleCreated ? viewer.Handle : IntPtr.Zero;
            int firstVisibleLine = NativeMethods.GetFirstVisibleLine(handle);
            NativeMethods.SuspendRedraw(handle);
            try
            {
                List<ViewerStyleRun> runs = CaptureViewerStyleRuns();
                viewer.Text = organized ?? "";
                List<int> positions = new List<int>();
                for (int i = 0; i < viewer.TextLength; i++) if (!char.IsWhiteSpace(viewer.Text[i])) positions.Add(i);
                foreach (ViewerStyleRun run in runs)
                {
                    if (run.LogicalStart < 0 || run.LogicalLength <= 0 || run.LogicalStart + run.LogicalLength > positions.Count) continue;
                    int start = positions[run.LogicalStart];
                    int end = positions[run.LogicalStart + run.LogicalLength - 1] + 1;
                    viewer.Select(start, end - start);
                    viewer.SelectionBackColor = run.BackColor;
                    viewer.SelectionColor = run.ForeColor;
                    using (Font font = new Font(run.FontName, run.FontSize, run.FontStyle)) viewer.SelectionFont = font;
                }
                viewer.Select(0, 0);
                NativeMethods.RestoreFirstVisibleLine(handle, firstVisibleLine);
            }
            finally
            {
                NativeMethods.ResumeRedraw(handle);
                if (handle != IntPtr.Zero) viewer.Invalidate();
            }
        }

        private List<ViewerStyleRun> CaptureViewerStyleRuns()
        {
            List<ViewerStyleRun> runs = new List<ViewerStyleRun>();
            ViewerStyleRun current = null;
            int logicalPosition = 0;
            for (int i = 0; i < viewer.TextLength; i++)
            {
                if (char.IsWhiteSpace(viewer.Text[i])) continue;
                viewer.Select(i, 1);
                Font font = viewer.SelectionFont ?? viewer.Font;
                Color back = viewer.SelectionBackColor;
                Color fore = viewer.SelectionColor;
                if (current == null || current.BackColor.ToArgb() != back.ToArgb() || current.ForeColor.ToArgb() != fore.ToArgb() || current.FontName != font.Name || Math.Abs(current.FontSize - font.Size) > 0.01F || current.FontStyle != font.Style)
                {
                    current = new ViewerStyleRun { LogicalStart = logicalPosition, LogicalLength = 0, BackColor = back, ForeColor = fore, FontName = font.Name, FontSize = font.Size, FontStyle = font.Style };
                    runs.Add(current);
                }
                current.LogicalLength++;
                logicalPosition++;
            }
            return runs;
        }

        private void EditViewerAnnotation(string annotationId)
        {
            ClipboardItem item = SelectedItem(); if (item == null) return;
            TextAnnotation annotation = ItemAnnotations(item).FirstOrDefault(x => x.Id == annotationId); if (annotation == null) return;
            string note = Prompt.Show(Localizer.T("修改这条批注：", "Edit this annotation:"), Localizer.T("编辑文字批注", "Edit annotation"), annotation.Note);
            if (note == null || string.IsNullOrWhiteSpace(note)) return;
            UndoService.Capture(Localizer.T("编辑文字批注", "edit text annotation"), store, library, new AnnotationStore());
            store.UpdateAnnotation(item.Id, annotationId, note); RefreshGrid();
        }

        private void DeleteViewerAnnotation(string annotationId)
        {
            ClipboardItem item = SelectedItem(); if (item == null) return;
            TextAnnotation annotation = ItemAnnotations(item).FirstOrDefault(x => x.Id == annotationId); if (annotation == null) return;
            if (MessageBox.Show(Localizer.T("确定删除这条文字批注吗？高亮和下划线不会被删除。", "Delete this text annotation? Highlights and underlines will remain."), "Clypsera", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            UndoService.Capture(Localizer.T("删除文字批注", "delete text annotation"), store, library, new AnnotationStore());
            store.DeleteAnnotation(item.Id, annotationId); expandedViewerAnnotations.Remove(annotationId); RefreshGrid();
        }

        private void EditSelected()
        {
            ClipboardItem item = SelectedItem(); if (item == null) return;
            using (EditorForm form = new EditorForm(item))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                UndoService.Capture(Localizer.T("编辑记录", "edit record"), store, library, new AnnotationStore());
                store.UpdateContent(item.Id, form.ResultText, form.ResultRtf); RefreshGrid();
            }
        }

        private void CreateLibraryNode(string type)
        {
            string parentId = null;
            LibraryNode selected = tree.SelectedNode == null ? null : tree.SelectedNode.Tag as LibraryNode;
            if (selected != null) parentId = selected.IsFolder ? selected.Id : selected.ParentId;
            string kind = type == "folder" ? "文件夹" : type == "book" ? "书籍" : "文档";
            string englishKind = type == "folder" ? "folder" : type == "book" ? "book" : "document";
            string name = Prompt.Show(Localizer.IsEnglish ? "Enter the " + englishKind + " name:" : "请输入" + kind + "名称：", Localizer.IsEnglish ? "New " + englishKind : "新建" + kind, Localizer.IsEnglish ? "New " + englishKind : "新" + kind);
            if (string.IsNullOrWhiteSpace(name)) return;
            UndoService.Capture(Localizer.T("新建分类", "create library item"), store, library, new AnnotationStore());
            LibraryNode node = library.Add(name, type, parentId); RebuildTree(node.Id);
        }

        private void RenameLibraryNode()
        {
            LibraryNode node = tree.SelectedNode == null ? null : tree.SelectedNode.Tag as LibraryNode; if (node == null) return;
            string name = Prompt.Show(Localizer.T("请输入新名称：", "Enter a new name:"), Localizer.T("重命名", "Rename"), node.Name); if (string.IsNullOrWhiteSpace(name)) return;
            UndoService.Capture(Localizer.T("重命名", "rename"), store, library, new AnnotationStore());
            library.Rename(node.Id, name); RebuildTree(node.Id); RefreshGrid();
        }

        private void DeleteLibraryNode()
        {
            LibraryNode node = tree.SelectedNode == null ? null : tree.SelectedNode.Tag as LibraryNode; if (node == null) return;
            if (MessageBox.Show("确定删除“" + node.Name + "”及其下级结构吗？其中的记录会移回“未归档”。", "Clypsera", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            UndoService.Capture(Localizer.T("删除资料库项目", "delete library item"), store, library, new AnnotationStore());
            List<string> removed = library.DeleteBranch(node.Id);
            foreach (ClipboardItem item in store.Snapshot().Where(x => removed.Contains(x.DocumentId))) store.UpdateDocument(item.Id, null);
            ConfigureLiveFolderWatchers();
            RebuildTree(AllTag); RefreshGrid();
        }

        private void RebuildTree(string selectKey)
        {
            if (tree == null) return;
            rebuildingTree = true;
            try
            {
                List<LibraryNode> nodes = library.Snapshot(); List<ClipboardItem> items = store.Snapshot();
                tree.BeginUpdate(); tree.Nodes.Clear();
                TreeNode root = new TreeNode("▾ " + Localizer.T("我的资料库", "My Library") + "  (" + items.Count + ")") { Tag = AllTag, ToolTipText = Localizer.T("显示全部记录", "Show all records") };
                root.Nodes.Add(new TreeNode(Localizer.T("未归档", "Unfiled") + "  (" + items.Count(x => string.IsNullOrEmpty(x.DocumentId)) + ")") { Tag = UnfiledTag, ToolTipText = Localizer.T("尚未放入文档的记录", "Records not filed into a document") });
                AddTreeChildren(root.Nodes, null, nodes, items); tree.Nodes.Add(root); root.Expand();
                SelectTreeNode(tree.Nodes, selectKey); if (tree.SelectedNode == null) tree.SelectedNode = root; tree.EndUpdate();
            }
            finally { rebuildingTree = false; }
        }

        private void AddTreeChildren(TreeNodeCollection target, string parentId, List<LibraryNode> nodes, List<ClipboardItem> items)
        {
            foreach (LibraryNode node in nodes.Where(x => x.ParentId == parentId).OrderByDescending(x => x.IsFolder).ThenBy(x => x.Name))
            {
                HashSet<string> branch = node.IsDocument ? null : new HashSet<string>(library.GetNodeIdsInBranch(node.Id));
                int count = node.IsDocument ? items.Count(x => x.DocumentId == node.Id) : items.Count(x => !string.IsNullOrEmpty(x.DocumentId) && branch.Contains(x.DocumentId));
                string icon = node.IsLiveFolder ? "🔄 " : node.IsFolder ? "📁 " : node.IsBook ? "📚 " : "📄 ";
                string tooltip = library.GetPath(node.Id);
                if (node.IsLiveFolder) tooltip += "\n" + Localizer.T("活动文件夹 · 持续同步：", "Live folder · continuous sync: ") + node.SyncSourcePath;
                TreeNode value = new TreeNode(icon + CompactLibraryName(node.Name) + "  (" + count + ")") { Tag = node, ToolTipText = tooltip };
                target.Add(value); if (node.IsFolder) AddTreeChildren(value.Nodes, node.Id, nodes, items);
            }
        }

        private void SelectTreeNode(TreeNodeCollection nodes, string key)
        {
            foreach (TreeNode node in nodes)
            {
                LibraryNode value = node.Tag as LibraryNode;
                if ((value != null && value.Id == key) || (value == null && Convert.ToString(node.Tag) == key)) { tree.SelectedNode = node; EnsureParentsExpanded(node); return; }
                SelectTreeNode(node.Nodes, key); if (tree.SelectedNode != null) return;
            }
        }

        private static void EnsureParentsExpanded(TreeNode node) { TreeNode current = node.Parent; while (current != null) { current.Expand(); current = current.Parent; } }
        private string CurrentTreeKey() { if (tree == null || tree.SelectedNode == null) return AllTag; LibraryNode node = tree.SelectedNode.Tag as LibraryNode; return node == null ? Convert.ToString(tree.SelectedNode.Tag) : node.Id; }

        private void MoveSelectedToLibraryNode()
        {
            List<ClipboardItem> items = SelectedItems(); if (items.Count == 0) return;
            using (DocumentPickerForm form = new DocumentPickerForm(library.Snapshot(), items[0].DocumentId))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                UndoService.Capture(Localizer.T("移动记录", "move records"), store, library, new AnnotationStore());
                foreach (ClipboardItem item in items) store.UpdateDocument(item.Id, form.SelectedDocumentId);
                RebuildTree(CurrentTreeKey()); RefreshGrid();
            }
        }

        private void GridMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            DataGridView.HitTestInfo hit = grid.HitTest(e.X, e.Y); if (hit.RowIndex < 0) return;
            if (hit.ColumnIndex == 0) { ToggleGridCheck(hit.RowIndex); return; }
            bool extend = (ModifierKeys & (Keys.Control | Keys.Shift)) != Keys.None;
            if (!grid.Rows[hit.RowIndex].Selected && !extend) grid.ClearSelection();
            grid.Rows[hit.RowIndex].Selected = true;
            List<ClipboardItem> items = SelectedItems();
            if (items.Count > 0) grid.DoDragDrop(new ItemDragPayload(items.Select(x => x.Id).ToList()), DragDropEffects.Move);
        }

        private void ToggleGridCheck(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= grid.Rows.Count) return;
            ClipboardRow row = grid.Rows[rowIndex].DataBoundItem as ClipboardRow; if (row == null) return;
            row.Marked = !row.Marked;
            grid.Rows[rowIndex].Cells[0].Value = row.Marked;
            if (row.Marked) checkedItemIds.Add(row.Item.Id); else checkedItemIds.Remove(row.Item.Id);
            grid.InvalidateCell(0, rowIndex);
            UpdateRecordHeaderCount();
        }

        private void TreeItemDrag(object sender, ItemDragEventArgs e) { TreeNode node = e.Item as TreeNode; LibraryNode value = node == null ? null : node.Tag as LibraryNode; if (value != null) tree.DoDragDrop(new NodeDragPayload(value.Id), DragDropEffects.Move); }
        private void TreeDragEnter(object sender, DragEventArgs e) { UpdateTreeDragEffect(e); }
        private void TreeDragOver(object sender, DragEventArgs e) { Point point = tree.PointToClient(new Point(e.X, e.Y)); tree.SelectedNode = tree.GetNodeAt(point); UpdateTreeDragEffect(e); }
        private void UpdateTreeDragEffect(DragEventArgs e) { e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : e.Data.GetDataPresent(typeof(ItemDragPayload)) || e.Data.GetDataPresent(typeof(NodeDragPayload)) ? DragDropEffects.Move : DragDropEffects.None; }

        private void TreeDragDrop(object sender, DragEventArgs e)
        {
            Point point = tree.PointToClient(new Point(e.X, e.Y)); TreeNode target = tree.GetNodeAt(point); if (target == null) return;
            string special = target.Tag as string; LibraryNode targetNode = target.Tag as LibraryNode;
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string parentId = targetNode != null && targetNode.IsFolder ? targetNode.Id : targetNode != null ? targetNode.ParentId : null;
                ImportFiles((string[])e.Data.GetData(DataFormats.FileDrop), parentId); return;
            }
            ItemDragPayload itemPayload = e.Data.GetData(typeof(ItemDragPayload)) as ItemDragPayload;
            if (itemPayload != null)
            {
                if (special != UnfiledTag && targetNode == null) return;
                string destinationId = special == UnfiledTag ? null : targetNode.Id;
                UndoService.Capture(Localizer.T("拖动记录", "drag records"), store, library, new AnnotationStore());
                foreach (string itemId in itemPayload.ItemIds) store.UpdateDocument(itemId, destinationId);
                RebuildTree(CurrentTreeKey()); RefreshGrid(); return;
            }
            NodeDragPayload nodePayload = e.Data.GetData(typeof(NodeDragPayload)) as NodeDragPayload;
            if (nodePayload != null)
            {
                string parentId = special == AllTag ? null : (targetNode != null && targetNode.IsFolder ? targetNode.Id : null);
                if (special != AllTag && (targetNode == null || !targetNode.IsFolder)) { MessageBox.Show("文件夹或文档只能移动到资料库根目录或另一个文件夹中。", "Clypsera"); return; }
                UndoService.Capture(Localizer.T("拖动分类", "drag library item"), store, library, new AnnotationStore());
                if (!library.Move(nodePayload.NodeId, parentId)) MessageBox.Show("无法移动到该位置。", "Clypsera");
                RebuildTree(nodePayload.NodeId); RefreshGrid();
            }
        }

        private void ChooseFilesToImport()
        {
            using (OpenFileDialog dialog = new OpenFileDialog { Multiselect = true, Filter = "支持的文档|*.pdf;*.docx;*.doc;*.md;*.markdown;*.txt;*.epub;*.mobi;*.azw3;*.aw3|纯文本|*.txt|Markdown|*.md;*.markdown|PDF|*.pdf|Word|*.docx;*.doc|电子书|*.epub;*.mobi;*.azw3;*.aw3|所有文件|*.*" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                ImportFiles(dialog.FileNames, GetImportParentId());
            }
        }

        private async void ChooseFolderToImport()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog
            {
                Description = Localizer.T("选择要导入资料库的文件夹。下一步可选择一次性导入或持续同步。", "Choose a folder to import. Next, choose one-time import or continuous sync."),
                ShowNewFolderButton = false
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                using (FolderImportModeForm mode = new FolderImportModeForm(dialog.SelectedPath, settings.AppTheme))
                {
                    if (mode.ShowDialog(this) != DialogResult.OK) return;
                    if (mode.ImportAsLiveFolder) await ImportLiveFolderAsync(dialog.SelectedPath, GetImportParentId());
                    else ImportFiles(new string[] { dialog.SelectedPath }, GetImportParentId());
                }
            }
        }

        private async Task ImportLiveFolderAsync(string folderPath, string parentId)
        {
            LibraryNode already = library.Snapshot().FirstOrDefault(x => x.IsLiveFolder && string.Equals(Path.GetFullPath(x.SyncSourcePath), Path.GetFullPath(folderPath), StringComparison.OrdinalIgnoreCase));
            if (already != null)
            {
                RebuildTree(already.Id);
                await SyncLiveFolderAsync(already.Id, true);
                return;
            }
            UndoService.Capture(Localizer.T("导入活动文件夹", "import live folder"), store, library, new AnnotationStore());
            LibraryNode root;
            try { root = library.CreateLiveFolder(folderPath, parentId); }
            catch (Exception ex) { MessageBox.Show(Localizer.T("无法创建活动文件夹：", "Could not create live folder: ") + ex.Message, "Clypsera", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            RebuildTree(root.Id);
            await SyncLiveFolderAsync(root.Id, true);
            ConfigureLiveFolderWatchers();
        }

        private async Task SyncSelectedLiveFolderAsync()
        {
            LibraryNode selected = tree.SelectedNode == null ? null : tree.SelectedNode.Tag as LibraryNode;
            LibraryNode root = selected == null ? null : library.GetLiveRootForNode(selected.Id);
            if (root == null) return;
            await SyncLiveFolderAsync(root.Id, true);
        }

        private async Task SyncLiveFolderAsync(string rootId, bool showProgress)
        {
            OperationProgressForm progress = showProgress ? new OperationProgressForm(Localizer.T("正在同步活动文件夹", "Synchronizing live folder"), settings.AppTheme) : null;
            LiveFolderSyncResult result;
            try
            {
                if (progress != null)
                {
                    progress.Show(this);
                    progress.SetStage(10, 24, Localizer.T("正在扫描源文件夹…", "Scanning the source folder…"));
                    progress.SetStage(26, 88, Localizer.T("正在同步新增、修改和删除…", "Synchronizing additions, changes, and deletions…"));
                }
                result = await Task.Run(delegate { return library.SyncLiveFolder(rootId); });
                if (progress != null) progress.SetStage(94, 98, Localizer.T("正在更新资料库视图…", "Updating the library view…"));
            }
            finally
            {
                if (progress != null) { if (!progress.IsDisposed) progress.Close(); progress.Dispose(); }
            }
            ApplyLiveFolderSyncResult(result, showProgress);
        }

        private void ApplyLiveFolderSyncResult(LiveFolderSyncResult result, bool showResult)
        {
            if (result == null) return;
            if (result.RemovedNodeIds.Count > 0)
            {
                HashSet<string> removed = new HashSet<string>(result.RemovedNodeIds);
                foreach (ClipboardItem item in store.Snapshot().Where(x => removed.Contains(x.DocumentId))) store.UpdateDocument(item.Id, null);
            }
            if (result.HasChanges || result.Errors.Count > 0)
            {
                RebuildTree(result.RootId); RefreshCategories(); RefreshGrid(); UpdateViewer();
            }
            string summary = Localizer.IsEnglish
                ? "Added " + result.AddedFiles + " files, updated " + result.UpdatedFiles + ", removed " + result.RemovedFiles + "."
                : "新增 " + result.AddedFiles + " 个文件，更新 " + result.UpdatedFiles + " 个，移除 " + result.RemovedFiles + " 个。";
            if (result.Errors.Count > 0)
            {
                string errorText = string.Join("\n", result.Errors.Take(8).ToArray());
                if (showResult) MessageBox.Show(summary + "\n\n" + errorText, Localizer.T("活动文件夹同步未完全完成", "Live folder sync was incomplete"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                else if (!headlessMode) tray.ShowBalloonTip(5000, "Clypsera · " + Localizer.T("活动文件夹", "Live folder"), result.SourceUnavailable ? result.Errors[0] : summary + " " + Localizer.T("部分文件暂时无法同步。", "Some files could not be synchronized."), ToolTipIcon.Warning);
            }
            else if (showResult) MessageBox.Show(summary, Localizer.T("活动文件夹同步完成", "Live folder synchronized"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            else if (result.HasChanges && !headlessMode) tray.ShowBalloonTip(3500, "Clypsera · " + Localizer.T("活动文件夹已更新", "Live folder updated"), summary, ToolTipIcon.Info);
        }

        private void DisableSelectedLiveFolder()
        {
            LibraryNode selected = tree.SelectedNode == null ? null : tree.SelectedNode.Tag as LibraryNode;
            LibraryNode root = selected == null ? null : library.GetLiveRootForNode(selected.Id);
            if (root == null) return;
            string question = Localizer.T("停止持续同步“", "Stop continuously synchronizing \"") + root.Name + Localizer.T("”？现有资料会保留，并转为普通文件夹。", "\"? Existing library copies will remain as a regular folder.");
            if (MessageBox.Show(question, "Clypsera", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            UndoService.Capture(Localizer.T("停止活动文件夹同步", "stop live folder sync"), store, library, new AnnotationStore());
            library.DisableLiveFolder(root.Id);
            ConfigureLiveFolderWatchers();
            RebuildTree(root.Id);
        }

        private void ConfigureLiveFolderWatchers()
        {
            DisposeLiveFolderWatchers();
            foreach (LibraryNode root in library.Snapshot().Where(x => x.IsLiveFolder))
            {
                if (!Directory.Exists(root.SyncSourcePath)) continue;
                try
                {
                    string rootId = root.Id;
                    FileSystemWatcher watcher = new FileSystemWatcher(root.SyncSourcePath)
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                        EnableRaisingEvents = true
                    };
                    FileSystemEventHandler changed = delegate { QueueLiveFolder(rootId); };
                    RenamedEventHandler renamed = delegate { QueueLiveFolder(rootId); };
                    ErrorEventHandler error = delegate { QueueLiveFolder(rootId); };
                    watcher.Created += changed; watcher.Changed += changed; watcher.Deleted += changed; watcher.Renamed += renamed; watcher.Error += error;
                    liveFolderWatchers[rootId] = watcher;
                }
                catch { QueueLiveFolder(root.Id); }
            }
        }

        private void DisposeLiveFolderWatchers()
        {
            foreach (FileSystemWatcher watcher in liveFolderWatchers.Values) try { watcher.Dispose(); } catch { }
            liveFolderWatchers.Clear();
        }

        private void QueueLiveFolder(string rootId)
        {
            if (string.IsNullOrWhiteSpace(rootId) || IsDisposed) return;
            MethodInvoker queue = delegate
            {
                pendingLiveSyncRoots.Add(rootId);
                liveSyncDebounceTimer.Stop();
                liveSyncDebounceTimer.Start();
            };
            if (InvokeRequired) { try { BeginInvoke(queue); } catch { } }
            else queue();
        }

        private void QueueAllLiveFolders()
        {
            ConfigureLiveFolderWatchers();
            foreach (LibraryNode root in library.Snapshot().Where(x => x.IsLiveFolder)) pendingLiveSyncRoots.Add(root.Id);
            if (pendingLiveSyncRoots.Count > 0) { liveSyncDebounceTimer.Stop(); liveSyncDebounceTimer.Start(); }
        }

        private async void LiveSyncDebounceTick(object sender, EventArgs e)
        {
            liveSyncDebounceTimer.Stop();
            if (liveSyncBusy) { liveSyncDebounceTimer.Start(); return; }
            liveSyncBusy = true;
            List<string> roots = pendingLiveSyncRoots.ToList(); pendingLiveSyncRoots.Clear();
            try
            {
                foreach (string rootId in roots)
                {
                    LiveFolderSyncResult result = await Task.Run(delegate { return library.SyncLiveFolder(rootId); });
                    ApplyLiveFolderSyncResult(result, false);
                }
                ConfigureLiveFolderWatchers();
            }
            finally
            {
                liveSyncBusy = false;
                if (pendingLiveSyncRoots.Count > 0) liveSyncDebounceTimer.Start();
            }
        }

        private async Task SyncAllLiveFoldersAtStartupAsync()
        {
            foreach (LibraryNode root in library.Snapshot().Where(x => x.IsLiveFolder).ToList())
            {
                LiveFolderSyncResult result = await Task.Run(delegate { return library.SyncLiveFolder(root.Id); });
                ApplyLiveFolderSyncResult(result, false);
            }
            ConfigureLiveFolderWatchers();
        }

        private string GetImportParentId()
        {
            LibraryNode selected = tree.SelectedNode == null ? null : tree.SelectedNode.Tag as LibraryNode;
            return selected == null ? null : selected.IsFolder ? selected.Id : selected.ParentId;
        }

        private void ImportFiles(string[] paths, string parentId)
        {
            if (paths == null || paths.Length == 0) return;
            UndoService.Capture(Localizer.T("导入文件或文件夹", "import files or folders"), store, library, new AnnotationStore());
            LibraryNode last = null; List<string> errors = new List<string>(); int importedFiles = 0; int importedFolders = 0;
            DuplicateImportContext duplicateContext = new DuplicateImportContext();
            foreach (string path in paths)
            {
                try
                {
                    if (Directory.Exists(path)) last = ImportFolderTree(path, parentId, errors, ref importedFiles, ref importedFolders, duplicateContext);
                    else last = ImportFileWithDuplicateChoice(path, parentId, ref importedFiles, duplicateContext);
                }
                catch (Exception ex) { errors.Add(Path.GetFileName(path) + "：" + ex.Message); }
            }
            RebuildTree(last == null ? CurrentTreeKey() : last.Id); RefreshGrid();
            if (last != null && errors.Count == 0) tray.ShowBalloonTip(3000, "Clypsera", Localizer.IsEnglish ? "Imported " + importedFiles + " files; replaced " + duplicateContext.Replaced + ", merged " + duplicateContext.Merged + ", skipped " + duplicateContext.Skipped + "." : "已导入 " + importedFiles + " 个文件；替换 " + duplicateContext.Replaced + " 个，合并 " + duplicateContext.Merged + " 个，不添加 " + duplicateContext.Skipped + " 个。", ToolTipIcon.Info);
            if (errors.Count > 0) MessageBox.Show(string.Join("\n", errors.ToArray()), "部分文件未能导入", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private LibraryNode ImportFileWithDuplicateChoice(string filePath, string parentId, ref int importedFiles, DuplicateImportContext context)
        {
            LibraryNode duplicate = library.FindDuplicateFile(filePath, parentId);
            if (duplicate == null) { importedFiles++; return library.ImportFile(filePath, parentId); }
            DuplicateImportAction action;
            if (context.StickyAction.HasValue) action = context.StickyAction.Value;
            else
            {
                using (DuplicateFileForm form = new DuplicateFileForm(Path.GetFileName(filePath)))
                {
                    if (form.ShowDialog(this) != DialogResult.OK || form.Decision == null) { context.Skipped++; return duplicate; }
                    action = form.Decision.Action;
                    if (form.Decision.ApplyToAll) context.StickyAction = action;
                }
            }
            if (action == DuplicateImportAction.Skip) { context.Skipped++; return duplicate; }
            if (action == DuplicateImportAction.Merge) { context.Merged++; return library.ReplaceFile(duplicate.Id, filePath, true); }
            context.Replaced++; return library.ReplaceFile(duplicate.Id, filePath, false);
        }

        private LibraryNode ImportFolderTree(string folderPath, string parentId, List<string> errors, ref int importedFiles, ref int importedFolders, DuplicateImportContext duplicateContext)
        {
            string normalized = folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string name = Path.GetFileName(normalized);
            if (string.IsNullOrWhiteSpace(name)) name = new DirectoryInfo(folderPath).Name;
            LibraryNode root = library.FindFolder(name, parentId);
            if (root == null) { root = library.Add(name, "folder", parentId); importedFolders++; }
            string[] directories;
            string[] files;
            try
            {
                directories = Directory.GetDirectories(folderPath).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToArray();
                files = Directory.GetFiles(folderPath).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToArray();
            }
            catch (Exception ex)
            {
                errors.Add(folderPath + "：" + ex.Message);
                return root;
            }
            foreach (string directory in directories)
            {
                try
                {
                    if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
                    ImportFolderTree(directory, root.Id, errors, ref importedFiles, ref importedFolders, duplicateContext);
                }
                catch (Exception ex) { errors.Add(directory + "：" + ex.Message); }
            }
            foreach (string filePath in files)
            {
                if (!IsSupportedImportFile(filePath)) continue;
                try { ImportFileWithDuplicateChoice(filePath, root.Id, ref importedFiles, duplicateContext); }
                catch (Exception ex) { errors.Add(filePath + "：" + ex.Message); }
            }
            return root;
        }

        private static bool IsSupportedImportFile(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            return extension == ".pdf" || extension == ".docx" || extension == ".doc" || extension == ".md" || extension == ".markdown" || extension == ".txt" || extension == ".epub" || extension == ".mobi" || extension == ".azw3" || extension == ".aw3";
        }

        private void FileDragEnter(object sender, DragEventArgs e) { e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; }
        private void FileDragDrop(object sender, DragEventArgs e) { if (e.Data.GetDataPresent(DataFormats.FileDrop)) ImportFiles((string[])e.Data.GetData(DataFormats.FileDrop), GetImportParentId()); }

        private void OpenManualEntry()
        {
            string clipboardText = "";
            try { if (Clipboard.ContainsText()) clipboardText = Clipboard.GetText(TextDataFormat.UnicodeText); } catch { }
            using (ManualEntryForm form = new ManualEntryForm(clipboardText))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                UndoService.Capture(Localizer.T("手动录入", "manual entry"), store, library, new AnnotationStore());
                ClipboardItem item = store.Add(form.ResultText, new SourceInfo { ProcessName = Localizer.T("Clypsera 手动录入", "Clypsera manual entry"), WindowTitle = Localizer.T("手动粘贴或输入", "Manual paste or entry") }, settings.MaxTextLength);
                LibraryNode selected = tree.SelectedNode == null ? null : tree.SelectedNode.Tag as LibraryNode;
                if (item != null && selected != null) store.UpdateDocument(item.Id, selected.Id);
                RefreshCategories(); RebuildTree(selected == null ? AllTag : selected.Id); RefreshGrid();
                if (settings.OverlayVisible) overlay.UpdateItem(item, paused);
            }
        }

        private void OpenSelectedRecordSource()
        {
            OpenRecordSource(SelectedItem());
        }

        private void OpenRecordSource(ClipboardItem item)
        {
            if (item == null) return;
            List<LibraryNode> nodes = library.Snapshot();
            LibraryNode node = nodes.FirstOrDefault(x => x.Id == item.DocumentId && !string.IsNullOrWhiteSpace(x.FilePath));
            if (node == null && !string.IsNullOrWhiteSpace(item.SourceTitle))
            {
                node = nodes.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.FilePath) &&
                    (string.Equals(x.Name, item.SourceTitle, StringComparison.CurrentCultureIgnoreCase) || string.Equals(x.OriginalFileName, item.SourceTitle, StringComparison.CurrentCultureIgnoreCase)));
            }
            if (node == null || string.IsNullOrWhiteSpace(node.FilePath))
            {
                if (!string.IsNullOrEmpty(item.DocumentId)) RebuildTree(item.DocumentId);
                MessageBox.Show(Localizer.T("这条记录尚未关联到已导入的书籍或文档。请先把它移动到对应资料，再双击文本内容。", "This record is not linked to an imported book or document. Move it to the source document, then double-click its content."), "Clypsera", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            List<DocumentAnnotation> annotations = new AnnotationStore().ForNode(node.Id);
            DocumentAnnotation target = annotations.FirstOrDefault(x => x.HistoryItemId == item.Id);
            if (target == null) target = annotations.Where(x => !string.IsNullOrWhiteSpace(x.Quote) && (item.Text ?? "").Contains(x.Quote)).OrderBy(x => Math.Abs((x.CreatedAt - item.CapturedAt).TotalSeconds)).FirstOrDefault();
            if (target == null) target = BuildNavigationTarget(item, node.Id);
            LastRequestedReaderNodeId = node.Id;
            LastRequestedNavigationTarget = target;
            if (headlessMode) return;
            OpenLibraryFile(node, target);
        }

        internal static DocumentAnnotation BuildNavigationTarget(ClipboardItem item, string nodeId)
        {
            string quote = (item.NoteQuote ?? "").Trim();
            int pageNumber = 0;
            string text = (item.Text ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
            Match pageMatch = Regex.Match(text, @"(?:第\s*(\d+)\s*页|Page\s*(\d+))", RegexOptions.IgnoreCase);
            if (pageMatch.Success) int.TryParse(pageMatch.Groups[1].Success ? pageMatch.Groups[1].Value : pageMatch.Groups[2].Value, out pageNumber);
            if (quote.Length == 0 && (text.StartsWith("【图书", StringComparison.Ordinal) || text.StartsWith("[Book ", StringComparison.OrdinalIgnoreCase)))
            {
                int firstLine = text.IndexOf('\n');
                if (firstLine >= 0)
                {
                    quote = text.Substring(firstLine + 1);
                    int noteStart = quote.IndexOf("\n\n批注：", StringComparison.Ordinal);
                    if (noteStart < 0) noteStart = quote.IndexOf("\n\nNote:", StringComparison.OrdinalIgnoreCase);
                    if (noteStart >= 0) quote = quote.Substring(0, noteStart);
                    quote = quote.Trim();
                }
            }
            return string.IsNullOrWhiteSpace(quote) && pageNumber <= 0 ? null : new DocumentAnnotation { NodeId = nodeId, Quote = quote, Note = item.Note, Page = pageNumber };
        }

        private void MainFormKeyDown(object sender, KeyEventArgs e)
        {
            if (!e.Control || e.KeyCode != Keys.Z) return;
            UndoLastAction();
            e.Handled = true; e.SuppressKeyPress = true;
        }

        private void UndoLastAction()
        {
            string description;
            if (!UndoService.Undo(out description))
            {
                tray.ShowBalloonTip(1800, "Clypsera", Localizer.T("没有可以撤销的操作。", "There is nothing to undo."), ToolTipIcon.Info);
                return;
            }
            store.Reload(); library.Reload(); checkedItemIds.Clear();
            ConfigureLiveFolderWatchers();
            RefreshCategories(); RebuildTree(AllTag); RefreshGrid();
            foreach (Form open in Application.OpenForms)
            {
                ReaderForm reader = open as ReaderForm;
                if (reader != null) reader.ReloadAfterUndo();
            }
            tray.ShowBalloonTip(2200, "Clypsera", Localizer.T("已撤销：", "Undone: ") + description, ToolTipIcon.Info);
        }

        private void OpenSelectedLibraryFile()
        {
            LibraryNode node = tree.SelectedNode == null ? null : tree.SelectedNode.Tag as LibraryNode;
            if (node == null || string.IsNullOrWhiteSpace(node.FilePath)) { MessageBox.Show("请选择一个已导入的 Markdown、PDF、Word 或电子书文件。", "Clypsera"); return; }
            OpenLibraryFile(node);
        }

        private void OpenLibraryFile(LibraryNode node)
        {
            OpenLibraryFile(node, null);
        }

        private void OpenLibraryFile(LibraryNode node, DocumentAnnotation target)
        {
            try
            {
                ReaderForm reader = new ReaderForm(node, store, target);
                reader.AnnotationChanged += delegate
                {
                    if (IsDisposed) return;
                    BeginInvoke((MethodInvoker)delegate { RefreshCategories(); RebuildTree(node.Id); RefreshGrid(); });
                };
                reader.Show();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "无法打开阅读器", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void OpenAiAssistant(bool searchMode, ClipboardItem overrideItem = null, string initialQuestion = null)
        {
            if (!settings.DeepSeekEnabled || string.IsNullOrWhiteSpace(AppData.UnprotectSecret(settings.ApiKeyEncrypted)))
            {
                MessageBox.Show("请先在设置中启用 DeepSeek 并填写 API Key。", "Clypsera", MessageBoxButtons.OK, MessageBoxIcon.Information);
                OpenSettings(); return;
            }
            ClipboardItem item = overrideItem ?? SelectedItem();
            if (!searchMode && item == null)
            {
                MessageBox.Show("请先在中栏选择一条需要解释或总结的记录。", "Clypsera", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!searchMode && (TextSafety.FindSensitiveReason(item.Text) != null || TextSafety.LooksLikeStandaloneCredential(item.Text)))
            {
                MessageBox.Show("这条记录疑似包含密码、验证码或密钥。为避免把敏感信息发送给 AI，问询已被阻止。", "Clypsera", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (aiAssistant != null && !aiAssistant.IsDisposed) aiAssistant.Close();
            aiAssistant = new AiAssistantForm(settings, store, library, item, searchMode, initialQuestion);
            aiAssistant.SearchResultsUpdated += delegate(List<string> ids)
            {
                if (IsDisposed) return;
                BeginInvoke((MethodInvoker)delegate
                {
                    if (search.Text.Length > 0) search.Clear();
                    aiSearchIds = ids == null ? null : new HashSet<string>(ids);
                    if (tree.Nodes.Count > 0) tree.SelectedNode = tree.Nodes[0];
                    RefreshGrid();
                });
            };
            aiAssistant.InquirySaved += delegate
            {
                BeginInvoke((MethodInvoker)delegate { RefreshCategories(); RebuildTree(CurrentTreeKey()); RefreshGrid(); });
            };
            aiAssistant.FormClosed += delegate
            {
                if (searchMode)
                {
                    aiSearchIds = null;
                    if (!IsDisposed) BeginInvoke((MethodInvoker)delegate { RefreshGrid(); });
                }
                aiAssistant = null;
            };
            aiAssistant.Show();
            aiAssistant.Activate();
        }

        private void OpenViewerExplanation()
        {
            ClipboardItem item = SelectedItem(); if (item == null) return;
            string selected = viewer.SelectionLength > 0 ? viewer.SelectedText.Trim() : item.Text;
            if (string.IsNullOrWhiteSpace(selected)) { MessageBox.Show(Localizer.T("请先在右栏选中一个单词或一段文字。", "Select a word or passage in the right pane first."), "Clypsera", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            ClipboardItem focus = item.Clone(); focus.Text = selected;
            OpenAiAssistant(false, focus, AiExplanation.BuildQuestion(selected));
        }

        private void OpenViewerAnnotationExplanation(string annotationId)
        {
            ClipboardItem item = SelectedItem(); if (item == null) return;
            TextAnnotation annotation = ItemAnnotations(item).FirstOrDefault(x => x.Id == annotationId); if (annotation == null) return;
            string selected = string.IsNullOrWhiteSpace(annotation.Quote) ? annotation.Note : annotation.Quote;
            if (string.IsNullOrWhiteSpace(selected)) return;
            ClipboardItem focus = item.Clone(); focus.Text = selected;
            OpenAiAssistant(false, focus, AiExplanation.BuildQuestion(selected));
        }

        private void ShowSafePasteHelp()
        {
            MessageBox.Show("请把光标放到目标软件的输入位置，然后按 Ctrl+Shift+V。\n\nClypsera 会先在本机检查密码、验证码、密钥等敏感内容；通过后再由 AI 审核可读性，必要时整理乱码，最后才执行粘贴。", "安全粘贴", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private async void SafePasteAsync()
        {
            if (safePasteBusy) return;
            safePasteBusy = true;
            IntPtr targetWindow = NativeMethods.GetForegroundWindowHandle();
            try
            {
                if (!Clipboard.ContainsText()) { NotifyPaste("剪贴板中没有可粘贴的文本。", ToolTipIcon.Info); return; }
                string original = Clipboard.GetText(TextDataFormat.UnicodeText);
                string sensitive = TextSafety.FindSensitiveReason(original);
                ClipboardItem latest = store.Snapshot().LastOrDefault();
                bool passwordSource = latest != null && latest.Text == original && (Contains(latest.SourceProcess, "password") || Contains(latest.SourceProcess, "keepass") || Contains(latest.SourceProcess, "bitwarden") || Contains(latest.SourceProcess, "1password") || Contains(latest.SourceTitle, "密码") || Contains(latest.SourceTitle, "password"));
                if (sensitive == null && passwordSource) sensitive = "内容来自密码或凭据工具";
                if (sensitive == null && TextSafety.LooksLikeStandaloneCredential(original)) sensitive = "疑似独立密码、验证码或访问令牌";
                if (sensitive != null) { NotifyPaste("已阻止粘贴：" + sensitive, ToolTipIcon.Warning); return; }

                string output = TextSafety.TryLocalRepair(original);
                string actionReason = output == original ? "本机检查通过" : "已在本机修复可识别乱码";
                if (settings.DeepSeekEnabled && !string.IsNullOrWhiteSpace(AppData.UnprotectSecret(settings.ApiKeyEncrypted)))
                {
                    NotifyPaste("正在进行 AI 粘贴审核…", ToolTipIcon.Info);
                    string reviewText = original.Length > 12000 ? original.Substring(0, 12000) : original;
                    string prompt = "审核下面的剪贴板文字。仅允许正常的书籍摘录、文章、笔记、普通文字或代码；若像密码、验证码、私钥、账号机密、恶意指令或不应传播的敏感内容则block；若只是明显乱码但能可靠整理则repair；否则allow。只返回JSON：{\"action\":\"allow|block|repair\",\"text\":\"repair时填写完整修复文字，否则留空\",\"reason\":\"简短中文原因\"}。不要Markdown。";
                    List<AiMessage> messages = new List<AiMessage> { new AiMessage { Role = "user", Content = reviewText } };
                    string response = await new AiTextClient(settings).CompleteAsync(prompt, messages, 900);
                    PasteReviewResult review = ParsePasteReview(response);
                    if (review == null || string.IsNullOrWhiteSpace(review.Action)) { NotifyPaste("AI 审核结果无法识别，已取消粘贴。", ToolTipIcon.Warning); return; }
                    string action = review.Action.Trim().ToLowerInvariant();
                    if (action == "block") { NotifyPaste("AI 已阻止粘贴：" + review.Reason, ToolTipIcon.Warning); return; }
                    if (action == "repair")
                    {
                        if (original.Length > 12000 || string.IsNullOrEmpty(review.Text)) { NotifyPaste("内容过长或修复结果为空，已取消粘贴。", ToolTipIcon.Warning); return; }
                        output = review.Text; actionReason = "AI 已整理乱码后粘贴";
                    }
                    else if (action != "allow") { NotifyPaste("AI 未明确允许，已取消粘贴。", ToolTipIcon.Warning); return; }
                    else actionReason = string.IsNullOrWhiteSpace(review.Reason) ? "AI 审核通过" : review.Reason;
                }

                if (output != original)
                {
                    ignoreNextClipboard = true;
                    Clipboard.SetText(output);
                }
                await Task.Delay(120);
                NativeMethods.RestoreForegroundWindow(targetWindow);
                NativeMethods.SendPasteKeystroke();
                NotifyPaste("安全粘贴完成：" + actionReason, ToolTipIcon.Info);
            }
            catch (Exception ex) { NotifyPaste("安全粘贴失败：" + ex.Message, ToolTipIcon.Error); }
            finally { safePasteBusy = false; }
        }

        private static PasteReviewResult ParsePasteReview(string response)
        {
            try
            {
                int start = response.IndexOf('{'); int end = response.LastIndexOf('}');
                if (start < 0 || end <= start) return null;
                return AppData.CreateSerializer().Deserialize<PasteReviewResult>(response.Substring(start, end - start + 1));
            }
            catch { return null; }
        }

        private void NotifyPaste(string message, ToolTipIcon icon)
        {
            tray.ShowBalloonTip(4000, "Clypsera · 安全粘贴", message, icon);
        }

        private async Task ClassifySelectedAsync()
        {
            ClipboardItem item = SelectedItem(); if (item == null) return;
            if (!settings.DeepSeekEnabled) { MessageBox.Show("请先在设置中启用 DeepSeek 并填写 API Key。", "Clypsera", MessageBoxButtons.OK, MessageBoxIcon.Information); OpenSettings(); return; }
            Cursor old = Cursor; Cursor = Cursors.WaitCursor;
            OperationProgressForm progress = new OperationProgressForm(Localizer.T("AI 正在自动归档", "AI is filing the item"), settings.AppTheme);
            try
            {
                progress.Show(this);
                progress.SetStage(12, 28, Localizer.T("正在读取可用分类…", "Reading available categories…"));
                progress.SetStage(30, 88, Localizer.T("正在判断主题和归档位置…", "Choosing a topic and filing location…"));
                AiRouteResult result = await new DeepSeekClient(settings).ClassifyAndRouteAsync(item.Text, library.GetDocumentChoices());
                progress.SetStage(94, 98, Localizer.T("正在保存分类结果…", "Saving the filing result…"));
                UndoService.Capture(Localizer.T("AI 自动归档", "AI auto filing"), store, library, new AnnotationStore());
                store.UpdateCategory(item.Id, result.Category, true);
                if (!string.IsNullOrEmpty(result.DocumentId)) store.UpdateDocument(item.Id, result.DocumentId);
                RefreshCategories(); RebuildTree(CurrentTreeKey()); RefreshGrid();
                MessageBox.Show("主题：" + result.Category + "\n" + (string.IsNullOrEmpty(result.DocumentId) ? "未自动移动文档" : "已移到：" + library.GetPath(result.DocumentId)), "AI 整理完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "DeepSeek 整理失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally
            {
                Cursor = old;
                if (!progress.IsDisposed) progress.Close();
                progress.Dispose();
            }
        }

        private void EditCategory()
        {
            List<ClipboardItem> items = SelectedItems(); if (items.Count == 0) return;
            string value = Prompt.Show(Localizer.T("请输入新的主题分类：", "Enter a new topic:"), Localizer.T("修改主题", "Edit topic"), items[0].Category);
            if (string.IsNullOrWhiteSpace(value)) return;
            UndoService.Capture(Localizer.T("修改主题", "edit topic"), store, library, new AnnotationStore());
            foreach (ClipboardItem item in items) store.UpdateCategory(item.Id, value.Trim(), false);
            RefreshCategories(); RefreshGrid();
        }

        private void CopySelected()
        {
            List<ClipboardItem> items = SelectedItems(); if (items.Count == 0) return;
            try { ignoreNextClipboard = true; Clipboard.SetText(string.Join(Environment.NewLine + Environment.NewLine, items.Select(x => x.Text ?? "").ToArray())); }
            catch (Exception ex) { MessageBox.Show(Localizer.T("复制失败：", "Copy failed: ") + ex.Message, "Clypsera"); }
        }

        private void DeleteSelected()
        {
            List<ClipboardItem> items = SelectedItems(); if (items.Count == 0) return;
            string question = Localizer.IsEnglish ? "Delete " + items.Count + " selected records? Linked book annotations will also be deleted." : "确定删除选中的 " + items.Count + " 条记录吗？关联的图书批注也会一并删除。";
            if (MessageBox.Show(question, "Clypsera", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            AnnotationStore annotationStore = new AnnotationStore();
            UndoService.Capture(Localizer.T("删除记录", "delete records"), store, library, annotationStore);
            foreach (ClipboardItem item in items) { annotationStore.DeleteByHistoryItemId(item.Id); store.Delete(item.Id); checkedItemIds.Remove(item.Id); }
            foreach (Form open in Application.OpenForms) { ReaderForm reader = open as ReaderForm; if (reader != null && items.Any(x => x.DocumentId == reader.NodeId)) reader.ReloadAnnotations(); }
            RebuildTree(CurrentTreeKey()); RefreshCategories(); RefreshGrid();
        }

        private List<ClipboardItem> ItemsInCurrentTreeNode()
        {
            IEnumerable<ClipboardItem> query = store.Snapshot().OrderByDescending(x => x.CapturedAt);
            object tag = tree.SelectedNode == null ? AllTag : tree.SelectedNode.Tag;
            string special = tag as string;
            LibraryNode node = tag as LibraryNode;
            if (special == UnfiledTag) query = query.Where(x => string.IsNullOrEmpty(x.DocumentId));
            else if (node != null && node.IsDocument) query = query.Where(x => x.DocumentId == node.Id);
            else if (node != null && node.IsFolder)
            {
                HashSet<string> ids = new HashSet<string>(library.GetNodeIdsInBranch(node.Id));
                query = query.Where(x => !string.IsNullOrEmpty(x.DocumentId) && ids.Contains(x.DocumentId));
            }
            return query.ToList();
        }

        private void Export(string preferredScope)
        {
            ClipboardItem current = SelectedItem();
            List<ClipboardItem> selected = SelectedItems();
            List<ClipboardItem> folderItems = ItemsInCurrentTreeNode();
            List<ClipboardItem> viewItems = FilteredItems();
            using (ExportOptionsForm options = new ExportOptionsForm(current != null, selected.Count, folderItems.Count, viewItems.Count, preferredScope))
            {
                if (options.ShowDialog(this) != DialogResult.OK) return;
                List<ClipboardItem> items = options.Scope == "current" ? (current == null ? new List<ClipboardItem>() : new List<ClipboardItem> { current })
                    : options.Scope == "selected" ? selected : options.Scope == "folder" ? folderItems : viewItems;
                if (items.Count == 0) { MessageBox.Show(Localizer.T("所选范围没有可导出的记录。", "There are no records in the selected scope."), "Clypsera", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                try
                {
                    string output;
                    if (options.Separate)
                    {
                        using (FolderBrowserDialog dialog = new FolderBrowserDialog { Description = Localizer.T("选择保存导出文件夹的位置", "Choose where to save the export folder"), SelectedPath = ExistingDefaultExportFolder() })
                        {
                            if (dialog.ShowDialog(this) != DialogResult.OK) return;
                            output = Path.Combine(dialog.SelectedPath, "Clypsera-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                            ExportWriter.WriteSeparate(output, options.Format, items, library, options.IncludeAnnotations, options.IncludeAiExplanations);
                        }
                    }
                    else
                    {
                        using (SaveFileDialog dialog = new SaveFileDialog { Filter = ExportWriter.DialogFilter(options.Format), DefaultExt = ExportWriter.Extension(options.Format), AddExtension = true, FileName = "Clypsera-" + DateTime.Now.ToString("yyyyMMdd-HHmm"), InitialDirectory = ExistingDefaultExportFolder() })
                        {
                            if (dialog.ShowDialog(this) != DialogResult.OK) return;
                            output = dialog.FileName;
                            ExportWriter.WriteMerged(output, options.Format, items, library, options.IncludeAnnotations, options.IncludeAiExplanations);
                        }
                    }
                    MessageBox.Show(Localizer.T("已导出 " + items.Count + " 条记录：\n" + output, "Exported " + items.Count + " records:\n" + output), "Clypsera", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { MessageBox.Show(Localizer.T("导出失败：", "Export failed: ") + ex.Message, "Clypsera", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        private string ExistingDefaultExportFolder()
        {
            try { return !string.IsNullOrWhiteSpace(settings.DefaultExportFolder) && Directory.Exists(settings.DefaultExportFolder) ? settings.DefaultExportFolder : ""; }
            catch { return ""; }
        }
        private void ApplyPaneVisibility()
        {
            if (leftSplit == null || contentSplit == null) return;
            if (!middlePaneButton.Checked && !rightPaneButton.Checked) rightPaneButton.Checked = true;
            leftSplit.Panel1Collapsed = !leftPaneButton.Checked;
            contentSplit.Panel1Collapsed = !middlePaneButton.Checked;
            contentSplit.Panel2Collapsed = !rightPaneButton.Checked;
            if (leftCollapseHandle != null)
            {
                leftCollapseHandle.Text = leftSplit.Panel1Collapsed ? "▶" : "◀";
                leftCollapseTip.SetToolTip(leftCollapseHandle, leftSplit.Panel1Collapsed ? Localizer.T("展开分类栏", "Expand library pane") : Localizer.T("收起分类栏", "Collapse library pane"));
            }
        }
        private void TogglePause()
        {
            paused = !paused;
            pauseMenu.Text = paused ? Localizer.T("开始采集", "Start capture") : Localizer.T("停止采集", "Stop capture");
            if (viewPauseMenu != null) viewPauseMenu.Text = pauseMenu.Text;
            tray.Text = paused ? Localizer.T("Clypsera · 等待手动开始采集", "Clypsera · Capture is off") : Localizer.T("Clypsera · 正在记录剪贴板", "Clypsera · Capturing clipboard text");
            if (paused) overlay.Hide();
            else if (settings.OverlayVisible) { overlay.UpdateItem(store.Snapshot().LastOrDefault(), false); overlay.Show(); }
        }
        private void ToggleOverlay() { SetOverlayVisible(!settings.OverlayVisible); }
        private void SetOverlayVisible(bool visible) { settings.OverlayVisible = visible; overlayMenu.Checked = visible; if (viewOverlayMenu != null) viewOverlayMenu.Checked = visible; if (visible && !paused) { overlay.UpdateItem(store.Snapshot().LastOrDefault(), false); overlay.Show(); } else overlay.Hide(); AppData.SaveSettings(settings); }

        private void OpenSettings()
        {
            using (SettingsForm form = new SettingsForm(settings))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                string previousLanguage = settings.Language;
                string previousFontSize = settings.UiFontSize;
                settings = form.Result; AppData.SaveSettings(settings); ApplyLocalApi(); ApplyStartupSetting();
                SetOverlayVisible(settings.OverlayVisible);
                foreach (Form open in Application.OpenForms) { UiStyle.ApplyTheme(open, settings.AppTheme); UiStyle.ApplyFontScale(open, previousFontSize, settings.UiFontSize); }
                ApplyResponsiveLayout();
                UpdateViewer();
                if (previousLanguage != settings.Language)
                    MessageBox.Show(settings.Language == "en-US" ? "Language saved. Restart Clypsera to apply English everywhere." : "语言已保存。重新启动 Clypsera 后会完整切换为中文。", "Clypsera", MessageBoxButtons.OK, MessageBoxIcon.Information);
                else MessageBox.Show(Localizer.T("设置已保存。", "Settings saved."), "Clypsera", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void ApplyLocalApi()
        {
            if (apiServer != null) { apiServer.Dispose(); apiServer = null; }
            if (!settings.EnableLocalApi) return;
            try { apiServer = new LocalApiServer(store, library); apiServer.Start(settings.LocalApiPort); }
            catch (Exception ex) { settings.EnableLocalApi = false; AppData.SaveSettings(settings); MessageBox.Show("本地 API 无法启动：" + ex.Message, "Clypsera", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private void ApplyStartupSetting()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", true))
                {
                    if (settings.StartWithWindows) { key.SetValue("Clypsera", "\"" + Application.ExecutablePath + "\""); key.DeleteValue("ClipboardTrail", false); }
                    else { key.DeleteValue("Clypsera", false); key.DeleteValue("ClipboardTrail", false); }
                }
            }
            catch { }
        }

        private void ShowMain() { Show(); WindowState = FormWindowState.Maximized; BalanceInitialLayout(); Activate(); BringToFront(); }
        private void MainFormClosing(object sender, FormClosingEventArgs e) { if (!exiting) { e.Cancel = true; Hide(); } }
        private void ExitApplication() { exiting = true; if (apiServer != null) apiServer.Dispose(); tray.Visible = false; overlay.Close(); Close(); }
    }

    internal sealed class ClipboardRow
    {
        public ClipboardItem Item { get; private set; }
        public bool Marked { get; set; }
        public string Time { get { return Item.CapturedAt.ToString("MM-dd HH:mm:ss"); } }
        public string Source { get { return Item.SourceProcess; } }
        public string Document { get; private set; }
        public string Category { get { return Item.Category; } }
        public string Preview { get { return (Item.Text ?? "").Replace("\r", " ").Replace("\n", " "); } }
        public ClipboardRow(ClipboardItem item, string document, bool marked) { Item = item; Document = document; Marked = marked; }
    }

    internal sealed class ItemDragPayload { public List<string> ItemIds { get; private set; } public ItemDragPayload(List<string> ids) { ItemIds = ids ?? new List<string>(); } }
    internal sealed class NodeDragPayload { public string NodeId { get; private set; } public NodeDragPayload(string id) { NodeId = id; } }
    internal sealed class DuplicateImportContext { public DuplicateImportAction? StickyAction { get; set; } public int Replaced { get; set; } public int Merged { get; set; } public int Skipped { get; set; } }

    internal static class Prompt
    {
        public static string Show(string text, string caption, string value)
        {
            using (Form form = new Form { Width = 430, Height = 155, Text = caption, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, Font = new Font("Microsoft YaHei UI", 9F) })
            {
                Label label = new Label { Left = 16, Top = 14, Width = 380, Text = text };
                TextBox box = new TextBox { Left = 16, Top = 40, Width = 380, Text = value ?? "" };
                Button ok = new Button { Text = "确定", Left = 222, Top = 75, Width = 82, DialogResult = DialogResult.OK };
                Button cancel = new Button { Text = "取消", Left = 314, Top = 75, Width = 82, DialogResult = DialogResult.Cancel };
                form.Controls.Add(label); form.Controls.Add(box); form.Controls.Add(ok); form.Controls.Add(cancel); form.AcceptButton = ok; form.CancelButton = cancel;
                SettingsData appearance = AppData.LoadSettings();
                UiStyle.Prepare(form, appearance.AppTheme);
                Localizer.Apply(form);
                return form.ShowDialog() == DialogResult.OK ? box.Text : null;
            }
        }
    }
}
