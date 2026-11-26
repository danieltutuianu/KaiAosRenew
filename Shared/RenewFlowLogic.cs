using System;
using System.Net;

namespace KaiAosRenew
{
    /// <summary>Queued work produced while Renew is busy (incl. parked 403 retry VIN).</summary>
    public sealed class PendingRenewWork
    {
        public string Password { get; set; }
        public bool ForceRegister { get; set; }
        public bool IgnoreAuthGuards { get; set; }
        public bool LockRegisterAfter { get; set; }
        public bool StartTimerOnSuccess { get; set; }
        public string Retry403Vin { get; set; }
        public string Retry403ReaderId { get; set; }
    }

    /// <summary>
    /// State machine for queuing Force / 403 Register while a run is active, ensuring
    /// a parked 403 VIN is not consumed by the finishing active run.
    /// </summary>
    public sealed class RenewRunQueueState
    {
        public string PendingForcePassword { get; private set; }
        public bool PendingForceRegister { get; private set; }
        public bool PendingIgnoreAuthGuards { get; private set; }
        public bool PendingLockRegisterAfter { get; private set; }
        public bool PendingStartTimerOnSuccess { get; private set; }
        public string Queued403RetryVin { get; private set; }
        public string Queued403RetryReaderId { get; private set; }

        public bool HasPending
        {
            get { return !string.IsNullOrEmpty(PendingForcePassword) || PendingForceRegister; }
        }

        public void Clear403Queued()
        {
            Queued403RetryVin = null;
            Queued403RetryReaderId = null;
        }

        public void Enqueue(
            string password,
            bool ignoreAuthGuards,
            bool lockRegisterAfter,
            bool startTimerOnSuccess,
            ref string active403Vin,
            ref string active403ReaderId)
        {
            if (!string.IsNullOrEmpty(active403Vin))
            {
                Queued403RetryVin = active403Vin;
                Queued403RetryReaderId = active403ReaderId;
                active403Vin = null;
                active403ReaderId = null;
            }
            PendingForcePassword = password;
            PendingForceRegister = true;
            PendingIgnoreAuthGuards = PendingIgnoreAuthGuards || ignoreAuthGuards;
            PendingLockRegisterAfter = PendingLockRegisterAfter || lockRegisterAfter;
            PendingStartTimerOnSuccess = PendingStartTimerOnSuccess || startTimerOnSuccess;
        }

        public bool TryDequeue(out PendingRenewWork work)
        {
            work = null;
            if (!HasPending)
                return false;
            work = new PendingRenewWork
            {
                Password = PendingForcePassword,
                ForceRegister = PendingForceRegister,
                IgnoreAuthGuards = PendingIgnoreAuthGuards,
                LockRegisterAfter = PendingLockRegisterAfter,
                StartTimerOnSuccess = PendingStartTimerOnSuccess,
                Retry403Vin = Queued403RetryVin,
                Retry403ReaderId = Queued403RetryReaderId
            };
            PendingForcePassword = null;
            PendingForceRegister = false;
            PendingIgnoreAuthGuards = false;
            PendingLockRegisterAfter = false;
            PendingStartTimerOnSuccess = false;
            Queued403RetryVin = null;
            Queued403RetryReaderId = null;
            return true;
        }
    }

