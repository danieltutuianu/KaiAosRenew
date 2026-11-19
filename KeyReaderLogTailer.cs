using System;
using System.IO;
using System.Text;
using System.Threading;

namespace KaiAosRenew
{
    internal enum KeyReaderActivityKind
    {
        Info,
        Success,
        Warning,
        Error,
        Verbose
    }

    /// <summary>
    /// Tails Desktop KeyReader Plus log folder (and keyreader.bin) into Activity via callback.
    /// Seeds at EOF — only live lines after Start.
    /// </summary>
    internal sealed class KeyReaderLogTailer : IDisposable
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false);
        private readonly object _gate = new object();
        private Action<string, KeyReaderActivityKind> _onLine;
        private string _logDir;
        private string _dataBinPath;
        private string _linePrefix = "KeyReader: ";
        private bool _watchBin;
        private FileSystemWatcher _logWatcher;
        private FileSystemWatcher _binWatcher;
        private Timer _debounce;
        private Timer _poll;
        private string _currentFile;
        private long _offset;
        private string _partial = "";
        /// <summary>Max carried-over bytes of an unterminated line (guards against files without newlines).</summary>
        private const int MaxPartialChars = 1024 * 1024;
        private const string PartialTruncatedMarker = "...[truncated]";
        // Persistent decoder: multi-byte UTF-8 sequences split across read chunks are preserved.
        private Decoder _decoder = Utf8.GetDecoder();
        private DateTime _lastBinWriteUtc = DateTime.MinValue;
        private bool _missingFolderNotified;
        private bool _disposed;

        public void Start(string logDir, string dataBinPath, Action<string, KeyReaderActivityKind> onLine)
        {
            Start(logDir, dataBinPath, onLine, "KeyReader: ", true);
        }

        /// <summary>
        /// Tail a log folder into Activity. When <paramref name="watchBin"/> is false, only *.log is watched
        /// (used for KeyUploader / KAI service logs).
        /// </summary>
        public void Start(
            string logDir,
            string dataBinPath,
            Action<string, KeyReaderActivityKind> onLine,
            string linePrefix,
            bool watchBin)
        {
            lock (_gate)
            {
                StopUnlocked();
                _disposed = false;
                _onLine = onLine;
                _logDir = string.IsNullOrWhiteSpace(logDir) ? null : logDir.Trim();
                _dataBinPath = string.IsNullOrWhiteSpace(dataBinPath) ? null : dataBinPath.Trim();
                _linePrefix = string.IsNullOrEmpty(linePrefix) ? "KeyReader: " : linePrefix;
                _watchBin = watchBin;
                _currentFile = null;
                _offset = 0;
                ResetDecodeStateUnlocked();
                _missingFolderNotified = false;
                _lastBinWriteUtc = DateTime.MinValue;

                SeedLogAtEofUnlocked();
                if (_watchBin)
                    SeedBinStampUnlocked();
                StartWatchersUnlocked();
                _debounce = new Timer(_ => PollUnlocked(false), null, Timeout.Infinite, Timeout.Infinite);
                _poll = new Timer(_ => PollUnlocked(false), null, 2000, 2000);

                if (string.IsNullOrEmpty(_logDir) || !Directory.Exists(_logDir))
                {
                    if (!_missingFolderNotified && _watchBin)
                    {
                        _missingFolderNotified = true;
                        EmitUnlocked(
                            string.Format(UiStrings.T("KeyReaderLogMissing"), _logDir ?? "(empty)"),
                            KeyReaderActivityKind.Warning);
                    }
                }
            }
        }

        /// <summary>Force an immediate drain of new log bytes (Refresh button).</summary>
        public void PollNow()
        {
            PollUnlocked(true);
        }

        public void Stop()
        {
            lock (_gate)
            {
                StopUnlocked();
            }
        }

        public void Dispose()
        {
            Stop();
        }

        private void StopUnlocked()
        {
            _disposed = true;
            _onLine = null;
            try { if (_debounce != null) _debounce.Dispose(); }
            catch { }
            _debounce = null;
            try { if (_poll != null) _poll.Dispose(); }
            catch { }
            _poll = null;
            DisposeWatcher(ref _logWatcher);
            DisposeWatcher(ref _binWatcher);
        }

        private static void DisposeWatcher(ref FileSystemWatcher w)
        {
            if (w == null) return;
            try { w.EnableRaisingEvents = false; }
            catch { }
            try { w.Dispose(); }
            catch { }
            w = null;
        }

        private void StartWatchersUnlocked()
        {
            DisposeWatcher(ref _logWatcher);
            DisposeWatcher(ref _binWatcher);

            if (!string.IsNullOrEmpty(_logDir) && Directory.Exists(_logDir))
            {
                try
                {
                    _logWatcher = new FileSystemWatcher(_logDir);
                    _logWatcher.Filter = "*.log";
                    _logWatcher.NotifyFilter = NotifyFilters.FileName
                        | NotifyFilters.LastWrite
                        | NotifyFilters.Size
                        | NotifyFilters.CreationTime;
                    _logWatcher.Created += OnFsEvent;
                    _logWatcher.Changed += OnFsEvent;
                    _logWatcher.Renamed += OnFsEvent;
                    _logWatcher.EnableRaisingEvents = true;
                }
                catch
                {
                    DisposeWatcher(ref _logWatcher);
                }
            }

            if (!_watchBin || string.IsNullOrEmpty(_dataBinPath)) return;
            var binDir = Path.GetDirectoryName(_dataBinPath);
            if (string.IsNullOrEmpty(binDir) || !Directory.Exists(binDir)) return;
            try
            {
                _binWatcher = new FileSystemWatcher(binDir);
                _binWatcher.Filter = Path.GetFileName(_dataBinPath);
                if (string.IsNullOrEmpty(_binWatcher.Filter))
                    _binWatcher.Filter = "keyreader.bin";
                _binWatcher.NotifyFilter = NotifyFilters.FileName
                    | NotifyFilters.LastWrite
                    | NotifyFilters.Size
                    | NotifyFilters.CreationTime;
                _binWatcher.Created += OnBinFsEvent;
                _binWatcher.Changed += OnBinFsEvent;
                _binWatcher.Renamed += OnBinFsEvent;
                _binWatcher.EnableRaisingEvents = true;
            }
            catch
            {
                DisposeWatcher(ref _binWatcher);
            }
        }

        private void OnFsEvent(object sender, FileSystemEventArgs e)
        {
            try
            {
                if (_debounce != null)
                    _debounce.Change(250, Timeout.Infinite);
            }
            catch { }
        }

        private void OnBinFsEvent(object sender, FileSystemEventArgs e)
        {
            lock (_gate)
            {
                if (_disposed) return;
                CheckBinUpdatedUnlocked();
            }
        }

        private void PollUnlocked(bool force)
        {
            lock (_gate)
            {
                if (_disposed) return;
                ReadNewLogLinesUnlocked();
                if (_watchBin)
                    CheckBinUpdatedUnlocked();
            }
        }

        private void SeedLogAtEofUnlocked()
        {
            var newest = NewestLogFile(_logDir);
            if (string.IsNullOrEmpty(newest) || !File.Exists(newest))
            {
                _currentFile = null;
                _offset = 0;
                return;
            }
            try
            {
                using (var fs = OpenShare(newest))
                {
                    _currentFile = newest;
                    _offset = fs.Length;
                    ResetDecodeStateUnlocked();
                }
            }
            catch
            {
                _currentFile = newest;
                _offset = 0;
                ResetDecodeStateUnlocked();
            }
        }

        private void ResetDecodeStateUnlocked()
        {
            _partial = "";
            try { _decoder.Reset(); }
            catch { _decoder = Utf8.GetDecoder(); }
        }

        private void SeedBinStampUnlocked()
        {
            if (string.IsNullOrEmpty(_dataBinPath) || !File.Exists(_dataBinPath))
            {
                _lastBinWriteUtc = DateTime.MinValue;
                return;
            }
            try { _lastBinWriteUtc = File.GetLastWriteTimeUtc(_dataBinPath); }
            catch { _lastBinWriteUtc = DateTime.MinValue; }
        }

        private void ReadNewLogLinesUnlocked()
        {
            if (string.IsNullOrEmpty(_logDir))
                return;

            if (!Directory.Exists(_logDir))
            {
                if (!_missingFolderNotified)
                {
                    _missingFolderNotified = true;
                    EmitUnlocked(
                        string.Format(UiStrings.T("KeyReaderLogMissing"), _logDir),
                        KeyReaderActivityKind.Warning);
                }
                return;
            }
            _missingFolderNotified = false;

            var newest = NewestLogFile(_logDir);
            if (string.IsNullOrEmpty(newest))
                return;

            if (!string.Equals(_currentFile, newest, StringComparison.OrdinalIgnoreCase))
            {
                // Log rolled — drain remaining of old file, then read the new file from the start
                // so lines written between create and this poll are not skipped.
                DrainFileUnlocked(_currentFile);
                _currentFile = newest;
                _offset = 0;
                ResetDecodeStateUnlocked();
            }

            DrainFileUnlocked(_currentFile);
        }

        private void DrainFileUnlocked(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return;

            string chunk = null;
            try
            {
                using (var fs = OpenShare(path))
                {
                    if (fs.Length < _offset)
                    {
                        _offset = 0; // truncated
                        ResetDecodeStateUnlocked();
                    }
                    if (fs.Length == _offset)
                        return;
                    fs.Position = _offset;
                    var buf = new byte[checked((int)Math.Min(fs.Length - _offset, 512 * 1024))];
                    var n = fs.Read(buf, 0, buf.Length);
                    if (n <= 0) return;
                    _offset = fs.Position;
                    // Stateful decode: a multi-byte char split at the chunk boundary is completed on next read.
                    var chars = new char[_decoder.GetCharCount(buf, 0, n, false)];
                    var charCount = _decoder.GetChars(buf, 0, n, chars, 0, false);
                    chunk = new string(chars, 0, charCount);
                }
            }
            catch
            {
                return;
            }

            if (string.IsNullOrEmpty(chunk))
                return;

            var text = _partial + chunk;
            _partial = "";
            var start = 0;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c != '\n' && c != '\r') continue;
                var line = text.Substring(start, i - start).TrimEnd('\r');
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                start = i + 1;
                EmitLineUnlocked(line);
            }
            if (start < text.Length)
            {
                var rest = text.Substring(start);
                if (rest.Length > MaxPartialChars)
                {
                    // Runaway line without newline: emit what we have (marked) and drop the rest.
                    EmitLineUnlocked(rest.Substring(0, 4096) + PartialTruncatedMarker);
                    rest = "";
                }
                _partial = rest;
            }
        }

        private void CheckBinUpdatedUnlocked()
        {
            if (string.IsNullOrEmpty(_dataBinPath) || !File.Exists(_dataBinPath))
                return;
            DateTime writeUtc;
            try { writeUtc = File.GetLastWriteTimeUtc(_dataBinPath); }
            catch { return; }
            if (writeUtc <= _lastBinWriteUtc)
                return;
            _lastBinWriteUtc = writeUtc;
            EmitUnlocked(UiStrings.T("KeyReaderBinUpdated"), KeyReaderActivityKind.Success);
        }

        private void EmitUnlocked(string text, KeyReaderActivityKind kind)
        {
            var cb = _onLine;
            if (cb == null) return;
            try { cb(text, kind); }
            catch { }
        }

        internal static KeyReaderActivityKind Classify(string body)
        {
            if (string.IsNullOrEmpty(body))
                return KeyReaderActivityKind.Info;
            if (ContainsAny(body, "MAINTENANCE : Info", "LoadStringFromEncryptedFile"))
                return KeyReaderActivityKind.Verbose;
            if (ContainsAny(body, "enc file does not exist"))
                return KeyReaderActivityKind.Warning;
            if (ContainsAny(body, "403", "Forbidden", "could not upload", "failed to upload",
                    "upload key data failed", "failed to send", "could not send", "upload failed"))
                return KeyReaderActivityKind.Error;
            if (ContainsAny(body, "error", "fail", "exception", "denied", "fatal",
                    "fehler", "fehlgeschlagen", "eșuat"))
                return KeyReaderActivityKind.Error;
            if (ContainsAny(body, "cannot", "unable", "warn", "warning", "warnung", "avertisment"))
                return KeyReaderActivityKind.Warning;
            if (ContainsAny(body, "Successfully uploaded key data to KAI Central",
                    "successfully sent to KAI Dezentral", "success", "written", "keyreader.bin",
                    "read key", "key read", "erfolgreich", "geschrieben", "scris cu succes",
                    "date cheie disponibile"))
                return KeyReaderActivityKind.Success;
            if (ContainsAny(body, "Picked up binary file", "New KeyReader folder", "Upload Url:",
                    "keyreaderids/", "PutKeyDataV1", "Enter method"))
                return KeyReaderActivityKind.Info;
            if (ContainsAny(body, "debug", "verbose", "trace"))
                return KeyReaderActivityKind.Verbose;
            return KeyReaderActivityKind.Info;
        }

        private void EmitLineUnlocked(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;
            // Skip known BMW service noise before it hits Activity.
            if (ContainsAny(line, "MAINTENANCE : Info", "LoadStringFromEncryptedFile"))
                return;
            // Redact long base64 payloads from KeyUploader / KAI dumps.
            var cleaned = RedactBase64(line.Trim());
            var body = KeyReaderLogLocalize.LocalizeBody(cleaned);
            var kind = Classify(body);
            EmitUnlocked(_linePrefix + body, kind);
        }

        private static string RedactBase64(string body)
        {
            if (string.IsNullOrEmpty(body)) return body;
            try
            {
                return System.Text.RegularExpressions.Regex.Replace(
                    body, @"[A-Za-z0-9+/]{80,}={0,2}", "[redacted]");
            }
            catch
            {
                return body;
            }
        }

        private static bool ContainsAny(string hay, params string[] needles)
        {
            foreach (var n in needles)
            {
                if (hay.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static FileStream OpenShare(string path)
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }

        private static string NewestLogFile(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    return null;
                string best = null;
                var bestTime = DateTime.MinValue;
                foreach (var f in Directory.GetFiles(dir, "*.log"))
                {
                    DateTime t;
                    try { t = File.GetLastWriteTimeUtc(f); }
                    catch { continue; }
                    if (t >= bestTime)
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
    }
}
