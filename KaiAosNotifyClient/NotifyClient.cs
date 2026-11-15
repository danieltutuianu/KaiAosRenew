using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace KaiAosNotifyClient
{
    internal sealed class NotifyClient : IDisposable
    {
        private readonly object _gate = new object();
        private TcpClient _tcp;
        private Thread _readThread;
        private Thread _reconnectThread;
        private volatile bool _wantConnect;
        private volatile bool _disposed;
        private string _host = "127.0.0.1";
        private int _port = 17890;

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
            DisconnectSocket();
            StartReconnectLoop();
            TryConnectOnce();
        }

        public void Disconnect()
        {
            _wantConnect = false;
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
                Thread.Sleep(3000);
                if (!_wantConnect || _disposed) continue;
                if (IsConnected) continue;
                TryConnectOnce();
            }
        }

        private void TryConnectOnce()
        {
            string host;
            int port;
            lock (_gate)
            {
                host = _host;
                port = _port;
            }
            try
            {
                RaiseStatus("Connecting to " + host + ":" + port + "…");
                var tcp = new TcpClient();
                tcp.NoDelay = true;
                var ar = tcp.BeginConnect(host, port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(5000) || !tcp.Connected)
                {
                    try { tcp.Close(); }
                    catch { }
                    RaiseStatus("Connect failed — retrying…");
                    return;
                }
                tcp.EndConnect(ar);
                lock (_gate)
                {
                    if (!_wantConnect)
                    {
                        try { tcp.Close(); }
                        catch { }
                        return;
                    }
                    DisconnectSocketUnlocked();
                    _tcp = tcp;
                }
                RaiseStatus("Connected to " + host + ":" + port);
                RaiseConnected();
                _readThread = new Thread(ReadLoop) { IsBackground = true, Name = "NotifyClientRead" };
                _readThread.Start();
            }
            catch (Exception ex)
            {
                RaiseStatus("Connect error: " + ex.Message);
            }
        }

        private void ReadLoop()
        {
            NetworkStream stream = null;
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
                    if (n <= 0) break;
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
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                RaiseStatus("Read error: " + ex.Message);
            }
            finally
            {
                DisconnectSocket();
                if (_wantConnect)
                {
                    RaiseStatus("Connection lost — reconnecting…");
                    RaiseDisconnected();
                }
            }
        }

        private static int IndexOfNewline(string s)
        {
            var i = s.IndexOf('\n');
            return i;
        }

        private void DisconnectSocket()
        {
            lock (_gate)
                DisconnectSocketUnlocked();
        }

        private void DisconnectSocketUnlocked()
        {
            try
            {
                if (_tcp != null)
                    _tcp.Close();
            }
            catch { }
            _tcp = null;
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
