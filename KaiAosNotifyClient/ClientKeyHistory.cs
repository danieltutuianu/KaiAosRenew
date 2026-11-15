using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace KaiAosNotifyClient
{
    /// <summary>
    /// Persist Keys rows under %AppData%\KaiAosNotifyClient\key-events.xml
    /// so history survives disconnect / restart.
    /// </summary>
    internal static class ClientKeyHistory
    {
        public const int MaxEvents = 100;

        public static string StorePath
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "KaiAosNotifyClient");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "key-events.xml");
            }
        }

        public static List<ClientKeyRow> Load()
        {
            var list = new List<ClientKeyRow>();
            try
            {
                if (!File.Exists(StorePath)) return list;
                var ser = new XmlSerializer(typeof(List<ClientKeyRow>));
                using (var fs = File.OpenRead(StorePath))
                {
                    var loaded = ser.Deserialize(fs) as List<ClientKeyRow>;
                    if (loaded == null) return list;
                    foreach (var row in loaded)
                    {
                        if (row == null) continue;
                        list.Add(row);
                        if (list.Count >= MaxEvents) break;
                    }
                }
            }
            catch { }
            return list;
        }

        public static void Save(IList<ClientKeyRow> rows)
        {
            try
            {
                var list = new List<ClientKeyRow>();
                if (rows != null)
                {
                    foreach (var row in rows)
                    {
                        if (row == null) continue;
                        list.Add(Clone(row));
                        if (list.Count >= MaxEvents) break;
                    }
                }
                var ser = new XmlSerializer(typeof(List<ClientKeyRow>));
                using (var fs = File.Create(StorePath))
                    ser.Serialize(fs, list);
            }
            catch { }
        }

        public static ClientKeyRow Clone(ClientKeyRow row)
        {
            if (row == null) return null;
            return new ClientKeyRow
            {
                DetectedLocal = row.DetectedLocal,
                Vin = row.Vin,
                Paint = row.Paint,
                Upholstery = row.Upholstery,
                ILevelCurrent = row.ILevelCurrent,
                ReaderId = row.ReaderId,
                LocalSent = row.LocalSent,
                BmwUploaded = row.BmwUploaded,
                Failed = row.Failed,
                Status = row.Status,
                Detail = row.Detail
            };
        }

        public static string RowKey(ClientKeyRow row)
        {
            if (row == null) return "";
            var vin = row.Vin ?? "";
            return row.DetectedLocal.ToString("yyyyMMddHHmmss") + "|" + vin.ToUpperInvariant();
        }
    }
}
