using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Linq;
using System.Windows.Forms;

namespace ClipboardTrail
{
    internal static class Branding
    {
        public const string ProductName = "Clypsera";

        public static Icon CreateIcon()
        {
            try
            {
                using (Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
                {
                    if (icon != null) return (Icon)icon.Clone();
                }
            }
            catch { }
            return (Icon)SystemIcons.Application.Clone();
        }
    }

    internal static class Localizer
    {
        private static bool english;
        private static readonly Dictionary<string, string> map = new Dictionary<string, string>
        {
            { "Clypsera · 我的剪贴板资料库", "Clypsera · My Clipboard Library" },
            { "文件", "File" }, { "新建", "New" }, { "搜索", "Search" }, { "整理", "Organize" }, { "阅读批注", "Read & Annotate" }, { "视图", "View" }, { "帮助", "Help" },
            { "导入文档…", "Import documents…" }, { "导入文件夹…", "Import folder…" }, { "导出…", "Export…" }, { "设置…", "Settings…" }, { "退出", "Exit" },
            { "文件夹", "Folder" }, { "文档", "Document" }, { "书籍", "Book" },
            { "关键词搜索…", "Keyword search…" }, { "书名搜索…", "Book title search…" }, { "文件名搜索…", "File name search…" }, { "主题筛选", "Topic filter" }, { "清除本地筛选", "Clear local filters" }, { "AI 描述搜索…", "AI semantic search…" },
            { "移动到文件夹或文档…", "Move to folder or document…" }, { "修改主题…", "Edit topic…" }, { "AI 自动归档", "AI auto-file" }, { "AI 问询…", "Ask AI…" }, { "安全粘贴说明", "Safe paste guide" },
            { "打开选中文件", "Open selected file" }, { "编辑当前记录…", "Edit current item…" }, { "高亮所选文字", "Highlight selection" }, { "下划线所选文字", "Underline selection" }, { "清除所选格式", "Clear selection format" },
            { "显示分类栏", "Show library pane" }, { "显示记录栏", "Show records pane" }, { "显示查看栏", "Show viewer pane" }, { "显示悬浮窗", "Show floating window" }, { "暂停采集", "Pause capture" },
            { "安全粘贴快捷键  Ctrl+Shift+V", "Safe paste shortcut  Ctrl+Shift+V" }, { "关于 Clypsera", "About Clypsera" },
            { "我的分类", "My Library" }, { "剪贴板记录", "Clipboard Records" }, { "查看", "Viewer" }, { "操作 ▼", "Menu ▼" },
            { "新建文件夹", "New folder" }, { "新建文档", "New document" }, { "新建书籍", "New book" }, { "导入文档或电子书", "Import document or ebook" }, { "导入整个文件夹", "Import entire folder" }, { "导出此分类…", "Export this category…" }, { "打开阅读", "Open reader" }, { "重命名", "Rename" }, { "删除", "Delete" },
            { "在右栏查看", "View in right pane" }, { "编辑内容", "Edit content" }, { "移动到文件夹或文档", "Move to folder or document" }, { "手动修改主题", "Edit topic manually" }, { "使用 DeepSeek 整理", "Organize with DeepSeek" }, { "复制到剪贴板", "Copy to clipboard" }, { "导出所选条目…", "Export selected items…" }, { "删除所选条目", "Delete selected items" },
            { "AI 整理排版", "AI tidy layout" }, { "默认导出位置", "Default export location" }, { "默认导出文件夹", "Default export folder" }, { "选择文件夹…", "Choose folder…" }, { "清除", "Clear" },
            { "时间", "Time" }, { "来源", "Source" }, { "文本内容", "Content" }, { "全部主题", "All topics" },
            { "Clypsera 设置", "Clypsera Settings" }, { "保存", "Save" }, { "取消", "Cancel" }, { "移动", "Move" },
            { "高亮", "Highlight" }, { "下划线", "Underline" },
            { "选中文字或摘录", "Selected text or excerpt" }, { "批注", "Annotation" }, { "页码（不知道可填 0）", "Page (0 if unknown)" }, { "保存批注", "Save annotation" },
            { "未归档", "Unfiled" }, { "批注与摘录", "Annotations & Excerpts" }, { "当前页", "Page" }, { "删除所选批注", "Delete annotation" },
            { "查看批注详情", "View annotation details" }, { "复制摘录", "Copy excerpt" }, { "图书批注", "Book annotation" },
            { "荧光笔", "Highlight" }, { "重新载入", "Reload" }, { "打开归档目录", "Open archive folder" },
            { "阅读主题", "Reading theme" }, { "护眼绿", "Eye-comfort green" }, { "羊皮纸", "Parchment" }, { "黑色", "Black" }, { "白色", "White" },
            { "AI 描述式搜索", "AI Semantic Search" }, { "针对选中内容提问", "Ask about selection" }, { "发送  Ctrl+Enter", "Send  Ctrl+Enter" }, { "总结", "Summarize" }, { "清空对话", "Clear chat" }, { "保存问答", "Save Q&A" },
            { "Clypsera 悬浮窗", "Clypsera Floating Window" }, { "采集已暂停", "Capture paused" }, { "右键托盘图标可继续采集", "Right-click the tray icon to resume" }, { "Clypsera 正在等待复制内容", "Clypsera is waiting for copied text" }, { "复制一段文字后，这里会显示来源、时间和预览。", "Copy some text to see its source, time, and preview here." },
            { "DeepSeek 自动分类（会把最多 4000 字发送给所填接口）", "DeepSeek auto-filing (sends up to 4,000 characters)" }, { "启用 DeepSeek 自动分类", "Enable DeepSeek auto-filing" }, { "接口地址", "Endpoint" }, { "模型", "Model" },
            { "本机集成接口（只监听 127.0.0.1）", "Local integration API (127.0.0.1 only)" }, { "启用只读本地 API", "Enable read-only local API" }, { "端口", "Port" }, { "采集", "Capture" }, { "每条最多保存字符数", "Maximum characters per item" }, { "随 Windows 启动", "Start with Windows" },
            { "外观与语言", "Appearance and Language" }, { "界面语言", "Interface language" }, { "软件外观", "App appearance" }, { "日间模式", "Light mode" }, { "夜间模式", "Dark mode" },
            { "中文（简体）", "Chinese (Simplified)" }, { "English", "English" }
        };

