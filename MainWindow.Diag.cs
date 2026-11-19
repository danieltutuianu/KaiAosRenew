using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace KaiAosRenew
{
    public partial class MainWindow : Window
    {
        private DateTime? _lastTokenHealthAlertUtc;
        private DateTime? _lastSqliteMissingAlertUtc;
        private int _diagBundleBusy;

        private void MaybeAlertTokenExpiry()
        {
            if (_settings == null) return;
            var now = DateTime.UtcNow;
            var cooldownActive = _settings.AuthCooldownUntilUtc.HasValue
                && _settings.AuthCooldownUntilUtc.Value > now;
            if (!HealthAlertLogic.ShouldAlertTokenExpiry(
                now,
                _settings.LastTokenExpiryUtc,
                _settings.ConsecutiveFailures,
                cooldownActive,
                _settings.AuthRegisterBlocked,
                _lastTokenHealthAlertUtc,
                HealthAlertLogic.TokenExpiryWindow,
                HealthAlertLogic.AlertThrottle))
                return;

            _lastTokenHealthAlertUtc = now;
            var expiryLocal = _settings.LastTokenExpiryUtc.HasValue
                ? _settings.LastTokenExpiryUtc.Value.ToLocalTime().ToString("g")
                : "?";
            var reasons = HealthAlertLogic.DescribeReasons(
                _settings.ConsecutiveFailures, cooldownActive, _settings.AuthRegisterBlocked);
            var msg = string.Format(UiStrings.T("TokenExpiryAlert"), expiryLocal, reasons);
            AppendLog(msg, LogLevel.Warning);
            try
            {
                TrayTipWindow.ShowTip(
                    UiStrings.T("WindowTitle"),
                    msg,
                    TrayTipLevel.Warning);
            }
            catch { }
        }

        private void MaybeAlertSqliteMissing()
        {
            if (_settings == null) return;
            var path = string.IsNullOrWhiteSpace(_settings.KeysSqlitePath)
                ? KeyActivityMonitor.DefaultSqlitePath
                : _settings.KeysSqlitePath.Trim();
            if (File.Exists(path)) return;
            var now = DateTime.UtcNow;
            if (_lastSqliteMissingAlertUtc.HasValue
                && now - _lastSqliteMissingAlertUtc.Value < HealthAlertLogic.AlertThrottle)
                return;
            _lastSqliteMissingAlertUtc = now;
            var msg = string.Format(UiStrings.T("SqliteMissingAlert"), path);
            AppendLog(msg, LogLevel.Warning);
        }

        private void DiagBundle_Click(object sender, RoutedEventArgs e)
        {
            Forget(BuildAndSaveDiagBundleAsync(null), "DiagBundle");
        }

        /// <summary>Build a local diagnostic zip (redacted). When <paramref name="clientId"/> is set, stream it to that client as getZip|diag.</summary>
        private async Task BuildAndSaveDiagBundleAsync(int? clientId)
        {
            if (System.Threading.Interlocked.CompareExchange(ref _diagBundleBusy, 1, 0) != 0)
            {
                AppendLog(UiStrings.T("DiagBundleBusy"), LogLevel.Info);
                return;
            }
            string stageDir = null;
            string tempZip = null;
            try
            {
                AppendLog(UiStrings.T("DiagBundleBusy"), LogLevel.Info);
                stageDir = Path.Combine(Path.GetTempPath(), "kai-aos-diag-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(stageDir);

                await Task.Run(() => StageDiagBundleFiles(stageDir)).ConfigureAwait(true);

                tempZip = Path.Combine(Path.GetTempPath(),
                    "kai-aos-diag-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".zip");
                if (File.Exists(tempZip)) File.Delete(tempZip);
                ZipFile.CreateFromDirectory(stageDir, tempZip, CompressionLevel.Optimal, false);

                if (clientId.HasValue)
                {
                    var bytes = File.ReadAllBytes(tempZip);
                    if (bytes.Length > MaxFolderZipBytes)
                    {
                        SendAosAck(clientId.Value, false, "Zip too large");
                        return;
                    }
                    var name = "kai-aos-diag-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip";
                    SendFolderZipChunked(clientId.Value, name, bytes);
                    SendAosAck(clientId.Value, true, "getZip|diag");
                    AppendLog(string.Format(UiStrings.T("DiagBundleDone"), name), LogLevel.Success);
                    return;
                }

                var dlg = new SaveFileDialog
                {
                    Title = UiStrings.T("DiagBundle"),
                    Filter = "ZIP (*.zip)|*.zip",
                    FileName = "kai-aos-diag-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip"
                };
                if (dlg.ShowDialog(this) != true) return;
                File.Copy(tempZip, dlg.FileName, true);
                AppendLog(string.Format(UiStrings.T("DiagBundleDone"), dlg.FileName), LogLevel.Success);
            }
            catch (Exception ex)
            {
                AppendLog(string.Format(UiStrings.T("DiagBundleFail"), ex.Message), LogLevel.Error);
                if (clientId.HasValue)
                {
                    try { SendAosAck(clientId.Value, false, ex.Message); }
                    catch { }
                }
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _diagBundleBusy, 0);
                if (!string.IsNullOrEmpty(tempZip))
                {
                    try { File.Delete(tempZip); }
                    catch { }
                }
                if (!string.IsNullOrEmpty(stageDir))
                {
                    try { Directory.Delete(stageDir, true); }
                    catch { }
                }
            }
        }

        private void StageDiagBundleFiles(string stageDir)
        {
            // Redacted settings
            try
            {
                var raw = File.Exists(AppSettings.SettingsPath)
                    ? File.ReadAllText(AppSettings.SettingsPath, Encoding.UTF8)
                    : "";
                File.WriteAllText(Path.Combine(stageDir, "settings.redacted.xml"),
                    DiagBundleLogic.RedactSettingsXml(raw), Encoding.UTF8);
            }
            catch { }

            // Log tails
            CopyLogTail(AppSettings.LogPath, Path.Combine(stageDir, "renew.log.tail.txt"));
            try
            {
                var browserLog = Path.Combine(
                    Path.GetDirectoryName(AppSettings.LogPath) ?? "",
                    "browser-pass-check.log");
                CopyLogTail(browserLog, Path.Combine(stageDir, "browser-pass-check.log.tail.txt"));
            }
            catch { }
            try
            {
                var bmwLog = Path.Combine(
                    Path.GetDirectoryName(AppSettings.LogPath) ?? "",
                    "bmw-http.log");
                CopyLogTail(bmwLog, Path.Combine(stageDir, "bmw-http.log.tail.txt"));
            }
            catch { }

            // Key events (no secrets)
            try
            {
                var eventsPath = Path.Combine(
                    Path.GetDirectoryName(AppSettings.SettingsPath) ?? "",
                    "key-events.xml");
                if (File.Exists(eventsPath))
                    File.Copy(eventsPath, Path.Combine(stageDir, "key-events.xml"), true);
            }
            catch { }

            // Services + app info
            var info = new List<KeyValuePair<string, string>>();
            try
            {
                var ver = Assembly.GetExecutingAssembly().GetName().Version;
                info.Add(new KeyValuePair<string, string>("AppVersion", ver != null ? ver.ToString() : "?"));
            }
            catch { info.Add(new KeyValuePair<string, string>("AppVersion", "?")); }
            if (_settings != null)
            {
                info.Add(new KeyValuePair<string, string>("Email", DiagBundleLogic.MaskEmail(_settings.Email)));
                info.Add(new KeyValuePair<string, string>("LastTokenExpiryUtc",
                    _settings.LastTokenExpiryUtc.HasValue ? _settings.LastTokenExpiryUtc.Value.ToString("o") : ""));
                info.Add(new KeyValuePair<string, string>("ConsecutiveFailures",
                    _settings.ConsecutiveFailures.ToString()));
                info.Add(new KeyValuePair<string, string>("AuthRegisterBlocked",
                    _settings.AuthRegisterBlocked ? "1" : "0"));
                info.Add(new KeyValuePair<string, string>("AuthCooldownUntilUtc",
                    _settings.AuthCooldownUntilUtc.HasValue ? _settings.AuthCooldownUntilUtc.Value.ToString("o") : ""));
                info.Add(new KeyValuePair<string, string>("LastMessage",
                    DiagBundleLogic.ScrubLogText(_settings.LastMessage ?? "")));
            }
            try
            {
                var report = ServiceMonitor.Check();
                info.Add(new KeyValuePair<string, string>("BmwServices",
                    report != null ? (report.UiText ?? "") : ""));
            }
            catch (Exception ex)
            {
                info.Add(new KeyValuePair<string, string>("BmwServices", ex.Message ?? "fail"));
            }
            File.WriteAllText(Path.Combine(stageDir, "diag-info.txt"),
                DiagBundleLogic.BuildInfoText(info), Encoding.UTF8);
        }

        private static void CopyLogTail(string source, string dest)
        {
            if (string.IsNullOrEmpty(source) || !File.Exists(source)) return;
            try
            {
                using (var fs = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var len = fs.Length;
                    var take = (int)Math.Min(len, DiagBundleLogic.DefaultLogTailBytes);
                    if (take <= 0) return;
                    fs.Seek(-take, SeekOrigin.End);
                    var buf = new byte[take];
                    var n = fs.Read(buf, 0, take);
                    var text = Encoding.UTF8.GetString(buf, 0, n);
                    File.WriteAllText(dest, DiagBundleLogic.ScrubLogText(text), Encoding.UTF8);
                }
            }
            catch { }
        }
    }
}
