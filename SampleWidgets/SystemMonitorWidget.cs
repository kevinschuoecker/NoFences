using FlowGrid.Sdk;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace SampleWidgets
{
    /// <summary>
    /// Extended system monitor: CPU, RAM, GPU, VRAM and disk usage.
    /// Each section can be toggled by clicking its chip at the top of the
    /// widget; the selection is stored per fence (SDK v2 settings).
    /// </summary>
    public class SystemMonitorWidget : IFlowGridWidget2
    {
        public string Name => "System Monitor";

        public int RefreshIntervalMs => 1000;

        private static readonly string[] Sections = { "CPU", "RAM", "GPU", "VRAM", "DISK" };

        // Sampling state (shared across fences; the data is global anyway).
        private readonly object sync = new object();
        private PerformanceCounter cpuCounter;
        private List<PerformanceCounter> gpuCounters;
        private List<PerformanceCounter> vramCounters;
        private DateTime lastGpuCounterRefresh = DateTime.MinValue;
        private bool countersInitializing;
        private float cpuValue = -1, gpuValue = -1;
        private double vramUsedGb = -1;
        private double vramTotalGb = -1;

        public void Render(Graphics g, Rectangle area, IWidgetHost host)
        {
            EnsureCounters();
            Sample();

            var hidden = GetHidden(host);
            var chipRects = LayoutChips(g, area, host.BaseFont);

            // Toggle chips.
            for (var i = 0; i < Sections.Length; i++)
            {
                var enabled = !hidden.Contains(Sections[i]);
                var rect = chipRects[i];
                using (var back = new SolidBrush(Color.FromArgb(enabled ? 70 : 25, Color.White)))
                    g.FillRectangle(back, rect);
                var textBrush = new SolidBrush(Color.FromArgb(enabled ? 235 : 110, Color.White));
                g.DrawString(Sections[i], host.BaseFont, textBrush, rect,
                    new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
            }

            // Visible rows.
            var rows = new List<Action<Rectangle>>();
            if (!hidden.Contains("CPU"))
                rows.Add(r => DrawBarRow(g, host, r, "CPU", cpuValue, FormatPercent(cpuValue)));
            if (!hidden.Contains("RAM"))
            {
                var ram = ReadRam();
                rows.Add(r => DrawBarRow(g, host, r, "RAM", ram, FormatPercent(ram)));
            }
            if (!hidden.Contains("GPU"))
                rows.Add(r => DrawBarRow(g, host, r, "GPU", gpuValue, FormatPercent(gpuValue)));
            if (!hidden.Contains("VRAM"))
            {
                var percent = (vramUsedGb >= 0 && vramTotalGb > 0) ? (float)(vramUsedGb / vramTotalGb * 100) : -1;
                var label = vramUsedGb < 0 ? "–"
                    : vramTotalGb > 0
                        ? string.Format(CultureInfo.CurrentCulture, "{0:0.0} / {1:0.0} GB", vramUsedGb, vramTotalGb)
                        : string.Format(CultureInfo.CurrentCulture, "{0:0.0} GB", vramUsedGb);
                rows.Add(r => DrawBarRow(g, host, r, "VRAM", percent, label));
            }
            if (!hidden.Contains("DISK"))
            {
                foreach (var drive in SafeDrives())
                {
                    var d = drive;
                    var used = d.TotalSize - d.TotalFreeSpace;
                    var percent = d.TotalSize > 0 ? (float)(used * 100.0 / d.TotalSize) : -1;
                    var label = string.Format(CultureInfo.CurrentCulture, "{0:0} / {1:0} GB",
                        used / 1073741824.0, d.TotalSize / 1073741824.0);
                    rows.Add(r => DrawBarRow(g, host, r, d.Name.TrimEnd('\\'), percent, label));
                }
            }

            var chipsBottom = chipRects.Length > 0 ? chipRects[0].Bottom + 6 : area.Y;
            var contentRect = Rectangle.FromLTRB(area.X + 12, chipsBottom, area.Right - 12, area.Bottom - 6);
            if (rows.Count == 0 || contentRect.Height < 10)
                return;

            var rowHeight = Math.Min(46, contentRect.Height / rows.Count);
            for (var i = 0; i < rows.Count; i++)
            {
                var rowRect = new Rectangle(contentRect.X, contentRect.Y + i * rowHeight, contentRect.Width, rowHeight);
                if (rowRect.Bottom > contentRect.Bottom + 1)
                    break;
                rows[i](rowRect);
            }
        }

        public bool OnClick(Point location, Rectangle area, IWidgetHost host)
        {
            // Chip rects are deterministic from the area, so recompute instead of caching
            // (the widget instance is shared between fences).
            using (var bmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(bmp))
            {
                var chipRects = LayoutChips(g, area, host.BaseFont);
                for (var i = 0; i < Sections.Length; i++)
                {
                    if (!chipRects[i].Contains(location))
                        continue;
                    var hidden = GetHidden(host);
                    if (!hidden.Remove(Sections[i]))
                        hidden.Add(Sections[i]);
                    host.Settings = "hide=" + string.Join(",", hidden);
                    return true;
                }
            }
            return false;
        }

        private static HashSet<string> GetHidden(IWidgetHost host)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var settings = host.Settings ?? "";
            var marker = "hide=";
            var index = settings.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return result;
            foreach (var part in settings.Substring(index + marker.Length).Split(','))
                if (part.Trim().Length > 0)
                    result.Add(part.Trim());
            return result;
        }

        private static Rectangle[] LayoutChips(Graphics g, Rectangle area, Font font)
        {
            var rects = new Rectangle[Sections.Length];
            var chipHeight = (int)(font.GetHeight(g) + 8);
            var x = area.X + 10;
            for (var i = 0; i < Sections.Length; i++)
            {
                var width = (int)g.MeasureString(Sections[i], font).Width + 14;
                rects[i] = new Rectangle(x, area.Y + 6, width, chipHeight);
                x += width + 5;
            }
            return rects;
        }

        private void DrawBarRow(Graphics g, IWidgetHost host, Rectangle row, string label, float percent, string valueText)
        {
            var barHeight = Math.Max(6, row.Height / 5);
            g.DrawString(label, host.BaseFont, Brushes.White, row.X, row.Y + 2);
            var valueSize = g.MeasureString(valueText, host.BaseFont);
            g.DrawString(valueText, host.BaseFont, new SolidBrush(Color.FromArgb(200, Color.White)),
                row.Right - valueSize.Width, row.Y + 2);

            var barRect = new Rectangle(row.X, row.Bottom - barHeight - 4, row.Width, barHeight);
            g.FillRectangle(new SolidBrush(Color.FromArgb(45, Color.White)), barRect);
            if (percent >= 0)
            {
                var fill = (int)(barRect.Width * Math.Min(100f, percent) / 100f);
                var color = percent > 85 ? Color.IndianRed : (percent > 60 ? Color.Gold : Color.MediumSeaGreen);
                g.FillRectangle(new SolidBrush(Color.FromArgb(210, color)), new Rectangle(barRect.X, barRect.Y, fill, barRect.Height));
            }
        }

        private static string FormatPercent(float value)
        {
            return value < 0 ? "–" : string.Format(CultureInfo.CurrentCulture, "{0:0} %", value);
        }

        private void EnsureCounters()
        {
            lock (sync)
            {
                if (countersInitializing || (cpuCounter != null && DateTime.Now - lastGpuCounterRefresh < TimeSpan.FromSeconds(10)))
                    return;
                countersInitializing = true;
            }

            // Counter creation and instance enumeration are slow - keep them off the UI thread.
            Task.Run(() =>
            {
                try
                {
                    var cpu = cpuCounter ?? new PerformanceCounter("Processor", "% Processor Time", "_Total");

                    var gpu = new List<PerformanceCounter>();
                    var vram = new List<PerformanceCounter>();
                    try
                    {
                        var engineCategory = new PerformanceCounterCategory("GPU Engine");
                        foreach (var instance in engineCategory.GetInstanceNames())
                            if (instance.EndsWith("engtype_3D", StringComparison.OrdinalIgnoreCase))
                                gpu.Add(new PerformanceCounter("GPU Engine", "Utilization Percentage", instance));

                        var memCategory = new PerformanceCounterCategory("GPU Adapter Memory");
                        foreach (var instance in memCategory.GetInstanceNames())
                            vram.Add(new PerformanceCounter("GPU Adapter Memory", "Dedicated Usage", instance));
                    }
                    catch
                    {
                        // GPU counters unavailable (older Windows / no GPU) - sections show "–".
                    }

                    var totalVram = ReadTotalVramGb();

                    lock (sync)
                    {
                        cpuCounter = cpu;
                        DisposeAll(gpuCounters);
                        DisposeAll(vramCounters);
                        gpuCounters = gpu;
                        vramCounters = vram;
                        if (totalVram > 0)
                            vramTotalGb = totalVram;
                        lastGpuCounterRefresh = DateTime.Now;
                    }
                }
                catch
                {
                    // Leave whatever we had.
                }
                finally
                {
                    lock (sync)
                    {
                        countersInitializing = false;
                    }
                }
            });
        }

        private void Sample()
        {
            lock (sync)
            {
                try { cpuValue = cpuCounter?.NextValue() ?? -1; } catch { cpuValue = -1; }

                if (gpuCounters != null)
                {
                    try
                    {
                        float sum = 0;
                        foreach (var counter in gpuCounters)
                            sum += counter.NextValue();
                        gpuValue = Math.Min(100f, sum);
                    }
                    catch { gpuValue = -1; }
                }

                if (vramCounters != null && vramCounters.Count > 0)
                {
                    try
                    {
                        double bytes = 0;
                        foreach (var counter in vramCounters)
                            bytes += counter.NextValue();
                        vramUsedGb = bytes / 1073741824.0;
                    }
                    catch { vramUsedGb = -1; }
                }
            }
        }

        private static double ReadTotalVramGb()
        {
            try
            {
                using (var displayClass = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                {
                    if (displayClass == null)
                        return -1;
                    double best = -1;
                    foreach (var name in displayClass.GetSubKeyNames())
                    {
                        using (var adapter = displayClass.OpenSubKey(name))
                        {
                            var size = adapter?.GetValue("HardwareInformation.qwMemorySize");
                            if (size is long qw && qw > 0)
                                best = Math.Max(best, qw / 1073741824.0);
                        }
                    }
                    return best;
                }
            }
            catch
            {
                return -1;
            }
        }

        private static float ReadRam()
        {
            var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
            return GlobalMemoryStatusEx(ref status) ? (float)status.dwMemoryLoad : -1f;
        }

        private static IEnumerable<DriveInfo> SafeDrives()
        {
            try
            {
                return DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).ToList();
            }
            catch
            {
                return Enumerable.Empty<DriveInfo>();
            }
        }

        private static void DisposeAll(List<PerformanceCounter> counters)
        {
            if (counters == null)
                return;
            foreach (var counter in counters)
                counter.Dispose();
        }

        [StructLayout(LayoutKind.Sequential)]
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

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
    }
}
