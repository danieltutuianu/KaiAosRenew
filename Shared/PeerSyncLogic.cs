using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace KaiAosRenew
{
    /// <summary>Pure peer-sync decisions (no UI / network).</summary>
    public static class PeerSyncLogic
    {
        public const string HealthOnline = "Online";
        public const string HealthStale = "Stale";
        public const string HealthOffline = "Offline";
        public const string CommandTypeUpdateAll = "updateAll";

        public static bool ShouldIgnoreOwnEvent(string originServerId, string localServerId)
        {
            if (string.IsNullOrWhiteSpace(originServerId) || string.IsNullOrWhiteSpace(localServerId))
                return false;
            return string.Equals(originServerId.Trim(), localServerId.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsNewerSeq(long eventSeq, long lastProcessedSeq)
        {
            return eventSeq > lastProcessedSeq;
        }

        public static bool IsDuplicateEvent(string eventId, string lastEventId)
        {
            if (string.IsNullOrWhiteSpace(eventId)) return false;
            return string.Equals(eventId.Trim(), (lastEventId ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsDuplicateAck(string ackId, string lastAckId)
        {
            if (string.IsNullOrWhiteSpace(ackId)) return false;
            return string.Equals(ackId.Trim(), (lastAckId ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Commit only when Register actually succeeded with the new credentials
        /// (not soft "token still valid" / skipped Register).
        /// </summary>
        public static bool ShouldCommitCredentials(bool registerOk, bool registerAttempted, bool registerAuthRejected, bool skippedRegister)
        {
            return registerOk && registerAttempted && !registerAuthRejected && !skippedRegister;
        }

        public static bool ShouldPublishAfterLocalRegister(bool registerOk, bool registerAttempted, bool registerAuthRejected, bool skippedRegister)
        {
            return ShouldCommitCredentials(registerOk, registerAttempted, registerAuthRejected, skippedRegister);
        }

        public static bool CredentialsDiffer(string emailA, string passA, string emailB, string passB)
        {
            if (!string.Equals((emailA ?? "").Trim(), (emailB ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                return true;
            return !string.Equals((passA ?? "").Trim(), (passB ?? "").Trim(), StringComparison.Ordinal);
        }

        public static int ClampPollMinutes(int minutes)
        {
            if (minutes < 1) return 1;
            if (minutes > 1440) return 1440;
            return minutes;
        }

        public static bool ShouldProcessInboundCreds(
            PeerCredsEvent ev,
            string localServerId,
            long lastSeq,
            string lastEventId)
        {
            if (ev == null) return false;
            if (ShouldIgnoreOwnEvent(ev.OriginServerId, localServerId)) return false;
            if (IsDuplicateEvent(ev.EventId, lastEventId)) return false;
            if (!IsNewerSeq(ev.Seq, lastSeq)) return false;
            return !string.IsNullOrWhiteSpace(ev.PayloadEnc);
        }

        public static bool ShouldProcessInboundAck(
            PeerAckEvent ack,
            string localServerId,
            long lastAckSeq)
        {
            if (ack == null) return false;
            if (!string.IsNullOrWhiteSpace(ack.TargetServerId)
                && !string.Equals(ack.TargetServerId.Trim(), (localServerId ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                return false;
            if (ack.AckSeq <= lastAckSeq) return false;
            return !string.IsNullOrWhiteSpace(ack.PayloadEnc);
        }

        public static bool ShouldProcessInboundCommand(
            PeerCommandEvent ev,
            string localServerId,
            long lastCmdSeq,
            string lastCmdId)
        {
            if (ev == null) return false;
            if (ShouldIgnoreOwnEvent(ev.OriginServerId, localServerId)) return false;
            if (IsDuplicateEvent(ev.CmdId, lastCmdId)) return false;
            if (!IsNewerSeq(ev.CmdSeq, lastCmdSeq)) return false;
            return !string.IsNullOrWhiteSpace(ev.PayloadEnc);
        }

        /// <summary>
        /// Online: seen within 2× poll interval (min 10 min).
        /// Stale: seen within 24h.
        /// Offline: older / missing.
        /// </summary>
        public static string ClassifyPeerHealth(string lastSeenUtc, int pollMinutes, DateTime utcNow)
        {
            DateTime seen;
            if (!TryParseUtc(lastSeenUtc, out seen))
                return HealthOffline;
            var age = utcNow.ToUniversalTime() - seen.ToUniversalTime();
            if (age.TotalSeconds < 0) age = TimeSpan.Zero;
            var minutes = ClampPollMinutes(pollMinutes);
            var onlineWindow = TimeSpan.FromMinutes(Math.Max(10, minutes * 2));
            if (age <= onlineWindow) return HealthOnline;
            if (age <= TimeSpan.FromHours(24)) return HealthStale;
            return HealthOffline;
        }

        public static List<PeerRecord> SortPeersForDisplay(IEnumerable<PeerRecord> peers, string localServerId)
        {
            var list = new List<PeerRecord>();
            if (peers != null)
            {
                foreach (var p in peers)
                    if (p != null) list.Add(p);
            }
            list.Sort(delegate(PeerRecord a, PeerRecord b)
            {
                var aLocal = ShouldIgnoreOwnEvent(a.ServerId, localServerId);
                var bLocal = ShouldIgnoreOwnEvent(b.ServerId, localServerId);
                if (aLocal != bLocal) return aLocal ? -1 : 1;
                var aName = (a.Status != null ? a.Status.ServerName : null) ?? a.ServerId ?? "";
                var bName = (b.Status != null ? b.Status.ServerName : null) ?? b.ServerId ?? "";
                return string.Compare(aName, bName, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        public static bool TryParseUtc(string iso, out DateTime utc)
        {
            utc = DateTime.MinValue;
            if (string.IsNullOrWhiteSpace(iso)) return false;
            DateTime parsed;
            if (DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed))
            {
                utc = parsed.ToUniversalTime();
                return true;
            }
            if (DateTime.TryParse(iso, out parsed))
            {
                utc = parsed.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
                    : parsed.ToUniversalTime();
                return true;
            }
            return false;
        }
    }

    /// <summary>Wire row for peerStatusSync (server ↔ client LAN).</summary>
    public sealed class PeerDashboardWire
    {
        public string ServerId { get; set; }
        public string ServerName { get; set; }
        public string Health { get; set; }
        public string TokenStatus { get; set; }
        public string AppVersion { get; set; }
        public string ClientCount { get; set; }
        public string LastSeen { get; set; }
        public string LastSync { get; set; }

        public static string Encode(IList<PeerDashboardWire> rows)
        {
            var sb = new StringBuilder();
            if (rows == null) return "";
            for (var i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                if (r == null) continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(Escape(r.ServerId)).Append('|')
                    .Append(Escape(r.ServerName)).Append('|')
                    .Append(Escape(r.Health)).Append('|')
                    .Append(Escape(r.TokenStatus)).Append('|')
                    .Append(Escape(r.AppVersion)).Append('|')
                    .Append(Escape(r.ClientCount)).Append('|')
                    .Append(Escape(r.LastSeen)).Append('|')
                    .Append(Escape(r.LastSync));
            }
            return sb.ToString();
        }

        public static List<PeerDashboardWire> Parse(string text)
        {
            var list = new List<PeerDashboardWire>();
            if (string.IsNullOrEmpty(text)) return list;
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = SplitFields(line);
                if (parts.Count < 4) continue;
                list.Add(new PeerDashboardWire
                {
                    ServerId = parts.Count > 0 ? parts[0] : "",
                    ServerName = parts.Count > 1 ? parts[1] : "",
                    Health = parts.Count > 2 ? parts[2] : "",
                    TokenStatus = parts.Count > 3 ? parts[3] : "",
                    AppVersion = parts.Count > 4 ? parts[4] : "",
                    ClientCount = parts.Count > 5 ? parts[5] : "",
                    LastSeen = parts.Count > 6 ? parts[6] : "",
                    LastSync = parts.Count > 7 ? parts[7] : ""
                });
            }
            return list;
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("|", "\\|").Replace("\n", "\\n").Replace("\r", "");
        }

        private static List<string> SplitFields(string line)
        {
            var parts = new List<string>();
            var sb = new StringBuilder();
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (c == '\\' && i + 1 < line.Length)
                {
                    var n = line[i + 1];
                    if (n == 'n') { sb.Append('\n'); i++; continue; }
                    if (n == '|' || n == '\\') { sb.Append(n); i++; continue; }
                }
                if (c == '|')
                {
                    parts.Add(sb.ToString());
                    sb.Clear();
                    continue;
                }
                sb.Append(c);
            }
            parts.Add(sb.ToString());
            return parts;
        }
    }
}
