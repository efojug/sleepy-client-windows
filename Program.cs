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

        // 静态 HttpClient 实例
        static readonly HttpClient client = new();

        // 异步发送请求
        static async Task SendPutRequest(string url, int status, string app)
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

        // 获取当前前台窗口所属进程的名称
        static string GetForegroundProcessTitle()
        {
            IntPtr hwnd = Win32Api.GetForegroundWindow();
            Win32Api.GetWindowThreadProcessId(hwnd, out uint processId);
            try
            {
                Process proc = Process.GetProcessById((int)processId);
                return proc.MainWindowTitle;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("获取前台窗口失败: " + ex);
                return "Unknown";
            }
        }

        static NotifyIcon trayIcon;
        // 使用 System.Threading.Timer 替代 System.Windows.Forms.Timer
        static Timer putTimer;
        static Timer idleTimer;
        static bool isSleepMode = false;
        static string lastForegroundApp = "";

        // 定时器周期（毫秒）
        const int PutInterval = 300000;   // 5分钟
        const int IdleInterval = 45000;     // 45秒

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

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 创建系统托盘图标
            trayIcon = new NotifyIcon
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath),
                Visible = true,
                Text = "Sleepy"
            };

            // 右键菜单退出
            ContextMenuStrip menu = new();
            ToolStripMenuItem exitItem = new("Exit");
            exitItem.Click += (s, e) =>
            {
                trayIcon.Visible = false;
                Application.Exit();
            };
            menu.Items.Add(exitItem);
            trayIcon.ContextMenuStrip = menu;

            // 初始化两个计时器（初始均不启动）
            putTimer = new Timer(PutTimerCallback, null, Timeout.Infinite, Timeout.Infinite);
            idleTimer = new Timer(IdleTimerCallback, null, Timeout.Infinite, Timeout.Infinite);

            // 启动正常模式计时器
            putTimer.Change(0, PutInterval);

            Application.Run();
        }

        // putTimer 的回调：正常模式下每5分钟执行一次
        static async void PutTimerCallback(object state)
        {
            // 当空闲超过15分钟，则发送休眠请求，并切换到休眠模式
            if (!isSleepMode && GetIdleTime() >= 15 * 60 * 1000)
            {
                await SendPutRequest(server, 0, "");
                isSleepMode = true;
                // 停止 putTimer，启动 idleTimer
                putTimer.Change(Timeout.Infinite, Timeout.Infinite);
                idleTimer.Change(0, IdleInterval);
                return;
            }

            string currentApp = GetForegroundProcessTitle();
            lastForegroundApp = currentApp;
            await SendPutRequest(server, 1, currentApp);
        }

        // idleTimer 的回调：休眠模式下每45秒检测是否恢复活动
        static async void IdleTimerCallback(object state)
        {
            string currentApp = GetForegroundProcessTitle();
            if (GetIdleTime() < IdleInterval || currentApp != lastForegroundApp)
            {
                lastForegroundApp = currentApp;
                await SendPutRequest(server, 1, currentApp);
                isSleepMode = false;
                // 停止 idleTimer，恢复正常模式
                idleTimer.Change(Timeout.Infinite, Timeout.Infinite);
                putTimer.Change(0, PutInterval);
            }
        }
    }

    // 调用 WinAPI 获取前台窗口句柄及进程 ID
    public static class Win32Api
    {
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    }
}
