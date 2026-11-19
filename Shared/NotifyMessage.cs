using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KaiAosRenew
{
    /// <summary>TCP notify JSON line (one object per \n). Shared by server and client.</summary>
    public sealed class NotifyMessage
    {
        /// <summary>Max Keys rows synced to clients / kept in history (match KeyActivityMonitor.MaxEvents).</summary>
        public const int MaxKeySyncRows = 500;

        [JsonProperty("v")]
        public int V = 1;

        [JsonProperty("kind")]
        public string Kind;

        [JsonProperty("level")]
        public string Level;

        [JsonProperty("title")]
        public string Title;

        [JsonProperty("text")]
        public string Text;

        [JsonProperty("vin")]
        public string Vin;

        [JsonProperty("action")]
        public string Action;

        [JsonProperty("seconds")]
        public int Seconds;

        [JsonProperty("at")]
        public string At;

        [JsonProperty("paint")]
        public string Paint;

        [JsonProperty("uph")]
        public string Uph;

        [JsonProperty("ilevel")]
        public string ILevel;

        [JsonProperty("reader")]
        public string Reader;

        [JsonProperty("detail")]
        public string Detail;

        [JsonProperty("status")]
        public string Status;

        /// <summary>-1 unset, 0/1 when set.</summary>
        [JsonProperty("local")]
        public int Local = -1;

        [JsonProperty("bmw")]
        public int Bmw = -1;

        [JsonProperty("failed")]
        public int Failed = -1;

        /// <summary>Mileage km; -1 unset.</summary>
        [JsonProperty("km")]
        public int Km = -1;

        public string ToJsonLine()
        {
            var o = new JObject();
            o["v"] = V <= 0 ? 1 : V;
            o["kind"] = Kind ?? "";
            o["level"] = Level ?? "";
            o["title"] = Title ?? "";
            o["text"] = Text ?? "";
            if (!string.IsNullOrEmpty(Vin)) o["vin"] = Vin;
            if (!string.IsNullOrEmpty(Action)) o["action"] = Action;
            if (Seconds > 0) o["seconds"] = Seconds;
            if (!string.IsNullOrEmpty(At)) o["at"] = At;
            if (!string.IsNullOrEmpty(Paint)) o["paint"] = Paint;
            if (!string.IsNullOrEmpty(Uph)) o["uph"] = Uph;
            if (!string.IsNullOrEmpty(ILevel)) o["ilevel"] = ILevel;
            if (!string.IsNullOrEmpty(Reader)) o["reader"] = Reader;
            if (!string.IsNullOrEmpty(Detail)) o["detail"] = Detail;
            if (!string.IsNullOrEmpty(Status)) o["status"] = Status;
            if (Local >= 0) o["local"] = Local;
            if (Bmw >= 0) o["bmw"] = Bmw;
            if (Failed >= 0) o["failed"] = Failed;
            if (Km >= 0) o["km"] = Km;
            return o.ToString(Formatting.None);
        }

        public static NotifyMessage ParseLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            line = line.Trim();
            try
            {
                var m = JsonConvert.DeserializeObject<NotifyMessage>(line);
                if (m == null) return null;
                if (string.IsNullOrEmpty(m.Kind)) m.Kind = "info";
                if (string.IsNullOrEmpty(m.Level)) m.Level = "info";
                if (string.IsNullOrEmpty(m.Title)) m.Title = "KAI - AOS Renew";
                if (m.Text == null) m.Text = "";
                if (m.Local < -1) m.Local = -1;
                if (m.Bmw < -1) m.Bmw = -1;
                if (m.Failed < -1) m.Failed = -1;
                if (m.Km < -1) m.Km = -1;
                if (m.V <= 0) m.V = 1;
                return m;
            }
            catch
            {
                return null;
            }
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

        public static string EncodeActivitySync(IList<ActivityWireRow> rows)
        {
            if (rows == null || rows.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (var row in rows)
            {
                if (row == null) continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(row.At.ToString("yyyy-MM-ddTHH:mm:ss"));
                sb.Append('|').Append(EscPipe(row.Level ?? "info"));
                sb.Append('|').Append(EscPipe(row.Text ?? ""));
            }
            return sb.ToString();
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

        public static string EncodeRegisterSync(IList<RegisterWireRow> rows)
        {
            if (rows == null || rows.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (var row in rows)
            {
                if (row == null) continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(row.At.ToString("yyyy-MM-ddTHH:mm:ss"));
                var kind = row.Kind;
                if (kind < 0 || kind > 2)
                    kind = row.Ok ? 1 : 0;
                sb.Append('|').Append(kind.ToString());
                sb.Append('|').Append(EscPipe(row.Text ?? ""));
            }
            return sb.ToString();
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

        public static string EscPipe(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("|", "\\p").Replace("\r", "").Replace("\n", "\\n");
        }

        public static List<string> SplitPipe(string line)
        {
            var parts = new List<string>();
            if (line == null) return parts;
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
    }

    public sealed class KaiSvcWireRow
    {
        public string Name { get; set; }
        public string StatusText { get; set; }
        public string StartTypeText { get; set; }
        public bool Ok { get; set; }
    }

    public sealed class KaiSyncData
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

    public sealed class ActivityWireRow
    {
        public DateTime At { get; set; }
        public string Level { get; set; }
        public string Text { get; set; }
    }

    public sealed class RegisterWireRow
    {
        public DateTime At { get; set; }
        /// <summary>0=fail, 1=ok, 2=info.</summary>
        public int Kind { get; set; }
        public bool Ok { get; set; }
        public string Text { get; set; }
    }
}
