using System;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace KaiAosNotifyClient
{
    public partial class App : Application
    {
        public static bool StartMinimized { get; private set; }

        private static void Log(string line)
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

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            try { RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly; }
            catch { }
            Log("OnStartup");

            if (e.Args != null)
            {
                for (var i = 0; i < e.Args.Length; i++)
                {
                    if (string.Equals(e.Args[i], "--minimized", StringComparison.OrdinalIgnoreCase))
                        StartMinimized = true;
                }
            }

            MainWindow window;
            try
            {
                Log("creating MainWindow");
                window = new MainWindow();
                Log("MainWindow created");
            }
            catch (Exception ex)
            {
                Log("MainWindow ctor FAIL: " + ex);
                MessageBox.Show("UI failed to start:\n" + ex.Message, "KAI - AOS Renew - Client",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            MainWindow = window;
            var hide = StartMinimized || window.PreferStartHidden;
            Log("Show hide=" + hide + " preferHidden=" + window.PreferStartHidden);
            if (hide)
            {
                window.MarkStartedHiddenToTray();
                window.ShowActivated = false;
                window.ShowInTaskbar = false;
                window.WindowState = WindowState.Minimized;
                window.Visibility = Visibility.Hidden;
                window.Show();
            }
            else
            {
                window.Show();
                window.Activate();
            }
            Log("Show done");
        }
    }
}
