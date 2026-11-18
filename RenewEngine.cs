using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using BMW.ISPI.KAI.Decentral.KaiAdminLibData.KaiZentral;

namespace KaiAosRenew
{
    public sealed class RenewResult
    {
        public bool Ok { get; set; }
        public string Message { get; set; }
        public string ClientId { get; set; }
        public string IdentificationHash { get; set; }
        public bool SkippedRegister { get; set; }
        /// <summary>Register returned auth failure but existing WebEAM token still works.</summary>
        public bool RegisterAuthRejected { get; set; }
        /// <summary>True when RegisterWithKaiZentral was invoked (success or fail).</summary>
        public bool RegisterAttempted { get; set; }
    }

    public static class RenewEngine
    {
        private static readonly int[] BackoffSeconds = new int[] { 5, 15, 45 };
        private static readonly object LogGate = new object();

        public static RenewResult Run(
            AppSettings settings,
            bool forceRegister = false,
            string passwordOverride = null,
            bool allowRegister = true)
        {
            return Run(settings, forceRegister, passwordOverride, allowRegister, CancellationToken.None);
        }

        public static RenewResult Run(
            AppSettings settings,
            bool forceRegister,
            string passwordOverride,
            bool allowRegister,
            CancellationToken ct)
        {
            if (!allowRegister)
            {
                Log("Register disabled (auth cooldown) — Health only...");
                return HealthOnly(settings);
            }

            RenewResult last = Fail("Unknown");
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                if (ct.IsCancellationRequested)
                    return Fail("Cancelled");
                Log("Attempt " + attempt + "/3...");
                last = RunOnce(settings, forceRegister, passwordOverride, true);
                if (last.Ok)
                    return last;
                Log("Attempt " + attempt + " failed: " + last.Message);
                // Auth failures will not succeed on retry with the same password.
                if (LooksLikeAuthFailure(last.Message))
                {
                    Log("Auth failure — skipping further retries.");
                    return last;
                }
                if (attempt < 3)
                {
                    var wait = BackoffSeconds[attempt - 1];
                    Log("Waiting " + wait + "s before retry...");
                    if (ct.WaitHandle.WaitOne(wait * 1000))
                        return Fail("Cancelled");
                }
            }
            return last;
        }

        private static bool LooksLikeAuthFailure(string message)
        {
            if (string.IsNullOrEmpty(message)) return false;
            var m = message.ToLowerInvariant();
            return m.IndexOf("401", StringComparison.Ordinal) >= 0
                || m.IndexOf("403", StringComparison.Ordinal) >= 0
                || m.IndexOf("unauthorized", StringComparison.Ordinal) >= 0
                || m.IndexOf("forbidden", StringComparison.Ordinal) >= 0
                || m.IndexOf("invalid credentials", StringComparison.Ordinal) >= 0
                || m.IndexOf("invalid credential", StringComparison.Ordinal) >= 0
                || m.IndexOf("authentication", StringComparison.Ordinal) >= 0
                || m.IndexOf("access denied", StringComparison.Ordinal) >= 0
                || m.IndexOf("login failed", StringComparison.Ordinal) >= 0
                || m.IndexOf("bad credentials", StringComparison.Ordinal) >= 0;
        }

        /// <summary>WebEAM token check only — never calls Register.</summary>
        public static RenewResult HealthOnly(AppSettings settings)
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                var email = (settings.Email ?? string.Empty).Trim();
                var password = settings.GetPassword();
                if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
                    return Fail("Email or password missing.");

                var configPath = settings.ConfigPath;
                if (string.IsNullOrWhiteSpace(configPath))
                    configPath = @"C:\ProgramData\BMW\ISPI\config\KAI\KAI";

