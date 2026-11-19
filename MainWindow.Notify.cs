using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Velopack;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;
using DiagProcess = System.Diagnostics.Process;

namespace KaiAosRenew
{
    public partial class MainWindow : Window
    {

        private void BroadcastUpdateFeed()
        {
            if (_settings == null || !_settings.NotifyServerEnabled) return;
            if (!NotifyServer.Instance.IsRunning) return;
            if (string.IsNullOrEmpty(_feedServerVer) && string.IsNullOrEmpty(_feedClientVer)) return;
            BroadcastNotify(new NotifyMessage
            {
                Kind = "updateFeed",
                Level = "info",
                Title = "KAI - AOS Renew",
                Text = NotifyMessage.EncodeUpdateFeed(_feedServerVer, _feedClientVer)
            });
        }

        private void SendUpdateFeedTo(int clientId, bool allowEmpty = false)
        {
            if (_settings == null || !_settings.NotifyServerEnabled) return;
            if (!NotifyServer.Instance.IsRunning) return;
            // Never push a blank wipe that clears a client's known-good feed.
            // allowEmpty only means "ok to reply after a refresh that found nothing"
            // when we truly have no cached versions — not "force empty over good".
            if (string.IsNullOrEmpty(_feedServerVer) && string.IsNullOrEmpty(_feedClientVer))
            {
                if (!allowEmpty) return;
                // Intentional empty reply (both unknown) — still skip; empty Encode
                // would only clobber client sticky offers. Prefer silence.
                return;
            }
            try
            {
                NotifyServer.Instance.SendTo(clientId, new NotifyMessage
                {
                    Kind = "updateFeed",
                    Level = "info",
                    Title = "KAI - AOS Renew",
                    Text = NotifyMessage.EncodeUpdateFeed(_feedServerVer, _feedClientVer)
                });
            }
            catch { }
        }

        private void BroadcastServerUpdating(string version)
        {
            BroadcastNotify(new NotifyMessage
            {
                Kind = "serverUpdating",
                Level = "info",
                Title = "KAI - AOS Renew",
                Text = version ?? ""
            });
        }

        private void BroadcastServerUpdateFailed(string error)
        {
            BroadcastNotify(new NotifyMessage
            {
                Kind = "serverUpdateFailed",
                Level = "error",
                Title = "KAI - AOS Renew",
                Text = error ?? ""
            });
        }

        private static void BroadcastNotify(NotifyMessage msg)
        {
            try { NotifyServer.Instance.Broadcast(msg); }
            catch { }
        }

        private void OnNotifyMessageFromClient(int clientId, NotifyMessage msg)
        {
            if (msg == null) return;
            var id = clientId;
            var copy = msg;
            Dispatcher.BeginInvoke(new Action(() => HandleClientNotify(id, copy)));
        }

        private void HandleClientNotify(int clientId, NotifyMessage msg)
        {
            if (msg == null) return;
            var kind = (msg.Kind ?? "").Trim().ToLowerInvariant();
            if (kind == "aosapply")
            {
                ApplyAosFromClient(clientId, msg.Text);
                return;
            }
            if (kind == "aosinterval")
            {
                ApplyAosIntervalFromClient(clientId, msg.Text);
                return;
            }
            if (kind == "aoscmd")
            {
                RunAosCmdFromClient(clientId, msg.Text);
                return;
            }
            if (kind == "serverupdate")
            {
                Forget(BeginServerUpdateFromClient(clientId), "ServerUpdateFromClient");
                return;
            }
            if (kind == "clientupdateallreq")
            {
                Forget(RequestUpdateAllClientsAsync(false, "client"), "RequestUpdateAllClients");
                return;
            }
            if (kind == "updatefeed")
            {
                Forget(HandleInboundUpdateFeedAsync(msg.Text), "HandleInboundUpdateFeed");
                return;
            }
            if (kind == "keysearchreq")
            {
                HandleKeySearchReq(clientId, msg.Text);
                return;
            }
            if (kind == "aossyncreq")
            {
                SendAosSyncTo(clientId);
                return;
            }
            if (kind == "settingssyncreq")
            {
                SendServerSettingsTo(clientId);
                return;
            }
            if (kind == "settingsexportreq")
            {
                SendServerSettingsExportTo(clientId);
                return;
            }
            if (kind == "settingsimport")
            {
                ImportServerSettingsFromClient(clientId, msg.Text);
                return;
            }
            if (kind == "settingsapply")
            {
                ApplyServerSettingsFromClient(clientId, msg.Text);
                return;
            }
            if (kind == "notifyclientsreq")
            {
                SendNotifyClientsListTo(clientId);
                return;
            }
            if (kind == "notifytest")
            {
                HandleNotifyTestFromClient(clientId, msg.Text);
                return;
            }
            if (kind == "updatefeedreq")
            {
                Forget(HandleUpdateFeedReqAsync(clientId), "HandleUpdateFeedReq");
                return;
            }
            if (kind == "clientupdating")
            {
                MarkClientUpdating(clientId, true, msg.Text);
                return;
            }
            if (kind == "clientupdatedone")
            {
                MarkClientUpdating(clientId, false, null);
                return;
            }
            if (kind == "clienthello")
            {
                NotifyServer.Instance.ApplyClientHello(clientId, msg.Text, msg.Detail);
                RefreshNotifyClientsCombo();
                BroadcastNotifyClientsList();
                return;
            }
            if (kind == "activitysyncreq")
            {
                try { _keyReaderLogTailer.PollNow(); }
                catch { }
                try { if (_uploaderLogTailer != null) _uploaderLogTailer.PollNow(); }
                catch { }
                try { if (_kaiLogTailer != null) _kaiLogTailer.PollNow(); }
                catch { }
                SeedActivityHistoryFromLog();
                RefreshKeysTab(false);
                SendActivitySyncTo(clientId);
                return;
            }
            if (kind == "registersyncreq")
            {
                LoadRegisterHistoryFromLog();
                SendRegisterSyncTo(clientId);
                return;
            }
            if (kind == "activityhistoryreq")
            {
                HandleActivityHistoryReq(clientId, msg);
                return;
            }
            if (kind == "registerhistoryreq")
            {
                HandleRegisterHistoryReq(clientId, msg);
                return;
            }
            if (kind == "activityclearreq")
            {
                ClearLog_Click(null, null);
                return;
            }
            if (kind == "registerclearreq")
            {
                ClearRegisterLog_Click(null, null);
                return;
            }
            if (kind == "activityexportreq")
            {
                HandleActivityExportReq(clientId, msg);
                return;
            }
            if (kind == "registerexportreq")
            {
                HandleRegisterExportReq(clientId, msg);
                return;
            }
        }

        private const int KeySearchReplyMax = 200;

