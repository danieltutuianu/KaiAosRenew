using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Velopack;

namespace KaiAosNotifyClient
{
    internal static class Program
    {
        public const int WmShowMe = 0x0400 + 77;
        private const string MutexName = @"Local\KaiAosNotifyClient.SingleInstance";
        private const string WindowTitle = "KAI - AOS Renew - Client";
        private static Mutex _mutex;

        private static void StartupLog(string line)
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "KaiAosNotifyClient");
                Directory.CreateDirectory(dir);
                File.AppendAllText(
                    Path.Combine(dir, "startup.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + line + Environment.NewLine);
            }
            catch { }
        }

        [STAThread]
        private static void Main(string[] args)
        {
            // Blank client area + visible title bar is classic WPF+GPU / remote-desktop failure.
            try { RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly; }
            catch { }
            StartupLog("Main begin renderMode=SoftwareOnly");

            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                StartupLog("BaseDirectory=" + baseDir);
                if (!string.IsNullOrEmpty(baseDir))
                    Directory.SetCurrentDirectory(baseDir);
            }
            catch (Exception ex) { StartupLog("cwd fail: " + ex.Message); }

            try { VelopackApp.Build().Run(); StartupLog("VelopackApp.Run done"); }
            catch (Exception ex) { StartupLog("VelopackApp.Run: " + ex.Message); }

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
                try { SetCurrentProcessExplicitAppUserModelID(ClientThemeManager.AppUserModelId); }
                catch { }
                // Shortcuts off the UI thread — COM can hang on some desktops.
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { KaiAosRenew.StartMenuShortcut.Ensure("KAI - AOS Renew - Client", ClientThemeManager.AppUserModelId); }
                    catch { }
                });
                var app = new App();
                app.DispatcherUnhandledException += App_DispatcherUnhandledException;
                AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
                StartupLog("App InitializeComponent");
                app.InitializeComponent();
                StartupLog("App.Run");
                app.Run();
                StartupLog("App.Run returned");
            }
            catch (Exception ex)
            {
                StartupLog("FATAL: " + ex);
                try
                {
                    MessageBox.Show(
                        "Startup failed:\n" + ex,
                        WindowTitle,
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                catch { }
                try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "KaiAosNotifyClient-startup-error.txt"), ex.ToString()); }
                catch { }
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

        private static void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                File.WriteAllText(
                    Path.Combine(Path.GetTempPath(), "KaiAosNotifyClient-ui-error.txt"),
                    e.Exception != null ? e.Exception.ToString() : "unknown");
            }
            catch { }
            try
            {
                MessageBox.Show(
                    "Unexpected error:\n" + (e.Exception != null ? e.Exception.Message : ""),
                    WindowTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch { }
            e.Handled = true;
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                File.WriteAllText(
                    Path.Combine(Path.GetTempPath(), "KaiAosNotifyClient-crash.txt"),
                    e.ExceptionObject != null ? e.ExceptionObject.ToString() : "unknown");
            }
            catch { }
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

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SetCurrentProcessExplicitAppUserModelID(string appID);

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
