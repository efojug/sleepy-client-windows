using SharpConfig;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Timer = System.Threading.Timer;

namespace sleepy_client_windows
{
    static class Program
    {
        // WinAPI获取最后一次输入时间
        [DllImport("user32.dll")]
        static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        // 获取系统空闲时间（毫秒）
        static long GetIdleTime()
        {
            LASTINPUTINFO lastInputInfo = new();
            lastInputInfo.cbSize = (uint)Marshal.SizeOf(lastInputInfo);
            if (!GetLastInputInfo(ref lastInputInfo))
                return 0;
            return Environment.TickCount - lastInputInfo.dwTime;
        }

        static string server;
        static string secret;
        static int device;

        static readonly HttpClient client = new();

        // 异步发送请求
        static async Task SendPostRequest(string url, int status, string app)
        {
            try
            {
                await client.PostAsync(url, new StringContent(JsonSerializer.Serialize(new { secret, device, status, app }), Encoding.UTF8, "application/json"));
            }
            catch (Exception ex)
            {
                Debug.WriteLine("发送请求失败: " + ex);
            }
        }

        // 获取前台窗口名称
        static string GetForegroundProcessTitle()
        {
            IntPtr hwnd = Win32Api.GetForegroundWindow();
            Win32Api.GetWindowThreadProcessId(hwnd, out uint processId);
            try
            {
                Process proc = Process.GetProcessById((int)processId);
                if (proc.MainWindowTitle == "") return proc.ProcessName;
                return proc.MainWindowTitle;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("获取前台窗口失败: " + ex);
                return "Unknown";
            }
        }

        static NotifyIcon trayIcon;
        static Timer sendTimer;
        static Timer idleTimer;
        static bool isSleepMode = false;
        static string lastForegroundApp = "";

        const int SendInterval = 300000;
        const int IdleInterval = 45000;

        [STAThread]
        static void Main()
        {
            // 避免多实例
            if (Process.GetProcessesByName(Application.ProductName).Length > 1)
            {
                Environment.Exit(-1);
            }

            try
            {
                var section = Configuration.LoadFromFile("config.ini")["Main"];
                server = section["server"].StringValue;
                secret = section["secret"].StringValue;
                device = section["device"].IntValue;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("加载配置失败: " + ex);
                Environment.Exit(-1);
            }

            //Application.EnableVisualStyles();
            //Application.SetCompatibleTextRenderingDefault(false);

            // 创建系统托盘图标
            trayIcon = new NotifyIcon
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath),
                Visible = true,
                Text = "Sleepy"
            };

            // 托盘上传
            ContextMenuStrip menu = new();
            ToolStripMenuItem uploadItem = new("Upload");
            uploadItem.Click += async (s, e) =>
            {
                await SendPostRequest(server, 1, GetForegroundProcessTitle());
                // 提示
                trayIcon.ShowBalloonTip(3000, "状态更新", "上传请求已发送", ToolTipIcon.Info);
            };

            // 托盘退出
            ToolStripMenuItem exitItem = new("Exit");
            exitItem.Click += async (s, e) =>
            {
                await SendPostRequest(server, 0, "");
                trayIcon.Visible = false;
                Application.Exit();
            };
            menu.Items.Add(uploadItem);
            menu.Items.Add(exitItem);
            trayIcon.ContextMenuStrip = menu;

            // 初始化两个计时器（初始均不启动）
            sendTimer = new Timer(PutTimerCallback, null, Timeout.Infinite, Timeout.Infinite);
            idleTimer = new Timer(IdleTimerCallback, null, Timeout.Infinite, Timeout.Infinite);

            // 启动正常模式计时器
            sendTimer.Change(0, SendInterval);

            Application.Run();
        }

        static async void PutTimerCallback(object state)
        {
            // 当空闲超过15分钟，则发送休眠请求，并切换到休眠模式
            if (!isSleepMode && GetIdleTime() >= 15 * 60 * 1000)
            {
                await SendPostRequest(server, 0, "");
                isSleepMode = true;
                // 停止 putTimer，启动 idleTimer
                sendTimer.Change(Timeout.Infinite, Timeout.Infinite);
                idleTimer.Change(0, IdleInterval);
                return;
            }

            string currentApp = GetForegroundProcessTitle();
            lastForegroundApp = currentApp;
            await SendPostRequest(server, 1, currentApp);
        }

        static async void IdleTimerCallback(object state)
        {
            string currentApp = GetForegroundProcessTitle();
            if (GetIdleTime() < IdleInterval || currentApp != lastForegroundApp)
            {
                lastForegroundApp = currentApp;
                await SendPostRequest(server, 1, currentApp);
                isSleepMode = false;
                // 停止 idleTimer，恢复正常模式
                idleTimer.Change(Timeout.Infinite, Timeout.Infinite);
                sendTimer.Change(0, SendInterval);
            }
        }
    }

    // WinAPI 获取前台窗口句柄及进程 ID
    public static class Win32Api
    {
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    }
}
