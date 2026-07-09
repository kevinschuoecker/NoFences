using FlowGrid.Sdk;
using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace SampleWidgets
{
    /// <summary>
    /// Minimal example widget: shows how long Windows has been running.
    /// Build this project and copy SampleWidgets.dll into
    /// %LOCALAPPDATA%\FlowGrid\Plugins, then restart FlowGrid.
    /// </summary>
    public class UptimeWidget : IFlowGridWidget
    {
        public string Name => "Uptime";

        public int RefreshIntervalMs => 1000;

        public void Render(Graphics g, Rectangle area, IWidgetHost host)
        {
            var uptime = TimeSpan.FromMilliseconds(GetTickCount64());
            var text = string.Format("{0} d  {1:00}:{2:00}:{3:00}",
                uptime.Days, uptime.Hours, uptime.Minutes, uptime.Seconds);

            var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using (var bigFont = new Font(host.BaseFont.FontFamily, area.Height * 0.18f, GraphicsUnit.Pixel))
            {
                var labelRect = new RectangleF(area.X, area.Y + area.Height * 0.12f, area.Width, area.Height * 0.25f);
                var valueRect = new RectangleF(area.X, area.Y + area.Height * 0.35f, area.Width, area.Height * 0.5f);
                g.DrawString("System uptime", host.BaseFont, new SolidBrush(Color.FromArgb(180, Color.White)), labelRect, center);
                g.DrawString(text, bigFont, Brushes.White, valueRect, center);
            }
        }

        [DllImport("kernel32.dll")]
        private static extern ulong GetTickCount64();
    }
}