        /// <summary>Client typed a Keys search: look through persisted history + key-bins and reply keySearchSync.</summary>
        private void HandleKeySearchReq(int clientId, string query)
        {
            var q = (query ?? "").Trim();
            if (q.Length == 0) return;
            if (q.Length > 64) q = q.Substring(0, 64);
            var monitor = _keyMonitor;
            if (monitor == null) return;
            Forget(Task.Run(() =>
            {
                List<KeyActivityEvent> found = null;
                try { found = monitor.SearchHistory(q, KeySearchReplyMax); }
                catch (Exception ex)
                {
                    try { RenewEngine.Log("Notify keySearch: " + ex.Message); }
                    catch { }
                }
                try
                {
                    // Title echoes the query so the client can drop stale replies.
                    NotifyServer.Instance.SendTo(clientId, new NotifyMessage
                    {
                        Kind = "keySearchSync",
                        Level = "info",
                        Title = q,
                        Text = NotifyKeySync.EncodeKeySync(found ?? new List<KeyActivityEvent>()) ?? ""
                    });
                }
                catch (Exception ex)
                {
                    try { RenewEngine.Log("Notify keySearch send: " + ex.Message); }
                    catch { }
                }
            }), "keySearchReq");
        }

        private void HandleActivityHistoryReq(int clientId, NotifyMessage msg)
        {
            var filter = (msg != null ? msg.Text : null) ?? "";
            filter = filter.Trim().ToLowerInvariant();
            var reqId = (msg != null ? msg.Title : null) ?? "";
            var search = (msg != null ? msg.Detail : null) ?? "";
            search = search.Trim();
            if (string.IsNullOrEmpty(filter) || filter == "all")
            {
                try
                {
                    NotifyServer.Instance.SendTo(clientId, new NotifyMessage
                    {
                        Kind = "activityHistory",
                        Level = "all",
                        Title = reqId,
                        Text = ""
                    });
                }
                catch { }
                return;
            }
            Task.Run(() =>
            {
                var rows = new List<LogEntry>();
                try { CollectFilteredActivityFromDisk(rows, ActivityFilteredMax, filter, search); }
                catch { }
                var wire = new List<ActivityWireRow>(rows.Count);
                for (var i = 0; i < rows.Count; i++)
                {
                    var e = rows[i];
                    if (e == null) continue;
                    wire.Add(new ActivityWireRow
                    {
                        At = e.Time,
                        Level = LogLevelWireName(e.Level),
                        Text = e.Text ?? ""
                    });
                }
                var payload = NotifyMessage.EncodeActivitySync(wire);
                try
                {
                    NotifyServer.Instance.SendTo(clientId, new NotifyMessage
                    {
                        Kind = "activityHistory",
                        Level = filter,
                        Title = reqId,
                        Text = payload ?? ""
                    });
                }
                catch { }
            });
        }

        private void HandleRegisterHistoryReq(int clientId, NotifyMessage msg)
        {
            var filter = (msg != null ? msg.Text : null) ?? "";
            filter = filter.Trim().ToLowerInvariant();
            var reqId = (msg != null ? msg.Title : null) ?? "";
            var search = (msg != null ? msg.Detail : null) ?? "";
            search = search.Trim();
            if (string.IsNullOrEmpty(filter) || filter == "all")
            {
                try
                {
                    NotifyServer.Instance.SendTo(clientId, new NotifyMessage
                    {
                        Kind = "registerHistory",
                        Level = "all",
                        Title = reqId,
                        Text = ""
                    });
                }
                catch { }
                return;
            }
            var filterRows = new List<ActivityRegisterLogic.RegisterHistoryFilterRow>(_registerHistory.Count);
            for (var i = 0; i < _registerHistory.Count; i++)
            {
                var r = _registerHistory[i];
                if (r == null) continue;
                filterRows.Add(new ActivityRegisterLogic.RegisterHistoryFilterRow
                {
                    At = r.At,
                    Kind = r.Kind,
                    Text = r.Text ?? "",
                    ResultText = r.ResultText ?? ""
                });
            }
            var list = ActivityRegisterLogic.FilterRegisterHistory(filterRows, filter, search, RegisterHistoryMax);
            if (list == null)
            {
                try
                {
                    NotifyServer.Instance.SendTo(clientId, new NotifyMessage
                    {
                        Kind = "registerHistory",
                        Level = "all",
                        Title = reqId,
                        Text = ""
                    });
                }
                catch { }
                return;
            }
            var payload = NotifyMessage.EncodeRegisterSync(list);
            try
            {
                NotifyServer.Instance.SendTo(clientId, new NotifyMessage
                {
                    Kind = "registerHistory",
                    Level = filter,
                    Title = reqId,
                    Text = payload ?? ""
                });
            }
            catch { }
        }

        private void HandleActivityExportReq(int clientId, NotifyMessage msg)
        {
            DateTime day;
            if (!TryParseExportDay(msg != null ? msg.Text : null, out day)) return;
            string body;
            try { body = CollectActivityLogForDay(day) ?? ""; }
            catch (Exception ex) { body = ex.Message ?? ""; }
            try
            {
                NotifyServer.Instance.SendTo(clientId, new NotifyMessage
                {
                    Kind = "activityExport",
                    Level = "info",
                    Title = "KAI - AOS Renew",
                    At = day.ToString("yyyy-MM-dd"),
                    Text = body
                });
            }
            catch { }
        }

        private void HandleRegisterExportReq(int clientId, NotifyMessage msg)
        {
            DateTime day;
            if (!TryParseExportDay(msg != null ? msg.Text : null, out day)) return;
            string body;
            try { body = CollectRegisterLogForDay(day) ?? ""; }
            catch (Exception ex) { body = ex.Message ?? ""; }
            try
            {
                NotifyServer.Instance.SendTo(clientId, new NotifyMessage
                {
                    Kind = "registerExport",
                    Level = "info",
                    Title = "KAI - AOS Renew",
                    At = day.ToString("yyyy-MM-dd"),
                    Text = body
                });
            }
            catch { }
        }

        private static bool TryParseExportDay(string text, out DateTime day)
        {
            return ActivityRegisterLogic.TryParseExportDay(text, out day);
        }

