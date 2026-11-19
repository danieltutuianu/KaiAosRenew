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

        private void UpdateKeysTabPolling()
        {
            var active = !_aboutVisible && TabKeys != null && TabKeys.IsSelected;
            if (active)
            {
                if (!_keysRefreshTimer.IsEnabled)
                    _keysRefreshTimer.Start();
            }
            else if (_keysRefreshTimer.IsEnabled)
                _keysRefreshTimer.Stop();
        }

        private void UpdateKeysRetryButton()
        {
            if (KeysRetryBmwBtn == null) return;
            if (_keysRetryBusy)
            {
                KeysRetryBmwBtn.IsEnabled = false;
                return;
            }
            var newest = KeyActivityMonitor.GetNewestEvent(_keysAllEvents);
            KeysRetryBmwBtn.IsEnabled = _keyMonitor != null && _keyMonitor.CanRetryBmwUpload(newest);
        }

        private static void UpdateVinCopyIconVisibility(Button btn)
        {
            if (btn == null) return;
            var ev = btn.DataContext as KeyActivityEvent;
            var tipText = UiStrings.T("KeysCopyVin");
            System.Windows.Automation.AutomationProperties.SetName(btn, tipText);
            var tip = btn.ToolTip as ToolTip;
            if (tip != null)
            {
                var tb = tip.Content as TextBlock;
                if (tb != null) tb.Text = tipText;
            }
            else
                btn.ToolTip = tipText;
            btn.Visibility = (ev != null && !string.IsNullOrEmpty(ev.Vin))
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private static void UpdateKmCopyIconVisibility(Button btn)
        {
            if (btn == null) return;
            var ev = btn.DataContext as KeyActivityEvent;
            var tipText = UiStrings.T("KeysCopyKm");
            System.Windows.Automation.AutomationProperties.SetName(btn, tipText);
            var tip = btn.ToolTip as ToolTip;
            if (tip != null)
            {
                var tb = tip.Content as TextBlock;
                if (tb != null) tb.Text = tipText;
            }
            else
                btn.ToolTip = tipText;
            btn.Visibility = (ev != null && ev.MileageKm.HasValue && ev.MileageKm.Value > 0)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void ShowVinCopyIconFeedback(Button btn)
        {
            if (btn == null) return;
            ResetVinCopyIconFeedback();
            _keysCopyFeedbackBtn = btn;
            btn.Content = KeysCopyOkGlyph;
            btn.Foreground = StatusOk() ?? KeysCopyOkBrush;
            _keysCopyFeedbackTimer.Stop();
            _keysCopyFeedbackTimer.Start();
        }

        private void KeysCopyFeedbackTimer_Tick(object sender, EventArgs e)
        {
            _keysCopyFeedbackTimer.Stop();
            ResetVinCopyIconFeedback();
        }

        private void ResetVinCopyIconFeedback()
        {
            if (_keysCopyFeedbackBtn == null) return;
            try
            {
                _keysCopyFeedbackBtn.Content = KeysCopyGlyph;
                _keysCopyFeedbackBtn.ClearValue(Control.ForegroundProperty);
            }
            catch { }
            _keysCopyFeedbackBtn = null;
        }

        private static void UpdateEmptyHint(TextBlock hint, bool show)
        {
            if (hint == null) return;
            hint.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateClearButtonsEnabled()
        {
            if (ClearLogBtn != null)
                ClearLogBtn.IsEnabled = _logEntries != null && _logEntries.Count > 0;
            if (ClearRegisterLogBtn != null)
                ClearRegisterLogBtn.IsEnabled = _registerHistory != null && _registerHistory.Count > 0;
            if (KeysClearBtn != null)
                KeysClearBtn.IsEnabled = _keysAllEvents != null && _keysAllEvents.Count > 0;
            if (KeysExportBtn != null)
                KeysExportBtn.IsEnabled = KeysEventsList != null && KeysEventsList.Items.Count > 0;
        }

        private static void UpdateNoMatchesHint(TextBlock hint, bool hasSourceItems, int visibleCount)
        {
            if (hint == null) return;
            hint.Visibility = (hasSourceItems && visibleCount == 0)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void UpdateKeysTodayBadge(int count)
        {
            if (KeysTodayBadge == null) return;
            KeysTodayBadge.Text = count > 0
                ? string.Format(UiStrings.T("KeysTodayBadge"), count)
                : "";
        }

        private void UpdateKeysAlertFromCache()
        {
            var snap = new KeyActivitySnapshot();
            snap.Events = _keysAllEvents;
            UpdateKeysAlert(snap);
        }

        private void UpdateKeysAlert(KeyActivitySnapshot snap)
        {
            _keysAlert = false;
            var stallOn = _settings == null || _settings.KeysStallAlertEnabled;
            // Alert only from the newest event — and only within KeysAlertMaxMinutes.
            var ev = snap != null ? KeyActivityMonitor.GetNewestEvent(snap.Events) : null;
            if (ev != null)
            {
                var stallMin = _settings != null ? _settings.KeysStallMinutes : 2;
                _keysAlert = KeysAlertLogic.ShouldAlertNewestKey(
                    true,
                    (int)ev.Status,
                    ev.LocalSent,
                    ev.BmwUploaded,
                    ev.Failed,
                    ev.DetectedLocal,
                    DateTime.Now,
                    stallOn,
                    stallMin,
                    KeysAlertMaxMinutes);
            }
            if (KeysTabAlertDot != null)
                KeysTabAlertDot.Visibility = _keysAlert ? Visibility.Visible : Visibility.Collapsed;
            SyncAlertBlinkTimer();
            RefreshTrayIcon();
            UpdateTrayTooltip();
        }

        private void MaybeCheckForUpdatesOnStart()
        {
            if (_updateCheckStarted) return;
            ContentRendered += OnContentRenderedForUpdateCheck;
        }

        private async void OnContentRenderedForUpdateCheck(object sender, EventArgs e)
        {
            ContentRendered -= OnContentRenderedForUpdateCheck;
            if (_updateCheckStarted) return;
            _updateCheckStarted = true;
            try { await System.Threading.Tasks.Task.Delay(1500); }
            catch { }
            var autoApply = _settings != null && _settings.AutoUpdate;
            await RunUpdateCheckAsync(autoApply);
            try { _updatePeekTimer.Start(); }
            catch { }
        }

        private async void UpdatePeekTimer_Tick(object sender, EventArgs e)
        {
            await RunUpdateCheckAsync(false);
        }

        private async Task RunUpdateCheckAsync(bool autoApply)
        {
            if (_updateCheckBusy) return;
            _updateCheckBusy = true;
            UpdateInfo peeked = null;
            try
            {
                peeked = await UpdateService.PeekAsync();
                ShowPendingUpdate(peeked);
            }
            catch
            {
                ShowPendingUpdate(null);
                peeked = null;
            }
            finally
            {
                _updateCheckBusy = false;
            }
            try { await RefreshUpdateFeedAsync(); }
            catch { }
            if (autoApply && peeked != null)
            {
                try
                {
                    await ApplySelfUpdateAsync(peeked, false, null, UiStrings.T("UpdateSourceAuto"));
                }
                catch { }
            }
        }

        private async Task RefreshUpdateFeedAsync()
        {
            if (_updateFeedBusy) return;
            _updateFeedBusy = true;
            try
            {
                string serverVer = "";
                string clientVer = "";
                try { serverVer = await UpdateService.PeekLatestChannelVersionAsync(UpdateService.ServerChannel) ?? ""; }
                catch { serverVer = ""; }
                try { clientVer = await UpdateService.PeekLatestChannelVersionAsync(UpdateService.ClientChannel) ?? ""; }
                catch { clientVer = ""; }
                // Local peek is source of truth when non-empty; empty = failed peek, keep cache.
                if (!string.IsNullOrEmpty(serverVer)) _feedServerVer = serverVer;
                if (!string.IsNullOrEmpty(clientVer)) _feedClientVer = clientVer;
                await ApplyFeedToSelfAsync();
                BroadcastUpdateFeed();
            }
            finally
            {
                _updateFeedBusy = false;
            }
        }

        private async Task ApplyFeedToSelfAsync()
        {
            if (string.IsNullOrWhiteSpace(_feedServerVer)) return;
            if (!UpdateService.IsNewerVersion(_feedServerVer, AppVersionDisplay)) return;
            try
            {
                var update = await UpdateService.PeekAsync();
                if (update != null)
                    ShowPendingUpdate(update);
            }
            catch { }
        }

        private async Task HandleInboundUpdateFeedAsync(string text)
        {
            string serverVer;
            string clientVer;
            if (!NotifyMessage.TryParseUpdateFeed(text, out serverVer, out clientVer)) return;
            MergeFeedVersion(ref _feedServerVer, serverVer);
            MergeFeedVersion(ref _feedClientVer, clientVer);
            await ApplyFeedToSelfAsync();
            BroadcastUpdateFeed();
        }

        /// <summary>Keep non-empty feed; prefer the newer of current vs inbound.</summary>
        private static void MergeFeedVersion(ref string current, string inbound)
        {
            var next = (inbound ?? "").Trim();
            if (string.IsNullOrEmpty(next))
                return;
            var cur = current ?? "";
            if (string.IsNullOrEmpty(cur) || UpdateService.IsNewerVersion(next, cur))
                current = next;
        }

        private void ShowPendingUpdate(UpdateInfo update)
        {
            _pendingUpdate = update;
            if (update == null)
            {
                if (UpdateAvailableText != null)
                    UpdateAvailableText.Visibility = Visibility.Collapsed;
                if (FooterUpdateBtn != null)
                {
                    FooterUpdateBtn.Visibility = Visibility.Collapsed;
                    FooterUpdateBtn.ToolTip = null;
                }
                RefreshTrayIcon();
                return;
            }
            var ver = UpdateService.FormatUpdateVersion(update);
            if (UpdateAvailableText != null)
            {
                UpdateAvailableText.Text = "";
                UpdateAvailableText.Visibility = Visibility.Collapsed;
            }
            if (FooterUpdateBtn != null)
            {
                FooterUpdateBtn.Content = string.Format(UiStrings.T("UpdateServerBtn"), ver);
                FooterUpdateBtn.ToolTip = string.Format(UiStrings.T("UpdateChannelTooltip"), UpdateService.ServerChannel);
                FooterUpdateBtn.Visibility = Visibility.Visible;
                FooterUpdateBtn.IsEnabled = true;
            }
            RefreshTrayIcon();
        }

        private async void FooterUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (FooterUpdateBtn != null) FooterUpdateBtn.IsEnabled = false;
            try
            {
                var update = _pendingUpdate;
                if (update == null)
                {
                    update = await UpdateService.PeekAsync();
                    if (update == null)
                    {
                        MessageBox.Show(this, UiStrings.T("UpdateNone"),
                            UiStrings.T("CheckForUpdates"), MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    ShowPendingUpdate(update);
                }
                await ApplySelfUpdateAsync(update, true, null, UiStrings.T("UpdateSourceFooter"));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(UiStrings.T("UpdateFailed"), ex.Message),
                    UiStrings.T("CheckForUpdates"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                if (FooterUpdateBtn != null && !_remoteServerUpdateBusy && _pendingUpdate != null)
                    FooterUpdateBtn.IsEnabled = true;
            }
        }

        private async Task ApplySelfUpdateAsync(UpdateInfo update, bool prompt, int? ackClientId, string sourceLabel)
        {
            if (update == null) return;
            if (_remoteServerUpdateBusy)
            {
                if (ackClientId.HasValue)
                    SendAosAck(ackClientId.Value, false, UiStrings.T("UpdateServerBusy"));
                return;
            }
            _remoteServerUpdateBusy = true;
            RefreshAosActionButtons();
            UpdateUpdateAllClientsButtonEnabled();
            var ver = UpdateService.FormatUpdateVersion(update);
            try
            {
                if (prompt)
                {
                    var msg = string.Format(UiStrings.T("UpdateAvailable"), ver);
                    var result = MessageBox.Show(this, msg, UiStrings.T("CheckForUpdates"),
                        MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (result != MessageBoxResult.Yes)
                        return;
                }

                AppendLog(string.Format(UiStrings.T("UpdateStartedFrom"),
                    string.IsNullOrEmpty(sourceLabel) ? "?" : sourceLabel), LogLevel.Info);
                BroadcastServerUpdating(ver);
                if (ackClientId.HasValue)
                    SendAosAck(ackClientId.Value, true, "serverUpdate " + ver);

                if (FooterUpdateBtn != null)
                    FooterUpdateBtn.IsEnabled = false;

                await System.Threading.Tasks.Task.Delay(2500);

                AppendLog(string.Format(UiStrings.T("UpdateApplied"), ver), LogLevel.Info);
                await UpdateService.ApplyUpdateAsync(this, update, UiStrings.T, false,
                    (stage, pct) => ShowSelfUpdateProgress(stage, ver, pct),
                    PrepareForRestart);
            }
            catch (Exception ex)
            {
                var err = ex.Message ?? "Update failed";
                AppendLog(string.Format(UiStrings.T("UpdateFailed"), err), LogLevel.Error);
                BroadcastServerUpdateFailed(err);
                if (ackClientId.HasValue)
                {
                    try { SendAosAck(ackClientId.Value, false, err); }
                    catch { }
                }
                HideUpdateProgressBar();
                ShowPendingUpdate(_pendingUpdate ?? update);
            }
            finally
            {
                _remoteServerUpdateBusy = false;
                RefreshAosActionButtons();
                UpdateUpdateAllClientsButtonEnabled();
            }
        }

        private void ShowSelfUpdateProgress(string stage, string version, int percent)
        {
            if (UpdateAvailableText == null) return;
            var ver = version ?? "?";
            if (string.Equals(stage, "downloading", StringComparison.OrdinalIgnoreCase))
            {
                var pct = percent < 0 ? 0 : (percent > 100 ? 100 : percent);
                UpdateAvailableText.Text = string.Format(UiStrings.T("UpdateDownloadingPct"), ver, pct);
                if (UpdateProgressBar != null)
                {
                    UpdateProgressBar.IsIndeterminate = false;
                    UpdateProgressBar.Value = pct;
                    UpdateProgressBar.Visibility = Visibility.Visible;
                }
            }
            else if (string.Equals(stage, "installing", StringComparison.OrdinalIgnoreCase))
            {
                UpdateAvailableText.Text = string.Format(UiStrings.T("UpdateInstalling"), ver);
                if (UpdateProgressBar != null)
                {
                    UpdateProgressBar.IsIndeterminate = false;
                    UpdateProgressBar.Value = 100;
                    UpdateProgressBar.Visibility = Visibility.Visible;
                }
            }
            else
            {
                UpdateAvailableText.Text = string.Format(UiStrings.T("UpdateRestarting"), ver);
                HideUpdateProgressBar();
            }
            UpdateAvailableText.Visibility = Visibility.Visible;
        }

        private void HideUpdateProgressBar()
        {
            if (UpdateProgressBar == null) return;
            UpdateProgressBar.Visibility = Visibility.Collapsed;
            UpdateProgressBar.Value = 0;
            UpdateProgressBar.IsIndeterminate = false;
        }

        private void AutoUpdate_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingUi || _settings == null) return;
            _settings.AutoUpdate = AutoUpdateCheck != null && AutoUpdateCheck.IsChecked == true;
            _settings.Save();
            BroadcastServerSettings();
        }

        private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
        {
            if (_remoteServerUpdateBusy)
            {
                MessageBox.Show(this, UiStrings.T("UpdateServerBusy"),
                    UiStrings.T("CheckForUpdates"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (CheckForUpdatesBtn != null) CheckForUpdatesBtn.IsEnabled = false;
            try
            {
                var update = await UpdateService.PeekAsync();
                ShowPendingUpdate(update);
                if (update == null)
                {
                    MessageBox.Show(this, UiStrings.T("UpdateNone"),
                        UiStrings.T("CheckForUpdates"), MessageBoxButton.OK, MessageBoxImage.Information);
                    try { await RefreshUpdateFeedAsync(); }
                    catch { }
                    return;
                }
                try { await RefreshUpdateFeedAsync(); }
                catch { }
                await ApplySelfUpdateAsync(update, true, null, UiStrings.T("UpdateSourceFooter"));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(UiStrings.T("UpdateFailed"), ex.Message),
                    UiStrings.T("CheckForUpdates"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                if (CheckForUpdatesBtn != null) CheckForUpdatesBtn.IsEnabled = true;
            }
        }

        private void UpdateNotifyFirewallHint()
        {
            if (NotifyFirewallHintText == null && NotifyFirewallPanel == null) return;
            var enabled = _settings != null && _settings.NotifyServerEnabled && NotifyServer.Instance.IsRunning;
            var port = NotifyServer.Instance.Port;
            if (port <= 0 && _settings != null) port = _settings.NotifyServerPort;
            if (port <= 0) port = NotifyServer.DefaultPort;
            var needsRule = enabled && !NotifyFirewallHelper.IsPortAllowed(port);
            if (needsRule)
            {
                if (NotifyFirewallHintText != null)
                {
                    NotifyFirewallHintText.Text = string.Format(UiStrings.T("NotifyFirewallHint"), port);
                    NotifyFirewallHintText.Visibility = Visibility.Visible;
                }
                if (NotifyFirewallAllowBtn != null)
                    NotifyFirewallAllowBtn.Content = UiStrings.T("NotifyFirewallAllow");
                if (NotifyFirewallPanel != null)
                    NotifyFirewallPanel.Visibility = Visibility.Visible;
            }
            else
            {
                if (NotifyFirewallPanel != null)
                    NotifyFirewallPanel.Visibility = Visibility.Collapsed;
                else if (NotifyFirewallHintText != null)
                    NotifyFirewallHintText.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateMinimizeToTrayEnabled()
        {
            // Always editable; minimize-on-launch still requires Start with Windows at runtime.
            if (MinimizeToTrayCheck != null)
                MinimizeToTrayCheck.IsEnabled = true;
        }

        private void UpdateServiceActionButtons(BmwServicesReport report)
        {
            if (KaiFixServicesBtn == null || KaiStopServicesBtn == null) return;
            if (report == null)
            {
                KaiFixServicesBtn.Visibility = Visibility.Collapsed;
                KaiStopServicesBtn.Visibility = Visibility.Collapsed;
                SetKaiServiceAlert(true);
                return;
            }

            KaiFixServicesBtn.Visibility = report.AllOk ? Visibility.Collapsed : Visibility.Visible;
            SetKaiServiceAlert(!report.AllOk);

            var anyRunning = false;
            if (report.Services != null)
            {
                for (var i = 0; i < report.Services.Count; i++)
                {
                    if (report.Services[i] != null && report.Services[i].IsRunning)
                    {
                        anyRunning = true;
                        break;
                    }
                }
            }
            KaiStopServicesBtn.Visibility = anyRunning ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateServicesFooter(bool allOk)
        {
            if (ServicesFooterText == null) return;
            ServicesFooterText.Text = allOk ? UiStrings.T("ServicesFooterOk") : UiStrings.T("ServicesFooterFail");
            _footerSvcMode = allOk ? "ok" : "fail";
            _servicesFail = !allOk;
            try
            {
                ServicesFooterText.Foreground = allOk
                    ? (Brush)FindResource("StatusOkBrush")
                    : (Brush)FindResource("StatusErrorBrush");
            }
            catch
            {
                ServicesFooterText.Foreground = allOk ? Brushes.LightGreen : Brushes.OrangeRed;
            }
            SyncServicesBlinkTimer();
            QueueFooterPublish();
        }

        private void UpdateTokenFooterDisplay()
        {
            if (TokenFooterText == null) return;
            if (!_tokenFooterOk.HasValue)
            {
                TokenFooterText.Text = "";
                _footerTokenMode = "none";
                MaybeDebugTokenSticky(_footerTokenMode);
                QueueFooterPublish();
                return;
            }
            var ok = _tokenFooterOk.Value;
            if (!ok)
            {
                TokenFooterText.Text = UiStrings.T("TokenFooterFail");
                _footerTokenMode = "fail";
            }
            else
            {
                var text = UiStrings.T("TokenFooterOk");
                var remain = GetTokenRemaining();
                if (remain.HasValue && remain.Value.TotalSeconds > 0)
                    text += " · " + FormatAge(remain.Value);
                else if (remain.HasValue)
                    text = UiStrings.T("TokenFooterFail"); // expired
                TokenFooterText.Text = text;
                if (remain.HasValue && remain.Value.TotalSeconds <= 0)
                    ok = false;
                _footerTokenMode = ok ? "ok" : "fail";
            }
            try
            {
                TokenFooterText.Foreground = ok
                    ? (Brush)FindResource("StatusOkBrush")
                    : (Brush)FindResource("StatusErrorBrush");
            }
            catch
            {
                TokenFooterText.Foreground = ok ? Brushes.LightGreen : Brushes.OrangeRed;
            }
            MaybeDebugTokenSticky(_footerTokenMode);
            UpdateAosLoginStatus();
            QueueFooterPublish();
        }

        private void UpdateAosLoginStatus()
        {
            if (AosLoginStatusText == null && AosLoginStatusIcon == null) return;
            var mode = _footerTokenMode ?? "none";
            var connected = string.Equals(mode, "ok", StringComparison.OrdinalIgnoreCase)
                && !IsAuthRegisterBlocked()
                && !_aosBmw403Fail;
            var fail = _aosBmw403Fail
                || IsAuthRegisterBlocked()
                || string.Equals(mode, "fail", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mode, "error", StringComparison.OrdinalIgnoreCase);
            Brush brush;
            string glyph;
            string text;
            if (connected)
            {
                text = UiStrings.T("AosLoginConnected");
                brush = StatusOk();
                glyph = "\uE73E";
            }
            else
            {
                text = UiStrings.T("AosLoginNotConnected");
                if (fail)
                {
                    brush = StatusErr();
                    glyph = "\uE711";
                }
                else
                {
                    brush = StatusStopped();
                    glyph = "\uE711";
                }
            }
            if (AosLoginStatusText != null)
            {
                AosLoginStatusText.Text = text;
                AosLoginStatusText.Foreground = brush;
            }
            if (AosLoginStatusIcon != null)
            {
                AosLoginStatusIcon.Text = glyph;
                AosLoginStatusIcon.Foreground = brush;
            }
        }

        private void UpdateAosDirtyStatus()
        {
            if (_aosPullCloudBusy != 0) return;
            if (AosDirtyStatus != null)
            {
                AosDirtyStatus.Text = _aosCredentialsDirty ? UiStrings.T("AosSyncDirty") : "";
                AosDirtyStatus.Foreground = _aosCredentialsDirty ? StatusWarn() : TextMuted();
            }
            if (AosDirtyPendingMark != null)
                AosDirtyPendingMark.Visibility = _aosCredentialsDirty ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateNextLabel()
        {
            if (_nextDueUtc == DateTime.MaxValue || !_timerActive)
            {
                if (!_timerActive)
                    NextRunText.Text = "";
                QueueFooterPublish();
                return;
            }
            var prefix = _retrySchedule ? UiStrings.T("NextRunRetry") : UiStrings.T("NextRun");
            var text = prefix + _nextDueUtc.ToLocalTime().ToString("g");
            if (_retrySchedule && _settings != null && _settings.ConsecutiveFailures > 0)
                text += string.Format(UiStrings.T("FailsCount"), _settings.ConsecutiveFailures);
            NextRunText.Text = text;
            QueueFooterPublish();
        }

        private void UpdateTrayTooltip()
        {
            string tip;
            if (_hasFailureAlert || (_settings != null && _settings.ConsecutiveFailures > 0 && _retrySchedule))
            {
                var fails = _settings != null ? _settings.ConsecutiveFailures : 0;
                tip = "KAI - AOS Renew ERR";
                if (fails > 0)
                    tip += " x" + fails;
                if (_timerActive && _nextDueUtc != DateTime.MaxValue)
                    tip += " @" + _nextDueUtc.ToLocalTime().ToString("t");
            }
            else if (_keysAlert)
                tip = "KAI - AOS Renew — " + UiStrings.T("TrayKeysAlert");
            else if (_timerActive && _nextDueUtc != DateTime.MaxValue)
                tip = "KAI - AOS Renew — next " + _nextDueUtc.ToLocalTime().ToString("g");
            else
                tip = "KAI - AOS Renew";
            if (tip.Length > 63)
                tip = tip.Substring(0, 63);
            if (_tray != null)
                _tray.Text = tip;
        }

        private void UpdateAuthGuardsFooter(bool cooldown, bool blocked, string untilLocal)
        {
            if (AuthGuardsFooterText == null) return;
            AuthGuardsFooterText.Inlines.Clear();

            var warn = StatusWarn();
            var err = StatusErr();
            var ok = StatusOk();

            string cooldownLabel;
            Brush cooldownBrush;
            if (cooldown)
            {
                cooldownLabel = untilLocal != null
                    ? string.Format(UiStrings.T("FooterCooldownOnUntil"), untilLocal)
                    : UiStrings.T("FooterCooldownOn");
                cooldownBrush = warn;
            }
            else
            {
                cooldownLabel = UiStrings.T("FooterCooldownOff");
                cooldownBrush = ok;
            }

            string blockedLabel = blocked ? UiStrings.T("FooterBlockedOn") : UiStrings.T("FooterBlockedOff");
            Brush blockedBrush = blocked ? err : ok;

            var muted = TextMuted();
            AuthGuardsFooterText.Inlines.Add(new Run(cooldownLabel) { Foreground = cooldownBrush });
            AuthGuardsFooterText.Inlines.Add(new Run("  ·  ") { Foreground = muted });
            AuthGuardsFooterText.Inlines.Add(new Run(blockedLabel) { Foreground = blockedBrush });
            AuthGuardsFooterText.ToolTip =
                UiStrings.T("AuthCooldownExplain") + "\n\n" + UiStrings.T("AuthRegisterBlockedExplain");
            _authCooldownOn = cooldown;
            _authBlockedOn = blocked;
            SyncAuthGuardsBlinkTimer();
            QueueFooterPublish();
        }

        private void UpdateTokenAgeText()
        {
            if (_settings == null || TokenAgeText == null)
                return;
            if (!_settings.LastSuccessUtc.HasValue)
            {
                TokenAgeText.Text = UiStrings.T("SinceRenewNone");
                RefreshKaiTokenExpiryLabel();
                UpdateRegisterAgeText();
                return;
            }
            var span = DateTime.UtcNow - _settings.LastSuccessUtc.Value;
            if (span < TimeSpan.Zero) span = TimeSpan.Zero;
            var age = FormatAge(span);
            var text = UiStrings.T("SinceRenew") + age;
            if (_settings.LastTokenExpiryUtc.HasValue)
            {
                text += "  |  " + UiStrings.T("ExpiresAt") + _settings.LastTokenExpiryUtc.Value.ToLocalTime().ToString("g");
            }
            else if (_settings.LastTokenExpiresInSeconds > 0)
            {
                text += "  |  " + UiStrings.T("Ttl") + FormatTtl(_settings.LastTokenExpiresInSeconds);
            }
            TokenAgeText.Text = text;
            RefreshKaiTokenExpiryLabel();
            UpdateRegisterAgeText();
        }

        private void UpdateRegisterAgeText()
        {
            if (RegisterAgeText == null)
                return;
            RegisterAgeText.Inlines.Clear();
            if (_settings == null)
            {
                RegisterAgeText.ToolTip = null;
                return;
            }

            string okTip = null;
            if (!_settings.LastRegisterOkUtc.HasValue)
            {
                RegisterAgeText.Inlines.Add(new Run(UiStrings.T("RegisterOkNone")));
            }
            else
            {
                var span = DateTime.UtcNow - _settings.LastRegisterOkUtc.Value;
                if (span < TimeSpan.Zero) span = TimeSpan.Zero;
                var local = _settings.LastRegisterOkUtc.Value.ToLocalTime();
                RegisterAgeText.Inlines.Add(new Run(UiStrings.T("RegisterOkAge") + FormatAge(span)));
                okTip = local.ToString("g");
            }

            var showFail = _settings.LastRegisterFailUtc.HasValue
                && (!_settings.LastRegisterOkUtc.HasValue
                    || _settings.LastRegisterFailUtc.Value > _settings.LastRegisterOkUtc.Value);
            string failTip = null;
            if (showFail)
            {
                var span = DateTime.UtcNow - _settings.LastRegisterFailUtc.Value;
                if (span < TimeSpan.Zero) span = TimeSpan.Zero;
                var failRun = new Run("   ·   " + UiStrings.T("RegisterFailAge") + FormatAge(span));
                try { failRun.Foreground = (Brush)FindResource("StatusErrorBrush"); }
                catch { failRun.Foreground = Brushes.OrangeRed; }
                RegisterAgeText.Inlines.Add(failRun);
                failTip = _settings.LastRegisterFailUtc.Value.ToLocalTime().ToString("g");
            }

            if (okTip != null && failTip != null)
                RegisterAgeText.ToolTip = okTip + "  ·  " + failTip;
            else
                RegisterAgeText.ToolTip = okTip ?? (object)failTip;

            var okTicks = _settings.LastRegisterOkUtc.HasValue ? _settings.LastRegisterOkUtc.Value.Ticks : 0L;
            var failTicks = _settings.LastRegisterFailUtc.HasValue ? _settings.LastRegisterFailUtc.Value.Ticks : 0L;
            MaybeBlinkRegisterAge(okTicks + "|" + failTicks);

            QueueFooterPublish();
        }

    }
}
