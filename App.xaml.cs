using System;
using System.Windows;

namespace KaiAosRenew
{
    public partial class App : Application
    {
        public static bool StartMinimized { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (e.Args != null)
            {
                for (var i = 0; i < e.Args.Length; i++)
                {
                    if (string.Equals(e.Args[i], "--minimized", StringComparison.OrdinalIgnoreCase))
                        StartMinimized = true;
                }
            }

            var window = new MainWindow();
            MainWindow = window;
            var hide = StartMinimized || window.PreferStartHidden;
            if (hide)
            {
                window.MarkStartedHiddenToTray();
                window.ShowActivated = false;
                window.ShowInTaskbar = false;
                window.WindowState = WindowState.Minimized;
                window.Visibility = Visibility.Hidden;
                window.Show(); // HWND for tray / single-instance; no visible flash
            }
            else
            {
                window.Show();
            }
        }
    }
}
