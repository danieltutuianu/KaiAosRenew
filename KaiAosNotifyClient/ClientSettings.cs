using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Serialization;
using Microsoft.Win32;
using KaiAosRenew;

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
        /// <summary>Optional LAN pairing code matching the server NotifyPairingCode.</summary>
        public string PairingCode { get; set; }
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
        /// <summary>Stable client instance id for notify ghost replace (survives reconnect).</summary>
        public string InstanceGuid { get; set; }
        /// <summary>Newest first, max 3, semicolon-separated in registry.</summary>
        public List<string> RecentHosts { get; private set; }
        /// <summary>Saved host IPs with optional alias (registry "FavoriteHosts"), shown first in the host list.</summary>
        public List<HostFavorite> FavoriteHosts { get; private set; }

        public const int MaxFavoriteHosts = 20;
        public const int MaxFavoriteAliasLength = 40;

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
            PairingCode = "";
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
            InstanceGuid = "";
            RecentHosts = new List<string>();
            FavoriteHosts = new List<HostFavorite>();
        }

        public void Normalize()
        {
            if (Host == null) Host = "";
            EnsureInstanceGuid();
            Host = Host.Trim();
            if (string.IsNullOrWhiteSpace(Port)) Port = "17890";
            if (PairingCode == null) PairingCode = "";
            PairingCode = PairingCode.Trim();
            if (VinTipSeconds != 0)
            {
                if (VinTipSeconds < 3) VinTipSeconds = 3;
                if (VinTipSeconds > 60) VinTipSeconds = 60;
            }
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
            Theme = ThemeManager.NormalizeMode(Theme);
            if (RecentHosts == null) RecentHosts = new List<string>();
            while (RecentHosts.Count > 3)
                RecentHosts.RemoveAt(RecentHosts.Count - 1);
            if (FavoriteHosts == null) FavoriteHosts = new List<HostFavorite>();
            while (FavoriteHosts.Count > MaxFavoriteHosts)
                FavoriteHosts.RemoveAt(FavoriteHosts.Count - 1);
        }

        // ---- favorite hosts ----------------------------------------------------------------------

        public static string SanitizeFavoriteAlias(string alias)
        {
            if (string.IsNullOrEmpty(alias)) return "";
            var sb = new StringBuilder();
            foreach (var ch in alias)
            {
                if (ch == ';' || ch == '|' || char.IsControl(ch)) continue;
                sb.Append(ch);
            }
            var s = sb.ToString().Trim();
            if (s.Length > MaxFavoriteAliasLength) s = s.Substring(0, MaxFavoriteAliasLength).Trim();
            return s;
        }

        public HostFavorite FindFavoriteHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host) || FavoriteHosts == null) return null;
            host = host.Trim();
            foreach (var f in FavoriteHosts)
            {
                if (f != null && string.Equals(f.Host, host, StringComparison.OrdinalIgnoreCase))
                    return f;
            }
            return null;
        }

        /// <summary>Add or update a favorite (alias optional). Newest/updated entries move to the top.</summary>
        public bool SetFavoriteHost(string host, string alias)
        {
            if (string.IsNullOrWhiteSpace(host)) return false;
            host = host.Trim();
            if (host.IndexOfAny(new[] { ';', '|' }) >= 0) return false;
            if (FavoriteHosts == null) FavoriteHosts = new List<HostFavorite>();
            RemoveFavoriteHost(host);
            FavoriteHosts.Insert(0, new HostFavorite { Host = host, Alias = SanitizeFavoriteAlias(alias) });
            while (FavoriteHosts.Count > MaxFavoriteHosts)
                FavoriteHosts.RemoveAt(FavoriteHosts.Count - 1);
            return true;
        }

        public bool RemoveFavoriteHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host) || FavoriteHosts == null) return false;
            host = host.Trim();
            var removed = false;
            for (var i = FavoriteHosts.Count - 1; i >= 0; i--)
            {
                if (FavoriteHosts[i] != null
                    && string.Equals(FavoriteHosts[i].Host, host, StringComparison.OrdinalIgnoreCase))
                {
                    FavoriteHosts.RemoveAt(i);
                    removed = true;
                }
            }
            return removed;
        }

        /// <summary>"host|alias;host2|alias2" (alias may be empty). Aliases never contain ';' or '|'.</summary>
        public static string SerializeFavoriteHosts(IEnumerable<HostFavorite> favorites)
        {
            var sb = new StringBuilder();
            if (favorites != null)
            {
                foreach (var f in favorites)
                {
                    if (f == null || string.IsNullOrWhiteSpace(f.Host)) continue;
                    if (sb.Length > 0) sb.Append(';');
                    sb.Append(f.Host.Trim()).Append('|').Append(SanitizeFavoriteAlias(f.Alias));
                }
            }
            return sb.ToString();
        }

        public static List<HostFavorite> ParseFavoriteHosts(string text)
        {
            var list = new List<HostFavorite>();
            if (string.IsNullOrWhiteSpace(text)) return list;
            foreach (var part in text.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var bar = part.IndexOf('|');
                var host = (bar >= 0 ? part.Substring(0, bar) : part).Trim();
                var alias = bar >= 0 ? part.Substring(bar + 1) : "";
                if (host.Length == 0) continue;
                var dup = false;
                foreach (var f in list)
                {
                    if (string.Equals(f.Host, host, StringComparison.OrdinalIgnoreCase)) { dup = true; break; }
                }
                if (dup) continue;
                list.Add(new HostFavorite { Host = host, Alias = SanitizeFavoriteAlias(alias) });
                if (list.Count >= MaxFavoriteHosts) break;
            }
            return list;
        }

        public void EnsureInstanceGuid()
        {
            Guid parsed;
            if (!string.IsNullOrWhiteSpace(InstanceGuid) && Guid.TryParse(InstanceGuid.Trim(), out parsed))
            {
                InstanceGuid = parsed.ToString("D");
                return;
            }
            InstanceGuid = Guid.NewGuid().ToString("D");
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RegPath))
                {
                    if (key != null)
                        key.SetValue("InstanceGuid", InstanceGuid, RegistryValueKind.String);
                }
            }
            catch { }
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
                    // Pairing code is DPAPI-protected; legacy plaintext "PairingCode" is read once and
                    // removed on the next Save().
                    var pairingProtected = key.GetValue(PairingProtectedValueName) as string;
                    if (!string.IsNullOrEmpty(pairingProtected))
                    {
                        s.PairingCode = UnprotectPairing(pairingProtected);
                    }
                    else
                    {
                        var pairing = key.GetValue("PairingCode") as string;
                        if (pairing != null) s.PairingCode = pairing.Trim();
                    }
                    var instanceGuid = key.GetValue("InstanceGuid") as string;
                    if (!string.IsNullOrWhiteSpace(instanceGuid)) s.InstanceGuid = instanceGuid.Trim();
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
                    var favs = key.GetValue("FavoriteHosts") as string;
                    if (!string.IsNullOrWhiteSpace(favs))
                        s.FavoriteHosts = ParseFavoriteHosts(favs);
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
                    var pairingBlob = ProtectPairing(PairingCode);
                    if (string.IsNullOrEmpty(pairingBlob))
                        try { key.DeleteValue(PairingProtectedValueName, false); } catch { }
                    else
                        key.SetValue(PairingProtectedValueName, pairingBlob, RegistryValueKind.String);
                    // Remove legacy plaintext value.
                    try { key.DeleteValue("PairingCode", false); } catch { }
                    key.SetValue("InstanceGuid", InstanceGuid ?? "", RegistryValueKind.String);
                    key.SetValue("Language", Language ?? "en", RegistryValueKind.String);
                    key.SetValue("Theme", Theme ?? "system", RegistryValueKind.String);
                    key.SetValue("RecentHosts", string.Join(";", RecentHosts.ToArray()), RegistryValueKind.String);
                    key.SetValue("FavoriteHosts", SerializeFavoriteHosts(FavoriteHosts), RegistryValueKind.String);
                }
            }
            catch { }
        }

        private const string PairingProtectedValueName = "PairingCodeProtected";

        private static string ProtectPairing(string code)
        {
            code = (code ?? "").Trim();
            if (code.Length == 0) return "";
            return DpapiString.Protect(code);
        }

        private static string UnprotectPairing(string base64)
        {
            return DpapiString.Unprotect(base64);
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
                PairingCode = "", // never exported (DPAPI-local secret)
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
                RecentHosts = RecentHosts != null ? string.Join(";", RecentHosts.ToArray()) : "",
                FavoriteHosts = SerializeFavoriteHosts(FavoriteHosts)
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
            // Legacy exports may still carry a plaintext code; keep it only if non-empty.
            if (!string.IsNullOrWhiteSpace(imported.PairingCode)) PairingCode = imported.PairingCode.Trim();
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
            // Only replace favorites when the file carries them (older exports have none).
            if (!string.IsNullOrWhiteSpace(imported.FavoriteHosts))
                FavoriteHosts = ParseFavoriteHosts(imported.FavoriteHosts);
            Normalize();
        }
    }

    /// <summary>A saved server host (IP/name) with an optional friendly alias.</summary>
    public sealed class HostFavorite
    {
        public string Host { get; set; }
        public string Alias { get; set; }

        /// <summary>Combo text: "★ alias — host" or "★ host".</summary>
        public string DisplayText
        {
            get
            {
                return string.IsNullOrEmpty(Alias)
                    ? "\u2605 " + Host
                    : "\u2605 " + Alias + " \u2014 " + Host;
            }
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
        public string PairingCode { get; set; }
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
        public string FavoriteHosts { get; set; }
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
        public int? MileageKm { get; set; }

        /// <summary>1-based row number in the filtered Keys list (UI only).</summary>
        [XmlIgnore]
        public int Index { get; set; }

        public string KmText
        {
            get
            {
                if (!MileageKm.HasValue || MileageKm.Value <= 0) return "—";
                return MileageKm.Value.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
            }
        }
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
            get
            {
                var tip = string.IsNullOrEmpty(Vin) ? "VIN: —" : ("VIN: " + Vin);
                if (MileageKm.HasValue && MileageKm.Value > 0)
                    tip += "\nKm: " + KmText;
                return tip;
            }
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
