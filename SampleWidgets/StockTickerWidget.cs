using FlowGrid.Sdk;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SampleWidgets
{
    /// <summary>
    /// Stock/ETF ticker using Yahoo Finance's public chart endpoint (no API key).
    /// Symbols: create %LOCALAPPDATA%\FlowGrid\Plugins\stocks.txt with one Yahoo
    /// symbol per line or comma-separated (e.g. "VWCE.DE, AAPL, ^GSPC, BTC-USD").
    /// Click the widget to refresh immediately.
    /// </summary>
    public class StockTickerWidget : IFlowGridWidget2
    {
        public string Name => "Stocks";

        public int RefreshIntervalMs => 2000;

        private static readonly TimeSpan FetchInterval = TimeSpan.FromMinutes(5);
        private static readonly string[] DefaultSymbols = { "VWCE.DE", "AAPL", "^GSPC" };

        private class Quote
        {
            public string Symbol;
            public string Currency;
            public double Price = double.NaN;
            public double PreviousClose = double.NaN;
            public string Error;
        }

        private readonly object sync = new object();
        private bool fetching;
        private DateTime lastFetch = DateTime.MinValue;
        private List<Quote> quotes = new List<Quote>();

        public void Render(Graphics g, Rectangle area, IWidgetHost host)
        {
            EnsureData();

            List<Quote> snapshot;
            lock (sync)
                snapshot = new List<Quote>(quotes);

            var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            if (snapshot.Count == 0)
            {
                g.DrawString("Loading quotes...", host.BaseFont, new SolidBrush(Color.FromArgb(200, Color.White)), area, center);
                return;
            }

            var margin = 12;
            var rowHeight = Math.Min(34, Math.Max(18, (area.Height - 2 * margin) / snapshot.Count));
            var y = area.Y + margin;
            var rightAlign = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
            var leftAlign = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };

            foreach (var quote in snapshot)
            {
                if (y + rowHeight > area.Bottom - margin + 2)
                    break;
                var rowRect = new RectangleF(area.X + margin, y, area.Width - 2 * margin, rowHeight);

                g.DrawString(quote.Symbol, host.BaseFont, Brushes.White, rowRect, leftAlign);

                if (double.IsNaN(quote.Price))
                {
                    g.DrawString(quote.Error ?? "–", host.BaseFont,
                        new SolidBrush(Color.FromArgb(160, Color.White)), rowRect, rightAlign);
                }
                else
                {
                    var changeText = "";
                    var changeColor = Color.FromArgb(200, Color.White);
                    if (!double.IsNaN(quote.PreviousClose) && quote.PreviousClose > 0)
                    {
                        var changePercent = (quote.Price - quote.PreviousClose) / quote.PreviousClose * 100;
                        changeText = string.Format(CultureInfo.CurrentCulture, "{0:+0.00;-0.00} %", changePercent);
                        changeColor = changePercent >= 0 ? Color.MediumSeaGreen : Color.IndianRed;
                    }

                    var priceText = string.Format(CultureInfo.CurrentCulture, "{0:0.00} {1}", quote.Price, quote.Currency);
                    var changeSize = g.MeasureString(changeText, host.BaseFont);
                    var changeRect = new RectangleF(rowRect.Right - changeSize.Width - 2, rowRect.Y, changeSize.Width + 2, rowRect.Height);
                    var priceRect = new RectangleF(rowRect.X, rowRect.Y, rowRect.Width - changeSize.Width - 10, rowRect.Height);

                    g.DrawString(priceText, host.BaseFont, new SolidBrush(Color.FromArgb(220, Color.White)), priceRect, rightAlign);
                    g.DrawString(changeText, host.BaseFont, new SolidBrush(changeColor), changeRect, rightAlign);
                }
                y += rowHeight;
            }
        }

        public bool OnClick(Point location, Rectangle area, IWidgetHost host)
        {
            // Any click forces an immediate refresh.
            lock (sync)
                lastFetch = DateTime.MinValue;
            EnsureData();
            return true;
        }

        private static string[] ReadSymbols()
        {
            try
            {
                var configFile = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FlowGrid", "Plugins", "stocks.txt");
                if (File.Exists(configFile))
                {
                    var parts = File.ReadAllText(configFile)
                        .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    var symbols = new List<string>();
                    foreach (var part in parts)
                        if (part.Trim().Length > 0)
                            symbols.Add(part.Trim().ToUpperInvariant());
                    if (symbols.Count > 0)
                        return symbols.ToArray();
                }
            }
            catch
            {
                // Fall through to defaults.
            }
            return DefaultSymbols;
        }

        private void EnsureData()
        {
            lock (sync)
            {
                if (fetching || DateTime.Now - lastFetch < FetchInterval)
                    return;
                fetching = true;
            }
            Task.Run((Action)Fetch);
        }

        private void Fetch()
        {
            var result = new List<Quote>();
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                foreach (var symbol in ReadSymbols())
                {
                    var quote = new Quote { Symbol = symbol, Currency = "" };
                    try
                    {
                        using (var client = new WebClient { Encoding = System.Text.Encoding.UTF8 })
                        {
                            client.Headers.Add("User-Agent", "Mozilla/5.0 (FlowGrid widget)");
                            var json = client.DownloadString(
                                "https://query1.finance.yahoo.com/v8/finance/chart/" + Uri.EscapeDataString(symbol) +
                                "?range=1d&interval=1d");
                            quote.Price = Num(json, "regularMarketPrice");
                            quote.PreviousClose = Num(json, "chartPreviousClose");
                            quote.Currency = Str(json, "currency") ?? "";
                            if (double.IsNaN(quote.Price))
                                quote.Error = "no data";
                        }
                    }
                    catch
                    {
                        quote.Error = "error";
                    }
                    result.Add(quote);
                }
            }
            finally
            {
                lock (sync)
                {
                    quotes = result;
                    fetching = false;
                    lastFetch = DateTime.Now;
                }
            }
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
    }
}
