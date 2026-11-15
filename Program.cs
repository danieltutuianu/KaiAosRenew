using System;
using System.Threading;
using System.Windows;

namespace KaiAosRenew
{
    internal static class Program
    {
        public const int WmShowMe = 0x0400 + 77; // WM_USER + 77
        private static Mutex _mutex;

        [STAThread]
        private static void Main(string[] args)
        {
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
            }

            bool created;
            _mutex = new Mutex(true, @"Local\KaiAosRenew.SingleInstance", out created);
            if (!created)
            {
                if (NativeMethods.TryActivateOtherInstance())
                    return;

                // Zombie / white-square: kill stuck instance and take ownership.
                NativeMethods.TerminateOtherKaiAosRenewProcesses();
                try
                {
                    if (_mutex != null)
                    {
                        _mutex.Dispose();
                        _mutex = null;
                    }
                }
                catch { }

                // Brief wait for OS to release abandoned mutex name.
                Thread.Sleep(400);
                _mutex = new Mutex(true, @"Local\KaiAosRenew.SingleInstance", out created);
                if (!created)
                {
                    // Still blocked — give up activating; avoid two UIs fighting.
                    try
                    {
                        if (NativeMethods.TryActivateOtherInstance())
                            return;
                    }
                    catch { }
                    return;
                }
            }

            try
            {
                NativeMethods.SetAppUserModelId(ThemeManager.AppUserModelId);
                NativeMethods.EnsureTrayNotificationsEnabled();
                StartMenuShortcut.Ensure();
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
    }
}
