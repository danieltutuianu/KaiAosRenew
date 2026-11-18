using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using BMW.ISPI.KAI.Decentral.KaiAdminLibData.KaiZentral;

namespace KaiAosRenew
{
    /// <summary>
    /// One-shot probe of Zentral UploadKeyData so we can log the real HTTP body (KAI logs only "403 Forbidden").
    /// </summary>
    internal static class KeyUploadProbe
    {
        private const string ImplDllName = "BMW.ISPI.KAI.Decentral.KaiServices.Impl.dll";
        private const string ProxyTypeName =
            "BMW.ISPI.KAI.Decentral.KaiServices.KaiZentral.KaiZentralProxyUserRegistration";
        private const int MaxBodyRetain = 262144;

        public sealed class ProbeResult
        {
            public bool Ok { get; set; }
            public string Summary { get; set; }
            public int HttpStatus { get; set; }
            public string Body { get; set; }
            public string UploadUrl { get; set; }
            public string RequestDump { get; set; }
            public string Operation { get; set; }
        }

        public static ProbeResult ProbeTransferredBin(AppSettings settings, string readerIdHint)
        {
            return ProbeTransferredBin(settings, readerIdHint, null);
        }

        public static ProbeResult ProbeTransferredBin(
            AppSettings settings, string readerIdHint, string hardwareIdHint)
        {
            var result = new ProbeResult();
            result.Operation = "KeyUpload";
            string uploadUrl = null;
            string requestDump = null;
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                if (!File.Exists(KeyActivityMonitor.TransferredBin))
                {
                    result.Summary = "Transferred keyreader.bin missing — cannot probe BMW upload.";
                    return result;
                }

                var configPath = settings != null && !string.IsNullOrWhiteSpace(settings.ConfigPath)
                    ? settings.ConfigPath.Trim()
                    : @"C:\ProgramData\BMW\ISPI\config\KAI\KAI";
                var propsPath = Path.Combine(configPath, "KAI_Properties.cfg");
                if (!File.Exists(propsPath))
                {
                    result.Summary = "KAI_Properties.cfg missing.";
                    return result;
                }

                var urls = KaiZentralUserRegProc.GetKaiZentralURLs(configPath);
                uploadUrl = urls != null ? urls.KeyDataUploadAddress : null;
                result.UploadUrl = uploadUrl;
                if (string.IsNullOrWhiteSpace(uploadUrl))
                {
                    result.Summary = "Key upload URL missing from KAI config.";
                    return result;
                }

                string token;
                KaiCentralCredentials creds;
                string tokenErr;
                int tokenStatus;
                string tokenBody;
                string tokenReq;
                var tokenUrl = ResolveWebEamUrl(settings, urls != null ? urls.WenAccessManagerAddress : null);
                if (!TryGetM2mTokenAndCreds(settings, propsPath, tokenUrl,
                    out token, out creds, out tokenErr, out tokenStatus, out tokenBody, out tokenReq))
                {
                    result.Summary = "M2M token failed: " + tokenErr;
                    result.Operation = "WebEAM";
                    result.UploadUrl = tokenUrl;
                    result.RequestDump = tokenReq;
                    result.HttpStatus = tokenStatus;
                    result.Body = CapBody(tokenBody);
                    if (tokenStatus != 0 && tokenStatus != 200)
                    {
                        BmwHttpLog.LogNon200("WebEAM", tokenStatus, tokenUrl, tokenReq, result.Body);
                    }
                    return result;
                }

                var bin = File.ReadAllBytes(KeyActivityMonitor.TransferredBin);
                var readerId = string.IsNullOrWhiteSpace(readerIdHint) ? "unknown" : readerIdHint.Trim();
                var hwId = ResolveHardwareId(hardwareIdHint, readerId, settings);
                var kd = new KaiZentralKeyData();
                kd.Language = "en";
                kd.KeyDataFile = "K " + Convert.ToBase64String(bin);
                kd.KeyReadDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                kd.KeyReaderId = readerId;
                kd.KeyReaderHardwareId = hwId;
                kd.OriginCodeType = "KIS";

                requestDump = BmwHttpLog.FormatKeyUploadRequest(
                    "POST", uploadUrl, kd.OriginCodeType, kd.KeyReaderId, kd.KeyReaderHardwareId,
                    kd.KeyReadDate, kd.Language, bin.Length);
                result.RequestDump = requestDump;

                AppDomain.CurrentDomain.AssemblyResolve -= ResolveKaiAsm;
                AppDomain.CurrentDomain.AssemblyResolve += ResolveKaiAsm;

                var implPath = FindImplDll();
                if (string.IsNullOrEmpty(implPath))
                {
                    result.Summary = "KaiServices.Impl.dll not found under BMW ISPI KAI.";
                    return result;
                }

                var asm = Assembly.LoadFrom(implPath);
                var typ = ResolveProxyType(asm);
                if (typ == null)
                {
                    result.Summary = "KaiZentralProxyUserRegistration type missing.";
                    return result;
                }
                MethodInfo method;
                string methodErr;
                if (!TryFindUploadKeyData(typ, out method, out methodErr))
                {
                    result.Summary = methodErr;
                    return result;
                }

                object target = null;
                if (!method.IsStatic)
                    target = Activator.CreateInstance(typ);
                method.Invoke(target, new object[] { new Uri(uploadUrl), creds, token, kd });
                result.Ok = true;
                result.HttpStatus = 200;
                result.Summary = "Probe UploadKeyData OK (BMW accepted).";
                result.Body = "(UploadKeyData returned without exception)";
                BmwHttpLog.LogExchange("KeyUpload", 200, uploadUrl, requestDump, result.Body);
                return result;
            }
            catch (TargetInvocationException tie)
            {
                return FromException(tie.InnerException ?? tie, uploadUrl, requestDump);
            }
            catch (Exception ex)
            {
                return FromException(ex, uploadUrl, requestDump);
            }
        }

