using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace KaiAosRenew
{
    /// <summary>Server-only keySync encode helpers (uses KeyActivityEvent).</summary>
    public static class NotifyKeySync
    {
        public static string EncodeKeySync(IList<KeyActivityEvent> rows)
        {
            if (rows == null || rows.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (var ev in rows)
            {
                if (ev == null || string.IsNullOrEmpty(ev.Vin)) continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(ev.DetectedLocal.ToString("yyyy-MM-ddTHH:mm:ss"));
                sb.Append('|').Append(NotifyMessage.EscPipe(ev.Vin ?? ""));
                sb.Append('|').Append(NotifyMessage.EscPipe(ev.Paint ?? ""));
                sb.Append('|').Append(NotifyMessage.EscPipe(ev.Upholstery ?? ""));
                sb.Append('|').Append(NotifyMessage.EscPipe(ev.ILevelCurrent ?? ""));
                sb.Append('|').Append(NotifyMessage.EscPipe(ev.ReaderId ?? ""));
                sb.Append('|').Append(ev.LocalSent ? "1" : "0");
                sb.Append('|').Append(ev.BmwUploaded ? "1" : "0");
                sb.Append('|').Append(ev.Failed ? "1" : "0");
                sb.Append('|').Append(NotifyMessage.EscPipe(StatusName(ev.Status)));
                sb.Append('|').Append(NotifyMessage.EscPipe(ev.DetailText ?? ""));
                var hasBin = !string.IsNullOrEmpty(ev.DebugBinPath) && File.Exists(ev.DebugBinPath);
                sb.Append('|').Append(hasBin ? "1" : "0");
                sb.Append('|').Append(ev.MileageKm.HasValue && ev.MileageKm.Value > 0
                    ? ev.MileageKm.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : "");
            }
            return sb.ToString();
        }

        public static string StatusName(KeyEventStatus status)
        {
            switch (status)
            {
                case KeyEventStatus.Waiting: return "Waiting";
                case KeyEventStatus.LocalOk: return "LocalOk";
                case KeyEventStatus.BmwOk: return "BmwOk";
                case KeyEventStatus.Failed: return "Failed";
                default: return "Waiting";
            }
        }

        public static void ApplyKeyEventFields(NotifyMessage m, KeyActivityEvent ev)
        {
            if (m == null || ev == null) return;
            m.At = ev.DetectedLocal.ToString("yyyy-MM-ddTHH:mm:ss");
            m.Vin = ev.Vin;
            m.Paint = ev.Paint ?? "";
            m.Uph = ev.Upholstery ?? "";
            m.ILevel = ev.ILevelCurrent ?? "";
            m.Reader = ev.ReaderId ?? "";
            m.Detail = ev.DetailText ?? "";
            m.Status = StatusName(ev.Status);
            m.Local = ev.LocalSent ? 1 : 0;
            m.Bmw = ev.BmwUploaded ? 1 : 0;
            m.Failed = ev.Failed ? 1 : 0;
            m.Km = ev.MileageKm.HasValue && ev.MileageKm.Value > 0 ? ev.MileageKm.Value : -1;
        }
    }
}
