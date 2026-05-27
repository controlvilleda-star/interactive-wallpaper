using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace InteractiveWallpaper
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768 | SecurityProtocolType.Tls;
            BrowserFeatureControl.EnableIe11Mode();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            AppSettings settings = AppSettings.Load();
            bool windowed = HasArg(args, "--windowed") || HasArg(args, "/windowed");
            Application.Run(new MainForm(settings, !windowed));
        }

        private static bool HasArg(string[] args, string value)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }

    internal static class BrowserFeatureControl
    {
        public static void EnableIe11Mode()
        {
            try
            {
                string exeName = Path.GetFileName(Application.ExecutablePath);
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION"))
                {
                    if (key != null)
                    {
                        key.SetValue(exeName, 11001, RegistryValueKind.DWord);
                    }
                }
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_SCRIPTURL_MITIGATION"))
                {
                    if (key != null)
                    {
                        key.SetValue(exeName, 1, RegistryValueKind.DWord);
                    }
                }
            }
            catch
            {
                // The app still works if the browser feature keys cannot be written.
            }
        }
    }

    internal sealed class AppSettings
    {
        private readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string CalendarIcsUrl
        {
            get { return GetString("CalendarIcsUrl", ""); }
            set { SetString("CalendarIcsUrl", value); }
        }

        public string WebUrl
        {
            get { return GetString("WebUrl", "https://calendar.google.com/calendar/u/0/r"); }
            set { SetString("WebUrl", value); }
        }

        public static string DataDirectory
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "InteractiveWallpaper");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string ConfigPath
        {
            get { return Path.Combine(DataDirectory, "settings.ini"); }
        }

        public static AppSettings Load()
        {
            AppSettings settings = new AppSettings();
            settings.WebUrl = "https://calendar.google.com/calendar/u/0/r";

            if (!File.Exists(ConfigPath))
            {
                return settings;
            }

            string[] lines = File.ReadAllLines(ConfigPath);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }

                string key = line.Substring(0, eq).Trim();
                string raw = line.Substring(eq + 1).Trim();
                settings.values[key] = Decode(raw);
            }

            return settings;
        }

        public void Save()
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("# Interactive Wallpaper settings");
            foreach (KeyValuePair<string, string> pair in values)
            {
                builder.Append(pair.Key);
                builder.Append('=');
                builder.AppendLine(Encode(pair.Value));
            }
            File.WriteAllText(ConfigPath, builder.ToString(), Encoding.UTF8);
        }

        public string GetString(string key, string fallback)
        {
            string value;
            if (values.TryGetValue(key, out value))
            {
                return value;
            }
            return fallback;
        }

        public void SetString(string key, string value)
        {
            values[key] = value == null ? "" : value;
        }

        public int GetInt(string key, int fallback)
        {
            int parsed;
            if (int.TryParse(GetString(key, ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                return parsed;
            }
            return fallback;
        }

        public void SetInt(string key, int value)
        {
            SetString(key, value.ToString(CultureInfo.InvariantCulture));
        }

        public bool GetBool(string key, bool fallback)
        {
            bool parsed;
            if (bool.TryParse(GetString(key, ""), out parsed))
            {
                return parsed;
            }
            return fallback;
        }

        public void SetBool(string key, bool value)
        {
            SetString(key, value ? "true" : "false");
        }

        private static string Encode(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value == null ? "" : value));
        }

        private static string Decode(string value)
        {
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(value));
            }
            catch
            {
                return value;
            }
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly AppSettings settings;
        private readonly bool wallpaperMode;
        private readonly CultureInfo spanishCulture = new CultureInfo("es-ES");
        private readonly List<CalendarEvent> calendarEvents = new List<CalendarEvent>();
        private readonly System.Windows.Forms.Timer clockTimer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer calendarTimer = new System.Windows.Forms.Timer();

        private NotifyIcon trayIcon;
        private WidgetPanel clockWidget;
        private WidgetPanel calendarWidget;
        private WidgetPanel webWidget;
        private Label clockTimeLabel;
        private Label clockDateLabel;
        private Label monthLabel;
        private TableLayoutPanel monthGrid;
        private FlowLayoutPanel eventsPanel;
        private Label eventsStatusLabel;
        private TextBox webUrlBox;
        private WebBrowser webBrowser;

        public MainForm(AppSettings settings, bool wallpaperMode)
        {
            this.settings = settings;
            this.wallpaperMode = wallpaperMode;

            Text = "Interactive Wallpaper";
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(14, 17, 24);
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            if (wallpaperMode)
            {
                Rectangle screen = Screen.PrimaryScreen.Bounds;
                Bounds = new Rectangle(0, 0, screen.Width, screen.Height);
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
            }
            else
            {
                Size = new Size(1280, 720);
                StartPosition = FormStartPosition.CenterScreen;
                FormBorderStyle = FormBorderStyle.Sizable;
                ShowInTaskbar = true;
            }

            BuildTray();
            BuildCommandDock();
            BuildClockWidget();
            BuildCalendarWidget();
            BuildWebWidget();

            clockTimer.Interval = 1000;
            clockTimer.Tick += delegate { UpdateClock(); };
            clockTimer.Start();
            UpdateClock();

            calendarTimer.Interval = 15 * 60 * 1000;
            calendarTimer.Tick += delegate { RefreshCalendarEvents(); };
            calendarTimer.Start();
            RefreshCalendarEvents();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (wallpaperMode)
            {
                DesktopHost.AttachToDesktop(Handle);
                Rectangle screen = Screen.PrimaryScreen.Bounds;
                Bounds = new Rectangle(0, 0, screen.Width, screen.Height);
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Rectangle rect = ClientRectangle;
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return;
            }

            using (LinearGradientBrush brush = new LinearGradientBrush(rect, Color.FromArgb(8, 12, 20), Color.FromArgb(20, 48, 54), 35F))
            {
                e.Graphics.FillRectangle(brush, rect);
            }

            using (Pen linePen = new Pen(Color.FromArgb(35, 255, 255, 255), 1F))
            {
                for (int x = 0; x < rect.Width; x += 96)
                {
                    e.Graphics.DrawLine(linePen, x, 0, x - 260, rect.Height);
                }
            }

            using (SolidBrush glow = new SolidBrush(Color.FromArgb(28, 255, 190, 95)))
            {
                e.Graphics.FillEllipse(glow, rect.Width - 360, 80, 520, 520);
            }
            using (SolidBrush glow = new SolidBrush(Color.FromArgb(22, 92, 180, 210)))
            {
                e.Graphics.FillEllipse(glow, -220, rect.Height - 280, 460, 460);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveLayout();
            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
            }
            base.OnFormClosing(e);
        }

        private void BuildTray()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Ajustes", null, delegate { ShowSettings(); });
            menu.Items.Add("Recargar calendario", null, delegate { RefreshCalendarEvents(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Mostrar/Ocultar reloj", null, delegate { ToggleWidget(clockWidget, "Clock"); });
            menu.Items.Add("Mostrar/Ocultar calendario", null, delegate { ToggleWidget(calendarWidget, "Calendar"); });
            menu.Items.Add("Mostrar/Ocultar web", null, delegate { ToggleWidget(webWidget, "Web"); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Salir", null, delegate { Close(); });

            trayIcon = new NotifyIcon();
            trayIcon.Icon = SystemIcons.Application;
            trayIcon.Text = "Interactive Wallpaper";
            trayIcon.ContextMenuStrip = menu;
            trayIcon.Visible = true;
            trayIcon.DoubleClick += delegate { ShowSettings(); };
        }

        private void BuildCommandDock()
        {
            FlowLayoutPanel dock = new FlowLayoutPanel();
            dock.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            dock.FlowDirection = FlowDirection.LeftToRight;
            dock.WrapContents = false;
            dock.AutoSize = true;
            dock.BackColor = Color.FromArgb(42, 22, 28, 36);
            dock.Padding = new Padding(8);
            dock.Location = new Point(Math.Max(16, Width - 420), 18);

            AddDockButton(dock, "Reloj", delegate { ToggleWidget(clockWidget, "Clock"); });
            AddDockButton(dock, "Cal", delegate { ToggleWidget(calendarWidget, "Calendar"); });
            AddDockButton(dock, "Web", delegate { ToggleWidget(webWidget, "Web"); });
            AddDockButton(dock, "Ajustes", delegate { ShowSettings(); });
            AddDockButton(dock, "Salir", delegate { Close(); });

            Controls.Add(dock);
            dock.BringToFront();
        }

        private void AddDockButton(FlowLayoutPanel dock, string text, EventHandler click)
        {
            Button button = new Button();
            button.Text = text;
            button.AutoSize = true;
            button.Height = 30;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Color.FromArgb(90, 255, 255, 255);
            button.ForeColor = Color.White;
            button.BackColor = Color.FromArgb(70, 35, 45, 58);
            button.Margin = new Padding(3);
            button.Click += click;
            dock.Controls.Add(button);
        }

        private void BuildClockWidget()
        {
            clockWidget = new WidgetPanel("Reloj");
            clockWidget.Size = new Size(310, 170);
            clockWidget.Location = new Point(42, 48);
            clockWidget.ApplySettings(settings, "Clock");

            clockTimeLabel = new Label();
            clockTimeLabel.Dock = DockStyle.Top;
            clockTimeLabel.Height = 76;
            clockTimeLabel.TextAlign = ContentAlignment.MiddleLeft;
            clockTimeLabel.ForeColor = Color.White;
            clockTimeLabel.Font = new Font("Segoe UI Light", 34F, FontStyle.Regular, GraphicsUnit.Point);

            clockDateLabel = new Label();
            clockDateLabel.Dock = DockStyle.Fill;
            clockDateLabel.TextAlign = ContentAlignment.TopLeft;
            clockDateLabel.ForeColor = Color.FromArgb(210, 226, 232, 238);
            clockDateLabel.Font = new Font("Segoe UI", 12F, FontStyle.Regular, GraphicsUnit.Point);

            clockWidget.Body.Controls.Add(clockDateLabel);
            clockWidget.Body.Controls.Add(clockTimeLabel);
            Controls.Add(clockWidget);
        }

        private void BuildCalendarWidget()
        {
            calendarWidget = new WidgetPanel("Google Calendar");
            calendarWidget.Size = new Size(430, 460);
            calendarWidget.Location = new Point(42, 240);
            calendarWidget.ApplySettings(settings, "Calendar");

            Panel body = calendarWidget.Body;
            monthLabel = new Label();
            monthLabel.Dock = DockStyle.Top;
            monthLabel.Height = 34;
            monthLabel.TextAlign = ContentAlignment.MiddleLeft;
            monthLabel.ForeColor = Color.White;
            monthLabel.Font = new Font("Segoe UI Semibold", 13F, FontStyle.Regular, GraphicsUnit.Point);

            monthGrid = new TableLayoutPanel();
            monthGrid.Dock = DockStyle.Top;
            monthGrid.Height = 220;
            monthGrid.ColumnCount = 7;
            monthGrid.RowCount = 7;
            monthGrid.BackColor = Color.Transparent;
            monthGrid.Padding = new Padding(0, 2, 0, 8);
            for (int i = 0; i < 7; i++)
            {
                monthGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 7F));
            }
            for (int i = 0; i < 7; i++)
            {
                monthGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / 7F));
            }

            eventsStatusLabel = new Label();
            eventsStatusLabel.Dock = DockStyle.Top;
            eventsStatusLabel.Height = 30;
            eventsStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
            eventsStatusLabel.ForeColor = Color.FromArgb(225, 210, 224, 230);

            eventsPanel = new FlowLayoutPanel();
            eventsPanel.Dock = DockStyle.Fill;
            eventsPanel.FlowDirection = FlowDirection.TopDown;
            eventsPanel.WrapContents = false;
            eventsPanel.AutoScroll = true;
            eventsPanel.BackColor = Color.Transparent;

            Button configureButton = MakeSmallButton("Configurar iCal");
            configureButton.Dock = DockStyle.Bottom;
            configureButton.Height = 34;
            configureButton.Click += delegate { ShowSettings(); };

            body.Controls.Add(eventsPanel);
            body.Controls.Add(eventsStatusLabel);
            body.Controls.Add(monthGrid);
            body.Controls.Add(monthLabel);
            body.Controls.Add(configureButton);

            Controls.Add(calendarWidget);
            RenderMonth();
        }

        private void BuildWebWidget()
        {
            webWidget = new WidgetPanel("Ventana web");
            webWidget.Size = new Size(620, 410);
            webWidget.Location = new Point(Math.Max(500, Width - 700), 150);
            webWidget.ApplySettings(settings, "Web");

            Panel topBar = new Panel();
            topBar.Dock = DockStyle.Top;
            topBar.Height = 36;
            topBar.Padding = new Padding(0, 0, 0, 6);

            Button goButton = MakeSmallButton("Ir");
            goButton.Dock = DockStyle.Right;
            goButton.Width = 46;
            goButton.Click += delegate { NavigateWeb(); };

            webUrlBox = new TextBox();
            webUrlBox.Dock = DockStyle.Fill;
            webUrlBox.Text = settings.WebUrl;
            webUrlBox.BorderStyle = BorderStyle.FixedSingle;
            webUrlBox.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    NavigateWeb();
                }
            };

            topBar.Controls.Add(webUrlBox);
            topBar.Controls.Add(goButton);

            webBrowser = new WebBrowser();
            webBrowser.Dock = DockStyle.Fill;
            webBrowser.ScriptErrorsSuppressed = true;
            webBrowser.IsWebBrowserContextMenuEnabled = true;
            webBrowser.AllowWebBrowserDrop = false;

            webWidget.Body.Controls.Add(webBrowser);
            webWidget.Body.Controls.Add(topBar);
            Controls.Add(webWidget);

            NavigateWeb();
        }

        private Button MakeSmallButton(string text)
        {
            Button button = new Button();
            button.Text = text;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Color.FromArgb(90, 255, 255, 255);
            button.BackColor = Color.FromArgb(70, 38, 48, 60);
            button.ForeColor = Color.White;
            button.Margin = new Padding(0, 4, 0, 0);
            return button;
        }

        private void UpdateClock()
        {
            DateTime now = DateTime.Now;
            clockTimeLabel.Text = now.ToString("HH:mm:ss", spanishCulture);
            clockDateLabel.Text = now.ToString("dddd, d 'de' MMMM 'de' yyyy", spanishCulture);
        }

        private void RenderMonth()
        {
            DateTime today = DateTime.Today;
            DateTime first = new DateTime(today.Year, today.Month, 1);
            int startIndex = ((int)first.DayOfWeek + 6) % 7;
            int days = DateTime.DaysInMonth(today.Year, today.Month);

            monthLabel.Text = today.ToString("MMMM yyyy", spanishCulture);
            monthGrid.Controls.Clear();

            string[] headers = new string[] { "L", "M", "X", "J", "V", "S", "D" };
            for (int i = 0; i < headers.Length; i++)
            {
                Label header = MakeDayLabel(headers[i], true, false, false);
                monthGrid.Controls.Add(header, i, 0);
            }

            for (int cell = 0; cell < 42; cell++)
            {
                int dayNumber = cell - startIndex + 1;
                Label label;
                if (dayNumber < 1 || dayNumber > days)
                {
                    label = MakeDayLabel("", false, false, false);
                }
                else
                {
                    DateTime date = new DateTime(today.Year, today.Month, dayNumber);
                    bool isToday = date == today;
                    bool hasEvent = HasEventOn(date);
                    label = MakeDayLabel(dayNumber.ToString(CultureInfo.InvariantCulture), false, isToday, hasEvent);
                }

                int row = (cell / 7) + 1;
                int col = cell % 7;
                monthGrid.Controls.Add(label, col, row);
            }
        }

        private Label MakeDayLabel(string text, bool header, bool today, bool hasEvent)
        {
            Label label = new Label();
            label.Text = text;
            label.Dock = DockStyle.Fill;
            label.Margin = new Padding(2);
            label.TextAlign = ContentAlignment.MiddleCenter;
            label.ForeColor = header ? Color.FromArgb(185, 218, 226, 232) : Color.White;
            label.Font = new Font("Segoe UI", header ? 8.5F : 9.5F, today ? FontStyle.Bold : FontStyle.Regular);
            if (today)
            {
                label.BackColor = Color.FromArgb(214, 127, 85);
            }
            else if (hasEvent)
            {
                label.BackColor = Color.FromArgb(70, 80, 150, 162);
            }
            else
            {
                label.BackColor = Color.FromArgb(22, 255, 255, 255);
            }
            return label;
        }

        private bool HasEventOn(DateTime date)
        {
            for (int i = 0; i < calendarEvents.Count; i++)
            {
                if (calendarEvents[i].Start.Date == date.Date)
                {
                    return true;
                }
            }
            return false;
        }

        private void RefreshCalendarEvents()
        {
            string url = settings.CalendarIcsUrl.Trim();
            if (url.Length == 0)
            {
                eventsStatusLabel.Text = "Pega una URL iCal de Google para sincronizar.";
                eventsPanel.Controls.Clear();
                RenderMonth();
                return;
            }

            eventsStatusLabel.Text = "Sincronizando calendario...";
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<CalendarEvent> fetched = new List<CalendarEvent>();
                string error = null;
                try
                {
                    using (WebClient client = new WebClient())
                    {
                        client.Encoding = Encoding.UTF8;
                        client.Headers.Add("User-Agent", "InteractiveWallpaper/1.0");
                        string ics = client.DownloadString(url);
                        fetched = IcsParser.Parse(ics);
                    }
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                if (!IsDisposed)
                {
                    BeginInvoke(new MethodInvoker(delegate
                    {
                        calendarEvents.Clear();
                        calendarEvents.AddRange(fetched);
                        RenderMonth();
                        RenderEvents(error);
                    }));
                }
            });
        }

        private void RenderEvents(string error)
        {
            eventsPanel.Controls.Clear();
            if (error != null)
            {
                eventsStatusLabel.Text = "No se pudo sincronizar: " + error;
                return;
            }

            DateTime now = DateTime.Now.AddHours(-2);
            List<CalendarEvent> upcoming = new List<CalendarEvent>();
            for (int i = 0; i < calendarEvents.Count; i++)
            {
                if (calendarEvents[i].End >= now)
                {
                    upcoming.Add(calendarEvents[i]);
                }
            }
            upcoming.Sort(CalendarEventComparer.Instance);

            eventsStatusLabel.Text = "Proximos eventos";
            if (upcoming.Count == 0)
            {
                AddEventLine("Sin eventos proximos.", Color.FromArgb(210, 226, 232, 238));
                return;
            }

            int count = Math.Min(8, upcoming.Count);
            for (int i = 0; i < count; i++)
            {
                CalendarEvent item = upcoming[i];
                string when = item.AllDay
                    ? item.Start.ToString("ddd d MMM", spanishCulture)
                    : item.Start.ToString("ddd d MMM HH:mm", spanishCulture);
                AddEventLine(when + "  " + item.Summary, Color.White);
            }
        }

        private void AddEventLine(string text, Color color)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = false;
            label.Width = Math.Max(260, calendarWidget.Body.Width - 26);
            label.Height = 30;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.ForeColor = color;
            label.BackColor = Color.FromArgb(28, 255, 255, 255);
            label.Margin = new Padding(0, 0, 0, 5);
            eventsPanel.Controls.Add(label);
        }

        private void NavigateWeb()
        {
            string url = NormalizeUrl(webUrlBox.Text.Trim());
            if (url.Length == 0)
            {
                return;
            }

            settings.WebUrl = url;
            settings.Save();
            webUrlBox.Text = url;
            try
            {
                webBrowser.Navigate(url);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "No se pudo abrir la pagina", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private string NormalizeUrl(string url)
        {
            if (url.Length == 0)
            {
                return "";
            }
            if (url.IndexOf("://", StringComparison.Ordinal) < 0)
            {
                return "https://" + url;
            }
            return url;
        }

        private void ToggleWidget(WidgetPanel widget, string key)
        {
            widget.Visible = !widget.Visible;
            settings.SetBool(key + ".Visible", widget.Visible);
            settings.Save();
        }

        private void ShowSettings()
        {
            using (SettingsForm form = new SettingsForm(settings))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    settings.Save();
                    webUrlBox.Text = settings.WebUrl;
                    NavigateWeb();
                    RefreshCalendarEvents();
                }
            }
        }

        private void SaveLayout()
        {
            clockWidget.SaveSettings(settings, "Clock");
            calendarWidget.SaveSettings(settings, "Calendar");
            webWidget.SaveSettings(settings, "Web");
            settings.Save();
        }
    }

    internal sealed class WidgetPanel : Panel
    {
        private readonly Panel header;
        private readonly Label titleLabel;
        private readonly Panel resizeGrip;
        private bool dragging;
        private bool resizing;
        private Point dragStart;
        private Size resizeStart;

        public Panel Body { get; private set; }

        public WidgetPanel(string title)
        {
            DoubleBuffered = true;
            MinimumSize = new Size(240, 130);
            BackColor = Color.FromArgb(210, 24, 31, 40);
            Padding = new Padding(1);

            header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 32;
            header.BackColor = Color.FromArgb(230, 34, 43, 54);
            header.Cursor = Cursors.SizeAll;
            header.MouseDown += StartDrag;
            header.MouseMove += ContinueDrag;
            header.MouseUp += StopMouseAction;

            titleLabel = new Label();
            titleLabel.Text = title;
            titleLabel.Dock = DockStyle.Fill;
            titleLabel.TextAlign = ContentAlignment.MiddleLeft;
            titleLabel.Padding = new Padding(10, 0, 0, 0);
            titleLabel.ForeColor = Color.White;
            titleLabel.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
            titleLabel.MouseDown += StartDrag;
            titleLabel.MouseMove += ContinueDrag;
            titleLabel.MouseUp += StopMouseAction;

            Button closeButton = new Button();
            closeButton.Text = "x";
            closeButton.Dock = DockStyle.Right;
            closeButton.Width = 32;
            closeButton.FlatStyle = FlatStyle.Flat;
            closeButton.FlatAppearance.BorderSize = 0;
            closeButton.ForeColor = Color.White;
            closeButton.BackColor = Color.FromArgb(180, 52, 62, 74);
            closeButton.Click += delegate { Visible = false; };

            header.Controls.Add(titleLabel);
            header.Controls.Add(closeButton);

            Body = new Panel();
            Body.Dock = DockStyle.Fill;
            Body.Padding = new Padding(12);
            Body.BackColor = Color.FromArgb(198, 19, 25, 34);

            resizeGrip = new Panel();
            resizeGrip.Size = new Size(18, 18);
            resizeGrip.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            resizeGrip.Cursor = Cursors.SizeNWSE;
            resizeGrip.BackColor = Color.FromArgb(90, 255, 255, 255);
            resizeGrip.MouseDown += StartResize;
            resizeGrip.MouseMove += ContinueResize;
            resizeGrip.MouseUp += StopMouseAction;

            Controls.Add(Body);
            Controls.Add(header);
            Controls.Add(resizeGrip);
            Resize += delegate { PositionResizeGrip(); };
            PositionResizeGrip();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen border = new Pen(Color.FromArgb(95, 255, 255, 255), 1F))
            {
                e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            }
        }

        public void ApplySettings(AppSettings settings, string prefix)
        {
            int x = settings.GetInt(prefix + ".X", Left);
            int y = settings.GetInt(prefix + ".Y", Top);
            int w = settings.GetInt(prefix + ".W", Width);
            int h = settings.GetInt(prefix + ".H", Height);
            bool visible = settings.GetBool(prefix + ".Visible", true);
            Location = new Point(Math.Max(0, x), Math.Max(0, y));
            Size = new Size(Math.Max(MinimumSize.Width, w), Math.Max(MinimumSize.Height, h));
            Visible = visible;
        }

        public void SaveSettings(AppSettings settings, string prefix)
        {
            settings.SetInt(prefix + ".X", Left);
            settings.SetInt(prefix + ".Y", Top);
            settings.SetInt(prefix + ".W", Width);
            settings.SetInt(prefix + ".H", Height);
            settings.SetBool(prefix + ".Visible", Visible);
        }

        private void StartDrag(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }
            dragging = true;
            dragStart = e.Location;
            BringToFront();
        }

        private void ContinueDrag(object sender, MouseEventArgs e)
        {
            if (!dragging)
            {
                return;
            }
            Point screen = ((Control)sender).PointToScreen(e.Location);
            Point parentPoint = Parent.PointToClient(screen);
            Left = Math.Max(0, parentPoint.X - dragStart.X);
            Top = Math.Max(0, parentPoint.Y - dragStart.Y);
        }

        private void StartResize(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }
            resizing = true;
            dragStart = e.Location;
            resizeStart = Size;
            BringToFront();
        }

        private void ContinueResize(object sender, MouseEventArgs e)
        {
            if (!resizing)
            {
                return;
            }
            int width = resizeStart.Width + e.X - dragStart.X;
            int height = resizeStart.Height + e.Y - dragStart.Y;
            Size = new Size(Math.Max(MinimumSize.Width, width), Math.Max(MinimumSize.Height, height));
        }

        private void StopMouseAction(object sender, MouseEventArgs e)
        {
            dragging = false;
            resizing = false;
        }

        private void PositionResizeGrip()
        {
            resizeGrip.Location = new Point(Width - resizeGrip.Width - 3, Height - resizeGrip.Height - 3);
            resizeGrip.BringToFront();
        }
    }

    internal sealed class SettingsForm : Form
    {
        private readonly AppSettings settings;
        private readonly TextBox calendarUrlTextBox;
        private readonly TextBox webUrlTextBox;

        public SettingsForm(AppSettings settings)
        {
            this.settings = settings;

            Text = "Ajustes del wallpaper";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(720, 330);
            MinimizeBox = false;
            MaximizeBox = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            Font = new Font("Segoe UI", 9F);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(16);
            layout.ColumnCount = 1;
            layout.RowCount = 6;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label calendarLabel = new Label();
            calendarLabel.Text = "URL iCal de Google Calendar";
            calendarLabel.Dock = DockStyle.Top;
            calendarLabel.Height = 24;

            calendarUrlTextBox = new TextBox();
            calendarUrlTextBox.Dock = DockStyle.Top;
            calendarUrlTextBox.Text = settings.CalendarIcsUrl;

            Label helpLabel = new Label();
            helpLabel.Text = "En Google Calendar: Configuracion > Integrar calendario > Direccion secreta en formato iCal. Es lectura sincronizada.";
            helpLabel.Dock = DockStyle.Top;
            helpLabel.ForeColor = Color.DimGray;
            helpLabel.Height = 38;

            Label webLabel = new Label();
            webLabel.Text = "Pagina de la ventana web";
            webLabel.Dock = DockStyle.Top;
            webLabel.Height = 24;

            webUrlTextBox = new TextBox();
            webUrlTextBox.Dock = DockStyle.Top;
            webUrlTextBox.Text = settings.WebUrl;

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.RightToLeft;

            Button save = new Button();
            save.Text = "Guardar";
            save.Width = 100;
            save.DialogResult = DialogResult.OK;
            save.Click += delegate
            {
                settings.CalendarIcsUrl = calendarUrlTextBox.Text.Trim();
                settings.WebUrl = webUrlTextBox.Text.Trim();
            };

            Button cancel = new Button();
            cancel.Text = "Cancelar";
            cancel.Width = 100;
            cancel.DialogResult = DialogResult.Cancel;

            buttons.Controls.Add(save);
            buttons.Controls.Add(cancel);

            layout.Controls.Add(calendarLabel, 0, 0);
            layout.Controls.Add(calendarUrlTextBox, 0, 1);
            layout.Controls.Add(helpLabel, 0, 2);
            layout.Controls.Add(webLabel, 0, 3);
            layout.Controls.Add(webUrlTextBox, 0, 4);
            layout.Controls.Add(buttons, 0, 5);
            Controls.Add(layout);

            AcceptButton = save;
            CancelButton = cancel;
        }
    }

    internal sealed class CalendarEvent
    {
        public DateTime Start;
        public DateTime End;
        public string Summary;
        public bool AllDay;
    }

    internal sealed class CalendarEventComparer : IComparer<CalendarEvent>
    {
        public static readonly CalendarEventComparer Instance = new CalendarEventComparer();

        public int Compare(CalendarEvent x, CalendarEvent y)
        {
            return x.Start.CompareTo(y.Start);
        }
    }

    internal static class IcsParser
    {
        public static List<CalendarEvent> Parse(string ics)
        {
            List<CalendarEvent> events = new List<CalendarEvent>();
            List<string> lines = Unfold(ics);
            Dictionary<string, string> current = null;

            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                if (line == "BEGIN:VEVENT")
                {
                    current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    continue;
                }
                if (line == "END:VEVENT")
                {
                    AddEvent(events, current);
                    current = null;
                    continue;
                }
                if (current == null)
                {
                    continue;
                }

                int colon = line.IndexOf(':');
                if (colon <= 0)
                {
                    continue;
                }
                string left = line.Substring(0, colon);
                string value = Unescape(line.Substring(colon + 1));
                int semi = left.IndexOf(';');
                string key = semi >= 0 ? left.Substring(0, semi) : left;
                current[key] = value;
                current[key + ".RAW"] = left;
            }

            events.Sort(CalendarEventComparer.Instance);
            return events;
        }

        private static void AddEvent(List<CalendarEvent> events, Dictionary<string, string> item)
        {
            if (item == null)
            {
                return;
            }

            string startValue;
            if (!item.TryGetValue("DTSTART", out startValue))
            {
                return;
            }

            DateTime start;
            bool allDay;
            if (!TryParseDate(startValue, out start, out allDay))
            {
                return;
            }

            DateTime end = start;
            string endValue;
            bool endAllDay;
            if (item.TryGetValue("DTEND", out endValue) && TryParseDate(endValue, out end, out endAllDay))
            {
                if (end <= start)
                {
                    end = start.AddHours(1);
                }
            }
            else
            {
                end = allDay ? start.AddDays(1) : start.AddHours(1);
            }

            string summary;
            if (!item.TryGetValue("SUMMARY", out summary) || summary.Trim().Length == 0)
            {
                summary = "(Sin titulo)";
            }

            events.Add(new CalendarEvent
            {
                Start = start,
                End = end,
                Summary = summary,
                AllDay = allDay
            });
        }

        private static List<string> Unfold(string text)
        {
            List<string> result = new List<string>();
            string[] rawLines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < rawLines.Length; i++)
            {
                string line = rawLines[i];
                if ((line.StartsWith(" ", StringComparison.Ordinal) || line.StartsWith("\t", StringComparison.Ordinal)) && result.Count > 0)
                {
                    result[result.Count - 1] += line.Substring(1);
                }
                else
                {
                    result.Add(line.TrimEnd());
                }
            }
            return result;
        }

        private static bool TryParseDate(string value, out DateTime date, out bool allDay)
        {
            allDay = false;
            date = DateTime.MinValue;

            if (value.Length == 8)
            {
                allDay = true;
                return DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
            }

            string working = value;
            bool utc = working.EndsWith("Z", StringComparison.OrdinalIgnoreCase);
            if (utc)
            {
                working = working.Substring(0, working.Length - 1);
            }

            string[] formats = new string[] { "yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmm" };
            DateTime parsed;
            if (!DateTime.TryParseExact(working, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            {
                return false;
            }

            date = utc ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc).ToLocalTime() : parsed;
            return true;
        }

        private static string Unescape(string value)
        {
            return value.Replace(@"\n", " ").Replace(@"\N", " ").Replace(@"\,", ",").Replace(@"\;", ";").Replace(@"\\", @"\");
        }
    }

    internal static class DesktopHost
    {
        private const int GWL_STYLE = -16;
        private const int WS_CHILD = 0x40000000;
        private const int SWP_NOZORDER = 0x0004;
        private const int SWP_NOACTIVATE = 0x0010;

        public static bool AttachToDesktop(IntPtr handle)
        {
            try
            {
                IntPtr progman = FindWindow("Progman", null);
                IntPtr result;
                SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 1000, out result);

                IntPtr worker = IntPtr.Zero;
                EnumWindows(delegate(IntPtr topHandle, IntPtr topParam)
                {
                    IntPtr shellView = FindWindowEx(topHandle, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (shellView != IntPtr.Zero)
                    {
                        worker = FindWindowEx(IntPtr.Zero, topHandle, "WorkerW", null);
                    }
                    return true;
                }, IntPtr.Zero);

                if (worker == IntPtr.Zero)
                {
                    worker = progman;
                }

                int style = GetWindowLong(handle, GWL_STYLE);
                SetWindowLong(handle, GWL_STYLE, style | WS_CHILD);
                SetParent(handle, worker);

                Rectangle screen = Screen.PrimaryScreen.Bounds;
                SetWindowPos(handle, IntPtr.Zero, 0, 0, screen.Width, screen.Height, SWP_NOZORDER | SWP_NOACTIVATE);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string windowTitle);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, int flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(IntPtr windowHandle, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
    }
}
