using FlowGrid.Sdk;
using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SampleWidgets
{
    /// <summary>
    /// Weather widget powered by open-meteo.com (free, no API key).
    /// Location: by default detected from your IP. To pin a city, create
    /// %LOCALAPPDATA%\FlowGrid\Plugins\weather-location.txt containing the
    /// city name (e.g. "Wien") and restart FlowGrid.
    /// </summary>
    public class WeatherWidget : IFlowGridWidget3
    {
        public string Name => "Weather";

        public int RefreshIntervalMs => 2000;

        private static readonly TimeSpan FetchInterval = TimeSpan.FromMinutes(10);

        private readonly object sync = new object();
        private bool fetching;
        private DateTime lastFetch = DateTime.MinValue;

        private string city = "";
        private double temperature = double.NaN;
        private double windSpeed = double.NaN;
        private int weatherCode = -1;
        private string error;

        public void Render(Graphics g, Rectangle area, IWidgetHost host)
        {
            EnsureData();

            string cityText, errorText, glyph, description;
            double temp, wind;
            lock (sync)
            {
                cityText = city;
                errorText = error;
                temp = temperature;
                wind = windSpeed;
                DescribeWeather(weatherCode, out glyph, out description);
            }

            var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

            if (double.IsNaN(temp))
            {
                var message = errorText == null ? "Loading weather..." : "Weather unavailable:\n" + errorText;
                g.DrawString(message, host.BaseFont, new SolidBrush(Color.FromArgb(200, Color.White)), area, center);
                return;
            }

            using (var tempFont = new Font("Segoe UI Light", area.Height * 0.32f, GraphicsUnit.Pixel))
            using (var glyphFont = new Font("Segoe UI Symbol", area.Height * 0.24f, GraphicsUnit.Pixel))
            using (var textFont = new Font(host.BaseFont.FontFamily, area.Height * 0.10f, GraphicsUnit.Pixel))
            {
                var topRect = new RectangleF(area.X, area.Y + area.Height * 0.05f, area.Width, area.Height * 0.5f);
                var midRect = new RectangleF(area.X, area.Y + area.Height * 0.52f, area.Width, area.Height * 0.22f);
                var footRect = new RectangleF(area.X, area.Y + area.Height * 0.74f, area.Width, area.Height * 0.2f);

                // Glyph and temperature side by side.
                var tempText = string.Format(CultureInfo.CurrentCulture, "{0:0}°", temp);
                var combined = glyph + "  " + tempText;
                var glyphSize = g.MeasureString(glyph + "  ", glyphFont);
                var tempSize = g.MeasureString(tempText, tempFont);
                var totalWidth = glyphSize.Width + tempSize.Width;
                var startX = area.X + (area.Width - totalWidth) / 2;
                var midY = topRect.Y + topRect.Height / 2;
                g.DrawString(glyph, glyphFont, Brushes.White, startX, midY - glyphSize.Height / 2);
                g.DrawString(tempText, tempFont, Brushes.White, startX + glyphSize.Width, midY - tempSize.Height / 2);

                g.DrawString(description, textFont, new SolidBrush(Color.FromArgb(220, Color.White)), midRect, center);

                var footer = cityText;
                if (!double.IsNaN(wind))
                    footer += (footer.Length > 0 ? "  ·  " : "") + string.Format(CultureInfo.CurrentCulture, "{0:0} km/h", wind);
                g.DrawString(footer, textFont, new SolidBrush(Color.FromArgb(170, Color.White)), footRect, center);
            }
        }

        /// <summary>Click anywhere = refresh now.</summary>
        public bool OnClick(Point location, Rectangle area, IWidgetHost host)
        {
            lock (sync)
                lastFetch = DateTime.MinValue;
            EnsureData();
            return true;
        }

        public System.Collections.Generic.IList<WidgetMenuItem> GetMenuItems(IWidgetHost host)
        {
            return new System.Collections.Generic.List<WidgetMenuItem>
            {
                new WidgetMenuItem("Set weather location...", () =>
                {
                    var current = File.Exists(ConfigFile) ? File.ReadAllText(ConfigFile).Trim() : "";
                    var input = host.PromptText("Weather location",
                        "City name (leave empty to auto-detect via IP):", current);
                    if (input == null)
                        return;
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(ConfigFile));
                        File.WriteAllText(ConfigFile, input.Trim());
                    }
                    catch
                    {
                        return;
                    }
                    lock (sync)
                        lastFetch = DateTime.MinValue;
                    EnsureData();
                })
            };
        }

        private static string ConfigFile => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FlowGrid", "Plugins", "weather-location.txt");

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
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var client = new WebClient { Encoding = System.Text.Encoding.UTF8 })
                {
                    double latitude, longitude;
                    string place;

                    var configFile = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "FlowGrid", "Plugins", "weather-location.txt");
                    var configuredCity = File.Exists(configFile) ? File.ReadAllText(configFile).Trim() : "";

                    if (configuredCity.Length > 0)
                    {
                        // Resolve the configured city via open-meteo's geocoder. The language
                        // parameter matters: without it, "Wien" resolves to Wien, Missouri...
                        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                        var geo = client.DownloadString(
                            "https://geocoding-api.open-meteo.com/v1/search?count=1&language=" + language +
                            "&name=" + Uri.EscapeDataString(configuredCity));
                        latitude = Num(geo, "latitude");
                        longitude = Num(geo, "longitude");
                        place = Str(geo, "name") ?? configuredCity;
                        if (double.IsNaN(latitude))
                            throw new Exception("City not found: " + configuredCity);
                    }
                    else
                    {
                        // No configuration - locate roughly via IP.
                        var ip = client.DownloadString("https://ipapi.co/json/");
                        latitude = Num(ip, "latitude");
                        longitude = Num(ip, "longitude");
                        place = Str(ip, "city") ?? "";
                        if (double.IsNaN(latitude))
                            throw new Exception("IP location failed");
                    }

                    var url = string.Format(CultureInfo.InvariantCulture,
                        "https://api.open-meteo.com/v1/forecast?latitude={0:0.####}&longitude={1:0.####}&current_weather=true",
                        latitude, longitude);
                    var json = client.DownloadString(url);

                    var currentStart = json.IndexOf("\"current_weather\":", StringComparison.Ordinal);
                    if (currentStart < 0)
                        throw new Exception("Unexpected API response");
                    var current = json.Substring(currentStart);

                    lock (sync)
                    {
                        temperature = Num(current, "temperature");
                        windSpeed = Num(current, "windspeed");
                        weatherCode = (int)Num(current, "weathercode");
                        city = place;
                        error = null;
                    }
                }
            }
            catch (Exception ex)
            {
                lock (sync)
                {
                    error = ex.Message;
                }
            }
            finally
            {
                lock (sync)
                {
                    fetching = false;
                    lastFetch = DateTime.Now;
                }
            }
        }

        private static void DescribeWeather(int code, out string glyph, out string description)
        {
            // WMO weather interpretation codes as used by open-meteo.
            if (code == 0) { glyph = "☀"; description = "Clear sky"; }
            else if (code == 1 || code == 2) { glyph = "⛅"; description = "Partly cloudy"; }
            else if (code == 3) { glyph = "☁"; description = "Overcast"; }
            else if (code == 45 || code == 48) { glyph = "☁"; description = "Fog"; }
            else if (code >= 51 && code <= 57) { glyph = "☂"; description = "Drizzle"; }
            else if (code >= 61 && code <= 67) { glyph = "☂"; description = "Rain"; }
            else if (code >= 71 && code <= 77) { glyph = "❄"; description = "Snow"; }
            else if (code >= 80 && code <= 82) { glyph = "☂"; description = "Rain showers"; }
            else if (code == 85 || code == 86) { glyph = "❄"; description = "Snow showers"; }
            else if (code >= 95) { glyph = "⚡"; description = "Thunderstorm"; }
            else { glyph = ""; description = ""; }
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
