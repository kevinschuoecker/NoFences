using NoFences.Model;
using NoFences.Util;
using NoFences.Win32;
using Peter;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using static NoFences.Win32.WindowUtil;

namespace NoFences
{
    public partial class FenceWindow : Form
    {
        private int logicalTitleHeight;
        private int titleHeight;
        private const int titleOffset = 3;
        private const int textHeight = 35;
        private const int itemPadding = 15;
        private const float shadowDist = 1.5f;

        private int iconSize;
        private int itemWidth;
        private int itemHeight;

        private readonly FenceInfo fenceInfo;

        public FenceInfo FenceInfo => fenceInfo;

        private Color baseColor = Color.Black;

        private FileSystemWatcher portalWatcher;

        private bool IsPortal => !string.IsNullOrEmpty(fenceInfo.TargetFolder) && Directory.Exists(fenceInfo.TargetFolder);

        private Font titleFont;
        private Font iconFont;

        private string selectedItem;
        private string hoveringItem;
        private bool shouldUpdateSelection;
        private bool shouldRunDoubleClick;
        private bool hasSelectionUpdated;
        private bool hasHoverUpdated;
        private bool isMinified;
        private int prevHeight;

        private int scrollHeight;
        private int scrollOffset;

        private bool isDraggingScrollbar;
        private int scrollDragStartY;
        private int scrollDragStartOffset;

        private TextBox searchBox;

        private string SearchQuery => (searchBox != null && searchBox.Visible) ? searchBox.Text.Trim() : "";

        // Transient navigation inside a folder portal (null = portal root).
        private string portalCurrentPath;

        private string CurrentPortalFolder => portalCurrentPath ?? fenceInfo.TargetFolder;

        // Items added recently get a short visual highlight.
        private readonly Dictionary<string, DateTime> recentItems = new Dictionary<string, DateTime>();
        private readonly Timer highlightTimer = new Timer { Interval = 500 };

        // Drag & drop of items out of this fence.
        private string pendingDragItem;
        private Point dragStartPoint;
        private const string FenceItemDragFormat = "NoFencesItem";

        private readonly ThrottledExecution throttledMove = new ThrottledExecution(TimeSpan.FromSeconds(4));
        private readonly ThrottledExecution throttledResize = new ThrottledExecution(TimeSpan.FromSeconds(4));

        private readonly ShellContextMenu shellContextMenu = new ShellContextMenu();

        private readonly ThumbnailProvider thumbnailProvider = new ThumbnailProvider();

        private void ReloadFonts()
        {
            var family = new FontFamily("Segoe UI");
            titleFont = new Font(family, (int)Math.Floor(logicalTitleHeight / 2.0));
            iconFont = new Font(family, 9);
        }

        public FenceWindow(FenceInfo fenceInfo)
        {
            InitializeComponent();
            DropShadow.ApplyShadows(this);
            BlurUtil.EnableBlur(Handle);
            BlurUtil.TryEnableRoundedCorners(Handle);
            WindowUtil.HideFromAltTab(Handle);
            DesktopUtil.GlueToDesktop(Handle);
            //DesktopUtil.PreventMinimize(Handle);
            logicalTitleHeight = (fenceInfo.TitleHeight < 16 || fenceInfo.TitleHeight > 100) ? 35 : fenceInfo.TitleHeight;
            titleHeight = LogicalToDeviceUnits(logicalTitleHeight);

            this.MouseWheel += FenceWindow_MouseWheel;
            thumbnailProvider.IconThumbnailLoaded += ThumbnailProvider_IconThumbnailLoaded;

            ReloadFonts();

            AllowDrop = true;


            this.fenceInfo = fenceInfo;
            Text = fenceInfo.Name;
            Location = new Point(fenceInfo.PosX, fenceInfo.PosY);

            Width = fenceInfo.Width;
            Height = fenceInfo.Height;

            prevHeight = Height;
            lockedToolStripMenuItem.Checked = fenceInfo.Locked;
            minifyToolStripMenuItem.Checked = fenceInfo.CanMinify;

            ApplyIconSize();
            ApplyCustomColor();
            BuildExtraMenuItems();
            SetupPortalWatcher();
            InitSearchBox();

            this.MouseDown += FenceWindow_MouseDown;
            this.MouseUp += FenceWindow_MouseUp;
            this.ResizeEnd += (s, e) => SnapToNeighbors();

            highlightTimer.Tick += (s, e) =>
            {
                var cutoff = DateTime.Now.AddSeconds(-8);
                foreach (var key in recentItems.Where(kv => kv.Value < cutoff).Select(kv => kv.Key).ToList())
                    recentItems.Remove(key);
                if (recentItems.Count == 0)
                    highlightTimer.Stop();
                Invalidate();
            };

            Minify();
        }

