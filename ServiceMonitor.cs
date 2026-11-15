using System;
using System.Collections.Generic;
using System.Management;
using System.ServiceProcess;
using System.Text;

namespace KaiAosRenew
{
    internal sealed class BmwServiceStatus
    {
        public string ServiceName { get; set; }
        public string DisplayName { get; set; }
        public bool Exists { get; set; }
        public bool IsRunning { get; set; }
        public bool IsAutomatic { get; set; }
        public string StatusText { get; set; }
        public string StartTypeText { get; set; }
        public bool Ok { get { return Exists && IsRunning && IsAutomatic; } }
        public string ProblemSummary { get; set; }
    }

    internal sealed class BmwServicesReport
    {
        public List<BmwServiceStatus> Services { get; set; }
        public bool AllOk { get; set; }
        public string UiText { get; set; }
        public string AlertMessage { get; set; }
        /// <summary>Stable signature for anti-spam (problems only).</summary>
        public string ProblemSignature { get; set; }

        public BmwServicesReport()
        {
            Services = new List<BmwServiceStatus>();
        }
    }

    internal sealed class BmwServiceActionResult
    {
        public bool Ok { get; set; }
        public bool NeedsElevation { get; set; }
        public string Message { get; set; }
    }

    internal static class ServiceMonitor
    {
        public const string ArgFix = "--fix-bmw-services";
        public const string ArgStop = "--stop-bmw-services";

        public static readonly string[] WatchedServiceNames = new string[]
        {
            "BMW KAI BDR Services",
            "BMW Key Uploader BDR Service"
        };

        public static BmwServicesReport Check()
        {
            var report = new BmwServicesReport();
            var ui = new StringBuilder();
            var alert = new StringBuilder();
            var sig = new StringBuilder();
            var allOk = true;

            foreach (var name in WatchedServiceNames)
            {
                var s = ReadOne(name);
                report.Services.Add(s);
                ui.AppendLine(FormatLine(s));
                if (!s.Ok)
                {
                    allOk = false;
                    if (alert.Length > 0) alert.Append(" | ");
                    alert.Append(s.ProblemSummary ?? (s.DisplayName + " not OK"));
                    sig.Append(s.ServiceName).Append('=');
                    if (!s.Exists) sig.Append("missing");
                    else sig.Append(s.StatusText).Append('/').Append(s.StartTypeText);
                    sig.Append(';');
                }
            }

            report.AllOk = allOk;
            report.UiText = ui.ToString().TrimEnd();
            report.AlertMessage = allOk ? null : alert.ToString();
            report.ProblemSignature = allOk ? "" : sig.ToString();
            return report;
        }

        public static BmwServiceActionResult TryFixAll()
        {
            var sb = new StringBuilder();
            var anyFail = false;
            var needsElevation = false;

            foreach (var name in WatchedServiceNames)
            {
                var one = TryFixOne(name);
                if (!string.IsNullOrEmpty(one.Message))
                {
                    if (sb.Length > 0) sb.Append(" | ");
                    sb.Append(one.Message);
                }
                if (!one.Ok)
                {
                    anyFail = true;
                    if (one.NeedsElevation) needsElevation = true;
                }
            }

            var report = Check();
            if (report.AllOk)
            {
                return new BmwServiceActionResult
                {
                    Ok = true,
                    NeedsElevation = false,
                    Message = "BMW services fixed (Automatic + Running)."
                };
            }

            return new BmwServiceActionResult
            {
                Ok = !anyFail && report.AllOk,
                NeedsElevation = needsElevation,
                Message = string.IsNullOrEmpty(sb.ToString()) ? report.AlertMessage : sb.ToString()
            };
        }

        public static BmwServiceActionResult TryStopAll()
        {
            var sb = new StringBuilder();
            var anyFail = false;
            var needsElevation = false;

            foreach (var name in WatchedServiceNames)
            {
                var one = TryStopOne(name);
                if (!string.IsNullOrEmpty(one.Message))
                {
                    if (sb.Length > 0) sb.Append(" | ");
                    sb.Append(one.Message);
                }
                if (!one.Ok)
                {
                    anyFail = true;
                    if (one.NeedsElevation) needsElevation = true;
                }
            }

            return new BmwServiceActionResult
            {
                Ok = !anyFail,
                NeedsElevation = needsElevation,
                Message = sb.Length > 0 ? sb.ToString() : "BMW services stopped."
            };
        }

        private static BmwServiceActionResult TryFixOne(string serviceName)
        {
            var status = ReadOne(serviceName);
            if (!status.Exists)
            {
                return new BmwServiceActionResult
                {
                    Ok = false,
                    Message = status.DisplayName + " missing"
                };
            }

            try
            {
                if (!status.IsAutomatic)
                {
                    SetStartModeAutomatic(serviceName);
                }

                using (var sc = new ServiceController(serviceName))
                {
                    sc.Refresh();
                    if (sc.Status != ServiceControllerStatus.Running
                        && sc.Status != ServiceControllerStatus.StartPending)
                    {
                        sc.Start();
                        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(45));
                    }
                }

                return new BmwServiceActionResult
                {
                    Ok = true,
                    Message = status.DisplayName + " OK"
                };
            }
            catch (Exception ex)
            {
                return FailWithElevationHint(status.DisplayName, ex);
            }
        }

