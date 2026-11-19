using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using KaiAosRenew;

namespace KaiAosNotifyClient
{
    internal sealed class NotifyClient : IDisposable
    {
        private const int ReconnectDelayMs = 3000;
        private const int ConnectTimeoutMs = 5000;
        private const int PingWatchdogMs = 75000;
        private const int WatchdogPollMs = 5000;

        private readonly object _gate = new object();
        private TcpClient _tcp;
        private Thread _readThread;
        private Thread _reconnectThread;
        private Thread _watchdogThread;
        private volatile bool _wantConnect;
        private volatile bool _disposed;
        private int _connectFlight;
        private string _host = "127.0.0.1";
        private int _port = 17890;
        private string _pairingCode = "";
        private int _reconnectAttempt;
        private string _lastDisconnectReason = "";
        private DateTime _nextRetryUtc = DateTime.MinValue;
        private int _forceRetry;
        private DateTime _lastRxUtc = DateTime.MinValue;
        private volatile bool _authFailedStop;
        private volatile bool _awaitingAuth;
        private int _connectedRaised;

        public event Action Connected;
        public event Action Disconnected;
        public event Action<string> Status;
        public event Action<NotifyMessage> MessageReceived;
        /// <summary>Raised when server rejects the pairing code (auto-reconnect stopped).</summary>
        public event Action<string> AuthFailed;

        public bool IsConnected
        {
            get
            {
                lock (_gate)
                    return _tcp != null && _tcp.Connected;
            }
        }

        public bool WantConnect { get { return _wantConnect; } }

        public string Host
        {
            get { lock (_gate) return _host; }
        }

        public int Port
        {
            get { lock (_gate) return _port; }
        }

        public string PairingCode
        {
            get { lock (_gate) return _pairingCode ?? ""; }
        }

        public int ReconnectAttempt { get { return _reconnectAttempt; } }

        public string LastDisconnectReason
        {
            get { return _lastDisconnectReason ?? ""; }
        }

        public DateTime NextRetryUtc { get { return _nextRetryUtc; } }

        /// <summary>Test hook: make RX look stale so WatchdogLoop can fire promptly.</summary>
        internal void ForceStaleRxForTests()
        {
            _lastRxUtc = DateTime.UtcNow.AddMinutes(-10);
        }

        /// <summary>
        /// Test hook: run the same ownership-safe disconnect path as WatchdogLoop
        /// without waiting for PingWatchdogMs.
        /// </summary>
        internal void TriggerWatchdogDisconnectForTests()
        {
            var closed = false;
            lock (_gate)
            {
                if (_tcp == null) return;
                NoteDisconnect("Server ping timeout");
                DisconnectSocketUnlocked();
                closed = true;
            }
            if (!closed) return;
            RaiseStatus("Server ping timeout — reconnecting…");
            RaiseDisconnected();
        }

        public void Connect(string host, int port)
        {
            Connect(host, port, null);
        }

        public void Connect(string host, int port, string pairingCode)
        {
            if (string.IsNullOrWhiteSpace(host)) host = "127.0.0.1";
            if (port < 1 || port > 65535) port = 17890;
            lock (_gate)
            {
                _host = host.Trim();
                _port = port;
                if (pairingCode != null)
                    _pairingCode = pairingCode.Trim();
                _wantConnect = true;
            }
            _authFailedStop = false;
            Interlocked.Exchange(ref _forceRetry, 1);
            _nextRetryUtc = DateTime.MinValue;
            DisconnectSocket();
            StartReconnectLoop();
            StartWatchdogLoop();
            // Do not call TryConnectOnce() on the caller (often UI) thread — it blocks up to ConnectTimeoutMs.
            // ReconnectLoop honors _forceRetry and connects on the background thread immediately.
        }

        public void SetPairingCode(string pairingCode)
        {
            lock (_gate)
                _pairingCode = (pairingCode ?? "").Trim();
        }

