using System;
using System.Collections.Generic;
using System.Text;

namespace KaiAosNotifyClient
{
    internal sealed class NotifyMessage
    {
        public int V = 1;
        public string Kind;
        public string Level;
        public string Title;
        public string Text;
        public string Vin;
        public string Action;
        public int Seconds;
        public string At;
        public string Paint;
        public string Uph;
        public string ILevel;
        public string Reader;
        public string Detail;
        public string Status;
        public int Local = -1;
        public int Bmw = -1;
        public int Failed = -1;

        public string ToJsonLine()
        {
            var sb = new StringBuilder(256);
            sb.Append("{\"v\":").Append(V);
            sb.Append(",\"kind\":\"").Append(Esc(Kind)).Append("\"");
            sb.Append(",\"level\":\"").Append(Esc(Level)).Append("\"");
            sb.Append(",\"title\":\"").Append(Esc(Title)).Append("\"");
            sb.Append(",\"text\":\"").Append(Esc(Text)).Append("\"");
            if (!string.IsNullOrEmpty(Vin))
                sb.Append(",\"vin\":\"").Append(Esc(Vin)).Append("\"");
            if (!string.IsNullOrEmpty(Action))
                sb.Append(",\"action\":\"").Append(Esc(Action)).Append("\"");
            if (Seconds > 0)
                sb.Append(",\"seconds\":").Append(Seconds);
            if (!string.IsNullOrEmpty(At))
                sb.Append(",\"at\":\"").Append(Esc(At)).Append("\"");
            sb.Append("}");
            return sb.ToString();
        }

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        public static string EncodeUpdateFeed(string serverVer, string clientVer)
        {
            return (serverVer ?? "") + "\u001f" + (clientVer ?? "");
        }

        public static bool TryParseUpdateFeed(string text, out string serverVer, out string clientVer)
        {
            serverVer = "";
            clientVer = "";
            if (text == null) return false;
            var i = text.IndexOf('\u001f');
            if (i < 0)
            {
                serverVer = text;
                return true;
            }
            serverVer = text.Substring(0, i);
            clientVer = i + 1 < text.Length ? text.Substring(i + 1) : "";
            return true;
        }

