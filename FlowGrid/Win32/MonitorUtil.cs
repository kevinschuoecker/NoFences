using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace FlowGrid.Win32
{
    /// <summary>
    /// Live monitor queries. Deliberately NOT based on Screen.AllScreens:
    /// .NET Framework caches that array once, so it reports a stale monitor
    /// layout after displays are plugged/unplugged - exactly the moment the
    /// off-screen rescue runs.
    /// </summary>
    public static class MonitorUtil
    {
        /// <summary>True if the rectangle intersects any currently attached monitor.</summary>
        public static bool IntersectsAnyMonitor(Rectangle rect)
        {
            var native = RECT.From(rect);
            return MonitorFromRect(ref native, MONITOR_DEFAULTTONULL) != IntPtr.Zero;
        }

        /// <summary>Work area of the monitor nearest to the rectangle.</summary>
        public static Rectangle GetNearestWorkArea(Rectangle rect)
        {
            var native = RECT.From(rect);
            var monitor = MonitorFromRect(ref native, MONITOR_DEFAULTTONEAREST);
            var info = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
                return Rectangle.FromLTRB(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom);
            return System.Windows.Forms.SystemInformation.WorkingArea;
        }

        private const int MONITOR_DEFAULTTONULL = 0;
        private const int MONITOR_DEFAULTTONEAREST = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;

            public static RECT From(Rectangle r) => new RECT
            {
                Left = r.Left,
                Top = r.Top,
                Right = r.Right,
                Bottom = r.Bottom
            };
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromRect(ref RECT lprc, int dwFlags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
    }
}
