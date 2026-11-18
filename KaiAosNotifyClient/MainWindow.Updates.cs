using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using KaiAosRenew;
using Velopack;
using WinForms = System.Windows.Forms;

namespace KaiAosNotifyClient
{
    public partial class MainWindow : Window
    {

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
            try { await Task.Delay(1500); }
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
            try { await RefreshUpdateFeedAsync(true); }
            catch { }
            if (autoApply && peeked != null && !_clientUpdateBusy)
            {
                _clientUpdateBusy = true;
                UpdateAosControlsEnabled();
                if (FooterUpdateBtn != null) FooterUpdateBtn.IsEnabled = false;
                var ver = UpdateService.FormatUpdateVersion(peeked);
                try
                {
                    SendClientUpdating(ver);
                    await UpdateService.ApplyUpdateAsync(this, peeked, ClientUiStrings.T, false,
                        (stage, pct) => ShowSelfUpdateProgress(stage, ver, pct),
                        PrepareForRestart);
                }
                catch
                {
                    SendClientUpdateDone();
                    HideUpdateProgressBar();
                    ShowPendingUpdate(_pendingUpdate ?? peeked);
                }
                finally
                {
                    _clientUpdateBusy = false;
                    UpdateAosControlsEnabled();
                    if (FooterUpdateBtn != null && _pendingUpdate != null)
                        FooterUpdateBtn.IsEnabled = true;
                }
            }
        }

        private void ShowSelfUpdateProgress(string stage, string version, int percent)
        {
            if (UpdateAvailableText == null) return;
            var ver = version ?? "?";
            if (string.Equals(stage, "downloading", StringComparison.OrdinalIgnoreCase))
            {
                var pct = percent < 0 ? 0 : (percent > 100 ? 100 : percent);
                UpdateAvailableText.Text = string.Format(ClientUiStrings.T("UpdateDownloadingPct"), ver, pct);
                if (UpdateProgressBar != null)
                {
                    UpdateProgressBar.IsIndeterminate = false;
                    UpdateProgressBar.Value = pct;
                    UpdateProgressBar.Visibility = Visibility.Visible;
                }
            }
            else if (string.Equals(stage, "installing", StringComparison.OrdinalIgnoreCase))
            {
                UpdateAvailableText.Text = string.Format(ClientUiStrings.T("UpdateInstalling"), ver);
                if (UpdateProgressBar != null)
                {
                    UpdateProgressBar.IsIndeterminate = false;
                    UpdateProgressBar.Value = 100;
                    UpdateProgressBar.Visibility = Visibility.Visible;
                }
            }
            else
            {
                UpdateAvailableText.Text = string.Format(ClientUiStrings.T("UpdateRestarting"), ver);
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
                RefreshTrayOutdatedIcon();
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
                FooterUpdateBtn.Content = string.Format(ClientUiStrings.T("UpdateClientBtn"), ver);
                FooterUpdateBtn.ToolTip = string.Format(ClientUiStrings.T("UpdateChannelTooltip"), UpdateService.ClientChannel);
                FooterUpdateBtn.Visibility = Visibility.Visible;
                FooterUpdateBtn.IsEnabled = !_clientUpdateBusy;
            }
            RefreshTrayOutdatedIcon();
        }

