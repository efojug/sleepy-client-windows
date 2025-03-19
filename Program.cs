using System.Runtime.InteropServices;
using System.Diagnostics;
using Timer = System.Windows.Forms.Timer;
using SharpConfig;

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

        // 系统空闲时间
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

        // 异步发送请求
        static async Task SendPutRequest(string url, int status, string app)
        {
            using HttpClient client = new();
            try
            {
                await client.PutAsync(url + $"?secret={secret}&device={device}&status={status}&app={app}", null);
            }
            catch (Exception ex)
            {
                MessageBox.Show("发送请求失败: \n" + ex);
            }
        }

        // 获取当前前台窗口所属进程的名称
        static string GetForegroundProcessTitle()
        {
            IntPtr hwnd = Win32Api.GetForegroundWindow();
            uint processId;
            Win32Api.GetWindowThreadProcessId(hwnd, out processId);
            try
            {
                Process proc = Process.GetProcessById((int)processId);
                return proc.MainWindowTitle;
            }
            catch (Exception ex)
            {
                MessageBox.Show("获取前台窗口失败: \n" + ex);
                return "Unknown";
            }
        }

        static NotifyIcon trayIcon;
        static Timer putTimer;
        static Timer idleTimer;
        static bool isSleepMode = false;
        static string lastForegroundApp = "";
        

        [STAThread]
        static void Main()
        {
            if (Process.GetProcessesByName(Application.ProductName).Length > 1)
            {
                MessageBox.Show("program already running.");
                Environment.Exit(-1);
            }
            try
            {
                var section = Configuration.LoadFromFile("config.ini")["Main"];
                server = section["server"].StringValue;
                secret = section["secret"].StringValue;
                device = section["device"].IntValue;
            } catch (Exception ex)
            {
                MessageBox.Show("load config failed! error: \n" + ex);
                Environment.Exit(-1);
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 创建系统托盘图标
            trayIcon = new NotifyIcon
            {
                // 加载图标
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

            //休眠模式计时器 每45秒检测是否恢复活动
            idleTimer = new Timer();
            idleTimer.Interval = 45000;
            idleTimer.Tick += async (s, e) =>
            {
                string currentApp = GetForegroundProcessTitle();
                if (GetIdleTime() < 45000 || currentApp != lastForegroundApp)
                {
                    lastForegroundApp = currentApp;
                    await SendPutRequest(server, 1, currentApp);
                    isSleepMode = false;
                    putTimer.Start();
                    idleTimer.Stop();
                }
            };

            // 正常模式：每5分钟获取前台应用并发送
            putTimer = new Timer();
            putTimer.Interval = 300000;
            putTimer.Tick += async (s, e) =>
            {
                if (!isSleepMode && GetIdleTime() >= 15 * 60 * 1000)
                {
                    await SendPutRequest(server, 0, "");
                    isSleepMode = true;
                    putTimer.Stop();
                    return;
                }

                string currentApp = GetForegroundProcessTitle();
                lastForegroundApp = currentApp;
                await SendPutRequest(server, 1, currentApp);
            };
            putTimer.Start();

            Application.Run();
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
