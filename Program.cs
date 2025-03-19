using System.Runtime.InteropServices;
using System.Diagnostics;
using Timer = System.Windows.Forms.Timer;
using SharpConfig;

namespace sleepy_client_windows
{

    static class Program
    {
        // WinAPI��ȡ���һ������ʱ��
        [DllImport("user32.dll")]
        static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        // ϵͳ����ʱ��
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

        // �첽��������
        static async Task SendPutRequest(string url, int status, string app)
        {
            using HttpClient client = new();
            try
            {
                await client.PutAsync(url + $"?secret={secret}&device={device}&status={status}&app={app}", null);
            }
            catch (Exception ex)
            {
                MessageBox.Show("��������ʧ��: \n" + ex);
            }
        }

        // ��ȡ��ǰǰ̨�����������̵�����
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
                MessageBox.Show("��ȡǰ̨����ʧ��: \n" + ex);
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

            // ����ϵͳ����ͼ��
            trayIcon = new NotifyIcon
            {
                // ����ͼ��
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath),
                Visible = true,
                Text = "Sleepy"
            };

            // �Ҽ��˵��˳�
            ContextMenuStrip menu = new();
            ToolStripMenuItem exitItem = new("Exit");
            exitItem.Click += (s, e) =>
            {
                trayIcon.Visible = false;
                Application.Exit();
            };
            menu.Items.Add(exitItem);
            trayIcon.ContextMenuStrip = menu;

            //����ģʽ��ʱ�� ÿ45�����Ƿ�ָ��
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

            // ����ģʽ��ÿ5���ӻ�ȡǰ̨Ӧ�ò�����
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

    // ���� WinAPI ��ȡǰ̨���ھ�������� ID
    public static class Win32Api
    {
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    }
}
