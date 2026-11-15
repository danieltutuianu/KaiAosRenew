using System;
using System.Windows;
using Microsoft.Win32;

namespace KaiAosNotifyClient
{
    internal static class ClientThemeManager
    {
        public const string AppUserModelId = "BMW.KaiAosRenew.Client";

        private static bool _night;
        private static bool _listening;
        private static Func<string> _getMode;

        public static bool IsNight { get { return _night; } }

        public static void SetModeProvider(Func<string> getMode)
        {
            _getMode = getMode;
        }

        public static string NormalizeMode(string mode)
        {
            if (string.IsNullOrWhiteSpace(mode)) return "system";
            mode = mode.Trim().ToLowerInvariant();
            if (mode == "day" || mode == "night" || mode == "system")
                return mode;
            return "system";
        }

        public static bool IsOsLightTheme()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key == null) return true;
                    var v = key.GetValue("AppsUseLightTheme");
                    if (v is int)
                        return (int)v != 0;
                }
            }
            catch { }
            return true;
        }

        public static void Apply(string mode)
        {
            mode = NormalizeMode(mode);
            var night = mode == "night" || (mode == "system" && !IsOsLightTheme());
            ApplyNight(night);
            EnsureOsListener(mode == "system");
        }

        public static void ApplyNight(bool night)
        {
            _night = night;
            var app = Application.Current;
            if (app == null) return;
            try
            {
                // pack:// is required when cwd is not the app folder (Start Menu / Velopack stub).
                var uri = new Uri(night
                    ? "pack://application:,,,/Themes/KaiTheme.Dark.xaml"
                    : "pack://application:,,,/Themes/KaiTheme.xaml");
                var theme = new ResourceDictionary { Source = uri };
                var dicts = app.Resources.MergedDictionaries;
                if (dicts.Count == 0)
                    dicts.Add(theme);
                else
                    dicts[0] = theme;
            }
            catch
            {
                // Keep App.xaml theme if swap fails — never blank the UI.
            }
        }

        private static void EnsureOsListener(bool enable)
        {
            if (enable && !_listening)
            {
                SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
                _listening = true;
            }
            else if (!enable && _listening)
            {
                SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
                _listening = false;
            }
        }

        private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General &&
                e.Category != UserPreferenceCategory.VisualStyle)
                return;
            var app = Application.Current;
            if (app == null) return;
            app.Dispatcher.BeginInvoke(new Action(() =>
            {
                var mode = _getMode != null ? _getMode() : "system";
                if (NormalizeMode(mode) == "system")
                    ApplyNight(!IsOsLightTheme());
            }));
        }
    }
}
