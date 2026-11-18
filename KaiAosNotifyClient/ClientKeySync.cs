using System;
using System.Collections.Generic;
using KaiAosRenew;

namespace KaiAosNotifyClient
{
    /// <summary>Client-side parse of kind=keySync payloads into <see cref="ClientKeyRow"/>.</summary>
    internal static class ClientKeySync
    {
        public static List<ClientKeyRow> ParseKeySync(string text)
        {
            return ParseKeySync(text, ClientKeyHistory.MaxEvents);
        }

        public static List<ClientKeyRow> ParseKeySync(string text, int maxRows)
        {
            if (maxRows < 1) maxRows = ClientKeyHistory.MaxEvents;
            var list = new List<ClientKeyRow>();
            if (string.IsNullOrWhiteSpace(text)) return list;
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (var raw in lines)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var row = ParseKeySyncLine(raw);
                if (row != null && !string.IsNullOrEmpty(row.Vin))
                    list.Add(row);
                if (list.Count >= maxRows) break;
            }
            return list;
        }

        public static ClientKeyRow ParseKeySyncLine(string line)
        {
            var parts = NotifyMessage.SplitPipe(line);
            if (parts.Count < 2) return null;
            var row = new ClientKeyRow();
            DateTime at;
            if (DateTime.TryParse(parts[0], out at))
                row.DetectedLocal = DateTime.SpecifyKind(at, DateTimeKind.Local);
            else
                row.DetectedLocal = DateTime.Now;
            row.Vin = parts.Count > 1 ? parts[1] : "";
            row.Paint = parts.Count > 2 ? parts[2] : "";
            row.Upholstery = parts.Count > 3 ? parts[3] : "";
            row.ILevelCurrent = parts.Count > 4 ? parts[4] : "";
            row.ReaderId = parts.Count > 5 ? parts[5] : "";
            row.LocalSent = parts.Count > 6 && parts[6] == "1";
            row.BmwUploaded = parts.Count > 7 && parts[7] == "1";
            row.Failed = parts.Count > 8 && parts[8] == "1";
            row.Status = parts.Count > 9 ? parts[9] : "Waiting";
            row.Detail = parts.Count > 10 ? parts[10] : "";
            row.HasBin = parts.Count > 11 && parts[11] == "1";
            if (parts.Count > 12)
            {
                int km;
                if (int.TryParse((parts[12] ?? "").Trim(), System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out km) && km > 0)
                    row.MileageKm = km;
            }
            return row;
        }

        public static ClientKeyRow FromNotifyMessage(NotifyMessage m)
        {
            if (m == null) return null;
            var row = new ClientKeyRow();
            DateTime at;
            if (!string.IsNullOrEmpty(m.At) && DateTime.TryParse(m.At, out at))
                row.DetectedLocal = DateTime.SpecifyKind(at, DateTimeKind.Local);
            else
                row.DetectedLocal = DateTime.Now;
            row.Vin = m.Vin ?? "";
            row.Paint = m.Paint ?? "";
            row.Upholstery = m.Uph ?? "";
            row.ILevelCurrent = m.ILevel ?? "";
            row.ReaderId = m.Reader ?? "";
            row.Detail = m.Detail ?? "";
            row.Status = string.IsNullOrEmpty(m.Status) ? "Waiting" : m.Status;
            row.LocalSent = m.Local == 1;
            row.BmwUploaded = m.Bmw == 1;
            row.Failed = m.Failed == 1;
            if (m.Km > 0) row.MileageKm = m.Km;
            return row;
        }
    }
}
