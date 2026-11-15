using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using BMW.ISPI.KAI.Decentral.KaiAdminLibData;
using BMW.ISPI.KAI.Decentral.KaiAdminLibData.KaiZentral;

namespace KaiAosRenew
{
    internal sealed class KaiInstallInfo
    {
        public bool Exists { get; set; }
        public int Port { get; set; }
        public bool LiteMode { get; set; }
        public string FilePath { get; set; }
        public string Summary { get; set; }
    }

    internal sealed class KaiUrlInfo
    {
        public string Register { get; set; }
        public string WebEam { get; set; }
        public string KeyUpload { get; set; }
    }

    internal sealed class KaiStatusInfo
    {
        public bool Ok { get; set; }
        public string Message { get; set; }
        public string KaiVersion { get; set; }
        public string ApplicationState { get; set; }
        public string AmountKeyReaderPlus { get; set; }
        public string KeysToday { get; set; }
        public string KeysLastWeek { get; set; }
        public string Raw { get; set; }
    }

    internal sealed class KaiCommonConfigInfo
    {
        public bool Ok { get; set; }
        public string Message { get; set; }
        public string Language { get; set; }
        public string UpdateDuringStartKAI { get; set; }
        public string UpdateDuringNightKAI { get; set; }
        public string UpdateKeyReaderPlusFirmware { get; set; }
        public string LastModifier { get; set; }
        public string Raw { get; set; }
    }

    internal sealed class KaiLocalHealthInfo
    {
        public bool Ok { get; set; }
        public string Message { get; set; }
        public string Raw { get; set; }
        public int HttpStatus { get; set; }
    }

    internal static class KaiDiag
    {
        public static string NormalizeConfigPath(string configPath)
        {
            if (string.IsNullOrWhiteSpace(configPath))
                return @"C:\ProgramData\BMW\ISPI\config\KAI\KAI";
            return configPath.Trim();
        }

        public static string InstallationFile(string configPath)
        {
            return Path.Combine(NormalizeConfigPath(configPath), "KAIInstallation.cfg");
        }

        public static KaiInstallInfo ReadInstallation(string configPath)
        {
            var info = new KaiInstallInfo();
            info.FilePath = InstallationFile(configPath);
            try
            {
                info.Exists = KaiInstallation.KaiInstallationExists(info.FilePath);
                if (!info.Exists)
                {
                    info.Summary = "KAI not installed (KAIInstallation.cfg missing)";
                    return info;
                }
                info.Port = KaiInstallation.GetServicePort(info.FilePath);
                info.LiteMode = KaiInstallation.IsKaiLiteMode(info.FilePath);
                info.Summary = "Installed | port " + info.Port + " | Lite: " + (info.LiteMode ? "yes" : "no");
            }
            catch (Exception ex)
            {
                info.Exists = false;
                info.Summary = "Install read failed: " + ex.Message;
            }
            return info;
        }

        public static KaiUrlInfo ReadUrls(string configPath)
        {
            var info = new KaiUrlInfo();
            try
            {
                var urls = KaiZentralUserRegProc.GetKaiZentralURLs(NormalizeConfigPath(configPath));
                if (urls != null)
                {
                    info.Register = urls.RegisterAddress ?? "";
                    info.WebEam = urls.WenAccessManagerAddress ?? "";
                    info.KeyUpload = urls.KeyDataUploadAddress ?? "";
                }
            }
            catch (Exception ex)
            {
                info.Register = "Error: " + ex.Message;
            }
            return info;
        }

        public static IWebProxy ResolveProxy(bool useSystemProxy)
        {
            if (!useSystemProxy) return null;
            try
            {
                var p = WebRequest.GetSystemWebProxy();
                if (p != null)
                    p.Credentials = CredentialCache.DefaultCredentials;
                return p;
            }
            catch
            {
                return WebRequest.DefaultWebProxy;
            }
        }

        public static KaiLocalHealthInfo CheckLocalHealth(string configPath)
        {
            var install = ReadInstallation(configPath);
            if (!install.Exists || install.Port <= 0)
            {
                return new KaiLocalHealthInfo
                {
                    Ok = false,
                    Message = "KAI service port unknown (installation missing)."
                };
            }

            string[] paths = new string[]
            {
                "/HealthCheck",
                "/HealthChecksForList",
                "/KaiHcRestService/GetKaiHealthChecksList_V01",
                "/GeneralRestService_V01/GetKaiHealthChecksList_V01"
            };

            KaiLocalHealthInfo last = null;
            foreach (var path in paths)
            {
                var url = "http://127.0.0.1:" + install.Port + path;
                string body;
                int code;
                var err = TryHttpGet(url, out body, out code);
                last = new KaiLocalHealthInfo
                {
                    HttpStatus = code,
                    Raw = body,
                    Ok = err == null && code >= 200 && code < 300,
                    Message = err ?? ("HTTP " + code + " " + path + (string.IsNullOrEmpty(body) ? "" : " — " + Trim(body, 240)))
                };
                if (last.Ok) return last;
                // 404 try next; 503 means service up but unhealthy — still report
                if (code == 503)
                {
                    last.Ok = false;
                    last.Message = "KAI local service HTTP 503 (unavailable): " + path;
                    return last;
                }
            }
            return last ?? new KaiLocalHealthInfo { Ok = false, Message = "No health endpoint responded." };
        }

