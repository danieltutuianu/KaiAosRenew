using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Serialization;

namespace KaiAosRenew
{
    public enum KeyEventStatus
    {
        Unknown = 0,
        Waiting = 1,
        LocalOk = 2,
        BmwOk = 3,
        Failed = 4
    }

    internal sealed class KeyReaderSnapshot
    {
        public bool Ok { get; set; }
        public string Message { get; set; }
        public string KeyReaderId { get; set; }
        public string HwAddress { get; set; }
        public string FirmwareVersion { get; set; }
        public string IpAddress { get; set; }
        public DateTime? LastKeyReceivedLocal { get; set; }
        public bool? Inactive { get; set; }
    }

    public sealed class KeyActivityEvent
    {
        public DateTime DetectedLocal { get; set; }
        public string ReaderId { get; set; }
        public string FileId { get; set; }
        public string Vin { get; set; }
        public string Paint { get; set; }
        public string Upholstery { get; set; }
        public string ILevelCurrent { get; set; }
        public string ILevelBackup { get; set; }
        public string ILevelPlant { get; set; }
        public int? MileageKm { get; set; }
        public bool LocalSent { get; set; }
        public bool BmwUploaded { get; set; }
        public bool Failed { get; set; }
        public string Detail { get; set; }
        public KeyEventStatus Status { get; set; }
        public bool StallNotified { get; set; }

        [XmlIgnore]
        public string TimeText { get { return DetectedLocal.ToString("g"); } }
        [XmlIgnore]
        public string VinText { get { return string.IsNullOrEmpty(Vin) ? "—" : Vin; } }
        [XmlIgnore]
        public string KmText
        {
            get
            {
                if (!MileageKm.HasValue || MileageKm.Value <= 0) return "—";
                return MileageKm.Value.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
            }
        }
        [XmlIgnore]
        public string RowToolTip
        {
            get
            {
                var tip = string.IsNullOrEmpty(Vin) ? "VIN: —" : ("VIN: " + Vin);
                if (MileageKm.HasValue && MileageKm.Value > 0)
                    tip += "\nKm: " + KmText;
                return tip;
            }
        }
        [XmlIgnore]
        public string PaintText { get { return string.IsNullOrEmpty(Paint) ? "—" : Paint; } }
        [XmlIgnore]
        public string UpholsteryText { get { return string.IsNullOrEmpty(Upholstery) ? "—" : Upholstery; } }
        [XmlIgnore]
        public string ILevelText
        {
            get { return string.IsNullOrEmpty(ILevelCurrent) ? "—" : ILevelCurrent; }
        }
        [XmlIgnore]
        public string LocalText
        {
            get
            {
                if (Failed && !LocalSent) return "FAIL";
                return LocalSent ? "OK" : "—";
            }
        }
        [XmlIgnore]
        public string BmwText
        {
            get
            {
                if (BmwUploaded) return "OK";
                if (Failed) return "FAIL";
                if (LocalSent) return "…";
                return "—";
            }
        }
        [XmlIgnore]
        public string StatusText
        {
            get
            {
                switch (Status)
                {
                    case KeyEventStatus.Waiting: return "Waiting";
                    case KeyEventStatus.LocalOk: return "Local OK";
                    case KeyEventStatus.BmwOk: return "OK";
                    case KeyEventStatus.Failed: return "Failed";
                    default: return "Unknown";
                }
            }
        }
        /// <summary>Local archived keyreader.bin path (debug mode only; not synced).</summary>
        [XmlIgnore]
        public string DebugBinPath { get; set; }

        [XmlIgnore]
        public string DetailText
        {
            get
            {
                if (string.IsNullOrEmpty(Detail) || IsPickupNoiseDetail(Detail))
                    return "—";
                // Migrate older persisted wording.
                if (string.Equals(Detail, "Uploaded to KAI Central (BMW)", StringComparison.Ordinal)
                    || string.Equals(Detail, "Uploaded to KAI Central (BMW Server)", StringComparison.Ordinal))
                    return "Uploaded to KAI Central";
                return Detail;
            }
        }

