using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace KaiAosRenew
{
    /// <summary>TCP notify hub: JSON line per message to connected remote clients.</summary>
    internal sealed class NotifyServer : IDisposable
    {
        public const int DefaultPort = 17890;
        private static NotifyServer _instance;
        private readonly object _gate = new object();
        private TcpListener _listener;
        private Thread _acceptThread;
        private volatile bool _running;
        private readonly List<ClientConn> _clients = new List<ClientConn>();
        private int _port = DefaultPort;
        private Timer _pingTimer;
        private const int PingIntervalMs = 30000;
        private int _nextClientId = 1;

        public static NotifyServer Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new NotifyServer();
                return _instance;
            }
        }

        public bool IsRunning { get { return _running; } }
        public int Port { get { return _port; } }
        /// <summary>Raised on the accept thread after a client is added (arg = client id).</summary>
        public event Action<int> ClientConnected;
        /// <summary>Raised when a client sends a JSON line (args = client id, message).</summary>
        public event Action<int, NotifyMessage> MessageFromClient;
        public int ClientCount
        {
            get
            {
                lock (_gate) return _clients.Count;
            }
        }

        public void ApplySettings(bool enabled, int port)
        {
            if (port < 1024) port = DefaultPort;
            if (port > 65535) port = DefaultPort;
            if (!enabled)
            {
                Stop();
                return;
            }
            if (_running && _port == port)
                return;
            Stop();
            Start(port);
        }

        public void Start(int port)
        {
            lock (_gate)
            {
                if (_running) return;
                _port = port;
                try
                {
                    _listener = new TcpListener(IPAddress.Any, port);
                    _listener.Start();
                    _running = true;
                    _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "NotifyServerAccept" };
                    _acceptThread.Start();
                    StartPingTimer();
                    RenewEngine.Log("Notify server listening on 0.0.0.0:" + port);
                }
                catch (Exception ex)
                {
                    _running = false;
                    _listener = null;
                    RenewEngine.Log("Notify server start failed: " + ex.Message);
                }
            }
        }

        public void Stop()
        {
            lock (_gate)
            {
                _running = false;
                StopPingTimer();
                try
                {
                    if (_listener != null)
                        _listener.Stop();
                }
                catch { }
                _listener = null;
                foreach (var c in _clients)
                {
                    try { c.Dispose(); }
                    catch { }
                }
                _clients.Clear();
            }
        }

        private void StartPingTimer()
        {
            StopPingTimer();
            _pingTimer = new Timer(PingTick, null, PingIntervalMs, PingIntervalMs);
        }

        private void StopPingTimer()
        {
            if (_pingTimer != null)
            {
                try { _pingTimer.Dispose(); }
                catch { }
                _pingTimer = null;
            }
        }

        private void PingTick(object state)
        {
            if (!_running) return;
            Broadcast(new NotifyMessage
            {
                Kind = "ping",
                Level = "info",
                Title = "KAI AOS Renew",
                Text = "ping"
            });
        }

        public void Dispose()
        {
            Stop();
        }

        public void Broadcast(NotifyMessage msg)
        {
            if (msg == null || !_running) return;
            var bytes = Encode(msg);
            List<ClientConn> snapshot;
            lock (_gate)
                snapshot = new List<ClientConn>(_clients);
            foreach (var c in snapshot)
                TrySendOrDrop(c, bytes);
        }

        public bool SendTo(int clientId, NotifyMessage msg)
        {
            if (msg == null || !_running) return false;
            var bytes = Encode(msg);
            ClientConn target = null;
            lock (_gate)
            {
                foreach (var c in _clients)
                {
                    if (c.Id == clientId)
                    {
                        target = c;
                        break;
                    }
                }
            }
            if (target == null) return false;
            return TrySendOrDrop(target, bytes);
        }

        public List<NotifyClientInfo> GetClients()
        {
            var list = new List<NotifyClientInfo>();
            lock (_gate)
            {
                foreach (var c in _clients)
                    list.Add(new NotifyClientInfo { Id = c.Id, Display = c.RemoteDisplay });
            }
            return list;
        }

        private static byte[] Encode(NotifyMessage msg)
        {
            return Encoding.UTF8.GetBytes(msg.ToJsonLine() + "\n");
        }

        private bool TrySendOrDrop(ClientConn c, byte[] bytes)
        {
            try
            {
                if (!c.TrySend(bytes))
                {
                    RemoveClient(c);
                    return false;
                }
                return true;
            }
            catch
            {
                RemoveClient(c);
                return false;
            }
        }

        public static string GetLocalIpv4Summary()
        {
            var list = new List<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var props = ni.GetIPProperties();
                    foreach (var ua in props.UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        var ip = ua.Address.ToString();
                        if (!string.IsNullOrEmpty(ip) && list.IndexOf(ip) < 0)
                            list.Add(ip);
                    }
                }
            }
            catch { }
            if (list.Count == 0)
                return "127.0.0.1";
            return string.Join(", ", list.ToArray());
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                try
                {
                    var listener = _listener;
                    if (listener == null) break;
                    var tcp = listener.AcceptTcpClient();
                    tcp.NoDelay = true;
                    int id;
                    ClientConn conn;
                    lock (_gate)
                    {
                        id = _nextClientId++;
                        conn = new ClientConn(id, tcp, OnClientMessage, RemoveClient);
                        _clients.Add(conn);
                    }
                    conn.StartRead();
                    RenewEngine.Log("Notify client connected #" + id + " (" + ClientCount + ")");
                    var handler = ClientConnected;
                    if (handler != null)
                    {
                        try { handler(id); }
                        catch (Exception ex)
                        {
                            RenewEngine.Log("Notify ClientConnected handler: " + ex.Message);
                        }
                    }
                }
                catch (SocketException)
                {
                    if (!_running) break;
                }
                catch
                {
                    if (!_running) break;
                    Thread.Sleep(200);
                }
            }
        }

        private void OnClientMessage(int clientId, NotifyMessage msg)
        {
            var handler = MessageFromClient;
            if (handler == null || msg == null) return;
            try { handler(clientId, msg); }
            catch (Exception ex)
            {
                try { RenewEngine.Log("Notify MessageFromClient: " + ex.Message); }
                catch { }
            }
        }

        private void RemoveClient(ClientConn c)
        {
            if (c == null) return;
            var removed = false;
            lock (_gate)
            {
                removed = _clients.Remove(c);
            }
            if (!removed) return;
            try { c.Dispose(); }
            catch { }
            try { RenewEngine.Log("Notify client disconnected #" + c.Id + " (" + ClientCount + ")"); }
            catch { }
        }

        private sealed class ClientConn : IDisposable
        {
            private readonly TcpClient _tcp;
            private readonly NetworkStream _stream;
            private readonly object _writeGate = new object();
            private readonly Action<int, NotifyMessage> _onMessage;
            private readonly Action<ClientConn> _onClosed;
            private Thread _readThread;
            private volatile bool _disposed;

            public int Id { get; private set; }
            public string RemoteDisplay { get; private set; }

            public ClientConn(int id, TcpClient tcp, Action<int, NotifyMessage> onMessage, Action<ClientConn> onClosed)
            {
                Id = id;
                _tcp = tcp;
                _stream = tcp.GetStream();
                _onMessage = onMessage;
                _onClosed = onClosed;
                try
                {
                    var ep = tcp.Client != null ? tcp.Client.RemoteEndPoint : null;
                    RemoteDisplay = ep != null ? ep.ToString() : ("#" + id);
                }
                catch
                {
                    RemoteDisplay = "#" + id;
                }
            }

            public void StartRead()
            {
                if (_readThread != null) return;
                _readThread = new Thread(ReadLoop)
                {
                    IsBackground = true,
                    Name = "NotifyClientRead#" + Id
                };
                _readThread.Start();
            }

            private void ReadLoop()
            {
                try
                {
                    var buf = new byte[4096];
                    var sb = new StringBuilder();
                    while (!_disposed)
                    {
                        var n = _stream.Read(buf, 0, buf.Length);
                        if (n <= 0) break;
                        sb.Append(Encoding.UTF8.GetString(buf, 0, n));
                        var chunk = sb.ToString();
                        int nl;
                        while ((nl = chunk.IndexOf('\n')) >= 0)
                        {
                            var line = chunk.Substring(0, nl).Trim('\r', '\n', ' ');
                            chunk = chunk.Substring(nl + 1);
                            if (line.Length > 0)
                            {
                                var msg = NotifyMessage.ParseLine(line);
                                if (msg != null && _onMessage != null)
                                    _onMessage(Id, msg);
                            }
                        }
                        sb.Length = 0;
                        sb.Append(chunk);
                    }
                }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
                catch { }
                finally
                {
                    if (_onClosed != null)
                    {
                        try { _onClosed(this); }
                        catch { }
                    }
                }
            }

            public bool TrySend(byte[] bytes)
            {
                lock (_writeGate)
                {
                    if (_disposed || _tcp == null || !_tcp.Connected) return false;
                    _stream.Write(bytes, 0, bytes.Length);
                    _stream.Flush();
                    return true;
                }
            }

            public void Dispose()
            {
                _disposed = true;
                try { _stream.Close(); }
                catch { }
                try { _tcp.Close(); }
                catch { }
            }
        }
    }

    internal sealed class NotifyClientInfo
    {
        public int Id { get; set; }
        public string Display { get; set; }
    }

    internal sealed class NotifyMessage
    {
        public int V = 1;
        public string Kind;      // vin | info | warning | error | keySync | ping
        public string Level;     // info | warning | error
        public string Title;
        public string Text;
        public string Vin;
        public string Action;    // copyVin | null
        public int Seconds;
        // Key row fields (live vin / keySync line payload)
        public string At;        // yyyy-MM-ddTHH:mm:ss local
        public string Paint;
        public string Uph;
        public string ILevel;
        public string Reader;
        public string Detail;
        public string Status;    // Waiting|LocalOk|BmwOk|Failed
        public int Local = -1;   // -1 unset, 0/1
        public int Bmw = -1;
        public int Failed = -1;

        public string ToJsonLine()
        {
            var sb = new StringBuilder(256);
            sb.Append("{\"v\":").Append(V);
            sb.Append(",\"kind\":\"").Append(Esc(Kind)).Append("\"");
            sb.Append(",\"level\":\"").Append(Esc(Level)).Append("\"");
            sb.Append(",\"title\":\"").Append(Esc(Title)).Append("\"");
            sb.Append(",\"text\":\"").Append(Esc(Text)).Append("\"");
            if (!string.IsNullOrEmpty(Vin))
                sb.Append(",\"vin\":\"").Append(Esc(Vin)).Append("\"");
            if (!string.IsNullOrEmpty(Action))
                sb.Append(",\"action\":\"").Append(Esc(Action)).Append("\"");
            if (Seconds > 0)
                sb.Append(",\"seconds\":").Append(Seconds);
            if (!string.IsNullOrEmpty(At))
                sb.Append(",\"at\":\"").Append(Esc(At)).Append("\"");
            if (!string.IsNullOrEmpty(Paint))
                sb.Append(",\"paint\":\"").Append(Esc(Paint)).Append("\"");
            if (!string.IsNullOrEmpty(Uph))
                sb.Append(",\"uph\":\"").Append(Esc(Uph)).Append("\"");
            if (!string.IsNullOrEmpty(ILevel))
                sb.Append(",\"ilevel\":\"").Append(Esc(ILevel)).Append("\"");
            if (!string.IsNullOrEmpty(Reader))
                sb.Append(",\"reader\":\"").Append(Esc(Reader)).Append("\"");
            if (!string.IsNullOrEmpty(Detail))
                sb.Append(",\"detail\":\"").Append(Esc(Detail)).Append("\"");
            if (!string.IsNullOrEmpty(Status))
                sb.Append(",\"status\":\"").Append(Esc(Status)).Append("\"");
            if (Local >= 0)
                sb.Append(",\"local\":").Append(Local);
            if (Bmw >= 0)
                sb.Append(",\"bmw\":").Append(Bmw);
            if (Failed >= 0)
                sb.Append(",\"failed\":").Append(Failed);
            sb.Append("}");
            return sb.ToString();
        }

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        public static NotifyMessage ParseLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            line = line.Trim();
            var m = new NotifyMessage();
            m.Kind = JsonStr(line, "kind") ?? "info";
            m.Level = JsonStr(line, "level") ?? "info";
            m.Title = JsonStr(line, "title") ?? "KAI AOS Renew";
            m.Text = JsonStr(line, "text") ?? "";
            m.Vin = JsonStr(line, "vin");
            m.Action = JsonStr(line, "action");
            m.Seconds = JsonInt(line, "seconds");
            m.At = JsonStr(line, "at");
            m.Paint = JsonStr(line, "paint");
            m.Uph = JsonStr(line, "uph");
            m.ILevel = JsonStr(line, "ilevel");
            m.Reader = JsonStr(line, "reader");
            m.Detail = JsonStr(line, "detail");
            m.Status = JsonStr(line, "status");
            m.Local = JsonIntOpt(line, "local");
            m.Bmw = JsonIntOpt(line, "bmw");
            m.Failed = JsonIntOpt(line, "failed");
            return m;
        }

        private static int JsonIntOpt(string json, string key)
        {
            var i = IndexOfKeyColon(json, key);
            if (i < 0) return -1;
            i++;
            while (i < json.Length && (json[i] == ' ' || json[i] == '\t')) i++;
            var start = i;
            while (i < json.Length && (char.IsDigit(json[i]) || json[i] == '-')) i++;
            if (i == start) return -1;
            int v;
            return int.TryParse(json.Substring(start, i - start), out v) ? v : -1;
        }

        /// <summary>Encode up to N key rows for kind=keySync text (newline + pipe).</summary>
        public static string EncodeKeySync(IList<KeyActivityEvent> rows)
        {
            if (rows == null || rows.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (var ev in rows)
            {
                if (ev == null || string.IsNullOrEmpty(ev.Vin)) continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(ev.DetectedLocal.ToString("yyyy-MM-ddTHH:mm:ss"));
                sb.Append('|').Append(EscPipe(ev.Vin ?? ""));
                sb.Append('|').Append(EscPipe(ev.Paint ?? ""));
                sb.Append('|').Append(EscPipe(ev.Upholstery ?? ""));
                sb.Append('|').Append(EscPipe(ev.ILevelCurrent ?? ""));
                sb.Append('|').Append(EscPipe(ev.ReaderId ?? ""));
                sb.Append('|').Append(ev.LocalSent ? "1" : "0");
                sb.Append('|').Append(ev.BmwUploaded ? "1" : "0");
                sb.Append('|').Append(ev.Failed ? "1" : "0");
                sb.Append('|').Append(EscPipe(StatusName(ev.Status)));
                sb.Append('|').Append(EscPipe(ev.DetailText ?? ""));
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
        }

        private static string EscPipe(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("|", "\\p").Replace("\r", "").Replace("\n", "\\n");
        }

        public static string EncodeActivitySync(IList<ActivityWireRow> rows)
        {
            if (rows == null || rows.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (var row in rows)
            {
                if (row == null) continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(row.At.ToString("yyyy-MM-ddTHH:mm:ss"));
                sb.Append('|').Append(EscPipe(row.Level ?? "info"));
                sb.Append('|').Append(EscPipe(row.Text ?? ""));
            }
            return sb.ToString();
        }

        /// <summary>
        /// kind=kaiSync text. Line 1: install|registerUrl|webEamUrl|uploadUrl|tokenExpiryText|allOk|anyRunning|svcAlert
        /// then up to 2 lines: name|statusText|startTypeText|ok
        /// </summary>
        public static string EncodeKaiSync(
            string install, string registerUrl, string webEamUrl, string uploadUrl, string tokenExpiryText,
            bool allOk, bool anyRunning, bool svcAlert, IList<KaiSvcWireRow> services)
        {
            var sb = new StringBuilder();
            sb.Append(EscPipe(install ?? ""));
            sb.Append('|').Append(EscPipe(registerUrl ?? ""));
            sb.Append('|').Append(EscPipe(webEamUrl ?? ""));
            sb.Append('|').Append(EscPipe(uploadUrl ?? ""));
            sb.Append('|').Append(EscPipe(tokenExpiryText ?? ""));
            sb.Append('|').Append(allOk ? "1" : "0");
            sb.Append('|').Append(anyRunning ? "1" : "0");
            sb.Append('|').Append(svcAlert ? "1" : "0");
            if (services != null)
            {
                var n = 0;
                foreach (var s in services)
                {
                    if (s == null) continue;
                    if (n >= 2) break;
                    sb.Append('\n');
                    sb.Append(EscPipe(s.Name ?? ""));
                    sb.Append('|').Append(EscPipe(s.StatusText ?? ""));
                    sb.Append('|').Append(EscPipe(s.StartTypeText ?? ""));
                    sb.Append('|').Append(s.Ok ? "1" : "0");
                    n++;
                }
            }
            return sb.ToString();
        }

        public static string EncodeAosSync(int intervalHours, string email, string password)
        {
            var sb = new StringBuilder();
            sb.Append(intervalHours < 1 ? 1 : intervalHours);
            sb.Append('|').Append(EscPipe(email ?? ""));
            sb.Append('|').Append(EscPipe(password ?? ""));
            return sb.ToString();
        }

        public static bool TryParseAosSync(string text, out int intervalHours, out string email, out string password)
        {
            intervalHours = 1;
            email = "";
            password = "";
            if (string.IsNullOrWhiteSpace(text)) return false;
            var parts = SplitPipe(text);
            if (parts.Count < 3) return false;
            int h;
            if (!int.TryParse(parts[0], out h) || h < 1)
                h = 1;
            intervalHours = h;
            email = parts[1] ?? "";
            password = parts[2] ?? "";
            return true;
        }

        public static List<ActivityWireRow> ParseActivitySync(string text)
        {
            var list = new List<ActivityWireRow>();
            if (string.IsNullOrWhiteSpace(text)) return list;
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (var raw in lines)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var parts = SplitPipe(raw);
                if (parts.Count < 3) continue;
                DateTime at;
                if (!DateTime.TryParse(parts[0], out at))
                    at = DateTime.Now;
                list.Add(new ActivityWireRow
                {
                    At = DateTime.SpecifyKind(at, DateTimeKind.Local),
                    Level = parts[1] ?? "info",
                    Text = parts[2] ?? ""
                });
            }
            return list;
        }

        private static List<string> SplitPipe(string line)
        {
            var parts = new List<string>();
            if (line == null) return parts;
            var sb = new StringBuilder();
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (c == '\\' && i + 1 < line.Length)
                {
                    var n = line[i + 1];
                    if (n == 'p') { sb.Append('|'); i++; continue; }
                    if (n == 'n') { sb.Append('\n'); i++; continue; }
                    if (n == '\\') { sb.Append('\\'); i++; continue; }
                }
                if (c == '|')
                {
                    parts.Add(sb.ToString());
                    sb.Length = 0;
                    continue;
                }
                sb.Append(c);
            }
            parts.Add(sb.ToString());
            return parts;
        }

        /// <summary>Find "key" : value — must not match a string value equal to key (e.g. kind:"vin").</summary>
        private static int IndexOfKeyColon(string json, string key)
        {
            var needle = "\"" + key + "\"";
            var start = 0;
            while (start < json.Length)
            {
                var i = json.IndexOf(needle, start, StringComparison.OrdinalIgnoreCase);
                if (i < 0) return -1;
                var j = i + needle.Length;
                while (j < json.Length && (json[j] == ' ' || json[j] == '\t')) j++;
                if (j < json.Length && json[j] == ':')
                    return j;
                start = i + needle.Length;
            }
            return -1;
        }

        private static string JsonStr(string json, string key)
        {
            var i = IndexOfKeyColon(json, key);
            if (i < 0) return null;
            i = json.IndexOf('"', i + 1);
            if (i < 0) return null;
            var j = i + 1;
            var sb = new StringBuilder();
            while (j < json.Length)
            {
                var c = json[j];
                if (c == '\\' && j + 1 < json.Length)
                {
                    var n = json[j + 1];
                    if (n == 'n') sb.Append('\n');
                    else if (n == 'r') sb.Append('\r');
                    else if (n == 't') sb.Append('\t');
                    else sb.Append(n);
                    j += 2;
                    continue;
                }
                if (c == '"') break;
                sb.Append(c);
                j++;
            }
            return sb.ToString();
        }

        private static int JsonInt(string json, string key)
        {
            var i = IndexOfKeyColon(json, key);
            if (i < 0) return 0;
            i++;
            while (i < json.Length && (json[i] == ' ' || json[i] == '\t')) i++;
            var start = i;
            while (i < json.Length && char.IsDigit(json[i])) i++;
            if (i == start) return 0;
            int v;
            return int.TryParse(json.Substring(start, i - start), out v) ? v : 0;
        }
    }

    internal sealed class KaiSvcWireRow
    {
        public string Name { get; set; }
        public string StatusText { get; set; }
        public string StartTypeText { get; set; }
        public bool Ok { get; set; }
    }

    internal sealed class ActivityWireRow
    {
        public DateTime At { get; set; }
        public string Level { get; set; }
        public string Text { get; set; }
    }
}
