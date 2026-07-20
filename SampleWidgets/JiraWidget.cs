using FlowGrid.Sdk;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace SampleWidgets
{
    /// <summary>
    /// Enterprise blueprint plugin: shows your open Jira issues in a real table
    /// (SDK v4 control hosting). Setup via right-click → "Configure Jira...";
    /// the API token is stored DPAPI-encrypted via the SDK secret store.
    /// Double-click an issue to open it in the browser.
    /// </summary>
    public class JiraWidget : IFlowGridControlWidget, IFlowGridWidget3
    {
        public string Name => "Jira Issues";

        public int RefreshIntervalMs => 0; // the hosted control refreshes itself

        public Control CreateControl(IWidgetHost host)
        {
            return new JiraBoardControl(host);
        }

        public void Render(Graphics g, Rectangle area, IWidgetHost host)
        {
            // Fallback only - normally the hosted control paints instead.
            g.DrawString("Jira widget could not create its view.", host.BaseFont,
                new SolidBrush(Color.FromArgb(200, Color.IndianRed)), area,
                new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
        }

        public bool OnClick(Point location, Rectangle area, IWidgetHost host)
        {
            return false; // clicks land on the hosted control
        }

        public IList<WidgetMenuItem> GetMenuItems(IWidgetHost host)
        {
            return new List<WidgetMenuItem>
            {
                new WidgetMenuItem("Configure Jira...", () => Configure(host))
            };
        }

        private static void Configure(IWidgetHost host)
        {
            var url = host.PromptText("Jira - step 1/3",
                "Base URL of your Jira instance (e.g. https://mycompany.atlassian.net):",
                JiraConfig.GetUrl(host));
            if (url == null)
                return;
            var email = host.PromptText("Jira - step 2/3",
                "Your Jira account e-mail:", JiraConfig.GetEmail(host));
            if (email == null)
                return;
            var token = host.PromptText("Jira - step 3/3",
                "API token (create one at id.atlassian.com → Security → API tokens). Stored encrypted, leave empty to keep the current one:", "");
            if (token == null)
                return;

            host.Settings = "url=" + url.Trim().TrimEnd('/') + ";email=" + email.Trim();
            if (token.Trim().Length > 0)
                host.SetSecret("api-token", token.Trim());

            ConfigurationChanged?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>Raised after "Configure Jira..." so open views reload immediately.</summary>
        internal static event EventHandler ConfigurationChanged;
    }

    internal static class JiraConfig
    {
        public static string GetUrl(IWidgetHost host) => GetPart(host.Settings, "url");

        public static string GetEmail(IWidgetHost host) => GetPart(host.Settings, "email");

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
    }

    /// <summary>
    /// The hosted WinForms view: dark-styled DataGridView + status label,
    /// with its own 5-minute refresh timer and async loading.
    /// </summary>
    internal class JiraBoardControl : UserControl
    {
        private readonly IWidgetHost host;
        private readonly DataGridView grid;
        private readonly Label statusLabel;
        private readonly Timer refreshTimer;
        private bool loading;

        public JiraBoardControl(IWidgetHost host)
        {
            this.host = host;

            BackColor = Color.FromArgb(24, 24, 28);

            grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BorderStyle = BorderStyle.None,
                BackgroundColor = BackColor,
                GridColor = Color.FromArgb(45, 45, 52),
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                EnableHeadersVisualStyles = false
            };
            grid.DefaultCellStyle.BackColor = BackColor;
            grid.DefaultCellStyle.ForeColor = Color.White;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(50, 60, 80);
            grid.DefaultCellStyle.SelectionForeColor = Color.White;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(32, 32, 38);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(190, 190, 200);

            grid.Columns.Add("key", "Key");
            grid.Columns.Add("summary", "Summary");
            grid.Columns.Add("status", "Status");
            grid.Columns["key"].FillWeight = 22;
            grid.Columns["summary"].FillWeight = 56;
            grid.Columns["status"].FillWeight = 22;

            grid.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex < 0)
                    return;
                var key = grid.Rows[e.RowIndex].Cells["key"].Value as string;
                var url = JiraConfig.GetUrl(host);
                if (!string.IsNullOrEmpty(key) && url.Length > 0)
                    try { Process.Start(url + "/browse/" + key); } catch { }
            };

            statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(200, 200, 210),
                BackColor = BackColor,
                Padding = new Padding(12)
            };

            Controls.Add(grid);
            Controls.Add(statusLabel);
            ShowStatus("Loading...");

            refreshTimer = new Timer { Interval = 5 * 60 * 1000 };
            refreshTimer.Tick += (s, e) => LoadIssues();
            refreshTimer.Start();

            HandleCreated += (s, e) => LoadIssues();

            EventHandler onConfigChanged = (s, e) => LoadIssues();
            JiraWidget.ConfigurationChanged += onConfigChanged;
            Disposed += (s, e) =>
            {
                JiraWidget.ConfigurationChanged -= onConfigChanged;
                refreshTimer.Dispose();
            };
        }

        private void ShowStatus(string text)
        {
            statusLabel.Text = text;
            statusLabel.Visible = true;
            statusLabel.BringToFront();
        }

        private void ShowGrid()
        {
            statusLabel.Visible = false;
            grid.BringToFront();
        }

        public void LoadIssues()
        {
            if (loading)
                return;

            var url = JiraConfig.GetUrl(host);
            var email = JiraConfig.GetEmail(host);
            var token = host.GetSecret("api-token");
            if (url.Length == 0 || email.Length == 0 || string.IsNullOrEmpty(token))
            {
                ShowStatus("Not configured.\nRight-click the fence → \"Configure Jira...\"");
                return;
            }

            loading = true;
            Task.Run(() =>
            {
                List<string[]> rows = null;
                string error = null;
                try
                {
                    rows = FetchIssues(url, email, token);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        loading = false;
                        if (error != null)
                        {
                            ShowStatus("Jira request failed:\n" + error);
                            return;
                        }
                        grid.Rows.Clear();
                        foreach (var row in rows)
                            grid.Rows.Add(row[0], row[1], row[2]);
                        if (rows.Count == 0)
                            ShowStatus("No open issues assigned to you. 🎉");
                        else
                            ShowGrid();
                    }));
                }
                catch
                {
                    // Control disposed while fetching.
                }
            });
        }

        private static List<string[]> FetchIssues(string baseUrl, string email, string token)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var client = new WebClient { Encoding = Encoding.UTF8 })
            {
                client.Headers.Add("User-Agent", "FlowGrid Jira widget");
                client.Headers.Add("Authorization", "Basic " +
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(email + ":" + token)));
                var json = client.DownloadString(baseUrl +
                    "/rest/api/2/search?maxResults=30&fields=summary,status&jql=" +
                    Uri.EscapeDataString("assignee = currentUser() AND statusCategory != Done ORDER BY updated DESC"));

                var serializer = new JavaScriptSerializer { MaxJsonLength = 20 * 1024 * 1024 };
                var root = serializer.DeserializeObject(json) as Dictionary<string, object>;
                var rows = new List<string[]>();
                if (root != null && root.TryGetValue("issues", out var issuesObj) && issuesObj is object[] issues)
                {
                    foreach (var issueObj in issues)
                    {
                        if (!(issueObj is Dictionary<string, object> issue))
                            continue;
                        var key = issue.TryGetValue("key", out var k) ? k as string : null;
                        var summary = "";
                        var status = "";
                        if (issue.TryGetValue("fields", out var fieldsObj) && fieldsObj is Dictionary<string, object> fields)
                        {
                            summary = fields.TryGetValue("summary", out var s) ? s as string ?? "" : "";
                            if (fields.TryGetValue("status", out var statusObj) && statusObj is Dictionary<string, object> statusDict)
                                status = statusDict.TryGetValue("name", out var n) ? n as string ?? "" : "";
                        }
                        if (key != null)
                            rows.Add(new[] { key, summary, status });
                    }
                }
                return rows;
            }
        }
    }
}
