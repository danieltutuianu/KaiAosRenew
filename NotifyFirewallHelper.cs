using System;
using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;

namespace KaiAosRenew
{
    /// <summary>
    /// Windows Firewall inbound rules for the notify TCP/UDP port (1-click with UAC).
    /// </summary>
    internal static class NotifyFirewallHelper
    {
        public const string ArgAllow = "--allow-notify-firewall";
        public const string RuleNameTcp = "KAI - AOS Renew Notify (TCP)";
        public const string RuleNameUdp = "KAI - AOS Renew Notify (UDP)";
        private const string FirewallRulesKey =
            @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules";

        public sealed class Result
        {
            public bool Ok { get; set; }
            public bool NeedsElevation { get; set; }
            public string Message { get; set; }
        }

        public static bool IsAdministrator()
        {
            try
            {
                using (var id = WindowsIdentity.GetCurrent())
                {
                    var principal = new WindowsPrincipal(id);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool IsPortAllowed(int port)
        {
            if (port < 1 || port > 65535) return false;
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(FirewallRulesKey, false))
                {
                    if (key == null) return false;
                    var names = key.GetValueNames();
                    if (names == null) return false;
                    var tcpOk = false;
                    var udpOk = false;
                    foreach (var name in names)
                    {
                        var raw = key.GetValue(name) as string;
                        if (string.IsNullOrEmpty(raw)) continue;
                        if (!RuleMatches(raw, RuleNameTcp, "TCP", port) && !RuleMatches(raw, RuleNameUdp, "UDP", port))
                            continue;
                        if (RuleMatches(raw, RuleNameTcp, "TCP", port))
                            tcpOk = true;
                        if (RuleMatches(raw, RuleNameUdp, "UDP", port))
                            udpOk = true;
                        if (tcpOk && udpOk) return true;
                    }
                    return tcpOk && udpOk;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool RuleMatches(string raw, string displayName, string protocol, int port)
        {
            if (string.IsNullOrEmpty(raw)) return false;
            // FirewallRules value: v2.30|Action=Allow|Active=TRUE|Dir=In|Protocol=6|LPort=17890|Name=...|...
            if (raw.IndexOf("Action=Allow", StringComparison.OrdinalIgnoreCase) < 0) return false;
            if (raw.IndexOf("Active=TRUE", StringComparison.OrdinalIgnoreCase) < 0) return false;
            if (raw.IndexOf("Dir=In", StringComparison.OrdinalIgnoreCase) < 0) return false;
            if (raw.IndexOf("Name=" + displayName, StringComparison.OrdinalIgnoreCase) < 0) return false;
            if (string.Equals(protocol, "TCP", StringComparison.OrdinalIgnoreCase))
            {
                if (raw.IndexOf("Protocol=6", StringComparison.OrdinalIgnoreCase) < 0) return false;
            }
            else if (string.Equals(protocol, "UDP", StringComparison.OrdinalIgnoreCase))
            {
                if (raw.IndexOf("Protocol=17", StringComparison.OrdinalIgnoreCase) < 0) return false;
            }
            var token = "LPort=" + port;
            var idx = raw.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return false;
            var after = idx + token.Length;
            return after >= raw.Length || raw[after] == '|';
        }

        public static Result AllowPort(int port)
        {
            if (port < 1024 || port > 65535)
                port = NotifyServer.DefaultPort;
            if (!IsAdministrator())
            {
                return new Result
                {
                    Ok = false,
                    NeedsElevation = true,
                    Message = "Administrator rights required."
                };
            }
            try
            {
                DeleteRuleByName(RuleNameTcp);
                DeleteRuleByName(RuleNameUdp);
                RunNetsh("advfirewall firewall add rule name=\"" + RuleNameTcp
                    + "\" dir=in action=allow protocol=TCP localport=" + port
                    + " profile=domain,private remoteip=localsubnet enable=yes");
                RunNetsh("advfirewall firewall add rule name=\"" + RuleNameUdp
                    + "\" dir=in action=allow protocol=UDP localport=" + port
                    + " profile=domain,private remoteip=localsubnet enable=yes");
                var ok = IsPortAllowed(port);
                return new Result
                {
                    Ok = ok,
                    NeedsElevation = false,
                    Message = ok
                        ? "Windows Firewall allows TCP/UDP port " + port + "."
                        : "Firewall rule was added but could not be verified."
                };
            }
            catch (Exception ex)
            {
                return new Result
                {
                    Ok = false,
                    NeedsElevation = false,
                    Message = ex.Message ?? "Firewall update failed."
                };
            }
        }

        public static Result AllowPortElevated(int port)
        {
            if (port < 1024 || port > 65535)
                port = NotifyServer.DefaultPort;
            if (IsAdministrator())
                return AllowPort(port);
            try
            {
                var exe = Process.GetCurrentProcess().MainModule.FileName;
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = ArgAllow + " " + port,
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null)
                    {
                        return new Result
                        {
                            Ok = false,
                            NeedsElevation = true,
                            Message = "Could not start elevated process."
                        };
                    }
                    p.WaitForExit(60000);
                    var ok = p.HasExited && p.ExitCode == 0;
                    return new Result
                    {
                        Ok = ok,
                        NeedsElevation = false,
                        Message = ok
                            ? "Windows Firewall allows TCP/UDP port " + port + "."
                            : (p.HasExited
                                ? "Elevated firewall update exit " + p.ExitCode
                                : "Elevated firewall update timed out.")
                    };
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return new Result
                {
                    Ok = false,
                    NeedsElevation = true,
                    Message = "UAC cancelled."
                };
            }
            catch (Exception ex)
            {
                return new Result
                {
                    Ok = false,
                    NeedsElevation = false,
                    Message = ex.Message ?? "Elevated firewall update failed."
                };
            }
        }

        private static void DeleteRuleByName(string name)
        {
            try
            {
                RunNetsh("advfirewall firewall delete rule name=\"" + name + "\"");
            }
            catch { }
        }

        private static void RunNetsh(string args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var p = Process.Start(psi))
            {
                if (p == null) throw new InvalidOperationException("netsh failed to start.");
                p.WaitForExit(30000);
                if (!p.HasExited || p.ExitCode != 0)
                {
                    var err = "";
                    try { err = p.StandardError.ReadToEnd(); }
                    catch { }
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(err)
                            ? ("netsh exit " + (p.HasExited ? p.ExitCode.ToString() : "timeout"))
                            : err.Trim());
                }
            }
        }
    }
}