        private static string FormatNotifyClientLabel(string display, string version)
        {
            var label = string.IsNullOrWhiteSpace(display) ? "" : display.Trim();
            var ver = (version ?? "").Trim();
            if (ver.Length == 0) return label;
            if (ver.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                return label + " (" + ver + ")";
            return label + " (v" + ver + ")";
        }

        private async Task HandleUpdateFeedReqAsync(int clientId)
        {
            try { await RefreshUpdateFeedAsync(); }
            catch { }
            // Send only if we have something useful; never blank-wipe the client.
            SendUpdateFeedTo(clientId, false);
        }

        private void MarkClientUpdating(int clientId, bool updating, string version)
        {
            if (clientId <= 0) return;
            if (updating)
            {
                _clientsUpdating.Add(clientId);
                AppendLog(string.Format(UiStrings.T("ClientUpdateStarted"), clientId,
                    string.IsNullOrEmpty(version) ? "?" : version), LogLevel.Info);
            }
            else
            {
                _clientsUpdating.Remove(clientId);
            }
            RefreshNotifyClientsCombo();
        }

        private async Task BeginServerUpdateFromClient(int clientId)
        {
            try
            {
                var mgr = UpdateService.CreateManager();
                if (!mgr.IsInstalled)
                {
                    SendAosAck(clientId, false, UiStrings.T("UpdateNotInstalled"));
                    return;
                }
                var update = await UpdateService.PeekAsync();
                if (update == null)
                {
                    SendAosAck(clientId, false, UiStrings.T("UpdateNone"));
                    return;
                }
                await ApplySelfUpdateAsync(update, false, clientId,
                    string.Format(UiStrings.T("UpdateSourceClient"), clientId));
            }
            catch (Exception ex)
            {
                try { SendAosAck(clientId, false, ex.Message ?? "Update failed"); }
                catch { }
            }
        }

        private string BuildAosSyncPayload()
        {
            if (_settings == null) return NotifyMessage.EncodeAosSync(1, "", "", null, 3);
            var hours = _settings.IntervalHours < 1 ? 1 : _settings.IntervalHours;
            string pass = "";
            try { pass = _settings.GetPassword() ?? ""; }
            catch { pass = ""; }
            var blockAfter = _settings.AuthRegisterBlockAfter < 1 ? 3 : _settings.AuthRegisterBlockAfter;
            return NotifyMessage.EncodeAosSync(hours, _settings.Email ?? "", pass, null, blockAfter);
        }

        private void BroadcastAosSync()
        {
            if (_settings == null || !_settings.NotifyServerEnabled) return;
            try
            {
                BroadcastNotify(new NotifyMessage
                {
                    Kind = "aosSync",
                    Level = "info",
                    Title = "KAI - AOS Renew",
                    Text = BuildAosSyncPayload()
                });
            }
            catch { }
        }

        private void SendAosSyncTo(int clientId)
        {
            if (_settings == null || !_settings.NotifyServerEnabled) return;
            try
            {
                NotifyServer.Instance.SendTo(clientId, new NotifyMessage
                {
                    Kind = "aosSync",
                    Level = "info",
                    Title = "KAI - AOS Renew",
                    Text = BuildAosSyncPayload()
                });
            }
            catch { }
        }

        private void BroadcastServerSettings()
        {
            if (_settings == null || !_settings.NotifyServerEnabled) return;
            try
            {
                BroadcastNotify(new NotifyMessage
                {
                    Kind = "settingsSync",
                    Level = "info",
                    Title = "KAI - AOS Renew",
                    Text = BuildServerSettingsPayload()
                });
            }
            catch { }
        }

        private void SendServerSettingsTo(int clientId)
        {
            if (_settings == null || !_settings.NotifyServerEnabled) return;
            try
            {
                NotifyServer.Instance.SendTo(clientId, new NotifyMessage
                {
                    Kind = "settingsSync",
                    Level = "info",
                    Title = "KAI - AOS Renew",
                    Text = BuildServerSettingsPayload()
                });
            }
            catch { }
        }

        private void SendServerSettingsExportTo(int clientId)
        {
            if (_settings == null || !_settings.NotifyServerEnabled) return;
            try
            {
                NotifyServer.Instance.SendTo(clientId, new NotifyMessage
                {
                    Kind = "settingsExport",
                    Level = "info",
                    Title = "KAI - AOS Renew",
                    Text = _settings.ExportPortableToString() ?? ""
                });
            }
            catch { }
        }

        private void ImportServerSettingsFromClient(int clientId, string xml)
        {
            if (_settings == null)
            {
                SendAosAck(clientId, false, "No settings");
                return;
            }
            try
            {
                if (string.IsNullOrWhiteSpace(xml))
                {
                    SendAosAck(clientId, false, "Empty settings import");
                    return;
                }
                _settings.ImportPortableFromString(xml);
                _settings.Save();
                _loadingUi = true;
                try
                {
                    if (EmailBox != null) EmailBox.Text = _settings.Email ?? "";
                    var hours = _settings.IntervalHours <= 0 ? 1 : _settings.IntervalHours;
                    if (hours < 1) hours = 1;
                    if (hours > 168) hours = 168;
                    if (IntervalBox != null) IntervalBox.Text = hours.ToString();
                    if (ConfigPathBox != null) ConfigPathBox.Text = _settings.ConfigPath ?? "";
                    _lastValidConfigPath = _settings.ConfigPath;
                    if (StartWithWindowsCheck != null) StartWithWindowsCheck.IsChecked = _settings.StartWithWindows;
                    if (MinimizeToTrayCheck != null) MinimizeToTrayCheck.IsChecked = _settings.MinimizeToTray;
                    UpdateMinimizeToTrayEnabled();
                    if (MinimizeOnCloseCheck != null) MinimizeOnCloseCheck.IsChecked = _settings.MinimizeOnClose;
                    if (AutoUpdateCheck != null) AutoUpdateCheck.IsChecked = _settings.AutoUpdate;
                    if (UseSystemProxyCheck != null) UseSystemProxyCheck.IsChecked = _settings.UseSystemProxy;
                    if (BrowserPassHintCheck != null)
                        BrowserPassHintCheck.IsChecked = _settings.BrowserPasswordHintOnAuthFail;
                    if (KeysBalloonCheck != null)
                        KeysBalloonCheck.IsChecked = _settings.KeysBalloonEnabled;
                    if (KeysVinBmwFailOnlyCheck != null)
                    {
                        KeysVinBmwFailOnlyCheck.IsChecked = _settings.KeysBalloonEnabled && _settings.KeysVinTipBmwFailOnly;
                        SyncKeysVinBmwFailOnlyUi();
                    }
                    if (KeysStallAlertCheck != null)
                        KeysStallAlertCheck.IsChecked = _settings.KeysStallAlertEnabled;
                    if (KeysAutoCopyVinCheck != null)
                        KeysAutoCopyVinCheck.IsChecked = _settings.KeysAutoCopyVin;
                    _settings.LogVerbose = true;
                    if (KeysRefreshSecondsBox != null)
                        KeysRefreshSecondsBox.Text = _settings.KeysRefreshSeconds.ToString();
                    if (KeysVinTipSecondsBox != null)
                        KeysVinTipSecondsBox.Text = _settings.KeysVinTipSeconds.ToString();
                    if (KeysStallMinutesBox != null)
                        KeysStallMinutesBox.Text = _settings.KeysStallMinutes.ToString();
                    if (AuthCooldownMinutesBox != null)
                        AuthCooldownMinutesBox.Text = _settings.AuthCooldownMinutes.ToString();
                    if (AuthRegisterBlockAfterBox != null)
                        AuthRegisterBlockAfterBox.Text = GetAuthRegisterBlockAfter().ToString();
                    LoadKeyReaderPathsToUi();
                    LoadKeysDataPathsToUi();
                    var lang = string.IsNullOrWhiteSpace(_settings.Language) ? "en" : _settings.Language.Trim();
                    UiStrings.SetLanguage(lang);
                    if (LanguageCombo != null) LanguageCombo.SelectedValue = lang;
                    var theme = ThemeManager.NormalizeMode(_settings.ThemeMode);
                    _settings.ThemeMode = theme;
                    if (ThemeCombo != null) ThemeCombo.SelectedValue = theme;
                    ThemeManager.Apply(theme);
                    ApplyThemeChrome();
                    ApplyLanguage();
                    ApplyStartup(_settings.StartWithWindows);
                    ApplyKeysMonitorSettings();
                    RefreshAuthCooldownBanner();
                    RefreshPasswordHintBanners(false);
                }
                finally
                {
                    _loadingUi = false;
                }
                LoadFirebasePushToUi();
                ApplyFirebasePushFromSettings(true);
                AppendLog("Server settings imported from client " + clientId, LogLevel.Info);
                SendAosAck(clientId, true, "settingsImport");
                BroadcastServerSettings();
                BroadcastAosSync();
            }
            catch (Exception ex)
            {
                SendAosAck(clientId, false, ex.Message ?? "settingsImport failed");
            }
        }

        private void ApplyServerSettingsFromClient(int clientId, string payload)
        {
            if (_settings == null)
            {
                SendAosAck(clientId, false, "No settings");
                return;
            }
            try
            {
                var map = NotifyMessage.ParseKvMap(payload);
                if (map.Count == 0)
                {
                    SendAosAck(clientId, false, "Empty settings");
                    return;
                }

                _loadingUi = true;
                try
                {
                    _settings.Language = MapStr(map, "language", _settings.Language ?? "en");
                    _settings.ThemeMode = MapStr(map, "themeMode", _settings.ThemeMode ?? "system");
                    _settings.LogVerbose = true;
                    _settings.StartWithWindows = MapFlag(map, "startWithWindows", _settings.StartWithWindows);
                    _settings.MinimizeToTray = MapFlag(map, "minimizeToTray", _settings.MinimizeToTray);
                    _settings.MinimizeOnClose = MapFlag(map, "minimizeOnClose", _settings.MinimizeOnClose);
                    _settings.AutoUpdate = MapFlag(map, "autoUpdate", _settings.AutoUpdate);
                    _settings.KeysRefreshSeconds = MapInt(map, "keysRefreshSeconds", _settings.KeysRefreshSeconds, 1, 60);
                    _settings.KeysVinTipSeconds = MapInt(map, "keysVinTipSeconds", _settings.KeysVinTipSeconds, 3, 60);
                    _settings.KeysStallMinutes = MapInt(map, "keysStallMinutes", _settings.KeysStallMinutes, 1, 60);
                    _settings.KeysBalloonEnabled = MapFlag(map, "keysBalloon", _settings.KeysBalloonEnabled);
                    _settings.KeysVinTipBmwFailOnly = MapFlag(map, "keysVinBmwFailOnly", _settings.KeysVinTipBmwFailOnly);
                    if (!_settings.KeysBalloonEnabled)
                        _settings.KeysVinTipBmwFailOnly = false;
                    _settings.KeysAutoCopyVin = MapFlag(map, "keysAutoCopyVin", _settings.KeysAutoCopyVin);
                    _settings.KeysStallAlertEnabled = MapFlag(map, "keysStallAlert", _settings.KeysStallAlertEnabled);
                    _settings.BrowserPasswordHintOnAuthFail = MapFlag(map, "browserPassHint", _settings.BrowserPasswordHintOnAuthFail);
                    _settings.UseSystemProxy = MapFlag(map, "useSystemProxy", _settings.UseSystemProxy);
                    _settings.AuthCooldownMinutes = MapInt(map, "authCooldownMinutes", _settings.AuthCooldownMinutes, 5, 240);
                    _settings.AuthRegisterBlockAfter = MapInt(map, "authRegisterBlockAfter", GetAuthRegisterBlockAfter(), 1, 10);
                    if (map.ContainsKey("firebasePushEnabled")
                        || map.ContainsKey("firebasePushSyncMode")
                        || map.ContainsKey("firebasePushUsePolling")
                        || map.ContainsKey("firebasePushPollMinutes")
                        || map.ContainsKey("firebasePushUrl")
                        || map.ContainsKey("firebasePushSecret")
                        || map.ContainsKey("firebasePushServerName"))
                    {
                        _settings.FirebasePushEnabled = MapFlag(map, "firebasePushEnabled", _settings.FirebasePushEnabled);
                        if (map.ContainsKey("firebasePushSyncMode"))
                            _settings.FirebasePushSyncMode = PeerSyncLogic.NormalizeFirebasePushSyncMode(
                                MapStr(map, "firebasePushSyncMode", _settings.FirebasePushSyncMode ?? ""));
                        else if (map.ContainsKey("firebasePushUsePolling") && MapFlag(map, "firebasePushUsePolling", false))
                            _settings.FirebasePushSyncMode = PeerSyncLogic.SyncModePoll;
                        _settings.FirebasePushPollMinutes = MapInt(map, "firebasePushPollMinutes", _settings.FirebasePushPollMinutes, 1, 1440);
                        _settings.FirebasePushUrl = MapStr(map, "firebasePushUrl", _settings.FirebasePushUrl ?? "");
                        if (map.ContainsKey("firebasePushSecret"))
                            _settings.SetFirebasePushSecret(MapStr(map, "firebasePushSecret", ""));
                        _settings.FirebasePushServerName = MapStr(map, "firebasePushServerName", _settings.FirebasePushServerName ?? "");
                    }
                    _settings.NormalizeClamps();
                    _settings.Save();

                    // Apply to server UI
                    if (LanguageCombo != null) LanguageCombo.SelectedValue = _settings.Language;
                    if (ThemeCombo != null) ThemeCombo.SelectedValue = _settings.ThemeMode;
                    if (StartWithWindowsCheck != null) StartWithWindowsCheck.IsChecked = _settings.StartWithWindows;
                    if (MinimizeToTrayCheck != null) MinimizeToTrayCheck.IsChecked = _settings.MinimizeToTray;
                    if (MinimizeOnCloseCheck != null) MinimizeOnCloseCheck.IsChecked = _settings.MinimizeOnClose;
                    if (AutoUpdateCheck != null) AutoUpdateCheck.IsChecked = _settings.AutoUpdate;
                    if (KeysRefreshSecondsBox != null) KeysRefreshSecondsBox.Text = _settings.KeysRefreshSeconds.ToString();
                    if (KeysVinTipSecondsBox != null) KeysVinTipSecondsBox.Text = _settings.KeysVinTipSeconds.ToString();
                    if (KeysStallMinutesBox != null) KeysStallMinutesBox.Text = _settings.KeysStallMinutes.ToString();
                    if (KeysBalloonCheck != null) KeysBalloonCheck.IsChecked = _settings.KeysBalloonEnabled;
                    if (KeysVinBmwFailOnlyCheck != null) KeysVinBmwFailOnlyCheck.IsChecked = _settings.KeysVinTipBmwFailOnly;
                    if (KeysAutoCopyVinCheck != null) KeysAutoCopyVinCheck.IsChecked = _settings.KeysAutoCopyVin;
                    if (KeysStallAlertCheck != null) KeysStallAlertCheck.IsChecked = _settings.KeysStallAlertEnabled;
                    if (BrowserPassHintCheck != null) BrowserPassHintCheck.IsChecked = _settings.BrowserPasswordHintOnAuthFail;
                    if (UseSystemProxyCheck != null) UseSystemProxyCheck.IsChecked = _settings.UseSystemProxy;
                    if (AuthCooldownMinutesBox != null) AuthCooldownMinutesBox.Text = _settings.AuthCooldownMinutes.ToString();
                    if (AuthRegisterBlockAfterBox != null) AuthRegisterBlockAfterBox.Text = _settings.AuthRegisterBlockAfter.ToString();
                    LoadFirebasePushToUi();

                    ApplyStartup(_settings.StartWithWindows);
                    ApplyKeysMonitorSettings();
                    UiStrings.SetLanguage(_settings.Language);
                    ThemeManager.Apply(_settings.ThemeMode);
                    ApplyLanguage();
                    RefreshAuthCooldownBanner();
                    ApplyFirebasePushFromSettings(true);

                    if (map.ContainsKey("keyReaderLogLevel"))
                    {
                        var level = MapInt(map, "keyReaderLogLevel", 4, 0, 4);
                        try
                        {
                            var paths = DesktopKeyReaderReg.ReadAbsolute();
                            paths.LogLevel = level;
                            DesktopKeyReaderReg.WriteAbsolute(paths);
                            RefreshKeyReaderLogLevelCombo();
                            if (KeyReaderLogLevelCombo != null)
                                KeyReaderLogLevelCombo.SelectedValue = level.ToString();
                            StartKeyReaderLogTailer(paths);
                        }
                        catch (Exception ex)
                        {
                            AppendLog("KeyReader log level: " + ex.Message, LogLevel.Warning);
                        }
                    }
                }
                finally
                {
                    _loadingUi = false;
                }

                AppendLog("Server settings updated from client " + clientId, LogLevel.Info);
                SendAosAck(clientId, true, "settingsApply");
                BroadcastServerSettings();
                BroadcastAosSync();
            }
            catch (Exception ex)
            {
                SendAosAck(clientId, false, ex.Message ?? "settingsApply failed");
            }
        }

        private void BroadcastKaiSync()
        {
            if (_settings == null || !_settings.NotifyServerEnabled) return;
            try
            {
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.BeginInvoke(new Action(BroadcastKaiSync));
                    return;
                }
                BroadcastNotify(new NotifyMessage
                {
                    Kind = "kaiSync",
                    Level = "info",
                    Title = "KAI - AOS Renew",
                    Text = BuildKaiSyncPayload()
                });
            }
            catch { }
        }

