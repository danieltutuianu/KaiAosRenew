using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace KaiAosNotifyClient
{
    /// <summary>
    /// Instant UDP broadcast discovery, then verified TCP /24 fallback (no false positives).
    /// </summary>
    /// <summary>Progress snapshot for a LAN scan (phase "udp" = broadcast discovery, "tcp" = /24 sweep).</summary>
    internal sealed class LanScanProgress
    {
        public string Phase;
        public int Checked;
        public int Total;
    }

    internal static class LanServerDiscovery
    {
        public const string UdpDiscoverRequest = "KAI_DISCOVER_V1";
        public const string UdpDiscoverResponsePrefix = "KAI_HERE_V1|";
        private const int DefaultPort = 17890;
        private const int UdpWaitMs = 450;
        private const int ConnectTimeoutMs = 500;
        private const int ProtocolVerifyMs = 800;
        private const int MaxParallel = 48;

        private enum KaiProbe
        {
            Fail = 0,
            Open = 1,
            PairingRequired = 2
        }

        /// <summary>Test-accessible TCP+protocol probe against a known host (loopback / LAN).</summary>
        internal static bool TryTcpVerify(string ip, int port, out bool pairingRequired)
        {
            pairingRequired = false;
            if (string.IsNullOrWhiteSpace(ip)) return false;
            if (port < 1 || port > 65535) port = DefaultPort;
            KaiProbe probe;
            if (!TryConnectVerified(ip.Trim(), port, Math.Max(ConnectTimeoutMs, ProtocolVerifyMs), out probe))
                return false;
            if (probe == KaiProbe.Fail) return false;
            pairingRequired = probe == KaiProbe.PairingRequired;
            return true;
        }

        public static string FindFirstOpenHost(int port, CancellationToken cancel)
        {
            bool unused;
            return FindFirstOpenHost(port, cancel, out unused);
        }

        public static string FindFirstOpenHost(int port, CancellationToken cancel, out bool pairingRequired)
        {
            return FindFirstOpenHost(port, cancel, null, out pairingRequired);
        }

        /// <summary>
        /// Same as the plain overload but reports <see cref="LanScanProgress"/> (UDP phase once, then TCP checked/total
        /// roughly every 150 ms) and honours <paramref name="cancel"/> promptly. <paramref name="progress"/> is invoked
        /// on a worker thread — marshal to the UI thread in the callback.
        /// </summary>
        public static string FindFirstOpenHost(int port, CancellationToken cancel,
            Action<LanScanProgress> progress, out bool pairingRequired)
        {
            pairingRequired = false;
            if (port < 1 || port > 65535) port = DefaultPort;

            RaiseProgress(progress, "udp", 0, 0);
            var udpHost = TryUdpDiscoverVerified(port, cancel, out pairingRequired);
            if (!string.IsNullOrEmpty(udpHost))
                return udpHost;

            if (cancel.IsCancellationRequested) return null;
            return TryTcpScanVerified(port, cancel, progress, out pairingRequired);
        }

        private static void RaiseProgress(Action<LanScanProgress> progress, string phase, int done, int total)
        {
            if (progress == null) return;
            try { progress(new LanScanProgress { Phase = phase, Checked = done, Total = total }); }
            catch { }
        }

        /// <summary>
        /// UDP broadcast, then TCP-verify each responder. Unverified UDP hosts are ignored
        /// so we fall through to TCP /24 scan on multi-NIC / filtered networks.
        /// </summary>
        private static string TryUdpDiscoverVerified(int port, CancellationToken cancel, out bool pairingRequired)
        {
            pairingRequired = false;
            UdpClient udp = null;
            var tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                udp = new UdpClient(0);
                try { udp.EnableBroadcast = true; }
                catch { }
                udp.Client.ReceiveTimeout = 50;

                var payload = Encoding.ASCII.GetBytes(UdpDiscoverRequest);
                var targets = BuildUdpTargets(port);
                foreach (var ep in targets)
                {
                    if (cancel.IsCancellationRequested) return null;
                    try { udp.Send(payload, payload.Length, ep); }
                    catch { }
                }

                var deadline = Environment.TickCount + UdpWaitMs;
                while (Environment.TickCount - deadline < 0)
                {
                    if (cancel.IsCancellationRequested) return null;
                    try
                    {
                        if (udp.Available <= 0)
                        {
                            Thread.Sleep(15);
                            continue;
                        }
                        IPEndPoint remote = null;
                        var data = udp.Receive(ref remote);
                        if (data == null || remote == null) continue;
                        var text = Encoding.ASCII.GetString(data);
                        if (string.IsNullOrEmpty(text)
                            || !text.StartsWith(UdpDiscoverResponsePrefix, StringComparison.Ordinal))
                            continue;
                        var host = remote.Address.ToString();
                        if (string.IsNullOrEmpty(host) || !tried.Add(host)) continue;

                        KaiProbe probe;
                        if (TryConnectVerified(host, port, ConnectTimeoutMs, out probe)
                            && probe != KaiProbe.Fail)
                        {
                            pairingRequired = probe == KaiProbe.PairingRequired;
                            return host;
                        }
                    }
                    catch (SocketException)
                    {
                        Thread.Sleep(15);
                    }
                    catch
                    {
                        break;
                    }
                }
            }
            catch { }
            finally
            {
                if (udp != null)
                {
                    try { udp.Close(); }
                    catch { }
                }
            }
            return null;
        }

        private static List<IPEndPoint> BuildUdpTargets(int port)
        {
            var list = new List<IPEndPoint>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            Action<string> add = ip =>
            {
                if (string.IsNullOrEmpty(ip) || !seen.Add(ip)) return;
                try { list.Add(new IPEndPoint(IPAddress.Parse(ip), port)); }
                catch { }
            };

            add("127.0.0.1");
            add("255.255.255.255");
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        var addr = ua.Address.GetAddressBytes();
                        if (addr.Length != 4) continue;
                        if (addr[0] == 169 && addr[1] == 254) continue;
                        if (ua.IPv4Mask == null) continue;
                        var mask = ua.IPv4Mask.GetAddressBytes();
                        if (mask.Length != 4) continue;
                        var bcast = new byte[4];
                        for (var i = 0; i < 4; i++)
                            bcast[i] = (byte)(addr[i] | (byte)~mask[i]);
                        add(string.Format("{0}.{1}.{2}.{3}", bcast[0], bcast[1], bcast[2], bcast[3]));
                    }
                }
            }
            catch { }
            return list;
        }

        private static string TryTcpScanVerified(int port, CancellationToken cancel,
            Action<LanScanProgress> progress, out bool pairingRequired)
        {
            pairingRequired = false;
            var targets = BuildTargetList();
            if (targets.Count == 0) return null;

            string found = null;
            var foundPairing = false;
            var gate = new object();
            var index = 0;
            var checkedCount = 0;
            var total = targets.Count;
            var workers = Math.Min(MaxParallel, targets.Count);
            var remaining = workers;
            // Stops workers when we give up (cancel / overall timeout) so they do not keep probing in the background.
            using (var abort = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            using (var done = new ManualResetEvent(false))
            {
                var token = abort.Token;
                for (var w = 0; w < workers; w++)
                {
                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        try
                        {
                            while (!token.IsCancellationRequested)
                            {
                                string ip;
                                lock (gate)
                                {
                                    if (found != null) return;
                                    if (index >= targets.Count) return;
                                    ip = targets[index++];
                                }
                                KaiProbe probe;
                                var probeOk = TryConnectVerified(ip, port, ConnectTimeoutMs, out probe);
                                Interlocked.Increment(ref checkedCount);
                                if (probeOk && probe != KaiProbe.Fail)
                                {
                                    lock (gate)
                                    {
                                        if (found == null)
                                        {
                                            found = ip;
                                            foundPairing = probe == KaiProbe.PairingRequired;
                                        }
                                    }
                                    try { done.Set(); }
                                    catch { }
                                    return;
                                }
                            }
                        }
                        finally
                        {
                            if (Interlocked.Decrement(ref remaining) == 0)
                            {
                                try { done.Set(); }
                                catch { }
                            }
                        }
                    });
                }
                var started = Environment.TickCount;
                while (!done.WaitOne(150))
                {
                    if (cancel.IsCancellationRequested) break;
                    if (Environment.TickCount - started >= 45000) break;
                    RaiseProgress(progress, "tcp", Math.Min(Volatile.Read(ref checkedCount), total), total);
                }
                abort.Cancel();
                RaiseProgress(progress, "tcp", Math.Min(Volatile.Read(ref checkedCount), total), total);
            }
            lock (gate)
            {
                if (found == null) return null;
                pairingRequired = foundPairing;
                return found;
            }
        }

        private static bool TryConnectVerified(string ip, int port, int timeoutMs, out KaiProbe probe)
        {
            probe = KaiProbe.Fail;
            TcpClient tcp = null;
            try
            {
                tcp = new TcpClient();
                var ar = tcp.BeginConnect(IPAddress.Parse(ip), port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(timeoutMs) || !tcp.Connected)
                    return false;
                try { tcp.EndConnect(ar); }
                catch { return false; }
                tcp.NoDelay = true;
                probe = VerifyKaiProtocol(tcp, ProtocolVerifyMs);
                return probe != KaiProbe.Fail;
            }
            catch
            {
                probe = KaiProbe.Fail;
                return false;
            }
            finally
            {
                if (tcp != null)
                {
                    try { tcp.Close(); }
                    catch { }
                }
            }
        }

        /// <summary>
        /// Accept authChallenge (pairing on) or reply to discoverPing (pairing off).
        /// </summary>
        private static KaiProbe VerifyKaiProtocol(TcpClient tcp, int timeoutMs)
        {
            try
            {
                var stream = tcp.GetStream();
                stream.ReadTimeout = Math.Max(100, timeoutMs / 2);
                stream.WriteTimeout = Math.Max(100, timeoutMs / 2);

                // Probe first — servers without pairing won't speak until we ask.
                var ping = Encoding.UTF8.GetBytes(
                    "{\"v\":1,\"kind\":\"discoverPing\",\"level\":\"info\",\"title\":\"\",\"text\":\"\"}\n");
                stream.Write(ping, 0, ping.Length);
                stream.Flush();

                var buf = new byte[2048];
                var sb = new StringBuilder();
                var sw = Environment.TickCount;
                while (Environment.TickCount - sw < timeoutMs)
                {
                    if (!stream.DataAvailable)
                    {
                        Thread.Sleep(20);
                        continue;
                    }
                    var n = stream.Read(buf, 0, buf.Length);
                    if (n <= 0) break;
                    sb.Append(Encoding.UTF8.GetString(buf, 0, n));
                    var text = sb.ToString();
                    var hasChallenge = text.IndexOf("\"kind\":\"authChallenge\"", StringComparison.OrdinalIgnoreCase) >= 0
                        || text.IndexOf("\"kind\": \"authChallenge\"", StringComparison.OrdinalIgnoreCase) >= 0;
                    var hasPong = text.IndexOf("\"kind\":\"discoverPong\"", StringComparison.OrdinalIgnoreCase) >= 0
                        || text.IndexOf("\"kind\": \"discoverPong\"", StringComparison.OrdinalIgnoreCase) >= 0;
                    // authChallenge may arrive first when pairing is on (before/alongside pong).
                    if (hasChallenge) return KaiProbe.PairingRequired;
                    if (hasPong) return KaiProbe.Open;
                    if (text.IndexOf('\n') >= 0)
                        return KaiProbe.Fail;
                }
                return KaiProbe.Fail;
            }
            catch
            {
                return KaiProbe.Fail;
            }
        }

        private static List<string> BuildTargetList()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            var priority = new List<string>();
            var rest = new List<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        var bytes = ua.Address.GetAddressBytes();
                        if (bytes.Length != 4) continue;
                        if (bytes[0] == 169 && bytes[1] == 254) continue;
                        var selfIp = ua.Address.ToString();
                        if (IsLoopback(selfIp)) continue;

                        if (set.Add(selfIp))
                            priority.Add(selfIp);

                        var prefixLen = 24;
                        try
                        {
                            if (ua.IPv4Mask != null)
                            {
                                prefixLen = MaskToPrefix(ua.IPv4Mask.GetAddressBytes());
                                if (prefixLen < 24) prefixLen = 24;
                                if (prefixLen > 30) prefixLen = 24;
                            }
                        }
                        catch { }

                        foreach (var ip in EnumerateSubnet(bytes, prefixLen))
                        {
                            if (IsLoopback(ip)) continue;
                            if (set.Add(ip))
                                rest.Add(ip);
                        }
                    }
                }
            }
            catch { }

            var list = new List<string>(priority.Count + rest.Count + 1);
            if (set.Add("127.0.0.1"))
                list.Add("127.0.0.1");
            list.AddRange(priority);
            list.AddRange(rest);
            return list;
        }

        private static bool IsLoopback(string ip)
        {
            return string.Equals(ip, "127.0.0.1", StringComparison.OrdinalIgnoreCase);
        }

        private static int MaskToPrefix(byte[] mask)
        {
            var n = 0;
            foreach (var b in mask)
            {
                for (var i = 7; i >= 0; i--)
                {
                    if (((b >> i) & 1) == 1) n++;
                    else return n;
                }
            }
            return n;
        }

        private static List<string> EnumerateSubnet(byte[] addr, int prefixLen)
        {
            var list = new List<string>();
            if (addr == null || addr.Length != 4) return list;
            uint ip = ((uint)addr[0] << 24) | ((uint)addr[1] << 16) | ((uint)addr[2] << 8) | addr[3];
            uint mask = prefixLen == 0 ? 0u : uint.MaxValue << (32 - prefixLen);
            uint network = ip & mask;
            uint broadcast = network | ~mask;
            for (uint h = network + 1; h < broadcast; h++)
            {
                list.Add(string.Format("{0}.{1}.{2}.{3}",
                    (h >> 24) & 0xff, (h >> 16) & 0xff, (h >> 8) & 0xff, h & 0xff));
            }
            return list;
        }
    }
}