        internal static bool IsPickupNoiseDetail(string detail)
        {
            if (string.IsNullOrEmpty(detail)) return true;
            return detail.IndexOf("Picked up binary", StringComparison.OrdinalIgnoreCase) >= 0
                || detail.IndexOf("New KeyReader folder", StringComparison.OrdinalIgnoreCase) >= 0
                || detail.IndexOf("PutKeyDataV1", StringComparison.OrdinalIgnoreCase) >= 0
                || detail.IndexOf("Enter method", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    internal sealed class KeyActivitySnapshot
    {
        public KeyReaderSnapshot Reader { get; set; }
        public List<KeyActivityEvent> Events { get; set; }
        public List<KeyActivityEvent> NewEvents { get; set; }
        public List<KeyActivityEvent> StallEvents { get; set; }
        /// <summary>VINs newly read from keyreader.bin / Sqlite (before BMW upload).</summary>
        public List<string> NewVinReads { get; set; }
        public string PickupFolder { get; set; }
        public bool PickupFolderExists { get; set; }
        public DateTime? PickupFileLocal { get; set; }
        public string UploaderLogPath { get; set; }
        public string KaiLogPath { get; set; }
        public string Summary { get; set; }
        public int KeysToday { get; set; }
        public string EncPath { get; set; }
        public bool EncFileExists { get; set; }

        public KeyActivitySnapshot()
        {
            Events = new List<KeyActivityEvent>();
            NewEvents = new List<KeyActivityEvent>();
            StallEvents = new List<KeyActivityEvent>();
            NewVinReads = new List<string>();
            Reader = new KeyReaderSnapshot();
        }
    }

    /// <summary>Read-only observation of KeyUploader + KAI logs + keyreader.bin + SqliteKaiDb.</summary>
    internal sealed class KeyActivityMonitor : IDisposable
    {
        public const string DefaultPickupFolder = @"C:\Users\Public\keyreader";
        public const string DefaultUploaderLogDir = @"C:\ProgramData\BMW\ISPI\Logs\KAI\KeyUploader";
        public const string DefaultKaiLogDir = @"C:\ProgramData\BMW\ISPI\Logs\KAI\KAI";
        public const string DefaultSqlitePath = @"C:\ProgramData\BMW\ISPI\data\KAI\KAI\SqliteKaiDb.db";
        public const string TransferredBin =
            @"C:\ProgramData\BMW\ISPI\data\KAI\KeyUploader\Transferred\keyreader.bin";

        private const int MaxEvents = 500;
        private const int MaxVinHistory = 10;
        private const int MaxBinHints = 50;
        private const int LogTailBytes = 512 * 1024;

        private string _pickupFolder = DefaultPickupFolder;
        private string _uploaderLogDir = DefaultUploaderLogDir;
        private string _kaiLogDir = DefaultKaiLogDir;
        private string _sqlitePath = DefaultSqlitePath;
        private double _stallMinutes = 2.0;
        private readonly Dictionary<string, string> _lastDebugBinByVin =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>When true, archive keyreader.bin + parse log under AppData (Debug Mode).</summary>
        public bool KeyDebugEnabled { get; set; }

        public string ActivePickupFolder
        {
            get { return _pickupFolder; }
        }

        public static string KeyDebugStoreDirectory
        {
            get { return KeyDebugStoreDir; }
        }

        /// <summary>Resolve archived debug .bin for a VIN (single {VIN}.bin, else legacy timestamped).</summary>
        public static string ResolveDebugBinForVin(string vin)
        {
            if (string.IsNullOrWhiteSpace(vin)) return null;
            try
            {
                var bins = Path.Combine(KeyDebugStoreDir, "key-bins");
                if (!Directory.Exists(bins)) return null;
                var token = SanitizeVinForFile(vin);
                var single = Path.Combine(bins, token + ".bin");
                if (File.Exists(single)) return single;
                var hits = Directory.GetFiles(bins, "*_" + token + ".bin");
                Array.Sort(hits, StringComparer.OrdinalIgnoreCase);
                if (hits.Length > 0) return hits[hits.Length - 1];
            }
            catch { }
            return null;
        }

        public double StallMinutes
        {
            get { return _stallMinutes; }
        }

        /// <summary>Apply user settings. Restarts watcher if pickup folder changed.</summary>
        public void Configure(
            string pickupFolder,
            int stallMinutes,
            string uploaderLogDir = null,
            string kaiLogDir = null,
            string sqlitePath = null)
        {
            lock (_gate)
            {
                var folder = string.IsNullOrWhiteSpace(pickupFolder)
                    ? DefaultPickupFolder
                    : pickupFolder.Trim();
                var stall = stallMinutes;
                if (stall < 1) stall = 1;
                if (stall > 60) stall = 60;
                _stallMinutes = stall;
                _uploaderLogDir = string.IsNullOrWhiteSpace(uploaderLogDir)
                    ? DefaultUploaderLogDir
                    : uploaderLogDir.Trim();
                _kaiLogDir = string.IsNullOrWhiteSpace(kaiLogDir)
                    ? DefaultKaiLogDir
                    : kaiLogDir.Trim();
                _sqlitePath = string.IsNullOrWhiteSpace(sqlitePath)
                    ? DefaultSqlitePath
                    : sqlitePath.Trim();

                var folderChanged = !string.Equals(_pickupFolder, folder, StringComparison.OrdinalIgnoreCase);
                _pickupFolder = folder;
                if (folderChanged && _onChanged != null)
                {
                    StopWatcher();
                    StartWatcher();
                }
            }
        }

        private static readonly Regex RxTs = new Regex(
            @"^(?<ts>\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2},\d{3})",
            RegexOptions.Compiled);
        private static readonly Regex RxGuid = new Regex(
            @"\b([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\b",
            RegexOptions.Compiled);
        private static readonly Regex RxReaderId = new Regex(
            @"keyreaderids/([^/]+)/macaddr",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RxBase64Chunk = new Regex(
            @"[A-Za-z0-9+/]{80,}={0,2}",
            RegexOptions.Compiled);
        private static readonly Regex RxVin = new Regex(
            @"\b([A-HJ-NPR-Z0-9]{17})\b",
            RegexOptions.Compiled);
        /// <summary>Paint/upholstery token: % + 4 alnum paint (must include a digit) + 4 alnum upholstery.</summary>
        private static readonly Regex RxPaintUph = new Regex(
            @"%([0-9A-Z]{4})([A-Z0-9]{4})",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        /// <summary>I-level family: any series letter + 3 hex (E/F/G/I/… and later).</summary>
        private static readonly Regex RxILevelFamily = new Regex(
            @"^[A-Z][0-9A-F]{3}$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RxILevelAscii = new Regex(
            @"\b([A-Z][0-9A-F]{3}-\d{2}-\d{2}-\d+)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private const int KeyBinArchiveMax = 40;
        private const int KeyDebugDedupSeconds = 5;
        private static readonly object KeyDebugGate = new object();
        private static string _lastKeyDebugVin = "";
        private static string _lastKeyDebugHash = "";
        private static DateTime _lastKeyDebugUtc = DateTime.MinValue;

        private FileSystemWatcher _watcher;
        private readonly object _gate = new object();
        private int _notifyQueued;
        private Action _onChanged;
        private readonly List<BinHint> _binHints = new List<BinHint>();
        private readonly List<KeyActivityEvent> _persisted = new List<KeyActivityEvent>();
        private readonly HashSet<string> _seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _announcedVinKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _historyLoaded;
        /// <summary>Newest VINs for tray/UI; safe to read without waiting on Refresh.</summary>
        private List<string> _recentVinCache = new List<string>();
        private readonly object _vinCacheGate = new object();
        private bool _vinAnnounceSeeded;

        public static string EventsStorePath
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "KaiAosRenew");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "key-events.xml");
            }
        }

        public void Start(Action onChanged)
        {
            _onChanged = onChanged;
            lock (_gate)
            {
                EnsureHistoryLoaded();
                UpdateRecentVinCacheUnlocked();
            }
            StopWatcher();
            StartWatcher();
        }

        private void StartWatcher()
        {
            try
            {
                if (!Directory.Exists(_pickupFolder))
                    Directory.CreateDirectory(_pickupFolder);
            }
            catch { }

            try
            {
                _watcher = new FileSystemWatcher(_pickupFolder);
                _watcher.IncludeSubdirectories = true;
                _watcher.NotifyFilter = NotifyFilters.FileName
                    | NotifyFilters.DirectoryName
                    | NotifyFilters.LastWrite
                    | NotifyFilters.Size
                    | NotifyFilters.CreationTime;
                _watcher.Created += Watcher_Changed;
                _watcher.Changed += Watcher_Changed;
                _watcher.Renamed += Watcher_Changed;
                _watcher.Deleted += Watcher_Changed;
                _watcher.EnableRaisingEvents = true;
            }
            catch
            {
                StopWatcher();
            }
        }

        public void Stop()
        {
            _onChanged = null;
            StopWatcher();
        }

        public void Dispose()
        {
            Stop();
        }

        /// <summary>VIN encoded in the single Transferred keyreader.bin, if present.</summary>
        public static string TryGetTransferredVin()
        {
            KeyBinFields fields;
            if (!TryParseKeyReaderBin(TransferredBin, out fields))
                return null;
            return string.IsNullOrEmpty(fields.Vin) ? null : fields.Vin;
        }

        /// <summary>
        /// Retry only when the newest event is Failed and matches Transferred keyreader.bin.
        /// </summary>
        public bool CanRetryBmwUpload(KeyActivityEvent newestEvent)
        {
            if (newestEvent == null) return false;
            if (!newestEvent.Failed || newestEvent.BmwUploaded) return false;
            if (!File.Exists(TransferredBin)) return false;
            var diskVin = TryGetTransferredVin();
            if (string.IsNullOrEmpty(diskVin)) return false;
            if (string.IsNullOrEmpty(newestEvent.Vin)) return false;
            return string.Equals(diskVin, newestEvent.Vin, StringComparison.OrdinalIgnoreCase);
        }

        public static KeyActivityEvent GetNewestEvent(IList<KeyActivityEvent> events)
        {
            if (events == null || events.Count == 0) return null;
            KeyActivityEvent best = null;
            for (var i = 0; i < events.Count; i++)
            {
                var ev = events[i];
                if (ev == null || string.IsNullOrWhiteSpace(ev.Vin)) continue;
                if (best == null || ev.DetectedLocal > best.DetectedLocal)
                    best = ev;
            }
            return best;
        }

        public string GetLastKnownVin()
        {
            var list = GetRecentVins(1);
            return list.Count > 0 ? list[0] : null;
        }

        public List<string> GetRecentVins(int max)
        {
            if (max < 1) return new List<string>();
            // Never block on Refresh()'s long _gate hold — use cache only.
            lock (_vinCacheGate)
            {
                var result = new List<string>();
                foreach (var vin in _recentVinCache)
                {
                    if (string.IsNullOrEmpty(vin)) continue;
                    result.Add(vin);
                    if (result.Count >= max) break;
                }
                return result;
            }
        }

        private void UpdateRecentVinCacheUnlocked()
        {
            var vins = GetRecentVinsUnlocked(MaxVinHistory);
            lock (_vinCacheGate)
            {
                _recentVinCache = vins ?? new List<string>();
            }
        }

        private List<string> GetRecentVinsUnlocked(int max)
        {
            var result = new List<string>();
            if (max < 1) return result;
            // Newest first from persisted + bin hints
            var ordered = new List<KeyActivityEvent>(_persisted);
            ordered.Sort((a, b) => b.DetectedLocal.CompareTo(a.DetectedLocal));
            foreach (var ev in ordered)
            {
                if (string.IsNullOrEmpty(ev.Vin)) continue;
                if (ContainsVin(result, ev.Vin)) continue;
                result.Add(ev.Vin);
                if (result.Count >= max) return result;
            }
            var hints = new List<BinHint>(_binHints);
            hints.Sort((a, b) => b.Time.CompareTo(a.Time));
            foreach (var h in hints)
            {
                if (string.IsNullOrEmpty(h.Vin)) continue;
                if (ContainsVin(result, h.Vin)) continue;
                result.Add(h.Vin);
                if (result.Count >= max) break;
            }
            return result;
        }

        private static bool ContainsVin(List<string> list, string vin)
        {
            foreach (var v in list)
            {
                if (string.Equals(v, vin, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private void StopWatcher()
        {
            if (_watcher == null) return;
            try
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Created -= Watcher_Changed;
                _watcher.Changed -= Watcher_Changed;
                _watcher.Renamed -= Watcher_Changed;
                _watcher.Deleted -= Watcher_Changed;
                _watcher.Dispose();
            }
            catch { }
            _watcher = null;
        }

        private void Watcher_Changed(object sender, FileSystemEventArgs e)
        {
            if (Interlocked.Exchange(ref _notifyQueued, 1) == 1)
                return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { Thread.Sleep(150); }
                catch { }
                Interlocked.Exchange(ref _notifyQueued, 0);
                var cb = _onChanged;
                if (cb != null)
                {
                    try { cb(); }
                    catch { }
                }
            });
        }

        public KeyActivitySnapshot Refresh()
        {
            lock (_gate)
            {
                EnsureHistoryLoaded();
                var snap = new KeyActivitySnapshot();
                snap.PickupFolder = _pickupFolder;
                snap.PickupFolderExists = Directory.Exists(_pickupFolder);
                snap.UploaderLogPath = NewestLogFile(_uploaderLogDir);
                snap.KaiLogPath = NewestLogFile(_kaiLogDir);
                snap.Reader = ReadReaderSnapshot();

                try
                {
                    var enc = DesktopKeyReaderReg.ReadAbsolute().EncPath;
                    snap.EncPath = enc;
                    snap.EncFileExists = !string.IsNullOrEmpty(enc) && File.Exists(enc);
                }
                catch
                {
                    snap.EncPath = "";
                    snap.EncFileExists = true; // unknown — do not warn
                }

                try
                {
                    var bin = Path.Combine(_pickupFolder, "keyreader.bin");
                    if (File.Exists(bin))
                        snap.PickupFileLocal = File.GetLastWriteTime(bin);
                }
                catch { }

                CollectBinHintsFromDisk();
                CollectBinHintsFromSqlite();
                // Announce VIN as soon as .bin/Sqlite has it — do not wait for BMW upload logs.
                snap.NewVinReads = DetectNewVinReads();

                var live = BuildEvents(snap.UploaderLogPath, snap.KaiLogPath, snap.Reader);
                AttachBinFields(live);
                var merged = MergeEvents(live, _persisted);
                AttachBinFields(merged);
                InheritUploadStatusByVin(merged);
                merged = DeduplicateVinRows(merged);
                foreach (var ev in merged)
                {
                    TryAssignDebugBinPath(ev);
                    BackfillEventFromArchivedBin(ev);
                }
                snap.Events = merged;
                snap.KeysToday = CountKeysToday(merged);
                snap.NewEvents = DetectNewEvents(merged);
                snap.StallEvents = DetectStallEvents(merged);
                PersistEvents(merged);
                snap.Summary = BuildSummary(snap);
                UpdateRecentVinCacheUnlocked();
                return snap;
            }
        }

        /// <summary>
        /// Search persisted history (key-events.xml) plus archived key-bins filenames for older VINs.
        /// Case-insensitive match on VIN, paint, upholstery, I-level, km, reader id and detail text.
        /// Newest first; capped at <paramref name="maxResults"/> (default 100 when &lt;= 0).
        /// </summary>
        public List<KeyActivityEvent> SearchHistory(string query, int maxResults)
        {
            var results = new List<KeyActivityEvent>();
            var q = (query ?? "").Trim();
            if (q.Length == 0) return results;
            if (maxResults <= 0) maxResults = 100;

            var matched = new List<KeyActivityEvent>();
            // VINs already present in history; archived bins only add VINs not seen there.
            var seenVins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            lock (_gate)
            {
                EnsureHistoryLoaded();
                foreach (var ev in _persisted)
                {
                    if (ev == null) continue;
                    if (!string.IsNullOrEmpty(ev.Vin)) seenVins.Add(ev.Vin);
                    if (!EventMatchesQuery(ev, q)) continue;
                    var clone = CloneEvent(ev);
                    try { TryAssignDebugBinPath(clone); }
                    catch { }
                    matched.Add(clone);
                }
            }
            matched.Sort((a, b) => b.DetectedLocal.CompareTo(a.DetectedLocal));

            foreach (var ev in matched)
            {
                if (results.Count >= maxResults) break;
                results.Add(ev);
            }

            if (results.Count < maxResults)
            {
                try
                {
                    var bins = Path.Combine(KeyDebugStoreDir, "key-bins");
                    if (Directory.Exists(bins))
                    {
                        var files = Directory.GetFiles(bins, "*.bin");
                        Array.Sort(files, (a, b) => File.GetLastWriteTime(b).CompareTo(File.GetLastWriteTime(a)));
                        foreach (var f in files)
                        {
                            if (results.Count >= maxResults) break;
                            var name = Path.GetFileNameWithoutExtension(f) ?? "";
                            var us = name.LastIndexOf('_');
                            var vin = (us >= 0 ? name.Substring(us + 1) : name).Trim().ToUpperInvariant();
                            if (vin.Length != 17) continue;
                            if (name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                            if (!seenVins.Add(vin)) continue;
                            var ev = new KeyActivityEvent
                            {
                                Vin = vin,
                                DetectedLocal = File.GetLastWriteTime(f),
                                ReaderId = "",
                                Detail = "Archived key bin",
                                Status = KeyEventStatus.Unknown,
                                DebugBinPath = f
                            };
                            KeyBinFields fields;
                            if (TryParseKeyReaderBin(f, out fields) && fields != null)
                            {
                                ev.Paint = fields.Paint;
                                ev.Upholstery = fields.Upholstery;
                                ev.ILevelCurrent = fields.ILevelCurrent;
                                ev.ILevelBackup = fields.ILevelBackup;
                                ev.ILevelPlant = fields.ILevelPlant;
                                ev.MileageKm = fields.MileageKm;
                            }
                            results.Add(ev);
                        }
                    }
                }
                catch { }
            }

            results.Sort((a, b) => b.DetectedLocal.CompareTo(a.DetectedLocal));
            if (results.Count > maxResults)
                results = results.GetRange(0, maxResults);
            return results;
        }

        private static bool EventMatchesQuery(KeyActivityEvent ev, string q)
        {
            if (ev == null || string.IsNullOrEmpty(q)) return false;
            if (ContainsText(ev.Vin, q)
                || ContainsText(ev.Paint, q)
                || ContainsText(ev.Upholstery, q)
                || ContainsText(ev.ILevelCurrent, q)
                || ContainsText(ev.ReaderId, q)
                || ContainsText(ev.Detail, q)
                || ContainsText(ev.DetailText, q))
                return true;
            if (ev.MileageKm.HasValue && ev.MileageKm.Value > 0)
            {
                if (ContainsText(ev.MileageKm.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), q)
                    || ContainsText(ev.KmText, q))
                    return true;
            }
            return false;
        }

        private static bool ContainsText(string hay, string needle)
        {
            return !string.IsNullOrEmpty(hay)
                && hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void EnsureHistoryLoaded()
        {
            if (_historyLoaded) return;
            _historyLoaded = true;
            try
            {
                if (!File.Exists(EventsStorePath)) return;
                var ser = new XmlSerializer(typeof(List<KeyActivityEvent>));
                using (var fs = File.OpenRead(EventsStorePath))
                {
                    var loaded = ser.Deserialize(fs) as List<KeyActivityEvent>;
                    if (loaded == null) return;
                    _persisted.Clear();
                    foreach (var ev in loaded)
                    {
                        if (ev == null) continue;
                        _persisted.Add(ev);
                        _seenKeys.Add(EventKey(ev));
                    }
                }
            }
            catch { }
        }

        private void PersistEvents(List<KeyActivityEvent> events)
        {
            try
            {
                _persisted.Clear();
                if (events != null)
                {
                    foreach (var ev in events)
                    {
                        if (ev == null) continue;
                        _persisted.Add(CloneEvent(ev));
                        if (_persisted.Count >= MaxEvents) break;
                    }
                }
                var ser = new XmlSerializer(typeof(List<KeyActivityEvent>));
                using (var fs = File.Create(EventsStorePath))
                    ser.Serialize(fs, _persisted);
            }
            catch { }
        }

        private static KeyActivityEvent CloneEvent(KeyActivityEvent ev)
        {
            return new KeyActivityEvent
            {
                DetectedLocal = ev.DetectedLocal,
                ReaderId = ev.ReaderId,
                FileId = ev.FileId,
                Vin = ev.Vin,
                Paint = ev.Paint,
                Upholstery = ev.Upholstery,
                ILevelCurrent = ev.ILevelCurrent,
                ILevelBackup = ev.ILevelBackup,
                ILevelPlant = ev.ILevelPlant,
                MileageKm = ev.MileageKm,
                LocalSent = ev.LocalSent,
                BmwUploaded = ev.BmwUploaded,
                Failed = ev.Failed,
                Detail = ev.Detail,
                Status = ev.Status,
                StallNotified = ev.StallNotified
            };
        }

        private static string EventKey(KeyActivityEvent ev)
        {
            if (ev == null) return "";
            if (!string.IsNullOrEmpty(ev.FileId))
                return "F:" + ev.FileId;
            return "T:" + ev.DetectedLocal.ToString("yyyyMMddHHmmss") + ":V:" + (ev.Vin ?? "");
        }

        private List<string> DetectNewVinReads()
        {
            var neu = new List<string>();
            var now = DateTime.Now;
            if (!_vinAnnounceSeeded)
            {
                _vinAnnounceSeeded = true;
                foreach (var h in _binHints)
                {
                    if (h == null || string.IsNullOrEmpty(h.Vin)) continue;
                    _announcedVinKeys.Add(VinAnnounceKey(h.Vin, h.Time));
                }
                foreach (var ev in _persisted)
                {
                    if (ev == null || string.IsNullOrEmpty(ev.Vin)) continue;
                    _announcedVinKeys.Add(VinAnnounceKey(ev.Vin, ev.DetectedLocal));
                }
                return neu;
            }
            foreach (var h in _binHints)
            {
                if (h == null || string.IsNullOrEmpty(h.Vin)) continue;
                if ((now - h.Time).TotalMinutes > 5) continue;
                var key = VinAnnounceKey(h.Vin, h.Time);
                if (!_announcedVinKeys.Add(key)) continue;
                if (!ContainsVin(neu, h.Vin))
                    neu.Add(h.Vin);
            }
            if (_announcedVinKeys.Count > 500)
            {
                _announcedVinKeys.Clear();
                foreach (var h in _binHints)
                {
                    if (h == null || string.IsNullOrEmpty(h.Vin)) continue;
                    _announcedVinKeys.Add(VinAnnounceKey(h.Vin, h.Time));
                }
            }
            return neu;
        }

        private static string VinAnnounceKey(string vin, DateTime when)
        {
            // Minute bucket: same physical read (~same minute) announced once.
            return (vin ?? "").Trim().ToUpperInvariant() + "|" + when.ToString("yyyyMMddHHmm");
        }

        private List<KeyActivityEvent> DetectNewEvents(List<KeyActivityEvent> merged)
        {
            var neu = new List<KeyActivityEvent>();
            if (merged == null) return neu;
            var now = DateTime.Now;
            foreach (var ev in merged)
            {
                var key = EventKey(ev);
                if (string.IsNullOrEmpty(key)) continue;
                if (_seenKeys.Contains(key)) continue;
                _seenKeys.Add(key);
                // Only surface truly recent arrivals (avoid balloon spam on first parse of old logs).
                if ((now - ev.DetectedLocal).TotalMinutes <= 5)
                    neu.Add(ev);
            }
            if (_seenKeys.Count > 500)
            {
                _seenKeys.Clear();
                foreach (var ev in merged)
                    _seenKeys.Add(EventKey(ev));
            }
            return neu;
        }

        private List<KeyActivityEvent> DetectStallEvents(List<KeyActivityEvent> merged)
        {
            var list = new List<KeyActivityEvent>();
            if (merged == null) return list;
            var now = DateTime.Now;
            foreach (var ev in merged)
            {
                if (ev == null) continue;
                if (!ev.LocalSent || ev.BmwUploaded || ev.Failed) continue;
                if ((now - ev.DetectedLocal).TotalMinutes < _stallMinutes) continue;
                if ((now - ev.DetectedLocal).TotalHours > 24) continue;
                if (ev.StallNotified) continue;
                ev.StallNotified = true;
                list.Add(ev);
            }
            return list;
        }

        private static int CountKeysToday(List<KeyActivityEvent> events)
        {
            if (events == null) return 0;
            var today = DateTime.Today;
            var n = 0;
            foreach (var ev in events)
            {
                if (ev != null && ev.DetectedLocal.Date == today)
                    n++;
            }
            return n;
        }

        private static List<KeyActivityEvent> MergeEvents(
            List<KeyActivityEvent> live,
            List<KeyActivityEvent> persisted)
        {
            var map = new Dictionary<string, KeyActivityEvent>(StringComparer.OrdinalIgnoreCase);
            if (persisted != null)
            {
                foreach (var ev in persisted)
                {
                    if (ev == null) continue;
                    map[EventKey(ev)] = CloneEvent(ev);
                }
            }
            if (live != null)
            {
                foreach (var ev in live)
                {
                    if (ev == null) continue;
                    var key = EventKey(ev);
                    KeyActivityEvent existing;
                    if (map.TryGetValue(key, out existing))
                    {
                        // Prefer richer/live status
                        if (ev.BmwUploaded) existing.BmwUploaded = true;
                        if (ev.LocalSent) existing.LocalSent = true;
                        if (ev.Failed) existing.Failed = true;
                        if (!string.IsNullOrEmpty(ev.Vin)) existing.Vin = ev.Vin;
                        if (!string.IsNullOrEmpty(ev.Paint)) existing.Paint = ev.Paint;
                        if (!string.IsNullOrEmpty(ev.Upholstery)) existing.Upholstery = ev.Upholstery;
                        if (!string.IsNullOrEmpty(ev.ILevelCurrent)) existing.ILevelCurrent = ev.ILevelCurrent;
                        if (!string.IsNullOrEmpty(ev.ILevelBackup)) existing.ILevelBackup = ev.ILevelBackup;
                        if (!string.IsNullOrEmpty(ev.ILevelPlant)) existing.ILevelPlant = ev.ILevelPlant;
                        if (ev.MileageKm.HasValue && ev.MileageKm.Value > 0) existing.MileageKm = ev.MileageKm;
                        if (!string.IsNullOrEmpty(ev.Detail)
                            && !KeyActivityEvent.IsPickupNoiseDetail(ev.Detail))
                            existing.Detail = ev.Detail;
                        if (KeyActivityEvent.IsPickupNoiseDetail(existing.Detail))
                            existing.Detail = "";
                        if (!string.IsNullOrEmpty(ev.ReaderId)) existing.ReaderId = ev.ReaderId;
                        existing.Status = ComputeStatus(existing);
                        // keep StallNotified from existing
                    }
                    else
                    {
                        map[key] = CloneEvent(ev);
                    }
                }
            }
            var list = new List<KeyActivityEvent>();
            foreach (var kv in map)
                list.Add(kv.Value);
            foreach (var ev in list)
                ev.Status = ComputeStatus(ev);
            list.Sort((a, b) => b.DetectedLocal.CompareTo(a.DetectedLocal));
            if (list.Count > MaxEvents)
                list = list.GetRange(0, MaxEvents);
            return list;
        }

        private static KeyEventStatus ComputeStatus(KeyActivityEvent ev)
        {
            if (ev.Failed) return KeyEventStatus.Failed;
            if (ev.BmwUploaded) return KeyEventStatus.BmwOk;
            if (ev.LocalSent) return KeyEventStatus.LocalOk;
            if ((DateTime.Now - ev.DetectedLocal).TotalMinutes <= 2)
                return KeyEventStatus.Waiting;
            return KeyEventStatus.Unknown;
        }

        private void CollectBinHintsFromDisk()
        {
            RememberBinFromPath(Path.Combine(_pickupFolder, "keyreader.bin"));
            RememberBinFromPath(TransferredBin);
            try
            {
                var dir = Path.GetDirectoryName(TransferredBin);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    foreach (var f in Directory.GetFiles(dir, "*.bin"))
                        RememberBinFromPath(f);
                }
            }
            catch { }
        }

        private void RememberBin(DateTime when, KeyBinFields fields)
        {
            if (fields == null || string.IsNullOrEmpty(fields.Vin)) return;
            for (var i = 0; i < _binHints.Count; i++)
            {
                if (string.Equals(_binHints[i].Vin, fields.Vin, StringComparison.OrdinalIgnoreCase)
                    && Math.Abs((_binHints[i].Time - when).TotalSeconds) <= 2)
                {
                    // Merge non-empty fields — do not wipe I-level/paint from a richer prior hint.
                    var existing = _binHints[i];
                    var merged = BinHint.From(when, fields);
                    if (string.IsNullOrEmpty(merged.Paint) && !string.IsNullOrEmpty(existing.Paint))
                        merged.Paint = existing.Paint;
                    if (string.IsNullOrEmpty(merged.Upholstery) && !string.IsNullOrEmpty(existing.Upholstery))
                        merged.Upholstery = existing.Upholstery;
                    if (string.IsNullOrEmpty(merged.ILevelCurrent) && !string.IsNullOrEmpty(existing.ILevelCurrent))
                        merged.ILevelCurrent = existing.ILevelCurrent;
                    if (string.IsNullOrEmpty(merged.ILevelBackup) && !string.IsNullOrEmpty(existing.ILevelBackup))
                        merged.ILevelBackup = existing.ILevelBackup;
                    if (string.IsNullOrEmpty(merged.ILevelPlant) && !string.IsNullOrEmpty(existing.ILevelPlant))
                        merged.ILevelPlant = existing.ILevelPlant;
                    if ((!merged.MileageKm.HasValue || merged.MileageKm.Value <= 0)
                        && existing.MileageKm.HasValue && existing.MileageKm.Value > 0)
                        merged.MileageKm = existing.MileageKm;
                    _binHints[i] = merged;
                    return;
                }
            }
            _binHints.Add(BinHint.From(when, fields));
            if (_binHints.Count > MaxBinHints)
            {
                _binHints.Sort((a, b) => a.Time.CompareTo(b.Time));
                _binHints.RemoveRange(0, _binHints.Count - MaxBinHints);
            }
        }

        private void CollectBinHintsFromSqlite()
        {
            if (!File.Exists(_sqlitePath)) return;
            try
            {
                var cs = "Data Source=" + _sqlitePath + ";Version=3;Read Only=True;FailIfMissing=True";
                using (var conn = new SQLiteConnection(cs))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText =
                            "SELECT Vin, Color, Upholstery, TotalDistanceKm, ReadDate, KeyBinary FROM KeyBaseDataEntity " +
                            "WHERE Vin IS NOT NULL AND Vin <> '' ORDER BY ReadDate DESC LIMIT 50";
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                var fields = new KeyBinFields();
                                fields.Vin = (Convert.ToString(r["Vin"]) ?? "").Trim().ToUpperInvariant();
                                try { fields.Paint = Convert.ToString(r["Color"]); }
                                catch { }
                                try { fields.Upholstery = Convert.ToString(r["Upholstery"]); }
                                catch { }
                                try
                                {
                                    if (r["TotalDistanceKm"] != DBNull.Value && r["TotalDistanceKm"] != null)
                                    {
                                        var tk = Convert.ToInt64(r["TotalDistanceKm"]);
                                        if (tk > 0 && tk <= int.MaxValue) fields.MileageKm = (int)tk;
                                    }
                                }
                                catch { }
                                try
                                {
                                    if (r["KeyBinary"] != DBNull.Value && r["KeyBinary"] != null)
                                    {
                                        var blob = r["KeyBinary"] as byte[];
                                        if (blob != null && blob.Length > 0)
                                        {
                                            KeyBinFields fromBin;
                                            if (TryParseKeyReaderBytes(blob, out fromBin) && fromBin != null)
                                            {
                                                if (string.IsNullOrEmpty(fields.Paint) && !string.IsNullOrEmpty(fromBin.Paint))
                                                    fields.Paint = fromBin.Paint;
                                                if (string.IsNullOrEmpty(fields.Upholstery) && !string.IsNullOrEmpty(fromBin.Upholstery))
                                                    fields.Upholstery = fromBin.Upholstery;
                                                if ((!fields.MileageKm.HasValue || fields.MileageKm.Value <= 0)
                                                    && fromBin.MileageKm.HasValue && fromBin.MileageKm.Value > 0)
                                                    fields.MileageKm = fromBin.MileageKm;
                                                if (string.IsNullOrEmpty(fields.ILevelCurrent) && !string.IsNullOrEmpty(fromBin.ILevelCurrent))
                                                    fields.ILevelCurrent = fromBin.ILevelCurrent;
                                                if (string.IsNullOrEmpty(fields.ILevelBackup) && !string.IsNullOrEmpty(fromBin.ILevelBackup))
                                                    fields.ILevelBackup = fromBin.ILevelBackup;
                                                if (string.IsNullOrEmpty(fields.ILevelPlant) && !string.IsNullOrEmpty(fromBin.ILevelPlant))
                                                    fields.ILevelPlant = fromBin.ILevelPlant;
                                            }
                                        }
                                    }
                                }
                                catch { }
                                DateTime when = DateTime.Now;
                                if (r["ReadDate"] != DBNull.Value && r["ReadDate"] != null)
                                {
                                    try { when = Convert.ToDateTime(r["ReadDate"]); }
                                    catch { }
                                }
                                if (!string.IsNullOrEmpty(fields.Vin))
                                    RememberBin(when, fields);
                            }
                        }
                    }
                }
            }
            catch
            {
                // Color/Upholstery/KeyBinary columns may be missing — fall back VIN-only
                try
                {
                    var cs = "Data Source=" + _sqlitePath + ";Version=3;Read Only=True;FailIfMissing=True";
                    using (var conn = new SQLiteConnection(cs))
                    {
                        conn.Open();
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText =
                                "SELECT Vin, ReadDate FROM KeyBaseDataEntity " +
                                "WHERE Vin IS NOT NULL AND Vin <> '' ORDER BY ReadDate DESC LIMIT 50";
                            using (var r = cmd.ExecuteReader())
                            {
                                while (r.Read())
                                {
                                    var fields = new KeyBinFields();
                                    fields.Vin = (Convert.ToString(r["Vin"]) ?? "").Trim().ToUpperInvariant();
                                    DateTime when = DateTime.Now;
                                    if (r["ReadDate"] != DBNull.Value && r["ReadDate"] != null)
                                    {
                                        try { when = Convert.ToDateTime(r["ReadDate"]); }
                                        catch { }
                                    }
                                    if (!string.IsNullOrEmpty(fields.Vin))
                                        RememberBin(when, fields);
                                }
                            }
                        }
                    }
                }
                catch { }
            }
        }

        private void AttachBinFields(List<KeyActivityEvent> events)
        {
            if (events == null || events.Count == 0 || _binHints.Count == 0)
                return;
            for (var ei = 0; ei < events.Count; ei++)
            {
                var ev = events[ei];
                if (ev == null) continue;
                BinHint best = null;
                var bestDelta = double.MaxValue;
                foreach (var h in _binHints)
                {
                    var d = Math.Abs((h.Time - ev.DetectedLocal).TotalSeconds);
                    if (d <= 120 && d < bestDelta)
                    {
                        bestDelta = d;
                        best = h;
                    }
                }
                if (best == null && ei == 0)
                {
                    foreach (var h in _binHints)
                    {
                        if (best == null || h.Time > best.Time)
                            best = h;
                    }
                    if (best != null
                        && Math.Abs((best.Time - ev.DetectedLocal).TotalMinutes) > 10)
                        best = null;
                }
                if (best == null) continue;
                if (string.IsNullOrEmpty(ev.Vin)) ev.Vin = best.Vin;
                if (string.IsNullOrEmpty(ev.Paint)) ev.Paint = best.Paint;
                if (string.IsNullOrEmpty(ev.Upholstery)) ev.Upholstery = best.Upholstery;
                if (string.IsNullOrEmpty(ev.ILevelCurrent)) ev.ILevelCurrent = best.ILevelCurrent;
                if (string.IsNullOrEmpty(ev.ILevelBackup)) ev.ILevelBackup = best.ILevelBackup;
                if (string.IsNullOrEmpty(ev.ILevelPlant)) ev.ILevelPlant = best.ILevelPlant;
                if ((!ev.MileageKm.HasValue || ev.MileageKm.Value <= 0)
                    && best.MileageKm.HasValue && best.MileageKm.Value > 0)
                    ev.MileageKm = best.MileageKm;
                TryAssignDebugBinPath(ev);
                BackfillEventFromArchivedBin(ev);
            }
        }

        private void TryAssignDebugBinPath(KeyActivityEvent ev)
        {
            if (ev == null || string.IsNullOrEmpty(ev.Vin)) return;
            string path = null;
            lock (_gate)
            {
                _lastDebugBinByVin.TryGetValue(ev.Vin, out path);
            }
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                path = ResolveDebugBinForVin(ev.Vin);
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    lock (_gate)
                    {
                        _lastDebugBinByVin[ev.Vin] = path;
                    }
                }
            }
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                ev.DebugBinPath = path;
        }

        private void RememberBinFromPath(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                byte[] bytes;
                DateTime mtimeUtc;
                if (!TryReadKeyReaderBytes(path, out bytes, out mtimeUtc)) return;
                KeyBinFields fields;
                if (!TryParseKeyReaderBytes(bytes, out fields)) return;
                try
                {
                    var archived = WriteKeyReadDebug(path, bytes, mtimeUtc, fields);
                    if (!string.IsNullOrEmpty(archived) && fields != null && !string.IsNullOrEmpty(fields.Vin))
                    {
                        lock (_gate)
                        {
                            _lastDebugBinByVin[fields.Vin] = archived;
                        }
                    }
                }
                catch { }
                RememberBin(File.GetLastWriteTime(path), fields);
            }
            catch { }
        }

        internal static bool TryParseKeyReaderBin(string path, out KeyBinFields fields)
        {
            fields = null;
            try
            {
                byte[] bytes;
                DateTime unused;
                if (!TryReadKeyReaderBytes(path, out bytes, out unused)) return false;
                return TryParseKeyReaderBytes(bytes, out fields);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryReadKeyReaderBytes(string path, out byte[] bytes, out DateTime mtimeUtc)
        {
            bytes = null;
            mtimeUtc = DateTime.UtcNow;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                try { mtimeUtc = File.GetLastWriteTimeUtc(path); }
                catch { mtimeUtc = DateTime.UtcNow; }
                var len = (int)Math.Min(fs.Length, 8192);
                bytes = new byte[len];
                var read = 0;
                while (read < len)
                {
                    var n = fs.Read(bytes, read, len - read);
                    if (n <= 0) break;
                    read += n;
                }
                if (read <= 0)
                {
                    bytes = null;
                    return false;
                }
                if (read != bytes.Length)
                {
                    var trimmed = new byte[read];
                    Buffer.BlockCopy(bytes, 0, trimmed, 0, read);
                    bytes = trimmed;
                }
            }
            return true;
        }

        private static string KeyDebugStoreDir
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "KaiAosRenew");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        private static string Sha256Hex(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(bytes ?? new byte[0]);
                var sb = new StringBuilder(hash.Length * 2);
                for (var i = 0; i < hash.Length; i++)
                    sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        private static string SanitizeVinForFile(string vin)
        {
            if (string.IsNullOrWhiteSpace(vin)) return "NOVIN";
            var sb = new StringBuilder(vin.Length);
            foreach (var ch in vin.Trim().ToUpperInvariant())
            {
                if ((ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9'))
                    sb.Append(ch);
            }
            return sb.Length > 0 ? sb.ToString() : "NOVIN";
        }

        /// <summary>
        /// Append parse diagnostics and archive a copy of the raw .bin under %AppData%\KaiAosRenew.
        /// Quiet (no UI); used so workshop PCs can share key-reader-parse.log / key-bins for analysis.
        /// Returns the archived .bin path (existing or newly written), or null.
        /// </summary>
        private static string WriteKeyReadDebug(string path, byte[] bytes, DateTime mtimeUtc, KeyBinFields fields)
        {
            if (bytes == null || bytes.Length == 0) return null;
            var vin = fields != null ? (fields.Vin ?? "") : "";
            var hash = Sha256Hex(bytes);
            lock (KeyDebugGate)
            {
                var now = DateTime.UtcNow;
                if (!string.IsNullOrEmpty(vin)
                    && string.Equals(vin, _lastKeyDebugVin, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(hash, _lastKeyDebugHash, StringComparison.Ordinal)
                    && (now - _lastKeyDebugUtc).TotalSeconds < KeyDebugDedupSeconds)
                {
                    try
                    {
                        var bins = Path.Combine(KeyDebugStoreDir, "key-bins");
                        var single = Path.Combine(bins, SanitizeVinForFile(vin) + ".bin");
                        if (File.Exists(single)) return single;
                        var hits = Directory.GetFiles(bins, "*_" + SanitizeVinForFile(vin) + ".bin");
                        Array.Sort(hits, StringComparer.OrdinalIgnoreCase);
                        if (hits.Length > 0) return hits[hits.Length - 1];
                    }
                    catch { }
                    return null;
                }

                var store = KeyDebugStoreDir;
                var binsDir = Path.Combine(store, "key-bins");
                Directory.CreateDirectory(binsDir);

                // Skip archive rewrite if current per-VIN file has same size+hash.
                var vinToken = SanitizeVinForFile(vin);
                var skipArchive = false;
                var archivedPath = (string)null;
                try
                {
                    var single = Path.Combine(binsDir, vinToken + ".bin");
                    var newest = File.Exists(single) ? single : null;
                    if (newest == null)
                    {
                        var existing = Directory.GetFiles(binsDir, "*_" + vinToken + ".bin");
                        Array.Sort(existing, StringComparer.OrdinalIgnoreCase);
                        if (existing.Length > 0)
                            newest = existing[existing.Length - 1];
                    }
                    if (newest != null)
                    {
                        var fi = new FileInfo(newest);
                        if (fi.Length == bytes.Length
                            && (now - fi.LastWriteTimeUtc).TotalSeconds < KeyDebugDedupSeconds)
                        {
                            var prev = File.ReadAllBytes(newest);
                            if (string.Equals(Sha256Hex(prev), hash, StringComparison.Ordinal))
                            {
                                skipArchive = true;
                                archivedPath = newest;
                            }
                        }
                    }
                }
                catch { }

                if (!skipArchive)
                {
                    // One archive file per VIN (overwrite). Remove legacy timestamped siblings.
                    var dest = Path.Combine(binsDir, vinToken + ".bin");
                    File.WriteAllBytes(dest, bytes);
                    archivedPath = dest;
                    try
                    {
                        var siblings = Directory.GetFiles(binsDir, "*_" + vinToken + ".bin");
                        for (var si = 0; si < siblings.Length; si++)
                        {
                            try
                            {
                                if (!string.Equals(siblings[si], dest, StringComparison.OrdinalIgnoreCase))
                                    File.Delete(siblings[si]);
                            }
                            catch { }
                        }
                    }
                    catch { }
                    try
                    {
                        var all = Directory.GetFiles(binsDir, "*.bin");
                        if (all.Length > KeyBinArchiveMax)
                        {
                            Array.Sort(all, (a, b) => File.GetLastWriteTimeUtc(a).CompareTo(File.GetLastWriteTimeUtc(b)));
                            var drop = all.Length - KeyBinArchiveMax;
                            for (var i = 0; i < drop; i++)
                            {
                                try { File.Delete(all[i]); }
                                catch { }
                            }
                        }
                    }
                    catch { }
                }

                var ascii = Encoding.ASCII.GetString(bytes);
                var paintHits = new List<string>();
                for (var i = 0; i + 11 <= bytes.Length; i++)
                {
                    if (bytes[i] != 0x84 || bytes[i + 1] != 0x01) continue;
                    string tagPaint, tagUph;
                    if (!TryReadPaintUphAscii(bytes, i + 3, out tagPaint, out tagUph)) continue;
                    paintHits.Add("84:01:" + (char)bytes[i + 2] + tagPaint + tagUph + "@0x" + i.ToString("X"));
                }
                for (var pu = RxPaintUph.Match(ascii); pu.Success; pu = pu.NextMatch())
                {
                    var p = pu.Groups[1].Value.ToUpperInvariant();
                    var u = pu.Groups[2].Value.ToUpperInvariant();
                    if (!IsValidPaintCode(p) || !IsValidUpholsteryCode(u)) continue;
                    paintHits.Add(pu.Value + "@0x" + pu.Index.ToString("X"));
                }

                var ilevelHits = new List<string>();
                for (var i = 0; i + 3 < bytes.Length; i++)
                {
                    var c0 = bytes[i];
                    if (!((c0 >= (byte)'A' && c0 <= (byte)'Z') || (c0 >= (byte)'a' && c0 <= (byte)'z')))
                        continue;
                    var fam = Encoding.ASCII.GetString(bytes, i, 4).ToUpperInvariant();
                    if (!RxILevelFamily.IsMatch(fam)) continue;
                    ilevelHits.Add(fam + "@0x" + i.ToString("X"));
                }

                var sb = new StringBuilder(bytes.Length * 4 + 512);
                sb.Append("==== ").Append(now.ToString("o")).Append(" path=").Append(path ?? "")
                    .Append(" size=").Append(bytes.Length)
                    .Append(" mtime=").Append(mtimeUtc.ToString("o"))
                    .Append(" sha256=").Append(hash)
                    .AppendLine();
                sb.Append("VIN=").Append(string.IsNullOrEmpty(vin) ? "(none)" : vin).AppendLine();
                sb.Append("Paint=").Append(fields != null && !string.IsNullOrEmpty(fields.Paint) ? fields.Paint : "(none)")
                    .Append(" Upholstery=")
                    .Append(fields != null && !string.IsNullOrEmpty(fields.Upholstery) ? fields.Upholstery : "(none)")
                    .AppendLine();
                sb.Append("Km=")
                    .Append(fields != null && fields.MileageKm.HasValue ? fields.MileageKm.Value.ToString() : "(none)")
                    .AppendLine();
                sb.Append("ILevelCurrent=")
                    .Append(fields != null && !string.IsNullOrEmpty(fields.ILevelCurrent) ? fields.ILevelCurrent : "(none)")
                    .Append(" Backup=")
                    .Append(fields != null && !string.IsNullOrEmpty(fields.ILevelBackup) ? fields.ILevelBackup : "(none)")
                    .Append(" Plant=")
                    .Append(fields != null && !string.IsNullOrEmpty(fields.ILevelPlant) ? fields.ILevelPlant : "(none)")
                    .AppendLine();
                sb.Append("PaintUphRegexHits= ");
                if (paintHits.Count == 0) sb.Append("(none)");
                else sb.Append(string.Join(" | ", paintHits.ToArray()));
                sb.AppendLine();
                sb.Append("ILevelFamilyHits= ");
                if (ilevelHits.Count == 0) sb.Append("(none)");
                else sb.Append(string.Join(" ", ilevelHits.ToArray()));
                sb.AppendLine();
                sb.AppendLine("--- hex+ascii (full file) ---");
                for (var off = 0; off < bytes.Length; off += 16)
                {
                    sb.Append(off.ToString("X4")).Append(':');
                    var lineAscii = new StringBuilder(16);
                    var n = Math.Min(16, bytes.Length - off);
                    for (var j = 0; j < 16; j++)
                    {
                        sb.Append(' ');
                        if (j < n)
                        {
                            var b = bytes[off + j];
                            sb.Append(b.ToString("x2"));
                            lineAscii.Append(b >= 32 && b <= 126 ? (char)b : '.');
                        }
                        else
                            sb.Append("  ");
                    }
                    sb.Append("  | ").Append(lineAscii).AppendLine();
                }
                sb.AppendLine("====");
                sb.AppendLine();

                var logPath = Path.Combine(store, "key-reader-parse.log");
                try
                {
                    if (File.Exists(logPath))
                    {
                        var fi = new FileInfo(logPath);
                        if (fi.Length >= 10L * 1024 * 1024)
                        {
                            var bak = logPath + ".1";
                            if (File.Exists(bak))
                                File.Delete(bak);
                            File.Move(logPath, bak);
                        }
                    }
                }
                catch { }
                File.AppendAllText(logPath, sb.ToString(), Encoding.UTF8);

                _lastKeyDebugVin = vin ?? "";
                _lastKeyDebugHash = hash;
                _lastKeyDebugUtc = now;
                return archivedPath;
            }
        }

        private static bool IsAsciiAlnumUpper(byte b)
        {
            return (b >= (byte)'0' && b <= (byte)'9')
                || (b >= (byte)'A' && b <= (byte)'Z')
                || (b >= (byte)'a' && b <= (byte)'z');
        }

        private static bool IsValidPaintCode(string paint)
        {
            if (string.IsNullOrEmpty(paint) || paint.Length != 4) return false;
            var hasDigit = false;
            for (var i = 0; i < 4; i++)
            {
                var c = char.ToUpperInvariant(paint[i]);
                if (c >= '0' && c <= '9') hasDigit = true;
                else if (c < 'A' || c > 'Z') return false;
            }
            return hasDigit;
        }

        private static bool IsValidUpholsteryCode(string uph)
        {
            if (string.IsNullOrEmpty(uph) || uph.Length != 4) return false;
            var letters = 0;
            for (var i = 0; i < 4; i++)
            {
                var c = char.ToUpperInvariant(uph[i]);
                if (c >= 'A' && c <= 'Z') letters++;
                else if (c < '0' || c > '9') return false;
            }
            return letters >= 2;
        }

        private static bool TryReadPaintUphAscii(byte[] bytes, int paintStart, out string paint, out string upholstery)
        {
            paint = null;
            upholstery = null;
            if (bytes == null || paintStart < 0 || paintStart + 8 > bytes.Length) return false;
            for (var i = 0; i < 8; i++)
            {
                if (!IsAsciiAlnumUpper(bytes[paintStart + i])) return false;
            }
            paint = Encoding.ASCII.GetString(bytes, paintStart, 4).ToUpperInvariant();
            upholstery = Encoding.ASCII.GetString(bytes, paintStart + 4, 4).ToUpperInvariant();
            return IsValidPaintCode(paint) && IsValidUpholsteryCode(upholstery);
        }

        /// <summary>
        /// Fill missing Paint/Upholstery/I-level/Km on a persisted event from its archived key-bins\{VIN}.bin.
        /// </summary>
        private void BackfillEventFromArchivedBin(KeyActivityEvent ev)
        {
            if (ev == null || string.IsNullOrEmpty(ev.Vin)) return;
            var needsPaint = string.IsNullOrEmpty(ev.Paint);
            var needsUph = string.IsNullOrEmpty(ev.Upholstery);
            var needsI = string.IsNullOrEmpty(ev.ILevelCurrent);
            var needsKm = !ev.MileageKm.HasValue || ev.MileageKm.Value <= 0;
            if (!needsPaint && !needsUph && !needsI && !needsKm) return;
            var path = ev.DebugBinPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                KeyBinFields fields;
                if (!TryParseKeyReaderBin(path, out fields) || fields == null) return;
                if (needsPaint && !string.IsNullOrEmpty(fields.Paint)) ev.Paint = fields.Paint;
                if (needsUph && !string.IsNullOrEmpty(fields.Upholstery)) ev.Upholstery = fields.Upholstery;
                if (needsI && !string.IsNullOrEmpty(fields.ILevelCurrent)) ev.ILevelCurrent = fields.ILevelCurrent;
                if (string.IsNullOrEmpty(ev.ILevelBackup) && !string.IsNullOrEmpty(fields.ILevelBackup))
                    ev.ILevelBackup = fields.ILevelBackup;
                if (string.IsNullOrEmpty(ev.ILevelPlant) && !string.IsNullOrEmpty(fields.ILevelPlant))
                    ev.ILevelPlant = fields.ILevelPlant;
                if (needsKm && fields.MileageKm.HasValue && fields.MileageKm.Value > 0)
                    ev.MileageKm = fields.MileageKm;
            }
            catch { }
        }

        internal static bool TryParseKeyReaderBytes(byte[] bytes, out KeyBinFields fields)
        {
            fields = null;
            if (bytes == null || bytes.Length < 20) return false;
            var ascii = Encoding.ASCII.GetString(bytes);
            var m = RxVin.Match(ascii);
            if (!m.Success) return false;
            var vin = m.Groups[1].Value.ToUpperInvariant();
            var letters = 0;
            for (var i = 0; i < vin.Length; i++)
            {
                if (vin[i] >= 'A' && vin[i] <= 'Z') letters++;
            }
            if (letters < 2) return false;

            fields = new KeyBinFields { Vin = vin };
            var vinIndex = m.Index;
            var vinEnd = vinIndex + 17;
            string paint = null;
            string upholstery = null;
            var puRecordStart = -1; // start of prefix byte before paint (i.e. tag+2 / %)

            // 1) Prefer 0x84 0x01 <anyByte> <Paint:4> <Upholstery:4> closest to VIN (outside VIN span).
            var bestTagDist = int.MaxValue;
            for (var i = 0; i + 11 <= bytes.Length; i++)
            {
                if (bytes[i] != 0x84 || bytes[i + 1] != 0x01) continue;
                var prefixAt = i + 2;
                var paintAt = i + 3;
                // Entire paint/uph block and tag must sit outside the 17-byte VIN.
                if (i < vinEnd && paintAt + 8 > vinIndex) continue;
                string tagPaint, tagUph;
                if (!TryReadPaintUphAscii(bytes, paintAt, out tagPaint, out tagUph)) continue;
                var dist = Math.Abs(prefixAt - vinIndex);
                if (dist < bestTagDist)
                {
                    bestTagDist = dist;
                    paint = tagPaint;
                    upholstery = tagUph;
                    puRecordStart = prefixAt;
                }
            }

            // 2) Fallback: fixed VIN-relative layout at vinIndex+24..31.
            if (paint == null && vinIndex + 32 <= bytes.Length)
            {
                string fixedPaint, fixedUph;
                if (TryReadPaintUphAscii(bytes, vinIndex + 24, out fixedPaint, out fixedUph))
                {
                    paint = fixedPaint;
                    upholstery = fixedUph;
                    puRecordStart = vinIndex + 23; // prefix byte before paint
                }
            }

            // 3) Fallback: %PaintUph regex (alnum paint with >=1 digit), closest to VIN.
            if (paint == null)
            {
                Match bestPu = null;
                var bestPuDist = int.MaxValue;
                for (var pu = RxPaintUph.Match(ascii); pu.Success; pu = pu.NextMatch())
                {
                    var p = pu.Groups[1].Value.ToUpperInvariant();
                    var u = pu.Groups[2].Value.ToUpperInvariant();
                    if (!IsValidPaintCode(p) || !IsValidUpholsteryCode(u)) continue;
                    var dist = Math.Abs(pu.Index - vinIndex);
                    if (dist < bestPuDist)
                    {
                        bestPuDist = dist;
                        bestPu = pu;
                    }
                }
                if (bestPu != null)
                {
                    paint = bestPu.Groups[1].Value.ToUpperInvariant();
                    upholstery = bestPu.Groups[2].Value.ToUpperInvariant();
                    puRecordStart = bestPu.Index;
                }
            }

            if (!string.IsNullOrEmpty(paint)) fields.Paint = paint;
            if (!string.IsNullOrEmpty(upholstery)) fields.Upholstery = upholstery;

            // Mileage: BE24 at puStart+13, else vinIndex+36, else 0x28.
            var kmOffset = -1;
            if (puRecordStart >= 0 && puRecordStart + 13 + 3 <= bytes.Length)
                kmOffset = puRecordStart + 13;
            else if (vinIndex + 36 + 3 <= bytes.Length)
                kmOffset = vinIndex + 36;
            else if (bytes.Length > 0x2A)
                kmOffset = 0x28;
            if (kmOffset >= 0 && kmOffset + 3 <= bytes.Length)
            {
                var km = (bytes[kmOffset] << 16) | (bytes[kmOffset + 1] << 8) | bytes[kmOffset + 2];
                if (km >= 1 && km <= 1999999 && km != 0xFFFFFF)
                    fields.MileageKm = km;
            }

            // I-levels: family [A-Z][0-9A-F]{3} ASCII followed by 4 payload bytes (any series).
            var levels = new List<string>();
            for (var i = 0; i + 7 < bytes.Length; i++)
            {
                var c0 = bytes[i];
                if (!((c0 >= (byte)'A' && c0 <= (byte)'Z') || (c0 >= (byte)'a' && c0 <= (byte)'z')))
                    continue;
                if (i + 3 >= bytes.Length) break;
                var fam = Encoding.ASCII.GetString(bytes, i, 4).ToUpperInvariant();
                if (!RxILevelFamily.IsMatch(fam)) continue;
                // Prefer records near end of file (known layout ~0x1E0)
                if (i < 0x180) continue;
                var b0 = bytes[i + 4];
                var b1 = bytes[i + 5];
                var b2 = bytes[i + 6];
                var b3 = bytes[i + 7];
                // Decode: yy = b0, mm = b1, build = b2<<8|b3
                var yy = b0;
                var mm = b1;
                var build = (b2 << 8) | b3;
                string formatted;
                if (yy >= 1 && yy <= 99 && mm >= 1 && mm <= 12)
                    formatted = string.Format("{0}-{1:D2}-{2:D2}-{3}", fam, yy, mm, build);
                else
                    formatted = fam;
                if (levels.Count > 0
                    && string.Equals(levels[levels.Count - 1], formatted, StringComparison.Ordinal))
                    continue;
                levels.Add(formatted);
                i += 7;
                if (levels.Count >= 3) break;
            }
            // ASCII fallback when binary decode found nothing (pre-formatted I-levels in text).
            if (levels.Count == 0)
            {
                for (var im = RxILevelAscii.Match(ascii); im.Success && levels.Count < 3; im = im.NextMatch())
                {
                    var formatted = im.Groups[1].Value.ToUpperInvariant();
                    if (levels.Count > 0
                        && string.Equals(levels[levels.Count - 1], formatted, StringComparison.Ordinal))
                        continue;
                    levels.Add(formatted);
                }
            }
            if (levels.Count > 0) fields.ILevelCurrent = levels[0];
            if (levels.Count > 1) fields.ILevelBackup = levels[1];
            if (levels.Count > 2) fields.ILevelPlant = levels[2];
            return true;
        }

        private static string BuildSummary(KeyActivitySnapshot snap)
        {
            var sb = new StringBuilder();
            if (snap.Reader != null && snap.Reader.Ok)
            {
                sb.Append("Reader ").Append(snap.Reader.KeyReaderId ?? "?");
                if (snap.Reader.LastKeyReceivedLocal.HasValue)
                    sb.Append(" — last key ").Append(snap.Reader.LastKeyReceivedLocal.Value.ToString("g"));
                else
                    sb.Append(" — no LastKeyReceived");
            }
            else if (snap.Reader != null && !string.IsNullOrEmpty(snap.Reader.Message))
                sb.Append(snap.Reader.Message);
            else
                sb.Append("Reader status unavailable");

            sb.Append(" | today ").Append(snap.KeysToday);

            if (snap.Events != null && snap.Events.Count > 0)
            {
                var last = snap.Events[0];
                sb.Append(" | latest: ").Append(last.StatusText);
                if (!string.IsNullOrEmpty(last.Vin))
                    sb.Append(" VIN ").Append(last.Vin);
                if (!string.IsNullOrEmpty(last.Paint))
                    sb.Append(" ").Append(last.Paint);
                if (!string.IsNullOrEmpty(last.ILevelCurrent))
                    sb.Append(" ").Append(last.ILevelCurrent);
            }
            return sb.ToString();
        }

        /// <summary>Quick reader row read (no gate) for startup self-check.</summary>
        public bool? PeekReaderInactive()
        {
            try
            {
                var r = ReadReaderSnapshot();
                return r != null && r.Ok ? r.Inactive : null;
            }
            catch
            {
                return null;
            }
        }

        private KeyReaderSnapshot ReadReaderSnapshot()
        {
            var info = new KeyReaderSnapshot();
            if (!File.Exists(_sqlitePath))
            {
                info.Ok = false;
                info.Message = "SqliteKaiDb.db not found";
                return info;
            }
            try
            {
                var cs = "Data Source=" + _sqlitePath + ";Version=3;Read Only=True;FailIfMissing=True";
                using (var conn = new SQLiteConnection(cs))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText =
                            "SELECT KeyReaderId, HwAddress, FirmwareVersion, IPAddress, LastKeyReceived, Inactive " +
                            "FROM KeyReaderEntity ORDER BY KeyReaderPk LIMIT 1";
                        using (var r = cmd.ExecuteReader())
                        {
                            if (!r.Read())
                            {
                                info.Ok = false;
                                info.Message = "No KeyReaderEntity rows";
                                return info;
                            }
                            info.Ok = true;
                            info.KeyReaderId = r["KeyReaderId"] as string;
                            info.HwAddress = r["HwAddress"] as string;
                            info.FirmwareVersion = r["FirmwareVersion"] as string;
                            info.IpAddress = r["IPAddress"] as string;
                            if (r["LastKeyReceived"] != DBNull.Value && r["LastKeyReceived"] != null)
                            {
                                try { info.LastKeyReceivedLocal = Convert.ToDateTime(r["LastKeyReceived"]); }
                                catch { }
                            }
                            try
                            {
                                var inactiveRaw = r["Inactive"];
                                if (inactiveRaw != DBNull.Value && inactiveRaw != null)
                                {
                                    try { info.Inactive = Convert.ToInt32(inactiveRaw) != 0; }
                                    catch
                                    {
                                        try { info.Inactive = Convert.ToBoolean(inactiveRaw); }
                                        catch { }
                                    }
                                }
                            }
                            catch { }
                            info.Message = "OK";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                info.Ok = false;
                info.Message = "SQLite: " + ex.Message;
            }
            return info;
        }

        private static List<KeyActivityEvent> BuildEvents(
            string uploaderLog,
            string kaiLog,
            KeyReaderSnapshot reader)
        {
            var picks = new List<ParsedLine>();
            var locals = new List<ParsedLine>();
            var bmws = new List<ParsedLine>();
            var fails = new List<ParsedLine>();
            var readerIds = new List<ParsedLine>();

            foreach (var line in ReadLogTailLines(uploaderLog))
            {
                DateTime ts;
                string body;
                if (!TryParseLogLine(line, out ts, out body)) continue;
                if (IsNoise(body)) continue;

                if (ContainsIgnoreCase(body, "Picked up binary file")
                    || ContainsIgnoreCase(body, "New KeyReader folder"))
                {
                    picks.Add(new ParsedLine { Time = ts, Body = Redact(body), Kind = "pick" });
                }
                else if (ContainsIgnoreCase(body, "successfully sent to KAI Dezentral"))
                {
                    locals.Add(new ParsedLine { Time = ts, Body = Redact(body), Kind = "local", Guid = ExtractGuid(body) });
                }
                else if (ContainsIgnoreCase(body, "Upload Url:") || ContainsIgnoreCase(body, "keyreaderids/"))
                {
                    var rid = ExtractReaderId(body);
                    if (!string.IsNullOrEmpty(rid))
                        readerIds.Add(new ParsedLine { Time = ts, Body = Redact(body), Kind = "reader", Guid = rid });
                }
                else if (LooksLikeUploaderFail(body))
                {
                    fails.Add(new ParsedLine { Time = ts, Body = Redact(body), Kind = "fail" });
                }
            }

            foreach (var line in ReadLogTailLines(kaiLog))
            {
                DateTime ts;
                string body;
                if (!TryParseLogLine(line, out ts, out body)) continue;
                if (IsNoise(body)) continue;

                if (ContainsIgnoreCase(body, "Successfully uploaded key data to KAI Central"))
                {
                    bmws.Add(new ParsedLine { Time = ts, Body = Redact(body), Kind = "bmw" });
                }
                else if (ContainsIgnoreCase(body, "PutKeyDataV1")
                    && ContainsIgnoreCase(body, "Enter method"))
                {
                    picks.Add(new ParsedLine { Time = ts, Body = Redact(body), Kind = "put" });
                }
                else if (LooksLikeKaiUploadFail(body))
                {
                    fails.Add(new ParsedLine { Time = ts, Body = Redact(body), Kind = "fail" });
                }
            }

            picks.Sort((a, b) => a.Time.CompareTo(b.Time));
            var dedupedPicks = new List<ParsedLine>();
            foreach (var p in picks)
            {
                if (dedupedPicks.Count > 0
                    && Math.Abs((p.Time - dedupedPicks[dedupedPicks.Count - 1].Time).TotalSeconds) <= 2)
                    continue;
                dedupedPicks.Add(p);
            }

            var events = new List<KeyActivityEvent>();
            var usedLocal = new HashSet<int>();
            var usedBmw = new HashSet<int>();
            var usedFail = new HashSet<int>();

            for (var i = dedupedPicks.Count - 1; i >= 0 && events.Count < MaxEvents; i--)
            {
                var pick = dedupedPicks[i];
                var ev = new KeyActivityEvent();
                ev.DetectedLocal = pick.Time;
                ev.ReaderId = FindNearestReaderId(readerIds, pick.Time)
                    ?? (reader != null ? reader.KeyReaderId : null)
                    ?? "";
                // Do not surface raw "Picked up binary file / Info message" log lines.
                ev.Detail = "";

                // Wide window: Local/BMW logs often arrive well after the pick line.
                var li = FindNearestIndex(locals, pick.Time, 180, usedLocal);
                if (li >= 0)
                {
                    usedLocal.Add(li);
                    ev.LocalSent = true;
                    if (!string.IsNullOrEmpty(locals[li].Guid))
                        ev.FileId = locals[li].Guid;
                    ev.Detail = "Sent to KAI Dezentral";
                }

                var bi = FindNearestIndex(bmws, pick.Time, 180, usedBmw);
                if (bi >= 0)
                {
                    usedBmw.Add(bi);
                    ev.BmwUploaded = true;
                    ev.Detail = "Uploaded to KAI Central";
                }

                var fi = FindNearestIndex(fails, pick.Time, 180, usedFail);
                if (fi >= 0 && !ev.BmwUploaded)
                {
                    usedFail.Add(fi);
                    ev.Failed = true;
                    ev.Detail = FormatUploadFailDetail(fails[fi].Body);
                }

                ev.Status = ComputeStatus(ev);
                events.Add(ev);
            }

            for (var i = locals.Count - 1; i >= 0 && events.Count < MaxEvents; i--)
            {
                if (usedLocal.Contains(i)) continue;
                var loc = locals[i];
                var ev = new KeyActivityEvent();
                ev.DetectedLocal = loc.Time;
                ev.FileId = loc.Guid;
                ev.LocalSent = true;
                ev.ReaderId = FindNearestReaderId(readerIds, loc.Time)
                    ?? (reader != null ? reader.KeyReaderId : null)
                    ?? "";
                ev.Detail = "Sent to KAI Dezentral";
                var bi = FindNearestIndex(bmws, loc.Time, 180, usedBmw);
                if (bi >= 0)
                {
                    usedBmw.Add(bi);
                    ev.BmwUploaded = true;
                    ev.Detail = "Uploaded to KAI Central";
                }
                var fi = FindNearestIndex(fails, loc.Time, 180, usedFail);
                if (fi >= 0 && !ev.BmwUploaded)
                {
                    usedFail.Add(fi);
                    ev.Failed = true;
                    ev.Detail = FormatUploadFailDetail(fails[fi].Body);
                }
                ev.Status = ComputeStatus(ev);
                events.Add(ev);
                usedLocal.Add(i);
            }

            events.Sort((a, b) => b.DetectedLocal.CompareTo(a.DetectedLocal));
            if (events.Count > MaxEvents)
                events = events.GetRange(0, MaxEvents);
            return events;
        }

        private static string FindNearestReaderId(List<ParsedLine> readerIds, DateTime around)
        {
            string best = null;
            var bestDelta = double.MaxValue;
            foreach (var r in readerIds)
            {
                var d = Math.Abs((r.Time - around).TotalSeconds);
                if (d <= 60 && d < bestDelta)
                {
                    bestDelta = d;
                    best = r.Guid;
                }
            }
            return best;
        }

        private static int FindNearestIndex(
            List<ParsedLine> list,
            DateTime around,
            double maxSeconds,
            HashSet<int> used)
        {
            var best = -1;
            var bestDelta = double.MaxValue;
            for (var i = 0; i < list.Count; i++)
            {
                if (used != null && used.Contains(i)) continue;
                // Allow log lines slightly before the pick, and up to maxSeconds after.
                var d = (list[i].Time - around).TotalSeconds;
                if (d < -60) continue;
                if (d > maxSeconds) continue;
                var ad = Math.Abs(d);
                if (ad < bestDelta)
                {
                    bestDelta = ad;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>
        /// Waiting pick rows often miss Local/BMW because those log lines were already
        /// matched to an earlier row for the same VIN — copy status from that sibling.
        /// </summary>
        private static void InheritUploadStatusByVin(List<KeyActivityEvent> events)
        {
            if (events == null) return;
            for (var i = 0; i < events.Count; i++)
            {
                var ev = events[i];
                if (ev == null || string.IsNullOrEmpty(ev.Vin)) continue;
                if (ev.LocalSent || ev.BmwUploaded || ev.Failed) continue;

                KeyActivityEvent donor = null;
                var bestDelta = double.MaxValue;
                for (var j = 0; j < events.Count; j++)
                {
                    if (i == j) continue;
                    var other = events[j];
                    if (other == null || string.IsNullOrEmpty(other.Vin)) continue;
                    if (!string.Equals(ev.Vin, other.Vin, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!other.LocalSent && !other.BmwUploaded && !other.Failed) continue;
                    var d = Math.Abs((other.DetectedLocal - ev.DetectedLocal).TotalMinutes);
                    if (d > 15) continue;
                    if (d < bestDelta)
                    {
                        bestDelta = d;
                        donor = other;
                    }
                }
                if (donor == null) continue;
                ev.LocalSent = donor.LocalSent;
                ev.BmwUploaded = donor.BmwUploaded;
                ev.Failed = donor.Failed;
                if (string.IsNullOrEmpty(ev.FileId)) ev.FileId = donor.FileId;
                if (string.IsNullOrEmpty(ev.Detail)
                    || KeyActivityEvent.IsPickupNoiseDetail(ev.Detail))
                    ev.Detail = donor.Detail;
                ev.Status = ComputeStatus(ev);
            }
        }

        /// <summary>Keep one row per VIN within a 3-minute window (prefer richest status).</summary>
        private static List<KeyActivityEvent> DeduplicateVinRows(List<KeyActivityEvent> events)
        {
            if (events == null) return new List<KeyActivityEvent>();
            var result = new List<KeyActivityEvent>();
            foreach (var ev in events)
            {
                if (ev == null) continue;
                if (string.IsNullOrEmpty(ev.Vin))
                {
                    result.Add(ev);
                    continue;
                }
                var replaced = false;
                for (var i = 0; i < result.Count; i++)
                {
                    var existing = result[i];
                    if (existing == null || string.IsNullOrEmpty(existing.Vin)) continue;
                    if (!string.Equals(existing.Vin, ev.Vin, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (Math.Abs((existing.DetectedLocal - ev.DetectedLocal).TotalMinutes) > 3)
                        continue;
                    result[i] = PreferRicherEvent(existing, ev);
                    replaced = true;
                    break;
                }
                if (!replaced)
                    result.Add(ev);
            }
            result.Sort((a, b) => b.DetectedLocal.CompareTo(a.DetectedLocal));
            return result;
        }

        private static KeyActivityEvent PreferRicherEvent(KeyActivityEvent a, KeyActivityEvent b)
        {
            var scoreA = EventRichness(a);
            var scoreB = EventRichness(b);
            if (scoreB > scoreA) return b;
            if (scoreA > scoreB) return a;
            return a.DetectedLocal >= b.DetectedLocal ? a : b;
        }

        private static int EventRichness(KeyActivityEvent ev)
        {
            if (ev == null) return 0;
            var s = 0;
            if (ev.BmwUploaded) s += 4;
            if (ev.LocalSent) s += 2;
            if (ev.Failed) s += 1;
            if (!string.IsNullOrEmpty(ev.Vin)) s += 1;
            return s;
        }

        private static bool LooksLikeUploaderFail(string body)
        {
            if (string.IsNullOrEmpty(body)) return false;
            if (ContainsIgnoreCase(body, "MAINTENANCE")) return false;
            if (ContainsIgnoreCase(body, "LoadStringFromEncryptedFile")) return false;
            return ContainsIgnoreCase(body, "failed to send")
                || ContainsIgnoreCase(body, "could not send")
                || ContainsIgnoreCase(body, "upload failed")
                || (ContainsIgnoreCase(body, "ERROR") && ContainsIgnoreCase(body, "key"));
        }

        private static bool LooksLikeKaiUploadFail(string body)
        {
            if (string.IsNullOrEmpty(body)) return false;
            if (ContainsIgnoreCase(body, "MAINTENANCE")) return false;
            if (ContainsIgnoreCase(body, "LoadStringFromEncryptedFile")) return false;
            if (ContainsIgnoreCase(body, "could not upload"))
                return true;
            if (ContainsIgnoreCase(body, "failed to upload")
                || ContainsIgnoreCase(body, "upload key data failed"))
                return true;
            if (ContainsIgnoreCase(body, "403")
                && (ContainsIgnoreCase(body, "Zentral")
                    || ContainsIgnoreCase(body, "Forbidden")
                    || ContainsIgnoreCase(body, "PutKey")
                    || ContainsIgnoreCase(body, "UploadKey")))
                return true;
            return ContainsIgnoreCase(body, "ERROR")
                && (ContainsIgnoreCase(body, "PutKeyData")
                    || ContainsIgnoreCase(body, "StoreKey")
                    || ContainsIgnoreCase(body, "KAI Central")
                    || ContainsIgnoreCase(body, "KAI Zentral"));
        }

        private static string FormatUploadFailDetail(string body)
        {
            if (string.IsNullOrEmpty(body)) return "BMW Server upload failed";
            if (ContainsIgnoreCase(body, "403") || ContainsIgnoreCase(body, "Forbidden"))
                return "BMW Server 403 Forbidden";
            return TrimDetail(body);
        }

        private static bool IsNoise(string body)
        {
            if (string.IsNullOrEmpty(body)) return true;
            if (ContainsIgnoreCase(body, "LoadStringFromEncryptedFile")) return true;
            if (ContainsIgnoreCase(body, "MAINTENANCE : Info")) return true;
            return false;
        }

        private static string Redact(string body)
        {
            if (string.IsNullOrEmpty(body)) return body;
            return RxBase64Chunk.Replace(body, "[redacted]");
        }

        private static string TrimDetail(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = Regex.Replace(s, @"\s+", " ").Trim();
            var idx = s.IndexOf("KAIS:", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0 && idx + 5 < s.Length)
                s = s.Substring(idx + 5).Trim();
            if (s.Length > 160) s = s.Substring(0, 157) + "...";
            return s;
        }

        private static string ExtractGuid(string body)
        {
            var m = RxGuid.Match(body ?? "");
            return m.Success ? m.Groups[1].Value : null;
        }

        private static string ExtractReaderId(string body)
        {
            var m = RxReaderId.Match(body ?? "");
            return m.Success ? m.Groups[1].Value : null;
        }

        private static bool ContainsIgnoreCase(string hay, string needle)
        {
            return hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryParseLogLine(string line, out DateTime ts, out string body)
        {
            ts = DateTime.MinValue;
            body = null;
            if (string.IsNullOrEmpty(line)) return false;
            var m = RxTs.Match(line);
            if (!m.Success) return false;
            var raw = m.Groups["ts"].Value.Replace(',', '.');
            DateTime parsed;
            if (!DateTime.TryParseExact(
                raw,
                "yyyy-MM-dd HH:mm:ss.fff",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out parsed))
                return false;
            ts = parsed;
            body = line.Substring(m.Length).Trim();
            return true;
        }

        private static string NewestLogFile(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return null;
                string best = null;
                var bestTime = DateTime.MinValue;
                foreach (var f in Directory.GetFiles(dir, "*.log"))
                {
                    DateTime t;
                    try { t = File.GetLastWriteTimeUtc(f); }
                    catch { continue; }
                    if (t > bestTime)
                    {
                        bestTime = t;
                        best = f;
                    }
                }
                return best;
            }
            catch
            {
                return null;
            }
        }

        private static IEnumerable<string> ReadLogTailLines(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                yield break;

            string text = null;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    var len = fs.Length;
                    var start = len > LogTailBytes ? len - LogTailBytes : 0;
                    fs.Position = start;
                    using (var sr = new StreamReader(fs, Encoding.UTF8, true))
                    {
                        if (start > 0)
                            sr.ReadLine();
                        text = sr.ReadToEnd();
                    }
                }
            }
            catch
            {
                yield break;
            }

            if (string.IsNullOrEmpty(text))
                yield break;

            using (var reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                    yield return line;
            }
        }

        private sealed class ParsedLine
        {
            public DateTime Time;
            public string Body;
            public string Kind;
            public string Guid;
        }

        private sealed class BinHint
        {
            public DateTime Time;
            public string Vin;
            public string Paint;
            public string Upholstery;
            public string ILevelCurrent;
            public string ILevelBackup;
            public string ILevelPlant;
            public int? MileageKm;

            public static BinHint From(DateTime when, KeyBinFields f)
            {
                return new BinHint
                {
                    Time = when,
                    Vin = f.Vin,
                    Paint = f.Paint,
                    Upholstery = f.Upholstery,
                    ILevelCurrent = f.ILevelCurrent,
                    ILevelBackup = f.ILevelBackup,
                    ILevelPlant = f.ILevelPlant,
                    MileageKm = f.MileageKm
                };
            }
        }

        internal sealed class KeyBinFields
        {
            public string Vin;
            public string Paint;
            public string Upholstery;
            public string ILevelCurrent;
            public string ILevelBackup;
            public string ILevelPlant;
            public int? MileageKm;
        }
    }
}