        public static bool IsEnglish { get { return english; } }
        public static void Use(string language) { english = language == "en-US"; }
        public static string T(string chinese, string englishText) { return english ? englishText : chinese; }
        public static string Translate(string value)
        {
            if (!english || string.IsNullOrEmpty(value)) return value;
            string translated;
            if (map.TryGetValue(value, out translated)) return translated;
            if (value.StartsWith("阅读 · ")) return "Reader · " + value.Substring(5);
            if (value.StartsWith("剪贴板记录  ·  ")) return "Clipboard Records · " + value.Substring(10).Replace(" 条", " items");
            return value;
        }

        public static void Apply(Control root)
        {
            if (root == null) return;
            root.Text = Translate(root.Text);
            ToolStrip strip = root as ToolStrip;
            if (strip != null) foreach (ToolStripItem item in strip.Items) ApplyItem(item);
            if (root.ContextMenuStrip != null) foreach (ToolStripItem item in root.ContextMenuStrip.Items) ApplyItem(item);
            DataGridView grid = root as DataGridView;
            if (grid != null) foreach (DataGridViewColumn column in grid.Columns) column.HeaderText = Translate(column.HeaderText);
            ComboBox combo = root as ComboBox;
            if (combo != null)
            {
                int selected = combo.SelectedIndex;
                for (int i = 0; i < combo.Items.Count; i++) if (combo.Items[i] is string) combo.Items[i] = Translate((string)combo.Items[i]);
                if (selected >= 0 && selected < combo.Items.Count) combo.SelectedIndex = selected;
            }
            TreeView tree = root as TreeView;
            if (tree != null) ApplyNodes(tree.Nodes);
            foreach (Control child in root.Controls) Apply(child);
        }

        public static void ApplyMenu(ContextMenuStrip menu)
        {
            if (menu == null) return;
            foreach (ToolStripItem item in menu.Items) ApplyItem(item);
        }