        private async void FooterUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (_clientUpdateBusy) return;
            var update = _pendingUpdate;
            if (update == null)
            {
                try { update = await UpdateService.PeekAsync(); }
                catch { update = null; }
                if (update == null)
                {
                    MessageBox.Show(this, ClientUiStrings.T("UpdateNone"),
                        ClientUiStrings.T("CheckForUpdates"), MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                ShowPendingUpdate(update);
            }
            await BeginClientSelfUpdateAsync(update);
        }

        private async Task BeginClientSelfUpdateAsync(UpdateInfo update, bool prompt = true)
        {
            if (update == null || _clientUpdateBusy) return;
            var ver = UpdateService.FormatUpdateVersion(update);
            if (!UpdateService.IsNewerVersion(ver, VersionDisplay))
                return;
            if (prompt)
            {
                var confirm = string.Format(ClientUiStrings.T("UpdateAvailable"), ver);
                var result = MessageBox.Show(this, confirm, ClientUiStrings.T("CheckForUpdates"),
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes) return;
            }

            _clientUpdateBusy = true;
            UpdateAosControlsEnabled();
            if (FooterUpdateBtn != null) FooterUpdateBtn.IsEnabled = false;
            if (CheckForUpdatesBtn != null) CheckForUpdatesBtn.IsEnabled = false;
            try
            {
                SendClientUpdating(ver);
                await UpdateService.ApplyUpdateAsync(this, update, ClientUiStrings.T, false,
                    (stage, pct) => ShowSelfUpdateProgress(stage, ver, pct),
                    PrepareForRestart);
            }
            catch (Exception ex)
            {
                SendClientUpdateDone();
                HideUpdateProgressBar();
                ShowPendingUpdate(_pendingUpdate ?? update);
                if (prompt)
                {
                    MessageBox.Show(this, string.Format(ClientUiStrings.T("UpdateFailed"), ex.Message),
                        ClientUiStrings.T("CheckForUpdates"), MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    AppendActivityRow(null, "error", string.Format(ClientUiStrings.T("UpdateFailed"), ex.Message));
                }
            }
            finally
            {
                _clientUpdateBusy = false;
                UpdateAosControlsEnabled();
                if (FooterUpdateBtn != null && _pendingUpdate != null)
                    FooterUpdateBtn.IsEnabled = true;
                if (CheckForUpdatesBtn != null) CheckForUpdatesBtn.IsEnabled = true;
            }
        }

        /// <summary>Server pushed clientUpdate — apply only when a newer package exists.</summary>
        private async void HandleForcedClientUpdate(NotifyMessage msg)
        {
            if (_clientUpdateBusy) return;
            var hinted = msg != null ? (msg.Text ?? "").Trim() : "";
            if (!string.IsNullOrEmpty(hinted) && !UpdateService.IsNewerVersion(hinted, VersionDisplay))
                return;
            UpdateInfo update = null;
            try { update = await UpdateService.PeekAsync(); }
            catch { return; }
            if (update == null) return;
            var ver = UpdateService.FormatUpdateVersion(update);
            if (!UpdateService.IsNewerVersion(ver, VersionDisplay))
                return;
            ShowPendingUpdate(update);
            await BeginClientSelfUpdateAsync(update, false);
        }

        private void SendClientUpdating(string version)
        {
            if (_client == null || !_client.IsConnected) return;
            try
            {
                _client.Send(new NotifyMessage
                {
                    Kind = "clientUpdating",
                    Level = "info",
                    Title = "KAI - AOS Renew - Client",
                    Text = version ?? ""
                });
            }
            catch { }
        }

        private void SendClientUpdateDone()
        {
            if (_client == null || !_client.IsConnected) return;
            try
            {
                _client.Send(new NotifyMessage
                {
                    Kind = "clientUpdateDone",
                    Level = "info",
                    Title = "KAI - AOS Renew - Client",
                    Text = ""
                });
            }
            catch { }
        }

        private async Task RefreshUpdateFeedAsync(bool sendToServer)
        {
            if (_serverUpdateCheckBusy) return;
            _serverUpdateCheckBusy = true;
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
                RefreshServerUpdateFromFeed();
                await ApplyFeedToSelfAsync();
                if (sendToServer)
                    SendUpdateFeed();
            }
            finally
            {
                _serverUpdateCheckBusy = false;
            }
        }

        private void ApplyCachedFeedVersions(string serverVer, string clientVer)
        {
            MergeFeedVersion(ref _feedServerVer, serverVer);
            MergeFeedVersion(ref _feedClientVer, clientVer);
            RefreshServerUpdateFromFeed();
            var ignore = ApplyFeedToSelfAsync();
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

        private async Task ApplyFeedToSelfAsync()
        {
            if (string.IsNullOrWhiteSpace(_feedClientVer)) return;
            if (!UpdateService.IsNewerVersion(_feedClientVer, VersionDisplay)) return;
            try
            {
                var update = await UpdateService.PeekAsync();
                if (update != null)
                    ShowPendingUpdate(update);
            }
            catch { }
        }

        private void SendUpdateFeed()
        {
            if (_client == null || !_client.IsConnected) return;
            if (string.IsNullOrEmpty(_feedServerVer) && string.IsNullOrEmpty(_feedClientVer)) return;
            try
            {
                _client.Send(new NotifyMessage
                {
                    Kind = "updateFeed",
                    Level = "info",
                    Title = "KAI - AOS Renew - Client",
                    Text = NotifyMessage.EncodeUpdateFeed(_feedServerVer, _feedClientVer)
                });
            }
            catch { }
        }

        private void RequestUpdateFeed()
        {
            if (_client == null || !_client.IsConnected) return;
            try
            {
                _client.Send(new NotifyMessage
                {
                    Kind = "updateFeedReq",
                    Level = "info",
                    Title = "KAI - AOS Renew - Client",
                    Text = ""
                });
            }
            catch { }
        }

        private void RefreshServerUpdateFromFeed()
        {
            var footer = _serverVersionFromFooter ?? "";
            if (!string.IsNullOrEmpty(_feedServerVer)
                && !string.IsNullOrEmpty(footer)
                && UpdateService.IsNewerVersion(_feedServerVer, footer))
            {
                _latestServerReleaseVer = _feedServerVer;
            }
            else if (!string.IsNullOrEmpty(_feedServerVer)
                && !string.IsNullOrEmpty(footer)
                && !UpdateService.IsNewerVersion(_feedServerVer, footer))
            {
                // Feed proves server is current (or feed is not newer) — clear offer.
                _latestServerReleaseVer = "";
            }
            else if (!string.IsNullOrEmpty(_latestServerReleaseVer)
                && !string.IsNullOrEmpty(footer)
                && UpdateService.IsNewerVersion(_latestServerReleaseVer, footer))
            {
                // Sticky: keep existing offer while still newer than footer
                // (empty/stale temporary _feedServerVer must not wipe it).
            }
            else if (!string.IsNullOrEmpty(_latestServerReleaseVer)
                && !string.IsNullOrEmpty(footer)
                && !UpdateService.IsNewerVersion(_latestServerReleaseVer, footer))
            {
                _latestServerReleaseVer = "";
            }
            RefreshServerUpdateUi();
        }

        private void RefreshServerUpdateUi()
        {
            var show = !_awaitingServerUpdateReconnect
                && !string.IsNullOrEmpty(_latestServerReleaseVer)
                && !string.IsNullOrEmpty(_serverVersionFromFooter)
                && UpdateService.IsNewerVersion(_latestServerReleaseVer, _serverVersionFromFooter);
            if (ServerUpdateAvailableText != null)
            {
                ServerUpdateAvailableText.Text = "";
                ServerUpdateAvailableText.Visibility = Visibility.Collapsed;
            }
            if (FooterServerUpdateBtn != null)
            {
                FooterServerUpdateBtn.Content = show
                    ? string.Format(ClientUiStrings.T("UpdateServerBtn"), _latestServerReleaseVer)
                    : ClientUiStrings.T("UpdateServer");
                FooterServerUpdateBtn.ToolTip = show
                    ? string.Format(ClientUiStrings.T("UpdateChannelTooltip"), UpdateService.ServerChannel)
                    : null;
                FooterServerUpdateBtn.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            }
            UpdateAosControlsEnabled();
            RefreshTrayOutdatedIcon();
        }

        private void BeginAwaitServerUpdate(string targetVer)
        {
            _awaitingServerUpdateReconnect = true;
            if (!string.IsNullOrEmpty(targetVer))
                _pendingServerUpdateVer = targetVer;
            else if (string.IsNullOrEmpty(_pendingServerUpdateVer))
                _pendingServerUpdateVer = _latestServerReleaseVer ?? "";
            _latestServerReleaseVer = "";
            if (ServerUpdateAvailableText != null)
                ServerUpdateAvailableText.Visibility = Visibility.Collapsed;
            if (FooterServerUpdateBtn != null)
            {
                FooterServerUpdateBtn.Visibility = Visibility.Collapsed;
                FooterServerUpdateBtn.IsEnabled = false;
            }
            SetConnStatus(ClientUiStrings.T("ServerRestarting"));
            RefreshTrayOutdatedIcon();
            UpdateAosControlsEnabled();
        }

        private void HandleServerUpdateFailed(string error)
        {
            _awaitingServerUpdateReconnect = false;
            _pendingServerUpdateVer = "";
            SetConnStatus("");
            var detail = string.IsNullOrEmpty(error) ? "?" : error;
            AppendActivityRow(null, "error",
                string.Format(ClientUiStrings.T("UpdateServerFailed"), detail));
            JumpToActivityWithAlert();
            RefreshServerUpdateFromFeed();
            UpdateAosControlsEnabled();
        }

        private void FooterServerUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_latestServerReleaseVer)) return;
            if (_client == null || !_client.IsConnected)
            {
                MessageBox.Show(this, ClientUiStrings.T("UpdateServerSendFail"),
                    ClientUiStrings.T("UpdateServer"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var targetVer = _latestServerReleaseVer;
            var msg = string.Format(ClientUiStrings.T("UpdateServerConfirm"), targetVer);
            var result = MessageBox.Show(this, msg, ClientUiStrings.T("UpdateServer"),
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            if (FooterServerUpdateBtn != null) FooterServerUpdateBtn.IsEnabled = false;
            _pendingServerUpdateVer = targetVer;
            var ok = _client.Send(new NotifyMessage
            {
                Kind = "serverUpdate",
                Level = "info",
                Title = "KAI - AOS Renew - Client",
                Text = targetVer ?? ""
            });
            if (!ok)
            {
                _pendingServerUpdateVer = "";
                if (FooterServerUpdateBtn != null) FooterServerUpdateBtn.IsEnabled = true;
                MessageBox.Show(this, ClientUiStrings.T("UpdateServerSendFail"),
                    ClientUiStrings.T("UpdateServer"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            BeginAosAckWait();
        }

        private async Task FinishServerUpdateReconnectAsync()
        {
            if (!_awaitingServerUpdateReconnect) return;
            SetConnStatus(ClientUiStrings.T("UpdateServerVerifying"));
            await RefreshUpdateFeedAsync(true);
            if (!_awaitingServerUpdateReconnect) return;

            var expected = _pendingServerUpdateVer ?? "";
            var actual = _serverVersionFromFooter ?? "";
            _awaitingServerUpdateReconnect = false;
            _pendingServerUpdateVer = "";

            if (!string.IsNullOrEmpty(actual)
                && (string.IsNullOrEmpty(expected)
                    || string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)
                    || !UpdateService.IsNewerVersion(expected, actual)))
            {
                AppendActivityRow(null, "success",
                    string.Format(ClientUiStrings.T("UpdateServerDone"), actual));
                SetConnStatus("");
            }
            else
            {
                AppendActivityRow(null, "warning",
                    string.Format(ClientUiStrings.T("UpdateServerStillOld"),
                        string.IsNullOrEmpty(actual) ? "?" : actual));
                SetConnStatus("");
                JumpToActivityWithAlert();
            }
            RefreshServerUpdateUi();
        }

        private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
        {
            if (_clientUpdateBusy) return;
            if (CheckForUpdatesBtn != null) CheckForUpdatesBtn.IsEnabled = false;
            try
            {
                var update = await UpdateService.PeekAsync();
                ShowPendingUpdate(update);
                try { await RefreshUpdateFeedAsync(true); }
                catch { }
                if (update == null)
                {
                    // Server offer may be active — don't claim "latest" or pump MessageBox over it.
                    var serverOffer = !_awaitingServerUpdateReconnect
                        && !string.IsNullOrEmpty(_latestServerReleaseVer)
                        && !string.IsNullOrEmpty(_serverVersionFromFooter)
                        && UpdateService.IsNewerVersion(_latestServerReleaseVer, _serverVersionFromFooter);
                    if (!serverOffer)
                    {
                        MessageBox.Show(this, ClientUiStrings.T("UpdateNone"),
                            ClientUiStrings.T("CheckForUpdates"), MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    return;
                }
                await BeginClientSelfUpdateAsync(update);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(ClientUiStrings.T("UpdateFailed"), ex.Message),
                    ClientUiStrings.T("CheckForUpdates"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                if (CheckForUpdatesBtn != null && !_clientUpdateBusy)
                    CheckForUpdatesBtn.IsEnabled = true;
            }
        }

        private void UpdateKeysTodayBadge()
        {
            if (KeysTodayBadge == null) return;
            var today = DateTime.Today;
            var count = 0;
            foreach (var row in _keyAllRows)
            {
                if (row != null && row.DetectedLocal.Date == today)
                    count++;
            }
            KeysTodayBadge.Text = count > 0
                ? string.Format(ClientUiStrings.T("KeysTodayBadge"), count)
                : "";
        }

        private void UpdateKeysRetryButton()
        {
            if (KeysRetryBmwBtn == null) return;
            var on = _client != null && _client.IsConnected;
            var newest = _keyAllRows.Count > 0 ? _keyAllRows[0] : null;
            var failed = newest != null && string.Equals(newest.Status, "Failed", StringComparison.OrdinalIgnoreCase);
            var enable = on && failed;
            KeysRetryBmwBtn.IsEnabled = enable;
            if (!on)
                KeysRetryBmwBtn.ToolTip = ClientUiStrings.T("TipAosOffline");
            else
                KeysRetryBmwBtn.ToolTip = ClientUiStrings.T("KeysRetryBmwTip");
        }

        private static void UpdateEmptyHint(TextBlock hint, bool show)
        {
            if (hint == null) return;
            hint.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateClearButtonsEnabled()
        {
            if (KeysClearBtn != null)
                KeysClearBtn.IsEnabled = _keyAllRows != null && _keyAllRows.Count > 0;
            if (ActivityClearBtn != null)
                ActivityClearBtn.IsEnabled = _activityRows != null && _activityRows.Count > 0;
            if (ClearRegisterLogBtn != null)
                ClearRegisterLogBtn.IsEnabled = _registerHistory != null && _registerHistory.Count > 0;
        }

        private static void UpdateNoMatchesHint(TextBlock hint, bool hasSourceItems, int visibleCount)
        {
            if (hint == null) return;
            hint.Visibility = (hasSourceItems && visibleCount == 0)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void UpdateAosButtonsVisibility()
        {
            if (ButtonsBorder != null)
                ButtonsBorder.Visibility = Visibility.Collapsed;
        }

        private void UpdateAosControlsEnabled()
        {
            var on = _client != null && _client.IsConnected;
            var updating = _awaitingServerUpdateReconnect || _clientUpdateBusy;
            var busy = _aosAwaitingAck || _kaiBusy;
            if (AosIntervalBox != null) AosIntervalBox.IsEnabled = on && !updating;
            if (AosEmailBox != null) AosEmailBox.IsEnabled = on && !updating;
            if (AosPasswordBox != null) AosPasswordBox.IsEnabled = on && !updating;
            if (AosPasswordPlainBox != null) AosPasswordPlainBox.IsEnabled = on && !updating;
            if (AosShowPasswordCheck != null) AosShowPasswordCheck.IsEnabled = on && !updating;

            string saveTip = null;
            var saveOn = on && !updating && !_aosAwaitingAck;
            if (!on) saveTip = "TipAosOffline";
            else if (updating) saveTip = "TipAosUpdating";
            else if (_aosAwaitingAck) saveTip = "TipAosBusy";
            SetActionEnabled(AosSaveBtn, saveOn, saveTip);

            string cmdTip = null;
            var cmdOn = on && !updating && !busy;
            if (!on) cmdTip = "TipAosOffline";
            else if (updating) cmdTip = "TipAosUpdating";
            else if (busy) cmdTip = "TipAosBusy";

            SetActionEnabled(AosCheckTokenBtn, cmdOn, cmdTip);
            SetActionEnabled(AosStartTimerBtn, cmdOn, cmdTip);
            SetActionEnabled(AosStopTimerBtn, cmdOn, cmdTip);
            SetActionEnabled(ForceRegisterBtn, cmdOn, cmdTip, "TipForceRegister");
            SetActionEnabled(KaiRefreshBtn, cmdOn, cmdTip);
            SetActionEnabled(KaiFixServicesBtn, on && !updating && !_kaiBusy,
                !on ? "TipAosOffline" : (updating ? "TipAosUpdating" : (_kaiBusy ? "TipAosBusy" : null)));
            SetActionEnabled(KaiStopServicesBtn, on && !updating && !_kaiBusy,
                !on ? "TipAosOffline" : (updating ? "TipAosUpdating" : (_kaiBusy ? "TipAosBusy" : null)));

            SetActionEnabled(KeysRefreshBtn, on && !updating,
                !on ? "TipAosOffline" : (updating ? "TipAosUpdating" : null));
            SetActionEnabled(ActivityRefreshBtn, on && !updating,
                !on ? "TipAosOffline" : (updating ? "TipAosUpdating" : null));
            SetActionEnabled(RegisterRefreshBtn, on && !updating,
                !on ? "TipAosOffline" : (updating ? "TipAosUpdating" : null));

            if (AosCheckBrowserPassBtn != null) AosCheckBrowserPassBtn.IsEnabled = true;
            if (AosOpenBmwBtn != null) AosOpenBmwBtn.IsEnabled = true;

            if (FooterServerUpdateBtn != null)
            {
                var show = FooterServerUpdateBtn.Visibility == Visibility.Visible;
                var enable = on && show && !updating && !busy;
                FooterServerUpdateBtn.IsEnabled = enable;
                if (!enable)
                {
                    if (!on) FooterServerUpdateBtn.ToolTip = ClientUiStrings.T("TipAosOffline");
                    else if (updating) FooterServerUpdateBtn.ToolTip = ClientUiStrings.T("TipAosUpdating");
                    else if (busy) FooterServerUpdateBtn.ToolTip = ClientUiStrings.T("TipAosBusy");
                }
                else
                {
                    FooterServerUpdateBtn.ToolTip = string.Format(
                        ClientUiStrings.T("UpdateChannelTooltip"), UpdateService.ServerChannel);
                }
            }

            UpdateKeysRetryButton();
        }

        private void UpdateAosLoginStatus(string tokenMode)
        {
            _lastTokenMode = string.IsNullOrEmpty(tokenMode) ? "none" : tokenMode;
            if (AosLoginStatusText == null && AosLoginStatusIcon == null) return;
            var connected = string.Equals(_lastTokenMode, "ok", StringComparison.OrdinalIgnoreCase)
                && !_authBlockedOn;
            var fail = _authBlockedOn
                || string.Equals(_lastTokenMode, "fail", StringComparison.OrdinalIgnoreCase)
                || string.Equals(_lastTokenMode, "error", StringComparison.OrdinalIgnoreCase);
            Brush brush;
            string glyph;
            string text;
            if (connected)
            {
                text = ClientUiStrings.T("AosLoginConnected");
                brush = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
                glyph = "\uE73E";
            }
            else
            {
                text = ClientUiStrings.T("AosLoginNotConnected");
                if (fail)
                {
                    brush = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
                    glyph = "\uE711";
                }
                else
                {
                    brush = new SolidColorBrush(Color.FromRgb(0x8C, 0x8C, 0x8C));
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

        private void UpdateAosSyncStatus()
        {
            if (AosDirtyStatus != null)
            {
                AosDirtyStatus.Text = _aosDirty ? ClientUiStrings.T("AosSyncDirty") : "";
                if (_aosDirty)
                    AosDirtyStatus.Foreground = CreateBrush(0xB2, 0x6A, 0x00);
                else
                    AosDirtyStatus.Foreground = _footerMuted
                        ?? (TryFindResource("TextMutedBrush") as Brush)
                        ?? CreateBrush(0x8C, 0x8C, 0x8C);
            }
            if (AosDirtyPendingMark != null)
                AosDirtyPendingMark.Visibility = _aosDirty ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateActivityAlertDot()
        {
            if (ActivityTabAlertDot == null) return;
            ActivityTabAlertDot.Visibility = _activityAlert ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateConnectTargetEnabled()
        {
            var locked = _wantConnectUi
                || (_client != null && (_client.IsConnected || _client.WantConnect));
            if (HostCombo != null) HostCombo.IsEnabled = !locked;
            if (PortBox != null) PortBox.IsEnabled = !locked;
            if (ScanLanBtn != null) ScanLanBtn.IsEnabled = !locked;
        }

        private void UpdateConnectButtonLabel()
        {
            if (ConnectBtn == null) return;
            var connecting = _wantConnectUi && (_client == null || !_client.IsConnected);
            ConnectBtn.Content = ClientUiStrings.T(connecting ? "Connecting" : "Connect");
        }

        private void UpdateKeysAlert()
        {
            _keysAlert = false;
            var ev = _keyAllRows.Count > 0 ? _keyAllRows[0] : null;
            if (ev != null)
            {
                var age = DateTime.Now - ev.DetectedLocal;
                if (age.TotalMinutes < KeysAlertMaxMinutes)
                {
                    if (string.Equals(ev.Status, "Failed", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(ev.Status, "Waiting", StringComparison.OrdinalIgnoreCase))
                    {
                        _keysAlert = true;
                    }
                    else if (ev.LocalSent && !ev.BmwUploaded && !ev.Failed
                        && age.TotalMinutes > KeysStallMinutes)
                    {
                        _keysAlert = true;
                    }
                }
            }
            if (KeysTabAlertDot != null)
                KeysTabAlertDot.Visibility = _keysAlert ? Visibility.Visible : Visibility.Collapsed;
            SyncAlertBlinkTimer();
        }

        private void UpdateServiceActionButtons(bool allOk, bool anyRunning)
        {
            if (KaiFixServicesBtn != null)
                KaiFixServicesBtn.Visibility = allOk ? Visibility.Collapsed : Visibility.Visible;
            if (KaiStopServicesBtn != null)
                KaiStopServicesBtn.Visibility = anyRunning ? Visibility.Visible : Visibility.Collapsed;
            UpdateAosControlsEnabled();
        }

        private void UpdateVinCopyIconVisibility(Button btn)
        {
            if (btn == null) return;
            var row = btn.DataContext as ClientKeyRow;
            var tipText = ClientUiStrings.T("CopyVin");
            var tip = btn.ToolTip as ToolTip;
            if (tip != null)
            {
                var tb = tip.Content as TextBlock;
                if (tb != null) tb.Text = tipText;
            }
            else
                btn.ToolTip = tipText;
            btn.Visibility = (row != null && !string.IsNullOrEmpty(row.Vin))
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void UpdateKmCopyIconVisibility(Button btn)
        {
            if (btn == null) return;
            var row = btn.DataContext as ClientKeyRow;
            var tipText = ClientUiStrings.T("KeysCopyKm");
            var tip = btn.ToolTip as ToolTip;
            if (tip != null)
            {
                var tb = tip.Content as TextBlock;
                if (tb != null) tb.Text = tipText;
            }
            else
                btn.ToolTip = tipText;
            btn.Visibility = (row != null && row.MileageKm.HasValue && row.MileageKm.Value > 0)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void ShowVinCopyIconFeedback(Button btn)
        {
            if (btn == null) return;
            ResetVinCopyIconFeedback();
            _keysCopyFeedbackBtn = btn;
            btn.Content = KeysCopyOkGlyph;
            btn.Foreground = KeysCopyOkBrush;
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

        private void UpdateSrvSettingsResetEnabled()
        {
            UpdateSrvSettingsActionButtons();
        }

        private void UpdateSrvSettingsActionButtons()
        {
            var online = _client != null && _client.IsConnected;
            var hasMap = _lastServerSettingsMap != null && _lastServerSettingsMap.Count > 0;
            if (SrvSettingsSaveBtn != null)
            {
                SrvSettingsSaveBtn.IsEnabled = online && _srvSettingsDirty;
                SrvSettingsSaveBtn.FontWeight = _srvSettingsDirty ? FontWeights.SemiBold : FontWeights.Normal;
            }
            if (SrvSettingsResetBtn != null)
                SrvSettingsResetBtn.IsEnabled = online && _srvSettingsDirty && hasMap;
        }

        private void UpdateServerSettingsEnabled()
        {
            var online = _client != null && _client.IsConnected;
            var editable = new UIElement[]
            {
                SrvLanguageCombo, SrvThemeCombo, SrvExportBtn, SrvImportBtn,
                SrvStartWithWindowsCheck, SrvMinimizeToTrayCheck, SrvMinimizeOnCloseCheck, SrvAutoUpdateCheck,
                SrvKeysRefreshBox, SrvKeysVinTipBox, SrvKeysStallBox,
                SrvKeysBalloonCheck, SrvKeysVinBmwFailOnlyCheck, SrvKeysAutoCopyCheck, SrvKeysStallAlertCheck,
                SrvBrowserPassHintCheck, SrvUseSystemProxyCheck, SrvAuthCooldownBox, SrvAuthBlockAfterBox,
                SrvKrLogLevelCombo,
                SrvNotifySendToCombo, SrvNotifyRefreshBtn,
                SrvNotifyTestVinBtn, SrvNotifyTestInfoBtn, SrvNotifyTestWarningBtn, SrvNotifyTestErrorBtn,
                SrvUpdateAllClientsBtn
            };
            for (var i = 0; i < editable.Length; i++)
            {
                if (editable[i] != null)
                    editable[i].IsEnabled = online;
            }
            UpdateSrvSettingsActionButtons();
        }

    }
}
