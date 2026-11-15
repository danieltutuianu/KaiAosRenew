using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;

namespace KaiAosNotifyClient
{
    internal static class Program
    {
        public const int WmShowMe = 0x0400 + 77;
        private const string MutexName = @"Local\KaiAosNotifyClient.SingleInstance";
        private const string WindowTitle = "KaiAos Notify Client";
        private static Mutex _mutex;

        [STAThread]
        private static void Main(string[] args)
        {
            bool created;
            _mutex = new Mutex(true, MutexName, out created);
            if (!created)
            {
                if (TryActivateOtherInstance())
                    return;

                // Stuck instance: kill other processes and retry once.
                TerminateOtherInstances();
                try
                {
                    if (_mutex != null)
                    {
                        _mutex.Dispose();
                        _mutex = null;
                    }
                }
                catch { }
                Thread.Sleep(400);
                _mutex = new Mutex(true, MutexName, out created);
                if (!created)
                {
                    TryActivateOtherInstance();
                    return;
                }
            }

            try
            {
                var app = new App();
                app.InitializeComponent();
                app.Run();
            }
            finally
            {
                if (_mutex != null)
                {
                    try { _mutex.ReleaseMutex(); }
                    catch { }
                    try { _mutex.Dispose(); }
                    catch { }
                    _mutex = null;
                }
            }
        }

        private static bool TryActivateOtherInstance()
        {
            try
            {
                var h = FindWindow(null, WindowTitle);
                if (h == IntPtr.Zero || !IsWindow(h))
                    h = FindOtherProcessMainWindow();
                if (h == IntPtr.Zero || !IsWindow(h))
                    return false;

                IntPtr result;
                SendMessageTimeout(h, WmShowMe, IntPtr.Zero, IntPtr.Zero, 0x0002 | 0x0001, 1500, out result);
                ShowWindow(h, 9); // SW_RESTORE
                SetForegroundWindow(h);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static IntPtr FindOtherProcessMainWindow()
        {
            try
            {
                var current = Process.GetCurrentProcess();
                foreach (var p in Process.GetProcessesByName(current.ProcessName))
                {
                    try
                    {
                        if (p.Id == current.Id) continue;
                        var h = p.MainWindowHandle;
                        if (h != IntPtr.Zero && IsWindow(h))
                            return h;
                    }
                    catch { }
                    finally
                    {
                        try { p.Dispose(); }
                        catch { }
                    }
                }
            }
            catch { }
            return IntPtr.Zero;
        }

        private static void TerminateOtherInstances()
        {
            try
            {
                var current = Process.GetCurrentProcess();
                foreach (var p in Process.GetProcessesByName(current.ProcessName))
                {
                    try
                    {
                        if (p.Id == current.Id) continue;
                        p.Kill();
                        p.WaitForExit(3000);
                    }
                    catch { }
                    finally
                    {
                        try { p.Dispose(); }
                        catch { }
                    }
                }
            }
            catch { }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessageTimeout(
            IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam,
            int fuFlags, int uTimeout, out IntPtr lpdwResult);
    }
}