        private static void ApplyItem(ToolStripItem item)
        {
            item.Text = Translate(item.Text);
            ToolStripDropDownItem drop = item as ToolStripDropDownItem;
            if (drop != null) foreach (ToolStripItem child in drop.DropDownItems) ApplyItem(child);
        }

        private static void ApplyNodes(TreeNodeCollection nodes)
        {
            foreach (TreeNode node in nodes) { node.Text = Translate(node.Text); ApplyNodes(node.Nodes); }
        }
    }

    internal static class UiStyle
    {
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        public static void Prepare(Form form, string theme)
        {
            if (form == null) return;
            form.HandleCreated += delegate { ApplyRoundedWindow(form); ApplyDwmTheme(form, theme); };
            form.Resize += delegate { if (form.IsHandleCreated) ApplyRoundedWindow(form); };
            ApplyTheme(form, theme);
            SettingsData settings = AppData.LoadSettings();
            ApplyFontScale(form, "medium", settings.UiFontSize);
        }

        public static void ApplyFontScale(Control root, string previousSize, string newSize)
        {
            if (root == null) return;
            float ratio = FontFactor(newSize) / FontFactor(previousSize);
            if (Math.Abs(ratio - 1F) < 0.001F) return;
            List<FontScaleEntry> entries = new List<FontScaleEntry>();
            CaptureFonts(root, null, entries);
            foreach (FontScaleEntry entry in entries)
            {
                if (!entry.IsRoot && !entry.HasOwnFont) continue;
                try { entry.Control.Font = new Font(entry.Font.FontFamily, Math.Max(7F, Math.Min(18F, entry.Font.Size * ratio)), entry.Font.Style, entry.Font.Unit, entry.Font.GdiCharSet, entry.Font.GdiVerticalFont); }
                catch { }
            }
            foreach (DataGridView grid in entries.Select(x => x.Control).OfType<DataGridView>())
            {
                grid.RowTemplate.Height = Math.Max(32, (int)Math.Round(grid.RowTemplate.Height * ratio));
                grid.ColumnHeadersHeight = Math.Max(32, (int)Math.Round(grid.ColumnHeadersHeight * ratio));
            }
            foreach (TreeView tree in entries.Select(x => x.Control).OfType<TreeView>()) tree.ItemHeight = Math.Max(18, (int)Math.Round(tree.ItemHeight * ratio));
        }

        private static void CaptureFonts(Control control, Font parentFont, List<FontScaleEntry> entries)
        {
            Font current = control.Font;
            bool own = parentFont == null || current.FontFamily.Name != parentFont.FontFamily.Name || Math.Abs(current.Size - parentFont.Size) > 0.01F || current.Style != parentFont.Style;
            entries.Add(new FontScaleEntry { Control = control, Font = current, HasOwnFont = own, IsRoot = parentFont == null });
            foreach (Control child in control.Controls) CaptureFonts(child, current, entries);
        }

        private static float FontFactor(string size) { return size == "small" ? 0.9F : size == "large" ? 1.15F : 1F; }

        private static void ApplyDwmTheme(Form form, string theme)
        {
            try
            {
                int dark = theme == "dark" ? 1 : 0;
                int result = DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int));
                if (result != 0) DwmSetWindowAttribute(form.Handle, 19, ref dark, sizeof(int));
            }
            catch { }
        }