        public void RetryNow()
        {
            if (!_wantConnect || _disposed || _authFailedStop) return;
            Interlocked.Exchange(ref _forceRetry, 1);
            _nextRetryUtc = DateTime.MinValue;
            TryConnectOnce();
        }

        public void Disconnect()
        {
            _wantConnect = false;
            _authFailedStop = false;
            Interlocked.Exchange(ref _forceRetry, 0);
            _reconnectAttempt = 0;
            _lastDisconnectReason = "";
            _nextRetryUtc = DateTime.MinValue;
            DisconnectSocket();
            RaiseStatus("Disconnected");
            RaiseDisconnected();
        }

        public bool Send(NotifyMessage msg)
        {
            if (msg == null || _disposed) return false;
            byte[] bytes;
            try
            {
                bytes = Encoding.UTF8.GetBytes(msg.ToJsonLine() + "\n");
            }
            catch
            {
                return false;
            }
            lock (_gate)
            {
                if (_tcp == null || !_tcp.Connected) return false;
                try
                {
                    var stream = _tcp.GetStream();
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush();
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        public void Dispose()
        {
            _disposed = true;
            _wantConnect = false;
            DisconnectSocket();
        }

        private void StartReconnectLoop()
        {
            if (_reconnectThread != null && _reconnectThread.IsAlive) return;
            _reconnectThread = new Thread(ReconnectLoop)
            {
                IsBackground = true,
                Name = "NotifyClientReconnect"
            };
            _reconnectThread.Start();
        }

        private void StartWatchdogLoop()
        {
            if (_watchdogThread != null && _watchdogThread.IsAlive) return;
            _watchdogThread = new Thread(WatchdogLoop)
            {
                IsBackground = true,
                Name = "NotifyClientWatchdog"
            };
            _watchdogThread.Start();
        }

        private void WatchdogLoop()
        {
            while (!_disposed)
            {
                Thread.Sleep(WatchdogPollMs);
                if (!_wantConnect || _authFailedStop || !IsConnected) continue;
                var last = _lastRxUtc;
                if (last == DateTime.MinValue) continue;
                if ((DateTime.UtcNow - last).TotalMilliseconds < PingWatchdogMs) continue;
                var closed = false;
                lock (_gate)
                {
                    if (_tcp == null || !_tcp.Connected) continue;
                    // Re-check under lock so a concurrent ReadLoop close wins ownership.
                    if ((DateTime.UtcNow - _lastRxUtc).TotalMilliseconds < PingWatchdogMs) continue;
                    NoteDisconnect("Server ping timeout");
                    DisconnectSocketUnlocked();
                    closed = true;
                }
                if (!closed) continue;
                RaiseStatus("Server ping timeout — reconnecting…");
                RaiseDisconnected();
            }
        }

        private void ReconnectLoop()
        {
            while (!_disposed)
            {
                if (!_wantConnect || _authFailedStop || IsConnected)
                {
                    Thread.Sleep(200);
                    continue;
                }

                _nextRetryUtc = DateTime.UtcNow.AddMilliseconds(ReconnectDelayMs);
                RaiseStatus("waiting");

                var waitUntil = _nextRetryUtc;
                while (!_disposed && _wantConnect && !_authFailedStop && !IsConnected)
                {
                    if (Interlocked.CompareExchange(ref _forceRetry, 0, 1) == 1)
                    {
                        _nextRetryUtc = DateTime.MinValue;
                        break;
                    }
                    if (DateTime.UtcNow >= waitUntil)
                        break;
                    Thread.Sleep(100);
                }

                if (!_wantConnect || _disposed || _authFailedStop || IsConnected)
                    continue;

                TryConnectOnce();
            }
        }

        private void TryConnectOnce()
        {
            // Single-flight: one connect attempt at a time (UI Connect + reconnect loop).
            if (Interlocked.CompareExchange(ref _connectFlight, 1, 0) != 0)
                return;
            Interlocked.Exchange(ref _forceRetry, 0);
            try
            {
                if (!_wantConnect || _disposed || _authFailedStop) return;
                if (IsConnected) return;

                string host;
                int port;
                lock (_gate)
                {
                    host = _host;
                    port = _port;
                }

                _reconnectAttempt++;
                _nextRetryUtc = DateTime.MinValue;

                // Drop any leftover socket before opening a new one.
                DisconnectSocket();

                RaiseStatus("Connecting to " + host + ":" + port + "…");
                var tcp = new TcpClient();
                tcp.NoDelay = true;
                var ar = tcp.BeginConnect(host, port, null, null);
                var connected = false;
                try
                {
                    connected = ar.AsyncWaitHandle.WaitOne(ConnectTimeoutMs) && tcp.Connected;
                }
                finally
                {
                    try { ar.AsyncWaitHandle.Close(); }
                    catch { }
                }
                if (!connected)
                {
                    HardClose(tcp);
                    NoteDisconnect("Connect failed");
                    RaiseStatus("Connect failed — retrying…");
                    return;
                }
                try { tcp.EndConnect(ar); }
                catch
                {
                    HardClose(tcp);
                    NoteDisconnect("Connect failed");
                    RaiseStatus("Connect failed — retrying…");
                    return;
                }
                lock (_gate)
                {
                    if (!_wantConnect || _disposed || _authFailedStop)
                    {
                        HardClose(tcp);
                        return;
                    }
                    DisconnectSocketUnlocked();
                    _tcp = tcp;
                }
                _reconnectAttempt = 0;
                _lastDisconnectReason = "";
                _nextRetryUtc = DateTime.MinValue;
                _lastRxUtc = DateTime.UtcNow;
                _awaitingAuth = false;
                Interlocked.Exchange(ref _connectedRaised, 0);
                RaiseStatus("Connected to " + host + ":" + port);
                // RaiseConnected fires after authOk, or on first non-challenge message when pairing is off.
                _readThread = new Thread(ReadLoop) { IsBackground = true, Name = "NotifyClientRead" };
                _readThread.Start();
            }
            catch (Exception ex)
            {
                NoteDisconnect(string.IsNullOrEmpty(ex.Message) ? "Connect error" : ex.Message);
                RaiseStatus("Connect error: " + ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _connectFlight, 0);
            }
        }

        private void NoteDisconnect(string reason)
        {
            if (!string.IsNullOrWhiteSpace(reason))
                _lastDisconnectReason = reason.Trim();
            _nextRetryUtc = DateTime.UtcNow.AddMilliseconds(ReconnectDelayMs);
        }

        private void ReadLoop()
        {
            TcpClient myTcp = null;
            NetworkStream stream = null;
            string failReason = null;
            var lineReader = new TcpUtf8LineReader();
            try
            {
                lock (_gate)
                {
                    myTcp = _tcp;
                    if (myTcp == null) return;
                    stream = myTcp.GetStream();
                }
                var buf = new byte[4096];
                while (_wantConnect && !_disposed && !_authFailedStop)
                {
                    var n = stream.Read(buf, 0, buf.Length);
                    if (n <= 0)
                    {
                        failReason = "Connection lost";
                        break;
                    }
                    _lastRxUtc = DateTime.UtcNow;
                    if (!lineReader.Append(buf, 0, n, HandleLine))
                    {
                        failReason = "Line too long";
                        break;
                    }
                }
            }
            catch (IOException)
            {
                failReason = "Connection lost";
            }
            catch (ObjectDisposedException)
            {
                failReason = "Connection lost";
            }
            catch (Exception ex)
            {
                failReason = string.IsNullOrEmpty(ex.Message) ? "Read error" : ex.Message;
                RaiseStatus("Read error: " + ex.Message);
            }
            finally
            {
                // Only close/notify if this loop still owns the active socket —
                // an older ReadLoop must not tear down a newly opened connection
                // or overwrite a watchdog "Server ping timeout" reason.
                var owned = false;
                lock (_gate)
                {
                    owned = myTcp != null && ReferenceEquals(_tcp, myTcp);
                    if (owned)
                        DisconnectSocketUnlocked();
                }
                if (owned && _wantConnect && !_authFailedStop)
                {
                    NoteDisconnect(failReason ?? "Connection lost");
                    RaiseStatus("Connection lost — reconnecting…");
                    RaiseDisconnected();
                }
            }
        }

        private void HandleLine(string line)
        {
            var msg = NotifyMessage.ParseLine(line);
            if (msg == null) return;
            _lastRxUtc = DateTime.UtcNow;
            var kind = (msg.Kind ?? "").Trim().ToLowerInvariant();
            if (kind == "authchallenge")
            {
                _awaitingAuth = true;
                var code = (PairingCode ?? "").Trim();
                if (string.IsNullOrEmpty(code))
                {
                    // Do not send an empty HMAC — stop before server authFailed.
                    _authFailedStop = true;
                    _wantConnect = false;
                    const string reason = "PAIRING_REQUIRED";
                    NoteDisconnect(reason);
                    RaiseStatus(reason);
                    RaiseAuthFailed(reason);
                    DisconnectSocket();
                    RaiseDisconnected();
                    return;
                }
                RaiseStatus("Pairing…");
                var response = NotifyPairing.ComputeResponse(code, msg.Text);
                Send(new NotifyMessage
                {
                    Kind = "authResponse",
                    Level = "info",
                    Title = "KAI - AOS Renew",
                    Text = response
                });
                return;
            }
            if (kind == "authok")
            {
                _awaitingAuth = false;
                RaiseStatus("Paired");
                RaiseConnectedOnce();
                return;
            }
            if (kind == "authfailed")
            {
                _authFailedStop = true;
                _wantConnect = false;
                var reason = string.IsNullOrWhiteSpace(msg.Text) ? "Invalid pairing code" : msg.Text.Trim();
                NoteDisconnect(reason);
                RaiseStatus(reason);
                RaiseAuthFailed(reason);
                DisconnectSocket();
                RaiseDisconnected();
                return;
            }
            // No pairing on server: first real message means we are authenticated.
            if (!_awaitingAuth)
                RaiseConnectedOnce();
            RaiseMessage(msg);
        }

        private void RaiseConnectedOnce()
        {
            if (Interlocked.Exchange(ref _connectedRaised, 1) != 0) return;
            RaiseConnected();
        }

        private void DisconnectSocket()
        {
            lock (_gate)
                DisconnectSocketUnlocked();
        }

        private void DisconnectSocketUnlocked()
        {
            var tcp = _tcp;
            _tcp = null;
            HardClose(tcp);
        }

        private static void HardClose(TcpClient tcp)
        {
            if (tcp == null) return;
            try
            {
                if (tcp.Client != null)
                {
                    try { tcp.Client.LingerState = new LingerOption(true, 0); }
                    catch { }
                    try { tcp.Client.Shutdown(SocketShutdown.Both); }
                    catch { }
                }
            }
            catch { }
            try { tcp.Close(); }
            catch { }
        }

        private void RaiseConnected()
        {
            var h = Connected;
            if (h != null) try { h(); } catch { }
        }

        private void RaiseDisconnected()
        {
            var h = Disconnected;
            if (h != null) try { h(); } catch { }
        }

        private void RaiseStatus(string s)
        {
            var h = Status;
            if (h != null) try { h(s); } catch { }
        }

        private void RaiseMessage(NotifyMessage m)
        {
            var h = MessageReceived;
            if (h != null) try { h(m); } catch { }
        }

        private void RaiseAuthFailed(string reason)
        {
            var h = AuthFailed;
            if (h != null) try { h(reason); } catch { }
        }
    }
}
