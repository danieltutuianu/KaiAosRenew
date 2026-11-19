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
        public const string UdpDiscoverRequest = "KAI_DISCOVER_V1";
        public const string UdpDiscoverResponsePrefix = "KAI_HERE_V1|";
        private static NotifyServer _instance;
        private readonly object _gate = new object();
        private TcpListener _listener;
        private Thread _acceptThread;
        private UdpClient _udp;
        private Thread _udpThread;
        private volatile bool _running;
        private readonly List<ClientConn> _clients = new List<ClientConn>();
        private int _port = DefaultPort;
        private Timer _pingTimer;
        private const int PingIntervalMs = 30000;
        private int _nextClientId = 1;
        private string _pairingCode = "";

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
        /// <summary>Optional LAN pairing code. Empty = no auth (compat with older clients).</summary>
        public string PairingCode
        {
            get { lock (_gate) return _pairingCode ?? ""; }
            set
            {
                lock (_gate)
                    _pairingCode = (value ?? "").Trim();
            }
        }
        /// <summary>Raised on the accept thread after a client is authenticated (arg = client id).</summary>
        public event Action<int> ClientConnected;
        /// <summary>Raised after a client is removed (arg = client id).</summary>
        public event Action<int> ClientDisconnected;
        /// <summary>Raised when a client sends a JSON line (args = client id, message).</summary>
        public event Action<int, NotifyMessage> MessageFromClient;
        public int ClientCount
        {
            get
            {
                lock (_gate)
                {
                    var n = 0;
                    foreach (var c in _clients)
                    {
                        if (c != null && c.Authenticated) n++;
                    }
                    return n;
                }
            }
        }

        public void ApplySettings(bool enabled, int port)
        {
            ApplySettings(enabled, port, null);
        }

        public void ApplySettings(bool enabled, int port, string pairingCode)
        {
            if (port < 1024) port = DefaultPort;
            if (port > 65535) port = DefaultPort;
            var pairingChanged = false;
            if (pairingCode != null)
            {
                var next = (pairingCode ?? "").Trim();
                string prev;
                lock (_gate)
                    prev = _pairingCode ?? "";
                pairingChanged = !string.Equals(prev, next, StringComparison.Ordinal);
                PairingCode = next;
            }
            if (!enabled)
            {
                Stop();
                return;
            }
            if (_running && _port == port)
            {
                // Enabling or rotating the pairing code must drop existing sessions
                // so clients re-authenticate with the new code.
                if (pairingChanged)
                    DisconnectAllClients("Pairing code changed");
                return;
            }
            Stop();
            Start(port);
        }

        private void DisconnectAllClients(string reason)
        {
            List<ClientConn> snapshot;
            lock (_gate)
            {
                snapshot = new List<ClientConn>(_clients);
                _clients.Clear();
            }
            if (snapshot.Count == 0) return;
            try
            {
                RenewEngine.Log("Notify disconnect all clients (" + snapshot.Count + "): "
                    + (string.IsNullOrEmpty(reason) ? "settings" : reason));
            }
            catch { }
            foreach (var c in snapshot)
            {
                if (c == null) continue;
                var wasAuth = c.Authenticated;
                var id = c.Id;
                try { c.Dispose(); }
                catch { }
                if (!wasAuth) continue;
                var handler = ClientDisconnected;
                if (handler == null) continue;
                try { handler(id); }
                catch (Exception ex)
                {
                    try { RenewEngine.Log("Notify ClientDisconnected handler: " + ex.Message); }
                    catch { }
                }
            }
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
                    StartUdpDiscovery(port);
                    StartPingTimer();
                    RenewEngine.Log("Notify server listening on 0.0.0.0:" + port + " (TCP+UDP)");
                }
                catch (Exception ex)
                {
                    _running = false;
                    StopUdpDiscovery();
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
                StopUdpDiscovery();
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

        private void StartUdpDiscovery(int port)
        {
            StopUdpDiscovery();
            try
            {
                _udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));
                try { _udp.EnableBroadcast = true; }
                catch { }
                _udpThread = new Thread(UdpDiscoverLoop)
                {
                    IsBackground = true,
                    Name = "NotifyServerUdpDiscover"
                };
                _udpThread.Start();
            }
            catch (Exception ex)
            {
                _udp = null;
                try { RenewEngine.Log("Notify UDP discovery start failed: " + ex.Message); }
                catch { }
            }
        }

        private void StopUdpDiscovery()
        {
            try
            {
                if (_udp != null)
                    _udp.Close();
            }
            catch { }
            _udp = null;
            _udpThread = null;
        }

        private void UdpDiscoverLoop()
        {
            var expected = Encoding.ASCII.GetBytes(UdpDiscoverRequest);
            while (_running)
            {
                try
                {
                    var udp = _udp;
                    if (udp == null) break;
                    IPEndPoint remote = null;
                    var data = udp.Receive(ref remote);
                    if (!_running || data == null || remote == null) continue;
                    if (data.Length < expected.Length) continue;
                    var match = true;
                    for (var i = 0; i < expected.Length; i++)
                    {
                        if (data[i] != expected[i])
                        {
                            match = false;
                            break;
                        }
                    }
                    if (!match) continue;
                    var reply = Encoding.ASCII.GetBytes(
                        UdpDiscoverResponsePrefix + _port + "|" + Environment.MachineName);
                    udp.Send(reply, reply.Length, remote);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException)
                {
                    if (!_running) break;
                }
                catch
                {
                    if (!_running) break;
                    Thread.Sleep(50);
                }
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
                Title = "KAI - AOS Renew",
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
            {
                snapshot = new List<ClientConn>();
                foreach (var c in _clients)
                {
                    if (c != null && c.Authenticated)
                        snapshot.Add(c);
                }
            }
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
            if (target == null || !target.Authenticated) return false;
            return TrySendOrDrop(target, bytes);
        }

        public List<NotifyClientInfo> GetClients()
        {
            var list = new List<NotifyClientInfo>();
            lock (_gate)
            {
                foreach (var c in _clients)
                {
                    if (c == null || !c.Authenticated) continue;
                    list.Add(new NotifyClientInfo
                    {
                        Id = c.Id,
                        Display = c.RemoteDisplay,
                        Version = c.ClientVersion ?? ""
                    });
                }
            }
            return list;
        }

        public bool SetClientVersion(int clientId, string version)
        {
            if (clientId <= 0) return false;
            var ver = (version ?? "").Trim();
            lock (_gate)
            {
                foreach (var c in _clients)
                {
                    if (c.Id == clientId)
                    {
                        c.ClientVersion = ver;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Apply clientHello identity and dispose ghost sockets for the same InstanceGuid
        /// (or same RemoteIp when Guid is empty — legacy clients).
        /// </summary>
        public void ApplyClientHello(int clientId, string version, string instanceGuid)
        {
            if (clientId <= 0) return;
            var ver = (version ?? "").Trim();
            var guid = (instanceGuid ?? "").Trim();
            List<ClientConn> replaced = null;
            lock (_gate)
            {
                ClientConn self = null;
                foreach (var c in _clients)
                {
                    if (c != null && c.Id == clientId)
                    {
                        self = c;
                        break;
                    }
                }
                if (self == null) return;
                self.ClientVersion = ver;
                self.InstanceGuid = guid;

                for (var i = _clients.Count - 1; i >= 0; i--)
                {
                    var old = _clients[i];
                    if (old == null || old.Id == clientId) continue;
                    var match = false;
                    if (!string.IsNullOrEmpty(guid))
                    {
                        match = string.Equals(old.InstanceGuid, guid, StringComparison.OrdinalIgnoreCase);
                    }
                    else if (!string.IsNullOrEmpty(self.RemoteIp))
                    {
                        match = string.Equals(old.RemoteIp, self.RemoteIp, StringComparison.OrdinalIgnoreCase);
                    }
                    if (!match) continue;
                    _clients.RemoveAt(i);
                    if (replaced == null) replaced = new List<ClientConn>();
                    replaced.Add(old);
                }
            }
            if (replaced == null) return;
            foreach (var old in replaced)
            {
                var wasAuth = old.Authenticated;
                try { old.Dispose(); }
                catch { }
                try
                {
                    RenewEngine.Log("Notify client replaced #" + old.Id + " (" + old.RemoteDisplay + ")"
                        + (string.IsNullOrEmpty(guid) ? " by RemoteIp" : " by InstanceGuid"));
                }
                catch { }
                if (wasAuth)
                {
                    var disc = ClientDisconnected;
                    if (disc != null)
                    {
                        try { disc(old.Id); }
                        catch { }
                    }
                }
            }
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
                    try
                    {
                        if (tcp.Client != null)
                            tcp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                    }
                    catch { }
                    int id;
                    ClientConn conn;
                    string pairing;
                    lock (_gate)
                    {
                        id = _nextClientId++;
                        pairing = _pairingCode ?? "";
                        conn = new ClientConn(id, tcp, OnClientMessage, RemoveClient, OnClientAuthenticated, pairing);
                        // Ghost replace happens on clientHello (InstanceGuid / RemoteIp fallback).
                        _clients.Add(conn);
                    }
                    conn.StartRead();
                    conn.BeginAuthIfNeeded();
                    RenewEngine.Log("Notify client socket #" + id + " (" + conn.RemoteDisplay + ")"
                        + (string.IsNullOrEmpty(pairing) ? "" : " — awaiting pairing"));
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

        private void OnClientAuthenticated(ClientConn c)
        {
            if (c == null) return;
            try { RenewEngine.Log("Notify client connected #" + c.Id + " (" + ClientCount + ")"); }
            catch { }
            var handler = ClientConnected;
            if (handler != null)
            {
                try { handler(c.Id); }
                catch (Exception ex)
                {
                    try { RenewEngine.Log("Notify ClientConnected handler: " + ex.Message); }
                    catch { }
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
            var id = 0;
            var wasAuth = false;
            lock (_gate)
            {
                removed = _clients.Remove(c);
                if (removed)
                {
                    id = c.Id;
                    wasAuth = c.Authenticated;
                }
            }
            if (!removed) return;
            try { c.Dispose(); }
            catch { }
            try { RenewEngine.Log("Notify client disconnected #" + id + " (" + ClientCount + ")"); }
            catch { }
            if (!wasAuth) return;
            var handler = ClientDisconnected;
            if (handler != null)
            {
                try { handler(id); }
                catch (Exception ex)
                {
                    try { RenewEngine.Log("Notify ClientDisconnected handler: " + ex.Message); }
                    catch { }
                }
            }
        }

        private sealed class ClientConn : IDisposable
        {
            private readonly TcpClient _tcp;
            private readonly NetworkStream _stream;
            private readonly object _writeGate = new object();
            private readonly Action<int, NotifyMessage> _onMessage;
            private readonly Action<ClientConn> _onClosed;
            private readonly Action<ClientConn> _onAuthenticated;
            private readonly string _pairingCode;
            private readonly TcpUtf8LineReader _lineReader = new TcpUtf8LineReader();
            private Thread _readThread;
            private volatile bool _disposed;
            private string _authNonce;
            private int _authNotified;

            public int Id { get; private set; }
            public string RemoteDisplay { get; private set; }
            public string RemoteIp { get; private set; }
            public string ClientVersion { get; set; }
            public string InstanceGuid { get; set; }
            public bool Authenticated { get; private set; }

            public ClientConn(
                int id,
                TcpClient tcp,
                Action<int, NotifyMessage> onMessage,
                Action<ClientConn> onClosed,
                Action<ClientConn> onAuthenticated,
                string pairingCode)
            {
                Id = id;
                _tcp = tcp;
                _stream = tcp.GetStream();
                _onMessage = onMessage;
                _onClosed = onClosed;
                _onAuthenticated = onAuthenticated;
                _pairingCode = (pairingCode ?? "").Trim();
                Authenticated = string.IsNullOrEmpty(_pairingCode);
                RemoteIp = "";
                ClientVersion = "";
                InstanceGuid = "";
                try
                {
                    var ep = tcp.Client != null ? tcp.Client.RemoteEndPoint as IPEndPoint : null;
                    if (ep != null)
                    {
                        RemoteIp = ep.Address.ToString();
                        RemoteDisplay = ep.ToString();
                    }
                    else
                        RemoteDisplay = "#" + id;
                }
                catch
                {
                    RemoteDisplay = "#" + id;
                }
            }

            public void BeginAuthIfNeeded()
            {
                if (Authenticated)
                {
                    NotifyAuthenticatedOnce();
                    return;
                }
                _authNonce = NotifyPairing.CreateNonce();
                TrySend(Encode(new NotifyMessage
                {
                    Kind = "authChallenge",
                    Level = "info",
                    Title = "KAI - AOS Renew",
                    Text = _authNonce
                }));
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
                    while (!_disposed)
                    {
                        var n = _stream.Read(buf, 0, buf.Length);
                        if (n <= 0) break;
                        if (!_lineReader.Append(buf, 0, n, HandleLine))
                        {
                            try { RenewEngine.Log("Notify client #" + Id + " line too long — dropped"); }
                            catch { }
                            break;
                        }
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

            private void HandleLine(string line)
            {
                var msg = NotifyMessage.ParseLine(line);
                if (msg == null) return;
                var kind = (msg.Kind ?? "").Trim().ToLowerInvariant();
                // LAN Scan probes may send discoverPing before pairing; reply without auth.
                if (kind == "discoverping")
                {
                    TrySend(Encode(new NotifyMessage
                    {
                        Kind = "discoverPong",
                        Level = "info",
                        Title = "KAI - AOS Renew",
                        Text = Environment.MachineName
                    }));
                    return;
                }
                if (!Authenticated)
                {
                    if (kind == "authresponse")
                    {
                        HandleAuthResponse(msg.Text);
                        return;
                    }
                    // Allow clientHello only after auth; drop everything else until paired.
                    return;
                }
                if (_onMessage != null)
                    _onMessage(Id, msg);
            }

            private void HandleAuthResponse(string response)
            {
                if (Authenticated) return;
                if (NotifyPairing.VerifyResponse(_pairingCode, _authNonce, response))
                {
                    Authenticated = true;
                    TrySend(Encode(new NotifyMessage
                    {
                        Kind = "authOk",
                        Level = "info",
                        Title = "KAI - AOS Renew",
                        Text = "ok"
                    }));
                    NotifyAuthenticatedOnce();
                    return;
                }
                TrySend(Encode(new NotifyMessage
                {
                    Kind = "authFailed",
                    Level = "error",
                    Title = "KAI - AOS Renew",
                    Text = "Invalid pairing code"
                }));
                try { RenewEngine.Log("Notify client #" + Id + " pairing failed"); }
                catch { }
                Dispose();
            }

            private void NotifyAuthenticatedOnce()
            {
                if (Interlocked.Exchange(ref _authNotified, 1) != 0) return;
                if (_onAuthenticated != null)
                {
                    try { _onAuthenticated(this); }
                    catch { }
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
        public string Version { get; set; }
    }
}
