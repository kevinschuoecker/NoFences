using FlowGrid.Sdk;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SampleWidgets
{
    /// <summary>
    /// Stock/ETF watchlist using Yahoo Finance's public chart endpoint (no API key).
    /// - Symbols are stored per fence; add via the "+" button or the context menu.
    /// - Click a row for a detail page with a one-month price chart; "‹" goes back.
    /// - Data refreshes every 5 minutes.
    /// </summary>
    public class StockTickerWidget : IFlowGridWidget3
    {
        public string Name => "Stocks";

        public int RefreshIntervalMs => 2000;

        private static readonly TimeSpan FetchInterval = TimeSpan.FromMinutes(5);
        private static readonly string[] DefaultSymbols = { "VWCE.DE", "AAPL", "^GSPC" };

        private class Quote
        {
            public string Currency = "";
            public double Price = double.NaN;
            public double PreviousClose = double.NaN;
            public List<double> History = new List<double>();
            public DateTime FetchedAt = DateTime.MinValue;
            public bool Fetching;
            public string Error;
        }

        // Global quote cache - market data is the same for every fence.
        private readonly object sync = new object();
        private readonly Dictionary<string, Quote> cache = new Dictionary<string, Quote>(StringComparer.OrdinalIgnoreCase);

        #region Per-fence state (host.Settings)

        private static List<string> GetSymbols(IWidgetHost host)
        {
            var symbols = SplitList(GetPart(host.Settings, "symbols"));
            if (symbols.Count > 0)
                return symbols;

            // First run on this fence: seed from the legacy stocks.txt or the defaults.
            symbols = SplitList(ReadLegacyConfig());
            if (symbols.Count == 0)
                symbols = DefaultSymbols.ToList();
            SaveState(host, symbols, GetView(host));
            return symbols;
        }

        private static string GetView(IWidgetHost host)
        {
            return GetPart(host.Settings, "view");
        }

        private static void SaveState(IWidgetHost host, List<string> symbols, string view)
        {
            host.Settings = "symbols=" + string.Join(",", symbols) + (string.IsNullOrEmpty(view) ? "" : ";view=" + view);
        }

        private static string GetPart(string settings, string key)
        {
            foreach (var part in (settings ?? "").Split(';'))
            {
                var split = part.Split(new[] { '=' }, 2);
                if (split.Length == 2 && split[0].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                    return split[1].Trim();
            }
            return "";
        }

        private static List<string> SplitList(string value)
        {
            return (value ?? "")
                .Split(new[] { ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().ToUpperInvariant())
                .Where(s => s.Length > 0)
                .Distinct()
                .ToList();
        }

        private static string ReadLegacyConfig()
        {
            try
            {
                var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FlowGrid", "Plugins", "stocks.txt");
                return File.Exists(file) ? File.ReadAllText(file).Replace(';', ',') : "";
            }
            catch
            {
                return "";
            }
        }

        #endregion

        #region Rendering

        public void Render(Graphics g, Rectangle area, IWidgetHost host)
        {
            var symbols = GetSymbols(host);
            EnsureData(symbols, host);

            var view = GetView(host);
            if (view.Length > 0 && symbols.Contains(view))
                RenderDetail(g, area, host, view);
            else
                RenderList(g, area, host, symbols);
        }

        private void RenderList(Graphics g, Rectangle area, IWidgetHost host, List<string> symbols)
        {
            var header = HeaderRect(area);
            var plus = PlusRect(area);
            g.DrawString("Watchlist", host.BaseFont, new SolidBrush(Color.FromArgb(160, Color.White)),
                new RectangleF(header.X + 12, header.Y, header.Width - 40, header.Height),
                new StringFormat { LineAlignment = StringAlignment.Center });
            g.DrawString("+", host.BaseFont, Brushes.White, plus,
                new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });

            var rightAlign = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
            var leftAlign = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };

            var rows = RowRects(area, symbols.Count);
            for (var i = 0; i < symbols.Count && i < rows.Count; i++)
            {
                var symbol = symbols[i];
                var row = RectangleF.Inflate(rows[i], -12, 0);
                Quote quote;
                lock (sync)
                    cache.TryGetValue(symbol, out quote);

                g.DrawString(symbol, host.BaseFont, Brushes.White, row, leftAlign);

                if (quote == null || double.IsNaN(quote.Price))
                {
                    g.DrawString(quote?.Error ?? "…", host.BaseFont,
                        new SolidBrush(Color.FromArgb(150, Color.White)), row, rightAlign);
                    continue;
                }

                var changeText = FormatChange(quote, out var changeColor);
                var priceText = string.Format(CultureInfo.CurrentCulture, "{0:0.00} {1}", quote.Price, quote.Currency);
                var changeSize = g.MeasureString(changeText, host.BaseFont);
                g.DrawString(changeText, host.BaseFont, new SolidBrush(changeColor),
                    new RectangleF(row.Right - changeSize.Width - 2, row.Y, changeSize.Width + 2, row.Height), rightAlign);
                g.DrawString(priceText, host.BaseFont, new SolidBrush(Color.FromArgb(220, Color.White)),
                    new RectangleF(row.X, row.Y, row.Width - changeSize.Width - 10, row.Height), rightAlign);
            }
        }

        private void RenderDetail(Graphics g, Rectangle area, IWidgetHost host, string symbol)
        {
            Quote quote;
            lock (sync)
                cache.TryGetValue(symbol, out quote);

            var back = BackRect(area);
            g.DrawString("‹", host.BaseFont, Brushes.White, back,
                new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });

            var headerRect = new RectangleF(back.Right + 4, back.Top, area.Width - back.Width - 24, back.Height);
            g.DrawString(symbol, host.BaseFont, Brushes.White, headerRect,
                new StringFormat { LineAlignment = StringAlignment.Center });

            if (quote == null || double.IsNaN(quote.Price))
            {
                g.DrawString(quote?.Error ?? "Loading...", host.BaseFont,
                    new SolidBrush(Color.FromArgb(180, Color.White)), area,
                    new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                return;
            }

            var changeText = FormatChange(quote, out var changeColor);
            var priceLine = string.Format(CultureInfo.CurrentCulture, "{0:0.00} {1}   ", quote.Price, quote.Currency);
            using (var priceFont = new Font(host.BaseFont.FontFamily, host.BaseFont.Size * 1.6f, FontStyle.Bold))
            {
                var priceY = back.Bottom + 4;
                g.DrawString(priceLine, priceFont, Brushes.White, area.X + 12, priceY);
                var priceSize = g.MeasureString(priceLine, priceFont);
                g.DrawString(changeText, host.BaseFont, new SolidBrush(changeColor),
                    area.X + 12 + priceSize.Width, priceY + (priceSize.Height - host.BaseFont.GetHeight(g)) / 2);

                var chartRect = Rectangle.FromLTRB(area.X + 12, (int)(priceY + priceSize.Height + 6),
                    area.Right - 12, area.Bottom - 22);
                List<double> history;
                lock (sync)
                    history = new List<double>(quote.History);
                DrawChart(g, chartRect, history, changeColor);

                g.DrawString("1 month", host.BaseFont, new SolidBrush(Color.FromArgb(130, Color.White)),
                    new RectangleF(area.X + 12, area.Bottom - 20, area.Width - 24, 18),
                    new StringFormat { Alignment = StringAlignment.Center });
            }
        }

        private static void DrawChart(Graphics g, Rectangle rect, List<double> closes, Color color)
        {
            var values = closes.Where(v => !double.IsNaN(v)).ToList();
            if (values.Count < 2 || rect.Width < 10 || rect.Height < 10)
                return;

            var min = values.Min();
            var max = values.Max();
            var padding = Math.Max((max - min) * 0.08, 0.0001);
            min -= padding;
            max += padding;

            var points = new PointF[values.Count];
            for (var i = 0; i < values.Count; i++)
            {
                var x = rect.X + (float)i / (values.Count - 1) * rect.Width;
                var y = rect.Bottom - (float)((values[i] - min) / (max - min)) * rect.Height;
                points[i] = new PointF(x, y);
            }

            // Translucent fill under the line, then the line itself.
            using (var fillPath = new GraphicsPath())
            {
                fillPath.AddLines(points);
                fillPath.AddLine(points[points.Length - 1], new PointF(rect.Right, rect.Bottom));
                fillPath.AddLine(new PointF(rect.Right, rect.Bottom), new PointF(rect.X, rect.Bottom));
                fillPath.CloseFigure();
                using (var fill = new SolidBrush(Color.FromArgb(40, color)))
                    g.FillPath(fill, fillPath);
            }
            using (var pen = new Pen(Color.FromArgb(230, color), 2f) { LineJoin = LineJoin.Round })
                g.DrawLines(pen, points);
        }

        private static string FormatChange(Quote quote, out Color color)
        {
            color = Color.FromArgb(200, Color.White);
            if (double.IsNaN(quote.PreviousClose) || quote.PreviousClose <= 0)
                return "";
            var percent = (quote.Price - quote.PreviousClose) / quote.PreviousClose * 100;
            color = percent >= 0 ? Color.MediumSeaGreen : Color.IndianRed;
            return string.Format(CultureInfo.CurrentCulture, "{0:+0.00;-0.00} %", percent);
        }

        #endregion

        #region Layout (deterministic, shared between Render and OnClick)

        private static RectangleF HeaderRect(Rectangle area)
        {
            return new RectangleF(area.X, area.Y + 4, area.Width, 24);
        }

        private static RectangleF PlusRect(Rectangle area)
        {
            var header = HeaderRect(area);
            return new RectangleF(area.Right - 34, header.Y, 26, header.Height);
        }

        private static RectangleF BackRect(Rectangle area)
        {
            return new RectangleF(area.X + 6, area.Y + 4, 26, 24);
        }

        private static List<RectangleF> RowRects(Rectangle area, int count)
        {
            var rects = new List<RectangleF>();
            var top = HeaderRect(area).Bottom + 2;
            if (count == 0)
                return rects;
            var rowHeight = Math.Min(32f, Math.Max(18f, (area.Bottom - 8 - top) / count));
            for (var i = 0; i < count; i++)
            {
                var y = top + i * rowHeight;
                if (y + rowHeight > area.Bottom - 2)
                    break;
                rects.Add(new RectangleF(area.X, y, area.Width, rowHeight));
            }
            return rects;
        }

        #endregion

        #region Interaction

        public bool OnClick(Point location, Rectangle area, IWidgetHost host)
        {
            var symbols = GetSymbols(host);
            var view = GetView(host);

            if (view.Length > 0)
            {
                if (BackRect(area).Contains(location))
                {
                    SaveState(host, symbols, "");
                    return true;
                }
                // Anywhere else on the detail page: force refresh.
                InvalidateCache(view);
                EnsureData(symbols, host);
                return true;
            }

            if (PlusRect(area).Contains(location))
            {
                AddSymbol(host);
                return true;
            }

            var rows = RowRects(area, symbols.Count);
            for (var i = 0; i < rows.Count && i < symbols.Count; i++)
            {
                if (rows[i].Contains(location))
                {
                    SaveState(host, symbols, symbols[i]);
                    return true;
                }
            }
            return false;
        }

        public IList<WidgetMenuItem> GetMenuItems(IWidgetHost host)
        {
            var items = new List<WidgetMenuItem>
            {
                new WidgetMenuItem("Add symbol...", () => AddSymbol(host))
            };

            var view = GetView(host);
            if (view.Length > 0)
            {
                items.Add(new WidgetMenuItem("Remove " + view, () =>
                {
                    var symbols = GetSymbols(host);
                    symbols.RemoveAll(s => s.Equals(view, StringComparison.OrdinalIgnoreCase));
                    SaveState(host, symbols, "");
                }));
            }
            else
            {
                items.Add(new WidgetMenuItem("Remove symbol...", () =>
                {
                    var symbols = GetSymbols(host);
                    var input = host.PromptText("Remove symbol", "Which symbol should be removed?",
                        symbols.FirstOrDefault() ?? "");
                    if (string.IsNullOrWhiteSpace(input))
                        return;
                    symbols.RemoveAll(s => s.Equals(input.Trim(), StringComparison.OrdinalIgnoreCase));
                    SaveState(host, symbols, "");
                }));
            }
            return items;
        }

        private void AddSymbol(IWidgetHost host)
        {
            var input = host.PromptText("Add symbol",
                "Yahoo Finance symbol, e.g. VWCE.DE, IWDA.AS, AAPL, ^GSPC, BTC-USD:", "");
            if (string.IsNullOrWhiteSpace(input))
                return;
            var symbols = GetSymbols(host);
            var symbol = input.Trim().ToUpperInvariant();
            if (!symbols.Contains(symbol))
                symbols.Add(symbol);
            SaveState(host, symbols, GetView(host));
            EnsureData(symbols, host);
        }

        private void InvalidateCache(string symbol)
        {
            lock (sync)
            {
                if (cache.TryGetValue(symbol, out var quote))
                    quote.FetchedAt = DateTime.MinValue;
            }
        }

        #endregion

        #region Data

        private void EnsureData(List<string> symbols, IWidgetHost host)
        {
            List<string> toFetch;
            lock (sync)
            {
                toFetch = symbols.Where(s =>
                {
                    if (!cache.TryGetValue(s, out var quote))
                    {
                        cache[s] = new Quote { Fetching = true };
                        return true;
                    }
                    if (quote.Fetching || DateTime.Now - quote.FetchedAt < FetchInterval)
                        return false;
                    quote.Fetching = true;
                    return true;
                }).ToList();
            }
            if (toFetch.Count == 0)
                return;

            Task.Run(() =>
            {
                foreach (var symbol in toFetch)
                    FetchSymbol(symbol);
                host.RequestRefresh();
            });
        }

        private void FetchSymbol(string symbol)
        {
            var quote = new Quote();
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var client = new WebClient { Encoding = System.Text.Encoding.UTF8 })
                {
                    client.Headers.Add("User-Agent", "Mozilla/5.0 (FlowGrid widget)");
                    var json = client.DownloadString(
                        "https://query1.finance.yahoo.com/v8/finance/chart/" + Uri.EscapeDataString(symbol) +
                        "?range=1mo&interval=1d");

                    quote.Price = Num(json, "regularMarketPrice");
                    quote.PreviousClose = Num(json, "chartPreviousClose");
                    quote.Currency = Str(json, "currency") ?? "";
                    quote.History = ParseCloses(json);
                    if (!double.IsNaN(quote.Price))
                        quote.History.Add(quote.Price);
                    else
                        quote.Error = "no data";
                }
            }
            catch
            {
                quote.Error = "error";
            }
            finally
            {
                quote.FetchedAt = DateTime.Now;
                quote.Fetching = false;
                lock (sync)
                    cache[symbol] = quote;
            }
        }

        private static List<double> ParseCloses(string json)
        {
            var result = new List<double>();
            var match = Regex.Match(json, "\"close\"\\s*:\\s*\\[([^\\]]*)\\]");
            if (!match.Success)
                return result;
            foreach (var raw in match.Groups[1].Value.Split(','))
            {
                var value = raw.Trim();
                if (value.Length == 0 || value == "null")
                    continue;
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                    result.Add(parsed);
            }
            return result;
        }

        private static double Num(string json, string key)
        {
            var match = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*(-?[0-9.]+)");
            return match.Success
                ? double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)
                : double.NaN;
        }

        private static string Str(string json, string key)
        {
            var match = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"([^\"]*)\"");
            return match.Success ? match.Groups[1].Value : null;
        }

        #endregion
    }
}
