using System;
using Newtonsoft.Json.Linq;

namespace KaiAosRenew
{
    /// <summary>Pure Firebase RTDB tree / SSE payload parsing (no network) — latestCreds only.</summary>
    public static class PeerSyncFirebaseParse
    {
        public const string RootPrefix = "kai-aos-peer";

        public static bool IsFirebaseDatabaseUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            Uri u;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out u)) return false;
            // HTTPS only: relay payloads must never travel over cleartext HTTP.
            if (!string.Equals(u.Scheme, "https", StringComparison.OrdinalIgnoreCase))
                return false;
            var host = (u.Host ?? "").ToLowerInvariant();
            return host.EndsWith(".firebaseio.com", StringComparison.Ordinal)
                   || host.EndsWith(".firebasedatabase.app", StringComparison.Ordinal);
        }

        /// <summary>Base64 → Base64URL path segment (no padding).</summary>
        public static string AuthTagToPathSegment(string authTag)
        {
            if (string.IsNullOrWhiteSpace(authTag)) return "";
            return authTag.Trim()
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        }

        public static string NormalizeDatabaseUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "";
            var t = url.Trim().TrimEnd('/');
            if (t.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                t = t.Substring(0, t.Length - 5).TrimEnd('/');
            return t;
        }

        public static string BuildRelayRootUrl(string databaseUrl, string secret)
        {
            var db = NormalizeDatabaseUrl(databaseUrl);
            if (string.IsNullOrEmpty(db) || string.IsNullOrEmpty(secret)) return "";
            var tag = AuthTagToPathSegment(PeerSyncCrypto.ComputeAuthTag(secret));
            if (string.IsNullOrEmpty(tag)) return "";
            return db + "/" + RootPrefix + "/" + tag;
        }

        public sealed class RelaySnapshot
        {
            public long Seq;
            public PeerCredsEvent LatestCreds;
        }

        /// <summary>Parse a Firebase root JSON object (or null) into a relay snapshot (latestCreds only).</summary>
        public static RelaySnapshot ParseRoot(JToken root)
        {
            var snap = new RelaySnapshot();
            if (root == null || root.Type == JTokenType.Null) return snap;
            var o = root as JObject;
            if (o == null) return snap;

            var meta = o["meta"] as JObject;
            if (meta != null)
                snap.Seq = ParseLong(meta["seq"]);

            snap.LatestCreds = ParseCreds(o["latestCreds"] as JObject);
            if (snap.LatestCreds != null && snap.LatestCreds.Seq > snap.Seq)
                snap.Seq = snap.LatestCreds.Seq;

            return snap;
        }

        public static RelaySnapshot ParseRootJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Trim() == "null")
                return new RelaySnapshot();
            return ParseRoot(JToken.Parse(json));
        }

        /// <summary>
        /// Parse one Firebase SSE data JSON: {"path":"/…","data":…}.
        /// Returns absolute child path under the relay root ("" for root) and data token.
        /// </summary>
        public static bool TryParseSseData(string dataJson, out string relativePath, out JToken data)
        {
            relativePath = "";
            data = null;
            if (string.IsNullOrWhiteSpace(dataJson)) return false;
            try
            {
                var o = JObject.Parse(dataJson);
                relativePath = NormalizeSsePath(TokenString(o["path"]));
                data = o["data"];
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Parse Firebase leaf JSON for a number (e.g. latestCreds/seq.json → "5" or 5).</summary>
        public static long ParseSeqJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Trim() == "null") return 0;
            try
            {
                var t = JToken.Parse(json);
                return ParseLong(t);
            }
            catch
            {
                long n;
                if (long.TryParse(json.Trim().Trim('"'), out n)) return n;
                return 0;
            }
        }

        public static string NormalizeSsePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path == "/") return "";
            var p = path.Trim();
            if (p.StartsWith("/", StringComparison.Ordinal)) p = p.Substring(1);
            return p.TrimEnd('/');
        }

        /// <summary>True when an SSE put/patch may affect email/password credentials (latestCreds / meta.seq).</summary>
        public static bool SsePathNeedsSnapshot(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return true;
            if (relativePath.StartsWith("latestCreds", StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(relativePath, "meta", StringComparison.OrdinalIgnoreCase)
                || relativePath.StartsWith("meta/seq", StringComparison.OrdinalIgnoreCase)
                || string.Equals(relativePath, "seq", StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        /// <summary>
        /// Parse an SSE event while listening on latestCreds.json (or root).
        /// Returns false if the event should be ignored (keepalive / delete / unparsable).
        /// When true: either <paramref name="creds"/> is set (apply directly) or <paramref name="needsSnapshot"/> is true.
        /// </summary>
        public static bool TryInterpretLatestCredsSse(
            string dataJson, out PeerCredsEvent creds, out bool needsSnapshot)
        {
            creds = null;
            needsSnapshot = false;
            string path;
            JToken data;
            if (!TryParseSseData(dataJson, out path, out data))
                return false;
            if (data == null || data.Type == JTokenType.Null)
                return false;

            // Child field update under latestCreds (e.g. path "seq") — need full GET.
            if (!string.IsNullOrEmpty(path)
                && !string.Equals(path, "latestCreds", StringComparison.OrdinalIgnoreCase)
                && !path.StartsWith("latestCreds/", StringComparison.OrdinalIgnoreCase))
            {
                needsSnapshot = true;
                return true;
            }

            JObject obj = null;
            if (string.Equals(path, "latestCreds", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrEmpty(path))
            {
                obj = data as JObject;
            }
            else if (path.StartsWith("latestCreds/", StringComparison.OrdinalIgnoreCase))
            {
                needsSnapshot = true;
                return true;
            }

            if (obj == null)
            {
                needsSnapshot = true;
                return true;
            }

            creds = ParseCreds(obj);
            if (creds != null) return true;
            needsSnapshot = true;
            return true;
        }

        private static PeerCredsEvent ParseCreds(JObject latest)
        {
            if (latest == null || latest.Type == JTokenType.Null) return null;
            var enc = TokenString(latest["payloadEnc"]);
            if (string.IsNullOrEmpty(enc)) return null;
            return new PeerCredsEvent
            {
                Seq = ParseLong(latest["seq"]),
                EventId = TokenString(latest["eventId"]),
                OriginServerId = TokenString(latest["originServerId"]),
                OriginName = TokenString(latest["originName"]),
                CreatedUtc = TokenString(latest["createdUtc"]),
                PayloadEnc = enc
            };
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