        private static string ResolveHardwareId(string hardwareIdHint, string readerId, AppSettings settings)
        {
            if (!string.IsNullOrWhiteSpace(hardwareIdHint))
                return hardwareIdHint.Trim();
            try
            {
                var sqlite = settings != null && !string.IsNullOrWhiteSpace(settings.KeysSqlitePath)
                    ? settings.KeysSqlitePath.Trim()
                    : KeyActivityMonitor.DefaultSqlitePath;
                if (File.Exists(sqlite))
                {
                    var cs = "Data Source=" + sqlite + ";Version=3;Read Only=True;FailIfMissing=True";
                    using (var conn = new System.Data.SQLite.SQLiteConnection(cs))
                    {
                        conn.Open();
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText =
                                "SELECT HwAddress FROM KeyReaderEntity ORDER BY KeyReaderPk LIMIT 1";
                            var v = cmd.ExecuteScalar() as string;
                            if (!string.IsNullOrWhiteSpace(v))
                                return v.Trim();
                        }
                    }
                }
            }
            catch { }
            return string.IsNullOrWhiteSpace(readerId) ? "unknown" : readerId.Trim();
        }

        private static ProbeResult FromException(Exception ex, string uploadUrl, string requestDump)
        {
            var result = new ProbeResult();
            result.Operation = "KeyUpload";
            result.UploadUrl = uploadUrl;
            result.RequestDump = requestDump;
            while (ex is TargetInvocationException && ex.InnerException != null)
                ex = ex.InnerException;
            while (ex.InnerException != null
                   && !(ex is WebException)
                   && ex.InnerException is WebException)
                ex = ex.InnerException;

            var we = ex as WebException;
            if (we == null && ex.InnerException is WebException)
                we = (WebException)ex.InnerException;

            if (we != null && we.Response != null)
            {
                try
                {
                    var resp = we.Response as HttpWebResponse;
                    result.HttpStatus = resp != null ? (int)resp.StatusCode : 0;
                    using (var sr = new StreamReader(we.Response.GetResponseStream() ?? Stream.Null, Encoding.UTF8))
                        result.Body = CapBody(sr.ReadToEnd() ?? "");
                    result.Summary = "HTTP " + result.HttpStatus + " " + TruncateOneLine(result.Body, 400);
                    if (result.HttpStatus != 200)
                    {
                        BmwHttpLog.LogNon200("KeyUpload", result.HttpStatus, uploadUrl,
                            requestDump, result.Body);
                    }
                    return result;
                }
                catch
                {
                    // fall through
                }
            }

            result.Summary = UnwrapMessage(ex);
            return result;
        }

        private static string CapBody(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            if (s.Length <= MaxBodyRetain) return s;
            return s.Substring(0, MaxBodyRetain);
        }

        private static string TruncateOneLine(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (s.Length > max) return s.Substring(0, max - 3) + "...";
            return s;
        }

        private static string UnwrapMessage(Exception ex)
        {
            while (ex.InnerException != null)
                ex = ex.InnerException;
            return ex.Message ?? ex.ToString();
        }

        private static Assembly ResolveKaiAsm(object sender, ResolveEventArgs args)
        {
            try
            {
                var name = new AssemblyName(args.Name).Name + ".dll";
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "BMW", "ISPI", "KAI", "KAI");
                var path = Path.Combine(dir, name);
                if (File.Exists(path))
                    return Assembly.LoadFrom(path);
            }
            catch { }
            return null;
        }

        private static string FindImplDll()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "BMW", "ISPI", "KAI", "KAI");
            var path = Path.Combine(dir, ImplDllName);
            return File.Exists(path) ? path : null;
        }

        private static Type ResolveProxyType(Assembly asm)
        {
            if (asm == null) return null;
            var typ = asm.GetType(ProxyTypeName, false);
            if (typ != null) return typ;
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException rtle) { types = rtle.Types; }
            catch { return null; }
            if (types == null) return null;
            Type fallback = null;
            for (var i = 0; i < types.Length; i++)
            {
                var t = types[i];
                if (t == null || !t.IsClass) continue;
                var n = t.FullName ?? t.Name ?? "";
                if (n.IndexOf("KaiZentral", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (n.IndexOf("Proxy", StringComparison.OrdinalIgnoreCase) < 0
                    && n.IndexOf("Upload", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (string.Equals(n, ProxyTypeName, StringComparison.OrdinalIgnoreCase))
                    return t;
                if (fallback == null) fallback = t;
            }
            return fallback;
        }

        private static bool TryFindUploadKeyData(Type typ, out MethodInfo method, out string error)
        {
            method = null;
            error = null;
            if (typ == null)
            {
                error = "UploadKeyData method missing.";
                return false;
            }
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            var preferred = typ.GetMethod(
                "UploadKeyData",
                flags,
                null,
                new[] { typeof(Uri), typeof(KaiCentralCredentials), typeof(string), typeof(KaiZentralKeyData) },
                null);
            if (preferred != null)
            {
                method = preferred;
                return true;
            }
            MethodInfo byName = null;
            MethodInfo fuzzy = null;
            MethodInfo[] all;
            try { all = typ.GetMethods(flags); }
            catch
            {
                error = "UploadKeyData method missing.";
                return false;
            }
            var relevant = new List<string>();
            for (var i = 0; i < all.Length; i++)
            {
                var m = all[i];
                if (m == null || m.IsSpecialName) continue;
                var name = m.Name ?? "";
                var lower = name.ToLowerInvariant();
                if (lower.IndexOf("upload", StringComparison.Ordinal) >= 0
                    || lower.IndexOf("key", StringComparison.Ordinal) >= 0)
                {
                    if (relevant.Count < 12)
                        relevant.Add(name);
                }
                if (string.Equals(name, "UploadKeyData", StringComparison.OrdinalIgnoreCase))
                {
                    if (byName == null) byName = m;
                    continue;
                }
                if (fuzzy == null
                    && lower.IndexOf("upload", StringComparison.Ordinal) >= 0
                    && lower.IndexOf("key", StringComparison.Ordinal) >= 0)
                    fuzzy = m;
            }
            method = byName ?? fuzzy;
            if (method != null) return true;
            var listed = relevant.Count > 0 ? string.Join(", ", relevant.ToArray()) : "(none)";
            error = "UploadKeyData method missing (methods: " + listed + ").";
            return false;
        }

        private static string ResolveWebEamUrl(AppSettings settings, string fromConfig)
        {
            if (settings != null && !string.IsNullOrWhiteSpace(settings.WebEamUrl))
                return settings.WebEamUrl.Trim();
            return fromConfig;
        }

        private static bool TryGetM2mTokenAndCreds(
            AppSettings settings,
            string propsPath,
            string tokenUrl,
            out string accessToken,
            out KaiCentralCredentials creds,
            out string error,
            out int httpStatus,
            out string responseBody,
            out string requestDump)
        {
            accessToken = null;
            creds = null;
            error = null;
            httpStatus = 0;
            responseBody = null;
            requestDump = null;
            string clientId = null;
            try
            {
                var lines = File.ReadAllLines(propsPath);
                if (lines.Length < 4)
                {
                    error = "KAI_Properties.cfg invalid";
                    return false;
                }

                clientId = BmwDecrypt(lines[2].Trim());
                var clientSecret = BmwDecrypt(lines[3].Trim());
                var hash = lines.Length > 1 ? BmwDecrypt(lines[1].Trim()) : null;
                if (string.IsNullOrEmpty(hash))
                    hash = BmwDecrypt(lines[0].Trim());
                if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
                {
                    error = "Decrypt ClientId/Secret failed";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(tokenUrl))
                    tokenUrl = "https://auth.bmwgroup.com/auth/oauth2/realms/root/realms/machine2machine/access_token";

                requestDump = BmwHttpLog.FormatWebEamRequest(tokenUrl, clientId);
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
                using (var sr = new StreamReader(resp.GetResponseStream() ?? Stream.Null))
                {
                    httpStatus = (int)resp.StatusCode;
                    responseBody = CapBody(sr.ReadToEnd() ?? "");
                    accessToken = ParseJsonString(responseBody, "access_token");
                    if (string.IsNullOrEmpty(accessToken))
                    {
                        error = "Token response without access_token HTTP " + httpStatus;
                        return false;
                    }
                    if (httpStatus != 200)
                    {
                        error = "Token HTTP " + httpStatus;
                        return false;
                    }
                    BmwHttpLog.LogExchange("WebEAM", 200, tokenUrl, requestDump, responseBody);
                }

                creds = new KaiCentralCredentials();
                creds.ClientId = clientId;
                creds.ClientSecret = clientSecret;
                creds.IdentificationHash = hash ?? "";
                return true;
            }
            catch (WebException we)
            {
                try
                {
                    var resp = we.Response as HttpWebResponse;
                    if (resp != null)
                    {
                        httpStatus = (int)resp.StatusCode;
                        using (var sr = new StreamReader(resp.GetResponseStream() ?? Stream.Null, Encoding.UTF8))
                            responseBody = CapBody(sr.ReadToEnd() ?? "");
                    }
                }
                catch { }
                if (string.IsNullOrEmpty(requestDump))
                    requestDump = BmwHttpLog.FormatWebEamRequest(tokenUrl, clientId);
                error = UnwrapMessage(we);
                return false;
            }
            catch (Exception ex)
            {
                if (string.IsNullOrEmpty(requestDump))
                    requestDump = BmwHttpLog.FormatWebEamRequest(tokenUrl, clientId);
                error = UnwrapMessage(ex);
                return false;
            }
        }

        private static string ParseJsonString(string json, string key)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return null;
            var needle = "\"" + key + "\"";
            var i = json.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            i = json.IndexOf(':', i + needle.Length);
            if (i < 0) return null;
            i = json.IndexOf('"', i + 1);
            if (i < 0) return null;
            var j = i + 1;
            var sb = new StringBuilder();
            while (j < json.Length)
            {
                var c = json[j];
                if (c == '\\' && j + 1 < json.Length)
                {
                    sb.Append(json[j + 1]);
                    j += 2;
                    continue;
                }
                if (c == '"') break;
                sb.Append(c);
                j++;
            }
            return sb.Length > 0 ? sb.ToString() : null;
        }

        private static string BmwDecrypt(string cipher)
        {
            try
            {
                Type t = null;
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    t = a.GetType("BMW.ISPI.KAI.Decentral.KaiAdminLibData.Encryption.Encryption");
                    if (t != null) break;
                }
                if (t == null)
                {
                    t = Type.GetType(
                        "BMW.ISPI.KAI.Decentral.KaiAdminLibData.Encryption.Encryption, BMW.ISPI.KAI.Decentral.KaiAdminLibData",
                        false);
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
    }
}
