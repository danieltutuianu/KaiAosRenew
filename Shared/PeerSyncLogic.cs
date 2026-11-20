using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KaiAosRenew
{
    /// <summary>Pure peer-sync decisions (no UI / network) — latestCreds only.</summary>
    public static class PeerSyncLogic
    {
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

        /// <summary>SHA-256 hex of normalized email + newline + password (for skip-identical publish).</summary>
        public static string ComputeCredsHash(string email, string password)
        {
            var norm = (email ?? "").Trim().ToLowerInvariant() + "\n" + (password ?? "");
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(norm));
                var sb = new StringBuilder(bytes.Length * 2);
                for (var i = 0; i < bytes.Length; i++)
                    sb.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        public static bool CredsHashMatches(string email, string password, string storedHash)
        {
            if (string.IsNullOrWhiteSpace(storedHash)) return false;
            return string.Equals(ComputeCredsHash(email, password), storedHash.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        public static int ClampPollMinutes(int minutes)
        {
            if (minutes < 1) return 1;
            if (minutes > 1440) return 1440;
            return minutes;
        }

        public const string SyncModeOnDemand = "ondemand";
        /// <summary>Hybrid: on-demand triggers + periodic poll.</summary>
        public const string SyncModePoll = "poll";
        public const string SyncModeSse = "sse";

        /// <summary>Normalize Firebase sync mode to ondemand / poll hybrid (legacy sse maps to ondemand).</summary>
        public static string NormalizeFirebasePushSyncMode(string mode)
        {
            var m = (mode ?? "").Trim().ToLowerInvariant();
            if (m == SyncModePoll || m == SyncModeOnDemand)
                return m;
            return SyncModeOnDemand;
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
    }
}
