using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Serialization;

namespace KaiAosRenew
{
    public class AppSettings
    {
        public string Email { get; set; }
        public string PasswordProtectedBase64 { get; set; }
        public int IntervalHours { get; set; }
        public string ConfigPath { get; set; }
        public string RegisterUrl { get; set; }
        /// <summary>Optional WebEAM token URL override (empty = from KAI config).</summary>
        public string WebEamUrl { get; set; }
        public string Language { get; set; }
        public bool StartWithWindows { get; set; }
        public bool LogVerbose { get; set; }
        public bool MinimizeToTray { get; set; }
        /// <summary>X / Alt+F4 hides to tray instead of exiting.</summary>
        public bool MinimizeOnClose { get; set; }
        /// <summary>Check/download/apply Velopack updates at startup without prompting.</summary>
        public bool AutoUpdate { get; set; }
        public bool UseSystemProxy { get; set; }
        /// <summary>On auth failure, hint to update app password to match Chrome/Edge (no decrypt).</summary>
        public bool BrowserPasswordHintOnAuthFail { get; set; }
        /// <summary>Remind to rotate AOS password every N hours (1–168).</summary>
        public int PasswordRotateRemindHours { get; set; }
        /// <summary>UTC when app AOS password was last changed by the user.</summary>
        public DateTime? LastPasswordChangeUtc { get; set; }
        /// <summary>UTC of last password-rotate reminder log/balloon (throttle).</summary>
        public DateTime? LastPasswordRemindUtc { get; set; }
        /// <summary>system | day | night</summary>
        public string ThemeMode { get; set; }
        public DateTime? LastSuccessUtc { get; set; }
        /// <summary>UTC of the last Register that returned credentials.</summary>
        public DateTime? LastRegisterOkUtc { get; set; }
        /// <summary>UTC of the last Register attempt that failed, including auth rejection.</summary>
        public DateTime? LastRegisterFailUtc { get; set; }
        public string LastMessage { get; set; }
        public int ConsecutiveFailures { get; set; }
        /// <summary>OAuth expires_in seconds from last successful WebEAM token (0 = unknown).</summary>
        public int LastTokenExpiresInSeconds { get; set; }
        /// <summary>Absolute UTC expiry of last WebEAM token (M2m-style).</summary>
        public DateTime? LastTokenExpiryUtc { get; set; }
        /// <summary>Until this UTC time, never call Register (anti-lockout after hard 401).</summary>
        public DateTime? AuthCooldownUntilUtc { get; set; }
        /// <summary>After wrong-password Register (401), block auto Register until the user changes the password.</summary>
        public bool AuthRegisterBlocked { get; set; }
        /// <summary>Activate AuthRegisterBlocked after this many auto/failed Register strikes (1–10).</summary>
        public int AuthRegisterBlockAfter { get; set; }
        /// <summary>Current Register strike count toward AuthRegisterBlockAfter.</summary>
        public int AuthRegisterStrikeCount { get; set; }
        /// <summary>UTC of last automatic Register attempt (Health-fail path); max one per 12h.</summary>
        public DateTime? LastAutoRegisterUtc { get; set; }
        /// <summary>Minutes to block Register after hard 401 (5–240).</summary>
        public int AuthCooldownMinutes { get; set; }
        /// <summary>Tray balloon when a new key is detected.</summary>
        public bool KeysBalloonEnabled { get; set; }
        /// <summary>When KeysBalloonEnabled, show VIN info tip only after BMW upload fails (not on every read).</summary>
        public bool KeysVinTipBmwFailOnly { get; set; }
        /// <summary>Alert when Local OK but BMW upload missing for &gt; stall minutes.</summary>
        public bool KeysStallAlertEnabled { get; set; }
        /// <summary>Folder watched for keyreader.bin (default C:\Users\Public\keyreader).</summary>
        public string KeysPickupFolder { get; set; }
        /// <summary>Minutes after Local OK without BMW Server before stall alert (1–60).</summary>
        public int KeysStallMinutes { get; set; }
        /// <summary>Copy VIN to clipboard automatically when a new VIN is read.</summary>
        public bool KeysAutoCopyVin { get; set; }
        /// <summary>Keys tab polling interval when active (1–60 seconds).</summary>
        public int KeysRefreshSeconds { get; set; }
        /// <summary>VIN info tip display duration with Copy button (3–60 seconds).</summary>
        public int KeysVinTipSeconds { get; set; }
        public string KeysUploaderLogDir { get; set; }
        public string KeysKaiLogDir { get; set; }
        public string KeysSqlitePath { get; set; }
        /// <summary>TCP notify server for remote KaiAosNotifyClient.</summary>
        public bool NotifyServerEnabled { get; set; }
        /// <summary>TCP listen port (1024–65535), default 17890.</summary>
        public int NotifyServerPort { get; set; }
        /// <summary>Optional LAN pairing code (empty = no auth). Stored as plain text in settings.xml.</summary>
        public string NotifyPairingCode { get; set; }
        /// <summary>Stable GUID for this server instance (peer origin/ack targeting).</summary>
        public string PeerSyncServerId { get; set; }
        /// <summary>Legacy (Apps Script era) server name. Kept only so XmlSerializer can read old settings.xml; migrated to FirebasePushServerName in NormalizeClamps and then cleared. Not used by the UI.
        /// Not marked [Obsolete]: XmlSerializer skips obsolete members and would not deserialize it.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public string PeerSyncServerName { get; set; }
        /// <summary>Last processed peer credential seq from relay.</summary>
        public long PeerSyncLastSeq { get; set; }
        /// <summary>Last processed peer credential eventId.</summary>
        public string PeerSyncLastEventId { get; set; }
        /// <summary>Last processed peer ACK seq from relay.</summary>
        public long PeerSyncLastAckSeq { get; set; }
        /// <summary>Last processed peer command seq from relay.</summary>
        public long PeerSyncLastCmdSeq { get; set; }
        /// <summary>Last processed peer command id (dedupe).</summary>
        public string PeerSyncLastCmdId { get; set; }
        /// <summary>Sync AOS credentials with peer servers via Firebase Realtime Database (push).</summary>
        public bool FirebasePushEnabled { get; set; }
        /// <summary>Firebase Realtime Database URL (https://….firebasedatabase.app or ….firebaseio.com).</summary>
        public string FirebasePushUrl { get; set; }
        /// <summary>Shared Firebase push secret (DPAPI-protected).</summary>
        public string FirebasePushSecretProtectedBase64 { get; set; }
        /// <summary>Display name for this server on the Firebase peer dashboard.</summary>
        public string FirebasePushServerName { get; set; }
        /// <summary>Force Update UI / debug banner until explicitly turned off.</summary>
        public bool DebugMode { get; set; }
        /// <summary>User pressed Stop timer; suppresses auto-start until Start timer.</summary>
        public bool TimerUserStopped { get; set; }

