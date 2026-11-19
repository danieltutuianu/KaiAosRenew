using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace KaiAosRenew
{
    /// <summary>
    /// Pure helpers for the one-click Diagnostic Bundle (redacted settings, scrubbed log tails).
    /// Nothing here touches Firebase or the network.
    /// </summary>
    public static class DiagBundleLogic
    {
        public const string Redacted = "***";

        /// <summary>Default per-log tail size inside the bundle (keeps the zip well under the 25 MB transfer cap).</summary>
        public const int DefaultLogTailBytes = 768 * 1024;

        /// <summary>True when this settings.xml element must never leave the machine in plaintext/DPAPI form.</summary>
        public static bool IsSecretElementName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (name.EndsWith("ProtectedBase64", StringComparison.OrdinalIgnoreCase)) return true; // password / pairing / Firebase secret blobs
            if (name.IndexOf("Secret", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (name.IndexOf("PairingCode", StringComparison.OrdinalIgnoreCase) >= 0) return true; // legacy plaintext NotifyPairingCode
            if (name.IndexOf("CredsHash", StringComparison.OrdinalIgnoreCase) >= 0) return true;   // hash of email+password
            if (name.IndexOf("EtagScope", StringComparison.OrdinalIgnoreCase) >= 0) return true;   // derived from url+secret
            if (string.Equals(name, "Password", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(name, "ApiKey", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>a***@domain — enough to tell which account without revealing it.</summary>
        public static string MaskEmail(string email)
        {
            var e = (email ?? "").Trim();
            if (e.Length == 0) return "";
            var at = e.IndexOf('@');
            if (at <= 0) return Redacted;
            return e.Substring(0, 1) + "***" + e.Substring(at);
        }

        /// <summary>Drop ?query and #fragment (Firebase auth=… secrets live there).</summary>
        public static string StripUrlSecrets(string url)
        {
            if (string.IsNullOrEmpty(url)) return url ?? "";
            var cut = url.IndexOfAny(new[] { '?', '#' });
            return cut >= 0 ? url.Substring(0, cut) : url;
        }

        /// <summary>
        /// Returns settings.xml text with every secret element replaced by ***
        /// (PasswordProtectedBase64, NotifyPairingCode*, FirebasePushSecret*, credential hash, …),
        /// the e-mail masked and URL query strings removed.
        /// </summary>
        public static string RedactSettingsXml(string xml)
        {
            if (string.IsNullOrEmpty(xml)) return "";
            try
            {
                var doc = new XmlDocument();
                doc.PreserveWhitespace = true;
                doc.XmlResolver = null;
                doc.LoadXml(xml);
                RedactNode(doc.DocumentElement);
                using (var sw = new StringWriter())
                {
                    var settings = new XmlWriterSettings { Indent = true, OmitXmlDeclaration = false };
                    using (var xw = XmlWriter.Create(sw, settings))
                        doc.Save(xw);
                    return sw.ToString();
                }
            }
            catch
            {
                return RedactSettingsXmlFallback(xml);
            }
        }

        private static void RedactNode(XmlNode node)
        {
            if (node == null) return;
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element) continue;
                var el = (XmlElement)child;
                var hasChildElements = false;
                foreach (XmlNode g in el.ChildNodes)
                    if (g.NodeType == XmlNodeType.Element) { hasChildElements = true; break; }
                if (hasChildElements)
                {
                    RedactNode(el);
                    continue;
                }
                var name = el.LocalName;
                var text = el.InnerText ?? "";
                if (IsSecretElementName(name))
                {
                    if (text.Trim().Length > 0)
                        el.InnerText = Redacted;
                }
                else if (string.Equals(name, "Email", StringComparison.OrdinalIgnoreCase))
                {
                    el.InnerText = MaskEmail(text);
                }
                else if (name.EndsWith("Url", StringComparison.OrdinalIgnoreCase) && text.IndexOfAny(new[] { '?', '#' }) >= 0)
                {
                    el.InnerText = StripUrlSecrets(text);
                }
                else if (string.Equals(name, "LastMessage", StringComparison.OrdinalIgnoreCase))
                {
                    el.InnerText = ScrubLogText(text);
                }
            }
        }

        /// <summary>Regex-only redaction used when the XML cannot be parsed (corrupt file still must not leak).</summary>
        private static string RedactSettingsXmlFallback(string xml)
        {
            var s = Regex.Replace(xml,
                @"<(?<n>[A-Za-z0-9_]*(?:ProtectedBase64|Secret|PairingCode|CredsHash|EtagScope|Password|ApiKey)[A-Za-z0-9_]*)(?<a>\s[^>]*)?>[\s\S]*?</\k<n>>",
                delegate(Match m)
                {
                    var n = m.Groups["n"].Value;
                    // Keep harmless "PasswordRotateRemindHours", "LastPasswordChangeUtc", … readable.
                    if (n.StartsWith("PasswordRotate", StringComparison.OrdinalIgnoreCase)
                        || n.StartsWith("LastPassword", StringComparison.OrdinalIgnoreCase)
                        || n.StartsWith("BrowserPassword", StringComparison.OrdinalIgnoreCase))
                        return m.Value;
                    return "<" + n + m.Groups["a"].Value + ">" + Redacted + "</" + n + ">";
                },
                RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"<Email>\s*([^<]*?)\s*</Email>",
                delegate(Match m) { return "<Email>" + MaskEmail(m.Groups[1].Value) + "</Email>"; },
                RegexOptions.IgnoreCase);
            return ScrubLogText(s);
        }

        private static readonly Regex BearerRx = new Regex(
            @"(?i)\b(Bearer|Basic)\s+[A-Za-z0-9._\-+/=]{8,}", RegexOptions.Compiled);
        private static readonly Regex KeyValueSecretRx = new Regex(
            @"(?i)(?<k>""?\b(?:password|passwd|pwd|secret|client_secret|access_token|refresh_token|id_token|pairing(?:_?code)?|authorization|auth)\b""?\s*[:=]\s*""?)(?<v>[^\s""',;&<>]{2,})",
            RegexOptions.Compiled);
        private static readonly Regex JwtRx = new Regex(
            @"\beyJ[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]*", RegexOptions.Compiled);
        private static readonly Regex UrlAuthRx = new Regex(
            @"(?i)([?&](?:auth|access_token|token|key|secret)=)[^&\s""']+", RegexOptions.Compiled);

        /// <summary>Best-effort scrub of secrets that could appear in log lines (never relied on as the only protection).</summary>
        public static string ScrubLogText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            try
            {
                var s = BearerRx.Replace(text, "$1 " + Redacted);
                s = JwtRx.Replace(s, "[jwt-redacted]");
                s = UrlAuthRx.Replace(s, "$1" + Redacted);
                s = KeyValueSecretRx.Replace(s, "${k}" + Redacted);
                return s;
            }
            catch
            {
                return text;
            }
        }

        /// <summary>Last <paramref name="maxBytes"/> of a text file (shared read, partial first line dropped). Null when missing.</summary>
        public static string ReadTail(string path, int maxBytes)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                if (maxBytes < 1024) maxBytes = 1024;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    var len = fs.Length;
                    var start = len > maxBytes ? len - maxBytes : 0;
                    fs.Seek(start, SeekOrigin.Begin);
                    var buf = new byte[len - start];
                    var read = 0;
                    while (read < buf.Length)
                    {
                        var n = fs.Read(buf, read, buf.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    var text = Encoding.UTF8.GetString(buf, 0, read);
                    if (start > 0)
                    {
                        var nl = text.IndexOf('\n');
                        if (nl >= 0 && nl + 1 < text.Length)
                            text = "… (older entries omitted)\r\n" + text.Substring(nl + 1);
                    }
                    return text;
                }
            }
            catch
            {
                return null;
            }
        }

        private static readonly Regex TimestampLineRx = new Regex(
            @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}  ", RegexOptions.Compiled);

        /// <summary>True for the first line of a renew.log entry that belongs to a BMW / WebEAM / Register HTTP exchange.</summary>
        public static bool IsBmwHttpHeadLine(string line)
        {
            if (string.IsNullOrEmpty(line) || !TimestampLineRx.IsMatch(line)) return false;
            var body = line.Substring(21).TrimStart();
            return body.StartsWith("BMW HTTP ", StringComparison.Ordinal)
                || body.StartsWith("Register OK: BMW HTTP ", StringComparison.Ordinal)
                || body.StartsWith("Register failed: BMW HTTP ", StringComparison.Ordinal)
                || body.StartsWith("REQUEST:", StringComparison.Ordinal)
                || body.StartsWith("RESPONSE:", StringComparison.Ordinal)
                || body.StartsWith("Register REQUEST:", StringComparison.Ordinal)
                || body.StartsWith("Register RESPONSE:", StringComparison.Ordinal);
        }

        /// <summary>
        /// Pull the BMW HTTP request/response blocks (header + REQUEST/RESPONSE entries with continuation lines)
        /// out of a renew.log tail. Returns an empty string when none are present.
        /// </summary>
        public static string ExtractBmwHttpLog(string renewLogText)
        {
            if (string.IsNullOrEmpty(renewLogText)) return "";
            var sb = new StringBuilder();
            var lines = renewLogText.Replace("\r\n", "\n").Split('\n');
            var inBlock = false;
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (TimestampLineRx.IsMatch(line))
                    inBlock = IsBmwHttpHeadLine(line);
                if (inBlock)
                    sb.Append(line).Append("\r\n");
            }
            return sb.ToString();
        }

        /// <summary>Human-readable key/value lines (version, token expiry, failures) for diag-info.txt.</summary>
        public static string BuildInfoText(IEnumerable<KeyValuePair<string, string>> items)
        {
            var sb = new StringBuilder();
            if (items != null)
            {
                foreach (var kv in items)
                    sb.Append(kv.Key).Append(": ").Append(ScrubLogText(kv.Value ?? "")).Append("\r\n");
            }
            return sb.ToString();
        }
    }

    /// <summary>Decision rules for the proactive "token about to expire while renew is failing" alert.</summary>
    public static class HealthAlertLogic
    {
        public static readonly TimeSpan TokenExpiryWindow = TimeSpan.FromMinutes(30);
        public static readonly TimeSpan AlertThrottle = TimeSpan.FromMinutes(30);

        /// <summary>
        /// True when the WebEAM token is expired or expires within <paramref name="window"/> AND renewing is currently
        /// struggling (consecutive failures, Register cooldown, or Register blocked), and the last alert is older than
        /// <paramref name="throttle"/>.
        /// </summary>
        public static bool ShouldAlertTokenExpiry(
            DateTime nowUtc,
            DateTime? tokenExpiryUtc,
            int consecutiveFailures,
            bool authCooldownActive,
            bool registerBlocked,
            DateTime? lastAlertUtc,
            TimeSpan window,
            TimeSpan throttle)
        {
            if (!tokenExpiryUtc.HasValue) return false;
            if (tokenExpiryUtc.Value - nowUtc > window) return false;
            var struggling = consecutiveFailures > 0 || authCooldownActive || registerBlocked;
            if (!struggling) return false;
            if (lastAlertUtc.HasValue && nowUtc - lastAlertUtc.Value < throttle) return false;
            return true;
        }

        /// <summary>Reason code appended to the alert text: "failures", "cooldown", "blocked" (comma-joined).</summary>
        public static string DescribeReasons(int consecutiveFailures, bool authCooldownActive, bool registerBlocked)
        {
            var parts = new List<string>();
            if (consecutiveFailures > 0) parts.Add("failures=" + consecutiveFailures);
            if (authCooldownActive) parts.Add("cooldown");
            if (registerBlocked) parts.Add("blocked");
            return string.Join(", ", parts.ToArray());
        }
    }
}
