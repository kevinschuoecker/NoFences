using System;
using System.Collections.Generic;

namespace NoFences.Model
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

        public FenceInfo()
        {

        }

        public FenceInfo(Guid id)
        {
            Id = id;
        }
    }
}