                var propsPath = Path.Combine(configPath, "KAI_Properties.cfg");
                var urls = KaiZentralUserRegProc.GetKaiZentralURLs(configPath);
                string healthMsg;
                int expiresIn;
                if (TryHealthToken(settings, propsPath, ResolveWebEamUrl(settings, urls.WenAccessManagerAddress), out healthMsg, out expiresIn))
                {
                    ApplyTokenExpiry(settings, expiresIn);
                    var ok = new RenewResult();
                    ok.Ok = true;
                    ok.SkippedRegister = true;
                    ok.Message = healthMsg;
                    return ok;
                }
                return Fail("Health failed (no Register): " + healthMsg);
            }
            catch (Exception ex)
            {
                return Fail(Unwrap(ex));
            }
        }

        private static RenewResult RunOnce(
            AppSettings settings,
            bool forceRegister = false,
            string passwordOverride = null,
            bool allowRegister = true)
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                var email = (settings.Email ?? string.Empty).Trim();
                var password = !string.IsNullOrEmpty(passwordOverride)
                    ? passwordOverride
                    : settings.GetPassword();
                if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
                    return Fail("Email or password missing.");

                var configPath = settings.ConfigPath;
                if (string.IsNullOrWhiteSpace(configPath))
                    configPath = @"C:\ProgramData\BMW\ISPI\config\KAI\KAI";
                if (!Directory.Exists(configPath))
                    Directory.CreateDirectory(configPath);

                var propsPath = Path.Combine(configPath, "KAI_Properties.cfg");
                var urls = KaiZentralUserRegProc.GetKaiZentralURLs(configPath);

                // Health: if existing ClientId/Secret still get a token, skip Register —
                // unless credentials just changed (forceRegister) and must be verified.
                string healthMsg = null;
                int expiresIn = 0;
                if (!forceRegister
                    && TryHealthToken(settings, propsPath, ResolveWebEamUrl(settings, urls.WenAccessManagerAddress), out healthMsg, out expiresIn))
                {
                    settings.LastSuccessUtc = DateTime.UtcNow;
                    settings.LastMessage = healthMsg;
                    ApplyTokenExpiry(settings, expiresIn);
                    var ok = new RenewResult();
                    ok.Ok = true;
                    ok.SkippedRegister = true;
                    ok.Message = healthMsg;
                    return ok;
                }
                if (!allowRegister)
                {
                    Log("Register blocked (auth cooldown). Health was not OK.");
                    return Fail(string.IsNullOrEmpty(healthMsg)
                        ? "Auth cooldown — Register skipped; Health failed."
                        : "Auth cooldown — Register skipped. " + healthMsg);
                }
                if (forceRegister)
                    Log("Credentials changed — forcing Register (skip Health short-circuit)...");
                else
                    Log("Health check failed: " + healthMsg + " — running Register...");
                Log("Register password length=" + password.Length);

                if (File.Exists(propsPath))
                    File.Copy(propsPath, propsPath + ".bak_before_renew", true);

                KaiCentralCredentials creds;
                var registerUrl = string.IsNullOrWhiteSpace(settings.RegisterUrl)
                    ? urls.RegisterAddress
                    : settings.RegisterUrl;
                try
                {
                    var uri = new Uri(registerUrl);
                    creds = KaiZentralUserRegProc.RegisterWithKaiZentral(uri, email, password, ResolveWebProxy(settings.UseSystemProxy));
                    BmwHttpLog.LogExchange("Register", 200, registerUrl,
                        BmwHttpLog.FormatRegisterRequest(registerUrl, email),
                        "RegisterWithKaiZentral OK (ClientId=" + (creds != null ? (creds.ClientId ?? "") : "") + ")",
                        true);
                }
                catch (Exception ex)
                {
                    int regStatus;
                    string regBody;
                    ParseHttpFromException(ex, out regStatus, out regBody);
                    var regFail = regStatus != 0
                        ? "Register failed: HTTP " + regStatus + " " + regBody
                        : "Register failed: " + Unwrap(ex);
                    if (regStatus != 0 && regStatus != 200)
                    {
                        BmwHttpLog.LogNon200("Register", regStatus, registerUrl,
                            BmwHttpLog.FormatRegisterRequest(registerUrl, email), regBody, true);
                    }
                    if (forceRegister && LooksLikeAuthFailure(regFail))
                    {
                        Log(regFail);
                        Log("Force Register auth failure — trying Health with existing ClientId/Secret...");
                        string fallbackMsg;
                        int fallbackExpires;
                        if (TryHealthToken(settings, propsPath, ResolveWebEamUrl(settings, urls.WenAccessManagerAddress),
                            out fallbackMsg, out fallbackExpires))
                        {
                            settings.LastSuccessUtc = DateTime.UtcNow;
                            settings.LastMessage = fallbackMsg;
                            settings.LastRegisterFailUtc = DateTime.UtcNow;
                            ApplyTokenExpiry(settings, fallbackExpires);
                            var soft = new RenewResult();
                            soft.Ok = true;
                            soft.SkippedRegister = true;
                            soft.RegisterAttempted = true;
                            soft.RegisterAuthRejected = true;
                            soft.Message = "Password saved. Register rejected (auth); existing WebEAM token still valid. " + fallbackMsg;
                            return soft;
                        }
                    }
                    var failReg = Fail(regFail);
                    failReg.RegisterAttempted = true;
                    settings.LastRegisterFailUtc = DateTime.UtcNow;
                    return failReg;
                }

                if (creds == null ||
                    string.IsNullOrEmpty(creds.ClientId) ||
                    string.IsNullOrEmpty(creds.ClientSecret))
                {
                    var empty = Fail("Register returned empty credentials.");
                    empty.RegisterAttempted = true;
                    settings.LastRegisterFailUtc = DateTime.UtcNow;
                    return empty;
                }

                // UI Language is for app UI only — Register always uses English.
                KaiZentralUserRegProc.SaveBdrRegistration("en", creds, configPath);

                if (!File.Exists(propsPath))
                {
                    var missing = Fail("KAI_Properties.cfg was not created.");
                    missing.RegisterAttempted = true;
                    settings.LastRegisterFailUtc = DateTime.UtcNow;
                    return missing;
                }

                var lines = File.ReadAllLines(propsPath);
                if (lines.Length < 4)
                {
                    var bad = Fail("KAI_Properties.cfg invalid (too few lines).");
                    bad.RegisterAttempted = true;
                    settings.LastRegisterFailUtc = DateTime.UtcNow;
                    return bad;
                }

                settings.LastSuccessUtc = DateTime.UtcNow;
                settings.LastRegisterOkUtc = DateTime.UtcNow;
                settings.LastRegisterFailUtc = null;
                settings.LastMessage = "OK ClientId=" + creds.ClientId;
                settings.Save();

                var result = new RenewResult();
                result.Ok = true;
                result.RegisterAttempted = true;
                result.ClientId = creds.ClientId;
                result.IdentificationHash = creds.IdentificationHash;
                result.Message = "Registration OK. KAI_Properties.cfg updated. ClientId=" + creds.ClientId;
                return result;
            }
            catch (Exception ex)
            {
                return Fail(Unwrap(ex));
            }
        }

        private static void ApplyTokenExpiry(AppSettings settings, int expiresInSeconds)
        {
            if (expiresInSeconds > 0)
            {
                settings.LastTokenExpiresInSeconds = expiresInSeconds;
                settings.LastTokenExpiryUtc = DateTime.UtcNow.AddSeconds(expiresInSeconds);
            }
            settings.Save();
        }

        private static WebProxy ResolveWebProxy(bool useSystemProxy)
        {
            if (!useSystemProxy) return null;
            try
            {
                var p = WebRequest.DefaultWebProxy as WebProxy;
                if (p != null) return p;
#pragma warning disable 618
                return WebProxy.GetDefaultProxy();
#pragma warning restore 618
            }
            catch
            {
                return null;
            }
        }

        private static string ResolveWebEamUrl(AppSettings settings, string fromConfig)
        {
            if (settings != null && !string.IsNullOrWhiteSpace(settings.WebEamUrl))
                return settings.WebEamUrl.Trim();
            return fromConfig;
        }

        private static bool TryHealthToken(AppSettings settings, string propsPath, string tokenUrl, out string message, out int expiresInSeconds)
        {
            message = null;
            expiresInSeconds = 0;
            try
            {
                if (!File.Exists(propsPath))
                {
                    message = "KAI_Properties.cfg missing";
                    return false;
                }
                var lines = File.ReadAllLines(propsPath);
                if (lines.Length < 4)
                {
                    message = "KAI_Properties.cfg invalid";
                    return false;
                }

                var clientId = BmwDecrypt(lines[2].Trim());
                var clientSecret = BmwDecrypt(lines[3].Trim());
                if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
                {
                    message = "Decrypt ClientId/Secret failed";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(tokenUrl))
                    tokenUrl = "https://auth.bmwgroup.com/auth/oauth2/realms/root/realms/machine2machine/access_token";

                var requestDump = BmwHttpLog.FormatWebEamRequest(tokenUrl, clientId);
                // Same as KaiZentralProxyUserRegistration.GetM2mAccessToken
                const string body = "grant_type=client_credentials&scope=machine2machine";
                var req = (HttpWebRequest)WebRequest.Create(tokenUrl);
                req.Method = "POST";
                req.Accept = "application/json";
                req.ContentType = "application/x-www-form-urlencoded";
                req.Timeout = 20000;
                req.Proxy = KaiDiag.ResolveProxy(settings != null && settings.UseSystemProxy);
                var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes(clientId + ":" + clientSecret));
                req.Headers[HttpRequestHeader.Authorization] = "Basic " + basic;
                var bytes = Encoding.UTF8.GetBytes(body);
                req.ContentLength = bytes.Length;
                using (var s = req.GetRequestStream())
                    s.Write(bytes, 0, bytes.Length);

                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream()))
                {
                    var json = sr.ReadToEnd() ?? "";
                    var status = (int)resp.StatusCode;
                    if (status >= 200 && status < 300
                        && json.IndexOf("access_token", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        expiresInSeconds = ParseExpiresIn(json);
                        message = "Health OK (WebEAM token). Register not required. ClientId=" + clientId;
                        if (expiresInSeconds > 0)
                            message += " expires_in=" + expiresInSeconds + "s";
                        BmwHttpLog.LogExchange("WebEAM", status, tokenUrl, requestDump, json);
                        return true;
                    }
                    message = "Token response without access_token HTTP " + status;
                    if (status != 200)
                        BmwHttpLog.LogNon200("WebEAM", status, tokenUrl, requestDump, json);
                    else
                        BmwHttpLog.LogExchange("WebEAM", status, tokenUrl, requestDump, json);
                    return false;
                }
            }
            catch (WebException we)
            {
                int status = 0;
                string respBody = "";
                string url = tokenUrl;
                string clientIdForDump = null;
                try
                {
                    if (File.Exists(propsPath))
                    {
                        var lines = File.ReadAllLines(propsPath);
                        if (lines.Length >= 3)
                            clientIdForDump = BmwDecrypt(lines[2].Trim());
                    }
                }
                catch { }
                try
                {
                    var resp = we.Response as HttpWebResponse;
                    if (resp != null)
                    {
                        status = (int)resp.StatusCode;
                        using (var sr = new StreamReader(resp.GetResponseStream() ?? Stream.Null))
                            respBody = sr.ReadToEnd() ?? "";
                    }
                }
                catch { }
                message = Unwrap(we);
                if (status != 0 && status != 200)
                {
                    BmwHttpLog.LogNon200("WebEAM", status, url,
                        BmwHttpLog.FormatWebEamRequest(url, clientIdForDump), respBody);
                }
                return false;
            }
            catch (Exception ex)
            {
                message = Unwrap(ex);
                return false;
            }
        }

        private static int ParseExpiresIn(string json)
        {
            try
            {
                const string key = "\"expires_in\"";
                var i = json.IndexOf(key, StringComparison.OrdinalIgnoreCase);
                if (i < 0) return 0;
                i = json.IndexOf(':', i + key.Length);
                if (i < 0) return 0;
                i++;
                while (i < json.Length && (json[i] == ' ' || json[i] == '\t')) i++;
                var start = i;
                while (i < json.Length && char.IsDigit(json[i])) i++;
                if (i == start) return 0;
                int value;
                if (int.TryParse(json.Substring(start, i - start), out value))
                    return value;
            }
            catch { }
            return 0;
        }

        private static string BmwDecrypt(string cipher)
        {
            try
            {
                var t = Type.GetType(
                    "BMW.ISPI.KAI.Decentral.KaiAdminLibData.Encryption.Encryption, BMW.ISPI.KAI.Decentral.KaiAdminLibData",
                    false);
                if (t == null)
                {
                    foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        t = a.GetType("BMW.ISPI.KAI.Decentral.KaiAdminLibData.Encryption.Encryption");
                        if (t != null) break;
                    }
                }
                if (t == null) return null;
                var m = t.GetMethod("Decrypt", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (m == null) return null;
                return m.Invoke(null, new object[] { cipher }) as string;
            }
            catch
            {
                return null;
            }
        }

        private static RenewResult Fail(string message)
        {
            var r = new RenewResult();
            r.Ok = false;
            r.Message = message;
            return r;
        }

        private static void ParseHttpFromException(Exception ex, out int status, out string body)
        {
            status = 0;
            body = "";
            while (ex != null)
            {
                var we = ex as WebException;
                if (we != null && we.Response != null)
                {
                    try
                    {
                        var resp = we.Response as HttpWebResponse;
                        if (resp != null) status = (int)resp.StatusCode;
                        using (var sr = new StreamReader(we.Response.GetResponseStream() ?? Stream.Null))
                            body = sr.ReadToEnd() ?? "";
                        return;
                    }
                    catch { }
                }
                ex = ex.InnerException;
            }
        }

        private static string Unwrap(Exception ex)
        {
            while (ex.InnerException != null)
                ex = ex.InnerException;
            var we = ex as WebException;
            if (we != null && we.Response != null)
            {
                try
                {
                    using (var sr = new StreamReader(we.Response.GetResponseStream()))
                    {
                        var body = sr.ReadToEnd();
                        var resp = we.Response as HttpWebResponse;
                        var code = resp != null ? ((int)resp.StatusCode).ToString() : "?";
                        return "HTTP " + code + " " + body;
                    }
                }
                catch { }
            }
            return ex.Message;
        }

        public static void Log(string line)
        {
            lock (LogGate)
            {
                try
                {
                    RotateLogIfNeededUnlocked();
                    var msg = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + line + Environment.NewLine;
                    File.AppendAllText(AppSettings.LogPath, msg, Encoding.UTF8);
                }
                catch { }
            }
        }

        private static void RotateLogIfNeededUnlocked()
        {
            try
            {
                var path = AppSettings.LogPath;
                if (!File.Exists(path)) return;
                var fi = new FileInfo(path);
                if (fi.Length < 10L * 1024 * 1024) return; // 10 MB
                var bak = path + ".1";
                if (File.Exists(bak))
                    File.Delete(bak);
                File.Move(path, bak);
            }
            catch { }
        }
    }
}
