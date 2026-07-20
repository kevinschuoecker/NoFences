using System;
using System.Collections.Generic;

namespace FlowGrid.Model
{
    public class FenceInfo
    {
        /* 
         * DO NOT RENAME PROPERTIES. Used for XML serialization.
         */

        public Guid Id { get; set; }

        public string Name { get; set; }

        public int PosX { get; set; }

        public int PosY { get; set; }

        /// <summary>
        /// Gets or sets the DPI scaled window width.
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// Gets or sets the DPI scaled window height.
        /// </summary>
        public int Height { get; set; }

        public bool Locked { get; set; }

        public bool CanMinify { get; set; }

        /// <summary>
        /// Gets or sets the logical window title height.
        /// </summary>
        public int TitleHeight { get; set; } = 35;

        /// <summary>
        /// Gets or sets the custom tint color as "#RRGGBB". Empty means the default dark look.
        /// </summary>
        public string CustomColor { get; set; } = "";

        /// <summary>
        /// Gets or sets the background opacity in percent (0-100).
        /// </summary>
        public int Transparency { get; set; } = 40;

        /// <summary>
        /// Gets or sets the display order: 0 = manual, 1 = by name, 2 = by type, 3 = by date.
        /// </summary>
        public int SortMode { get; set; }

        /// <summary>
        /// Gets or sets the logical icon size (32, 48 or 64).
        /// </summary>
        public int IconSize { get; set; } = 32;

        /// <summary>
        /// Gets or sets the folder this fence mirrors. Empty means a regular fence.
        /// </summary>
        public string TargetFolder { get; set; } = "";

        /// <summary>
        /// Gets or sets semicolon-separated wildcard patterns (e.g. "*.png; screenshot*").
        /// New desktop files matching a pattern are added to this fence automatically.
        /// </summary>
        public string AutoSortPatterns { get; set; } = "";

        public List<string> Files { get; set; } = new List<string>();

        /// <summary>
        /// Gets or sets the tabs of this fence. Empty means a single untabbed view using <see cref="Files"/>.
        /// </summary>
        public List<FenceTab> Tabs { get; set; } = new List<FenceTab>();

        /// <summary>
        /// Gets or sets the index of the currently visible tab.
        /// </summary>
        public int ActiveTab { get; set; }

        /// <summary>
        /// Gets or sets what this window shows: 0 = items, 1 = sticky note,
        /// 2 = clock widget, 3 = CPU/RAM widget, 4 = calendar widget,
        /// 100 = plugin widget (see <see cref="WidgetPlugin"/>).
        /// </summary>
        public int FenceType { get; set; }

        /// <summary>
        /// Gets or sets the text of a sticky note fence.
        /// </summary>
        public string NoteText { get; set; } = "";

        /// <summary>
        /// For plugin widget fences (FenceType 100): the full type name of the plugin widget.
        /// </summary>
        public string WidgetPlugin { get; set; } = "";

        /// <summary>
        /// For plugin widget fences: free-form per-fence settings owned by the plugin.
        /// </summary>
        public string WidgetSettings { get; set; } = "";

        /// <summary>
        /// Enumerates the items of all tabs (or the flat list when untabbed).
        /// </summary>
        public IEnumerable<string> EnumerateAllFiles()
        {
            foreach (var file in Files)
                yield return file;
            foreach (var tab in Tabs)
                foreach (var file in tab.Files)
                    yield return file;
        }

        public FenceInfo()
        {

        }

        public FenceInfo(Guid id)
        {
            Id = id;
        }
    }
}