        public static string EncodeKvMap(IDictionary<string, string> map)
        {
            if (map == null || map.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (var kv in map)
            {
                if (string.IsNullOrEmpty(kv.Key)) continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(EscPipe(kv.Key));
                sb.Append('|').Append(EscPipe(kv.Value ?? ""));
            }
            return sb.ToString();
        }

        public static Dictionary<string, string> ParseKvMap(string text)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(text)) return map;
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var raw = lines[i];
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var parts = SplitPipe(raw);
                if (parts.Count < 2) continue;
                var key = parts[0] ?? "";
                if (key.Length == 0) continue;
                map[key] = parts[1] ?? "";
            }
            return map;
        }

        public static string EncodeAosSync(int intervalHours, string email, string password, bool? forceRegister = null, int? registerBlockAfter = null)
        {
            var sb = new StringBuilder();
            sb.Append(intervalHours < 1 ? 1 : intervalHours);
            sb.Append('|').Append(EscPipe(email ?? ""));
            sb.Append('|').Append(EscPipe(password ?? ""));
            if (forceRegister.HasValue || registerBlockAfter.HasValue)
                sb.Append('|').Append(forceRegister.HasValue ? (forceRegister.Value ? "1" : "0") : "");
            if (registerBlockAfter.HasValue)
            {
                var n = registerBlockAfter.Value;
                if (n < 1) n = 1;
                if (n > 10) n = 10;
                sb.Append('|').Append(n);
            }
            return sb.ToString();
        }

        public static bool TryParseAosSync(string text, out int intervalHours, out string email, out string password)
        {
            bool? unused;
            int? unusedBlock;
            return TryParseAosSync(text, out intervalHours, out email, out password, out unused, out unusedBlock);
        }

        public static bool TryParseAosSync(string text, out int intervalHours, out string email, out string password, out bool? forceRegister)
        {
            int? unusedBlock;
            return TryParseAosSync(text, out intervalHours, out email, out password, out forceRegister, out unusedBlock);
        }

        public static bool TryParseAosSync(string text, out int intervalHours, out string email, out string password, out bool? forceRegister, out int? registerBlockAfter)
        {
            intervalHours = 1;
            email = "";
            password = "";
            forceRegister = null;
            registerBlockAfter = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var parts = SplitPipe(text);
            if (parts.Count < 3) return false;
            int h;
            if (!int.TryParse(parts[0], out h) || h < 1)
                h = 1;
            intervalHours = h;
            email = parts[1] ?? "";
            password = parts[2] ?? "";
            if (parts.Count >= 4)
            {
                var f = (parts[3] ?? "").Trim();
                if (f == "1") forceRegister = true;
                else if (f == "0") forceRegister = false;
            }
            if (parts.Count >= 5)
            {
                int n;
                if (int.TryParse((parts[4] ?? "").Trim(), out n) && n >= 1)
                {
                    if (n > 10) n = 10;
                    registerBlockAfter = n;
                }
            }
            return true;
        }

        private static string EscPipe(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("|", "\\p").Replace("\r", "").Replace("\n", "\\n");
        }

        public static NotifyMessage ParseLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            line = line.Trim();
            var m = new NotifyMessage();
            m.Kind = JsonStr(line, "kind") ?? "info";
            m.Level = JsonStr(line, "level") ?? "info";
            m.Title = JsonStr(line, "title") ?? "KAI - AOS Renew";
            m.Text = JsonStr(line, "text") ?? "";
            m.Vin = JsonStr(line, "vin");
            m.Action = JsonStr(line, "action");
            m.Seconds = JsonInt(line, "seconds");
            m.At = JsonStr(line, "at");
            m.Paint = JsonStr(line, "paint");
            m.Uph = JsonStr(line, "uph");
            m.ILevel = JsonStr(line, "ilevel");
            m.Reader = JsonStr(line, "reader");
            m.Detail = JsonStr(line, "detail");
            m.Status = JsonStr(line, "status");
            m.Local = JsonIntOpt(line, "local");
            m.Bmw = JsonIntOpt(line, "bmw");
            m.Failed = JsonIntOpt(line, "failed");
            return m;
        }

        public static List<ClientKeyRow> ParseKeySync(string text)
        {
            var list = new List<ClientKeyRow>();
            if (string.IsNullOrWhiteSpace(text)) return list;
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (var raw in lines)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var row = ParseKeySyncLine(raw);
                if (row != null && !string.IsNullOrEmpty(row.Vin))
                    list.Add(row);
                if (list.Count >= 10) break;
            }
            return list;
        }

        public static ClientKeyRow ParseKeySyncLine(string line)
        {
            var parts = SplitPipe(line);
            if (parts.Count < 2) return null;
            var row = new ClientKeyRow();
            DateTime at;
            if (DateTime.TryParse(parts[0], out at))
                row.DetectedLocal = DateTime.SpecifyKind(at, DateTimeKind.Local);
            else
                row.DetectedLocal = DateTime.Now;
            row.Vin = parts.Count > 1 ? parts[1] : "";
            row.Paint = parts.Count > 2 ? parts[2] : "";
            row.Upholstery = parts.Count > 3 ? parts[3] : "";
            row.ILevelCurrent = parts.Count > 4 ? parts[4] : "";
            row.ReaderId = parts.Count > 5 ? parts[5] : "";
            row.LocalSent = parts.Count > 6 && parts[6] == "1";
            row.BmwUploaded = parts.Count > 7 && parts[7] == "1";
            row.Failed = parts.Count > 8 && parts[8] == "1";
            row.Status = parts.Count > 9 ? parts[9] : "Waiting";
            row.Detail = parts.Count > 10 ? parts[10] : "";
            return row;
        }

        public static ClientKeyRow FromNotifyMessage(NotifyMessage m)
        {
            if (m == null) return null;
            var row = new ClientKeyRow();
            DateTime at;
            if (!string.IsNullOrEmpty(m.At) && DateTime.TryParse(m.At, out at))
                row.DetectedLocal = DateTime.SpecifyKind(at, DateTimeKind.Local);
            else
                row.DetectedLocal = DateTime.Now;
            row.Vin = m.Vin ?? "";
            row.Paint = m.Paint ?? "";
            row.Upholstery = m.Uph ?? "";
            row.ILevelCurrent = m.ILevel ?? "";
            row.ReaderId = m.Reader ?? "";
            row.Detail = m.Detail ?? "";
            row.Status = string.IsNullOrEmpty(m.Status) ? "Waiting" : m.Status;
            row.LocalSent = m.Local == 1;
            row.BmwUploaded = m.Bmw == 1;
            row.Failed = m.Failed == 1;
            return row;
        }

        public static List<ActivityWireRow> ParseActivitySync(string text)
        {
            var list = new List<ActivityWireRow>();
            if (string.IsNullOrWhiteSpace(text)) return list;
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (var raw in lines)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var parts = SplitPipe(raw);
                if (parts.Count < 3) continue;
                DateTime at;
                if (!DateTime.TryParse(parts[0], out at))
                    at = DateTime.Now;
                list.Add(new ActivityWireRow
                {
                    At = DateTime.SpecifyKind(at, DateTimeKind.Local),
                    Level = parts[1] ?? "info",
                    Text = parts[2] ?? ""
                });
            }
            return list;
        }

        public static List<RegisterWireRow> ParseRegisterSync(string text)
        {
            var list = new List<RegisterWireRow>();
            if (string.IsNullOrWhiteSpace(text)) return list;
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (var raw in lines)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var parts = SplitPipe(raw);
                if (parts.Count < 3) continue;
                DateTime at;
                if (!DateTime.TryParse(parts[0], out at))
                    at = DateTime.Now;
                var okFlag = (parts[1] ?? "").Trim();
                int kind;
                if (okFlag == "2" || string.Equals(okFlag, "info", StringComparison.OrdinalIgnoreCase))
                    kind = 2;
                else if (okFlag == "1" || string.Equals(okFlag, "ok", StringComparison.OrdinalIgnoreCase))
                    kind = 1;
                else
                    kind = 0;
                list.Add(new RegisterWireRow
                {
                    At = DateTime.SpecifyKind(at, DateTimeKind.Local),
                    Kind = kind,
                    Ok = kind == 1,
                    Text = parts[2] ?? ""
                });
            }
            return list;
        }

        /// <summary>
        /// kind=kaiSync text. Line 1: install|registerUrl|webEamUrl|uploadUrl|tokenExpiryText|allOk|anyRunning|svcAlert|cmdBusy
        /// then up to 2 lines: name|statusText|startTypeText|ok
        /// </summary>
        public static string EncodeKaiSync(
            string install, string registerUrl, string webEamUrl, string uploadUrl, string tokenExpiryText,
            bool allOk, bool anyRunning, bool svcAlert, bool cmdBusy, IList<KaiSvcWireRow> services)
        {
            var sb = new StringBuilder();
            sb.Append(EscPipe(install ?? ""));
            sb.Append('|').Append(EscPipe(registerUrl ?? ""));
            sb.Append('|').Append(EscPipe(webEamUrl ?? ""));
            sb.Append('|').Append(EscPipe(uploadUrl ?? ""));
            sb.Append('|').Append(EscPipe(tokenExpiryText ?? ""));
            sb.Append('|').Append(allOk ? "1" : "0");
            sb.Append('|').Append(anyRunning ? "1" : "0");
            sb.Append('|').Append(svcAlert ? "1" : "0");
            sb.Append('|').Append(cmdBusy ? "1" : "0");
            if (services != null)
            {
                var n = 0;
                foreach (var s in services)
                {
                    if (s == null) continue;
                    if (n >= 2) break;
                    sb.Append('\n');
                    sb.Append(EscPipe(s.Name ?? ""));
                    sb.Append('|').Append(EscPipe(s.StatusText ?? ""));
                    sb.Append('|').Append(EscPipe(s.StartTypeText ?? ""));
                    sb.Append('|').Append(s.Ok ? "1" : "0");
                    n++;
                }
            }
            return sb.ToString();
        }

        public static KaiSyncData ParseKaiSync(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            if (lines.Length == 0 || string.IsNullOrEmpty(lines[0])) return null;
            var head = SplitPipe(lines[0]);
            if (head.Count < 8) return null;
            var data = new KaiSyncData
            {
                Install = head[0] ?? "",
                RegisterUrl = head[1] ?? "",
                WebEamUrl = head[2] ?? "",
                UploadUrl = head[3] ?? "",
                TokenExpiryText = head[4] ?? "",
                AllOk = head[5] == "1",
                AnyRunning = head[6] == "1",
                ServiceAlert = head[7] == "1",
                ServiceCmdBusy = head.Count > 8 && head[8] == "1"
            };
            for (var i = 1; i < lines.Length && data.Services.Count < 2; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var p = SplitPipe(lines[i]);
                if (p.Count < 4) continue;
                data.Services.Add(new KaiSvcWireRow
                {
                    Name = p[0] ?? "",
                    StatusText = p[1] ?? "",
                    StartTypeText = p[2] ?? "",
                    Ok = p[3] == "1"
                });
            }
            return data;
        }

        private static List<string> SplitPipe(string line)
        {
            var parts = new List<string>();
            var sb = new StringBuilder();
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (c == '\\' && i + 1 < line.Length)
                {
                    var n = line[i + 1];
                    if (n == 'p') { sb.Append('|'); i++; continue; }
                    if (n == 'n') { sb.Append('\n'); i++; continue; }
                    if (n == '\\') { sb.Append('\\'); i++; continue; }
                    sb.Append(n);
                    i++;
                    continue;
                }
                if (c == '|')
                {
                    parts.Add(sb.ToString());
                    sb.Length = 0;
                    continue;
                }
                sb.Append(c);
            }
            parts.Add(sb.ToString());
            return parts;
        }

        private static int IndexOfKeyColon(string json, string key)
        {
            var needle = "\"" + key + "\"";
            var start = 0;
            while (start < json.Length)
            {
                var i = json.IndexOf(needle, start, StringComparison.OrdinalIgnoreCase);
                if (i < 0) return -1;
                var j = i + needle.Length;
                while (j < json.Length && (json[j] == ' ' || json[j] == '\t')) j++;
                if (j < json.Length && json[j] == ':')
                    return j;
                start = i + needle.Length;
            }
            return -1;
        }

        private static string JsonStr(string json, string key)
        {
            var i = IndexOfKeyColon(json, key);
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
                    var n = json[j + 1];
                    if (n == 'n') sb.Append('\n');
                    else if (n == 'r') sb.Append('\r');
                    else if (n == 't') sb.Append('\t');
                    else sb.Append(n);
                    j += 2;
                    continue;
                }
                if (c == '"') break;
                sb.Append(c);
                j++;
            }
            return sb.ToString();
        }

        private static int JsonInt(string json, string key)
        {
            var v = JsonIntOpt(json, key);
            return v < 0 ? 0 : v;
        }

        private static int JsonIntOpt(string json, string key)
        {
            var i = IndexOfKeyColon(json, key);
            if (i < 0) return -1;
            i++;
            while (i < json.Length && (json[i] == ' ' || json[i] == '\t')) i++;
            var start = i;
            while (i < json.Length && (char.IsDigit(json[i]) || json[i] == '-')) i++;
            if (i == start) return -1;
            int v;
            return int.TryParse(json.Substring(start, i - start), out v) ? v : -1;
        }
    }

    internal sealed class KaiSvcWireRow
    {
        public string Name { get; set; }
        public string StatusText { get; set; }
        public string StartTypeText { get; set; }
        public bool Ok { get; set; }
    }

    internal sealed class KaiSyncData
    {
        public string Install = "";
        public string RegisterUrl = "";
        public string WebEamUrl = "";
        public string UploadUrl = "";
        public string TokenExpiryText = "";
        public bool AllOk;
        public bool AnyRunning;
        public bool ServiceAlert;
        public bool ServiceCmdBusy;
        public readonly List<KaiSvcWireRow> Services = new List<KaiSvcWireRow>();
    }

    internal sealed class ActivityWireRow
    {
        public DateTime At { get; set; }
        public string Level { get; set; }
        public string Text { get; set; }
    }

    internal sealed class RegisterWireRow
    {
        public DateTime At { get; set; }
        /// <summary>0=fail, 1=ok, 2=info.</summary>
        public int Kind { get; set; }
        public bool Ok { get; set; }
        public string Text { get; set; }
    }
}
