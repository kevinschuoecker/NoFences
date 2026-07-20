using FlowGrid.Model;
using FlowGrid.Util;
using FlowGrid.Win32;
using Peter;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using static FlowGrid.Win32.WindowUtil;

namespace FlowGrid
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
        private bool droppedOnSelf;
        private const string FenceItemDragFormat = "FlowGridItem";

        // Fence type helpers.
        private bool IsNote => fenceInfo.FenceType == 1;
        private bool IsWidget => fenceInfo.FenceType >= 2;
        private bool IsNormal => fenceInfo.FenceType == 0;

        // Tabs.
        private bool HasTabs => IsNormal && !IsPortal && fenceInfo.Tabs.Count > 0;
        private int TabStripHeight => HasTabs ? LogicalToDeviceUnits(26) : 0;
        private int ContentTop => titleHeight + TabStripHeight;

        private class TabHit
        {
            public Rectangle Rect;
            public int Index; // -1 = the "+" button
        }
        private readonly List<TabHit> tabHitRects = new List<TabHit>();

        // Sticky note editor.
        private TextBox noteBox;
        private readonly ThrottledExecution throttledNoteSave = new ThrottledExecution(TimeSpan.FromSeconds(2));

        // Widget refresh + system counters.
        private Timer widgetTimer;
        private System.Diagnostics.PerformanceCounter cpuCounter;
        private float lastCpuValue = -1;

        // Plugin widget hosting (FenceType 100).
        private Sdk.IFlowGridWidget pluginWidget;
        private WidgetHost widgetHost;

        private class WidgetHost : Sdk.IWidgetHost
        {
            private readonly FenceWindow owner;
            public WidgetHost(FenceWindow owner) { this.owner = owner; }
            public Color AccentColor => owner.baseColor;
            public Font BaseFont => owner.iconFont;

            public string Settings
            {
                get => owner.fenceInfo.WidgetSettings ?? "";
                set
                {
                    owner.fenceInfo.WidgetSettings = value ?? "";
                    owner.Save();
                }
            }

            public string PromptText(string title, string description, string initialValue)
            {
                using (var dialog = new PromptDialog(title ?? "", description ?? "", initialValue ?? ""))
                    return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.Value : null;
            }

            public void RequestRefresh()
            {
                try
                {
                    owner.BeginInvoke((Action)owner.Invalidate);
                }
                catch
                {
                    // Window is closing - nothing to refresh.
                }
            }
        }

        // Context menu entries contributed by an SDK v3 plugin (rebuilt on every open).
        private readonly List<ToolStripItem> pluginContextItems = new List<ToolStripItem>();

        private void UpdatePluginContextItems()
        {
            foreach (var item in pluginContextItems)
                appContextMenu.Items.Remove(item);
            pluginContextItems.Clear();

            if (!(pluginWidget is Sdk.IFlowGridWidget3 contributor))
                return;

            try
            {
                var items = contributor.GetMenuItems(widgetHost);
                if (items == null || items.Count == 0)
                    return;

                var index = 0;
                foreach (var item in items)
                {
                    var menuItem = new ToolStripMenuItem(item.Text ?? "");
                    var action = item.OnClick;
                    menuItem.Click += (s, e) =>
                    {
                        try
                        {
                            action?.Invoke();
                        }
                        catch
                        {
                            // A faulty plugin must never take the fence down.
                        }
                        Invalidate();
                    };
                    appContextMenu.Items.Insert(index, menuItem);
                    pluginContextItems.Add(menuItem);
                    index++;
                }
                var separator = new ToolStripSeparator();
                appContextMenu.Items.Insert(index, separator);
                pluginContextItems.Add(separator);
            }
            catch
            {
                // Ignore faulty menu contributions.
            }
        }

        // Animation state: content scale while dragging the window plus release bounce,
        // and a fade-in when the fence first appears.
        private readonly Timer animTimer = new Timer { Interval = 15 };
        private float contentScale = 1f;
        private float scaleTarget = 1f;
        private bool bounceQueued;
        private bool fadingIn;

        // Smooth roll-up/expand: animated window height (-1 = idle).
        private int heightAnimTarget = -1;
        private bool HeightAnimating => heightAnimTarget >= 0;

        private void AnimateHeightTo(int target)
        {
            heightAnimTarget = target;
            animTimer.Start();
        }

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
            // Never allow resizing below the title bar - negative content areas
            // break font sizes and layout math.
            MinimumSize = new Size(LogicalToDeviceUnits(100), titleHeight);

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
            this.DragOver += FenceWindow_DragOver;

            InitAnimations();
            if (IsNote)
                InitNoteBox();
            if (fenceInfo.FenceType == 100)
            {
                pluginWidget = PluginManager.Find(fenceInfo.WidgetPlugin);
                widgetHost = new WidgetHost(this);
            }
            if (IsWidget)
                InitWidgetTimer();

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

        private void InitAnimations()
        {
            animTimer.Tick += (s, e) => StepAnimations();

            // Fade in when the fence appears.
            Opacity = 0;
            fadingIn = true;
            Shown += (s, e) => animTimer.Start();
        }

        private void StepAnimations()
        {
            var busy = false;

            if (fadingIn)
            {
                Opacity = Math.Min(1.0, Opacity + 0.09);
                if (Opacity >= 1.0)
                {
                    fadingIn = false;
                    // Opacity animation uses a layered window which suspends the blur; restore it.
                    BlurUtil.EnableBlur(Handle);
                }
                else
                {
                    busy = true;
                }
            }

            if (HeightAnimating)
            {
                var diff = heightAnimTarget - Height;
                if (Math.Abs(diff) <= 2)
                {
                    Height = heightAnimTarget;
                    heightAnimTarget = -1;
                }
                else
                {
                    var step = (int)(diff * 0.35f);
                    if (step == 0)
                        step = Math.Sign(diff);
                    Height += step;
                    busy = true;
                }
            }

            if (Math.Abs(contentScale - scaleTarget) > 0.0015f)
            {
                contentScale += (scaleTarget - contentScale) * 0.35f;
                busy = true;
                Invalidate();
            }
            else if (bounceQueued)
            {
                // Overshoot reached - settle back to normal.
                bounceQueued = false;
                scaleTarget = 1f;
                busy = true;
            }
            else if (contentScale != 1f && scaleTarget == 1f)
            {
                contentScale = 1f;
                Invalidate();
            }

            if (!busy)
                animTimer.Stop();
        }

        protected void OnWindowDragStarted()
        {
            scaleTarget = 0.97f;
            bounceQueued = false;
            animTimer.Start();
        }

        protected void OnWindowDragEnded()
        {
            scaleTarget = 1.015f;
            bounceQueued = true;
            animTimer.Start();
        }

        private void InitNoteBox()
        {
            noteBox = new TextBox
            {
                Multiline = true,
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(37, 34, 20),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 11f),
                ScrollBars = ScrollBars.Vertical,
                Text = fenceInfo.NoteText
            };
            noteBox.TextChanged += (s, e) => throttledNoteSave.Run(SaveNoteText);
            noteBox.Leave += (s, e) => SaveNoteText();
            Controls.Add(noteBox);
            PositionNoteBox();
        }

        private void SaveNoteText()
        {
            if (fenceInfo.NoteText == noteBox.Text)
                return;
            fenceInfo.NoteText = noteBox.Text;
            Save();
        }

        private void PositionNoteBox()
        {
            var margin = LogicalToDeviceUnits(8);
            noteBox.Location = new Point(margin, titleHeight + margin);
            noteBox.Size = new Size(Math.Max(10, Width - 2 * margin), Math.Max(10, Height - titleHeight - 2 * margin));
        }

        private void InitWidgetTimer()
        {
            var interval = 1000;
            if (fenceInfo.FenceType == 100)
            {
                if (pluginWidget == null || pluginWidget.RefreshIntervalMs <= 0)
                    return; // plugin missing or static content - no auto refresh
                interval = Math.Max(100, pluginWidget.RefreshIntervalMs);
            }

            widgetTimer = new Timer { Interval = interval };
            widgetTimer.Tick += (s, e) =>
            {
                if (fenceInfo.FenceType == 3)
                    lastCpuValue = ReadCpuUsage();
                Invalidate();
            };
            widgetTimer.Start();
        }

        private float ReadCpuUsage()
        {
            try
            {
                if (cpuCounter == null)
                    cpuCounter = new System.Diagnostics.PerformanceCounter("Processor", "% Processor Time", "_Total");
                return cpuCounter.NextValue();
            }
            catch
            {
                return -1;
            }
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
        private ToolStripMenuItem searchMenuItem;
        private ToolStripMenuItem iconSizeMenuItem;
        private ToolStripMenuItem rulesMenuItem;
        private ToolStripMenuItem tabsMenuItem;

        private static readonly string[] SortModeNames = { "Manual order", "By name", "By type", "By date" };
        private static readonly int[] OpacityValues = { 15, 30, 40, 55, 70, 85 };

        private void BuildExtraMenuItems()
        {
            var insertAt = appContextMenu.Items.IndexOf(toolStripSeparator1);

            searchMenuItem = new ToolStripMenuItem("Search in fence...");
            searchMenuItem.Click += (s, e) => ToggleSearch();

            tabsMenuItem = new ToolStripMenuItem("Tabs");
            var addTabItem = new ToolStripMenuItem("Add tab...");
            addTabItem.Click += (s, e) => AddTab();
            var renameTabItem = new ToolStripMenuItem("Rename current tab...");
            renameTabItem.Click += (s, e) => RenameTab();
            var removeTabItem = new ToolStripMenuItem("Remove current tab");
            removeTabItem.Click += (s, e) => RemoveTab();
            tabsMenuItem.DropDownItems.AddRange(new ToolStripItem[] { addTabItem, renameTabItem, removeTabItem });

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
            iconSizeMenuItem = new ToolStripMenuItem("Icon size");
            iconSizeMenuItem.DropDownItems.AddRange(new ToolStripItem[] { smallIconsMenuItem, mediumIconsMenuItem, largeIconsMenuItem });

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

            rulesMenuItem = new ToolStripMenuItem("Auto-sort rules...");
            rulesMenuItem.Click += (s, e) => ConfigureRules();

            var sortNowItem = new ToolStripMenuItem("Sort desktop now");
            sortNowItem.Click += (s, e) => DesktopAutoSorter.ApplyRulesNow();

            var hideAllItem = new ToolStripMenuItem("Hide all fences  (Ctrl+Alt+H)");
            hideAllItem.Click += (s, e) => FenceManager.Instance.ToggleAllFences();

            appContextMenu.Items.Insert(insertAt, searchMenuItem);
            appContextMenu.Items.Insert(insertAt + 1, tabsMenuItem);
            appContextMenu.Items.Insert(insertAt + 2, sortMenuItem);
            appContextMenu.Items.Insert(insertAt + 3, colorItem);
            appContextMenu.Items.Insert(insertAt + 4, resetColorItem);
            appContextMenu.Items.Insert(insertAt + 5, opacityMenuItem);
            appContextMenu.Items.Insert(insertAt + 6, iconSizeMenuItem);
            appContextMenu.Items.Insert(insertAt + 7, portalMenuItem);
            appContextMenu.Items.Insert(insertAt + 8, rulesMenuItem);
            appContextMenu.Items.Insert(insertAt + 9, sortNowItem);
            appContextMenu.Items.Insert(insertAt + 10, new ToolStripSeparator());
            appContextMenu.Items.Insert(insertAt + 11, hideAllItem);
        }

        private void AddTab()
        {
            using (var dialog = new PromptDialog("Add tab", "Name of the new tab:", ""))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Value.Trim().Length == 0)
                    return;

                // First tab: move the existing flat item list into a "Main" tab.
                if (fenceInfo.Tabs.Count == 0)
                {
                    fenceInfo.Tabs.Add(new FenceTab { Name = "Main", Files = fenceInfo.Files });
                    fenceInfo.Files = new List<string>();
                }

                fenceInfo.Tabs.Add(new FenceTab { Name = dialog.Value.Trim() });
                fenceInfo.ActiveTab = fenceInfo.Tabs.Count - 1;
                Save();
                Refresh();
            }
        }

        private void RenameTab()
        {
            if (!HasTabs)
                return;
            var tab = fenceInfo.Tabs[ClampActiveTab()];
            using (var dialog = new PromptDialog("Rename tab", "New name for this tab:", tab.Name))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Value.Trim().Length == 0)
                    return;
                tab.Name = dialog.Value.Trim();
                Save();
                Refresh();
            }
        }

        private void RemoveTab()
        {
            if (!HasTabs)
                return;
            var tab = fenceInfo.Tabs[ClampActiveTab()];
            if (tab.Files.Count > 0 &&
                MessageBox.Show(this, "The items of tab \"" + tab.Name + "\" will be released back to the desktop. Continue?",
                    "Remove tab", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            foreach (var file in tab.Files)
                DesktopIconHider.Unhide(file);
            fenceInfo.Tabs.Remove(tab);

            // Removing the last tab returns the fence to its untabbed mode.
            if (fenceInfo.Tabs.Count == 1)
            {
                fenceInfo.Files = fenceInfo.Tabs[0].Files;
                fenceInfo.Tabs.Clear();
            }
            fenceInfo.ActiveTab = 0;
            Save();
            Refresh();
        }

        private bool HandleTabClick(Point location)
        {
            if (!HasTabs)
                return false;
            foreach (var hit in tabHitRects)
            {
                if (!hit.Rect.Contains(location))
                    continue;
                if (hit.Index == -1)
                {
                    AddTab();
                }
                else if (fenceInfo.ActiveTab != hit.Index)
                {
                    fenceInfo.ActiveTab = hit.Index;
                    scrollOffset = 0;
                    Save();
                    Refresh();
                }
                return true;
            }
            return false;
        }

        private void FenceWindow_DragOver(object sender, DragEventArgs e)
        {
            // Hovering a tab header during a drag switches to that tab.
            if (!HasTabs)
                return;
            var location = PointToClient(new Point(e.X, e.Y));
            foreach (var hit in tabHitRects)
            {
                if (hit.Index >= 0 && hit.Rect.Contains(location) && fenceInfo.ActiveTab != hit.Index)
                {
                    fenceInfo.ActiveTab = hit.Index;
                    scrollOffset = 0;
                    Refresh();
                    break;
                }
            }
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

        /// <summary>The mutable item list of the active tab (or the flat list when untabbed).</summary>
        private List<string> CurrentFileList => HasTabs ? fenceInfo.Tabs[ClampActiveTab()].Files : fenceInfo.Files;

        private IEnumerable<string> CurrentFiles()
        {
            if (!IsPortal)
                return ApplySort(CurrentFileList);

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

            // Subtle scale-down while the fence is being moved, bounce on release.
            if (m.Msg == 0x0231) // WM_ENTERSIZEMOVE
                OnWindowDragStarted();
            if (m.Msg == 0x0232) // WM_EXITSIZEMOVE
                OnWindowDragEnded();

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
                foreach (var file in fenceInfo.EnumerateAllFiles())
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
            foreach (var tab in fenceInfo.Tabs)
                tab.Files.Remove(path);
            DesktopIconHider.Unhide(path);
            hoveringItem = null;
            Save();
            Refresh();
        }

        private void contextMenuStrip1_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            UpdatePluginContextItems();

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

            // Notes and widgets have no items, so hide everything item-related.
            searchMenuItem.Visible = IsNormal;
            sortMenuItem.Visible = IsNormal;
            iconSizeMenuItem.Visible = IsNormal;
            portalMenuItem.Visible = IsNormal;
            rulesMenuItem.Visible = IsNormal;
            tabsMenuItem.Visible = IsNormal && !IsPortal;
            minifyToolStripMenuItem.Visible = !IsNote;
        }

        private void FenceWindow_DragEnter(object sender, DragEventArgs e)
        {
            if (!IsNormal)
                return;
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
                else if (ItemExists(path) && !CurrentFileList.Contains(path))
                {
                    // If the item comes from another tab of this fence, move it here.
                    droppedOnSelf = fenceInfo.EnumerateAllFiles().Contains(path);
                    if (droppedOnSelf)
                    {
                        fenceInfo.Files.Remove(path);
                        foreach (var tab in fenceInfo.Tabs)
                            tab.Files.Remove(path);
                    }

                    CurrentFileList.Add(path);
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
                if (!fenceInfo.EnumerateAllFiles().Contains(file) && ItemExists(file))
                {
                    CurrentFileList.Add(file);
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
            if (noteBox != null)
                PositionNoteBox();

            throttledResize.Run(() =>
            {
                fenceInfo.Width = Width;
                // While animating, Height is transient - persist the real expanded height.
                fenceInfo.Height = (isMinified || HeightAnimating) ? prevHeight : Height;
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
                AnimateHeightTo(prevHeight);
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
                // Mid-expand the true full height is still stored in prevHeight.
                if (!HeightAnimating)
                    prevHeight = Height;
                AnimateHeightTo(titleHeight);
                Refresh();
            }
        }

        private void minifyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (isMinified)
            {
                AnimateHeightTo(prevHeight);
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

            // Drag/bounce animation: scale the content around the center.
            if (contentScale != 1f)
            {
                e.Graphics.TranslateTransform(Width / 2f, Height / 2f);
                e.Graphics.ScaleTransform(contentScale, contentScale);
                e.Graphics.TranslateTransform(-Width / 2f, -Height / 2f);
            }

            // Background (tinted with the custom fence color and per-fence opacity)
            var bgAlpha = Math.Max(0, Math.Min(255, fenceInfo.Transparency * 255 / 100));
            e.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(bgAlpha, baseColor)), ClientRectangle);

            // Title
            e.Graphics.DrawString(Text, titleFont, Brushes.White, new PointF(Width / 2, titleOffset), new StringFormat { Alignment = StringAlignment.Center });
            e.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(50, Color.Black)), new RectangleF(0, 0, Width, titleHeight));

            if (IsWidget)
            {
                RenderWidget(e.Graphics);
                ResetClickFlags();
                return;
            }

            if (IsNote)
            {
                ResetClickFlags();
                return;
            }

            if (HasTabs)
                RenderTabStrip(e.Graphics);

            // Items
            var x = itemPadding;
            var y = itemPadding;
            scrollHeight = 0;
            e.Graphics.Clip = new Region(new Rectangle(0, ContentTop, Width, Height - ContentTop));
            foreach (var item in BuildRenderItems())
            {
                RenderEntry(e.Graphics, item, x, y + ContentTop - scrollOffset);

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

            scrollHeight -= (ClientRectangle.Height - ContentTop);

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
            ResetClickFlags();
        }

        private void ResetClickFlags()
        {
            if (shouldUpdateSelection && !hasSelectionUpdated)
                selectedItem = null;

            if (!hasHoverUpdated)
                hoveringItem = null;

            shouldRunDoubleClick = false;
            shouldUpdateSelection = false;
            hasSelectionUpdated = false;
            hasHoverUpdated = false;
        }

        private void RenderTabStrip(Graphics g)
        {
            tabHitRects.Clear();
            var strip = new Rectangle(0, titleHeight, Width, TabStripHeight);
            g.FillRectangle(new SolidBrush(Color.FromArgb(35, Color.Black)), strip);

            var x = LogicalToDeviceUnits(4);
            var padding = LogicalToDeviceUnits(10);
            var active = ClampActiveTab();

            for (var i = 0; i < fenceInfo.Tabs.Count; i++)
            {
                var name = fenceInfo.Tabs[i].Name ?? "Tab";
                var textWidth = (int)g.MeasureString(name, iconFont).Width;
                var rect = new Rectangle(x, strip.Y, textWidth + 2 * padding, strip.Height);

                if (i == active)
                {
                    g.FillRectangle(new SolidBrush(Color.FromArgb(45, Color.White)), rect);
                    g.FillRectangle(new SolidBrush(Color.FromArgb(220, Color.White)),
                        new Rectangle(rect.X, rect.Bottom - 2, rect.Width, 2));
                }

                var textBrush = i == active ? Brushes.White : new SolidBrush(Color.FromArgb(170, Color.White));
                g.DrawString(name, iconFont, textBrush,
                    new RectangleF(rect.X, rect.Y, rect.Width, rect.Height),
                    new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });

                tabHitRects.Add(new TabHit { Rect = rect, Index = i });
                x += rect.Width + LogicalToDeviceUnits(2);
            }

            // Trailing "+" button for adding a tab.
            var plusRect = new Rectangle(x, strip.Y, strip.Height, strip.Height);
            g.DrawString("+", iconFont, new SolidBrush(Color.FromArgb(170, Color.White)),
                new RectangleF(plusRect.X, plusRect.Y, plusRect.Width, plusRect.Height),
                new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
            tabHitRects.Add(new TabHit { Rect = plusRect, Index = -1 });
        }

        private int ClampActiveTab()
        {
            if (fenceInfo.ActiveTab < 0 || fenceInfo.ActiveTab >= fenceInfo.Tabs.Count)
                fenceInfo.ActiveTab = 0;
            return fenceInfo.ActiveTab;
        }

        private void RenderWidget(Graphics g)
        {
            var area = new Rectangle(0, titleHeight, Width, Height - titleHeight);

            // Rolled up or squeezed tiny: nothing sensible to draw, and the
            // area-derived font sizes must stay positive.
            if (area.Width < 20 || area.Height < 20)
                return;

            switch (fenceInfo.FenceType)
            {
                case 2:
                    RenderClockWidget(g, area);
                    break;
                case 3:
                    RenderSystemWidget(g, area);
                    break;
                case 4:
                    RenderCalendarWidget(g, area);
                    break;
                case 100:
                    RenderPluginWidget(g, area);
                    break;
            }
        }

        private void RenderPluginWidget(Graphics g, Rectangle area)
        {
            var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            if (pluginWidget == null)
            {
                g.DrawString("Plugin not found:\n" + fenceInfo.WidgetPlugin, iconFont,
                    new SolidBrush(Color.FromArgb(200, Color.IndianRed)), area, center);
                return;
            }

            try
            {
                pluginWidget.Render(g, area, widgetHost);
            }
            catch (Exception ex)
            {
                // A faulty plugin must never take the fence down.
                g.DrawString("Widget error:\n" + ex.Message, iconFont,
                    new SolidBrush(Color.FromArgb(200, Color.IndianRed)), area, center);
            }
        }

        private void RenderClockWidget(Graphics g, Rectangle area)
        {
            var now = DateTime.Now;
            var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using (var timeFont = new Font("Segoe UI Light", area.Height * 0.30f, GraphicsUnit.Pixel))
            using (var dateFont = new Font("Segoe UI", area.Height * 0.11f, GraphicsUnit.Pixel))
            {
                var timeRect = new RectangleF(area.X, area.Y, area.Width, area.Height * 0.62f);
                var dateRect = new RectangleF(area.X, area.Y + area.Height * 0.58f, area.Width, area.Height * 0.32f);
                g.DrawString(now.ToString("HH:mm:ss"), timeFont, Brushes.White, timeRect, center);
                g.DrawString(now.ToString("dddd, d. MMMM yyyy"), dateFont, new SolidBrush(Color.FromArgb(200, Color.White)), dateRect, center);
            }
        }

        private void RenderSystemWidget(Graphics g, Rectangle area)
        {
            var margin = LogicalToDeviceUnits(14);
            var barHeight = LogicalToDeviceUnits(10);
            var rowHeight = (area.Height - 2 * margin) / 2;

            var cpu = lastCpuValue < 0 ? ReadCpuUsage() : lastCpuValue;
            var ram = ReadRamUsage();

            RenderUsageRow(g, "CPU", cpu, new Rectangle(area.X + margin, area.Y + margin, area.Width - 2 * margin, rowHeight), barHeight);
            RenderUsageRow(g, "RAM", ram, new Rectangle(area.X + margin, area.Y + margin + rowHeight, area.Width - 2 * margin, rowHeight), barHeight);
        }

        private void RenderUsageRow(Graphics g, string label, float percent, Rectangle row, int barHeight)
        {
            var text = percent < 0 ? label + "  –" : string.Format("{0}  {1:0} %", label, percent);
            g.DrawString(text, iconFont, Brushes.White, row.X, row.Y);

            var barRect = new Rectangle(row.X, row.Y + row.Height - barHeight - 4, row.Width, barHeight);
            g.FillRectangle(new SolidBrush(Color.FromArgb(50, Color.White)), barRect);
            if (percent >= 0)
            {
                var fillWidth = (int)(barRect.Width * Math.Min(100f, percent) / 100f);
                var fillColor = percent > 85 ? Color.IndianRed : (percent > 60 ? Color.Gold : Color.MediumSeaGreen);
                g.FillRectangle(new SolidBrush(Color.FromArgb(200, fillColor)), new Rectangle(barRect.X, barRect.Y, fillWidth, barRect.Height));
            }
        }

        private void RenderCalendarWidget(Graphics g, Rectangle area)
        {
            var now = DateTime.Now;
            var margin = LogicalToDeviceUnits(10);
            var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

            var headerRect = new RectangleF(area.X, area.Y + margin / 2, area.Width, LogicalToDeviceUnits(22));
            using (var headerFont = new Font("Segoe UI Semibold", 11f))
                g.DrawString(now.ToString("MMMM yyyy"), headerFont, Brushes.White, headerRect, center);

            var gridTop = headerRect.Bottom + margin / 2;
            var cellWidth = (area.Width - 2 * margin) / 7f;
            var firstOfMonth = new DateTime(now.Year, now.Month, 1);
            var startOffset = ((int)firstOfMonth.DayOfWeek + 6) % 7; // Monday first
            var daysInMonth = DateTime.DaysInMonth(now.Year, now.Month);
            var rows = (int)Math.Ceiling((startOffset + daysInMonth) / 7.0);
            var cellHeight = (area.Bottom - gridTop - margin - LogicalToDeviceUnits(16)) / rows;

            // Weekday header (Monday-first).
            var dayNames = new[] { "Mo", "Di", "Mi", "Do", "Fr", "Sa", "So" };
            for (var i = 0; i < 7; i++)
            {
                var rect = new RectangleF(area.X + margin + i * cellWidth, gridTop, cellWidth, LogicalToDeviceUnits(16));
                g.DrawString(dayNames[i], iconFont, new SolidBrush(Color.FromArgb(150, Color.White)), rect, center);
            }

            var daysTop = gridTop + LogicalToDeviceUnits(16);
            for (var day = 1; day <= daysInMonth; day++)
            {
                var index = startOffset + day - 1;
                var col = index % 7;
                var row = index / 7;
                var rect = new RectangleF(area.X + margin + col * cellWidth, daysTop + row * cellHeight, cellWidth, cellHeight);

                if (day == now.Day)
                {
                    var d = Math.Min(rect.Width, rect.Height) - 2;
                    g.FillEllipse(new SolidBrush(Color.FromArgb(190, Color.White)),
                        rect.X + (rect.Width - d) / 2, rect.Y + (rect.Height - d) / 2, d, d);
                    g.DrawString(day.ToString(), iconFont, Brushes.Black, rect, center);
                }
                else
                {
                    g.DrawString(day.ToString(), iconFont, new SolidBrush(Color.FromArgb(210, Color.White)), rect, center);
                }
            }
        }

        private static float ReadRamUsage()
        {
            var status = new MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
            if (!GlobalMemoryStatusEx(ref status))
                return -1;
            return status.dwMemoryLoad;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

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

            // Soft glow behind the hovered item.
            if (mouseOver)
            {
                using (var glowPath = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    var glowRect = new Rectangle(outlineRect.X - 10, outlineRect.Y - 10, outlineRect.Width + 20, outlineRect.Height + 20);
                    glowPath.AddEllipse(glowRect);
                    using (var glow = new System.Drawing.Drawing2D.PathGradientBrush(glowPath))
                    {
                        glow.CenterColor = Color.FromArgb(45, Color.White);
                        glow.SurroundColors = new[] { Color.FromArgb(0, Color.White) };
                        g.FillEllipse(glow, glowRect);
                    }
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
            // App exit on last closed fence is handled by FenceManager;
            // Application.OpenForms is unreliable with recreated handles.
            portalWatcher?.Dispose();
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
                MinimumSize = new Size(LogicalToDeviceUnits(100), titleHeight);
                ReloadFonts();
                Minify();
                if (isMinified && !HeightAnimating)
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

        private Rectangle ScrollbarTrackRect => new Rectangle(Width - 10, ContentTop + 2, 7, Height - ContentTop - 4);

        private Rectangle ScrollbarThumbRect
        {
            get
            {
                var track = ScrollbarTrackRect;
                var viewport = Height - ContentTop;
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

            // SDK v2 plugins receive clicks inside their content area.
            if (fenceInfo.FenceType == 100 && pluginWidget is Sdk.IFlowGridWidget2 clickable && e.Y > titleHeight)
            {
                var area = new Rectangle(0, titleHeight, Width, Height - titleHeight);
                try
                {
                    if (clickable.OnClick(e.Location, area, widgetHost))
                        Invalidate();
                }
                catch
                {
                    // A faulty plugin must never take the fence down.
                }
                return;
            }

            if (HandleTabClick(e.Location))
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
            droppedOnSelf = false;
            var result = DoDragDrop(data, DragDropEffects.Move);

            // The receiving fence took over (it also keeps the desktop-hidden state),
            // so drop only our own reference here. Tab-to-tab moves within this
            // fence already handled the lists themselves.
            if (result == DragDropEffects.Move && !droppedOnSelf)
            {
                fenceInfo.Files.Remove(item);
                foreach (var tab in fenceInfo.Tabs)
                    tab.Files.Remove(item);
                Save();
                Refresh();
            }
            droppedOnSelf = false;
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

