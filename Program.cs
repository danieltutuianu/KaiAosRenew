using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Velopack;

namespace KaiAosRenew
{
    internal static class Program
    {
        public const int WmShowMe = 0x0400 + 77; // WM_USER + 77
        public const string ShowWindowEventName = @"Local\KaiAosRenew.ShowWindow";
        private static Mutex _mutex;
        private static EventWaitHandle _showWindowEvent;

        /// <summary>Owned by the running UI instance; second launches Set() this to restore from tray.</summary>
        public static EventWaitHandle ShowWindowEvent { get { return _showWindowEvent; } }

        /// <summary>Release mutex/event before Velopack ApplyUpdatesAndRestart so the new process can start.</summary>
        internal static void ReleaseSingleInstanceForRestart()
        {
            if (_showWindowEvent != null)
            {
                try { _showWindowEvent.Dispose(); }
                catch { }
                _showWindowEvent = null;
            }
            if (_mutex != null)
            {
                try { _mutex.ReleaseMutex(); }
                catch { }
                try { _mutex.Dispose(); }
                catch { }
                _mutex = null;
            }
        }

        [STAThread]
        private static void Main(string[] args)
        {
            try { RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly; }
            catch { }

            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                if (!string.IsNullOrEmpty(baseDir))
                    Directory.SetCurrentDirectory(baseDir);
            }
            catch { }

            try { VelopackApp.Build().Run(); }
            catch { }

            for (var i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--once", StringComparison.OrdinalIgnoreCase))
                {
                    var settings = AppSettings.Load();
                    var result = RenewEngine.Run(settings);
                    Console.WriteLine(result.Ok ? "OK" : "FAIL");
                    Console.WriteLine(result.Message);
                    Environment.Exit(result.Ok ? 0 : 1);
                    return;
                }
                if (string.Equals(args[i], ServiceMonitor.ArgFix, StringComparison.OrdinalIgnoreCase))
                {
                    var fix = ServiceMonitor.TryFixAll();
                    Console.WriteLine(fix.Ok ? "OK" : "FAIL");
                    Console.WriteLine(fix.Message);
                    Environment.Exit(fix.Ok ? 0 : 1);
                    return;
                }
                if (string.Equals(args[i], ServiceMonitor.ArgStop, StringComparison.OrdinalIgnoreCase))
                {
                    var stop = ServiceMonitor.TryStopAll();
                    Console.WriteLine(stop.Ok ? "OK" : "FAIL");
                    Console.WriteLine(stop.Message);
                    Environment.Exit(stop.Ok ? 0 : 1);
                    return;
                }
                if (string.Equals(args[i], ServiceMonitor.ArgRestart, StringComparison.OrdinalIgnoreCase))
                {
                    var restart = ServiceMonitor.TryRestartAll();
                    Console.WriteLine(restart.Ok ? "OK" : "FAIL");
                    Console.WriteLine(restart.Message);
                    Environment.Exit(restart.Ok ? 0 : 1);
                    return;
                }
                if (string.Equals(args[i], NotifyFirewallHelper.ArgAllow, StringComparison.OrdinalIgnoreCase))
                {
                    var port = NotifyServer.DefaultPort;
                    if (i + 1 < args.Length)
                    {
                        int parsed;
                        if (int.TryParse(args[i + 1], out parsed) && parsed >= 1024 && parsed <= 65535)
                            port = parsed;
                    }
                    var fw = NotifyFirewallHelper.AllowPort(port);
                    Console.WriteLine(fw.Ok ? "OK" : "FAIL");
                    Console.WriteLine(fw.Message ?? "");
                    Environment.Exit(fw.Ok ? 0 : 1);
                    return;
                }
            }

            const string mutexName = @"Local\KaiAosRenew.SingleInstance";
            bool created;
            _mutex = new Mutex(true, mutexName, out created);
            if (!created)
            {
                // Pin/taskbar second launch: ask the tray instance to show.
                NativeMethods.TryActivateOtherInstance();
                // After Velopack restart the old process may still hold the mutex briefly.
                for (var i = 0; i < 4 && !created; i++)
                {
                    Thread.Sleep(300);
                    try
                    {
                        if (_mutex != null)
                        {
                            try { _mutex.Dispose(); }
                            catch { }
                            _mutex = null;
                        }
                    }
                    catch { }
                    _mutex = new Mutex(true, mutexName, out created);
                }
                if (!created)
                {
                    NativeMethods.TryActivateOtherInstance();
                    return;
                }
            }

            try
            {
                bool eventCreated;
                _showWindowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowEventName, out eventCreated);
            }
            catch
            {
                try { _showWindowEvent = new EventWaitHandle(false, EventResetMode.AutoReset); }
                catch { _showWindowEvent = null; }
            }

            try
            {
                ThemeManager.Configure(ThemeManager.AppUserModelIdServer,
                    () => AppSettings.Load().ThemeMode);
                NativeMethods.SetAppUserModelId(ThemeManager.AppUserModelId);
                NativeMethods.EnsureTrayNotificationsEnabled();
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { StartMenuShortcut.Ensure("KAI - AOS Renew", ThemeManager.AppUserModelId); }
                    catch { }
                });
                var app = new App();
                app.InitializeComponent();
                app.Run();
            }
            finally
            {
                if (_showWindowEvent != null)
                {
                    try { _showWindowEvent.Dispose(); }
                    catch { }
                    _showWindowEvent = null;
                }
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
    }
}
