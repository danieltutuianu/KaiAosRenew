using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KaiAosRenew
{
    /// <summary>Firebase Realtime Database REST + SSE client for peer credential relay.</summary>
    internal sealed class PeerSyncFirebaseClient
    {
        private const int TimeoutMs = 15000;
        private const string UserAgent = "KaiAosRenew-FirebasePush/1.0";
        private const int MaxAcksKept = 50;

        public static Task<PeerRelayPollResult> SnapshotAsync(
            string databaseUrl, string secret, string filterServerId, CancellationToken ct)
        {
            return Task.Run(() => Snapshot(databaseUrl, secret, filterServerId), ct);
        }

        public static PeerRelayPollResult Snapshot(string databaseUrl, string secret, string filterServerId)
        {
            var result = new PeerRelayPollResult();
            try
            {
                EnsureTls12();
                var root = PeerSyncFirebaseParse.BuildRelayRootUrl(databaseUrl, secret);
                if (string.IsNullOrEmpty(root))
                {
                    result.Error = "Missing URL or secret";
                    return result;
                }
                var json = HttpGet(root + ".json");
                var snap = PeerSyncFirebaseParse.ParseRootJson(json);
                return ToPollResult(snap, filterServerId);
            }
            catch (Exception ex)
            {
                result.Error = ex.Message ?? "snapshot failed";
                return result;
            }
        }

        public static Task<PeerRelayActionResult> PublishAsync(
            string databaseUrl, string secret, string eventId,
            string originServerId, string originName, string payloadEnc, string statusEnc,
            CancellationToken ct)
        {
            return Task.Run(() => Publish(databaseUrl, secret, eventId, originServerId, originName, payloadEnc, statusEnc), ct);
        }

        public static PeerRelayActionResult Publish(
            string databaseUrl, string secret, string eventId,
            string originServerId, string originName, string payloadEnc, string statusEnc)
        {
            return Mutate(databaseUrl, secret, originServerId, originName, statusEnc, root =>
            {
                var latest = root["latestCreds"] as JObject;
                if (latest != null
                    && string.Equals(TokenString(latest["eventId"]), eventId ?? "", StringComparison.OrdinalIgnoreCase))
                {
                    return new MutateOutcome { Duplicate = true };
                }
                var meta = EnsureMeta(root);
                var seq = ParseLong(meta["seq"]) + 1;
                meta["seq"] = seq;
                var creds = new JObject();
                creds["seq"] = seq;
                creds["eventId"] = eventId ?? "";
                creds["originServerId"] = originServerId ?? "";
                creds["originName"] = originName ?? "";
                creds["createdUtc"] = DateTime.UtcNow.ToString("o");
                creds["payloadEnc"] = payloadEnc ?? "";
                root["latestCreds"] = creds;
                return new MutateOutcome();
            });
        }

        public static Task<PeerRelayActionResult> AckAsync(
            string databaseUrl, string secret, string ackId, string forEventId,
            string targetServerId, string responderServerId, string responderName,
            string payloadEnc, string statusEnc, CancellationToken ct)
        {
            return Task.Run(() => Ack(databaseUrl, secret, ackId, forEventId, targetServerId,
                responderServerId, responderName, payloadEnc, statusEnc), ct);
        }

        public static PeerRelayActionResult Ack(
            string databaseUrl, string secret, string ackId, string forEventId,
            string targetServerId, string responderServerId, string responderName,
            string payloadEnc, string statusEnc)
        {
            return Mutate(databaseUrl, secret, responderServerId, responderName, statusEnc, root =>
            {
                var acks = root["acks"] as JObject;
                if (acks == null)
                {
                    acks = new JObject();
                    root["acks"] = acks;
                }
                if (acks[ackId] != null)
                    return new MutateOutcome { Duplicate = true };

                var meta = EnsureMeta(root);
                var ackSeq = ParseLong(meta["ackSeq"]) + 1;
                meta["ackSeq"] = ackSeq;
                var key = string.IsNullOrEmpty(ackId) ? Guid.NewGuid().ToString("N") : ackId;
                var ack = new JObject();
                ack["ackSeq"] = ackSeq;
                ack["ackId"] = ackId ?? "";
                ack["targetServerId"] = targetServerId ?? "";
                ack["forEventId"] = forEventId ?? "";
                ack["responderServerId"] = responderServerId ?? "";
                ack["responderName"] = responderName ?? "";
                ack["createdUtc"] = DateTime.UtcNow.ToString("o");
                ack["payloadEnc"] = payloadEnc ?? "";
                acks[key] = ack;
                PruneAcks(acks, MaxAcksKept);
                return new MutateOutcome();
            });
        }

        public static Task<PeerRelayActionResult> HeartbeatAsync(
            string databaseUrl, string secret, string serverId, string serverName,
            string statusEnc, CancellationToken ct)
        {
            return Task.Run(() => Heartbeat(databaseUrl, secret, serverId, serverName, statusEnc), ct);
        }

        public static PeerRelayActionResult Heartbeat(
            string databaseUrl, string secret, string serverId, string serverName, string statusEnc)
        {
            return Mutate(databaseUrl, secret, serverId, serverName, statusEnc, root => new MutateOutcome());
        }

        public static Task<PeerRelayActionResult> PublishCommandAsync(
            string databaseUrl, string secret, string cmdId, string type,
            string originServerId, string originName, string payloadEnc, string statusEnc,
            CancellationToken ct)
        {
            return Task.Run(() => PublishCommand(databaseUrl, secret, cmdId, type,
                originServerId, originName, payloadEnc, statusEnc), ct);
        }

        public static PeerRelayActionResult PublishCommand(
            string databaseUrl, string secret, string cmdId, string type,
            string originServerId, string originName, string payloadEnc, string statusEnc)
        {
            return Mutate(databaseUrl, secret, originServerId, originName, statusEnc, root =>
            {
                var latest = root["latestCommand"] as JObject;
                if (latest != null
                    && string.Equals(TokenString(latest["cmdId"]), cmdId ?? "", StringComparison.OrdinalIgnoreCase))
                {
                    return new MutateOutcome { Duplicate = true };
                }
                var meta = EnsureMeta(root);
                var cmdSeq = ParseLong(meta["cmdSeq"]) + 1;
                meta["cmdSeq"] = cmdSeq;
                var cmd = new JObject();
                cmd["cmdSeq"] = cmdSeq;
                cmd["cmdId"] = cmdId ?? "";
                cmd["type"] = type ?? "";
                cmd["originServerId"] = originServerId ?? "";
                cmd["originName"] = originName ?? "";
                cmd["createdUtc"] = DateTime.UtcNow.ToString("o");
                cmd["payloadEnc"] = payloadEnc ?? "";
                root["latestCommand"] = cmd;
                return new MutateOutcome();
            });
        }

        public static Task<PeerRelayActionResult> PingAsync(string databaseUrl, string secret, CancellationToken ct)
        {
            return Task.Run(() => Ping(databaseUrl, secret), ct);
        }

        public static PeerRelayActionResult Ping(string databaseUrl, string secret)
        {
            var result = new PeerRelayActionResult();
            try
            {
                EnsureTls12();
                var rootUrl = PeerSyncFirebaseParse.BuildRelayRootUrl(databaseUrl, secret);
                if (string.IsNullOrEmpty(rootUrl))
                {
                    result.Error = "Missing URL or secret";
                    return result;
                }
                var json = HttpGet(rootUrl + "/meta.json");
                JObject meta;
                if (string.IsNullOrWhiteSpace(json) || json.Trim() == "null")
                {
                    meta = NewMetaObject();
                    HttpPut(rootUrl + "/meta.json", meta.ToString(Formatting.None));
                }
                else
                {
                    meta = JObject.Parse(json) ?? new JObject();
                }
                result.Ok = true;
                result.Seq = ParseLong(meta["seq"]);
                result.AckSeq = ParseLong(meta["ackSeq"]);
                result.CmdSeq = ParseLong(meta["cmdSeq"]);
                result.PeersSeq = ParseLong(meta["peersSeq"]);
                return result;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message ?? "ping failed";
                return result;
            }
        }

        /// <summary>
        /// Long-lived SSE listen. Invokes onChange when put/patch may affect relay state.
        /// Reconnect/backoff is owned by the caller.
        /// </summary>
        public static void Listen(
            string databaseUrl,
            string secret,
            Action onChange,
            CancellationToken ct)
        {
            EnsureTls12();
            var root = PeerSyncFirebaseParse.BuildRelayRootUrl(databaseUrl, secret);
            if (string.IsNullOrEmpty(root))
                throw new InvalidOperationException("Missing URL or secret");

            var req = (HttpWebRequest)WebRequest.Create(root + ".json");
            req.Method = "GET";
            req.Timeout = Timeout.Infinite;
            req.ReadWriteTimeout = Timeout.Infinite;
            req.AllowAutoRedirect = true;
            req.UserAgent = UserAgent;
            req.Accept = "text/event-stream";
            req.KeepAlive = true;

            using (ct.Register(() =>
            {
                try { req.Abort(); }
                catch { }
            }))
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var stream = resp.GetResponseStream())
            {
                if (stream == null) return;
                using (var sr = new StreamReader(stream, Encoding.UTF8))
                {
                    string eventName = "message";
                    var data = new StringBuilder();
                    while (!ct.IsCancellationRequested)
                    {
                        var line = sr.ReadLine();
                        if (line == null) break;
                        if (line.Length == 0)
                        {
                            DispatchSse(eventName, data.ToString(), onChange);
                            eventName = "message";
                            data.Clear();
                            continue;
                        }
                        if (line.StartsWith(":", StringComparison.Ordinal))
                            continue;
                        if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
                        {
                            eventName = line.Substring(6).Trim();
                            continue;
                        }
                        if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                        {
                            if (data.Length > 0) data.Append('\n');
                            data.Append(line.Substring(5).TrimStart());
                        }
                    }
                }
            }
        }

        public static Task ListenAsync(
            string databaseUrl, string secret, Action onChange, CancellationToken ct)
        {
            return Task.Run(() => Listen(databaseUrl, secret, onChange, ct), ct);
        }

        private static void DispatchSse(string eventName, string dataJson, Action onChange)
        {
            if (onChange == null) return;
            var ev = (eventName ?? "").Trim().ToLowerInvariant();
            if (ev == "keep-alive" || ev == "cancel" || string.IsNullOrWhiteSpace(dataJson))
                return;
            if (ev != "put" && ev != "patch") return;

            string path;
            JToken data;
            if (!PeerSyncFirebaseParse.TryParseSseData(dataJson, out path, out data))
                return;
            if (!PeerSyncFirebaseParse.SsePathNeedsSnapshot(path))
                return;
            try { onChange(); }
            catch { }
        }

        private sealed class MutateOutcome
        {
            public bool Duplicate;
        }

        private static PeerRelayActionResult Mutate(
            string databaseUrl,
            string secret,
            string serverId,
            string serverName,
            string statusEnc,
            Func<JObject, MutateOutcome> mutate)
        {
            var result = new PeerRelayActionResult();
            try
            {
                EnsureTls12();
                var rootUrl = PeerSyncFirebaseParse.BuildRelayRootUrl(databaseUrl, secret);
                if (string.IsNullOrEmpty(rootUrl))
                {
                    result.Error = "Missing URL or secret";
                    return result;
                }
                var json = HttpGet(rootUrl + ".json");
                JObject root;
                if (string.IsNullOrWhiteSpace(json) || json.Trim() == "null")
                    root = new JObject();
                else
                    root = JObject.Parse(json) ?? new JObject();

                var outcome = mutate(root) ?? new MutateOutcome();
                UpsertPeer(root, serverId, statusEnc);
                HttpPut(rootUrl + ".json", root.ToString(Formatting.None));

                var meta = EnsureMeta(root);
                result.Ok = true;
                result.Duplicate = outcome.Duplicate;
                result.Seq = ParseLong(meta["seq"]);
                result.AckSeq = ParseLong(meta["ackSeq"]);
                result.CmdSeq = ParseLong(meta["cmdSeq"]);
                result.PeersSeq = ParseLong(meta["peersSeq"]);
                return result;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message ?? "mutate failed";
                return result;
            }
        }

        private static void UpsertPeer(JObject root, string serverId, string statusEnc)
        {
            if (string.IsNullOrWhiteSpace(serverId)) return;
            var peers = root["peers"] as JObject;
            if (peers == null)
            {
                peers = new JObject();
                root["peers"] = peers;
            }
            var meta = EnsureMeta(root);
            meta["peersSeq"] = ParseLong(meta["peersSeq"]) + 1;
            var peer = new JObject();
            peer["serverId"] = serverId;
            peer["lastSeenUtc"] = DateTime.UtcNow.ToString("o");
            peer["statusEnc"] = statusEnc ?? "";
            peers[serverId] = peer;
        }

        private static JObject NewMetaObject()
        {
            var meta = new JObject();
            meta["seq"] = 0;
            meta["ackSeq"] = 0;
            meta["cmdSeq"] = 0;
            meta["peersSeq"] = 0;
            return meta;
        }

        private static JObject EnsureMeta(JObject root)
        {
            var meta = root["meta"] as JObject;
            if (meta == null)
            {
                meta = NewMetaObject();
                root["meta"] = meta;
            }
            return meta;
        }

        private static void PruneAcks(JObject acks, int keep)
        {
            if (acks == null || acks.Count <= keep) return;
            var list = new System.Collections.Generic.List<Tuple<long, string>>();
            foreach (var p in acks.Properties())
            {
                var o = p.Value as JObject;
                list.Add(Tuple.Create(o != null ? ParseLong(o["ackSeq"]) : 0L, p.Name));
            }
            list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            var drop = list.Count - keep;
            for (var i = 0; i < drop; i++)
                acks.Remove(list[i].Item2);
        }

        private static PeerRelayPollResult ToPollResult(
            PeerSyncFirebaseParse.RelaySnapshot snap, string filterServerId)
        {
            var result = new PeerRelayPollResult { Ok = true };
            if (snap == null) return result;
            result.Seq = snap.Seq;
            result.AckSeq = snap.AckSeq;
            result.CmdSeq = snap.CmdSeq;
            result.PeersSeq = snap.PeersSeq;
            result.LatestCreds = snap.LatestCreds;
            result.LatestCommand = snap.LatestCommand;
            result.Peers = snap.Peers ?? new System.Collections.Generic.List<PeerRecord>();
            if (snap.Acks != null)
            {
                foreach (var a in snap.Acks)
                {
                    if (a == null) continue;
                    if (!string.IsNullOrWhiteSpace(filterServerId)
                        && !string.IsNullOrWhiteSpace(a.TargetServerId)
                        && !string.Equals(a.TargetServerId.Trim(), filterServerId.Trim(), StringComparison.OrdinalIgnoreCase))
                        continue;
                    result.Acks.Add(a);
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

        private static string HttpPut(string url, string jsonBody)
        {
            var bytes = Encoding.UTF8.GetBytes(jsonBody ?? "null");
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "PUT";
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
