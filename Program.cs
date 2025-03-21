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
        // WinAPI��ȡ���һ������ʱ��
        [DllImport("user32.dll")]
        static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        // ��ȡϵͳ����ʱ�䣨���룩
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

        // ��̬ HttpClient ʵ��
        static readonly HttpClient client = new();

        // �첽��������
        static async Task SendPutRequest(string url, int status, string app)
        {
            try
            {
                await client.PostAsync(url, new StringContent(JsonSerializer.Serialize(new { secret, device, status, app }), Encoding.UTF8, "application/json"));
            }
            catch (Exception ex)
            {
                Debug.WriteLine("��������ʧ��: " + ex);
            }
        }

        // ��ȡ��ǰǰ̨�����������̵�����
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
                Debug.WriteLine("��ȡǰ̨����ʧ��: " + ex);
                return "Unknown";
            }
        }

        static NotifyIcon trayIcon;
        // ʹ�� System.Threading.Timer ��� System.Windows.Forms.Timer
        static Timer putTimer;
        static Timer idleTimer;
        static bool isSleepMode = false;
        static string lastForegroundApp = "";

        // ��ʱ�����ڣ����룩
        const int PutInterval = 300000;   // 5����
        const int IdleInterval = 45000;     // 45��

        [STAThread]
        static void Main()
        {
            // �����ʵ��
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
                Debug.WriteLine("��������ʧ��: " + ex);
                Environment.Exit(-1);
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // ����ϵͳ����ͼ��
            trayIcon = new NotifyIcon
            {
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

            // ��ʼ��������ʱ������ʼ����������
            putTimer = new Timer(PutTimerCallback, null, Timeout.Infinite, Timeout.Infinite);
            idleTimer = new Timer(IdleTimerCallback, null, Timeout.Infinite, Timeout.Infinite);

            // ��������ģʽ��ʱ��
            putTimer.Change(0, PutInterval);

            Application.Run();
        }

        // putTimer �Ļص�������ģʽ��ÿ5����ִ��һ��
        static async void PutTimerCallback(object state)
        {
            // �����г���15���ӣ������������󣬲��л�������ģʽ
            if (!isSleepMode && GetIdleTime() >= 15 * 60 * 1000)
            {
                await SendPutRequest(server, 0, "");
                isSleepMode = true;
                // ֹͣ putTimer������ idleTimer
                putTimer.Change(Timeout.Infinite, Timeout.Infinite);
                idleTimer.Change(0, IdleInterval);
                return;
            }

            string currentApp = GetForegroundProcessTitle();
            lastForegroundApp = currentApp;
            await SendPutRequest(server, 1, currentApp);
        }

        // idleTimer �Ļص�������ģʽ��ÿ45�����Ƿ�ָ��
        static async void IdleTimerCallback(object state)
        {
            string currentApp = GetForegroundProcessTitle();
            if (GetIdleTime() < IdleInterval || currentApp != lastForegroundApp)
            {
                lastForegroundApp = currentApp;
                await SendPutRequest(server, 1, currentApp);
                isSleepMode = false;
                // ֹͣ idleTimer���ָ�����ģʽ
                idleTimer.Change(Timeout.Infinite, Timeout.Infinite);
                putTimer.Change(0, PutInterval);
            }
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