        private static void ApplyRoundedWindow(Form form)
        {
            if (form.WindowState == FormWindowState.Maximized) { form.Region = null; return; }
            try
            {
                int preference = 2;
                if (DwmSetWindowAttribute(form.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int)) == 0) return;
            }
            catch { }
            int radius = form.FormBorderStyle == FormBorderStyle.None ? 18 : 12;
            using (GraphicsPath path = RoundedPath(new Rectangle(0, 0, form.Width, form.Height), radius)) form.Region = new Region(path);
        }

        private static GraphicsPath RoundedPath(Rectangle rectangle, int radius)
        {
            int diameter = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter - 1, rectangle.Top, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter - 1, rectangle.Bottom - diameter - 1, diameter, diameter, 0, 90);
            path.AddArc(rectangle.Left, rectangle.Bottom - diameter - 1, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void ApplyTheme(Control root, string theme)
        {
            bool dark = theme == "dark";
            Color background = dark ? Color.FromArgb(17, 24, 39) : Color.FromArgb(247, 248, 250);
            Color surface = dark ? Color.FromArgb(31, 41, 55) : Color.White;
            Color input = surface;
            Color text = dark ? Color.FromArgb(229, 231, 235) : Color.FromArgb(30, 41, 59);
            Color muted = dark ? Color.FromArgb(148, 163, 184) : Color.FromArgb(100, 116, 139);
            Color border = dark ? Color.FromArgb(51, 65, 85) : Color.FromArgb(226, 232, 240);
            ApplyControl(root, dark, background, surface, input, text, muted, border);
            ApplyPaneChrome(root, dark, text, border);
            Form form = root as Form;
            if (form != null && form.IsHandleCreated) ApplyDwmTheme(form, theme);
        }

        private static void ApplyControl(Control control, bool dark, Color background, Color surface, Color input, Color text, Color muted, Color border)
        {
            if (control is Form) { control.BackColor = background; control.ForeColor = text; }
            else if (control is TextBoxBase || control is ListBox || control is TreeView || control is DataGridView || control is NumericUpDown || control is ComboBox)
            { control.BackColor = input; control.ForeColor = text; }
            else if (control is Button)
            {
                Button button = (Button)control;
                button.FlatStyle = FlatStyle.Flat; button.BackColor = input; button.ForeColor = text; button.FlatAppearance.BorderColor = border; button.FlatAppearance.MouseOverBackColor = dark ? Color.FromArgb(51, 65, 85) : Color.FromArgb(239, 246, 255);
            }
            else if (control is Label) { control.ForeColor = control.ForeColor == Color.DimGray ? muted : text; control.BackColor = Color.Transparent; }
            else if (control is Panel || control is TableLayoutPanel || control is SplitContainer) control.BackColor = surface;

            DataGridView grid = control as DataGridView;
            if (grid != null)
            {
                grid.BackgroundColor = surface; grid.GridColor = border; grid.BorderStyle = BorderStyle.None;
                grid.EnableHeadersVisualStyles = false;
                grid.ColumnHeadersDefaultCellStyle.BackColor = dark ? Color.FromArgb(31, 41, 55) : Color.FromArgb(248, 250, 252);
                grid.ColumnHeadersDefaultCellStyle.ForeColor = text;
                grid.DefaultCellStyle.BackColor = surface; grid.DefaultCellStyle.ForeColor = text;
                grid.DefaultCellStyle.SelectionBackColor = dark ? Color.FromArgb(59, 130, 246) : Color.FromArgb(37, 99, 235); grid.DefaultCellStyle.SelectionForeColor = Color.White;
            }
            ToolStrip strip = control as ToolStrip;
            if (strip != null)
            {
                strip.BackColor = surface; strip.ForeColor = text;
                strip.Renderer = new ToolStripProfessionalRenderer(new FlatColorTable(dark));
                ApplyToolStripItems(strip.Items, dark, surface, text);
            }
            if (control.ContextMenuStrip != null)
            {
                control.ContextMenuStrip.BackColor = surface; control.ContextMenuStrip.ForeColor = text;
                control.ContextMenuStrip.Renderer = new ToolStripProfessionalRenderer(new FlatColorTable(dark));
                ApplyToolStripItems(control.ContextMenuStrip.Items, dark, surface, text);
            }
            foreach (Control child in control.Controls) ApplyControl(child, dark, background, surface, input, text, muted, border);
        }

        private static void ApplyPaneChrome(Control root, bool dark, Color text, Color border)
        {
            Color divider = dark ? Color.FromArgb(71, 85, 105) : Color.FromArgb(203, 213, 225);
            Color library = dark ? Color.FromArgb(24, 34, 52) : Color.FromArgb(241, 245, 249);
            Color records = dark ? Color.FromArgb(31, 41, 55) : Color.White;
            Color viewer = dark ? Color.FromArgb(22, 32, 50) : Color.FromArgb(248, 250, 252);
            Color header = dark ? Color.FromArgb(39, 52, 73) : Color.FromArgb(238, 242, 247);
            SplitContainer left = FindNamed(root, "MainLeftSplit") as SplitContainer;
            SplitContainer content = FindNamed(root, "MainContentSplit") as SplitContainer;
            if (left != null) { left.BackColor = divider; left.Panel1.BackColor = library; left.Panel2.BackColor = records; }
            if (content != null) { content.BackColor = divider; content.Panel1.BackColor = records; content.Panel2.BackColor = viewer; }
            SetBackColor(root, "LibraryPane", library);
            SetBackColor(root, "RecordsPane", records);
            SetBackColor(root, "ViewerPane", viewer);
            SetBackColor(root, "LibraryTree", library);
            SetBackColor(root, "ViewerText", viewer);
            SetBackColor(root, "ViewerAnnotationPanel", viewer);
            SetBackColor(root, "ViewerAnnotationList", viewer);
            SetBackColor(root, "ViewerReferencePanel", viewer);
            foreach (string name in new string[] { "LibraryPaneHeader", "RecordsPaneHeader", "RecordsSearchPanel", "ViewerPaneHeader" })
            {
                Panel panel = FindNamed(root, name) as Panel;
                if (panel == null) continue;
                panel.BackColor = header;
                panel.BorderStyle = BorderStyle.FixedSingle;
            }
            Control tree = FindNamed(root, "LibraryTree");
            if (tree != null) tree.ForeColor = text;
            DataGridView grid = FindNamed(root, "RecordsGrid") as DataGridView;
            if (grid != null) { grid.BackgroundColor = records; grid.DefaultCellStyle.BackColor = records; grid.GridColor = border; }
        }

        private static void SetBackColor(Control root, string name, Color color)
        {
            Control control = FindNamed(root, name);
            if (control != null) control.BackColor = color;
        }

        private static Control FindNamed(Control root, string name)
        {
            if (root == null) return null;
            if (root.Name == name) return root;
            foreach (Control child in root.Controls)
            {
                Control found = FindNamed(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private static void ApplyToolStripItems(ToolStripItemCollection items, bool dark, Color surface, Color text)
        {
            foreach (ToolStripItem item in items)
            {
                item.BackColor = surface;
                item.ForeColor = text;
                ToolStripDropDownItem drop = item as ToolStripDropDownItem;
                if (drop == null) continue;
                drop.DropDown.BackColor = surface;
                drop.DropDown.ForeColor = text;
                drop.DropDown.Renderer = new ToolStripProfessionalRenderer(new FlatColorTable(dark));
                ApplyToolStripItems(drop.DropDownItems, dark, surface, text);
            }
        }

        private sealed class FlatColorTable : ProfessionalColorTable
        {
            private readonly bool dark;
            public FlatColorTable(bool dark) { this.dark = dark; UseSystemColors = false; }
            public override Color ToolStripDropDownBackground { get { return dark ? Color.FromArgb(31, 41, 55) : Color.White; } }
            public override Color MenuItemSelected { get { return dark ? Color.FromArgb(51, 65, 85) : Color.FromArgb(239, 246, 255); } }
            public override Color MenuItemBorder { get { return dark ? Color.FromArgb(59, 130, 246) : Color.FromArgb(37, 99, 235); } }
            public override Color MenuBorder { get { return MenuItemBorder; } }
            public override Color ImageMarginGradientBegin { get { return ToolStripDropDownBackground; } }
            public override Color ImageMarginGradientMiddle { get { return ToolStripDropDownBackground; } }
            public override Color ImageMarginGradientEnd { get { return ToolStripDropDownBackground; } }
            public override Color SeparatorDark { get { return MenuItemBorder; } }
            public override Color SeparatorLight { get { return ToolStripDropDownBackground; } }
        }

        private sealed class FontScaleEntry
        {
            public Control Control;
            public Font Font;
            public bool HasOwnFont;
            public bool IsRoot;
        }
    }
}
