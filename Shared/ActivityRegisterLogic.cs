using System;
using System.Collections.Generic;
using System.Globalization;

namespace KaiAosRenew
{
    /// <summary>Activity / Register classify + filter helpers (no UI).</summary>
    public static class ActivityRegisterLogic
    {
        /// <summary>Today + previous (N-1) calendar days.</summary>
        public const int LogRetentionCalendarDays = 3;

        public enum ActivityLogLevel
        {
            Info = 0,
            Success = 1,
            Warning = 2,
            Error = 3,
            Verbose = 4
        }

        public sealed class RegisterHistoryFilterRow
        {
            public DateTime At { get; set; }
            public int Kind { get; set; }
            public string Text { get; set; }
            public string ResultText { get; set; }
        }

        public static DateTime LogRetentionCutoffLocal()
        {
            return DateTime.Now.Date.AddDays(-(LogRetentionCalendarDays - 1));
        }

        public static bool IsWithinLogRetention(DateTime local)
        {
            return local >= LogRetentionCutoffLocal();
        }

        /// <summary>Notify connection chatter — keep on disk, hide from Activity UI.</summary>
        public static bool IsActivityUiNoise(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return text.StartsWith("Notify client connected", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("Notify client socket", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("Notify client replaced", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("Notify client disconnected", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsRegistrationFilterMatch(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return text.IndexOf("Registration", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("Register", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Activity filter "KeyReader" — includes KeyReader Plus, KeyUploader, KAI and BMW HTTP dumps.
        /// </summary>
        public static bool IsKeyReaderFilterMatch(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (text.StartsWith("KeyReader:", StringComparison.OrdinalIgnoreCase)) return true;
            if (text.StartsWith("KeyUploader:", StringComparison.OrdinalIgnoreCase)) return true;
            if (text.StartsWith("KAI:", StringComparison.OrdinalIgnoreCase)) return true;
            if (text.StartsWith("VIN ", StringComparison.OrdinalIgnoreCase)) return true;
            if (text.StartsWith("BMW HTTP", StringComparison.OrdinalIgnoreCase)) return true;
            if (text.StartsWith("BMW upload probe", StringComparison.OrdinalIgnoreCase)) return true;
            if (text.StartsWith("REQUEST:", StringComparison.OrdinalIgnoreCase)) return true;
            if (text.StartsWith("RESPONSE:", StringComparison.OrdinalIgnoreCase)) return true;
            if (text.StartsWith("Register REQUEST:", StringComparison.OrdinalIgnoreCase)) return true;
            if (text.StartsWith("Register RESPONSE:", StringComparison.OrdinalIgnoreCase)) return true;
            if (text.IndexOf("Uploaded to KAI Central", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (text.IndexOf("Sent to KAI Dezentral", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (text.IndexOf("BMW Server", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (text.IndexOf("Retry BMW", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        public static bool MatchesLogSearch(string text, string needle)
        {
            if (string.IsNullOrEmpty(needle)) return true;
            if (string.IsNullOrEmpty(text)) return false;
            return text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Same ~3 min window as VIN row dedupe — do not inherit BmwUploaded/Failed
        /// across separate key reads hours apart.
        /// </summary>
        public const double InheritUploadWindowMinutes = 3;

        public static bool ShouldInheritVinUploadStatus(DateTime targetLocal, DateTime candidateLocal)
        {
            return Math.Abs((candidateLocal - targetLocal).TotalMinutes) <= InheritUploadWindowMinutes;
        }

        public static bool TryParseExportDay(string text, out DateTime day)
        {
            day = DateTime.Today;
            if (string.IsNullOrWhiteSpace(text)) return false;
            DateTime parsed;
            if (!DateTime.TryParseExact(text.Trim(), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed) &&
                !DateTime.TryParse(text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                return false;
            day = parsed.Date;
            return true;
        }

        public static ActivityLogLevel InferLogLevel(string line, bool verboseOnly)
        {
            if (verboseOnly) return ActivityLogLevel.Verbose;
            if (string.IsNullOrEmpty(line)) return ActivityLogLevel.Info;
            if (line.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase))
                return ActivityLogLevel.Error;
            if (line.StartsWith("OK:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Last success", StringComparison.OrdinalIgnoreCase))
                return ActivityLogLevel.Success;
            if (line.IndexOf("Balloon tip: Error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("Balloon-Tipp: Fehler", StringComparison.OrdinalIgnoreCase) >= 0)
                return ActivityLogLevel.Error;
            if (line.IndexOf("Balloon tip: Warning", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("Balloon-Tipp: Warnung", StringComparison.OrdinalIgnoreCase) >= 0)
                return ActivityLogLevel.Warning;
            if (line.IndexOf("does not exist", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("Missing ", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("invalid", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("lipsește", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("nu există", StringComparison.OrdinalIgnoreCase) >= 0)
                return ActivityLogLevel.Warning;
            return ActivityLogLevel.Info;
        }

        public static bool TryClassifyRegisterHistory(string text, out int kind)
        {
            kind = 0;
            if (string.IsNullOrEmpty(text)) return false;
            var t = text.Trim();
            if (t.StartsWith("OK:", StringComparison.OrdinalIgnoreCase))
                t = t.Substring(3).TrimStart();
            else if (t.StartsWith("FAIL:", StringComparison.OrdinalIgnoreCase))
                t = t.Substring(5).TrimStart();

            if (t.IndexOf("Register not required", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("Checking WebEAM token", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("(no Register)", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("no Register", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            if (t.IndexOf("Save + Register automatic", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("Salvare + Register automat", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("Speichern + Register automatisch", StringComparison.OrdinalIgnoreCase) >= 0
                || (t.IndexOf("Startup:", StringComparison.OrdinalIgnoreCase) >= 0
                    && t.IndexOf("Register", StringComparison.OrdinalIgnoreCase) >= 0)
                || (t.IndexOf("Pornire:", StringComparison.OrdinalIgnoreCase) >= 0
                    && t.IndexOf("Register", StringComparison.OrdinalIgnoreCase) >= 0)
                || (t.IndexOf("Start:", StringComparison.OrdinalIgnoreCase) >= 0
                    && t.IndexOf("Register automat", StringComparison.OrdinalIgnoreCase) >= 0))
                return false;

            if (t.IndexOf("Registration OK", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("Register OK", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                kind = 1;
                return true;
            }
            if (t.IndexOf("Register failed", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("Register rejected", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("Register returned empty", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("KAI_Properties.cfg was not created", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("KAI_Properties.cfg invalid", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                kind = 0;
                return true;
            }

            var hasRegister = t.IndexOf("Register", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("Registration", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!hasRegister) return false;

            kind = 2;
            return true;
        }

        /// <summary>
        /// Filter register history by ok|fail|info (+ optional search).
        /// Returns null when filter is empty/all (caller should send empty payload).
        /// Returns empty list for unknown filters.
        /// </summary>
        public static List<RegisterWireRow> FilterRegisterHistory(
            IList<RegisterHistoryFilterRow> rows, string filter, string search, int maxRows = 5000)
        {
            filter = (filter ?? "").Trim().ToLowerInvariant();
            search = (search ?? "").Trim();
            if (string.IsNullOrEmpty(filter) || filter == "all")
                return null;

            int wantKind;
            if (filter == "ok") wantKind = 1;
            else if (filter == "fail") wantKind = 0;
            else if (filter == "info") wantKind = 2;
            else
                return new List<RegisterWireRow>();

            var list = new List<RegisterWireRow>();
            if (rows == null) return list;
            for (var i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                if (r == null || r.Kind != wantKind) continue;
                if (!MatchesLogSearch(r.Text, search) && !MatchesLogSearch(r.ResultText, search))
                    continue;
                list.Add(new RegisterWireRow
                {
                    At = r.At,
                    Kind = r.Kind,
                    Ok = r.Kind == 1,
                    Text = r.Text ?? ""
                });
            }
            if (maxRows > 0 && list.Count > maxRows)
                list = list.GetRange(list.Count - maxRows, maxRows);
            return list;
        }
    }

    /// <summary>
    /// Client Activity live-row filter rules (filter tab + search), shared for regression tests.
    /// </summary>
    public static class ActivityFilterLogic
    {
        public static bool MatchesActivityFilterOnly(string filter, string levelText, string text)
        {
            if (string.Equals(filter, "keyreader", StringComparison.OrdinalIgnoreCase))
                return ActivityRegisterLogic.IsKeyReaderFilterMatch(text);
            if (string.Equals(filter, "registration", StringComparison.OrdinalIgnoreCase))
                return ActivityRegisterLogic.IsRegistrationFilterMatch(text);
            if (string.IsNullOrEmpty(filter) || string.Equals(filter, "all", StringComparison.OrdinalIgnoreCase))
                return true;
            return string.Equals(levelText, filter, StringComparison.OrdinalIgnoreCase);
        }

        public static bool MatchesActivityRow(string filter, string levelText, string text, string search)
        {
            if (!ActivityRegisterLogic.MatchesLogSearch(text, search)) return false;
            return MatchesActivityFilterOnly(filter, levelText, text);
        }
    }
}