        private void SendAosAck(int clientId, bool ok, string text)
        {
            try
            {
                NotifyServer.Instance.SendTo(clientId, new NotifyMessage
                {
                    Kind = "aosAck",
                    Level = ok ? "success" : "error",
                    Title = "KAI - AOS Renew",
                    Text = text ?? ""
                });
            }
            catch { }
        }

        private void ApplyAosIntervalFromClient(int clientId, string payload)
        {
            if (_remoteServerUpdateBusy)
            {
                SendAosAck(clientId, false, UiStrings.T("AosSaveUpdating"));
                return;
            }
            int hours;
            if (!int.TryParse((payload ?? "").Trim(), out hours) || hours < 1 || hours > 168)
            {
                SendAosAck(clientId, false, UiStrings.T("InvalidInterval"));
                return;
            }
            try
            {
                var prevLoading = _loadingUi;
                _loadingUi = true;
                try
                {
                    if (IntervalBox != null) IntervalBox.Text = hours.ToString();
                }
                finally { _loadingUi = prevLoading; }
                if (_settings != null)
                {
                    _settings.IntervalHours = hours;
                    _settings.Save();
                }
                ScheduleNextServiceCheck();
                if (_timerActive)
                {
                    _retrySchedule = false;
                    ScheduleNext(true);
                    AppendLog(string.Format(UiStrings.T("LogIntervalUpdated"), hours), true);
                    UpdateNextLabel();
                    UpdateTrayTooltip();
                }
                else
                    EnsureAutoTimer(true);
                SendAosAck(clientId, true, "OK");
                BroadcastAosSync();
            }
            catch (Exception ex)
            {
                SendAosAck(clientId, false, ex.Message);
            }
        }