        private static BmwServiceActionResult TryStopOne(string serviceName)
        {
            var status = ReadOne(serviceName);
            if (!status.Exists)
            {
                return new BmwServiceActionResult
                {
                    Ok = true,
                    Message = status.DisplayName + " missing (skip)"
                };
            }

            try
            {
                using (var sc = new ServiceController(serviceName))
                {
                    sc.Refresh();
                    if (sc.Status == ServiceControllerStatus.Stopped
                        || sc.Status == ServiceControllerStatus.StopPending)
                    {
                        return new BmwServiceActionResult
                        {
                            Ok = true,
                            Message = status.DisplayName + " already stopped"
                        };
                    }
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(45));
                }
                return new BmwServiceActionResult
                {
                    Ok = true,
                    Message = status.DisplayName + " stopped"
                };
            }
            catch (Exception ex)
            {
                return FailWithElevationHint(status.DisplayName, ex);
            }
        }

        private static void SetStartModeAutomatic(string serviceName)
        {
            // Escape single quotes for WMI path
            var path = "Win32_Service.Name='" + serviceName.Replace("'", "\\'") + "'";
            using (var mo = new ManagementObject(path))
            {
                mo.Get();
                var result = mo.InvokeMethod("ChangeStartMode", new object[] { "Automatic" });
                var code = result == null ? -1 : Convert.ToInt32(result);
                // 0 = Success, 22 = Already set (AcceptPause etc.) — treat 0 as OK
                if (code != 0)
                    throw new InvalidOperationException("ChangeStartMode returned " + code + " for " + serviceName);
            }
        }

        private static BmwServiceActionResult FailWithElevationHint(string displayName, Exception ex)
        {
            var msg = ex.Message ?? "";
            var elevate = IsAccessDenied(ex);
            return new BmwServiceActionResult
            {
                Ok = false,
                NeedsElevation = elevate,
                Message = displayName + ": " + msg
            };
        }

        private static bool IsAccessDenied(Exception ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
            {
                var m = e.Message ?? "";
                if (m.IndexOf("denied", StringComparison.OrdinalIgnoreCase) >= 0
                    || m.IndexOf("privilege", StringComparison.OrdinalIgnoreCase) >= 0
                    || m.IndexOf("Access is denied", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                var ue = e as UnauthorizedAccessException;
                if (ue != null) return true;
                var win = e as System.ComponentModel.Win32Exception;
                if (win != null && (win.NativeErrorCode == 5 || win.NativeErrorCode == 1300))
                    return true;
            }
            // WMI often returns ManagementException with "Access denied"
            if (ex is ManagementException) return true;
            return false;
        }

        private static string FormatLine(BmwServiceStatus s)
        {
            if (!s.Exists)
                return s.DisplayName + ": missing";
            return s.DisplayName + ": " + s.StatusText + " | " + s.StartTypeText;
        }

        private static BmwServiceStatus ReadOne(string serviceName)
        {
            var info = new BmwServiceStatus();
            info.ServiceName = serviceName;
            info.DisplayName = serviceName;
            try
            {
                using (var sc = new ServiceController(serviceName))
                {
                    info.Exists = true;
                    try { info.DisplayName = sc.DisplayName; } catch { }
                    info.IsRunning = sc.Status == ServiceControllerStatus.Running;
                    info.StatusText = sc.Status.ToString();
                    var start = GetStartType(sc);
                    info.IsAutomatic = start == ServiceStartMode.Automatic;
                    info.StartTypeText = StartTypeLabel(start);

                    if (!info.IsRunning && !info.IsAutomatic)
                        info.ProblemSummary = info.DisplayName + " is " + info.StatusText + " and start type is " + info.StartTypeText;
                    else if (!info.IsRunning)
                        info.ProblemSummary = info.DisplayName + " is not running (" + info.StatusText + ")";
                    else if (!info.IsAutomatic)
                        info.ProblemSummary = info.DisplayName + " start type is " + info.StartTypeText + " (expected Automatic)";
                }
            }
            catch (InvalidOperationException)
            {
                info.Exists = false;
                info.StatusText = "Missing";
                info.StartTypeText = "—";
                info.ProblemSummary = info.DisplayName + " service not installed";
            }
            catch (Exception ex)
            {
                info.Exists = false;
                info.StatusText = "Error";
                info.StartTypeText = "—";
                info.ProblemSummary = info.DisplayName + ": " + ex.Message;
            }
            return info;
        }

        private static ServiceStartMode GetStartType(ServiceController sc)
        {
            try
            {
                return sc.StartType;
            }
            catch
            {
                return ServiceStartMode.Manual;
            }
        }

        private static string StartTypeLabel(ServiceStartMode mode)
        {
            switch (mode)
            {
                case ServiceStartMode.Automatic:
                    return "Automatic";
                case ServiceStartMode.Manual:
                    return "Manual";
                case ServiceStartMode.Disabled:
                    return "Disabled";
                default:
                    return mode.ToString();
            }
        }
    }
}
