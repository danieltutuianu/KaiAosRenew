using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace KaiAosRenew
{
    public partial class MainWindow : Window
    {
        private const int FirebaseSafetyPollMinutes = 60;
        private const int FirebaseSseMaxBackoffMs = 300000; // 5 minutes
        private CancellationTokenSource _firebaseListenCts;
        private int _firebaseListenGeneration;
        private string _firebaseStatusText = "";
        private readonly DispatcherTimer _firebaseSafetyTimer = new DispatcherTimer();
        private volatile bool _firebaseSseConnected;
        private bool _firebaseSyncModeComboReady;

        private sealed class FirebaseSyncModeItem
        {
            public string Code { get; set; }
            public string Name { get; set; }
        }

        private void InitFirebasePush()
        {
            _firebaseSafetyTimer.Tick -= FirebaseSafetyTimer_Tick;
            _firebaseSafetyTimer.Tick += FirebaseSafetyTimer_Tick;
            _firebaseSafetyTimer.Interval = TimeSpan.FromMinutes(FirebaseSafetyPollMinutes);
            EnsureFirebaseSyncModeComboItems();
            ApplyFirebasePushFromSettings(true);
        }

        private bool IsFirebasePushActive()
        {
            return _settings != null && _settings.FirebasePushEnabled;
        }

        private bool IsPeerRelayActive()
        {
            return IsFirebasePushActive();
        }

        private string GetFirebasePushSyncMode()
        {
            if (_settings == null) return PeerSyncLogic.SyncModeOnDemand;
            return PeerSyncLogic.NormalizeFirebasePushSyncMode(_settings.FirebasePushSyncMode);
        }

        private bool IsFirebasePushPollingMode()
        {
            return string.Equals(GetFirebasePushSyncMode(), PeerSyncLogic.SyncModePoll, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsFirebasePushSseMode()
        {
            return string.Equals(GetFirebasePushSyncMode(), PeerSyncLogic.SyncModeSse, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsFirebasePushOnDemandMode()
        {
            return string.Equals(GetFirebasePushSyncMode(), PeerSyncLogic.SyncModeOnDemand, StringComparison.OrdinalIgnoreCase);
        }

        private bool TryGetActivePeerRelay(out string url, out string secret, out string serverName)
        {
            url = "";
            secret = "";
            serverName = "";
            if (_settings == null || !IsFirebasePushActive()) return false;
            url = _settings.FirebasePushUrl ?? "";
            try { secret = _settings.GetFirebasePushSecret() ?? ""; }
            catch { secret = ""; }
            serverName = _settings.FirebasePushServerName ?? "";
            return !string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(secret);
        }

        private void ResetFirebaseEtagsIfScopeChanged(string url, string secret)
        {
            if (_settings == null) return;
            var scope = (url ?? "").Trim() + "\n" + (secret ?? "");
            if (string.Equals(scope, _settings.FirebasePushEtagScope ?? "", StringComparison.Ordinal))
                return;
            _settings.FirebasePushEtagScope = scope;
            _settings.FirebasePushCredsEtag = "";
            _settings.FirebasePushSeqEtag = "";
            try { _settings.Save(); } catch { }
        }

        private void ClearFirebaseEtagsPersistent()
        {
            if (_settings == null) return;
            _settings.FirebasePushCredsEtag = "";
            _settings.FirebasePushSeqEtag = "";
            try { _settings.Save(); } catch { }
        }

        private void PersistFirebaseCredsEtag(string etag)
        {
            if (_settings == null || string.IsNullOrEmpty(etag)) return;
            if (string.Equals(_settings.FirebasePushCredsEtag, etag, StringComparison.Ordinal)) return;
            _settings.FirebasePushCredsEtag = etag;
            try { _settings.Save(); } catch { }
        }

        private void PersistFirebaseSeqEtag(string etag)
        {
            if (_settings == null || string.IsNullOrEmpty(etag)) return;
            if (string.Equals(_settings.FirebasePushSeqEtag, etag, StringComparison.Ordinal)) return;
            _settings.FirebasePushSeqEtag = etag;
            try { _settings.Save(); } catch { }
        }

        private void EnsureFirebaseSyncModeComboItems()
        {
            if (FirebasePushSyncModeCombo == null) return;
            var items = new List<FirebaseSyncModeItem>
            {
                new FirebaseSyncModeItem { Code = PeerSyncLogic.SyncModeOnDemand, Name = UiStrings.T("FirebasePushSyncModeOnDemand") },
                new FirebaseSyncModeItem { Code = PeerSyncLogic.SyncModePoll, Name = UiStrings.T("FirebasePushSyncModePoll") }
            };
            var prev = FirebasePushSyncModeCombo.SelectedValue as string;
            _firebaseSyncModeComboReady = false;
            FirebasePushSyncModeCombo.ItemsSource = items;
            if (!string.IsNullOrEmpty(prev))
                FirebasePushSyncModeCombo.SelectedValue = PeerSyncLogic.NormalizeFirebasePushSyncMode(prev);
            else if (_settings != null)
                FirebasePushSyncModeCombo.SelectedValue = GetFirebasePushSyncMode();
            else
                FirebasePushSyncModeCombo.SelectedValue = PeerSyncLogic.SyncModeOnDemand;
            _firebaseSyncModeComboReady = true;
            UpdateFirebasePollMinutesVisibility();
        }

        private void UpdateFirebasePollMinutesVisibility()
        {
            var poll = IsFirebasePushPollingMode()
                || (FirebasePushSyncModeCombo != null
                    && string.Equals(FirebasePushSyncModeCombo.SelectedValue as string, PeerSyncLogic.SyncModePoll, StringComparison.OrdinalIgnoreCase));
            var vis = poll ? Visibility.Visible : Visibility.Collapsed;
            if (FirebasePushPollMinutesBox != null) FirebasePushPollMinutesBox.Visibility = vis;
            if (FirebasePushPollMinutesSuffix != null) FirebasePushPollMinutesSuffix.Visibility = vis;
        }

        private void ApplyFirebasePushFromSettings(bool syncNow)
        {
            if (_settings == null) return;
            _settings.EnsurePeerSyncServerId();
            _settings.NormalizeClamps();
            UpdateFirebasePushStatusUi();
            UpdateFirebasePollMinutesVisibility();
            _firebaseSafetyTimer.Stop();
            StopFirebaseListen();

            if (!IsFirebasePushActive()) return;

            string url, secret, serverName;
            if (!TryGetActivePeerRelay(out url, out secret, out serverName))
                return;
            ResetFirebaseEtagsIfScopeChanged(url, secret);

            // Empty AOS email/password: offer to pull from Firebase, confirm, force Register, then save.
            if (IsLocalAosCredentialsIncomplete())
                Forget(TryBootstrapAosCredsFromFirebaseAsync(), "FirebaseBootstrapAos");

            if (IsFirebasePushOnDemandMode())
            {
                // Zero idle traffic — only Sync now / Publish / auth-fail pull / empty-local bootstrap.
                return;
            }

            if (IsFirebasePushPollingMode())
            {
                var mins = PeerSyncLogic.ClampPollMinutes(_settings.FirebasePushPollMinutes);
                _firebaseSafetyTimer.Interval = TimeSpan.FromMinutes(mins);
                _firebaseSafetyTimer.Start();
                if (syncNow)
                    Forget(PollSeqThenSnapshotAsync(false), "FirebasePollSync");
                return;
            }

            // SSE mode
            _firebaseSafetyTimer.Interval = TimeSpan.FromMinutes(FirebaseSafetyPollMinutes);
            _firebaseSafetyTimer.Start();
            StartFirebaseListen();
        }

        private void FirebaseSafetyTimer_Tick(object sender, EventArgs e)
        {
            if (!IsFirebasePushActive()) return;
            if (IsFirebasePushOnDemandMode()) return;
            if (IsFirebasePushPollingMode())
            {
                Forget(PollSeqThenSnapshotAsync(false), "FirebasePollTick");
                return;
            }
            if (_firebaseSseConnected) return;
            BeginPeerSyncPoll(false);
        }

        private void StopFirebaseListen()
        {
            _firebaseSseConnected = false;
            Interlocked.Increment(ref _firebaseListenGeneration);
            var cts = Interlocked.Exchange(ref _firebaseListenCts, null);
            if (cts == null) return;
            try { cts.Cancel(); }
            catch { }
            try { cts.Dispose(); }
            catch { }
        }

        private void StartFirebaseListen()
        {
            string url, secret, serverName;
            if (!TryGetActivePeerRelay(out url, out secret, out serverName))
                return;
            if (!IsFirebasePushSseMode())
                return;

            StopFirebaseListen();
            var gen = Interlocked.Increment(ref _firebaseListenGeneration);
            var cts = new CancellationTokenSource();
            _firebaseListenCts = cts;
            var listenUrl = url;
            var listenSecret = secret;
            Forget(Task.Run(async () =>
            {
                var delayMs = 2000;
                while (!cts.IsCancellationRequested && gen == _firebaseListenGeneration)
                {
                    if (!IsFirebasePushActive() || !IsFirebasePushSseMode()) break;
                    try
                    {
                        await PeerSyncFirebaseClient.ListenAsync(
                            listenUrl,
                            listenSecret,
                            () => { _firebaseSseConnected = true; },
                            creds =>
                            {
                                if (cts.IsCancellationRequested) return;
                                Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    if (!IsFirebasePushActive()) return;
                                    Forget(HandleInboundFromSseAsync(creds), "FirebaseSseDirect");
                                }));
                            },
                            () =>
                            {
                                if (cts.IsCancellationRequested) return;
                                Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    if (!IsFirebasePushActive()) return;
                                    Forget(BeginPeerSyncPollAsync(false), "FirebaseSse");
                                }));
                            },
                            cts.Token).ConfigureAwait(false);
                        delayMs = 2000;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch
                    {
                        // reconnect with backoff
                    }
                    finally
                    {
                        _firebaseSseConnected = false;
                    }
                    if (cts.IsCancellationRequested || gen != _firebaseListenGeneration) break;
                    if (!IsFirebasePushActive() || !IsFirebasePushSseMode()) break;
                    try { await Task.Delay(delayMs, cts.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                    if (delayMs < FirebaseSseMaxBackoffMs)
                        delayMs = Math.Min(FirebaseSseMaxBackoffMs, delayMs * 2);
                }
            }), "FirebaseListen");
        }

        private async Task PollSeqThenSnapshotAsync(bool force)
        {
            if (_settings == null || !IsPeerRelayActive()) return;
            string url, secret, serverName;
            if (!TryGetActivePeerRelay(out url, out secret, out serverName))
            {
                if (force)
                    SetActiveRelayStatus(string.Format(UiStrings.T("FirebasePushStatusFail"), "URL/secret"));
                return;
            }
            ResetFirebaseEtagsIfScopeChanged(url, secret);

            if (!force)
            {
                PeerRelayPollResult seqCheck;
                try
                {
                    seqCheck = await PeerSyncFirebaseClient.CheckLatestSeqAsync(
                        url, secret, _settings.FirebasePushSeqEtag, CancellationToken.None).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    SetActiveRelayStatus(string.Format(UiStrings.T("FirebasePushStatusFail"), ex.Message ?? "seq"));
                    return;
                }
                if (seqCheck != null)
                    PersistFirebaseSeqEtag(seqCheck.ETag);
                if (seqCheck == null || !seqCheck.Ok)
                {
                    SetActiveRelayStatus(string.Format(
                        UiStrings.T("FirebasePushStatusFail"),
                        seqCheck != null ? seqCheck.Error : "seq"));
                    return;
                }
                if (seqCheck.Unchanged || seqCheck.Seq <= _settings.PeerSyncLastSeq)
                {
                    SetActiveRelayStatus(UiStrings.T("FirebasePushStatusOk"));
                    return;
                }
            }

            await BeginPeerSyncPollAsync(force).ConfigureAwait(true);
        }

        private void FirebasePush_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingUi || _settings == null) return;
            if (sender == FirebasePushSyncModeCombo && !_firebaseSyncModeComboReady) return;
            CaptureFirebasePushFromUi();
            UpdateFirebasePollMinutesVisibility();
            try { _settings.Save(); }
            catch { }
            BroadcastServerSettings();
            ApplyFirebasePushFromSettings(true);
        }

        private void FirebasePushField_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_loadingUi || _settings == null) return;
            CaptureFirebasePushFromUi();
            try { _settings.Save(); }
            catch { }
            BroadcastServerSettings();
            ApplyFirebasePushFromSettings(false);
        }

        private void FirebasePushGenerate_Click(object sender, RoutedEventArgs e)
        {
            if (_settings == null) return;
            var ask = UiStrings.T("FirebasePushGenerateConfirm");
            var result = MessageBox.Show(this, ask, UiStrings.T("FirebasePushGenerate"),
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (result != MessageBoxResult.Yes) return;
            var secret = NotifyPairing.GenerateCode() + NotifyPairing.GenerateCode();
            _settings.SetFirebasePushSecret(secret);
            if (FirebasePushSecretBox != null)
                FirebasePushSecretBox.Text = secret;
            try { _settings.Save(); }
            catch { }
            BroadcastServerSettings();
            ApplyFirebasePushFromSettings(false);
        }

        private void FirebasePushUrlCopy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var url = FirebasePushUrlBox != null
                    ? (FirebasePushUrlBox.Text ?? "").Trim()
                    : (_settings != null ? (_settings.FirebasePushUrl ?? "") : "");
                if (string.IsNullOrEmpty(url)) return;
                NativeMethods.TrySetClipboardText(url);
            }
            catch { }
        }

        private void FirebasePushUrlPaste_Click(object sender, RoutedEventArgs e)
        {
            if (_loadingUi || _settings == null) return;
            try
            {
                var clip = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(clip)) return;
                if (FirebasePushUrlBox != null)
                    FirebasePushUrlBox.Text = clip.Trim();
                CaptureFirebasePushFromUi();
                try { _settings.Save(); }
                catch { }
                BroadcastServerSettings();
                ApplyFirebasePushFromSettings(false);
            }
            catch { }
        }

        private void FirebasePushCopy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var secret = FirebasePushSecretBox != null
                    ? (FirebasePushSecretBox.Text ?? "").Trim()
                    : (_settings != null ? (_settings.GetFirebasePushSecret() ?? "") : "");
                if (string.IsNullOrEmpty(secret)) return;
                NativeMethods.TrySetClipboardText(secret);
            }
            catch { }
        }

        private void FirebasePushSecretPaste_Click(object sender, RoutedEventArgs e)
        {
            if (_loadingUi || _settings == null) return;
            try
            {
                var clip = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(clip)) return;
                if (FirebasePushSecretBox != null)
                    FirebasePushSecretBox.Text = clip.Trim();
                CaptureFirebasePushFromUi();
                try { _settings.Save(); }
                catch { }
                BroadcastServerSettings();
                ApplyFirebasePushFromSettings(false);
            }
            catch { }
        }

        private async void FirebasePushNow_Click(object sender, RoutedEventArgs e)
        {
            if (!IsFirebasePushActive())
            {
                SetFirebasePushStatus(UiStrings.T("FirebasePushStatusDisabled"));
                return;
            }
            ClearFirebaseEtagsPersistent();
            if (IsFirebasePushPollingMode() || IsFirebasePushOnDemandMode())
                await PollSeqThenSnapshotAsync(true).ConfigureAwait(true);
            else
                await BeginPeerSyncPollAsync(true);
        }

        private async void FirebasePushTest_Click(object sender, RoutedEventArgs e)
        {
            if (!IsFirebasePushActive())
            {
                SetFirebasePushStatus(UiStrings.T("FirebasePushStatusDisabled"));
                return;
            }
            await RunFirebasePushTestAsync(0);
        }

        private async void FirebasePushPush_Click(object sender, RoutedEventArgs e)
        {
            if (!IsFirebasePushActive())
            {
                SetFirebasePushStatus(UiStrings.T("FirebasePushStatusDisabled"));
                return;
            }
            await PushPeerCredentialsNowAsync(0).ConfigureAwait(true);
        }

        private void CaptureFirebasePushFromUi()
        {
            if (_settings == null) return;
            _settings.FirebasePushEnabled = FirebasePushEnableCheck != null && FirebasePushEnableCheck.IsChecked == true;
            if (FirebasePushSyncModeCombo != null && FirebasePushSyncModeCombo.SelectedValue != null)
                _settings.FirebasePushSyncMode = PeerSyncLogic.NormalizeFirebasePushSyncMode(
                    FirebasePushSyncModeCombo.SelectedValue as string);
            if (FirebasePushPollMinutesBox != null)
            {
                int mins;
                if (!int.TryParse((FirebasePushPollMinutesBox.Text ?? "").Trim(), out mins))
                    mins = 360;
                _settings.FirebasePushPollMinutes = PeerSyncLogic.ClampPollMinutes(mins);
                FirebasePushPollMinutesBox.Text = _settings.FirebasePushPollMinutes.ToString();
            }
            if (FirebasePushUrlBox != null)
                _settings.FirebasePushUrl = (FirebasePushUrlBox.Text ?? "").Trim();
            if (FirebasePushSecretBox != null)
                _settings.SetFirebasePushSecret((FirebasePushSecretBox.Text ?? "").Trim());
            if (FirebasePushServerNameBox != null)
            {
                var name = (FirebasePushServerNameBox.Text ?? "").Trim();
                if (!string.IsNullOrEmpty(name))
                    _settings.FirebasePushServerName = name;
            }
            _settings.EnsurePeerSyncServerId();
            _settings.NormalizeClamps();
        }

        private void LoadFirebasePushToUi()
        {
            if (_settings == null) return;
            _settings.EnsurePeerSyncServerId();
            _settings.NormalizeClamps();
            EnsureFirebaseSyncModeComboItems();
            if (FirebasePushEnableCheck != null)
                FirebasePushEnableCheck.IsChecked = _settings.FirebasePushEnabled;
            _firebaseSyncModeComboReady = false;
            if (FirebasePushSyncModeCombo != null)
                FirebasePushSyncModeCombo.SelectedValue = GetFirebasePushSyncMode();
            _firebaseSyncModeComboReady = true;
            if (FirebasePushPollMinutesBox != null)
                FirebasePushPollMinutesBox.Text = PeerSyncLogic.ClampPollMinutes(_settings.FirebasePushPollMinutes).ToString();
            if (FirebasePushUrlBox != null)
                FirebasePushUrlBox.Text = _settings.FirebasePushUrl ?? "";
            if (FirebasePushSecretBox != null)
            {
                try { FirebasePushSecretBox.Text = _settings.GetFirebasePushSecret() ?? ""; }
                catch { FirebasePushSecretBox.Text = ""; }
            }
            if (FirebasePushServerNameBox != null)
                FirebasePushServerNameBox.Text = _settings.FirebasePushServerName ?? "";
            UpdateFirebasePollMinutesVisibility();
            UpdateFirebasePushStatusUi();
        }

        private void ApplyFirebasePushLanguage()
        {
            if (SettingsFirebasePushGroup != null)
                SettingsFirebasePushGroup.Header = UiStrings.T("SettingsFirebasePushGroup");
            if (FirebasePushEnableCheck != null)
                FirebasePushEnableCheck.Content = UiStrings.T("FirebasePushEnable");
            if (FirebasePushSyncModeLabel != null)
                FirebasePushSyncModeLabel.Text = UiStrings.T("FirebasePushSyncMode");
            if (FirebasePushSyncModeCombo != null)
                FirebasePushSyncModeCombo.ToolTip = UiStrings.T("FirebasePushSyncModeTip");
            EnsureFirebaseSyncModeComboItems();
            if (FirebasePushPollMinutesSuffix != null)
                FirebasePushPollMinutesSuffix.Text = UiStrings.T("FirebasePushPollMinutesSuffix");
            if (FirebasePushUrlLabel != null)
                FirebasePushUrlLabel.Text = UiStrings.T("FirebasePushUrl");
            if (FirebasePushUrlCopyBtn != null)
                FirebasePushUrlCopyBtn.ToolTip = UiStrings.T("FirebasePushUrlCopyTip");
            if (FirebasePushUrlPasteBtn != null)
                FirebasePushUrlPasteBtn.ToolTip = UiStrings.T("FirebasePushUrlPasteTip");
            if (FirebasePushSecretLabel != null)
            {
                FirebasePushSecretLabel.Text = UiStrings.T("FirebasePushSecret");
                FirebasePushSecretLabel.ToolTip = UiStrings.T("FirebasePushSecretTip");
            }
            if (FirebasePushSecretBox != null)
                FirebasePushSecretBox.ToolTip = UiStrings.T("FirebasePushSecretTip");
            if (FirebasePushGenerateBtn != null)
            {
                FirebasePushGenerateBtn.Content = UiStrings.T("FirebasePushGenerate");
                FirebasePushGenerateBtn.ToolTip = UiStrings.T("FirebasePushGenerateTip");
            }
            if (FirebasePushCopyBtn != null)
                FirebasePushCopyBtn.ToolTip = UiStrings.T("FirebasePushCopyTip");
            if (FirebasePushSecretPasteBtn != null)
                FirebasePushSecretPasteBtn.ToolTip = UiStrings.T("FirebasePushSecretPasteTip");
            if (FirebasePushServerNameLabel != null)
                FirebasePushServerNameLabel.Text = UiStrings.T("FirebasePushServerName");
            if (FirebasePushNowBtn != null)
                FirebasePushNowBtn.Content = UiStrings.T("FirebasePushNow");
            if (FirebasePushTestBtn != null)
                FirebasePushTestBtn.Content = UiStrings.T("FirebasePushTest");
            if (FirebasePushPushBtn != null)
            {
                FirebasePushPushBtn.Content = UiStrings.T("FirebasePushPush");
                FirebasePushPushBtn.ToolTip = UiStrings.T("FirebasePushPushTip");
            }
            UpdateFirebasePushStatusUi();
        }

        private void UpdateFirebasePushStatusUi()
        {
            if (FirebasePushStatusText == null) return;
            if (_settings == null || !_settings.FirebasePushEnabled)
            {
                FirebasePushStatusText.Text = UiStrings.T("FirebasePushStatusDisabled");
                return;
            }
            if (!string.IsNullOrEmpty(_firebaseStatusText))
            {
                FirebasePushStatusText.Text = _firebaseStatusText;
                return;
            }
            FirebasePushStatusText.Text = UiStrings.T("FirebasePushStatusIdle");
        }

        private void SetFirebasePushStatus(string text)
        {
            _firebaseStatusText = text ?? "";
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(UpdateFirebasePushStatusUi));
                return;
            }
            UpdateFirebasePushStatusUi();
        }

        private void SetActiveRelayStatus(string text)
        {
            SetFirebasePushStatus(text);
        }

        private async Task RunFirebasePushTestAsync(int replyClientId)
        {
            if (_settings == null)
            {
                if (replyClientId > 0) SendAosAck(replyClientId, false, "No settings");
                return;
            }
            CaptureFirebasePushFromUi();
            if (!IsFirebasePushActive())
            {
                var off = UiStrings.T("FirebasePushStatusDisabled");
                SetFirebasePushStatus(off);
                if (replyClientId > 0) SendAosAck(replyClientId, false, off);
                return;
            }
            string url, secret, serverName;
            if (!TryGetActivePeerRelay(out url, out secret, out serverName))
            {
                var fail = string.Format(UiStrings.T("FirebasePushStatusFail"), "URL/secret");
                SetFirebasePushStatus(fail);
                if (replyClientId > 0) SendAosAck(replyClientId, false, fail);
                return;
            }
            PeerRelayActionResult ping;
            try
            {
                ping = await PeerSyncFirebaseClient.PingAsync(url, secret, CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                var fail = string.Format(UiStrings.T("FirebasePushStatusFail"), ex.Message ?? "ping");
                SetFirebasePushStatus(fail);
                if (replyClientId > 0) SendAosAck(replyClientId, false, fail);
                return;
            }
            if (ping == null || !ping.Ok)
            {
                var fail = string.Format(UiStrings.T("FirebasePushStatusFail"), ping != null ? ping.Error : "ping");
                SetFirebasePushStatus(fail);
                if (replyClientId > 0) SendAosAck(replyClientId, false, fail);
                return;
            }
            var ok = string.Format(UiStrings.T("FirebasePushTestOk"), ping.Seq);
            SetFirebasePushStatus(ok);
            AppendLog(ok, LogLevel.Info);
            if (replyClientId > 0) SendAosAck(replyClientId, true, "firebasePushTest");
        }
    }
}