        private void ApplyAosFromClient(int clientId, string payload)
        {
            if (_remoteServerUpdateBusy)
            {
                SendAosAck(clientId, false, UiStrings.T("AosSaveUpdating"));
                return;
            }
            if (_busy)
            {
                SendAosAck(clientId, false, UiStrings.T("AosBusyRetry"));
                return;
            }
            int hours;
            string email;
            string password;
            bool? forceRegisterOpt;
            int? registerBlockAfterOpt;
            if (!NotifyMessage.TryParseAosSync(payload, out hours, out email, out password, out forceRegisterOpt, out registerBlockAfterOpt))
            {
                SendAosAck(clientId, false, "Invalid AOS payload");
                return;
            }
            email = (email ?? "").Trim();
            password = (password ?? "").Trim();
            if (string.IsNullOrEmpty(email))
            {
                SendAosAck(clientId, false, UiStrings.T("MissingEmail"));
                return;
            }
            if (email.IndexOf('@') < 0 || email.IndexOf(' ') >= 0)
            {
                SendAosAck(clientId, false, UiStrings.T("InvalidUser"));
                return;
            }
            if (string.IsNullOrEmpty(password))
            {
                SendAosAck(clientId, false, UiStrings.T("MissingPassword"));
                return;
            }
            if (hours < 1 || hours > 168)
            {
                SendAosAck(clientId, false, UiStrings.T("InvalidInterval"));
                return;
            }
            try
            {
                if (EmailBox != null) EmailBox.Text = email;
                SetPasswordUi(password);
                if (IntervalBox != null) IntervalBox.Text = hours.ToString();
                if (registerBlockAfterOpt.HasValue && _settings != null)
                {
                    var n = registerBlockAfterOpt.Value;
                    if (n < 1) n = 1;
                    if (n > 10) n = 10;
                    _settings.AuthRegisterBlockAfter = n;
                    if (AuthRegisterBlockAfterBox != null)
                        AuthRegisterBlockAfterBox.Text = n.ToString();
                }
                var forceRegister = forceRegisterOpt.HasValue
                    ? forceRegisterOpt.Value
                    : CredentialsChangedInUi();
                SaveFromUi(true);
                string fail;
                if (!TrySavePreflight(out fail, false))
                {
                    SendAosAck(clientId, false, fail ?? "Preflight failed");
                    BroadcastAosSync();
                    return;
                }
                Forget(RunNowAsync(true, forceRegister), "AosApplyRemote");
                SendAosAck(clientId, true, "OK");
                BroadcastAosSync();
            }
            catch (Exception ex)
            {
                SendAosAck(clientId, false, ex.Message);
            }
        }