        public AppSettings()
        {
            IntervalHours = 1;
            ConfigPath = @"C:\ProgramData\BMW\ISPI\config\KAI\KAI";
            RegisterUrl = "https://kai-b2i-basic.bmwgroup.com/api/agent/register";
            WebEamUrl = "";
            Language = "en";
            ThemeMode = "system";
            UseSystemProxy = false;
            BrowserPasswordHintOnAuthFail = true;
            PasswordRotateRemindHours = 1;
            MinimizeOnClose = true;
            AutoUpdate = true;
            LogVerbose = true;
            AuthCooldownMinutes = 60;
            AuthRegisterBlockAfter = 3;
            AuthRegisterStrikeCount = 0;
            KeysBalloonEnabled = true;
            KeysVinTipBmwFailOnly = false;
            KeysStallAlertEnabled = true;
            KeysPickupFolder = @"C:\Users\Public\keyreader";
            KeysStallMinutes = 2;
            KeysAutoCopyVin = false;
            KeysRefreshSeconds = 3;
            KeysVinTipSeconds = 10;
            KeysUploaderLogDir = @"C:\ProgramData\BMW\ISPI\Logs\KAI\KeyUploader";
            KeysKaiLogDir = @"C:\ProgramData\BMW\ISPI\Logs\KAI\KAI";
            KeysSqlitePath = @"C:\ProgramData\BMW\ISPI\data\KAI\KAI\SqliteKaiDb.db";
            NotifyServerEnabled = false;
            NotifyServerPort = 17890;
            NotifyPairingCode = "";
            PeerSyncServerId = "";
            PeerSyncServerName = "";
            PeerSyncLastSeq = 0;
            PeerSyncLastEventId = "";
            PeerSyncLastAckSeq = 0;
            PeerSyncLastCmdSeq = 0;
            PeerSyncLastCmdId = "";
            FirebasePushEnabled = false;
            FirebasePushUrl = "";
            FirebasePushSecretProtectedBase64 = null;
            FirebasePushServerName = "";
        }

