using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KaiAosRenew
{
    /// <summary>HTTPS client for the Google Apps Script peer credential relay.</summary>
    internal sealed class PeerSyncClient
    {
        private const int TimeoutMs = 15000;
        private const string UserAgent = "KaiAosRenew-PeerSync/1.0";

        public sealed class PollResult
        {
            public bool Ok;
            public bool Unchanged;
            public long Seq;
            public long AckSeq;
            public long CmdSeq;
            public long PeersSeq;
            public PeerCredsEvent LatestCreds;
            public PeerCommandEvent LatestCommand;
            public List<PeerAckEvent> Acks = new List<PeerAckEvent>();
            public List<PeerRecord> Peers = new List<PeerRecord>();
            public string Error;
        }

        public sealed class ActionResult
        {
            public bool Ok;
            public long Seq;
            public long AckSeq;
            public long CmdSeq;
            public long PeersSeq;
            public bool Duplicate;
            public string Error;
        }

        public static Task<PollResult> PollAsync(
            string url,
            string secret,
            string serverId,
            long sinceSeq,
            long sinceAckSeq,
            long sinceCmdSeq,
            long sincePeersSeq,
            CancellationToken ct)
        {
            return Task.Run(() => Poll(url, secret, serverId, sinceSeq, sinceAckSeq, sinceCmdSeq, sincePeersSeq), ct);
        }

        public static PollResult Poll(
            string url,
            string secret,
            string serverId,
            long sinceSeq,
            long sinceAckSeq,
            long sinceCmdSeq,
            long sincePeersSeq)
        {
            var result = new PollResult();
            try
            {
                EnsureTls12();
                url = NormalizeUrl(url);
                if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(secret))
                {
                    result.Error = "Missing URL or secret";
                    return result;
                }
                var authTag = PeerSyncCrypto.ComputeAuthTag(secret);
                var q = new StringBuilder();
                q.Append(url.Contains("?") ? "&" : "?");
                q.Append("authTag=").Append(Uri.EscapeDataString(authTag));
                q.Append("&sinceSeq=").Append(sinceSeq);
                q.Append("&sinceAckSeq=").Append(sinceAckSeq);
                q.Append("&sinceCmdSeq=").Append(sinceCmdSeq);
                q.Append("&sincePeersSeq=").Append(sincePeersSeq);
                q.Append("&serverId=").Append(Uri.EscapeDataString(serverId ?? ""));

                var json = HttpGet(url + q.ToString());
                return ParsePoll(json);
            }
            catch (Exception ex)
            {
                result.Error = ex.Message ?? "poll failed";
                return result;
            }
        }

        public static Task<ActionResult> PublishAsync(
            string url,
            string secret,
            string eventId,
            string originServerId,
            string originName,
            string payloadEnc,
            string statusEnc,
            CancellationToken ct)
        {
            return Task.Run(() => Publish(url, secret, eventId, originServerId, originName, payloadEnc, statusEnc), ct);
        }

        public static ActionResult Publish(
            string url,
            string secret,
            string eventId,
            string originServerId,
            string originName,
            string payloadEnc,
            string statusEnc)
        {
            var body = new JObject();
            body["action"] = "publish";
            body["authTag"] = PeerSyncCrypto.ComputeAuthTag(secret);
            body["eventId"] = eventId ?? "";
            body["originServerId"] = originServerId ?? "";
            body["serverId"] = originServerId ?? "";
            body["originName"] = originName ?? "";
            body["payloadEnc"] = payloadEnc ?? "";
            if (!string.IsNullOrEmpty(statusEnc))
                body["statusEnc"] = statusEnc;
            return PostAction(url, secret, body);
        }

        public static Task<ActionResult> AckAsync(
            string url,
            string secret,
            string ackId,
            string forEventId,
            string targetServerId,
            string responderServerId,
            string responderName,
            string payloadEnc,
            string statusEnc,
            CancellationToken ct)
        {
            return Task.Run(() => Ack(url, secret, ackId, forEventId, targetServerId, responderServerId, responderName, payloadEnc, statusEnc), ct);
        }

        public static ActionResult Ack(
            string url,
            string secret,
            string ackId,
            string forEventId,
            string targetServerId,
            string responderServerId,
            string responderName,
            string payloadEnc,
            string statusEnc)
        {
            var body = new JObject();
            body["action"] = "ack";
            body["authTag"] = PeerSyncCrypto.ComputeAuthTag(secret);
            body["ackId"] = ackId ?? "";
            body["forEventId"] = forEventId ?? "";
            body["targetServerId"] = targetServerId ?? "";
            body["responderServerId"] = responderServerId ?? "";
            body["responderName"] = responderName ?? "";
            body["payloadEnc"] = payloadEnc ?? "";
            if (!string.IsNullOrEmpty(statusEnc))
                body["statusEnc"] = statusEnc;
            return PostAction(url, secret, body);
        }

        public static Task<ActionResult> HeartbeatAsync(
            string url,
            string secret,
            string serverId,
            string serverName,
            string statusEnc,
            CancellationToken ct)
        {
            return Task.Run(() => Heartbeat(url, secret, serverId, serverName, statusEnc), ct);
        }

        public static ActionResult Heartbeat(
            string url,
            string secret,
            string serverId,
            string serverName,
            string statusEnc)
        {
            var body = new JObject();
            body["action"] = "heartbeat";
            body["authTag"] = PeerSyncCrypto.ComputeAuthTag(secret);
            body["serverId"] = serverId ?? "";
            body["originServerId"] = serverId ?? "";
            body["originName"] = serverName ?? "";
            body["statusEnc"] = statusEnc ?? "";
            return PostAction(url, secret, body);
        }

        public static Task<ActionResult> PublishCommandAsync(
            string url,
            string secret,
            string cmdId,
            string type,
            string originServerId,
            string originName,
            string payloadEnc,
            string statusEnc,
            CancellationToken ct)
        {
            return Task.Run(() => PublishCommand(url, secret, cmdId, type, originServerId, originName, payloadEnc, statusEnc), ct);
        }

        public static ActionResult PublishCommand(
            string url,
            string secret,
            string cmdId,
            string type,
            string originServerId,
            string originName,
            string payloadEnc,
            string statusEnc)
        {
            var body = new JObject();
            body["action"] = "command";
            body["authTag"] = PeerSyncCrypto.ComputeAuthTag(secret);
            body["cmdId"] = cmdId ?? "";
            body["type"] = type ?? "";
            body["originServerId"] = originServerId ?? "";
            body["serverId"] = originServerId ?? "";
            body["originName"] = originName ?? "";
            body["payloadEnc"] = payloadEnc ?? "";
            if (!string.IsNullOrEmpty(statusEnc))
                body["statusEnc"] = statusEnc;
            return PostAction(url, secret, body);
        }

        public static Task<ActionResult> PingAsync(string url, string secret, CancellationToken ct)
        {
            return Task.Run(() => Ping(url, secret), ct);
        }

        public static ActionResult Ping(string url, string secret)
        {
            var body = new JObject();
            body["action"] = "ping";
            body["authTag"] = PeerSyncCrypto.ComputeAuthTag(secret);
            return PostAction(url, secret, body);
        }

        private static ActionResult PostAction(string url, string secret, JObject body)
        {
            var result = new ActionResult();
            try
            {
                EnsureTls12();
                url = NormalizeUrl(url);
                if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(secret))
                {
                    result.Error = "Missing URL or secret";
                    return result;
                }
                var json = HttpPost(url, body.ToString(Formatting.None));
                var o = JObject.Parse(json);
                result.Ok = o["ok"] != null && o["ok"].Type == JTokenType.Boolean && (bool)o["ok"];
                result.Duplicate = o["duplicate"] != null && o["duplicate"].Type == JTokenType.Boolean && (bool)o["duplicate"];
                long seq;
                if (long.TryParse(TokenString(o["seq"]), out seq)) result.Seq = seq;
                long ackSeq;
                if (long.TryParse(TokenString(o["ackSeq"]), out ackSeq)) result.AckSeq = ackSeq;
                long cmdSeq;
                if (long.TryParse(TokenString(o["cmdSeq"]), out cmdSeq)) result.CmdSeq = cmdSeq;
                long peersSeq;
                if (long.TryParse(TokenString(o["peersSeq"]), out peersSeq)) result.PeersSeq = peersSeq;
                if (!result.Ok)
                    result.Error = TokenString(o["error"]);
                if (string.IsNullOrEmpty(result.Error) && !result.Ok)
                    result.Error = "request failed";
                return result;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message ?? "post failed";
                return result;
            }
        }

        private static PollResult ParsePoll(string json)
        {
            var result = new PollResult();
            var o = JObject.Parse(json);
            result.Ok = o["ok"] != null && o["ok"].Type == JTokenType.Boolean && (bool)o["ok"];
            result.Unchanged = o["unchanged"] != null && o["unchanged"].Type == JTokenType.Boolean && (bool)o["unchanged"];
            long seq;
            if (long.TryParse(TokenString(o["seq"]), out seq)) result.Seq = seq;
            long ackSeq;
            if (long.TryParse(TokenString(o["ackSeq"]), out ackSeq)) result.AckSeq = ackSeq;
            long cmdSeq;
            if (long.TryParse(TokenString(o["cmdSeq"]), out cmdSeq)) result.CmdSeq = cmdSeq;
            long peersSeq;
            if (long.TryParse(TokenString(o["peersSeq"]), out peersSeq)) result.PeersSeq = peersSeq;
            if (!result.Ok)
            {
                result.Error = TokenString(o["error"]);
                if (string.IsNullOrEmpty(result.Error)) result.Error = "poll failed";
                return result;
            }
            var latest = o["latestCreds"] as JObject;
            if (latest != null && latest.Type != JTokenType.Null)
            {
                result.LatestCreds = new PeerCredsEvent
                {
                    Seq = ParseLong(latest["seq"]),
                    EventId = TokenString(latest["eventId"]),
                    OriginServerId = TokenString(latest["originServerId"]),
                    OriginName = TokenString(latest["originName"]),
                    CreatedUtc = TokenString(latest["createdUtc"]),
                    PayloadEnc = TokenString(latest["payloadEnc"])
                };
            }
            var cmd = o["latestCommand"] as JObject;
            if (cmd != null && cmd.Type != JTokenType.Null)
            {
                result.LatestCommand = new PeerCommandEvent
                {
                    CmdSeq = ParseLong(cmd["cmdSeq"]),
                    CmdId = TokenString(cmd["cmdId"]),
                    Type = TokenString(cmd["type"]),
                    OriginServerId = TokenString(cmd["originServerId"]),
                    OriginName = TokenString(cmd["originName"]),
                    CreatedUtc = TokenString(cmd["createdUtc"]),
                    PayloadEnc = TokenString(cmd["payloadEnc"])
                };
            }
            var acks = o["acks"] as JArray;
            if (acks != null)
            {
                foreach (var item in acks)
                {
                    var a = item as JObject;
                    if (a == null) continue;
                    result.Acks.Add(new PeerAckEvent
                    {
                        AckSeq = ParseLong(a["ackSeq"]),
                        AckId = TokenString(a["ackId"]),
                        TargetServerId = TokenString(a["targetServerId"]),
                        ForEventId = TokenString(a["forEventId"]),
                        ResponderServerId = TokenString(a["responderServerId"]),
                        ResponderName = TokenString(a["responderName"]),
                        CreatedUtc = TokenString(a["createdUtc"]),
                        PayloadEnc = TokenString(a["payloadEnc"])
                    });
                }
            }
            var peers = o["peers"] as JArray;
            if (peers != null)
            {
                foreach (var item in peers)
                {
                    var p = item as JObject;
                    if (p == null) continue;
                    result.Peers.Add(new PeerRecord
                    {
                        ServerId = TokenString(p["serverId"]),
                        LastSeenUtc = TokenString(p["lastSeenUtc"]),
                        StatusEnc = TokenString(p["statusEnc"])
                    });
                }
            }
            return result;
        }

        private static string HttpGet(string url)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.Timeout = TimeoutMs;
            req.ReadWriteTimeout = TimeoutMs;
            req.AllowAutoRedirect = true;
            req.UserAgent = UserAgent;
            req.Accept = "application/json";
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream() ?? Stream.Null, Encoding.UTF8))
                return sr.ReadToEnd() ?? "";
        }

        private static string HttpPost(string url, string jsonBody)
        {
            var bytes = Encoding.UTF8.GetBytes(jsonBody ?? "{}");
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.Timeout = TimeoutMs;
            req.ReadWriteTimeout = TimeoutMs;
            req.AllowAutoRedirect = true;
            req.UserAgent = UserAgent;
            req.ContentType = "application/json; charset=utf-8";
            req.Accept = "application/json";
            req.ContentLength = bytes.Length;
            using (var s = req.GetRequestStream())
                s.Write(bytes, 0, bytes.Length);
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream() ?? Stream.Null, Encoding.UTF8))
                return sr.ReadToEnd() ?? "";
        }

        private static void EnsureTls12()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
            catch { }
        }

        private static string NormalizeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "";
            return url.Trim();
        }

        private static string TokenString(JToken t)
        {
            if (t == null || t.Type == JTokenType.Null) return "";
            return t.Type == JTokenType.String ? ((string)t ?? "") : t.ToString();
        }

        private static long ParseLong(JToken t)
        {
            if (t == null || t.Type == JTokenType.Null) return 0;
            if (t.Type == JTokenType.Integer) return (long)t;
            long n;
            if (long.TryParse(t.ToString(), out n)) return n;
            return 0;
        }
    }
}
