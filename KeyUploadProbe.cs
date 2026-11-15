using System;
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

        public sealed class ProbeResult
        {
            public bool Ok { get; set; }
            public string Summary { get; set; }
            public int HttpStatus { get; set; }
            public string Body { get; set; }
        }

        public static ProbeResult ProbeTransferredBin(AppSettings settings, string readerIdHint)
        {
            var result = new ProbeResult();
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
                var uploadUrl = urls != null ? urls.KeyDataUploadAddress : null;
                if (string.IsNullOrWhiteSpace(uploadUrl))
                {
                    result.Summary = "Key upload URL missing from KAI config.";
                    return result;
                }

                string token;
                KaiCentralCredentials creds;
                string tokenErr;
                if (!TryGetM2mTokenAndCreds(settings, propsPath,
                    ResolveWebEamUrl(settings, urls.WenAccessManagerAddress),
                    out token, out creds, out tokenErr))
                {
                    result.Summary = "M2M token failed: " + tokenErr;
                    return result;
                }

                var bin = File.ReadAllBytes(KeyActivityMonitor.TransferredBin);
                var kd = new KaiZentralKeyData();
                kd.Language = "en";
                kd.KeyDataFile = "K " + Convert.ToBase64String(bin);
                kd.KeyReadDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                kd.KeyReaderId = string.IsNullOrWhiteSpace(readerIdHint) ? "unknown" : readerIdHint.Trim();
                kd.KeyReaderHardwareId = "";
                kd.OriginCodeType = "KeyReaderPlus";

                AppDomain.CurrentDomain.AssemblyResolve -= ResolveKaiAsm;
                AppDomain.CurrentDomain.AssemblyResolve += ResolveKaiAsm;

                var implPath = FindImplDll();
                if (string.IsNullOrEmpty(implPath))
                {
                    result.Summary = "KaiServices.Impl.dll not found under BMW ISPI KAI.";
                    return result;
                }

                var asm = Assembly.LoadFrom(implPath);
                var typ = asm.GetType(ProxyTypeName, false);
                if (typ == null)
                {
                    result.Summary = "KaiZentralProxyUserRegistration type missing.";
                    return result;
                }
                var method = typ.GetMethod("UploadKeyData", BindingFlags.Public | BindingFlags.Instance);
                if (method == null)
                {
                    result.Summary = "UploadKeyData method missing.";
                    return result;
                }

                var proxy = Activator.CreateInstance(typ);
                method.Invoke(proxy, new object[] { new Uri(uploadUrl), creds, token, kd });
                result.Ok = true;
                result.HttpStatus = 200;
                result.Summary = "Probe UploadKeyData OK (BMW accepted).";
                result.Body = "";
                return result;
            }
            catch (TargetInvocationException tie)
            {
                return FromException(tie.InnerException ?? tie);
            }
            catch (Exception ex)
            {
                return FromException(ex);
            }
        }

        private static ProbeResult FromException(Exception ex)
        {
            var result = new ProbeResult();
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
                        result.Body = sr.ReadToEnd() ?? "";
                    if (result.Body.Length > 2048)
                        result.Body = result.Body.Substring(0, 2048) + "...";
                    result.Summary = "HTTP " + result.HttpStatus + " " + TruncateOneLine(result.Body, 400);
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
            out string error)
        {
            accessToken = null;
            creds = null;
            error = null;
            try
            {
                var lines = File.ReadAllLines(propsPath);
                if (lines.Length < 4)
                {
                    error = "KAI_Properties.cfg invalid";
                    return false;
                }

                var clientId = BmwDecrypt(lines[2].Trim());
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
                    var json = sr.ReadToEnd();
                    accessToken = ParseJsonString(json, "access_token");
                    if (string.IsNullOrEmpty(accessToken))
                    {
                        error = "Token response without access_token HTTP " + (int)resp.StatusCode;
                        return false;
                    }
                }

                creds = new KaiCentralCredentials();
                creds.ClientId = clientId;
                creds.ClientSecret = clientSecret;
                creds.IdentificationHash = hash ?? "";
                return true;
            }
            catch (Exception ex)
            {
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
