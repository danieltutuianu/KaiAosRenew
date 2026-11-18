using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Serialization;
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
        /// <summary>When ShowVinTips, show tip only after BMW upload fails (not on every VIN read).</summary>
        public bool ShowVinTipsBmwFailOnly { get; set; }
        public bool AutoCopyVin { get; set; }
        public bool ShowAlertTips { get; set; }
        /// <summary>When checking browser passwords, remind to match app password.</summary>
        public bool BrowserPasswordHintOnAuthFail { get; set; }
        public int OfflineMinutes { get; set; }
        /// <summary>Synced with server AuthRegisterBlockAfter (1–10).</summary>
        public int AuthRegisterBlockAfter { get; set; }
        public string Language { get; set; }
        /// <summary>system | day | night</summary>
        public string Theme { get; set; }
        /// <summary>Force Update UI / debug banner until explicitly turned off.</summary>
        public bool DebugMode { get; set; }
        /// <summary>Newest first, max 3, semicolon-separated in registry.</summary>
        public List<string> RecentHosts { get; private set; }

        public static string StoreDir
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "KaiAosNotifyClient");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string LogDir
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "KaiAosNotifyClient");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

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
            VinTipSeconds = 10;
            ShowVinTips = true;
            ShowVinTipsBmwFailOnly = false;
            AutoCopyVin = false;
            ShowAlertTips = true;
            BrowserPasswordHintOnAuthFail = true;
            OfflineMinutes = 2;
            AuthRegisterBlockAfter = 3;
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
            if (AuthRegisterBlockAfter < 1) AuthRegisterBlockAfter = 1;
            if (AuthRegisterBlockAfter > 10) AuthRegisterBlockAfter = 10;
            if (string.IsNullOrWhiteSpace(Language)) Language = "en";
            Language = Language.Trim().ToLowerInvariant();
            if (Language == "en-gb" || Language == "en-us") Language = "en";
            var langOk = false;
            for (var i = 0; i < SupportedLanguages.Length; i++)
            {
                if (Language == SupportedLanguages[i]) { langOk = true; break; }
            }
            if (!langOk) Language = "en";
            if (!ShowVinTips)
                ShowVinTipsBmwFailOnly = false;
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
                    s.ShowVinTipsBmwFailOnly = ReadBool(key, "ShowVinTipsBmwFailOnly", false);
                    s.AutoCopyVin = ReadBool(key, "AutoCopyVin", false);
                    s.ShowAlertTips = ReadBool(key, "ShowAlertTips", true);
                    s.BrowserPasswordHintOnAuthFail = ReadBool(key, "BrowserPasswordHintOnAuthFail", true);
                    s.DebugMode = ReadBool(key, "DebugMode", false);
                    var host = key.GetValue("Host") as string;
                    var port = key.GetValue("Port") as string;
                    if (!string.IsNullOrWhiteSpace(host)) s.Host = host.Trim();
                    if (!string.IsNullOrWhiteSpace(port)) s.Port = port.Trim();
                    s.VinTipSeconds = ReadInt(key, "VinTipSeconds", 10);
                    s.OfflineMinutes = ReadInt(key, "OfflineMinutes", 2);
                    s.AuthRegisterBlockAfter = ReadInt(key, "AuthRegisterBlockAfter", 3);
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
                    key.SetValue("ShowVinTipsBmwFailOnly", ShowVinTipsBmwFailOnly ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("AutoCopyVin", AutoCopyVin ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("ShowAlertTips", ShowAlertTips ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("BrowserPasswordHintOnAuthFail", BrowserPasswordHintOnAuthFail ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("DebugMode", DebugMode ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("VinTipSeconds", VinTipSeconds, RegistryValueKind.DWord);
                    key.SetValue("OfflineMinutes", OfflineMinutes, RegistryValueKind.DWord);
                    key.SetValue("AuthRegisterBlockAfter", AuthRegisterBlockAfter, RegistryValueKind.DWord);
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

        public void ExportPortable(string path)
        {
            File.WriteAllText(path, ExportPortableToString() ?? "", Encoding.UTF8);
        }

        public string ExportPortableToString()
        {
            var portable = new ClientSettingsPortable
            {
                StartWithWindows = StartWithWindows,
                MinimizeToTray = MinimizeToTray,
                MinimizeOnClose = MinimizeOnClose,
                AutoUpdate = AutoUpdate,
                AutoConnect = AutoConnect,
                Host = Host ?? "",
                Port = Port ?? "17890",
                VinTipSeconds = VinTipSeconds,
                ShowVinTips = ShowVinTips,
                ShowVinTipsBmwFailOnly = ShowVinTipsBmwFailOnly,
                AutoCopyVin = AutoCopyVin,
                ShowAlertTips = ShowAlertTips,
                BrowserPasswordHintOnAuthFail = BrowserPasswordHintOnAuthFail,
                OfflineMinutes = OfflineMinutes,
                AuthRegisterBlockAfter = AuthRegisterBlockAfter,
                Language = Language ?? "en",
                Theme = Theme ?? "system",
                RecentHosts = RecentHosts != null ? string.Join(";", RecentHosts.ToArray()) : ""
            };
            var ser = new XmlSerializer(typeof(ClientSettingsPortable));
            using (var sw = new StringWriter())
            {
                ser.Serialize(sw, portable);
                return sw.ToString();
            }
        }

        public void ImportPortable(string path)
        {
            ImportPortableFromString(File.ReadAllText(path));
        }

        public void ImportPortableFromString(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml))
                throw new InvalidOperationException("Invalid settings file.");
            var ser = new XmlSerializer(typeof(ClientSettingsPortable));
            ClientSettingsPortable imported;
            using (var sr = new StringReader(xml))
                imported = ser.Deserialize(sr) as ClientSettingsPortable;
            if (imported == null)
                throw new InvalidOperationException("Invalid settings file.");

            StartWithWindows = imported.StartWithWindows;
            MinimizeToTray = imported.MinimizeToTray;
            MinimizeOnClose = imported.MinimizeOnClose;
            AutoUpdate = imported.AutoUpdate;
            AutoConnect = imported.AutoConnect;
            if (imported.Host != null) Host = imported.Host.Trim();
            if (!string.IsNullOrWhiteSpace(imported.Port)) Port = imported.Port.Trim();
            VinTipSeconds = imported.VinTipSeconds;
            ShowVinTips = imported.ShowVinTips;
            ShowVinTipsBmwFailOnly = imported.ShowVinTipsBmwFailOnly;
            AutoCopyVin = imported.AutoCopyVin;
            ShowAlertTips = imported.ShowAlertTips;
            BrowserPasswordHintOnAuthFail = imported.BrowserPasswordHintOnAuthFail;
            OfflineMinutes = imported.OfflineMinutes;
            AuthRegisterBlockAfter = imported.AuthRegisterBlockAfter;
            if (!string.IsNullOrWhiteSpace(imported.Language)) Language = imported.Language.Trim();
            if (!string.IsNullOrWhiteSpace(imported.Theme)) Theme = imported.Theme.Trim();
            if (RecentHosts == null) RecentHosts = new List<string>();
            RecentHosts.Clear();
            if (!string.IsNullOrWhiteSpace(imported.RecentHosts))
            {
                foreach (var part in imported.RecentHosts.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var h = part.Trim();
                    if (h.Length > 0 && RecentHosts.Count < 3
                        && !RecentHosts.Exists(x => string.Equals(x, h, StringComparison.OrdinalIgnoreCase)))
                        RecentHosts.Add(h);
                }
            }
            Normalize();
        }
    }

    public sealed class ClientSettingsPortable
    {
        public bool StartWithWindows { get; set; }
        public bool MinimizeToTray { get; set; }
        public bool MinimizeOnClose { get; set; }
        public bool AutoUpdate { get; set; }
        public bool AutoConnect { get; set; }
        public string Host { get; set; }
        public string Port { get; set; }
        public int VinTipSeconds { get; set; }
        public bool ShowVinTips { get; set; }
        public bool ShowVinTipsBmwFailOnly { get; set; }
        public bool AutoCopyVin { get; set; }
        public bool ShowAlertTips { get; set; }
        public bool BrowserPasswordHintOnAuthFail { get; set; }
        public int OfflineMinutes { get; set; }
        public int AuthRegisterBlockAfter { get; set; }
        public string Language { get; set; }
        public string Theme { get; set; }
        public string RecentHosts { get; set; }
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
        public bool HasBin { get; set; }

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

    public sealed class RegisterHistoryRow
    {
        public DateTime At { get; set; }
        /// <summary>0=fail, 1=ok, 2=info.</summary>
        public int Kind { get; set; }
        public bool Ok { get; set; }
        public string Text { get; set; }
        public string AgeText { get; set; }
        public string ResultText { get; set; }

        public string TimeText
        {
            get { return At.ToString("yyyy-MM-dd HH:mm:ss"); }
        }

        internal static RegisterHistoryRow FromWire(RegisterWireRow row)
        {
            if (row == null) return null;
            var kind = row.Kind;
            if (kind < 0 || kind > 2)
                kind = row.Ok ? 1 : 0;
            return new RegisterHistoryRow
            {
                At = row.At,
                Kind = kind,
                Ok = kind == 1,
                Text = row.Text ?? "",
                AgeText = "",
                ResultText = ""
            };
        }
    }
}