        private static string StoreDir
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "KaiAosRenew");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string SettingsPath
        {
            get { return Path.Combine(StoreDir, "settings.xml"); }
        }

        public static string LogPath
        {
            get { return Path.Combine(StoreDir, "renew.log"); }
        }

        public string GetPassword()
        {
            if (string.IsNullOrEmpty(PasswordProtectedBase64))
                return string.Empty;
            var protectedBytes = Convert.FromBase64String(PasswordProtectedBase64);
            var plain = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }

        public void SetPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                PasswordProtectedBase64 = null;
                return;
            }
            var plain = Encoding.UTF8.GetBytes(password);
            var protectedBytes = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            PasswordProtectedBase64 = Convert.ToBase64String(protectedBytes);
        }

        public void EnsurePeerSyncServerId()
        {
            Guid parsed;
            if (!string.IsNullOrWhiteSpace(PeerSyncServerId) && Guid.TryParse(PeerSyncServerId.Trim(), out parsed))
            {
                PeerSyncServerId = parsed.ToString("D");
                return;
            }
            PeerSyncServerId = Guid.NewGuid().ToString("D");
        }

        public string GetFirebasePushSecret()
        {
            if (string.IsNullOrEmpty(FirebasePushSecretProtectedBase64))
                return string.Empty;
            var protectedBytes = Convert.FromBase64String(FirebasePushSecretProtectedBase64);
            var plain = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }

        public void SetFirebasePushSecret(string secret)
        {
            if (string.IsNullOrEmpty(secret))
            {
                FirebasePushSecretProtectedBase64 = null;
                return;
            }
            var plain = Encoding.UTF8.GetBytes(secret);
            var protectedBytes = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            FirebasePushSecretProtectedBase64 = Convert.ToBase64String(protectedBytes);
        }

        public void NormalizeClamps()
        {
            if (PasswordRotateRemindHours < 1) PasswordRotateRemindHours = 1;
            if (PasswordRotateRemindHours > 168) PasswordRotateRemindHours = 168;
            if (KeysStallMinutes < 1) KeysStallMinutes = 1;
            if (KeysStallMinutes > 60) KeysStallMinutes = 60;
            if (AuthCooldownMinutes < 5) AuthCooldownMinutes = 5;
            if (AuthCooldownMinutes > 240) AuthCooldownMinutes = 240;
            if (AuthRegisterBlockAfter < 1) AuthRegisterBlockAfter = 1;
            if (AuthRegisterBlockAfter > 10) AuthRegisterBlockAfter = 10;
            if (AuthRegisterStrikeCount < 0) AuthRegisterStrikeCount = 0;
            if (KeysRefreshSeconds < 1) KeysRefreshSeconds = 1;
            if (KeysRefreshSeconds > 60) KeysRefreshSeconds = 60;
            if (KeysVinTipSeconds < 3) KeysVinTipSeconds = 3;
            if (KeysVinTipSeconds > 60) KeysVinTipSeconds = 60;
            if (NotifyServerPort < 1024 || NotifyServerPort > 65535)
                NotifyServerPort = 17890;
            if (NotifyPairingCode == null) NotifyPairingCode = "";
            NotifyPairingCode = NotifyPairingCode.Trim();
            if (PeerSyncServerId == null) PeerSyncServerId = "";
            PeerSyncServerId = PeerSyncServerId.Trim();
            if (PeerSyncServerName == null) PeerSyncServerName = "";
            PeerSyncServerName = PeerSyncServerName.Trim();
            if (PeerSyncLastSeq < 0) PeerSyncLastSeq = 0;
            if (PeerSyncLastAckSeq < 0) PeerSyncLastAckSeq = 0;
            if (PeerSyncLastCmdSeq < 0) PeerSyncLastCmdSeq = 0;
            if (PeerSyncLastEventId == null) PeerSyncLastEventId = "";
            if (PeerSyncLastCmdId == null) PeerSyncLastCmdId = "";
            if (FirebasePushUrl == null) FirebasePushUrl = "";
            FirebasePushUrl = FirebasePushUrl.Trim();
            if (FirebasePushServerName == null) FirebasePushServerName = "";
            FirebasePushServerName = FirebasePushServerName.Trim();
            // One-shot migration from the removed Apps Script peer sync server name.
            if (string.IsNullOrWhiteSpace(FirebasePushServerName) && !string.IsNullOrWhiteSpace(PeerSyncServerName))
                FirebasePushServerName = PeerSyncServerName;
            PeerSyncServerName = "";
            if (string.IsNullOrWhiteSpace(FirebasePushServerName))
            {
                try { FirebasePushServerName = Environment.MachineName; }
                catch { FirebasePushServerName = "Server"; }
            }
            EnsurePeerSyncServerId();
            if (string.IsNullOrWhiteSpace(KeysPickupFolder))
                KeysPickupFolder = @"C:\Users\Public\keyreader";
            if (string.IsNullOrWhiteSpace(KeysUploaderLogDir))
                KeysUploaderLogDir = @"C:\ProgramData\BMW\ISPI\Logs\KAI\KeyUploader";
            if (string.IsNullOrWhiteSpace(KeysKaiLogDir))
                KeysKaiLogDir = @"C:\ProgramData\BMW\ISPI\Logs\KAI\KAI";
            if (string.IsNullOrWhiteSpace(KeysSqlitePath))
                KeysSqlitePath = @"C:\ProgramData\BMW\ISPI\data\KAI\KAI\SqliteKaiDb.db";
            if (string.IsNullOrWhiteSpace(RegisterUrl))
                RegisterUrl = "https://kai-b2i-basic.bmwgroup.com/api/agent/register";
            if (WebEamUrl == null) WebEamUrl = "";
            LogVerbose = true;
        }

        private static readonly object SaveGate = new object();

        public static AppSettings Load()
        {
            var primary = TryLoadFromPath(SettingsPath);
            if (primary != null)
                return primary;
            var bakPath = SettingsPath + ".bak";
            var fromBak = TryLoadFromPath(bakPath);
            if (fromBak != null)
            {
                try
                {
                    // Restore primary from intact backup so next Save has a base file.
                    File.Copy(bakPath, SettingsPath, true);
                }
                catch { }
                return fromBak;
            }
            return new AppSettings();
        }

        private static AppSettings TryLoadFromPath(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;
            try
            {
                var ser = new XmlSerializer(typeof(AppSettings));
                using (var fs = File.OpenRead(path))
                {
                    var loaded = ser.Deserialize(fs) as AppSettings;
                    if (loaded == null)
                        return null;
                    try
                    {
                        var xml = File.ReadAllText(path);
                        ApplyLegacyDefaults(loaded, xml);
                    }
                    catch { }
                    loaded.NormalizeClamps();
                    return loaded;
                }
            }
            catch
            {
                return null;
            }
        }

        private static void ApplyLegacyDefaults(AppSettings loaded, string xml)
        {
            if (loaded == null || string.IsNullOrEmpty(xml)) return;
            if (xml.IndexOf("BrowserPasswordHintOnAuthFail", StringComparison.OrdinalIgnoreCase) < 0)
                loaded.BrowserPasswordHintOnAuthFail = true;
            if (xml.IndexOf("PasswordRotateRemindHours", StringComparison.OrdinalIgnoreCase) < 0
                || loaded.PasswordRotateRemindHours < 1)
                loaded.PasswordRotateRemindHours = 1;
            if (xml.IndexOf("KeysBalloonEnabled", StringComparison.OrdinalIgnoreCase) < 0)
                loaded.KeysBalloonEnabled = true;
            if (xml.IndexOf("KeysVinTipBmwFailOnly", StringComparison.OrdinalIgnoreCase) < 0)
                loaded.KeysVinTipBmwFailOnly = false;
            if (!loaded.KeysBalloonEnabled)
                loaded.KeysVinTipBmwFailOnly = false;
            if (xml.IndexOf("KeysStallAlertEnabled", StringComparison.OrdinalIgnoreCase) < 0)
                loaded.KeysStallAlertEnabled = true;
            if (xml.IndexOf("KeysStallMinutes", StringComparison.OrdinalIgnoreCase) < 0
                || loaded.KeysStallMinutes < 1)
                loaded.KeysStallMinutes = 2;
            if (xml.IndexOf("KeysPickupFolder", StringComparison.OrdinalIgnoreCase) < 0
                || string.IsNullOrWhiteSpace(loaded.KeysPickupFolder))
                loaded.KeysPickupFolder = @"C:\Users\Public\keyreader";
            if (xml.IndexOf("MinimizeOnClose", StringComparison.OrdinalIgnoreCase) < 0)
                loaded.MinimizeOnClose = true;
            if (xml.IndexOf("AutoUpdate", StringComparison.OrdinalIgnoreCase) < 0)
                loaded.AutoUpdate = true;
            if (xml.IndexOf("AuthCooldownMinutes", StringComparison.OrdinalIgnoreCase) < 0
                || loaded.AuthCooldownMinutes < 5)
                loaded.AuthCooldownMinutes = 60;
            if (xml.IndexOf("AuthRegisterBlockAfter", StringComparison.OrdinalIgnoreCase) < 0
                || loaded.AuthRegisterBlockAfter < 1)
                loaded.AuthRegisterBlockAfter = 3;
            if (xml.IndexOf("KeysRefreshSeconds", StringComparison.OrdinalIgnoreCase) < 0
                || loaded.KeysRefreshSeconds < 1)
                loaded.KeysRefreshSeconds = 3;
            if (xml.IndexOf("KeysVinTipSeconds", StringComparison.OrdinalIgnoreCase) < 0
                || loaded.KeysVinTipSeconds < 3)
                loaded.KeysVinTipSeconds = 10;
            if (xml.IndexOf("KeysUploaderLogDir", StringComparison.OrdinalIgnoreCase) < 0
                || string.IsNullOrWhiteSpace(loaded.KeysUploaderLogDir))
                loaded.KeysUploaderLogDir = @"C:\ProgramData\BMW\ISPI\Logs\KAI\KeyUploader";
            if (xml.IndexOf("KeysKaiLogDir", StringComparison.OrdinalIgnoreCase) < 0
                || string.IsNullOrWhiteSpace(loaded.KeysKaiLogDir))
                loaded.KeysKaiLogDir = @"C:\ProgramData\BMW\ISPI\Logs\KAI\KAI";
            if (xml.IndexOf("KeysSqlitePath", StringComparison.OrdinalIgnoreCase) < 0
                || string.IsNullOrWhiteSpace(loaded.KeysSqlitePath))
                loaded.KeysSqlitePath = @"C:\ProgramData\BMW\ISPI\data\KAI\KAI\SqliteKaiDb.db";
        }

        public void Save()
        {
            NormalizeClamps();
            lock (SaveGate)
            {
                var path = SettingsPath;
                var tmp = path + ".tmp";
                var bak = path + ".bak";
                var ser = new XmlSerializer(typeof(AppSettings));
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    ser.Serialize(fs, this);
                    fs.Flush(true);
                }
                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(tmp, path, bak, true);
                    }
                    catch
                    {
                        // Fallback when Replace is unavailable (locked bak, etc.).
                        try { File.Copy(path, bak, true); } catch { }
                        File.Copy(tmp, path, true);
                        try { File.Delete(tmp); } catch { }
                    }
                }
                else
                {
                    File.Move(tmp, path);
                }
            }
        }

        /// <summary>Portable settings without DPAPI password.</summary>
        public void ExportPortable(string path)
        {
            File.WriteAllText(path, ExportPortableToString() ?? "", Encoding.UTF8);
        }

        public string ExportPortableToString()
        {
            var portable = new AppSettings();
            portable.Email = Email;
            portable.IntervalHours = IntervalHours;
            portable.ConfigPath = ConfigPath;
            portable.RegisterUrl = RegisterUrl;
            portable.WebEamUrl = WebEamUrl;
            portable.Language = Language;
            portable.StartWithWindows = StartWithWindows;
            portable.LogVerbose = true;
            portable.MinimizeToTray = MinimizeToTray;
            portable.MinimizeOnClose = MinimizeOnClose;
            portable.AutoUpdate = AutoUpdate;
            portable.UseSystemProxy = UseSystemProxy;
            portable.BrowserPasswordHintOnAuthFail = BrowserPasswordHintOnAuthFail;
            portable.PasswordRotateRemindHours = PasswordRotateRemindHours;
            portable.ThemeMode = ThemeMode;
            portable.AuthCooldownMinutes = AuthCooldownMinutes;
            portable.AuthRegisterBlockAfter = AuthRegisterBlockAfter;
            portable.KeysBalloonEnabled = KeysBalloonEnabled;
            portable.KeysVinTipBmwFailOnly = KeysBalloonEnabled && KeysVinTipBmwFailOnly;
            portable.KeysStallAlertEnabled = KeysStallAlertEnabled;
            portable.KeysPickupFolder = KeysPickupFolder;
            portable.KeysStallMinutes = KeysStallMinutes;
            portable.KeysAutoCopyVin = KeysAutoCopyVin;
            portable.KeysRefreshSeconds = KeysRefreshSeconds;
            portable.KeysVinTipSeconds = KeysVinTipSeconds;
            portable.KeysUploaderLogDir = KeysUploaderLogDir;
            portable.KeysKaiLogDir = KeysKaiLogDir;
            portable.KeysSqlitePath = KeysSqlitePath;
            portable.NotifyServerEnabled = NotifyServerEnabled;
            portable.NotifyServerPort = NotifyServerPort;
            portable.NotifyPairingCode = NotifyPairingCode;
            portable.FirebasePushEnabled = FirebasePushEnabled;
            portable.FirebasePushUrl = FirebasePushUrl;
            portable.FirebasePushServerName = FirebasePushServerName;
            // Secrets stay DPAPI-local (same as AOS password) — not in portable export.
            var ser = new XmlSerializer(typeof(AppSettings));
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
            var ser = new XmlSerializer(typeof(AppSettings));
            AppSettings imported;
            using (var sr = new StringReader(xml))
                imported = ser.Deserialize(sr) as AppSettings;
            if (imported == null)
                throw new InvalidOperationException("Invalid settings file.");
            if (!string.IsNullOrWhiteSpace(imported.Email))
                Email = imported.Email.Trim();
            if (imported.IntervalHours >= 1 && imported.IntervalHours <= 168)
                IntervalHours = imported.IntervalHours;
            if (!string.IsNullOrWhiteSpace(imported.ConfigPath))
                ConfigPath = imported.ConfigPath.Trim();
            if (!string.IsNullOrWhiteSpace(imported.RegisterUrl))
                RegisterUrl = imported.RegisterUrl.Trim();
            if (imported.WebEamUrl != null)
                WebEamUrl = imported.WebEamUrl.Trim();
            if (!string.IsNullOrWhiteSpace(imported.Language))
                Language = imported.Language.Trim();
            StartWithWindows = imported.StartWithWindows;
            LogVerbose = true;
            MinimizeToTray = imported.MinimizeToTray;
            MinimizeOnClose = imported.MinimizeOnClose;
            AutoUpdate = imported.AutoUpdate;
            UseSystemProxy = imported.UseSystemProxy;
            BrowserPasswordHintOnAuthFail = imported.BrowserPasswordHintOnAuthFail;
            if (imported.PasswordRotateRemindHours >= 1 && imported.PasswordRotateRemindHours <= 168)
                PasswordRotateRemindHours = imported.PasswordRotateRemindHours;
            if (!string.IsNullOrWhiteSpace(imported.ThemeMode))
                ThemeMode = imported.ThemeMode.Trim();
            AuthCooldownMinutes = imported.AuthCooldownMinutes;
            if (imported.AuthRegisterBlockAfter >= 1 && imported.AuthRegisterBlockAfter <= 10)
                AuthRegisterBlockAfter = imported.AuthRegisterBlockAfter;
            KeysBalloonEnabled = imported.KeysBalloonEnabled;
            KeysVinTipBmwFailOnly = imported.KeysBalloonEnabled && imported.KeysVinTipBmwFailOnly;
            KeysStallAlertEnabled = imported.KeysStallAlertEnabled;
            KeysAutoCopyVin = imported.KeysAutoCopyVin;
            if (!string.IsNullOrWhiteSpace(imported.KeysPickupFolder))
                KeysPickupFolder = imported.KeysPickupFolder.Trim();
            if (imported.KeysStallMinutes >= 1 && imported.KeysStallMinutes <= 60)
                KeysStallMinutes = imported.KeysStallMinutes;
            KeysRefreshSeconds = imported.KeysRefreshSeconds;
            KeysVinTipSeconds = imported.KeysVinTipSeconds;
            if (!string.IsNullOrWhiteSpace(imported.KeysUploaderLogDir))
                KeysUploaderLogDir = imported.KeysUploaderLogDir.Trim();
            if (!string.IsNullOrWhiteSpace(imported.KeysKaiLogDir))
                KeysKaiLogDir = imported.KeysKaiLogDir.Trim();
            if (!string.IsNullOrWhiteSpace(imported.KeysSqlitePath))
                KeysSqlitePath = imported.KeysSqlitePath.Trim();
            NotifyServerEnabled = imported.NotifyServerEnabled;
            if (imported.NotifyServerPort >= 1024 && imported.NotifyServerPort <= 65535)
                NotifyServerPort = imported.NotifyServerPort;
            if (imported.NotifyPairingCode != null)
                NotifyPairingCode = imported.NotifyPairingCode.Trim();
            FirebasePushEnabled = imported.FirebasePushEnabled;
            if (imported.FirebasePushUrl != null)
                FirebasePushUrl = imported.FirebasePushUrl.Trim();
            if (!string.IsNullOrWhiteSpace(imported.FirebasePushServerName))
                FirebasePushServerName = imported.FirebasePushServerName.Trim();
            else if (!string.IsNullOrWhiteSpace(imported.PeerSyncServerName))
                FirebasePushServerName = imported.PeerSyncServerName.Trim(); // legacy export
            NormalizeClamps();
        }
    }
}
