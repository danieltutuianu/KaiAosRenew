using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace KaiAosNotifyClient
{
    /// <summary>Persisted client options (HKCU\Software\KaiAosNotifyClient).</summary>
    public sealed class ClientSettings
    {
        public const string RegPath = @"Software\KaiAosNotifyClient";
        public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string RunValueName = "KaiAosNotifyClient";

        public bool StartWithWindows { get; set; }
        public bool MinimizeToTray { get; set; }
        public bool MinimizeOnClose { get; set; }
        /// <summary>Check/download/apply Velopack updates at startup without prompting.</summary>
        public bool AutoUpdate { get; set; }
        public bool AutoConnect { get; set; }
        public string Host { get; set; }
        public string Port { get; set; }
        public int VinTipSeconds { get; set; }
        public bool ShowVinTips { get; set; }
        public bool AutoCopyVin { get; set; }
        public bool ShowAlertTips { get; set; }
        public int OfflineMinutes { get; set; }
        public string Language { get; set; }
        /// <summary>system | day | night</summary>
        public string Theme { get; set; }
        /// <summary>Newest first, max 3, semicolon-separated in registry.</summary>
        public List<string> RecentHosts { get; private set; }

        private static readonly string[] SupportedLanguages = new[]
        {
            "en", "ro", "de", "fr", "es", "it", "da", "cs", "nl", "pl",
            "pt", "sv", "no", "ru", "tr", "ja", "ko", "zh", "th"
        };

        public ClientSettings()
        {
            MinimizeOnClose = true;
            AutoUpdate = true;
            AutoConnect = true;
            Host = "";
            Port = "17890";
            VinTipSeconds = 8;
            ShowVinTips = true;
            AutoCopyVin = false;
            ShowAlertTips = true;
            OfflineMinutes = 2;
            Language = "en";
            Theme = "system";
            RecentHosts = new List<string>();
        }

        public void Normalize()
        {
            if (Host == null) Host = "";
            Host = Host.Trim();
            if (string.IsNullOrWhiteSpace(Port)) Port = "17890";
            if (VinTipSeconds < 3) VinTipSeconds = 3;
            if (VinTipSeconds > 60) VinTipSeconds = 60;
            if (OfflineMinutes < 1) OfflineMinutes = 1;
            if (OfflineMinutes > 30) OfflineMinutes = 30;
            if (string.IsNullOrWhiteSpace(Language)) Language = "en";
            Language = Language.Trim().ToLowerInvariant();
            if (Language == "en-gb" || Language == "en-us") Language = "en";
            var langOk = false;
            for (var i = 0; i < SupportedLanguages.Length; i++)
            {
                if (Language == SupportedLanguages[i]) { langOk = true; break; }
            }
            if (!langOk) Language = "en";
            Theme = ClientThemeManager.NormalizeMode(Theme);
            if (RecentHosts == null) RecentHosts = new List<string>();
            while (RecentHosts.Count > 3)
                RecentHosts.RemoveAt(RecentHosts.Count - 1);
        }

        public void PushRecentHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host)) return;
            host = host.Trim();
            if (RecentHosts == null) RecentHosts = new List<string>();
            for (var i = RecentHosts.Count - 1; i >= 0; i--)
            {
                if (string.Equals(RecentHosts[i], host, StringComparison.OrdinalIgnoreCase))
                    RecentHosts.RemoveAt(i);
            }
            RecentHosts.Insert(0, host);
            while (RecentHosts.Count > 3)
                RecentHosts.RemoveAt(RecentHosts.Count - 1);
        }

        public static ClientSettings Load()
        {
            var s = new ClientSettings();
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegPath, false))
                {
                    if (key == null) return s;
                    s.StartWithWindows = ReadBool(key, "StartWithWindows", false);
                    s.MinimizeToTray = ReadBool(key, "MinimizeToTray", false);
                    s.MinimizeOnClose = ReadBool(key, "MinimizeOnClose", true);
                    s.AutoUpdate = ReadBool(key, "AutoUpdate", true);
                    s.AutoConnect = ReadBool(key, "AutoConnect", true);
                    s.ShowVinTips = ReadBool(key, "ShowVinTips", true);
                    s.AutoCopyVin = ReadBool(key, "AutoCopyVin", false);
                    s.ShowAlertTips = ReadBool(key, "ShowAlertTips", true);
                    var host = key.GetValue("Host") as string;
                    var port = key.GetValue("Port") as string;
                    if (!string.IsNullOrWhiteSpace(host)) s.Host = host.Trim();
                    if (!string.IsNullOrWhiteSpace(port)) s.Port = port.Trim();
                    s.VinTipSeconds = ReadInt(key, "VinTipSeconds", 8);
                    s.OfflineMinutes = ReadInt(key, "OfflineMinutes", 2);
                    var lang = key.GetValue("Language") as string;
                    if (!string.IsNullOrWhiteSpace(lang)) s.Language = lang.Trim();
                    var theme = key.GetValue("Theme") as string;
                    if (!string.IsNullOrWhiteSpace(theme)) s.Theme = theme.Trim();
                    var recent = key.GetValue("RecentHosts") as string;
                    if (!string.IsNullOrWhiteSpace(recent))
                    {
                        foreach (var part in recent.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var h = part.Trim();
                            if (h.Length > 0 && s.RecentHosts.Count < 3
                                && !s.RecentHosts.Exists(x => string.Equals(x, h, StringComparison.OrdinalIgnoreCase)))
                                s.RecentHosts.Add(h);
                        }
                    }
                }
            }
            catch { }
            s.Normalize();
            return s;
        }

        public void Save()
        {
            Normalize();
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RegPath))
                {
                    if (key == null) return;
                    key.SetValue("StartWithWindows", StartWithWindows ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("MinimizeToTray", MinimizeToTray ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("MinimizeOnClose", MinimizeOnClose ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("AutoUpdate", AutoUpdate ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("AutoConnect", AutoConnect ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("ShowVinTips", ShowVinTips ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("AutoCopyVin", AutoCopyVin ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("ShowAlertTips", ShowAlertTips ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("VinTipSeconds", VinTipSeconds, RegistryValueKind.DWord);
                    key.SetValue("OfflineMinutes", OfflineMinutes, RegistryValueKind.DWord);
                    key.SetValue("Host", Host ?? "", RegistryValueKind.String);
                    key.SetValue("Port", Port ?? "17890", RegistryValueKind.String);
                    key.SetValue("Language", Language ?? "en", RegistryValueKind.String);
                    key.SetValue("Theme", Theme ?? "system", RegistryValueKind.String);
                    key.SetValue("RecentHosts", string.Join(";", RecentHosts.ToArray()), RegistryValueKind.String);
                }
            }
            catch { }
        }

        private static bool ReadBool(RegistryKey key, string name, bool defaultValue)
        {
            try
            {
                var v = key.GetValue(name);
                if (v == null) return defaultValue;
                if (v is int) return ((int)v) != 0;
                if (v is long) return ((long)v) != 0;
                int n;
                if (int.TryParse(v.ToString(), out n)) return n != 0;
                var s = v.ToString();
                if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase)) return false;
            }
            catch { }
            return defaultValue;
        }

        private static int ReadInt(RegistryKey key, string name, int defaultValue)
        {
            try
            {
                var v = key.GetValue(name);
                if (v == null) return defaultValue;
                if (v is int) return (int)v;
                int n;
                if (int.TryParse(v.ToString(), out n)) return n;
            }
            catch { }
            return defaultValue;
        }
    }

    public sealed class ClientKeyRow
    {
        public DateTime DetectedLocal { get; set; }
        public string Vin { get; set; }
        public string Paint { get; set; }
        public string Upholstery { get; set; }
        public string ILevelCurrent { get; set; }
        public string ReaderId { get; set; }
        public bool LocalSent { get; set; }
        public bool BmwUploaded { get; set; }
        public bool Failed { get; set; }
        public string Status { get; set; }
        public string Detail { get; set; }

        public string TimeText { get { return DetectedLocal.ToString("g"); } }
        public string VinText { get { return string.IsNullOrEmpty(Vin) ? "—" : Vin; } }
        public string PaintText { get { return string.IsNullOrEmpty(Paint) ? "—" : Paint; } }
        public string UpholsteryText { get { return string.IsNullOrEmpty(Upholstery) ? "—" : Upholstery; } }
        public string ILevelText { get { return string.IsNullOrEmpty(ILevelCurrent) ? "—" : ILevelCurrent; } }
        public string LocalText
        {
            get
            {
                if (Failed && !LocalSent) return "FAIL";
                return LocalSent ? "OK" : "—";
            }
        }
        public string BmwText
        {
            get
            {
                if (BmwUploaded) return "OK";
                if (Failed) return "FAIL";
                if (LocalSent) return "…";
                return "—";
            }
        }
        public string StatusText
        {
            get
            {
                switch (Status ?? "")
                {
                    case "Waiting": return "Waiting";
                    case "LocalOk": return "Local OK";
                    case "BmwOk": return "OK";
                    case "Failed": return "Failed";
                    default: return string.IsNullOrEmpty(Status) ? "—" : Status;
                }
            }
        }
        public string DetailText
        {
            get
            {
                if (string.IsNullOrEmpty(Detail)) return "—";
                return Detail;
            }
        }
        public string RowToolTip
        {
            get { return string.IsNullOrEmpty(Vin) ? "VIN: —" : ("VIN: " + Vin); }
        }
    }

    public sealed class ActivityRow
    {
        public DateTime At { get; set; }
        public string Level { get; set; }
        public string Text { get; set; }

        public string TimeText
        {
            get { return At.ToString("yyyy-MM-dd HH:mm:ss"); }
        }

        public string LevelText
        {
            get
            {
                var l = (Level ?? "info").Trim().ToLowerInvariant();
                if (l.Length == 0) return "info";
                return l;
            }
        }

        internal static ActivityRow FromWire(ActivityWireRow row)
        {
            if (row == null) return null;
            return new ActivityRow
            {
                At = row.At,
                Level = string.IsNullOrEmpty(row.Level) ? "info" : row.Level.Trim().ToLowerInvariant(),
                Text = row.Text ?? ""
            };
        }
    }
}