        public static KaiStatusInfo ReadKaiStatus(string configPath)
        {
            var install = ReadInstallation(configPath);
            if (!install.Exists || install.Port <= 0)
                return new KaiStatusInfo { Ok = false, Message = "KAI not installed / no port." };

            string[] paths = new string[]
            {
                "/KaiAdminRestService/GetInformationV1",
                "/GeneralRestService_V01/GetInformationV1",
                "/RestService/GetInformationV1",
                "/GetInformationV1"
            };

            foreach (var path in paths)
            {
                var url = "http://127.0.0.1:" + install.Port + path;
                string body;
                int code;
                var err = TryHttpGet(url, out body, out code);
                if (err != null || code < 200 || code >= 300)
                {
                    if (code == 503)
                        return new KaiStatusInfo { Ok = false, Message = "KAI service HTTP 503", Raw = body };
                    continue;
                }
                var info = new KaiStatusInfo { Ok = true, Raw = body, Message = "OK" };
                info.KaiVersion = Extract(body, "KaiVersion") ?? Extract(body, "kaiVersion");
                info.ApplicationState = Extract(body, "ApplicationState") ?? Extract(body, "applicationState");
                info.AmountKeyReaderPlus = Extract(body, "AmountKeyReaderPlus") ?? Extract(body, "amountKeyReaderPlus");
                info.KeysToday = Extract(body, "KeysToday") ?? Extract(body, "keysToday");
                info.KeysLastWeek = Extract(body, "KeysLastWeek") ?? Extract(body, "keysLastWeek");
                if (string.IsNullOrEmpty(info.KaiVersion) && string.IsNullOrEmpty(info.ApplicationState))
                    info.Message = "HTTP " + code + " (unparsed) " + Trim(body, 160);
                return info;
            }
            return new KaiStatusInfo { Ok = false, Message = "GetInformationV1 not reachable (is KAI service running?)." };
        }

        public static KaiCommonConfigInfo ReadCommonConfig(string configPath)
        {
            var install = ReadInstallation(configPath);
            if (!install.Exists || install.Port <= 0)
                return new KaiCommonConfigInfo { Ok = false, Message = "KAI not installed / no port." };

            string[] paths = new string[]
            {
                "/KaiAdminRestService/GetCommonConfigV1",
                "/GeneralRestService_V01/GetCommonConfigV1",
                "/RestService/GetCommonConfigV1",
                "/GetCommonConfigV1"
            };

            foreach (var path in paths)
            {
                var url = "http://127.0.0.1:" + install.Port + path;
                string body;
                int code;
                var err = TryHttpGet(url, out body, out code);
                if (err != null || code < 200 || code >= 300)
                {
                    if (code == 503)
                        return new KaiCommonConfigInfo { Ok = false, Message = "KAI service HTTP 503", Raw = body };
                    continue;
                }
                var info = new KaiCommonConfigInfo { Ok = true, Raw = body, Message = "OK" };
                info.Language = Extract(body, "Language") ?? Extract(body, "language");
                info.UpdateDuringStartKAI = Extract(body, "UpdateDuringStartKAI") ?? ExtractBool(body, "UpdateDuringStartKAI");
                info.UpdateDuringNightKAI = Extract(body, "UpdateDuringNightKAI") ?? ExtractBool(body, "UpdateDuringNightKAI");
                info.UpdateKeyReaderPlusFirmware = Extract(body, "UpdateKeyReaderPlusFirmware") ?? ExtractBool(body, "UpdateKeyReaderPlusFirmware");
                info.LastModifier = Extract(body, "LastModifier") ?? Extract(body, "lastModifier");
                if (string.IsNullOrEmpty(info.Language) && string.IsNullOrEmpty(info.LastModifier))
                    info.Message = "HTTP " + code + " (unparsed) " + Trim(body, 160);
                return info;
            }
            return new KaiCommonConfigInfo { Ok = false, Message = "GetCommonConfigV1 not reachable (is KAI service running?)." };
        }

        private static string TryHttpGet(string url, out string body, out int statusCode)
        {
            body = null;
            statusCode = 0;
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Accept = "application/json, application/xml, text/xml, */*";
                req.Timeout = 8000;
                req.Proxy = null; // local
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream() ?? Stream.Null, Encoding.UTF8))
                {
                    statusCode = (int)resp.StatusCode;
                    body = sr.ReadToEnd();
                    return null;
                }
            }
            catch (WebException we)
            {
                var resp = we.Response as HttpWebResponse;
                if (resp != null)
                {
                    statusCode = (int)resp.StatusCode;
                    try
                    {
                        using (var sr = new StreamReader(resp.GetResponseStream() ?? Stream.Null))
                            body = sr.ReadToEnd();
                    }
                    catch { }
                    return "HTTP " + statusCode;
                }
                return we.Message;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        private static string Extract(string body, string name)
        {
            if (string.IsNullOrEmpty(body) || string.IsNullOrEmpty(name)) return null;
            // JSON "name":"value" or "name":123 or "name":true
            var m = Regex.Match(body, "\"" + Regex.Escape(name) + "\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value;
            m = Regex.Match(body, "\"" + Regex.Escape(name) + "\"\\s*:\\s*([^,\\}\\s]+)", RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value.Trim();
            // XML <name>value</name>
            m = Regex.Match(body, "<" + Regex.Escape(name) + "[^>]*>([^<]*)</" + Regex.Escape(name) + ">", RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value.Trim();
            return null;
        }

        private static string ExtractBool(string body, string name)
        {
            var v = Extract(body, name);
            return v;
        }

        private static string Trim(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = Regex.Replace(s, "\\s+", " ").Trim();
            if (s.Length <= max) return s;
            return s.Substring(0, max - 3) + "...";
        }
    }
}
