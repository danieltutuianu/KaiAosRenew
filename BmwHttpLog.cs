using System;
using System.Text;
using System.Text.RegularExpressions;

namespace KaiAosRenew
{
    /// <summary>
    /// Logs BMW / Kai Zentral / WebEAM HTTP exchanges with full request + response.
    /// Secrets must already be redacted in <paramref name="requestDump"/>.
    /// </summary>
    internal static class BmwHttpLog
    {
        /// <summary>Optional UI sink (MainWindow.AppendLog). Always also writes via RenewEngine.Log when unset.</summary>
        public static Action<string> UiAppend;

        public static void LogNon200(
            string operation,
            int status,
            string url,
            string requestDump,
            string responseBody,
            bool registerPath = false)
        {
            LogExchange(operation, status, url, requestDump, responseBody, registerPath);
        }

        /// <summary>
        /// Log request + response for any status (including 200 OK). Non-200 stays Warning/Error style.
        /// </summary>
        public static void LogExchange(
            string operation,
            int status,
            string url,
            string requestDump,
            string responseBody,
            bool registerPath = false)
        {
            var op = string.IsNullOrWhiteSpace(operation) ? "BMW" : operation.Trim();
            var urlText = string.IsNullOrWhiteSpace(url) ? "" : url.Trim();
            var ok = status == 200;
            var header = registerPath
                ? (ok
                    ? "Register OK: BMW HTTP " + status + " [" + op + "]"
                    : "Register failed: BMW HTTP " + status + " [" + op + "]")
                : "BMW HTTP " + status + " [" + op + "]";
            if (!string.IsNullOrEmpty(urlText))
                header += " " + urlText;

            var req = string.IsNullOrEmpty(requestDump) ? "(no request dump)" : requestDump;
            var resp = string.IsNullOrEmpty(responseBody) ? "(empty body)" : RedactSecrets(responseBody);

            Emit(header);
            // Prefix Register so MaybeAppendRegisterHistory classifies request/response too.
            var reqLabel = registerPath ? "Register REQUEST:\n" : "REQUEST:\n";
            var respLabel = registerPath ? "Register RESPONSE:\n" : "RESPONSE:\n";
            Emit(reqLabel + req);
            Emit(respLabel + resp);
        }

        public static string FormatKeyUploadRequest(
            string method,
            string url,
            string originCodeType,
            string keyReaderId,
            string keyReaderHardwareId,
            string keyReadDate,
            string language,
            int binBytes)
        {
            var sb = new StringBuilder();
            sb.AppendLine((method ?? "POST") + " " + (url ?? ""));
            sb.AppendLine("Content-Type: application/json (via KaiServices.Impl UploadKeyData)");
            sb.AppendLine("OriginCodeType=" + (originCodeType ?? ""));
            sb.AppendLine("KeyReaderId=" + (keyReaderId ?? ""));
            sb.AppendLine("KeyReaderHardwareId=" + (keyReaderHardwareId ?? ""));
            sb.AppendLine("KeyReadDate=" + (keyReadDate ?? ""));
            sb.AppendLine("Language=" + (language ?? ""));
            sb.AppendLine("KeyDataFile=K <base64 omitted> binBytes=" + binBytes);
            return sb.ToString().TrimEnd();
        }

        public static string FormatWebEamRequest(string url, string clientId)
        {
            var sb = new StringBuilder();
            sb.AppendLine("POST " + (url ?? ""));
            sb.AppendLine("Accept: application/json");
            sb.AppendLine("Content-Type: application/x-www-form-urlencoded");
            sb.AppendLine("Authorization: Basic ***");
            sb.AppendLine("Body: grant_type=client_credentials&scope=machine2machine");
            if (!string.IsNullOrEmpty(clientId))
                sb.AppendLine("client_id=" + clientId);
            return sb.ToString().TrimEnd();
        }

        public static string FormatRegisterRequest(string url, string email)
        {
            var sb = new StringBuilder();
            sb.AppendLine("POST " + (url ?? ""));
            sb.AppendLine("via KaiZentralUserRegProc.RegisterWithKaiZentral");
            if (!string.IsNullOrEmpty(email))
                sb.AppendLine("email=" + email);
            sb.AppendLine("password=***");
            return sb.ToString().TrimEnd();
        }

        public static string RedactSecrets(string body)
        {
            if (string.IsNullOrEmpty(body)) return body ?? "";
            try
            {
                var s = Regex.Replace(body,
                    "\"access_token\"\\s*:\\s*\"[^\"]*\"",
                    "\"access_token\":\"***\"",
                    RegexOptions.IgnoreCase);
                s = Regex.Replace(s,
                    "\"refresh_token\"\\s*:\\s*\"[^\"]*\"",
                    "\"refresh_token\":\"***\"",
                    RegexOptions.IgnoreCase);
                s = Regex.Replace(s,
                    "\"client_secret\"\\s*:\\s*\"[^\"]*\"",
                    "\"client_secret\":\"***\"",
                    RegexOptions.IgnoreCase);
                s = Regex.Replace(s, @"[A-Za-z0-9+/]{80,}={0,2}", "[redacted]");
                return s;
            }
            catch
            {
                return body;
            }
        }

        private static void Emit(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var sink = UiAppend;
            if (sink != null)
            {
                try { sink(text); }
                catch { }
                return;
            }
            try { RenewEngine.Log(text); }
            catch { }
        }
    }
}
