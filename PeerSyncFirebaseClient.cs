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
    /// <summary>Firebase RTDB REST + SSE — credential relay only (latestCreds).</summary>
    internal sealed class PeerSyncFirebaseClient
    {
        private const int TimeoutMs = 15000;
        private const string UserAgent = "KaiAosRenew-FirebasePush/1.0";

        public static Task<PeerRelayPollResult> SnapshotAsync(
            string databaseUrl, string secret, string ifNoneMatch, bool forceRefresh, CancellationToken ct)
        {
            return Task.Run(() => Snapshot(databaseUrl, secret, ifNoneMatch, forceRefresh), ct);
        }

        public static PeerRelayPollResult Snapshot(
            string databaseUrl, string secret, string ifNoneMatch, bool forceRefresh)
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

                string etag;
                int status;
                var credsJson = HttpGetWithEtag(
                    root + "/latestCreds.json",
                    forceRefresh ? null : ifNoneMatch,
                    out etag,
                    out status);
                result.ETag = etag ?? "";
                if (status == 304)
                {
                    result.Ok = true;
                    result.Unchanged = true;
                    return result;
                }

                var wrap = new JObject();
                if (!string.IsNullOrWhiteSpace(credsJson) && credsJson.Trim() != "null")
                {
                    try { wrap["latestCreds"] = JToken.Parse(credsJson); }
                    catch { }
                }
                var snap = PeerSyncFirebaseParse.ParseRoot(wrap);
                return ToPollResult(snap, etag);
            }
            catch (Exception ex)
            {
                result.Error = ex.Message ?? "snapshot failed";
                return result;
            }
        }

        public static Task<PeerRelayPollResult> CheckLatestSeqAsync(
            string databaseUrl, string secret, string ifNoneMatch, CancellationToken ct)
        {
            return Task.Run(() => CheckLatestSeq(databaseUrl, secret, ifNoneMatch), ct);
        }

        public static PeerRelayPollResult CheckLatestSeq(string databaseUrl, string secret, string ifNoneMatch)
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

                string etag;
                int status;
                var json = HttpGetWithEtag(root + "/latestCreds/seq.json", ifNoneMatch, out etag, out status);
                result.ETag = etag ?? "";
                if (status == 304)
                {
                    result.Ok = true;
                    result.Unchanged = true;
                    return result;
                }

                result.Ok = true;
                result.Seq = PeerSyncFirebaseParse.ParseSeqJson(json);
                return result;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message ?? "seq check failed";
                return result;
            }
        }

        public static Task<PeerRelayActionResult> PublishAsync(
            string databaseUrl, string secret, string eventId,
            string originServerId, string originName, string payloadEnc,
            long minSeq, CancellationToken ct)
        {
            return Task.Run(() => Publish(databaseUrl, secret, eventId, originServerId, originName, payloadEnc, minSeq, true), ct);
        }

        public static PeerRelayActionResult Publish(
            string databaseUrl, string secret, string eventId,
            string originServerId, string originName, string payloadEnc, long minSeq)
        {
            return Publish(databaseUrl, secret, eventId, originServerId, originName, payloadEnc, minSeq, true);
        }

        public static PeerRelayActionResult Publish(
            string databaseUrl, string secret, string eventId,
            string originServerId, string originName, string payloadEnc, long minSeq, bool checkRemoteSeq)
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

                var unixMs = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
                var seq = Math.Max(minSeq + 1, unixMs);

                var creds = new JObject();
                creds["seq"] = seq;
                creds["eventId"] = eventId ?? "";
                creds["originServerId"] = originServerId ?? "";
                creds["originName"] = originName ?? "";
                creds["createdUtc"] = DateTime.UtcNow.ToString("o");
                creds["payloadEnc"] = payloadEnc ?? "";

                // Best-effort optimistic check (instead of If-Match): we only persist the ETag of
                // latestCreds/seq.json, which cannot be used as If-Match for the latestCreds.json PUT.
                // So GET the tiny seq value first and abort if a peer published something newer than
                // what we have processed (minSeq) — otherwise we would silently overwrite their creds.
                // A failed pre-check (network/parse) never blocks the publish itself.
                if (checkRemoteSeq && minSeq > 0)
                {
                    long remoteSeq = 0;
                    var checkedOk = false;
                    try
                    {
                        remoteSeq = PeerSyncFirebaseParse.ParseSeqJson(HttpGet(rootUrl + "/latestCreds/seq.json"));
                        checkedOk = true;
                    }
                    catch { }
                    if (checkedOk && remoteSeq > minSeq)
                    {
                        result.Seq = remoteSeq;
                        result.Error = "Remote latestCreds is newer (seq " + remoteSeq
                            + " > " + minSeq + "); publish aborted to avoid overwriting peer credentials. Sync first.";
                        return result;
                    }
                }

                // Path-scoped PUT; silent = 0-byte response.
                HttpPut(rootUrl + "/latestCreds.json?print=silent", creds.ToString(Formatting.None));

                result.Ok = true;
                result.Seq = seq;
                return result;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message ?? "publish failed";
                return result;
            }
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
                // Read-only: GET a few bytes from latestCreds/seq.json — never writes.
                var json = HttpGet(rootUrl + "/latestCreds/seq.json");
                result.Ok = true;
                result.Seq = PeerSyncFirebaseParse.ParseSeqJson(json);
                return result;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message ?? "ping failed";
                return result;
            }
        }

        /// <summary>
        /// Long-lived SSE on latestCreds only. Caller owns reconnect/backoff.
        /// onConnected is invoked once the SSE HTTP response is open.
        /// </summary>
        public static void Listen(
            string databaseUrl,
            string secret,
            Action onConnected,
            Action<PeerCredsEvent> onFullCreds,
            Action onNeedsSnapshot,
            CancellationToken ct)
        {
            EnsureTls12();
            var root = PeerSyncFirebaseParse.BuildRelayRootUrl(databaseUrl, secret);
            if (string.IsNullOrEmpty(root))
                throw new InvalidOperationException("Missing URL or secret");

            var req = (HttpWebRequest)WebRequest.Create(root + "/latestCreds.json");
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
                try
                {
                    if (onConnected != null) onConnected();
                }
                catch { }
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
                            DispatchSse(eventName, data.ToString(), onFullCreds, onNeedsSnapshot);
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
            string databaseUrl, string secret,
            Action onConnected,
            Action<PeerCredsEvent> onFullCreds, Action onNeedsSnapshot, CancellationToken ct)
        {
            return Task.Run(() => Listen(databaseUrl, secret, onConnected, onFullCreds, onNeedsSnapshot, ct), ct);
        }

        private static void DispatchSse(
            string eventName, string dataJson,
            Action<PeerCredsEvent> onFullCreds, Action onNeedsSnapshot)
        {
            var ev = (eventName ?? "").Trim().ToLowerInvariant();
            if (ev == "keep-alive" || ev == "cancel" || string.IsNullOrWhiteSpace(dataJson))
                return;
            if (ev != "put" && ev != "patch") return;

            PeerCredsEvent creds;
            bool needsSnapshot;
            if (!PeerSyncFirebaseParse.TryInterpretLatestCredsSse(dataJson, out creds, out needsSnapshot))
                return;
            try
            {
                if (creds != null && onFullCreds != null)
                    onFullCreds(creds);
                else if (needsSnapshot && onNeedsSnapshot != null)
                    onNeedsSnapshot();
            }
            catch { }
        }

        private static PeerRelayPollResult ToPollResult(PeerSyncFirebaseParse.RelaySnapshot snap, string etag)
        {
            var result = new PeerRelayPollResult { Ok = true, ETag = etag ?? "" };
            if (snap == null) return result;
            result.Seq = snap.Seq;
            result.LatestCreds = snap.LatestCreds;
            return result;
        }

        private static string HttpGet(string url)
        {
            string etag;
            int status;
            return HttpGetWithEtag(url, null, out etag, out status);
        }

        private static string HttpGetWithEtag(string url, string ifNoneMatch, out string etag, out int statusCode)
        {
            etag = "";
            statusCode = 0;
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.Timeout = TimeoutMs;
            req.ReadWriteTimeout = TimeoutMs;
            req.AllowAutoRedirect = true;
            req.UserAgent = UserAgent;
            req.Accept = "application/json";
            req.Headers["X-Firebase-ETag"] = "true";
            if (!string.IsNullOrWhiteSpace(ifNoneMatch))
                req.Headers[HttpRequestHeader.IfNoneMatch] = ifNoneMatch.Trim();

            try
            {
                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    statusCode = (int)resp.StatusCode;
                    etag = resp.Headers["ETag"] ?? "";
                    using (var sr = new StreamReader(resp.GetResponseStream() ?? Stream.Null, Encoding.UTF8))
                        return sr.ReadToEnd() ?? "";
                }
            }
            catch (WebException wex)
            {
                var resp = wex.Response as HttpWebResponse;
                if (resp != null)
                {
                    statusCode = (int)resp.StatusCode;
                    etag = resp.Headers["ETag"] ?? "";
                    if (statusCode == 304)
                        return "";
                    try
                    {
                        using (var sr = new StreamReader(resp.GetResponseStream() ?? Stream.Null, Encoding.UTF8))
                            return sr.ReadToEnd() ?? "";
                    }
                    catch { }
                }
                throw;
            }
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
    }
}
