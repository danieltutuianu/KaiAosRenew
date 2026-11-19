using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace KaiAosRenew
{
    /// <summary>
    /// Neutral view of one Keys-history row, shared by the Server (KeyActivityEvent) and
    /// the Client (ClientKeyRow) so sorting + CSV/TXT export behave identically.
    /// </summary>
    public sealed class KeysRowView
    {
        public DateTime Time;
        public string Vin;
        public int? Km;
        public string Paint;
        public string Upholstery;
        public string ILevel;
        public string Reader;
        /// <summary>Local column text (OK / FAIL / —).</summary>
        public string Local;
        /// <summary>BMW Server column text (OK / FAIL / … / —).</summary>
        public string Bmw;
        /// <summary>Status text (Waiting / Local OK / OK / Failed …).</summary>
        public string Status;
        /// <summary>Order used when sorting by Status (0 unknown, 1 waiting, 2 local OK, 3 BMW OK, 4 failed).</summary>
        public int StatusRank;
        public string Detail;
    }

    /// <summary>Pure helpers: Keys column sorting and CSV/TXT export.</summary>
    public static class KeysExportLogic
    {
        public static readonly string[] ColumnHeaders =
        {
            "Time", "VIN", "Km", "Paint", "Upholstery", "I-level", "Reader", "Local", "BMW", "Status", "Detail"
        };

        /// <summary>Status string used by ClientKeyRow ("Waiting", "LocalOk", "BmwOk", "Failed") → sort rank.</summary>
        public static int StatusRankFromName(string status)
        {
            switch ((status ?? "").Trim())
            {
                case "Waiting": return 1;
                case "LocalOk": return 2;
                case "BmwOk": return 3;
                case "Failed": return 4;
                default: return 0;
            }
        }

        public static readonly string[] SortableColumns =
        {
            "time", "vin", "km", "paint", "upholstery", "ilevel", "reader", "local", "bmw", "status"
        };

        public static bool IsSortableColumn(string column)
        {
            if (string.IsNullOrEmpty(column)) return false;
            for (var i = 0; i < SortableColumns.Length; i++)
            {
                if (string.Equals(SortableColumns[i], column, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>UiStrings / ClientUiStrings key for a Keys sort column id.</summary>
        public static string ColumnHeaderKey(string col)
        {
            switch (col)
            {
                case "time": return "KeysColTime";
                case "vin": return "KeysColVin";
                case "km": return "KeysColKm";
                case "paint": return "KeysColPaint";
                case "upholstery": return "KeysColUpholstery";
                case "ilevel": return "KeysColILevel";
                case "reader": return "KeysColReader";
                case "local": return "KeysColLocal";
                case "bmw": return "KeysColBmw";
                default: return "KeysColDetail";
            }
        }

        /// <summary>First-click direction for a column (time/km: biggest first; text: A→Z; status: ascending).</summary>
        public static bool DefaultAscending(string column)
        {
            return column != "time" && column != "km";
        }

        /// <summary>
        /// Stable sort of <paramref name="src"/> by <paramref name="column"/>. Empty values always sink to the bottom;
        /// ties fall back to newest-first. Unknown/empty column returns a copy in the original order.
        /// </summary>
        public static List<T> Sort<T>(IEnumerable<T> src, string column, bool ascending, Func<T, KeysRowView> view)
            where T : class
        {
            var list = new List<T>();
            if (src != null)
            {
                foreach (var item in src)
                    if (item != null) list.Add(item);
            }
            if (view == null || !IsSortableColumn(column) || list.Count < 2)
                return list;

            // Decorate once so the comparer does not rebuild views O(n log n) times.
            var keyed = new List<KeyValuePair<KeysRowView, T>>(list.Count);
            for (var i = 0; i < list.Count; i++)
                keyed.Add(new KeyValuePair<KeysRowView, T>(view(list[i]) ?? new KeysRowView(), list[i]));

            // List.Sort is unstable; add the original index as the final tie-break.
            var indexed = new List<int>(list.Count);
            for (var i = 0; i < list.Count; i++) indexed.Add(i);
            indexed.Sort(delegate(int ia, int ib)
            {
                var cmp = Compare(keyed[ia].Key, keyed[ib].Key, column, ascending);
                return cmp != 0 ? cmp : ia.CompareTo(ib);
            });

            var result = new List<T>(list.Count);
            for (var i = 0; i < indexed.Count; i++)
                result.Add(keyed[indexed[i]].Value);
            return result;
        }

        public static int Compare(KeysRowView a, KeysRowView b, string column, bool ascending)
        {
            int cmp;
            switch (column)
            {
                case "time":
                    cmp = a.Time.CompareTo(b.Time);
                    return ascending ? cmp : -cmp;
                case "status":
                    cmp = a.StatusRank.CompareTo(b.StatusRank);
                    if (!ascending) cmp = -cmp;
                    return cmp != 0 ? cmp : b.Time.CompareTo(a.Time);
                case "km":
                {
                    var ea = !a.Km.HasValue || a.Km.Value <= 0;
                    var eb = !b.Km.HasValue || b.Km.Value <= 0;
                    if (ea && eb) return b.Time.CompareTo(a.Time);
                    if (ea) return 1;
                    if (eb) return -1;
                    cmp = a.Km.Value.CompareTo(b.Km.Value);
                    if (!ascending) cmp = -cmp;
                    return cmp != 0 ? cmp : b.Time.CompareTo(a.Time);
                }
                default:
                {
                    var sa = TextOf(a, column);
                    var sb = TextOf(b, column);
                    var ea = IsEmptyCell(sa);
                    var eb = IsEmptyCell(sb);
                    if (ea && eb) return b.Time.CompareTo(a.Time);
                    if (ea) return 1;
                    if (eb) return -1;
                    cmp = string.Compare(sa, sb, StringComparison.OrdinalIgnoreCase);
                    if (!ascending) cmp = -cmp;
                    return cmp != 0 ? cmp : b.Time.CompareTo(a.Time);
                }
            }
        }

        private static string TextOf(KeysRowView v, string column)
        {
            switch (column)
            {
                case "vin": return v.Vin;
                case "paint": return v.Paint;
                case "upholstery": return v.Upholstery;
                case "ilevel": return v.ILevel;
                case "reader": return v.Reader;
                case "local": return v.Local;
                case "bmw": return v.Bmw;
                default: return "";
            }
        }

        private static bool IsEmptyCell(string s)
        {
            return string.IsNullOrWhiteSpace(s) || s == "—" || s == "-";
        }

        /// <summary>Cell text for export: UI placeholders (—) become empty; line breaks collapse to one space.</summary>
        public static string ExportCell(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var t = s.Trim();
            if (t == "—") return "";
            t = t.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
            return t;
        }

        /// <summary>Quote a CSV field (RFC 4180) and neutralise spreadsheet formula injection (=, +, @).</summary>
        public static string CsvField(string s)
        {
            var t = ExportCell(s);
            if (t.Length > 0 && (t[0] == '=' || t[0] == '+' || t[0] == '@'))
                t = "'" + t;
            if (t.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
                t = "\"" + t.Replace("\"", "\"\"") + "\"";
            return t;
        }

        public static string FormatTime(DateTime t)
        {
            return t.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private static string[] RowCells(KeysRowView r)
        {
            return new[]
            {
                FormatTime(r.Time),
                ExportCell(r.Vin),
                r.Km.HasValue && r.Km.Value > 0
                    ? r.Km.Value.ToString(CultureInfo.InvariantCulture)
                    : "",
                ExportCell(r.Paint),
                ExportCell(r.Upholstery),
                ExportCell(r.ILevel),
                ExportCell(r.Reader),
                ExportCell(r.Local),
                ExportCell(r.Bmw),
                ExportCell(r.Status),
                ExportCell(r.Detail)
            };
        }

        /// <summary>CSV text with header row (CRLF line endings, no BOM — writer adds it).</summary>
        public static string BuildCsv(IEnumerable<KeysRowView> rows)
        {
            var sb = new StringBuilder();
            sb.Append(string.Join(",", ColumnHeaders)).Append("\r\n");
            if (rows != null)
            {
                foreach (var r in rows)
                {
                    if (r == null) continue;
                    var cells = RowCells(r);
                    for (var i = 0; i < cells.Length; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append(CsvField(cells[i]));
                    }
                    sb.Append("\r\n");
                }
            }
            return sb.ToString();
        }

        /// <summary>Tab-separated TXT with header row.</summary>
        public static string BuildTxt(IEnumerable<KeysRowView> rows)
        {
            var sb = new StringBuilder();
            sb.Append(string.Join("\t", ColumnHeaders)).Append("\r\n");
            if (rows != null)
            {
                foreach (var r in rows)
                {
                    if (r == null) continue;
                    sb.Append(string.Join("\t", RowCells(r))).Append("\r\n");
                }
            }
            return sb.ToString();
        }

        /// <summary>True when the chosen file name ends with .txt (otherwise CSV).</summary>
        public static bool IsTxtPath(string path)
        {
            return !string.IsNullOrEmpty(path)
                && path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
        }

        public static string Build(IEnumerable<KeysRowView> rows, string path)
        {
            return IsTxtPath(path) ? BuildTxt(rows) : BuildCsv(rows);
        }

        /// <summary>Suggested file name, e.g. keys-history-20261119-194500.csv</summary>
        public static string SuggestFileName(string prefix, DateTime now, bool txt)
        {
            return (string.IsNullOrEmpty(prefix) ? "keys-history" : prefix)
                + "-" + now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
                + (txt ? ".txt" : ".csv");
        }
    }
}
