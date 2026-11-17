using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace KaiAosNotifyClient
{
    internal sealed class NotifyClient : IDisposable
    {
        private const int ReconnectDelayMs = 3000;

        private readonly object _gate = new object();
        private TcpClient _tcp;
        private Thread _readThread;
        private Thread _reconnectThread;
        private volatile bool _wantConnect;
        private volatile bool _disposed;
        private int _connectFlight;
        private string _host = "127.0.0.1";
        private int _port = 17890;
        private int _reconnectAttempt;
        private string _lastDisconnectReason = "";
        private DateTime _nextRetryUtc = DateTime.MinValue;
        private int _forceRetry;

        public event Action Connected;
        public event Action Disconnected;
        public event Action<string> Status;
        public event Action<NotifyMessage> MessageReceived;

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

        public int ReconnectAttempt { get { return _reconnectAttempt; } }

        public string LastDisconnectReason
        {
            get { return _lastDisconnectReason ?? ""; }
        }

        public DateTime NextRetryUtc { get { return _nextRetryUtc; } }

        public void Connect(string host, int port)
        {
            if (string.IsNullOrWhiteSpace(host)) host = "127.0.0.1";
            if (port < 1 || port > 65535) port = 17890;
            lock (_gate)
            {
                _host = host.Trim();
                _port = port;
                _wantConnect = true;
            }
            Interlocked.Exchange(ref _forceRetry, 1);
            _nextRetryUtc = DateTime.MinValue;
            DisconnectSocket();
            StartReconnectLoop();
            TryConnectOnce();
        }

        public void RetryNow()
        {
            if (!_wantConnect || _disposed) return;
            Interlocked.Exchange(ref _forceRetry, 1);
            _nextRetryUtc = DateTime.MinValue;
            TryConnectOnce();
        }

        public void Disconnect()
        {
            _wantConnect = false;
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

        private void ReconnectLoop()
        {
            while (!_disposed)
            {
                if (!_wantConnect || IsConnected)
                {
                    Thread.Sleep(200);
                    continue;
                }

                _nextRetryUtc = DateTime.UtcNow.AddMilliseconds(ReconnectDelayMs);
                RaiseStatus("waiting");

                var waitUntil = _nextRetryUtc;
                while (!_disposed && _wantConnect && !IsConnected)
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

                if (!_wantConnect || _disposed || IsConnected)
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
                if (!_wantConnect || _disposed) return;
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
                if (!ar.AsyncWaitHandle.WaitOne(5000) || !tcp.Connected)
                {
                    HardClose(tcp);
                    NoteDisconnect("Connect failed");
                    RaiseStatus("Connect failed — retrying…");
                    return;
                }
                tcp.EndConnect(ar);
                lock (_gate)
                {
                    if (!_wantConnect || _disposed)
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
                RaiseStatus("Connected to " + host + ":" + port);
                RaiseConnected();
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
            NetworkStream stream = null;
            string failReason = null;
            try
            {
                lock (_gate)
                {
                    if (_tcp == null) return;
                    stream = _tcp.GetStream();
                }
                var buf = new byte[4096];
                var sb = new StringBuilder();
                while (_wantConnect && !_disposed)
                {
                    var n = stream.Read(buf, 0, buf.Length);
                    if (n <= 0)
                    {
                        failReason = "Connection lost";
                        break;
                    }
                    sb.Append(Encoding.UTF8.GetString(buf, 0, n));
                    string chunk = sb.ToString();
                    int nl;
                    while ((nl = IndexOfNewline(chunk)) >= 0)
                    {
                        var line = chunk.Substring(0, nl).Trim('\r', '\n', ' ');
                        chunk = chunk.Substring(nl + 1);
                        if (line.Length > 0)
                        {
                            var msg = NotifyMessage.ParseLine(line);
                            if (msg != null)
                                RaiseMessage(msg);
                        }
                    }
                    sb.Length = 0;
                    sb.Append(chunk);
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
                DisconnectSocket();
                if (_wantConnect)
                {
                    NoteDisconnect(failReason ?? "Connection lost");
                    RaiseStatus("Connection lost — reconnecting…");
                    RaiseDisconnected();
                }
            }
        }

        private static int IndexOfNewline(string s)
        {
            return s.IndexOf('\n');
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
    }
}
