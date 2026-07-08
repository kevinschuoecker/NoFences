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

            Minify();
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

        private void BuildExtraMenuItems()
        {
            var insertAt = appContextMenu.Items.IndexOf(toolStripSeparator1);

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

            portalMenuItem = new ToolStripMenuItem("Folder portal...");
            portalMenuItem.Click += (s, e) => ConfigurePortal();

            var rulesItem = new ToolStripMenuItem("Auto-sort rules...");
            rulesItem.Click += (s, e) => ConfigureRules();

            var sortNowItem = new ToolStripMenuItem("Sort desktop now");
            sortNowItem.Click += (s, e) => DesktopAutoSorter.ApplyRulesNow();

            var hideAllItem = new ToolStripMenuItem("Hide all fences  (Ctrl+Alt+H)");
            hideAllItem.Click += (s, e) => FenceManager.Instance.ToggleAllFences();

            appContextMenu.Items.Insert(insertAt, colorItem);
            appContextMenu.Items.Insert(insertAt + 1, resetColorItem);
            appContextMenu.Items.Insert(insertAt + 2, iconSizeItem);
            appContextMenu.Items.Insert(insertAt + 3, portalMenuItem);
            appContextMenu.Items.Insert(insertAt + 4, rulesItem);
            appContextMenu.Items.Insert(insertAt + 5, sortNowItem);
            appContextMenu.Items.Insert(insertAt + 6, new ToolStripSeparator());
            appContextMenu.Items.Insert(insertAt + 7, hideAllItem);
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
                return;

            portalWatcher = new FileSystemWatcher(fenceInfo.TargetFolder)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                EnableRaisingEvents = true
            };
            FileSystemEventHandler refresh = (s, e) => BeginInvoke((Action)RefreshPortal);
            portalWatcher.Created += refresh;
            portalWatcher.Deleted += refresh;
            portalWatcher.Renamed += (s, e) => BeginInvoke((Action)RefreshPortal);
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
                return fenceInfo.Files;

            if (portalFiles == null)
            {
                try
                {
                    portalFiles = Directory.EnumerateFileSystemEntries(fenceInfo.TargetFolder)
                        .Where(p => (new FileInfo(p).Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                        .ToList();
                }
                catch
                {
                    portalFiles = new List<string>();
                }
            }
            return portalFiles;
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

                if ((int)m.Result == HTCLIENT && pt.Y < titleHeight)     // drag the form
                {
                    m.Result = (IntPtr)HTCAPTION;
                    FenceWindow_MouseEnter(null, null);
                }

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
                else if (pt.X > (Width - 10))
                    m.Result = new IntPtr(HTRIGHT);
            }
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(this, "Really remove this fence?", "Remove", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                FenceManager.Instance.RemoveFence(fenceInfo);
                Close();
            }
        }

        private void deleteItemToolStripMenuItem_Click(object sender, EventArgs e)
        {
            fenceInfo.Files.Remove(hoveringItem);
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
        }

        private void FenceWindow_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) && !lockedToolStripMenuItem.Checked)
                e.Effect = DragDropEffects.Move;
        }

        private void FenceWindow_DragDrop(object sender, DragEventArgs e)
        {
            var dropped = (string[])e.Data.GetData(DataFormats.FileDrop);

            if (IsPortal)
            {
                // A portal mirrors a real folder, so dropping means moving the file there.
                foreach (var file in dropped)
                {
                    try
                    {
                        var destination = Path.Combine(fenceInfo.TargetFolder, Path.GetFileName(file));
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
                if (!fenceInfo.Files.Contains(file) && ItemExists(file))
                    fenceInfo.Files.Add(file);
            Save();
            Refresh();
        }

        private void FenceWindow_Resize(object sender, EventArgs e)
        {
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

            // Background (tinted with the custom fence color, if set)
            e.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(100, baseColor)), ClientRectangle);

            // Title
            e.Graphics.DrawString(Text, titleFont, Brushes.White, new PointF(Width / 2, titleOffset), new StringFormat { Alignment = StringAlignment.Center });
            e.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(50, Color.Black)), new RectangleF(0, 0, Width, titleHeight));

            // Items
            var x = itemPadding;
            var y = itemPadding;
            scrollHeight = 0;
            e.Graphics.Clip = new Region(new Rectangle(0, titleHeight, Width, Height - titleHeight));
            foreach (var file in CurrentFiles())
            {
                var entry = FenceEntry.FromPath(file);
                if (entry == null)
                    continue;

                RenderEntry(e.Graphics, entry, x, y + titleHeight - scrollOffset);

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

            // Scroll bars
            if (scrollHeight > 0)
            {
                var contentHeight = Height - titleHeight;
                var scrollbarHeight = contentHeight - scrollHeight;
                e.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(150, Color.Black)), new Rectangle(Width - 5, titleHeight + scrollOffset, 5, scrollbarHeight));

                scrollOffset = Math.Min(scrollOffset, scrollHeight);
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

        private void RenderEntry(Graphics g, FenceEntry entry, int x, int y)
        {
            var icon = entry.ExtractIcon(thumbnailProvider, iconSize);
            var name = entry.Name;

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
                entry.Open();
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
                shellContextMenu.ShowContextMenu(new[] { new FileInfo(hoveringItem) }, MousePosition);
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

            scrollOffset -= Math.Sign(e.Delta) * 10;
            if (scrollOffset < 0)
                scrollOffset = 0;
            if (scrollOffset > scrollHeight)
                scrollOffset = scrollHeight;

            Invalidate();
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

