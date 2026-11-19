using System.Collections.Generic;
using KaiAosRenew;

namespace KaiAosNotifyClient
{
    /// <summary>Adapter between <see cref="ClientKeyRow"/> and the shared Keys sort/export helpers.</summary>
    internal static class ClientKeysExport
    {
        public static KeysRowView ToView(ClientKeyRow r)
        {
            if (r == null) return null;
            return new KeysRowView
            {
                Time = r.DetectedLocal,
                Vin = r.Vin ?? "",
                Km = r.MileageKm,
                Paint = r.Paint ?? "",
                Upholstery = r.Upholstery ?? "",
                ILevel = r.ILevelCurrent ?? "",
                Reader = r.ReaderId ?? "",
                Local = r.LocalText,
                Bmw = r.BmwText,
                Status = r.StatusText,
                StatusRank = KeysExportLogic.StatusRankFromName(r.Status),
                Detail = r.DetailText
            };
        }

        public static List<KeysRowView> ToViews(IEnumerable<ClientKeyRow> rows)
        {
            var list = new List<KeysRowView>();
            if (rows == null) return list;
            foreach (var r in rows)
            {
                var v = ToView(r);
                if (v != null) list.Add(v);
            }
            return list;
        }

        public static List<ClientKeyRow> Sort(IEnumerable<ClientKeyRow> rows, string column, bool ascending)
        {
            return KeysExportLogic.Sort<ClientKeyRow>(rows, column, ascending, ToView);
        }

        /// <summary>CSV (default) or TXT (when <paramref name="path"/> ends with .txt) text for the given rows.</summary>
        public static string BuildExport(IEnumerable<ClientKeyRow> rows, string path)
        {
            return KeysExportLogic.Build(ToViews(rows), path);
        }
    }
}