    /// <summary>Client AOS ack/timeout dirty-state & slow-command rules (no UI).</summary>
    public static class AosAckDecisionLogic
    {
        public static bool IsDownloadAckText(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return text.StartsWith("getKeyBin", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("getZip", StringComparison.OrdinalIgnoreCase)
                || text.IndexOf("Bin not found", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("Zip too large", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("Folder missing", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("Folder empty", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("Unknown zip", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsSlowAosCmd(string cmd)
        {
            if (string.IsNullOrEmpty(cmd)) return false;
            var c = cmd.Trim();
            return c.StartsWith("fixServices", StringComparison.OrdinalIgnoreCase)
                || c.StartsWith("restartServices", StringComparison.OrdinalIgnoreCase)
                || c.StartsWith("serverUpdate", StringComparison.OrdinalIgnoreCase)
                || c.StartsWith("forceRegister", StringComparison.OrdinalIgnoreCase)
                || c.StartsWith("firebasePush", StringComparison.OrdinalIgnoreCase);
        }

        public static int TimeoutSecondsForCmd(string cmd)
        {
            return IsSlowAosCmd(cmd) ? 90 : 10;
        }

        public static bool ResolveDirtyOnAck(bool currentDirty, bool isError, bool wasCredsSave, bool intervalOnlyAck)
        {
            if (!wasCredsSave || intervalOnlyAck)
                return currentDirty;
            return isError;
        }

        public static bool ResolveDirtyOnTimeout(bool currentDirty, bool wasCredsSave, bool intervalOnlyAck)
        {
            if (wasCredsSave && !intervalOnlyAck)
                return true;
            return currentDirty;
        }
    }

    /// <summary>Server/client Keys alert and Clear-Keys cutoff rules (no UI).</summary>
    public static class KeysAlertLogic
    {
        public static bool IsAfterClear(DateTime detectedLocal, DateTime? clearedAfterLocal)
        {
            if (!clearedAfterLocal.HasValue) return true;
            return detectedLocal > clearedAfterLocal.Value;
        }

        public static bool ShouldAlertNewestKey(
            bool hasEvent,
            int statusCode,
            bool localSent,
            bool bmwUploaded,
            bool failed,
            DateTime detectedLocal,
            DateTime nowLocal,
            bool stallEnabled,
            int stallMinutes,
            double maxAlertMinutes)
        {
            if (!hasEvent) return false;
            var ageMin = (nowLocal - detectedLocal).TotalMinutes;
            if (ageMin >= maxAlertMinutes) return false;
            // KeyEventStatus: Failed=4, Waiting=1
            if (statusCode == 4 || statusCode == 1)
                return true;
            if (stallMinutes < 1) stallMinutes = 1;
            return stallEnabled && localSent && !bmwUploaded && !failed && ageMin > stallMinutes;
        }
    }

    /// <summary>Shared version comparison + finite-timeout WebClient (used by UpdateService + tests).</summary>
    public static class UpdateFeedHelper
    {
        public static bool IsNewerVersion(string candidate, string current)
        {
            Version c;
            Version cur;
            if (!TryParseVersion(candidate, out c) || !TryParseVersion(current, out cur))
                return false;
            return c > cur;
        }

        public static bool TryParseVersion(string text, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var s = text.Trim();
            var cut = s.IndexOfAny(new[] { '-', '+' });
            if (cut > 0) s = s.Substring(0, cut);
            var parts = s.Split('.');
            if (parts.Length < 2 || parts.Length > 4) return false;
            try
            {
                version = new Version(s);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string FormatVersion(Version v)
        {
            if (v == null) return "?";
            if (v.Build < 0) return v.Major + "." + v.Minor;
            if (v.Revision < 0 || v.Revision == 0)
                return v.Major + "." + v.Minor + "." + v.Build;
            return v.Major + "." + v.Minor + "." + v.Build + "." + v.Revision;
        }

        /// <summary>WebClient with finite connect/read timeout (default WebClient is ~100s).</summary>
        public sealed class TimeoutWebClient : WebClient
        {
            private readonly int _timeoutMs;

            public TimeoutWebClient(int timeoutMs)
            {
                _timeoutMs = timeoutMs < 1000 ? 1000 : timeoutMs;
            }

            protected override WebRequest GetWebRequest(Uri address)
            {
                var request = base.GetWebRequest(address);
                if (request != null)
                {
                    request.Timeout = _timeoutMs;
                    var http = request as HttpWebRequest;
                    if (http != null)
                        http.ReadWriteTimeout = _timeoutMs;
                }
                return request;
            }
        }
    }

    /// <summary>Clipboard paste hygiene for password fields.</summary>
    public static class ClipboardText
    {
        public static string NormalizePassword(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            var s = raw.Trim().Trim('\0');
            if (s.Length > 0 && s[0] == '\uFEFF')
                s = s.Substring(1);
            s = s.Replace("\u200B", "").Replace("\u200C", "").Replace("\u200D", "")
                .Replace("\u00A0", " ").Trim();
            return s;
        }
    }

    /// <summary>Display-only password masking (never use for logic / comparison).</summary>
    public static class PasswordDisplayMask
    {
        /// <summary>First character + bullets (fixed length cap), or "—" when empty.</summary>
        public static string Mask(string password)
        {
            if (string.IsNullOrEmpty(password)) return "\u2014";
            const int maxBullets = 8;
            var n = Math.Min(password.Length - 1, maxBullets);
            if (n < 1) n = 1;
            return password.Substring(0, 1) + new string('\u2022', n);
        }
    }

    /// <summary>Auto-Register cooldown + DNS Health retry scheduling (no UI).</summary>
    public static class DnsHealthRetryLogic
    {
        /// <summary>Stamp 12h auto-Register cooldown only when Register API was actually invoked.</summary>
        public static bool ShouldStampAutoRegisterCooldown(bool registerAttempted)
        {
            return registerAttempted;
        }

        /// <summary>
        /// Minutes until next Health after consecutive DNS fails (1→5, 2→10, 3+→15).
        /// </summary>
        public static int NextRetryMinutes(int consecutiveDnsFails)
        {
            if (consecutiveDnsFails <= 1) return 5;
            if (consecutiveDnsFails == 2) return 10;
            return 15;
        }

        /// <summary>Default consecutive Health/renew fails before trying Firebase latestCreds.</summary>
        public const int FirebasePullHealthFailThreshold = 3;

        /// <summary>True when consecutive Health/renew fails reach the Firebase pull threshold.</summary>
        public static bool ShouldTryFirebasePullOnHealthStreak(
            int healthFailStreak,
            int threshold = FirebasePullHealthFailThreshold)
        {
            if (threshold < 1) threshold = FirebasePullHealthFailThreshold;
            return healthFailStreak >= threshold;
        }
    }
}