        private void OnNotifyClientDisconnected(int clientId)
        {
            var idCopy = clientId;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _clientsUpdating.Remove(idCopy);
                RefreshNotifyClientsCombo();
                BroadcastNotifyClientsList();
            }));
        }

        private void OnNotifyClientConnected(int clientId)
        {
            var idCopy = clientId;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                SendFooterTo(idCopy);
                SendActivitySyncTo(idCopy);
                SendRegisterSyncTo(idCopy);
                SendAosSyncTo(idCopy);
                SendServerSettingsTo(idCopy);
                SendKaiSyncTo(idCopy);
                SendUpdateFeedTo(idCopy);
                // Always push (including empty clear) so offline clients drop a stale banner.
                BroadcastKeysReaderWarn(idCopy);
                RefreshNotifyClientsCombo();
                BroadcastNotifyClientsList();
                // Snapshot on UI thread — _keysAllEvents is mutated there.
                try
                {
                    if (_settings == null || !_settings.NotifyServerEnabled) return;
                    // Authoritative snapshot only — never invent Waiting stubs from VIN cache.
                    // Skip empty after Clear Keys so reconnect does not wipe the client's history.
                    var rows = GetRecentKeyRows(NotifyMessage.MaxKeySyncRows) ?? new List<KeyActivityEvent>();
                    if (rows.Count == 0)
                        return;
                    var payload = NotifyKeySync.EncodeKeySync(rows) ?? "";
                    _lastKeySyncSignature = KeySyncSignature(rows);
                    NotifyServer.Instance.SendTo(idCopy, new NotifyMessage
                    {
                        Kind = "keySync",
                        Level = "info",
                        Title = "KAI - AOS Renew",
                        Text = payload
                    });
                }
                catch (Exception ex)
                {
                    try { RenewEngine.Log("Notify keySync: " + ex.Message); }
                    catch { }
                }
            }));
        }

        private void RefreshNotifyServerIpDisplay()
        {
            if (NotifyServerIpBox == null) return;
            try { NotifyServerIpBox.Text = NotifyServer.GetLocalIpv4Summary(); }
            catch { NotifyServerIpBox.Text = "127.0.0.1"; }
            SizeTextBoxToContent(NotifyServerIpBox);
        }

        private void NotifyServerIpCopy_Click(object sender, RoutedEventArgs e)
        {
            var text = NotifyServerIpBox != null ? (NotifyServerIpBox.Text ?? "").Trim() : "";
            if (string.IsNullOrEmpty(text))
            {
                RefreshNotifyServerIpDisplay();
                text = NotifyServerIpBox != null ? (NotifyServerIpBox.Text ?? "").Trim() : "";
            }
            if (string.IsNullOrEmpty(text)) return;
            TryCopyTextToClipboard(text, UiStrings.T("NotifyServerIpCopied"));
        }

        private void NotifyFirewallAllow_Click(object sender, RoutedEventArgs e)
        {
            var port = NotifyServer.Instance.Port;
            if (port <= 0 && _settings != null) port = _settings.NotifyServerPort;
            if (port <= 0) port = NotifyServer.DefaultPort;
            if (NotifyFirewallAllowBtn != null)
                NotifyFirewallAllowBtn.IsEnabled = false;
            try
            {
                var result = NotifyFirewallHelper.AllowPortElevated(port);
                if (NotifyTestResultText != null)
                {
                    NotifyTestResultText.Text = result.Ok
                        ? string.Format(UiStrings.T("NotifyFirewallAllowOk"), port)
                        : string.Format(UiStrings.T("NotifyFirewallAllowFail"), result.Message ?? "");
                }
                AppendLog(
                    result.Ok
                        ? string.Format(UiStrings.T("NotifyFirewallAllowOk"), port)
                        : string.Format(UiStrings.T("NotifyFirewallAllowFail"), result.Message ?? ""),
                    result.Ok ? LogLevel.Info : LogLevel.Warning);
                UpdateNotifyFirewallHint();
            }
            finally
            {
                if (NotifyFirewallAllowBtn != null)
                    NotifyFirewallAllowBtn.IsEnabled = true;
            }
        }

        private void ParseAndSetNotifyServerPort()
        {
            if (_settings == null) return;
            int port = NotifyServer.DefaultPort;
            if (NotifyServerPortBox != null)
            {
                int parsed;
                if (int.TryParse(NotifyServerPortBox.Text.Trim(), out parsed))
                    port = parsed;
            }
            if (port < 1024 || port > 65535)
                port = NotifyServer.DefaultPort;
            _settings.NotifyServerPort = port;
            if (NotifyServerPortBox != null)
                NotifyServerPortBox.Text = port.ToString();
        }

        private void NotifyServer_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingUi || _settings == null) return;
            _settings.NotifyServerEnabled = NotifyServerEnableCheck != null && NotifyServerEnableCheck.IsChecked == true;
            ParseAndSetNotifyServerPort();
            ParseAndSetNotifyPairingCode();
            try { _settings.Save(); }
            catch { }
            ApplyNotifyServerFromSettings();
        }

        private void NotifyServerPort_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_loadingUi || _settings == null) return;
            ParseAndSetNotifyServerPort();
            ParseAndSetNotifyPairingCode();
            try { _settings.Save(); }
            catch { }
            ApplyNotifyServerFromSettings();
        }

        private void ParseAndSetNotifyPairingCode()
        {
            if (_settings == null) return;
            var code = NotifyPairingCodeBox != null ? (NotifyPairingCodeBox.Text ?? "").Trim() : "";
            _settings.SetNotifyPairingCode(code);
            if (NotifyPairingCodeBox != null && NotifyPairingCodeBox.Text != code)
                NotifyPairingCodeBox.Text = code;
            UpdateNotifyPairingWarning();
        }

        private void UpdateNotifyPairingWarning()
        {
            // Soft-blocks for empty pairing were removed; keep banner hidden.
            try
            {
                if (NotifyPairingEmptyWarningBanner != null)
                    NotifyPairingEmptyWarningBanner.Visibility = Visibility.Collapsed;
            }
            catch { }
        }

        private void NotifyPairingCode_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_loadingUi || _settings == null) return;
            ParseAndSetNotifyPairingCode();
            try { _settings.Save(); }
            catch { }
            ApplyNotifyServerFromSettings();
        }

        private void NotifyPairingGenerate_Click(object sender, RoutedEventArgs e)
        {
            if (_settings == null) return;
            var ask = UiStrings.T("NotifyPairingGenerateConfirm");
            var result = MessageBox.Show(this, ask, UiStrings.T("NotifyPairingGenerate"),
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (result != MessageBoxResult.Yes) return;
            var code = NotifyPairing.GenerateCode();
            _settings.SetNotifyPairingCode(code);
            if (NotifyPairingCodeBox != null)
                NotifyPairingCodeBox.Text = code;
            UpdateNotifyPairingWarning();
            try { _settings.Save(); }
            catch { }
            ApplyNotifyServerFromSettings();
        }

        private void NotifyPairingCopy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var code = NotifyPairingCodeBox != null
                    ? (NotifyPairingCodeBox.Text ?? "").Trim()
                    : (_settings != null ? _settings.GetNotifyPairingCode() : "");
                if (string.IsNullOrEmpty(code)) return;
                NativeMethods.TrySetClipboardText(code);
            }
            catch { }
        }

        private void NotifyPairingPaste_Click(object sender, RoutedEventArgs e)
        {
            if (_loadingUi || _settings == null) return;
            try
            {
                var clip = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(clip)) return;
                if (NotifyPairingCodeBox != null)
                    NotifyPairingCodeBox.Text = clip.Trim();
                ParseAndSetNotifyPairingCode();
                try { _settings.Save(); }
                catch { }
                ApplyNotifyServerFromSettings();
            }
            catch { }
        }

        private void RefreshNotifyClientsCombo()
        {
            if (NotifySendToCombo == null) return;
            var prevId = -1;
            var prev = NotifySendToCombo.SelectedItem as NotifySendTarget;
            if (prev != null) prevId = prev.ClientId;

            var items = new List<NotifySendTarget>();
            items.Add(new NotifySendTarget
            {
                ClientId = 0,
                Label = UiStrings.T("NotifyAllClients") + " (" + NotifyServer.Instance.ClientCount + ")"
            });
            try
            {
                foreach (var c in NotifyServer.Instance.GetClients())
                {
                    var label = FormatNotifyClientLabel(c.Display ?? ("#" + c.Id), c.Version);
                    if (_clientsUpdating.Contains(c.Id))
                        label = string.Format(UiStrings.T("ClientUpdatingLabel"), label);
                    items.Add(new NotifySendTarget
                    {
                        ClientId = c.Id,
                        Label = label
                    });
                }
            }
            catch { }

            NotifySendToCombo.ItemsSource = items;
            NotifySendTarget select = items[0];
            if (prevId > 0)
            {
                foreach (var it in items)
                {
                    if (it.ClientId == prevId)
                    {
                        select = it;
                        break;
                    }
                }
            }
            NotifySendToCombo.SelectedItem = select;
            SizeComboToLongestLabel(NotifySendToCombo, items.ConvertAll(it => it.Label));
            UpdateNotifyFirewallHint();
            UpdateUpdateAllClientsButtonEnabled();
        }

        private void UpdateUpdateAllClientsButtonEnabled()
        {
            if (UpdateAllClientsBtn == null) return;
            var on = _settings != null && _settings.NotifyServerEnabled
                && NotifyServer.Instance.IsRunning
                && NotifyServer.Instance.ClientCount > 0
                && !_remoteServerUpdateBusy;
            UpdateAllClientsBtn.IsEnabled = on;
        }

        private async void UpdateAllClients_Click(object sender, RoutedEventArgs e)
        {
            await RequestUpdateAllClientsAsync(true, "settings");
        }

        /// <summary>
        /// Broadcast clientUpdate to all connected clients.
        /// <paramref name="confirm"/> true = show Yes/No (local Settings button).
        /// </summary>
        private async Task RequestUpdateAllClientsAsync(bool confirm, string source)
        {
            if (_settings == null || !_settings.NotifyServerEnabled || !NotifyServer.Instance.IsRunning)
                return;
            var count = NotifyServer.Instance.ClientCount;
            if (count <= 0)
            {
                AppendLog(UiStrings.T("UpdateAllClientsNone"), LogLevel.Warning);
                return;
            }
            if (confirm)
            {
                var ask = string.Format(UiStrings.T("UpdateAllClientsConfirm"), count);
                var result = MessageBox.Show(this, ask, UiStrings.T("UpdateAllClients"),
                    MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
                if (result != MessageBoxResult.Yes) return;
            }
            try { await RefreshUpdateFeedAsync(); }
            catch { }
            var ver = (_feedClientVer ?? "").Trim();
            try
            {
                BroadcastNotify(new NotifyMessage
                {
                    Kind = "clientUpdate",
                    Level = "info",
                    Title = "KAI - AOS Renew",
                    Text = ver
                });
            }
            catch (Exception ex)
            {
                AppendLog(ex.Message ?? "clientUpdate", LogLevel.Error);
                return;
            }
            var src = string.IsNullOrEmpty(source) ? "settings" : source;
            AppendLog(string.Format(UiStrings.T("UpdateAllClientsSent"), count,
                string.IsNullOrEmpty(ver) ? "?" : ver, src), LogLevel.Info);
        }

        private void SendNotifyTest(NotifyMessage msg, string kindLabel)
        {
            int? targetId = null;
            if (NotifySendToCombo != null)
            {
                var target = NotifySendToCombo.SelectedItem as NotifySendTarget;
                if (target != null) targetId = target.ClientId;
            }
            SendNotifyTest(msg, kindLabel, targetId, 0);
        }

        private static string FormatNotifyTestSuccess(int? targetClientId, string destLabel, int clientCount)
        {
            if (targetClientId == null || targetClientId.Value <= 0)
                return string.Format(UiStrings.T("NotifyTestSentCount"), clientCount);
            return string.Format(UiStrings.T("NotifyTestSentDest"), destLabel ?? "");
        }

        private void SendNotifyClientsListTo(int clientId)
        {
            if (_settings == null || !_settings.NotifyServerEnabled) return;
            try
            {
                NotifyServer.Instance.SendTo(clientId, new NotifyMessage
                {
                    Kind = "notifyClients",
                    Level = "info",
                    Title = "KAI - AOS Renew",
                    Text = NotifyMessage.EncodeKvMap(BuildNotifyClientsMap(clientId))
                });
            }
            catch { }
        }

        private void BroadcastNotifyClientsList()
        {
            if (_settings == null || !_settings.NotifyServerEnabled) return;
            try
            {
                foreach (var c in NotifyServer.Instance.GetClients())
                    SendNotifyClientsListTo(c.Id);
            }
            catch { }
        }

        private void HandleNotifyTestFromClient(int fromClientId, string payload)
        {
            var kind = "info";
            var targetId = 0;
            if (!string.IsNullOrWhiteSpace(payload))
            {
                var parts = payload.Split(new[] { '|' }, 2);
                if (parts.Length >= 1 && !string.IsNullOrWhiteSpace(parts[0]))
                    kind = parts[0].Trim().ToLowerInvariant();
                if (parts.Length >= 2)
                {
                    int parsed;
                    if (int.TryParse(parts[1].Trim(), out parsed) && parsed >= 0)
                        targetId = parsed;
                }
            }

            NotifyMessage msg;
            string kindLabel;
            switch (kind)
            {
                case "vin":
                    {
                        var tipSec = _settings != null ? _settings.KeysVinTipSeconds : 10;
                        if (tipSec < 3) tipSec = 3;
                        msg = new NotifyMessage
                        {
                            Kind = "vin",
                            Level = "info",
                            Title = "KAI - AOS Renew",
                            Text = "Test VIN WBATESTVIN0000001",
                            Vin = "WBATESTVIN0000001",
                            Action = "copyVin",
                            Seconds = tipSec
                        };
                        kindLabel = "VIN";
                        break;
                    }
                case "warning":
                    msg = new NotifyMessage
                    {
                        Kind = "warning",
                        Level = "warning",
                        Title = "KAI - AOS Renew",
                        Text = "Test warning tip (remote)",
                        Seconds = 8
                    };
                    kindLabel = "Warning";
                    break;
                case "error":
                    msg = new NotifyMessage
                    {
                        Kind = "error",
                        Level = "error",
                        Title = "KAI - AOS Renew",
                        Text = "Test error tip (remote)",
                        Seconds = 8
                    };
                    kindLabel = "Error";
                    break;
                default:
                    msg = new NotifyMessage
                    {
                        Kind = "info",
                        Level = "info",
                        Title = "KAI - AOS Renew",
                        Text = "Test info tip (remote)",
                        Seconds = 8
                    };
                    kindLabel = "Info";
                    break;
            }

            string err;
            var ok = SendNotifyTest(msg, kindLabel, targetId, fromClientId, out err);
            try
            {
                var ackText = ok
                    ? FormatNotifyTestSuccess(targetId, err, NotifyServer.Instance.ClientCount)
                    : (err ?? UiStrings.T("NotifyTestNoClients"));
                NotifyServer.Instance.SendTo(fromClientId, new NotifyMessage
                {
                    Kind = "notifyTestAck",
                    Level = ok ? "info" : "error",
                    Title = "KAI - AOS Renew",
                    Text = ackText
                });
            }
            catch { }
        }

        private bool SendNotifyTest(NotifyMessage msg, string kindLabel, int? targetClientId, int replyClientId)
        {
            string unused;
            return SendNotifyTest(msg, kindLabel, targetClientId, replyClientId, out unused);
        }

        private bool SendNotifyTest(NotifyMessage msg, string kindLabel, int? targetClientId, int replyClientId, out string destOrError)
        {
            destOrError = null;
            if (_settings == null || !_settings.NotifyServerEnabled || !NotifyServer.Instance.IsRunning)
            {
                destOrError = UiStrings.T("NotifyTestServerOff");
                AppendLog(destOrError, LogLevel.Warning);
                if (replyClientId == 0)
                    SetNotifyTestResult(destOrError, true);
                return false;
            }
            if (replyClientId == 0)
                RefreshNotifyClientsCombo();
            if (NotifyServer.Instance.ClientCount <= 0)
            {
                destOrError = UiStrings.T("NotifyTestNoClients");
                AppendLog(destOrError, LogLevel.Warning);
                if (replyClientId == 0)
                    SetNotifyTestResult(destOrError, true);
                return false;
            }
            var targetId = targetClientId ?? 0;
            string destLabel;
            if (targetId <= 0)
            {
                NotifyServer.Instance.Broadcast(msg);
                destLabel = UiStrings.T("NotifyAllClients") + " (" + NotifyServer.Instance.ClientCount + ")";
            }
            else
            {
                if (!NotifyServer.Instance.SendTo(targetId, msg))
                {
                    destOrError = UiStrings.T("NotifyTestNoClients");
                    AppendLog(destOrError, LogLevel.Warning);
                    if (replyClientId == 0)
                    {
                        RefreshNotifyClientsCombo();
                        SetNotifyTestResult(destOrError, true);
                    }
                    return false;
                }
                destLabel = "#" + targetId;
                try
                {
                    foreach (var c in NotifyServer.Instance.GetClients())
                    {
                        if (c.Id == targetId)
                        {
                            destLabel = c.Display ?? destLabel;
                            break;
                        }
                    }
                }
                catch { }
            }
            destOrError = destLabel;
            AppendLog(string.Format(UiStrings.T("NotifyTestSent"), kindLabel, destLabel), LogLevel.Info);
            if (replyClientId == 0)
                SetNotifyTestResult(FormatNotifyTestSuccess(targetId, destLabel, NotifyServer.Instance.ClientCount), false);
            return true;
        }

        private void BroadcastActivityEntry(LogEntry entry)
        {
            if (entry == null || _settings == null || !_settings.NotifyServerEnabled) return;
            try
            {
                BroadcastNotify(new NotifyMessage
                {
                    Kind = "activity",
                    Level = LogLevelWireName(entry.Level),
                    Title = "KAI - AOS Renew",
                    Text = entry.Text ?? "",
                    At = entry.Time.ToString("yyyy-MM-ddTHH:mm:ss")
                });
            }
            catch { }
        }

        private void BroadcastActivityClear()
        {
            if (_settings == null || !_settings.NotifyServerEnabled) return;
            try
            {
                BroadcastNotify(new NotifyMessage
                {
                    Kind = "activityClear",
                    Level = "info",
                    Title = "KAI - AOS Renew",
                    Text = ""
                });
            }
            catch { }
        }

        private List<RegisterWireRow> BuildRegisterSyncWireRows(int maxRows)
        {
            var list = new List<RegisterWireRow>();
            if (_registerHistory == null || _registerHistory.Count == 0) return list;
            var start = Math.Max(0, _registerHistory.Count - Math.Max(1, maxRows));
            for (var i = start; i < _registerHistory.Count; i++)
            {
                var r = _registerHistory[i];
                if (r == null) continue;
                list.Add(new RegisterWireRow
                {
                    At = r.At,
                    Kind = r.Kind,
                    Ok = r.Kind == 1,
                    Text = r.Text ?? ""
                });
            }
            return list;
        }

        private void BroadcastRegisterSync()
        {
            if (_settings == null || !_settings.NotifyServerEnabled) return;
            try
            {
                var list = BuildRegisterSyncWireRows(RegisterUnfilteredDisplayMax);
                var payload = NotifyMessage.EncodeRegisterSync(list);
                BroadcastNotify(new NotifyMessage
                {
                    Kind = "registerSync",
                    Level = "info",
                    Title = "KAI - AOS Renew",
                    Text = payload ?? ""
                });
            }
            catch (Exception ex)
            {
                try { RenewEngine.Log("Notify registerSync broadcast: " + ex.Message); }
                catch { }
            }
        }

        private void BroadcastRegisterEntry(RegisterHistoryRow row)
        {
            if (row == null || _settings == null || !_settings.NotifyServerEnabled) return;
            try
            {
                BroadcastNotify(new NotifyMessage
                {
                    Kind = "register",
                    Level = row.Kind.ToString(CultureInfo.InvariantCulture),
                    Title = "KAI - AOS Renew",
                    At = row.At.ToString("yyyy-MM-ddTHH:mm:ss"),
                    Text = row.Text ?? ""
                });
            }
            catch { }
        }

    }
}
