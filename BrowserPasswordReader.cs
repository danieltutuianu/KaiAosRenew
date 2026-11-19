using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace KaiAosRenew
{
    /// <summary>
    /// Reads Chromium-family login metadata for *.bmwgroup.com matching an email.
    /// Decrypts password via DPAPI+AES-GCM when possible (v10/v11). App-Bound (v20) is reported, not decrypted.
    /// Browsers: Chrome, Edge, Brave, Opera, Opera GX, Vivaldi, Chromium.
    /// </summary>
    internal static class BrowserPasswordReader
    {
        public sealed class Result
        {
            public bool Found { get; set; }
            public string SourceUrl { get; set; }
            public string Username { get; set; }
            public string BrowserName { get; set; }
            /// <summary>Local time when password was last modified/used in the browser, if known.</summary>
            public DateTime? LastSavedLocal { get; set; }
            /// <summary>Decrypted password when DPAPI path succeeded; otherwise null.</summary>
            public string Password { get; set; }
            public ChromiumCrypto.DecryptStatus DecryptStatus { get; set; }
            public string DecryptError { get; set; }
            public string Error { get; set; }
            /// <summary>Human-readable scan steps (no passwords). Written to browser-pass-check.log.</summary>
            public string Diagnostics { get; set; }
        }

        /// <summary>%AppData%\KaiAosRenew\browser-pass-check.log — overwrite each Check browser password.</summary>
        public static string DiagnosticsLogPath
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "KaiAosRenew");
                try { Directory.CreateDirectory(dir); } catch { }
                return Path.Combine(dir, "browser-pass-check.log");
            }
        }

        private sealed class Candidate
        {
            public string SourceUrl;
            public string Username;
            public string BrowserName;
            public string UserDataDir;
            public byte[] PasswordBlob;
            public long SortKey;
            public DateTime? LastSavedLocal;
        }

        private sealed class BrowserTarget
        {
            public string Name;
            public string UserDataDir;
        }

        /// <summary>
        /// Finds *.bmwgroup.com login metadata. When email is empty, any username matches
        /// (most recently used/modified across Chrome/Edge/Brave/Opera/Vivaldi/Chromium).
        /// </summary>
        public static Result TryFindBmwAosLoginMeta(string email)
        {
            var result = new Result();
            email = string.IsNullOrWhiteSpace(email) ? "" : email.Trim();
            var candidates = new List<Candidate>();
            var errors = new List<string>();
            var diag = new List<string>();
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            diag.Add("=== Browser password check " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
            diag.Add("Email filter: " + (email.Length == 0 ? "(any)" : email));
            diag.Add("User: " + (Environment.UserName ?? "?"));
            diag.Add("LocalAppData: " + local);
            diag.Add("RoamingAppData: " + roaming);
            diag.Add("Match rule: host is bmwgroup.com or ends with .bmwgroup.com; username equals email (case-insensitive) when email set.");

            var targets = new[]
            {
                new BrowserTarget { Name = "Chrome", UserDataDir = Path.Combine(local, "Google", "Chrome", "User Data") },
                new BrowserTarget { Name = "Edge", UserDataDir = Path.Combine(local, "Microsoft", "Edge", "User Data") },
                new BrowserTarget { Name = "Brave", UserDataDir = Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data") },
                new BrowserTarget { Name = "Opera", UserDataDir = Path.Combine(roaming, "Opera Software", "Opera Stable") },
                new BrowserTarget { Name = "Opera GX", UserDataDir = Path.Combine(roaming, "Opera Software", "Opera GX Stable") },
                new BrowserTarget { Name = "Vivaldi", UserDataDir = Path.Combine(local, "Vivaldi", "User Data") },
                new BrowserTarget { Name = "Chromium", UserDataDir = Path.Combine(local, "Chromium", "User Data") },
            };

            for (var i = 0; i < targets.Length; i++)
                ScanBrowser(targets[i].Name, targets[i].UserDataDir, email, candidates, errors, diag);

            diag.Add("--- Summary ---");
            diag.Add("Matching candidates: " + candidates.Count);
            if (errors.Count > 0)
                diag.Add("Errors: " + string.Join(" | ", errors.ToArray()));

            if (candidates.Count == 0)
            {
                result.Error = errors.Count > 0
                    ? string.Join(" ", errors.ToArray())
                    : (email.Length == 0
                        ? "No *.bmwgroup.com login found in Chrome/Edge/Brave/Opera/Vivaldi."
                        : "No *.bmwgroup.com login found in Chrome/Edge/Brave/Opera/Vivaldi for this email.");
                diag.Add("Result: NOT FOUND — " + result.Error);
                result.Diagnostics = string.Join(Environment.NewLine, diag.ToArray());
                WriteDiagnosticsFile(result.Diagnostics);
                return result;
            }

            candidates.Sort((a, b) => b.SortKey.CompareTo(a.SortKey));
            var best = candidates[0];
            result.Found = true;
            result.SourceUrl = best.SourceUrl;
            result.Username = best.Username;
            result.BrowserName = best.BrowserName;
            result.LastSavedLocal = best.LastSavedLocal;
            diag.Add("Best match: " + best.BrowserName + " | " + (best.SourceUrl ?? "") + " | user=" + (best.Username ?? ""));
            TryDecryptCandidate(best, result);
            diag.Add("Decrypt: " + result.DecryptStatus + (string.IsNullOrEmpty(result.DecryptError) ? "" : (" — " + result.DecryptError)));
            diag.Add("Result: FOUND");
            result.Diagnostics = string.Join(Environment.NewLine, diag.ToArray());
            WriteDiagnosticsFile(result.Diagnostics);
            return result;
        }

        private static void WriteDiagnosticsFile(string text)
        {
            try
            {
                File.WriteAllText(DiagnosticsLogPath, text ?? "", Encoding.UTF8);
            }
            catch { }
        }

        private static void TryDecryptCandidate(Candidate best, Result result)
        {
            result.DecryptStatus = ChromiumCrypto.DecryptStatus.Failed;
            if (best == null || best.PasswordBlob == null || best.PasswordBlob.Length == 0)
            {
                result.DecryptError = "Empty password blob.";
                return;
            }
            // Quick v20 detect without master key
            if (best.PasswordBlob.Length >= 3
                && best.PasswordBlob[0] == (byte)'v'
                && best.PasswordBlob[1] == (byte)'2'
                && best.PasswordBlob[2] == (byte)'0')
            {
                result.DecryptStatus = ChromiumCrypto.DecryptStatus.AppBoundEncrypted;
                result.DecryptError = "App-Bound Encryption (v20).";
                return;
            }

            byte[] masterKey;
            string keyErr;
            var keyStatus = ChromiumCrypto.TryGetMasterKey(best.UserDataDir, out masterKey, out keyErr);
            if (keyStatus != ChromiumCrypto.DecryptStatus.Ok)
            {
                result.DecryptStatus = keyStatus;
                result.DecryptError = keyErr;
                return;
            }

            try
            {
                string plain;
                string decErr;
                var decStatus = ChromiumCrypto.TryDecryptPassword(masterKey, best.PasswordBlob, out plain, out decErr);
                result.DecryptStatus = decStatus;
                result.DecryptError = decErr;
                if (decStatus == ChromiumCrypto.DecryptStatus.Ok)
                    result.Password = plain;
            }
            finally
            {
                if (masterKey != null) Array.Clear(masterKey, 0, masterKey.Length);
                if (best.PasswordBlob != null) Array.Clear(best.PasswordBlob, 0, best.PasswordBlob.Length);
            }
        }

        /// <summary>Returns password-manager URL and preferred exe path for the matched browser.</summary>
        public static bool TryGetPasswordManagerLaunch(string browserName, out string exePath, out string pageUrl)
        {
            exePath = null;
            pageUrl = null;
            if (string.IsNullOrWhiteSpace(browserName))
                return false;
            var name = browserName.Trim();
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            if (string.Equals(name, "Chrome", StringComparison.OrdinalIgnoreCase))
            {
                pageUrl = "chrome://password-manager/passwords";
                exePath = FirstExisting(
                    ResolveAppPath("chrome.exe"),
                    Path.Combine(pf, "Google", "Chrome", "Application", "chrome.exe"),
                    Path.Combine(pf86, "Google", "Chrome", "Application", "chrome.exe"),
                    Path.Combine(local, "Google", "Chrome", "Application", "chrome.exe"));
                return true;
            }
            if (string.Equals(name, "Edge", StringComparison.OrdinalIgnoreCase))
            {
                pageUrl = "edge://password-manager/passwords";
                exePath = FirstExisting(
                    ResolveAppPath("msedge.exe"),
                    Path.Combine(pf86, "Microsoft", "Edge", "Application", "msedge.exe"),
                    Path.Combine(pf, "Microsoft", "Edge", "Application", "msedge.exe"));
                return true;
            }
            if (string.Equals(name, "Brave", StringComparison.OrdinalIgnoreCase))
            {
                pageUrl = "brave://password-manager/passwords";
                exePath = FirstExisting(
                    ResolveAppPath("brave.exe"),
                    Path.Combine(pf, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
                    Path.Combine(pf86, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
                    Path.Combine(local, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"));
                return true;
            }
            if (string.Equals(name, "Opera", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Opera GX", StringComparison.OrdinalIgnoreCase))
            {
                pageUrl = "opera://settings/passwords";
                exePath = FirstExisting(
                    ResolveAppPath("opera.exe"),
                    Path.Combine(local, "Programs", "Opera", "opera.exe"),
                    Path.Combine(local, "Programs", "Opera GX", "opera.exe"),
                    Path.Combine(pf, "Opera", "opera.exe"),
                    Path.Combine(pf86, "Opera", "opera.exe"));
                return true;
            }
            if (string.Equals(name, "Vivaldi", StringComparison.OrdinalIgnoreCase))
            {
                pageUrl = "vivaldi://password-manager/passwords";
                exePath = FirstExisting(
                    ResolveAppPath("vivaldi.exe"),
                    Path.Combine(local, "Vivaldi", "Application", "vivaldi.exe"),
                    Path.Combine(pf, "Vivaldi", "Application", "vivaldi.exe"));
                return true;
            }
            if (string.Equals(name, "Chromium", StringComparison.OrdinalIgnoreCase))
            {
                pageUrl = "chrome://password-manager/passwords";
                exePath = FirstExisting(
                    ResolveAppPath("chrome.exe"),
                    Path.Combine(local, "Chromium", "Application", "chrome.exe"),
                    Path.Combine(pf, "Chromium", "Application", "chrome.exe"));
                return true;
            }
            return false;
        }

        /// <summary>
        /// Picks a Chromium-family browser for opening the password manager:
        /// HTTPS default if supported and installed, else first installed known browser.
        /// </summary>
        public static string TryResolvePreferredBrowserName()
        {
            var preferred = MapDefaultHttpsProgIdToBrowserName();
            if (!string.IsNullOrEmpty(preferred) && BrowserExeExists(preferred))
                return preferred;

            string[] fallback =
            {
                "Edge", "Chrome", "Brave", "Opera", "Opera GX", "Vivaldi", "Chromium"
            };
            for (var i = 0; i < fallback.Length; i++)
            {
                if (BrowserExeExists(fallback[i]))
                    return fallback[i];
            }
            return null;
        }

        private static bool BrowserExeExists(string browserName)
        {
            string exe, url;
            return TryGetPasswordManagerLaunch(browserName, out exe, out url)
                && !string.IsNullOrEmpty(exe)
                && File.Exists(exe);
        }

        private static string MapDefaultHttpsProgIdToBrowserName()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice", false))
                {
                    if (key == null) return null;
                    var progId = key.GetValue("ProgId") as string;
                    if (string.IsNullOrWhiteSpace(progId)) return null;
                    progId = progId.Trim();
                    if (progId.IndexOf("Chrome", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "Chrome";
                    if (progId.IndexOf("MSEdge", StringComparison.OrdinalIgnoreCase) >= 0
                        || progId.IndexOf("Edge", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "Edge";
                    if (progId.IndexOf("Brave", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "Brave";
                    if (progId.IndexOf("Opera", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "Opera";
                    if (progId.IndexOf("Vivaldi", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "Vivaldi";
                    if (progId.IndexOf("Chromium", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "Chromium";
                }
            }
            catch { }
            return null;
        }

        public static bool TryOpenPasswordManager(string browserName, out string error)
        {
            error = null;
            string exe, url;
            if (!TryGetPasswordManagerLaunch(browserName, out exe, out url))
            {
                error = "Unsupported browser: " + browserName;
                return false;
            }
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                error = "Browser executable not found for " + browserName + ".";
                return false;
            }
            try
            {
                // Chromium ignores chrome:// / edge:// when passed as a process argument
                // to an already-running browser. Open a window, then type the URL in the omnibox.
                var before = SnapshotBrowserWindows();
                var psi = new ProcessStartInfo();
                psi.FileName = exe;
                psi.Arguments = "--new-window --start-maximized about:blank";
                psi.UseShellExecute = false;
                Process.Start(psi);

                IntPtr hwnd = WaitForNewBrowserWindow(before, 4000);
                if (hwnd == IntPtr.Zero)
                    hwnd = FindAnyBrowserWindow();
                if (hwnd == IntPtr.Zero)
                {
                    error = "Browser window not found.";
                    return false;
                }

                ShowWindow(hwnd, SwMaximize);
                SetForegroundWindow(hwnd);
                Thread.Sleep(350);
                if (EnsureForegroundForSendKeys(hwnd))
                {
                    WinForms.SendKeys.SendWait("^l");
                    Thread.Sleep(200);
                    WinForms.SendKeys.SendWait(EscapeForSendKeys(url));
                    Thread.Sleep(80);
                    WinForms.SendKeys.SendWait("{ENTER}");
                }
                // Best-effort: focus page search and paste site (already on clipboard).
                Thread.Sleep(1500);
                try
                {
                    if (EnsureForegroundForSendKeys(hwnd))
                    {
                        WinForms.SendKeys.SendWait("^f");
                        Thread.Sleep(350);
                        WinForms.SendKeys.SendWait("^v");
                    }
                }
                catch { }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string EscapeForSendKeys(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length * 2);
            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if (ch == '+' || ch == '^' || ch == '%' || ch == '~' || ch == '(' || ch == ')'
                    || ch == '{' || ch == '}' || ch == '[' || ch == ']')
                {
                    sb.Append('{').Append(ch).Append('}');
                }
                else
                    sb.Append(ch);
            }
            return sb.ToString();
        }

        private static HashSet<long> SnapshotBrowserWindows()
        {
            var set = new HashSet<long>();
            EnumWindows((h, l) =>
            {
                if (IsBrowserMainWindow(h))
                    set.Add(h.ToInt64());
                return true;
            }, IntPtr.Zero);
            return set;
        }

        private static IntPtr WaitForNewBrowserWindow(HashSet<long> before, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                IntPtr found = IntPtr.Zero;
                EnumWindows((h, l) =>
                {
                    if (!IsBrowserMainWindow(h)) return true;
                    if (before != null && before.Contains(h.ToInt64())) return true;
                    found = h;
                    return false;
                }, IntPtr.Zero);
                if (found != IntPtr.Zero)
                    return found;
                Thread.Sleep(100);
            }
            return IntPtr.Zero;
        }

        private static IntPtr FindAnyBrowserWindow()
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows((h, l) =>
            {
                if (!IsBrowserMainWindow(h)) return true;
                found = h;
                return true; // keep going; last visible wins
            }, IntPtr.Zero);
            return found;
        }

        private static bool IsBrowserMainWindow(IntPtr hwnd)
        {
            if (!IsWindowVisible(hwnd)) return false;
            var cls = new StringBuilder(64);
            GetClassName(hwnd, cls, cls.Capacity);
            var className = cls.ToString();
            // Chrome / Edge / Brave / Opera / Vivaldi / Chromium
            if (className != "Chrome_WidgetWin_1") return false;
            var title = new StringBuilder(512);
            GetWindowText(hwnd, title, title.Capacity);
            return title.Length > 0;
        }

        private const int SwMaximize = 3;

        private static bool EnsureForegroundForSendKeys(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            if (GetForegroundWindow() == hwnd) return true;
            SetForegroundWindow(hwnd);
            Thread.Sleep(120);
            return GetForegroundWindow() == hwnd;
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private static string ResolveAppPath(string exeName)
        {
            if (string.IsNullOrEmpty(exeName)) return null;
            string[] roots =
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + exeName,
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\" + exeName
            };
            for (var i = 0; i < roots.Length; i++)
            {
                try
                {
                    using (var key = Registry.LocalMachine.OpenSubKey(roots[i], false))
                    {
                        if (key == null) continue;
                        var path = key.GetValue(null) as string;
                        if (!string.IsNullOrEmpty(path))
                        {
                            path = path.Trim().Trim('"');
                            if (File.Exists(path)) return path;
                        }
                    }
                }
                catch { }
            }
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + exeName, false))
                {
                    if (key != null)
                    {
                        var path = key.GetValue(null) as string;
                        if (!string.IsNullOrEmpty(path))
                        {
                            path = path.Trim().Trim('"');
                            if (File.Exists(path)) return path;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private static string FirstExisting(params string[] paths)
        {
            if (paths == null) return null;
            for (var i = 0; i < paths.Length; i++)
            {
                try
                {
                    if (!string.IsNullOrEmpty(paths[i]) && File.Exists(paths[i]))
                        return paths[i];
                }
                catch { }
            }
            return null;
        }

        private static readonly string[] LoginDbFileNames =
        {
            "Login Data",
            "Login Data For Account"
        };

        private static void ScanBrowser(string browserName, string userDataDir, string email,
            List<Candidate> candidates, List<string> errors, List<string> diag)
        {
            if (!Directory.Exists(userDataDir))
            {
                if (diag != null)
                    diag.Add(browserName + ": User Data missing — " + userDataDir);
                return;
            }
            if (diag != null)
                diag.Add(browserName + ": User Data OK — " + userDataDir);

            var profiles = new List<string>();
            foreach (var p in EnumerateProfiles(userDataDir))
                profiles.Add(p);
            if (diag != null)
                diag.Add(browserName + ": profiles with login DB: " + profiles.Count);

            for (var i = 0; i < profiles.Count; i++)
            {
                var profileDir = profiles[i];
                for (var d = 0; d < LoginDbFileNames.Length; d++)
                {
                    var loginDb = Path.Combine(profileDir, LoginDbFileNames[d]);
                    if (!File.Exists(loginDb))
                    {
                        if (diag != null)
                            diag.Add(browserName + ": " + LoginDbFileNames[d] + " missing in " + profileDir);
                        continue;
                    }
                    long size = 0;
                    try { size = new FileInfo(loginDb).Length; } catch { }
                    var walSize = FileSizeOrZero(loginDb + "-wal");
                    var shmSize = FileSizeOrZero(loginDb + "-shm");
                    if (diag != null)
                    {
                        diag.Add(browserName + ": scan " + loginDb
                            + " (" + size + " bytes, wal=" + walSize + ", shm=" + shmSize + ")");
                    }
                    try
                    {
                        ScanLoginDb(browserName, userDataDir, loginDb, email, candidates, diag);
                    }
                    catch (Exception ex)
                    {
                        errors.Add(browserName + ": " + ex.Message);
                        if (diag != null)
                            diag.Add(browserName + ": ERROR " + LoginDbFileNames[d] + " — " + ex.Message);
                    }
                }
            }
        }

        private static long FileSizeOrZero(string path)
        {
            try
            {
                if (File.Exists(path))
                    return new FileInfo(path).Length;
            }
            catch { }
            return 0;
        }

        private static bool ProfileHasLoginDb(string profileDir)
        {
            for (var i = 0; i < LoginDbFileNames.Length; i++)
            {
                if (File.Exists(Path.Combine(profileDir, LoginDbFileNames[i])))
                    return true;
            }
            return false;
        }

        private static IEnumerable<string> EnumerateProfiles(string userDataDir)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Opera often stores Login Data directly in the Stable folder.
            if (ProfileHasLoginDb(userDataDir))
            {
                seen.Add(userDataDir);
                yield return userDataDir;
            }

            var def = Path.Combine(userDataDir, "Default");
            if (Directory.Exists(def) && ProfileHasLoginDb(def) && seen.Add(def))
                yield return def;

            string[] dirs;
            try { dirs = Directory.GetDirectories(userDataDir); }
            catch { yield break; }
            for (var i = 0; i < dirs.Length; i++)
            {
                var name = Path.GetFileName(dirs[i]) ?? "";
                // Skip Chrome cache/system folders; keep Profile *, Default, and any other profile with login DB.
                if (string.Equals(name, "System Profile", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "Guest Profile", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "Crashpad", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "ShaderCache", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "GrShaderCache", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "GraphiteDawnCache", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "BrowserMetrics", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "Component", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith(".", StringComparison.Ordinal))
                    continue;
                if (!ProfileHasLoginDb(dirs[i]))
                    continue;
                if (!seen.Add(dirs[i]))
                    continue;
                yield return dirs[i];
            }
        }

        private static void CopySqliteSidecars(string loginDbPath, string temp)
        {
            File.Copy(loginDbPath, temp, true);
            TryCopySidecar(loginDbPath + "-wal", temp + "-wal");
            TryCopySidecar(loginDbPath + "-shm", temp + "-shm");
            TryCopySidecar(loginDbPath + "-journal", temp + "-journal");
        }

        private static void TryCopySidecar(string src, string dest)
        {
            try
            {
                if (File.Exists(src))
                    File.Copy(src, dest, true);
            }
            catch { }
        }

        private static void ScanLoginDb(string browserName, string userDataDir, string loginDbPath, string email,
            List<Candidate> candidates, List<string> diag)
        {
            var temp = Path.Combine(Path.GetTempPath(), "KaiAosRenew-LoginMeta-" + Guid.NewGuid().ToString("N"));
            var dbLabel = Path.GetFileName(loginDbPath) ?? "Login Data";
            try
            {
                CopySqliteSidecars(loginDbPath, temp);

                var totalRows = 0;
                var bmwHostRows = 0;
                var bmwEmailMismatch = 0;
                var bmwMatched = 0;
                var bmwOtherUsers = new List<string>();
                var bmwLikeHosts = new List<string>();
                var sampleOrigins = new List<string>();

                // Writable copy so SQLite can apply WAL; ReadOnly often shows 0 rows while Chrome is open.
                using (var conn = new SQLiteConnection("Data Source=" + temp + ";Version=3;Pooling=False;"))
                {
                    conn.Open();
                    try
                    {
                        using (var ck = conn.CreateCommand())
                        {
                            ck.CommandText = "PRAGMA wal_checkpoint(PASSIVE);";
                            ck.ExecuteNonQuery();
                        }
                    }
                    catch { }

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText =
                            "SELECT origin_url, username_value, " +
                            "IFNULL(date_password_modified,0), IFNULL(date_last_used,0), password_value " +
                            "FROM logins";
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                totalRows++;
                                var origin = reader.IsDBNull(0) ? "" : reader.GetString(0);
                                var user = reader.IsDBNull(1) ? "" : reader.GetString(1);

                                if (sampleOrigins.Count < 8)
                                {
                                    var sample = SafeHost(origin) + " | user="
                                        + (string.IsNullOrWhiteSpace(user) ? "(empty)" : user.Trim());
                                    if (!sampleOrigins.Contains(sample))
                                        sampleOrigins.Add(sample);
                                }

                                if (LooksBmwRelatedHost(origin) && bmwLikeHosts.Count < 12)
                                {
                                    var host = SafeHost(origin);
                                    var line = host + " | user=" + (string.IsNullOrWhiteSpace(user) ? "(empty)" : user.Trim());
                                    if (!bmwLikeHosts.Contains(line))
                                        bmwLikeHosts.Add(line);
                                }

                                if (!IsBmwGroupHost(origin))
                                    continue;
                                bmwHostRows++;

                                if (!string.IsNullOrWhiteSpace(email)
                                    && !string.Equals(user.Trim(), email, StringComparison.OrdinalIgnoreCase))
                                {
                                    bmwEmailMismatch++;
                                    if (bmwOtherUsers.Count < 8)
                                    {
                                        var other = SafeHost(origin) + " | user="
                                            + (string.IsNullOrWhiteSpace(user) ? "(empty)" : user.Trim());
                                        if (!bmwOtherUsers.Contains(other))
                                            bmwOtherUsers.Add(other);
                                    }
                                    continue;
                                }

                                bmwMatched++;
                                long mod = 0, used = 0;
                                try { mod = reader.GetInt64(2); } catch { }
                                try { used = reader.GetInt64(3); } catch { }
                                byte[] blob = null;
                                try
                                {
                                    if (!reader.IsDBNull(4))
                                        blob = (byte[])reader[4];
                                }
                                catch { }
                                var sort = Math.Max(mod, used);
                                candidates.Add(new Candidate
                                {
                                    SourceUrl = origin,
                                    Username = user,
                                    BrowserName = browserName,
                                    UserDataDir = userDataDir,
                                    PasswordBlob = blob,
                                    SortKey = sort,
                                    LastSavedLocal = ChromiumTimeToLocal(sort)
                                });
                            }
                        }
                    }
                }

                if (diag != null)
                {
                    diag.Add(browserName + " [" + dbLabel + "]: rows=" + totalRows
                        + " bmwgroup.com=" + bmwHostRows
                        + " emailMismatch=" + bmwEmailMismatch
                        + " matched=" + bmwMatched);
                    for (var i = 0; i < bmwOtherUsers.Count; i++)
                        diag.Add(browserName + " [" + dbLabel + "]: bmwgroup other user — " + bmwOtherUsers[i]);
                    if (bmwHostRows == 0 && bmwLikeHosts.Count > 0)
                    {
                        diag.Add(browserName + " [" + dbLabel + "]: no *.bmwgroup.com; bmw-related:");
                        for (var i = 0; i < bmwLikeHosts.Count; i++)
                            diag.Add(browserName + " [" + dbLabel + "]:   " + bmwLikeHosts[i]);
                    }
                    if (totalRows == 0)
                    {
                        diag.Add(browserName + " [" + dbLabel + "]: empty logins table — if Chrome Password Manager shows passwords, try closing Chrome completely and re-check; or passwords may be cloud-only / extension.");
                    }
                    else if (bmwHostRows == 0 && bmwLikeHosts.Count == 0)
                    {
                        diag.Add(browserName + " [" + dbLabel + "]: sample origins (first " + sampleOrigins.Count + "):");
                        for (var i = 0; i < sampleOrigins.Count; i++)
                            diag.Add(browserName + " [" + dbLabel + "]:   " + sampleOrigins[i]);
                    }
                }
            }
            finally
            {
                // Always remove temp Login Data copies (even on exceptions); SQLite may hold handles briefly.
                try { SQLiteConnection.ClearAllPools(); } catch { }
                TryDeleteTempFile(temp);
                TryDeleteTempFile(temp + "-wal");
                TryDeleteTempFile(temp + "-shm");
                TryDeleteTempFile(temp + "-journal");
            }
        }

        private static void TryDeleteTempFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (!File.Exists(path)) return;
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                    return;
                }
                catch
                {
                    if (attempt < 4) Thread.Sleep(40);
                }
            }
        }

        private static string SafeHost(string originUrl)
        {
            if (string.IsNullOrWhiteSpace(originUrl)) return "(empty)";
            Uri uri;
            if (Uri.TryCreate(originUrl, UriKind.Absolute, out uri) && !string.IsNullOrEmpty(uri.Host))
                return uri.Host;
            return originUrl.Length > 80 ? originUrl.Substring(0, 80) + "…" : originUrl;
        }

        private static bool LooksBmwRelatedHost(string originUrl)
        {
            if (string.IsNullOrWhiteSpace(originUrl)) return false;
            return originUrl.IndexOf("bmw", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static bool IsBmwGroupHost(string originUrl)
        {
            if (string.IsNullOrWhiteSpace(originUrl)) return false;
            Uri uri;
            if (!Uri.TryCreate(originUrl, UriKind.Absolute, out uri))
                return false;
            var host = (uri.Host ?? "").Trim().ToLowerInvariant();
            return host == "bmwgroup.com" || host.EndsWith(".bmwgroup.com", StringComparison.Ordinal);
        }

        /// <summary>Chromium stores microseconds since 1601-01-01 UTC (Windows FILETIME-like).</summary>
        private static DateTime? ChromiumTimeToLocal(long chromiumMicros)
        {
            if (chromiumMicros <= 0) return null;
            try
            {
                var ticks = chromiumMicros * 10;
                var utc = DateTime.FromFileTimeUtc(ticks);
                return utc.ToLocalTime();
            }
            catch
            {
                return null;
            }
        }
    }
}