        /// <summary>Marks an item as freshly added so it gets a short highlight.</summary>
        public void NotifyItemAdded(string path)
        {
            recentItems[path] = DateTime.Now;
            highlightTimer.Start();
            Invalidate();
        }

        private void NavigateInto(string folder)
        {
            portalCurrentPath = folder;
            scrollOffset = 0;
            SetupPortalWatcher();
            Invalidate();
        }

        private void NavigateUp()
        {
            var parent = Path.GetDirectoryName(CurrentPortalFolder);
            portalCurrentPath = (parent == null || string.Equals(parent, fenceInfo.TargetFolder, StringComparison.OrdinalIgnoreCase))
                ? null
                : parent;
            scrollOffset = 0;
            SetupPortalWatcher();
            Invalidate();
        }

        private void InitSearchBox()
        {
            searchBox = new TextBox
            {
                Visible = false,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.White,
                Font = iconFont,
                Width = LogicalToDeviceUnits(140)
            };
            searchBox.TextChanged += (s, e) =>
            {
                scrollOffset = 0;
                Invalidate();
            };
            searchBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    CloseSearch();
                    e.Handled = true;
                }
            };
            Controls.Add(searchBox);
            PositionSearchBox();
        }

        private void PositionSearchBox()
        {
            searchBox.Location = new Point(Width - searchBox.Width - LogicalToDeviceUnits(8),
                Math.Max(2, (titleHeight - searchBox.Height) / 2));
        }

        private void ToggleSearch()
        {
            if (searchBox.Visible)
            {
                CloseSearch();
                return;
            }
            PositionSearchBox();
            searchBox.Visible = true;
            searchBox.Focus();
            Invalidate();
        }

        private void CloseSearch()
        {
            searchBox.Text = "";
            searchBox.Visible = false;
            Invalidate();
        }

        private void ApplyIconSize()
        {
            iconSize = (fenceInfo.IconSize == 48 || fenceInfo.IconSize == 64) ? fenceInfo.IconSize : 32;
            itemWidth = iconSize + 43;
            itemHeight = iconSize + itemPadding + textHeight;
        }

        private void ApplyCustomColor()
        {
            baseColor = Color.Black;
            if (!string.IsNullOrEmpty(fenceInfo.CustomColor))
            {
                try
                {
                    baseColor = ColorTranslator.FromHtml(fenceInfo.CustomColor);
                }
                catch
                {
                    // Invalid stored color - fall back to the default dark look.
                }
            }
        }

        private ToolStripMenuItem portalMenuItem;
        private ToolStripMenuItem smallIconsMenuItem;
        private ToolStripMenuItem mediumIconsMenuItem;
        private ToolStripMenuItem largeIconsMenuItem;
        private ToolStripMenuItem sortMenuItem;
        private ToolStripMenuItem opacityMenuItem;

        private static readonly string[] SortModeNames = { "Manual order", "By name", "By type", "By date" };
        private static readonly int[] OpacityValues = { 15, 30, 40, 55, 70, 85 };

        private void BuildExtraMenuItems()
        {
            var insertAt = appContextMenu.Items.IndexOf(toolStripSeparator1);

            var searchItem = new ToolStripMenuItem("Search in fence...");
            searchItem.Click += (s, e) => ToggleSearch();

            var colorItem = new ToolStripMenuItem("Fence color...");
            colorItem.Click += (s, e) => PickColor();

            var resetColorItem = new ToolStripMenuItem("Reset color");
            resetColorItem.Click += (s, e) => SetColor("");

            smallIconsMenuItem = new ToolStripMenuItem("Small (32)");
            smallIconsMenuItem.Click += (s, e) => SetIconSize(32);
            mediumIconsMenuItem = new ToolStripMenuItem("Medium (48)");
            mediumIconsMenuItem.Click += (s, e) => SetIconSize(48);
            largeIconsMenuItem = new ToolStripMenuItem("Large (64)");
            largeIconsMenuItem.Click += (s, e) => SetIconSize(64);
            var iconSizeItem = new ToolStripMenuItem("Icon size");
            iconSizeItem.DropDownItems.AddRange(new ToolStripItem[] { smallIconsMenuItem, mediumIconsMenuItem, largeIconsMenuItem });

            sortMenuItem = new ToolStripMenuItem("Sort items");
            for (var i = 0; i < SortModeNames.Length; i++)
            {
                var mode = i;
                var item = new ToolStripMenuItem(SortModeNames[i]);
                item.Click += (s, e) => SetSortMode(mode);
                sortMenuItem.DropDownItems.Add(item);
            }

            opacityMenuItem = new ToolStripMenuItem("Background opacity");
            foreach (var value in OpacityValues)
            {
                var percent = value;
                var item = new ToolStripMenuItem(percent + " %");
                item.Click += (s, e) => SetOpacity(percent);
                opacityMenuItem.DropDownItems.Add(item);
            }

            portalMenuItem = new ToolStripMenuItem("Folder portal...");
            portalMenuItem.Click += (s, e) => ConfigurePortal();

            var rulesItem = new ToolStripMenuItem("Auto-sort rules...");
            rulesItem.Click += (s, e) => ConfigureRules();

            var sortNowItem = new ToolStripMenuItem("Sort desktop now");
            sortNowItem.Click += (s, e) => DesktopAutoSorter.ApplyRulesNow();

            var hideAllItem = new ToolStripMenuItem("Hide all fences  (Ctrl+Alt+H)");
            hideAllItem.Click += (s, e) => FenceManager.Instance.ToggleAllFences();

            appContextMenu.Items.Insert(insertAt, searchItem);
            appContextMenu.Items.Insert(insertAt + 1, sortMenuItem);
            appContextMenu.Items.Insert(insertAt + 2, colorItem);
            appContextMenu.Items.Insert(insertAt + 3, resetColorItem);
            appContextMenu.Items.Insert(insertAt + 4, opacityMenuItem);
            appContextMenu.Items.Insert(insertAt + 5, iconSizeItem);
            appContextMenu.Items.Insert(insertAt + 6, portalMenuItem);
            appContextMenu.Items.Insert(insertAt + 7, rulesItem);
            appContextMenu.Items.Insert(insertAt + 8, sortNowItem);
            appContextMenu.Items.Insert(insertAt + 9, new ToolStripSeparator());
            appContextMenu.Items.Insert(insertAt + 10, hideAllItem);
        }

        private void SetSortMode(int mode)
        {
            fenceInfo.SortMode = mode;
            Save();
            Refresh();
        }

        private void SetOpacity(int percent)
        {
            fenceInfo.Transparency = percent;
            Save();
            Refresh();
        }

        private void PickColor()
        {
            using (var dialog = new ColorDialog { Color = baseColor, FullOpen = true })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    SetColor(ColorTranslator.ToHtml(dialog.Color));
            }
        }

        private void SetColor(string color)
        {
            fenceInfo.CustomColor = color;
            ApplyCustomColor();
            Save();
            Refresh();
        }

        private void SetIconSize(int size)
        {
            fenceInfo.IconSize = size;
            ApplyIconSize();
            Save();
            Refresh();
        }

        private void ConfigurePortal()
        {
            if (!string.IsNullOrEmpty(fenceInfo.TargetFolder))
            {
                if (MessageBox.Show(this, "This fence mirrors\n" + fenceInfo.TargetFolder + "\n\nDisable the folder portal?",
                        "Folder portal", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    fenceInfo.TargetFolder = "";
                    SetupPortalWatcher();
                    Save();
                    Refresh();
                }
                return;
            }

            using (var dialog = new FolderBrowserDialog { Description = "Choose a folder to display inside this fence (e.g. Downloads)." })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    fenceInfo.TargetFolder = dialog.SelectedPath;
                    SetupPortalWatcher();
                    Save();
                    Refresh();
                }
            }
        }

        private void ConfigureRules()
        {
            using (var dialog = new PromptDialog("Auto-sort rules",
                "Wildcard patterns separated by semicolons, e.g. *.png; screenshot*. New desktop files matching a pattern are added to this fence.",
                fenceInfo.AutoSortPatterns))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    fenceInfo.AutoSortPatterns = dialog.Value;
                    Save();
                }
            }
        }

        private void SetupPortalWatcher()
        {
            portalWatcher?.Dispose();
            portalWatcher = null;
            portalFiles = null;

            if (!IsPortal)
            {
                portalCurrentPath = null;
                return;
            }

            if (portalCurrentPath != null && !Directory.Exists(portalCurrentPath))
                portalCurrentPath = null;

            try
            {
                portalWatcher = new FileSystemWatcher(CurrentPortalFolder)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                    EnableRaisingEvents = true
                };
                FileSystemEventHandler refresh = (s, e) => BeginInvoke((Action)RefreshPortal);
                portalWatcher.Created += refresh;
                portalWatcher.Deleted += refresh;
                portalWatcher.Renamed += (s, e) => BeginInvoke((Action)RefreshPortal);
            }
            catch
            {
                // Folder vanished - portal shows as empty until it comes back.
            }
        }

        private List<string> portalFiles;

        private void RefreshPortal()
        {
            portalFiles = null;
            Invalidate();
        }

        private IEnumerable<string> CurrentFiles()
        {
            if (!IsPortal)
                return ApplySort(fenceInfo.Files);

            if (portalFiles == null)
            {
                try
                {
                    portalFiles = Directory.EnumerateFileSystemEntries(CurrentPortalFolder)
                        .Where(p => (new FileInfo(p).Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                        .ToList();
                }
                catch
                {
                    portalFiles = new List<string>();
                }
            }
            return ApplySort(portalFiles);
        }

        private IEnumerable<string> ApplySort(IEnumerable<string> files)
        {
            switch (fenceInfo.SortMode)
            {
                case 1:
                    return files.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
                case 2:
                    // Folders first, then grouped by extension.
                    return files.OrderBy(f => Directory.Exists(f) ? "" : Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)
                        .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
                case 3:
                    return files.OrderByDescending(GetLastWriteSafe);
                default:
                    return files;
            }
        }

        private static DateTime GetLastWriteSafe(string path)
        {
            try
            {
                return File.GetLastWriteTime(path);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        protected override void WndProc(ref Message m)
        {
            //Console.WriteLine(m.Msg.ToString("X4"));

            // Remove border
            if (m.Msg == 0x0083)
            {
                m.Result = IntPtr.Zero;
                return;
            }

            // Mouse leave
            var myrect = new Rectangle(Location, Size);
            if (m.Msg == 0x02a2 && !myrect.IntersectsWith(new Rectangle(MousePosition, new Size(1, 1))))
            {
                Minify();
            }

            // Prevent maximize
            if ((m.Msg == WM_SYSCOMMAND) && m.WParam.ToInt32() == 0xF032)
            {
                m.Result = IntPtr.Zero;
                return;
            }

            // Prevent foreground
            if (m.Msg == WM_SETFOCUS)
            {
                SetWindowPos(Handle, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
                return;
            }

            // Other messages
            base.WndProc(ref m);

            // If not locked and using the left mouse button
            if (MouseButtons == MouseButtons.Right || lockedToolStripMenuItem.Checked)
                return;

            // Then, allow dragging and resizing
            if (m.Msg == WM_NCHITTEST)
            {

                var pt = PointToClient(new Point(m.LParam.ToInt32()));

                if ((int)m.Result == HTCLIENT && pt.Y < titleHeight && !(searchBox != null && searchBox.Visible && searchBox.Bounds.Contains(pt)))     // drag the form
                {
                    m.Result = (IntPtr)HTCAPTION;
                    FenceWindow_MouseEnter(null, null);
                }

                // The scrollbar lives on the right edge; don't let the resize grip eat its clicks.
                var overScrollbar = scrollHeight > 0 && pt.X >= Width - 14 && pt.Y > titleHeight + 10 && pt.Y < Height - 10;

                if (pt.X < 10 && pt.Y < 10)
                    m.Result = new IntPtr(HTTOPLEFT);
                else if (pt.X > (Width - 10) && pt.Y < 10)
                    m.Result = new IntPtr(HTTOPRIGHT);
                else if (pt.X < 10 && pt.Y > (Height - 10))
                    m.Result = new IntPtr(HTBOTTOMLEFT);
                else if (pt.X > (Width - 10) && pt.Y > (Height - 10))
                    m.Result = new IntPtr(HTBOTTOMRIGHT);
                else if (pt.Y > (Height - 10))
                    m.Result = new IntPtr(HTBOTTOM);
                else if (pt.X < 10)
                    m.Result = new IntPtr(HTLEFT);
                else if (pt.X > (Width - 10) && !overScrollbar)
                    m.Result = new IntPtr(HTRIGHT);
            }
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(this, "Really remove this fence?", "Remove", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                // Give the desktop its icons back before the fence disappears.
                foreach (var file in fenceInfo.Files)
                    DesktopIconHider.Unhide(file);
                FenceManager.Instance.RemoveFence(fenceInfo);
                Close();
            }
        }

        private void deleteItemToolStripMenuItem_Click(object sender, EventArgs e)
        {
            RemoveItemFromFence(hoveringItem);
        }

        private void RemoveItemFromFence(string path)
        {
            if (path == null)
                return;
            fenceInfo.Files.Remove(path);
            DesktopIconHider.Unhide(path);
            hoveringItem = null;
            Save();
            Refresh();
        }

        private void contextMenuStrip1_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // In portal mode items are real files, so "remove from fence" makes no sense.
            deleteItemToolStripMenuItem.Visible = hoveringItem != null && !IsPortal;

            smallIconsMenuItem.Checked = iconSize == 32;
            mediumIconsMenuItem.Checked = iconSize == 48;
            largeIconsMenuItem.Checked = iconSize == 64;
            portalMenuItem.Text = string.IsNullOrEmpty(fenceInfo.TargetFolder) ? "Folder portal..." : "Disable folder portal...";

            for (var i = 0; i < sortMenuItem.DropDownItems.Count; i++)
                ((ToolStripMenuItem)sortMenuItem.DropDownItems[i]).Checked = fenceInfo.SortMode == i;
            for (var i = 0; i < OpacityValues.Length; i++)
                ((ToolStripMenuItem)opacityMenuItem.DropDownItems[i]).Checked = fenceInfo.Transparency == OpacityValues[i];
        }

        private void FenceWindow_DragEnter(object sender, DragEventArgs e)
        {
            if ((e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(FenceItemDragFormat))
                && !lockedToolStripMenuItem.Checked)
                e.Effect = DragDropEffects.Move;
        }

        private void FenceWindow_DragDrop(object sender, DragEventArgs e)
        {
            // Item dragged over from another fence.
            if (e.Data.GetDataPresent(FenceItemDragFormat))
            {
                e.Effect = DragDropEffects.None;
                var path = e.Data.GetData(FenceItemDragFormat) as string;
                if (path == null)
                    return;

                if (IsPortal)
                {
                    try
                    {
                        var destination = Path.Combine(CurrentPortalFolder, Path.GetFileName(path));
                        if (!destination.Equals(path, StringComparison.OrdinalIgnoreCase) && !ItemExists(destination))
                        {
                            if (File.Exists(path))
                                File.Move(path, destination);
                            else if (Directory.Exists(path))
                                Directory.Move(path, destination);
                            e.Effect = DragDropEffects.Move;
                        }
                    }
                    catch
                    {
                        // Locked or otherwise immovable - the source keeps its item.
                    }
                }
                else if (!fenceInfo.Files.Contains(path) && ItemExists(path))
                {
                    fenceInfo.Files.Add(path);
                    DesktopIconHider.HideIfEnabled(path);
                    NotifyItemAdded(path);
                    Save();
                    e.Effect = DragDropEffects.Move;
                }
                Refresh();
                return;
            }

            var dropped = (string[])e.Data.GetData(DataFormats.FileDrop);

            if (IsPortal)
            {
                // A portal mirrors a real folder, so dropping means moving the file there.
                foreach (var file in dropped)
                {
                    try
                    {
                        var destination = Path.Combine(CurrentPortalFolder, Path.GetFileName(file));
                        if (destination.Equals(file, StringComparison.OrdinalIgnoreCase) || ItemExists(destination))
                            continue;
                        if (File.Exists(file))
                            File.Move(file, destination);
                        else if (Directory.Exists(file))
                            Directory.Move(file, destination);
                    }
                    catch
                    {
                        // Locked or otherwise immovable file - skip it.
                    }
                }
                Refresh();
                return;
            }

            foreach (var file in dropped)
            {
                if (!fenceInfo.Files.Contains(file) && ItemExists(file))
                {
                    fenceInfo.Files.Add(file);
                    DesktopIconHider.HideIfEnabled(file);
                    NotifyItemAdded(file);
                }
            }
            Save();
            Refresh();
        }

        private void FenceWindow_Resize(object sender, EventArgs e)
        {
            if (searchBox != null)
                PositionSearchBox();

            throttledResize.Run(() =>
            {
                fenceInfo.Width = Width;
                fenceInfo.Height = isMinified ? prevHeight : Height;
                Save();
            });

            Refresh();
        }

        private void FenceWindow_MouseMove(object sender, MouseEventArgs e)
        {
            HandleItemDrag(e);
            if (isDraggingScrollbar)
            {
                var dragRange = ScrollbarTrackRect.Height - ScrollbarThumbRect.Height;
                if (dragRange > 0)
                {
                    scrollOffset = scrollDragStartOffset + (e.Y - scrollDragStartY) * scrollHeight / dragRange;
                    ClampScrollOffset();
                }
            }
            Refresh();
        }

        private void FenceWindow_MouseEnter(object sender, EventArgs e)
        {
            if (minifyToolStripMenuItem.Checked && isMinified)
            {
                isMinified = false;
                Height = prevHeight;
            }
        }

        private void FenceWindow_MouseLeave(object sender, EventArgs e)
        {
            Minify();
            selectedItem = null;
            Refresh();
        }

        private void Minify()
        {
            // Don't roll up while the user is searching.
            if (searchBox != null && searchBox.Visible)
                return;
            if (minifyToolStripMenuItem.Checked && !isMinified)
            {
                isMinified = true;
                prevHeight = Height;
                Height = titleHeight;
                Refresh();
            }
        }

        private void minifyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (isMinified)
            {
                Height = prevHeight;
                isMinified = false;
            }
            fenceInfo.CanMinify = minifyToolStripMenuItem.Checked;
            Save();

        }

        private void FenceWindow_Click(object sender, EventArgs e)
        {
            shouldUpdateSelection = true;
            Refresh();
        }

        private void FenceWindow_DoubleClick(object sender, EventArgs e)
        {
            shouldRunDoubleClick = true;
            Refresh();
        }

        private void FenceWindow_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.Clip = new Region(ClientRectangle);
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // Background (tinted with the custom fence color and per-fence opacity)
            var bgAlpha = Math.Max(0, Math.Min(255, fenceInfo.Transparency * 255 / 100));
            e.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(bgAlpha, baseColor)), ClientRectangle);

            // Title
            e.Graphics.DrawString(Text, titleFont, Brushes.White, new PointF(Width / 2, titleOffset), new StringFormat { Alignment = StringAlignment.Center });
            e.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(50, Color.Black)), new RectangleF(0, 0, Width, titleHeight));

            // Items
            var x = itemPadding;
            var y = itemPadding;
            scrollHeight = 0;
            e.Graphics.Clip = new Region(new Rectangle(0, titleHeight, Width, Height - titleHeight));
            foreach (var item in BuildRenderItems())
            {
                RenderEntry(e.Graphics, item, x, y + titleHeight - scrollOffset);

                var itemBottom = y + itemHeight;
                if (itemBottom > scrollHeight)
                    scrollHeight = itemBottom;

                x += itemWidth + itemPadding;
                if (x + itemWidth > Width)
                {
                    x = itemPadding;
                    y += itemHeight + itemPadding;
                }
            }

            scrollHeight -= (ClientRectangle.Height - titleHeight);

            // Scroll bar (proportional, draggable thumb)
            if (scrollHeight > 0)
            {
                scrollOffset = Math.Min(scrollOffset, scrollHeight);
                e.Graphics.Clip = new Region(ClientRectangle);
                e.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(40, Color.White)), ScrollbarTrackRect);
                e.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(isDraggingScrollbar ? 200 : 120, Color.White)), ScrollbarThumbRect);
            }
            else
            {
                scrollOffset = 0;
            }



            // Click handlers
            if (shouldUpdateSelection && !hasSelectionUpdated)
                selectedItem = null;

            if (!hasHoverUpdated)
                hoveringItem = null;

            shouldRunDoubleClick = false;
            shouldUpdateSelection = false;
            hasSelectionUpdated = false;
            hasHoverUpdated = false;
        }

        private class RenderItem
        {
            public FenceEntry Entry;
            public string Name;
            public Action Open;
        }

        private List<RenderItem> BuildRenderItems()
        {
            var items = new List<RenderItem>();
            var query = SearchQuery;

            // Inside a navigated portal, the first tile goes back up.
            if (IsPortal && portalCurrentPath != null)
            {
                var parent = Path.GetDirectoryName(CurrentPortalFolder) ?? fenceInfo.TargetFolder;
                var upEntry = FenceEntry.FromPath(parent);
                if (upEntry != null)
                    items.Add(new RenderItem { Entry = upEntry, Name = "..", Open = NavigateUp });
            }

            foreach (var file in CurrentFiles())
            {
                var entry = FenceEntry.FromPath(file);
                if (entry == null)
                    continue;
                if (query.Length > 0 && entry.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                // Portal folders navigate within the fence instead of opening Explorer.
                var open = (IsPortal && entry.Type == EntryType.Folder)
                    ? () => NavigateInto(entry.Path)
                    : (Action)entry.Open;
                items.Add(new RenderItem { Entry = entry, Name = entry.Name, Open = open });
            }
            return items;
        }

        private void RenderEntry(Graphics g, RenderItem item, int x, int y)
        {
            var entry = item.Entry;
            var icon = entry.ExtractIcon(thumbnailProvider, iconSize);
            var name = item.Name;

            var textPosition = new PointF(x, y + iconSize + 5);
            var textMaxSize = new SizeF(itemWidth, textHeight);

            var stringFormat = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };

            var textSize = g.MeasureString(name, iconFont, textMaxSize, stringFormat);
            var outlineRect = new Rectangle(x - 2, y - 2, itemWidth + 2, iconSize + (int)textSize.Height + 5 + 2);
            var outlineRectInner = outlineRect.Shrink(1);

            var mousePos = PointToClient(MousePosition);
            var mouseOver = mousePos.X >= x && mousePos.Y >= y && mousePos.X < x + outlineRect.Width && mousePos.Y < y + outlineRect.Height;

            if (mouseOver)
            {
                hoveringItem = entry.Path;
                hasHoverUpdated = true;
            }

            if (mouseOver && shouldUpdateSelection)
            {
                selectedItem = entry.Path;
                shouldUpdateSelection = false;
                hasSelectionUpdated = true;
            }

            if (mouseOver && shouldRunDoubleClick)
            {
                shouldRunDoubleClick = false;
                item.Open();
            }

            if (selectedItem == entry.Path)
            {
                if (mouseOver)
                {
                    g.DrawRectangle(new Pen(Color.FromArgb(120, SystemColors.ActiveBorder)), outlineRectInner);
                    g.FillRectangle(new SolidBrush(Color.FromArgb(100, SystemColors.GradientActiveCaption)), outlineRect);
                }
                else
                {
                    g.DrawRectangle(new Pen(Color.FromArgb(120, SystemColors.ActiveBorder)), outlineRectInner);
                    g.FillRectangle(new SolidBrush(Color.FromArgb(80, SystemColors.GradientInactiveCaption)), outlineRect);
                }
            }
            else
            {
                if (mouseOver)
                {
                    g.DrawRectangle(new Pen(Color.FromArgb(120, SystemColors.ActiveBorder)), outlineRectInner);
                    g.FillRectangle(new SolidBrush(Color.FromArgb(80, SystemColors.ActiveCaption)), outlineRect);
                }
            }

            // Freshly added items get a short golden highlight.
            if (recentItems.ContainsKey(entry.Path))
            {
                g.DrawRectangle(new Pen(Color.FromArgb(220, Color.Gold), 1.5f), outlineRectInner);
                g.FillRectangle(new SolidBrush(Color.FromArgb(35, Color.Gold)), outlineRect);
            }

            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawIcon(icon, new Rectangle(x + itemWidth / 2 - iconSize / 2, y, iconSize, iconSize));
            g.DrawString(name, iconFont, new SolidBrush(Color.FromArgb(180, 15, 15, 15)), new RectangleF(textPosition.Move(shadowDist, shadowDist), textMaxSize), stringFormat);
            g.DrawString(name, iconFont, Brushes.White, new RectangleF(textPosition, textMaxSize), stringFormat);
        }

        private void renameToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var dialog = new EditDialog(Text);
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                Text = dialog.NewName;
                fenceInfo.Name = Text;
                Refresh();
                Save();
            }
        }

        private void newFenceToolStripMenuItem_Click(object sender, EventArgs e)
        {
            FenceManager.Instance.CreateFence("New fence");
        }

        private void FenceWindow_FormClosed(object sender, FormClosedEventArgs e)
        {
            portalWatcher?.Dispose();
            if (Application.OpenForms.Count == 0)
                Application.Exit();
        }

        private readonly object saveLock = new object();
        private void Save()
        {
            lock (saveLock)
            {
                FenceManager.Instance.UpdateFence(fenceInfo);
            }
        }

        private void FenceWindow_LocationChanged(object sender, EventArgs e)
        {
            throttledMove.Run(() =>
            {
                fenceInfo.PosX = Location.X;
                fenceInfo.PosY = Location.Y;
                Save();
            });
        }

        private void lockedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            fenceInfo.Locked = lockedToolStripMenuItem.Checked;
            Save();
        }

        private void FenceWindow_Load(object sender, EventArgs e)
        {

        }

        private void titleSizeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var dialog = new HeightDialog(fenceInfo.TitleHeight);
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                fenceInfo.TitleHeight = dialog.TitleHeight;
                logicalTitleHeight = dialog.TitleHeight;
                titleHeight = LogicalToDeviceUnits(logicalTitleHeight);
                ReloadFonts();
                Minify();
                if (isMinified)
                {
                    Height = titleHeight;
                }
                Refresh();
                Save();
            }
        }

        private void FenceWindow_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
                return;

            if (hoveringItem != null && !ModifierKeys.HasFlag(Keys.Shift))
            {
                var itemPath = hoveringItem;
                // Portals show real files, so removing only the fence reference makes no sense there.
                var customText = IsPortal ? null : "Remove from fence";
                shellContextMenu.ShowContextMenu(new[] { new FileInfo(itemPath) }, MousePosition,
                    customText, () => RemoveItemFromFence(itemPath));
            }
            else
            {
                appContextMenu.Show(this, e.Location);
            }
        }

        private void FenceWindow_MouseWheel(object sender, MouseEventArgs e)
        {
            if (scrollHeight < 1)
                return;

            scrollOffset -= Math.Sign(e.Delta) * 36;
            ClampScrollOffset();
            Invalidate();
        }

        private void ClampScrollOffset()
        {
            if (scrollOffset < 0)
                scrollOffset = 0;
            if (scrollOffset > scrollHeight)
                scrollOffset = scrollHeight;
        }

        private Rectangle ScrollbarTrackRect => new Rectangle(Width - 10, titleHeight + 2, 7, Height - titleHeight - 4);

        private Rectangle ScrollbarThumbRect
        {
            get
            {
                var track = ScrollbarTrackRect;
                var viewport = Height - titleHeight;
                var total = scrollHeight + viewport;
                var thumbHeight = Math.Max(24, track.Height * viewport / Math.Max(1, total));
                var y = track.Y + (int)((long)(track.Height - thumbHeight) * scrollOffset / Math.Max(1, scrollHeight));
                return new Rectangle(track.X, y, track.Width, thumbHeight);
            }
        }

        private void FenceWindow_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            if (scrollHeight < 1)
            {
                BeginPossibleItemDrag(e);
                return;
            }

            var thumb = ScrollbarThumbRect;
            var track = ScrollbarTrackRect;

            if (!thumb.Contains(e.Location) && track.Contains(e.Location))
            {
                // Jump so the thumb centers on the click, then keep dragging from there.
                var dragRange = track.Height - thumb.Height;
                if (dragRange > 0)
                {
                    scrollOffset = (e.Y - track.Y - thumb.Height / 2) * scrollHeight / dragRange;
                    ClampScrollOffset();
                }
                thumb = ScrollbarThumbRect;
                Invalidate();
            }

            if (thumb.Contains(e.Location))
            {
                isDraggingScrollbar = true;
                scrollDragStartY = e.Y;
                scrollDragStartOffset = scrollOffset;
                Invalidate();
            }
            else
            {
                BeginPossibleItemDrag(e);
            }
        }

        private void BeginPossibleItemDrag(MouseEventArgs e)
        {
            // Portals show real files; moving those between fences is handled by Explorer semantics only.
            if (IsPortal || hoveringItem == null || isDraggingScrollbar)
                return;
            pendingDragItem = hoveringItem;
            dragStartPoint = e.Location;
        }

        private void HandleItemDrag(MouseEventArgs e)
        {
            if (pendingDragItem == null || e.Button != MouseButtons.Left || isDraggingScrollbar)
                return;
            if (Math.Abs(e.X - dragStartPoint.X) <= SystemInformation.DragSize.Width &&
                Math.Abs(e.Y - dragStartPoint.Y) <= SystemInformation.DragSize.Height)
                return;

            var item = pendingDragItem;
            pendingDragItem = null;

            var data = new DataObject();
            data.SetData(FenceItemDragFormat, item);
            var result = DoDragDrop(data, DragDropEffects.Move);

            // The receiving fence took over (it also keeps the desktop-hidden state),
            // so drop only our own reference here.
            if (result == DragDropEffects.Move)
            {
                fenceInfo.Files.Remove(item);
                Save();
                Refresh();
            }
        }

        private void SnapToNeighbors()
        {
            if (lockedToolStripMenuItem.Checked)
                return;

            const int snapDistance = 12;
            var workingArea = Screen.FromControl(this).WorkingArea;

            var candidatesX = new List<int> { workingArea.Left, workingArea.Right - Width };
            var candidatesY = new List<int> { workingArea.Top, workingArea.Bottom - Height };

            foreach (var other in FenceManager.Instance.Windows)
            {
                if (other == this || !other.Visible)
                    continue;
                candidatesX.Add(other.Left);
                candidatesX.Add(other.Right);
                candidatesX.Add(other.Left - Width);
                candidatesX.Add(other.Right - Width);
                candidatesY.Add(other.Top);
                candidatesY.Add(other.Bottom);
                candidatesY.Add(other.Top - Height);
                candidatesY.Add(other.Bottom - Height);
            }

            var newX = Location.X;
            var newY = Location.Y;
            foreach (var candidate in candidatesX)
                if (Math.Abs(candidate - Location.X) <= snapDistance)
                    newX = candidate;
            foreach (var candidate in candidatesY)
                if (Math.Abs(candidate - Location.Y) <= snapDistance)
                    newY = candidate;

            if (newX != Location.X || newY != Location.Y)
                Location = new Point(newX, newY);
        }

        private void FenceWindow_MouseUp(object sender, MouseEventArgs e)
        {
            pendingDragItem = null;
            if (isDraggingScrollbar)
            {
                isDraggingScrollbar = false;
                Invalidate();
            }
        }

        private void ThumbnailProvider_IconThumbnailLoaded(object sender, EventArgs e)
        {
            Invalidate();
        }

        private bool ItemExists(string path)
        {
            return File.Exists(path) || Directory.Exists(path);
        }
    }

}

