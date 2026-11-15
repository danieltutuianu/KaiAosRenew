using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;

namespace KaiAosNotifyClient
{
    /// <summary>Scan local /24 subnets for a TCP notify port (skips only 127.0.0.1).</summary>
    internal static class LanServerDiscovery
    {
        private const int ConnectTimeoutMs = 500;
        private const int MaxParallel = 48;

        public static string FindFirstOpenHost(int port, CancellationToken cancel)
        {
            if (port < 1 || port > 65535) port = 17890;

            var targets = BuildTargetList();
            if (targets.Count == 0) return null;

            // Probe this PC's LAN IPs first (server often on same machine), then rest of subnet.
            string found = null;
            var gate = new object();
            var index = 0;
            var workers = Math.Min(MaxParallel, targets.Count);
            var remaining = workers;
            using (var done = new ManualResetEvent(false))
            {
                for (var w = 0; w < workers; w++)
                {
                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        try
                        {
                            while (!cancel.IsCancellationRequested)
                            {
                                string ip;
                                lock (gate)
                                {
                                    if (found != null) return;
                                    if (index >= targets.Count) return;
                                    ip = targets[index++];
                                }
                                if (TryConnect(ip, port, ConnectTimeoutMs))
                                {
                                    lock (gate)
                                    {
                                        if (found == null)
                                            found = ip;
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
                done.WaitOne(45000);
            }
            return found;
        }

        private static bool TryConnect(string ip, int port, int timeoutMs)
        {
            Socket sock = null;
            try
            {
                sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                sock.Blocking = false;
                try
                {
                    sock.Connect(IPAddress.Parse(ip), port);
                }
                catch (SocketException ex)
                {
                    if (ex.SocketErrorCode != SocketError.WouldBlock
                        && ex.SocketErrorCode != SocketError.InProgress
                        && ex.SocketErrorCode != SocketError.IsConnected)
                        return false;
                }

                var sw = Environment.TickCount;
                while (Environment.TickCount - sw < timeoutMs)
                {
                    if (PollWritableOrError(sock, 50))
                    {
                        try
                        {
                            if (sock.Connected)
                                return true;
                            var err = (int)sock.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Error);
                            return err == 0 && sock.Poll(0, SelectMode.SelectWrite);
                        }
                        catch
                        {
                            return false;
                        }
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (sock != null)
                {
                    try { sock.Close(); }
                    catch { }
                }
            }
        }

        private static bool PollWritableOrError(Socket sock, int ms)
        {
            try
            {
                return sock.Poll(ms * 1000, SelectMode.SelectWrite)
                    || sock.Poll(0, SelectMode.SelectError);
            }
            catch
            {
                return true;
            }
        }

        private static List<string> BuildTargetList()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            var priority = new List<string>(); // local NIC IPs first
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
                        if (bytes[0] == 169 && bytes[1] == 254) continue; // APIPA
                        var selfIp = ua.Address.ToString();
                        if (IsLoopback(selfIp)) continue; // skip only 127.0.0.1

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

            var list = new List<string>(priority.Count + rest.Count);
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
