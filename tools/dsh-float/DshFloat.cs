// DshFloat —— dsh 对话浮窗（独立窗口版）
// 运行方式：DshFloat.exe [--test] [--list]
// 依赖：同目录 WebView2Loader.dll + Microsoft.Web.WebView2.*.dll
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace DshFloat
{
    internal static class Native
    {
        public const int WM_NCHITTEST = 0x0084;
        public const int WM_HOTKEY = 0x0312;
        public const int WM_GETMINMAXINFO = 0x0024;

        public const int HTCLIENT = 1;
        public const int HTCAPTION = 2;
        public const int HTLEFT = 10;
        public const int HTRIGHT = 11;
        public const int HTTOP = 12;
        public const int HTTOPLEFT = 13;
        public const int HTTOPRIGHT = 14;
        public const int HTBOTTOM = 15;
        public const int HTBOTTOMLEFT = 16;
        public const int HTBOTTOMRIGHT = 17;

        public const int MOD_ALT = 0x0001;
        public const int MOD_CONTROL = 0x0002;

        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, int modifiers, int vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("kernel32.dll")]
        public static extern bool AttachConsole(int processId);

        [DllImport("kernel32.dll")]
        public static extern bool SetConsoleOutputCP(int codePage);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern int GetDeviceCaps(IntPtr hdc, int index);

        public const int LOGPIXELSX = 88;

        public static int SystemDpi()
        {
            IntPtr hdc = GetDC(IntPtr.Zero);
            try
            {
                int dpi = GetDeviceCaps(hdc, LOGPIXELSX);
                return dpi > 0 ? dpi : 96;
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, hdc);
            }
        }

        public static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);
    }

    internal sealed class Config
    {
        public int Width = 480;
        public int Height = 600;
        public int X = int.MinValue;
        public int Y = int.MinValue;
        public int BallX = int.MinValue;
        public int BallY = int.MinValue;
        public bool Pinned = true;
        public int DshPort = 3082;
        public int BridgePort = 0;
        public string DshHome = "";

        public static string DefaultPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini"); }
        }

        public static string PathForRun()
        {
            string over = Environment.GetEnvironmentVariable("DSH_FLOAT_CONFIG");
            return string.IsNullOrEmpty(over) ? DefaultPath : over;
        }

        public static string ResolveDshHome(string configured)
        {
            if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim();
            string env = Environment.GetEnvironmentVariable("DSH_HOME");
            if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh");
        }

        public static Config Load(string path)
        {
            var cfg = new Config();
            try
            {
                if (!File.Exists(path)) return cfg;
                foreach (string line in File.ReadAllLines(path))
                {
                    string s = line.Trim();
                    if (s.Length == 0 || s.StartsWith("#")) continue;
                    int eq = s.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = s.Substring(0, eq).Trim();
                    string v = s.Substring(eq + 1).Trim();
                    switch (k)
                    {
                        case "width": cfg.Width = ParseInt(v, cfg.Width); break;
                        case "height": cfg.Height = ParseInt(v, cfg.Height); break;
                        case "x": cfg.X = ParseInt(v, cfg.X); break;
                        case "y": cfg.Y = ParseInt(v, cfg.Y); break;
                        case "ball_x": cfg.BallX = ParseInt(v, cfg.BallX); break;
                        case "ball_y": cfg.BallY = ParseInt(v, cfg.BallY); break;
                        case "pinned": cfg.Pinned = v != "0"; break;
                        case "dsh_port": cfg.DshPort = ParseInt(v, cfg.DshPort); break;
                        case "bridge_port": cfg.BridgePort = ParseInt(v, cfg.BridgePort); break;
                        case "dsh_home": cfg.DshHome = v; break;
                    }
                }
            }
            catch { }
            return cfg;
        }

        private static int ParseInt(string s, int fallback)
        {
            int v;
            return int.TryParse(s, out v) ? v : fallback;
        }

        public void Save(string path)
        {
            try
            {
                string[] lines = new string[]
                {
                    "# DshFloat 配置，删掉即恢复默认",
                    "width=" + Width,
                    "height=" + Height,
                    "x=" + X,
                    "y=" + Y,
                    "ball_x=" + BallX,
                    "ball_y=" + BallY,
                    "pinned=" + (Pinned ? 1 : 0),
                    "dsh_port=" + DshPort,
                    "bridge_port=" + BridgePort,
                    "dsh_home=" + (DshHome ?? "")
                };
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllLines(path, lines);
            }
            catch { }
        }
    }

    // 本地桥接服务：给自绘 UI 提供同源接口，再转发到 dsh 的 /api RPC 网关
    internal sealed class Bridge
    {
        private readonly string _dshBase;
        private readonly int _preferredPort;
        private readonly string _uiPath;
        private readonly string _workspaceFile;
        private readonly object _uiLock = new object();
        private HttpListener _listener;
        private int _port;
        private string _uiCache;
        private DateTime _uiStamp = DateTime.MinValue;

        /// <summary>由宿主（Program）挂上；/show 请求到 UI 线程后把主窗唤到前台。</summary>
        public Action OnShow;

        public Bridge(int dshPort, int preferredPort, string uiPath, string dshHome)
        {
            _dshBase = "http://127.0.0.1:" + dshPort;
            _preferredPort = preferredPort;
            _uiPath = uiPath;
            _workspaceFile = Path.Combine(Config.ResolveDshHome(dshHome), "storages", "workspace.json");
        }

        public int Port { get { return _port; } }
        public string Url { get { return "http://127.0.0.1:" + _port + "/"; } }
        public string DshBase { get { return _dshBase; } }

        public void Start()
        {
            int start = _preferredPort > 0 ? _preferredPort : 3083;
            for (int p = start; p < start + 12; p++)
            {
                var l = new HttpListener();
                l.Prefixes.Add("http://127.0.0.1:" + p + "/");
                try { l.Start(); }
                catch { try { l.Close(); } catch { } continue; }
                _listener = l;
                _port = p;
                var t = new Thread(Loop);
                t.IsBackground = true;
                t.Start();
                return;
            }
            throw new InvalidOperationException("没有空闲端口可用（3083 起试了 12 个）");
        }

        public void Stop()
        {
            if (_listener == null) return;
            try { _listener.Stop(); _listener.Close(); } catch { }
            _listener = null;
        }

        private void Loop()
        {
            while (true)
            {
                HttpListenerContext ctx;
                try { ctx = _listener.GetContext(); }
                catch { return; }
                ThreadPool.QueueUserWorkItem(delegate { Handle(ctx); });
            }
        }

        private void Handle(HttpListenerContext ctx)
        {
            try
            {
                string path = ctx.Request.Url.AbsolutePath;
                if (path == "/" || path == "/index.html")
                {
                    ServeUi(ctx);
                    return;
                }
                if (path == "/health")
                {
                    bool ok = DshReachable();
                    Write(ctx, 200, "application/json; charset=utf-8",
                        "{\"ok\":" + (ok ? "true" : "false") + ",\"dsh\":\"" + _dshBase + "\",\"port\":" + _port + "}");
                    return;
                }
                if (path == "/show")
                {
                    if (!string.Equals(ctx.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                    {
                        Write(ctx, 405, "application/json; charset=utf-8", "{\"ok\":false,\"error\":\"POST only\"}");
                        return;
                    }
                    try
                    {
                        var cb = OnShow;
                        if (cb != null) cb();
                        Write(ctx, 200, "application/json; charset=utf-8", "{\"ok\":true,\"shown\":true}");
                    }
                    catch (Exception ex)
                    {
                        Write(ctx, 500, "application/json; charset=utf-8",
                            "{\"ok\":false,\"error\":\"" + Escape(ex.Message) + "\"}");
                    }
                    return;
                }
                if (path == "/workspaces")
                {
                    ServeWorkspaces(ctx);
                    return;
                }
                if (path.StartsWith("/rpc/"))
                {
                    string method = Uri.UnescapeDataString(path.Substring(5));
                    string args = ReadAll(ctx.Request.InputStream);
                    if (string.IsNullOrWhiteSpace(args)) args = "{}";
                    Forward(ctx, method, args);
                    return;
                }
                Write(ctx, 404, "text/plain; charset=utf-8", "not found");
            }
            catch
            {
                try
                {
                    Write(ctx, 500, "application/json; charset=utf-8",
                        "{\"result\":{\"ok\":false,\"error\":{\"code\":\"bridge/handler\",\"message\":\"桥接服务处理失败\"}}}");
                }
                catch { }
            }
        }

        private void ServeUi(HttpListenerContext ctx)
        {
            string html;
            try
            {
                lock (_uiLock)
                {
                    var stamp = File.GetLastWriteTimeUtc(_uiPath);
                    if (_uiCache == null || stamp != _uiStamp)
                    {
                        _uiCache = File.ReadAllText(_uiPath);
                        _uiStamp = stamp;
                    }
                    html = _uiCache;
                }
            }
            catch (Exception ex)
            {
                html = "<html><body style=\"font:14px 'Microsoft YaHei UI';padding:24px;color:#333\">" +
                       "<h3>找不到界面文件</h3><p>请确认 DshFloatUI.html 与 exe 在同一目录。</p><p style=\"color:#999\">" +
                       ex.Message + "</p></body></html>";
            }
            Write(ctx, 200, "text/html; charset=utf-8", html);
        }

        private void ServeWorkspaces(HttpListenerContext ctx)
        {
            try
            {
                if (!File.Exists(_workspaceFile))
                {
                    Write(ctx, 404, "application/json; charset=utf-8",
                        "{\"ok\":false,\"error\":\"找不到工作区数据文件\",\"path\":\"" + Escape(_workspaceFile) + "\"}");
                    return;
                }
                string raw = File.ReadAllText(_workspaceFile);
                if (!raw.TrimStart().StartsWith("{", StringComparison.Ordinal))
                {
                    throw new InvalidDataException("workspace.json 不是对象");
                }
                Write(ctx, 200, "application/json; charset=utf-8",
                    "{\"ok\":true,\"path\":\"" + Escape(_workspaceFile) + "\",\"data\":" + raw + "}");
            }
            catch (Exception ex)
            {
                Write(ctx, 500, "application/json; charset=utf-8",
                    "{\"ok\":false,\"error\":\"" + Escape(ex.Message) + "\",\"path\":\"" + Escape(_workspaceFile) + "\"}");
            }
        }

        private bool DshReachable()
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(_dshBase + "/");
                req.Method = "GET";
                req.Timeout = 2500;
                req.Proxy = null;
                using (var resp = req.GetResponse())
                using (var s = resp.GetResponseStream())
                {
                    ReadAll(s);
                    return true;
                }
            }
            catch { return false; }
        }

        private void Forward(HttpListenerContext ctx, string method, string args)
        {
            string envelope = "{\"type\":\"client-request\",\"rpcId\":\"" + Guid.NewGuid().ToString("N") +
                              "\",\"method\":\"" + method.Replace("\\", "\\\\").Replace("\"", "\\\"") +
                              "\",\"payload\":{\"args\":" + args + "}}";
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(envelope);

            var req = (HttpWebRequest)WebRequest.Create(_dshBase + "/api/" + method);
            req.Method = "POST";
            req.ContentType = "application/json; charset=utf-8";
            req.ContentLength = bytes.Length;
            req.Timeout = 90000;
            req.ReadWriteTimeout = 90000;
            req.KeepAlive = false;
            req.Proxy = null;
            try
            {
                using (var s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
                using (var resp = req.GetResponse())
                using (var rs = resp.GetResponseStream())
                {
                    Write(ctx, 200, "application/json; charset=utf-8", Normalize(ReadAll(rs), 200));
                }
            }
            catch (WebException ex)
            {
                if (ex.Response != null)
                {
                    var r = (HttpWebResponse)ex.Response;
                    string body;
                    using (var rs = r.GetResponseStream()) body = ReadAll(rs);
                    Write(ctx, (int)r.StatusCode, "application/json; charset=utf-8", Normalize(body, (int)r.StatusCode));
                    return;
                }
                Write(ctx, 502, "application/json; charset=utf-8",
                    "{\"result\":{\"ok\":false,\"error\":{\"code\":\"bridge/upstream\",\"message\":\"" +
                    Escape(ex.Message) + "\"}}}");
            }
        }

        private static string Escape(string s)
        {
            return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");
        }

        // dsh 网关对端点不存在等情形会直接回纯文本，这里统一包成 JSON 错误
        private static string Normalize(string body, int status)
        {
            string trimmed = (body ?? "").Trim();
            if (trimmed.StartsWith("{", StringComparison.Ordinal)) return trimmed;
            string msg = trimmed.Length > 160 ? trimmed.Substring(0, 160) : trimmed;
            return "{\"result\":{\"ok\":false,\"error\":{\"code\":\"bridge/upstream-" + status +
                   "\",\"message\":\"" + Escape(msg) + "\"}}}";
        }

        private static string ReadAll(Stream s)
        {
            using (var ms = new MemoryStream())
            {
                var buf = new byte[8192];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
                return System.Text.Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static void Write(HttpListenerContext ctx, int status, string contentType, string body)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(body ?? "");
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = contentType;
            ctx.Response.Headers["Cache-Control"] = "no-store";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.OutputStream.Close();
        }
    }    internal sealed class FloatForm : Form
    {
        private const int LogicalMinW = 340;
        private const int LogicalMinH = 470;
        private readonly Bridge _bridge;

        private readonly Config _cfg;
        private readonly string _cfgPath;
        private readonly WebView2 _web;
        private readonly Panel _header;
        private readonly Panel _webHost;
        private readonly Label _title;
        private readonly Label _pinBtn;
        private readonly Label _hideBtn;
        private readonly Label _closeBtn;
        private readonly Label _overlay;
        private float _scale = 1f;
        private bool _ready;
        private bool _navDone;
        private bool _hidingToBall;

        public bool Pinned { get { return TopMost; } }

        public FloatForm(Config cfg, string cfgPath, Bridge bridge)
        {
            _cfg = cfg;
            _cfgPath = cfgPath;
            _bridge = bridge;

            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(22, 23, 27);
            Padding = new Padding(4);
            DoubleBuffered = true;
            Text = "dsh 浮窗";
            TopMost = cfg.Pinned;

            _header = new Panel();
            _header.Dock = DockStyle.None;
            _header.BackColor = Color.FromArgb(28, 30, 36);
            _header.MouseDown += HeaderMouseDown;
            _header.MouseDoubleClick += delegate { ToggleMaximize(); };
            Controls.Add(_header);

            _title = new Label();
            _title.AutoSize = false;
            _title.BackColor = Color.Transparent;
            _title.ForeColor = Color.FromArgb(206, 213, 226);
            _title.Text = "dsh 浮窗";
            _title.TextAlign = ContentAlignment.MiddleLeft;
            _title.MouseDown += HeaderMouseDown;
            _header.Controls.Add(_title);

            _pinBtn = MakeHeaderButton("◉", "置顶");
            _pinBtn.Click += delegate { TogglePin(); };
            _header.Controls.Add(_pinBtn);

            _hideBtn = MakeHeaderButton("—", "收起到悬浮球");
            _hideBtn.Click += delegate { HideToBall(); };
            _header.Controls.Add(_hideBtn);

            _closeBtn = MakeHeaderButton("✕", "关闭浮窗（留在托盘）");
            _closeBtn.Click += delegate { HideToBall(); };
            _header.Controls.Add(_closeBtn);

            _webHost = new Panel();
            _webHost.Dock = DockStyle.None;
            _webHost.BackColor = Color.FromArgb(18, 19, 22);
            Controls.Add(_webHost);
            _header.BringToFront();

            _overlay = new Label();
            _overlay.Dock = DockStyle.Fill;
            _overlay.BackColor = Color.FromArgb(24, 26, 32);
            _overlay.ForeColor = Color.FromArgb(180, 190, 210);
            _overlay.TextAlign = ContentAlignment.MiddleCenter;
            _overlay.Font = new Font("Microsoft YaHei UI", 10f);
            _overlay.Text = "正在连接 dsh…";
            _overlay.Cursor = Cursors.Hand;
            _overlay.Click += (s, e) => { var ignored = LoadWeb(); };
            _overlay.Visible = false;
            _webHost.Controls.Add(_overlay);

            _web = new WebView2();
            _web.Dock = DockStyle.Fill;
            _webHost.Controls.Add(_web);
            _web.BringToFront();

            Load += (s, e) => { var ignored = LoadWeb(); };
        }

        private Label MakeHeaderButton(string glyph, string tip)
        {
            var l = new Label();
            l.AutoSize = false;
            l.Text = glyph;
            l.TextAlign = ContentAlignment.MiddleCenter;
            l.BackColor = Color.Transparent;
            l.ForeColor = Color.FromArgb(150, 160, 178);
            l.Cursor = Cursors.Hand;
            var t = new ToolTip();
            t.SetToolTip(l, tip);
            l.MouseEnter += delegate { l.ForeColor = Color.White; };
            l.MouseLeave += delegate { l.ForeColor = Color.FromArgb(150, 160, 178); };
            return l;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int dpi = Native.SystemDpi();
            _scale = dpi / 96f;
            LayoutChrome();
            ApplyGeometry();
        }

        private int S(int logical) { return (int)Math.Round(logical * _scale); }

        private void LayoutChrome()
        {
            if (!IsHandleCreated) return;
            int pad = S(4);
            int headerH = S(34);
            int innerW = Math.Max(0, ClientSize.Width - pad * 2);

            _header.SetBounds(pad, pad, innerW, headerH);
            _webHost.SetBounds(pad, pad + headerH, innerW,
                Math.Max(0, ClientSize.Height - pad * 2 - headerH));

            _title.SetBounds(S(10), 0, S(150), headerH);
            _title.Font = new Font("Microsoft YaHei UI", 8.8f);

            int bw = S(28);
            int right = _header.Width - S(6);
            _closeBtn.SetBounds(right - bw, 0, bw, headerH);
            _hideBtn.SetBounds(right - bw * 2, 0, bw, headerH);
            _pinBtn.SetBounds(right - bw * 3, 0, bw, headerH);
            UpdatePinGlyph();
        }

        private void UpdatePinGlyph()
        {
            _pinBtn.Text = TopMost ? "◉" : "○";
            _pinBtn.ForeColor = TopMost ? Color.FromArgb(120, 158, 255) : Color.FromArgb(150, 160, 178);
        }

        private void ApplyGeometry()
        {
            int minW = S(LogicalMinW), minH = S(LogicalMinH);
            MinimumSize = new Size(minW, minH);
            MaximumSize = new Size(S(820), S(1000));

            int w = S(Math.Max(_cfg.Width, LogicalMinW));
            int h = S(Math.Max(_cfg.Height, LogicalMinH));
            Size = new Size(w, h);

            if (_cfg.X != int.MinValue && _cfg.Y != int.MinValue)
            {
                Location = new Point(_cfg.X, _cfg.Y);
                EnsureOnScreen();
            }
            else
            {
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                Location = new Point(wa.Right - w - S(24), wa.Bottom - h - S(24));
            }
            UpdateRegion();
        }

        private void EnsureOnScreen()
        {
            Rectangle wa = Screen.FromPoint(Location).WorkingArea;
            int x = Math.Max(wa.Left, Math.Min(Location.X, wa.Right - Width));
            int y = Math.Max(wa.Top, Math.Min(Location.Y, wa.Bottom - Height));
            Location = new Point(x, y);
        }

        private void UpdateRegion()
        {
            int r = S(12);
            using (var path = new GraphicsPath())
            {
                path.AddArc(0, 0, r * 2, r * 2, 180, 90);
                path.AddArc(Width - r * 2, 0, r * 2, r * 2, 270, 90);
                path.AddArc(Width - r * 2, Height - r * 2, r * 2, r * 2, 0, 90);
                path.AddArc(0, Height - r * 2, r * 2, r * 2, 90, 90);
                path.CloseFigure();
                Region = new Region(path);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (!IsHandleCreated) return;
            LayoutChrome();
            UpdateRegion();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Color.FromArgb(46, 50, 60)))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY)
            {
                Program.ToggleVisible();
                return;
            }
            if (m.Msg == Native.WM_NCHITTEST)
            {
                Point p = PointToClient(new Point(m.LParam.ToInt32() & 0xFFFF, (m.LParam.ToInt32() >> 16) & 0xFFFF));
                int edge = S(6);
                bool left = p.X <= edge;
                bool right = p.X >= ClientSize.Width - edge;
                bool bottom = p.Y >= ClientSize.Height - edge;
                bool top = p.Y <= edge;
                if (bottom && right) { m.Result = (IntPtr)Native.HTBOTTOMRIGHT; return; }
                if (bottom && left) { m.Result = (IntPtr)Native.HTBOTTOMLEFT; return; }
                if (top && right) { m.Result = (IntPtr)Native.HTTOPRIGHT; return; }
                if (top && left) { m.Result = (IntPtr)Native.HTTOPLEFT; return; }
                if (left) { m.Result = (IntPtr)Native.HTLEFT; return; }
                if (right) { m.Result = (IntPtr)Native.HTRIGHT; return; }
                if (bottom) { m.Result = (IntPtr)Native.HTBOTTOM; return; }
                if (top) { m.Result = (IntPtr)Native.HTTOP; return; }
                if (p.Y < _header.Height) { m.Result = (IntPtr)Native.HTCAPTION; return; }
                m.Result = (IntPtr)Native.HTCLIENT;
                return;
            }
            base.WndProc(ref m);
        }

        private void HeaderMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && e.Clicks == 1)
            {
                ReleaseCapture();
                SendMessage(Handle, 0x00A1, Native.HTCAPTION, 0);
            }
        }

        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        private void ToggleMaximize()
        {
            Rectangle wa = Screen.FromControl(this).WorkingArea;
            if (Width < wa.Width - S(40))
            {
                _restoreBounds = Bounds;
                Bounds = wa;
            }
            else if (_restoreBounds.Width > 0)
            {
                Bounds = _restoreBounds;
            }
            UpdateRegion();
        }

        private Rectangle _restoreBounds;

        public void TogglePin()
        {
            TopMost = !TopMost;
            _cfg.Pinned = TopMost;
            UpdatePinGlyph();
        }

        private async Task LoadWeb()
        {
            _overlay.Visible = false;
            if (_ready)
            {
                _web.CoreWebView2.Navigate(_bridge.Url);
                return;
            }
            try
            {
                string dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "webview2");
                Directory.CreateDirectory(dataDir);
                var env = await CoreWebView2Environment.CreateAsync(null, dataDir, null);
                await _web.EnsureCoreWebView2Async(env);

                _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                _web.CoreWebView2.Settings.IsStatusBarEnabled = false;
                _web.CoreWebView2.Settings.AreDevToolsEnabled = false;
                _web.CoreWebView2.NewWindowRequested += delegate (object s, CoreWebView2NewWindowRequestedEventArgs e)
                {
                    e.Handled = true;
                    _web.CoreWebView2.Navigate(e.Uri);
                };
                _web.CoreWebView2.NavigationCompleted += delegate (object s, CoreWebView2NavigationCompletedEventArgs e)
                {
                    _navDone = e.IsSuccess;
                    if (!e.IsSuccess)
                    {
                        ShowOffline("dsh 没在运行，或 3082 端口没开。\n点这里重试。");
                    }
                    else
                    {
                        _overlay.Visible = false;
                    }
                };
                _ready = true;
                _web.CoreWebView2.Navigate(_bridge.Url);
            }
            catch (Exception ex)
            {
                ShowOffline("WebView2 初始化失败：\n" + ex.Message + "\n点这里重试。");
            }
        }

        private void ShowOffline(string message)
        {
            _overlay.Text = message;
            _overlay.Visible = true;
            _overlay.BringToFront();
        }

        public async Task<bool> WaitForPageAsync(int timeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (_ready && _navDone) return true;
                await Task.Delay(300);
            }
            return false;
        }

        public async Task<string> EvalAsync(string js)
        {
            if (!_ready) return null;
            return await _web.CoreWebView2.ExecuteScriptAsync(js);
        }

        public string DocumentTitle
        {
            get { return _ready ? _web.CoreWebView2.DocumentTitle : null; }
        }

        public void SaveState()
        {
            if (Program.TestMode) return;
            if (WindowState == FormWindowState.Normal)
            {
                _cfg.Width = (int)Math.Round(Width / _scale);
                _cfg.Height = (int)Math.Round(Height / _scale);
                _cfg.X = Location.X;
                _cfg.Y = Location.Y;
            }
            _cfg.Pinned = TopMost;
            _cfg.Save(_cfgPath);
        }

        public void HideToBall()
        {
            if (_hidingToBall) return;
            _hidingToBall = true;
            try
            {
                SaveState();
                Hide();
                Program.ShowBall();
            }
            finally
            {
                _hidingToBall = false;
            }
        }

        public void ShowFromBall()
        {
            Show();
            EnsureOnScreen();
            UpdateRegion();
        }
    }

    internal sealed class BallForm : Form
    {
        private bool _dragging;
        private Point _dragOrigin;
        private Point _mouseDownScreen;

        public BallForm()
        {
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(47, 107, 255);
            Size = new Size(56, 56);
            Cursor = Cursors.Hand;
            DoubleBuffered = true;
            UpdateRegion();

            var menu = new ContextMenuStrip();
            menu.Items.Add("显示浮窗", null, delegate { Program.ShowMain(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { Program.Quit(); });
            ContextMenuStrip = menu;
        }

        private void UpdateRegion()
        {
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(0, 0, Width, Height);
                Region = new Region(path);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new LinearGradientBrush(new Rectangle(0, 0, Width, Height),
                Color.FromArgb(130, 164, 255), Color.FromArgb(47, 107, 255), 45f))
            {
                e.Graphics.FillEllipse(brush, 1, 1, Width - 2, Height - 2);
            }
            using (var font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold))
            using (var brush = new SolidBrush(Color.White))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                e.Graphics.DrawString("dsh", font, brush, new RectangleF(0, 0, Width, Height - 4), sf);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _dragging = false;
                _dragOrigin = Location;
                _mouseDownScreen = Cursor.Position;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (e.Button != MouseButtons.Left) return;
            Point now = Cursor.Position;
            int dx = now.X - _mouseDownScreen.X;
            int dy = now.Y - _mouseDownScreen.Y;
            if (Math.Abs(dx) + Math.Abs(dy) > 4) _dragging = true;
            if (_dragging)
            {
                Location = new Point(_dragOrigin.X + dx, _dragOrigin.Y + dy);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            if (_dragging)
            {
                _dragging = false;
                Program.RememberBall(this);
            }
            else
            {
                Program.ShowMain();
            }
        }
    }

    internal static class Program
    {
        private static FloatForm _main;
        private static BallForm _ball;
        private static NotifyIcon _tray;
        private static Mutex _mutex;
        private static Config _cfg;
        private static string _cfgPath;
        private static bool _testMode;
        private static Bridge _bridge;

        [STAThread]
        private static int Main(string[] args)
        {
            try { Native.SetProcessDpiAwarenessContext(Native.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2); } catch { }
            try { Native.AttachConsole(-1); } catch { }
            try { Native.SetConsoleOutputCP(65001); } catch { }

            _testMode = Array.IndexOf(args, "--test") >= 0;

            _cfgPath = Config.PathForRun();
            _cfg = Config.Load(_cfgPath);

            if (Array.IndexOf(args, "--list") >= 0)
            {
                return ListStatus();
            }

            bool created;
            _mutex = new Mutex(true, "DshFloat.SingleInstance.v1", out created);
            if (!created)
            {
                MessageBox.Show("DshFloat 已经在运行（看托盘或按 Ctrl+Alt+F）。", "dsh 浮窗",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                _bridge = new Bridge(_cfg.DshPort, _cfg.BridgePort,
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DshFloatUI.html"), _cfg.DshHome);
                _bridge.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show("本地桥接服务启动失败：\n" + ex.Message, "dsh 浮窗",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }

            _main = new FloatForm(_cfg, _cfgPath, _bridge);
            _ball = new BallForm();
            _bridge.OnShow = delegate
            {
                if (_main == null) return;
                try
                {
                    _main.BeginInvoke(new Action(delegate { ShowMain(); }));
                }
                catch { }
            };

            if (_testMode)
            {
                return RunTests();
            }

            SetupTray();
            _main.FormClosing += delegate (object s, FormClosingEventArgs e)
            {
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    _main.HideToBall();
                    return;
                }
                _main.SaveState();
            };

            _main.Show();
            if (_cfg.BallX != int.MinValue && _cfg.BallY != int.MinValue)
            {
                _ball.Location = new Point(_cfg.BallX, _cfg.BallY);
            }
            else
            {
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                _ball.Location = new Point(wa.Right - _ball.Width - 24, wa.Bottom - _ball.Height - 24);
            }
            try { Native.RegisterHotKey(_main.Handle, 1, Native.MOD_CONTROL | Native.MOD_ALT, 0x46); } catch { }

            Application.Run();
            return 0;
        }

        public static bool TestMode { get { return _testMode; } }

        public static void ShowBall()
        {
            if (_ball == null) return;
            if (_testMode)
            {
                _ball.Location = new Point(-4000, -4000);
                _ball.Show();
                return;
            }
            Rectangle wa = Screen.FromPoint(_main.Location).WorkingArea;
            Point p = _ball.Location;
            if (p.X == 0 && p.Y == 0)
            {
                p = new Point(wa.Right - _ball.Width - 24, wa.Bottom - _ball.Height - 24);
            }
            p.X = Math.Max(wa.Left + 8, Math.Min(p.X, wa.Right - _ball.Width - 8));
            p.Y = Math.Max(wa.Top + 8, Math.Min(p.Y, wa.Bottom - _ball.Height - 8));
            _ball.Location = p;
            _ball.Show();
            _ball.BringToFront();
        }

        private static int ListStatus()
        {
            Console.WriteLine("DshFloat 诊断");
            string dshBase = "http://127.0.0.1:" + _cfg.DshPort;
            bool port = false;
            string title = "";
            try
            {
                using (var wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "DshFloat");
                    string html = wc.DownloadString(dshBase + "/");
                    port = html.IndexOf("DeepSeek Harness", StringComparison.OrdinalIgnoreCase) >= 0;
                    int a = html.IndexOf("<title>", StringComparison.OrdinalIgnoreCase);
                    int b = html.IndexOf("</title>", StringComparison.OrdinalIgnoreCase);
                    if (a >= 0 && b > a) title = html.Substring(a + 7, b - a - 7);
                }
            }
            catch { }
            Console.WriteLine("dsh " + _cfg.DshPort + "    : " + (port ? "OK (" + title + ")" : "不可用"));
            string ui = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DshFloatUI.html");
            Console.WriteLine("界面文件  : " + (File.Exists(ui) ? "OK (" + new FileInfo(ui).Length + " B)" : "缺失 " + ui));
            Console.WriteLine("config    : " + Config.PathForRun());
            bool sdk = File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Microsoft.Web.WebView2.Core.dll"));
            Console.WriteLine("webview2  : " + (sdk ? "SDK 就位" : "缺 SDK"));
            Console.WriteLine("桥接端口  : " + (_cfg.BridgePort > 0 ? _cfg.BridgePort.ToString() : "自动（3083 起）"));
            return port && File.Exists(ui) ? 0 : 1;
        }

        private static void SetupTray()
        {
            _tray = new NotifyIcon();
            _tray.Icon = MakeIcon();
            _tray.Text = "dsh 浮窗";
            _tray.Visible = true;

            var menu = new ContextMenuStrip();
            menu.Items.Add("显示 / 收起  (Ctrl+Alt+F)", null, delegate { ToggleVisible(); });
            menu.Items.Add("置顶", null, delegate { _main.TogglePin(); });
            menu.Items.Add("重新加载页面", null, delegate { ReloadPage(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { Quit(); });
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += delegate { ShowMain(); };
        }

        private static Icon MakeIcon()
        {
            var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var b = new LinearGradientBrush(new Rectangle(0, 0, 32, 32), Color.FromArgb(120, 158, 255), Color.FromArgb(47, 107, 255), 45f))
                {
                    g.FillEllipse(b, 1, 1, 30, 30);
                }
                using (var f = new Font("Microsoft YaHei UI", 7.5f, FontStyle.Bold))
                using (var b = new SolidBrush(Color.White))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString("dsh", f, b, new RectangleF(0, 0, 32, 31), sf);
                }
            }
            IntPtr h = bmp.GetHicon();
            return Icon.FromHandle(h);
        }

        public static void ToggleVisible()
        {
            if (_main.Visible) _main.HideToBall();
            else ShowMain();
        }

        public static void ShowMain()
        {
            _ball.Hide();
            _main.ShowFromBall();
            _main.Activate();
        }

        public static void RememberBall(BallForm ball)
        {
            _cfg.BallX = ball.Location.X;
            _cfg.BallY = ball.Location.Y;
            _cfg.Save(_cfgPath);
        }

        public static void ReloadPage()
        {
            if (!_main.Visible) ShowMain();
            _main.BeginInvoke(new Action(async delegate
            {
                try
                {
                    var t = _main.EvalAsync("location.reload()");
                    await t;
                }
                catch { }
            }));
        }

        public static void Quit()
        {
            try { _main.SaveState(); } catch { }
            try { if (_bridge != null) _bridge.Stop(); } catch { }
            if (_tray != null) _tray.Visible = false;
            Application.Exit();
        }

        private static T Pump<T>(Task<T> task, int timeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (!task.IsCompleted && DateTime.UtcNow < deadline)
            {
                Application.DoEvents();
                Thread.Sleep(20);
            }
            if (!task.IsCompleted) return default(T);
            try { return task.Result; } catch { return default(T); }
        }

        private static bool Pump(Task task, int timeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (!task.IsCompleted && DateTime.UtcNow < deadline)
            {
                Application.DoEvents();
                Thread.Sleep(20);
            }
            return task.IsCompleted && !task.IsFaulted;
        }

        private static string ReadStream(Stream s)
        {
            using (var ms = new MemoryStream())
            {
                var buf = new byte[8192];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
                return System.Text.Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static string HttpGet(string url, int timeoutMs)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.Timeout = timeoutMs;
            req.Proxy = null;
            using (var resp = req.GetResponse())
            using (var s = resp.GetResponseStream())
            {
                return ReadStream(s);
            }
        }

        private static string HttpPost(string url, string body, int timeoutMs)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(body);
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.ContentType = "application/json; charset=utf-8";
            req.ContentLength = bytes.Length;
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.Proxy = null;
            using (var s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
            try
            {
                using (var resp = req.GetResponse())
                using (var rs = resp.GetResponseStream())
                {
                    return ReadStream(rs);
                }
            }
            catch (WebException ex)
            {
                if (ex.Response == null) throw;
                using (var rs = ex.Response.GetResponseStream())
                {
                    return ReadStream(rs);
                }
            }
        }

        private static int RunTests()
        {
            var results = new List<string>();
            int failed = 0;
            Action<string, bool, string> check = delegate (string name, bool ok, string detail)
            {
                results.Add((ok ? "PASS  " : "FAIL  ") + name + (detail.Length > 0 ? "  (" + detail + ")" : ""));
                if (!ok) failed++;
            };

            _main.Location = new Point(-4000, -4000);
            _main.Show();
            Application.DoEvents();

            bool pageOk = false;
            try { pageOk = Pump<bool>(_main.WaitForPageAsync(60000), 65000); } catch { }
            check("WebView2 打开本地界面", pageOk, pageOk ? "" : "超时");

            string title = _main.DocumentTitle;
            bool titleOk = title != null && title.IndexOf("dsh 浮窗", StringComparison.Ordinal) >= 0;
            check("页面标题是 dsh 浮窗", titleOk, title ?? "null");

            bool healthOk = false;
            string health = null;
            try
            {
                health = HttpGet(_bridge.Url + "health", 5000);
                healthOk = health != null && health.IndexOf("\"ok\":true", StringComparison.Ordinal) >= 0;
            }
            catch { }
            check("桥接服务健康检查", healthOk, health ?? "null");

            string showResp = null;
            try { showResp = HttpPost(_bridge.Url + "show", "{}", 5000); } catch { }
            Application.DoEvents();
            bool showOk = showResp != null && showResp.IndexOf("\"ok\":true", StringComparison.Ordinal) >= 0;
            check("唤起接口 /show", showOk, showResp == null ? "null" : showResp.Substring(0, Math.Min(80, showResp.Length)));

            string html = null;
            try { html = HttpGet(_bridge.Url, 5000); } catch { }
            bool htmlOk = html != null && html.IndexOf("sessionbar", StringComparison.Ordinal) >= 0;
            check("界面 HTML 可访问", htmlOk, html == null ? "null" : ("len=" + html.Length));

            string wsData = null;
            try { wsData = HttpGet(_bridge.Url + "workspaces", 5000); } catch { }
            bool wsOk = wsData != null && wsData.IndexOf("\"ok\":true", StringComparison.Ordinal) >= 0
                && wsData.IndexOf("\"workspaceIds\"", StringComparison.Ordinal) >= 0
                && wsData.IndexOf("\"pinnedSessionIds\"", StringComparison.Ordinal) >= 0;
            check("工作区数据接口", wsOk, wsData == null ? "null" : wsData.Substring(0, Math.Min(90, wsData.Length)));

            string listing = null;
            try
            {
                listing = HttpPost(_bridge.Url + "rpc/session/list", "{\"_request\":{}}", 15000);
            }
            catch (Exception ex) { listing = ex.Message; }
            bool listOk = listing != null && listing.IndexOf("\"ok\":true", StringComparison.Ordinal) >= 0
                && listing.IndexOf("sessionId", StringComparison.Ordinal) >= 0;
            check("RPC 转发：会话列表", listOk, listing == null ? "null" : listing.Substring(0, Math.Min(90, listing.Length)));

            string bad = null;
            try { bad = HttpPost(_bridge.Url + "rpc/dsh/no-such-method", "{}", 15000); } catch { }
            check("未知方法返回错误而非崩溃", bad != null && bad.IndexOf("\"ok\":false", StringComparison.Ordinal) >= 0,
                bad == null ? "null" : bad.Substring(0, Math.Min(80, bad.Length)));

            // 发送参数校验：用一个不存在的会话 dry-run，网关应回 session/not-found 而不是参数错误
            string dry = "{\"request\":{\"requestId\":\"probe-dry\",\"sessionId\":\"session-00000000-0000-0000-0000-000000000000\",\"mode\":\"queue\",\"content\":[{\"type\":\"text\",\"text\":\"x\"}],\"clientTimeZone\":\"Asia/Shanghai\"}}";
            string promptResp = null;
            try { promptResp = HttpPost(_bridge.Url + "rpc/session/prompt", dry, 20000); } catch { }
            bool promptOk = promptResp != null
                && promptResp.IndexOf("session/not-found", StringComparison.Ordinal) >= 0
                && promptResp.IndexOf("arguments-invalid", StringComparison.Ordinal) < 0;
            check("发送参数被网关接受（dry-run）", promptOk,
                promptResp == null ? "null" : promptResp.Substring(0, Math.Min(120, promptResp.Length)));

            string inputs = null;
            try
            {
                inputs = Pump<string>(_main.EvalAsync(
                    "(() => document.querySelectorAll('#input, #send, #chat').length.toString())()"), 8000);
            }
            catch { }
            check("自绘界面已渲染输入区", inputs != null && inputs.Trim('"') == "3", inputs ?? "null");

            string headerFit = null;
            try
            {
                headerFit = Pump<string>(_main.EvalAsync(
                    "(() => { const t = document.getElementById('stitle'); const a = document.querySelector('.sb-actions'); " +
                    "const n = document.getElementById('newBtn'); const old = t.textContent; " +
                    "t.textContent = '超长会话名称'.repeat(12); " +
                    "const c = (r) => r.top + r.height / 2; " +
                    "const same = Math.abs(c(a.getBoundingClientRect()) - c(t.getBoundingClientRect())) < 3; " +
                    "const noWrap = n.getBoundingClientRect().height <= 27 && n.scrollWidth <= n.clientWidth + 1; " +
                    "const trunc = t.scrollWidth > t.clientWidth; " +
                    "t.textContent = old; return (same && noWrap && trunc).toString(); })()"), 8000);
            }
            catch { }
            check("顶栏长标题不换行", headerFit != null && headerFit.IndexOf("true", StringComparison.Ordinal) >= 0, headerFit ?? "null");

            string rich = null;
            try
            {
                rich = Pump<string>(_main.EvalAsync(
                    "(() => { renderRichText('```python\\nprint(1)\\n```', document.getElementById('chat')); " +
                    "return (typeof renderRichText === 'function' && typeof copyCode === 'function' && " +
                    "document.querySelector('.code') !== null && document.querySelector('.copy-btn') !== null).toString(); })()"), 8000);
            }
            catch { }
            check("代码块渲染 + 复制按钮", rich != null && rich.IndexOf("true", StringComparison.Ordinal) >= 0, rich ?? "null");

            string attach = null;
            try
            {
                attach = Pump<string>(_main.EvalAsync(
                    "(() => { const ids = ['dropmask','tray','fileInput']; " +
                    "const ok = ids.every((id) => document.getElementById(id) !== null); " +
                    "return (ok && typeof addFiles === 'function' && typeof uploadOne === 'function' && " +
                    "typeof renderTray === 'function').toString(); })()"), 8000);
            }
            catch { }
            check("拖拽附件入口", attach != null && attach.IndexOf("true", StringComparison.Ordinal) >= 0, attach ?? "null");

            string uploadProbe = null;
            try
            {
                uploadProbe = HttpPost(_bridge.Url + "rpc/fileUploads/upload",
                    "{\"request\":{\"data\":\"\",\"name\":\"probe.txt\"}}", 15000);
            }
            catch { }
            bool uploadOk = uploadProbe != null && uploadProbe.IndexOf("agentId", StringComparison.Ordinal) >= 0;
            check("上传接口已转发（参数校验）", uploadOk,
                uploadProbe == null ? "null" : uploadProbe.Substring(0, Math.Min(120, uploadProbe.Length)));

            string trayProbe = null;
            try
            {
                trayProbe = Pump<string>(_main.EvalAsync(
                    "(() => { addFiles([new File([new Uint8Array(128)], 'tray-test.txt', {type:'text/plain'})]); " +
                    "const added = document.querySelectorAll('#tray .file-chip').length; " +
                    "const id = app.attachments.length ? app.attachments[0].id : ''; " +
                    "if (id) removeAttachment(id); " +
                    "const left = document.querySelectorAll('#tray .file-chip').length; " +
                    "return (added === 1 && left === 0).toString(); })()"), 8000);
            }
            catch { }
            check("附件条增删", trayProbe != null && trayProbe.IndexOf("true", StringComparison.Ordinal) >= 0, trayProbe ?? "null");

            string stitle = null;
            var titleDeadline = DateTime.UtcNow.AddSeconds(12);
            while (DateTime.UtcNow < titleDeadline)
            {
                try
                {
                    stitle = Pump<string>(_main.EvalAsync("document.getElementById('stitle').textContent"), 6000);
                }
                catch { }
                if (stitle != null && stitle.Trim('"').Length > 0 && stitle.IndexOf("加载会话", StringComparison.Ordinal) < 0) break;
                Application.DoEvents();
                Thread.Sleep(200);
            }
            string st = stitle == null ? "" : stitle.Trim('"');
            check("界面已载入真实会话标题", st.Length > 0 && st.IndexOf("加载会话", StringComparison.Ordinal) < 0,
                stitle ?? "null");

            string grouped = null;
            try
            {
                grouped = Pump<string>(_main.EvalAsync(
                    "(() => { const groups = [...document.querySelectorAll('#pickerList .group')]; " +
                    "const heads = [...document.querySelectorAll('#pickerList .ghead')]; " +
                    "return (typeof buildWorkspaceGroups === 'function' && typeof togglePin === 'function' && " +
                    "document.getElementById('pickerSearch') !== null && groups.length >= 2 && " +
                    "heads.length === groups.length && groups.every((g) => g.querySelector('.gcount') !== null)).toString(); })()"), 8000);
            }
            catch { }
            check("会话按工作区分组", grouped != null && grouped.IndexOf("true", StringComparison.Ordinal) >= 0, grouped ?? "null");

            check("窗口尺寸在允许范围内", _main.Width >= _main.MinimumSize.Width && _main.Height >= _main.MinimumSize.Height,
                _main.Width + "x" + _main.Height);

            var cfg2 = new Config();
            cfg2.Width = 500;
            cfg2.Height = 700;
            string tmp = Path.Combine(Path.GetTempPath(), "dshfloat-test-" + Guid.NewGuid().ToString("N") + ".ini");
            cfg2.Save(tmp);
            var back = Config.Load(tmp);
            check("配置保存与读取", back.Width == 500 && back.Height == 700, back.Width + "x" + back.Height);
            try { File.Delete(tmp); } catch { }

            _main.HideToBall();
            check("收起到悬浮球", !_main.Visible && _ball != null, _main.Visible ? "仍可见" : "");
            ShowMain();
            check("从悬浮球恢复", _main.Visible, _main.Visible ? "" : "未恢复");

            Console.WriteLine(string.Join(Environment.NewLine, results.ToArray()));
            Console.WriteLine(failed == 0 ? Environment.NewLine + "全部通过" : Environment.NewLine + failed + " 项失败");
            try
            {
                string log = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-result.txt");
                File.WriteAllLines(log, results);
            }
            catch { }
            try { _main.Close(); } catch { }
            if (_tray != null) _tray.Visible = false;
            return failed == 0 ? 0 : 1;
        }
    }
}
