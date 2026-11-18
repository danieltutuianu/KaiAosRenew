using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace KaiAosRenew
{
    public partial class MainWindow : Window
    {
        private const int FirebaseSafetyPollMinutes = 60;
        private CancellationTokenSource _firebaseListenCts;
        private int _firebaseListenGeneration;
        private string _firebaseStatusText = "";
        private readonly DispatcherTimer _firebaseSafetyTimer = new DispatcherTimer();

        private void InitFirebasePush()
        {
            _firebaseSafetyTimer.Tick -= FirebaseSafetyTimer_Tick;
            _firebaseSafetyTimer.Tick += FirebaseSafetyTimer_Tick;
            _firebaseSafetyTimer.Interval = TimeSpan.FromMinutes(FirebaseSafetyPollMinutes);
            if (FirebasePushPeersList != null)
                FirebasePushPeersList.ItemsSource = _peerDashboardRows;
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

        private void ApplyFirebasePushFromSettings(bool syncNow)
        {
            if (_settings == null) return;
            _settings.EnsurePeerSyncServerId();
            _settings.NormalizeClamps();
            UpdateFirebasePushStatusUi();
            _firebaseSafetyTimer.Stop();
            StopFirebaseListen();

            if (!IsFirebasePushActive()) return;

            _firebaseSafetyTimer.Interval = TimeSpan.FromMinutes(FirebaseSafetyPollMinutes);
            _firebaseSafetyTimer.Start();
            StartFirebaseListen();
            if (syncNow)
                Forget(BeginPeerSyncPollAsync(false), "FirebaseSafetySync");
        }

        private void FirebaseSafetyTimer_Tick(object sender, EventArgs e)
        {
            if (!IsFirebasePushActive()) return;
            BeginPeerSyncPoll(false);
        }

        private void StopFirebaseListen()
        {
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
                    try
                    {
                        await PeerSyncFirebaseClient.ListenAsync(listenUrl, listenSecret, () =>
                        {
                            if (cts.IsCancellationRequested) return;
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                if (!IsFirebasePushActive()) return;
                                Forget(BeginPeerSyncPollAsync(false), "FirebaseSse");
                            }));
                        }, cts.Token).ConfigureAwait(false);
                        // ListenAsync returns when the SSE stream ends.
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
                    if (cts.IsCancellationRequested || gen != _firebaseListenGeneration) break;
                    try { await Task.Delay(delayMs, cts.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                    if (delayMs < 30000) delayMs = Math.Min(30000, delayMs * 2);
                    // Snapshot after reconnect gap.
                    var ignored = Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (IsFirebasePushActive())
                            Forget(BeginPeerSyncPollAsync(false), "FirebaseReconnect");
                    }));
                    if (ignored == null) { }
                }
            }), "FirebaseListen");
        }

        private void FirebasePush_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingUi || _settings == null) return;
            CaptureFirebasePushFromUi();
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

        private async void FirebasePushNow_Click(object sender, RoutedEventArgs e)
        {
            await BeginPeerSyncPollAsync(true);
        }

        private async void FirebasePushTest_Click(object sender, RoutedEventArgs e)
        {
            await RunFirebasePushTestAsync(0);
        }

        private async void FirebasePushPush_Click(object sender, RoutedEventArgs e)
        {
            await PushPeerCredentialsNowAsync(0).ConfigureAwait(true);
        }

        private async void FirebasePushUpdateAll_Click(object sender, RoutedEventArgs e)
        {
            var ask = UiStrings.T("FirebasePushUpdateAllConfirm");
            var result = MessageBox.Show(this, ask, UiStrings.T("FirebasePushUpdateAll"),
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (result != MessageBoxResult.Yes) return;
            await TriggerPeerUpdateAllAsync(0).ConfigureAwait(true);
        }

        private void CaptureFirebasePushFromUi()
        {
            if (_settings == null) return;
            _settings.FirebasePushEnabled = FirebasePushEnableCheck != null && FirebasePushEnableCheck.IsChecked == true;
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
            if (FirebasePushEnableCheck != null)
                FirebasePushEnableCheck.IsChecked = _settings.FirebasePushEnabled;
            if (FirebasePushUrlBox != null)
                FirebasePushUrlBox.Text = _settings.FirebasePushUrl ?? "";
            if (FirebasePushSecretBox != null)
            {
                try { FirebasePushSecretBox.Text = _settings.GetFirebasePushSecret() ?? ""; }
                catch { FirebasePushSecretBox.Text = ""; }
            }
            if (FirebasePushServerNameBox != null)
                FirebasePushServerNameBox.Text = _settings.FirebasePushServerName ?? "";
            UpdateFirebasePushStatusUi();
            UpdateFirebaseDashboardEmptyHint();
        }

        private void ApplyFirebasePushLanguage()
        {
            if (SettingsFirebasePushGroup != null)
                SettingsFirebasePushGroup.Header = UiStrings.T("SettingsFirebasePushGroup");
            if (FirebasePushEnableCheck != null)
                FirebasePushEnableCheck.Content = UiStrings.T("FirebasePushEnable");
            if (FirebasePushUrlLabel != null)
                FirebasePushUrlLabel.Text = UiStrings.T("FirebasePushUrl");
            if (FirebasePushUrlCopyBtn != null)
                FirebasePushUrlCopyBtn.ToolTip = UiStrings.T("FirebasePushUrlCopyTip");
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
            if (FirebasePushUpdateAllBtn != null)
            {
                FirebasePushUpdateAllBtn.Content = UiStrings.T("FirebasePushUpdateAll");
                FirebasePushUpdateAllBtn.ToolTip = UiStrings.T("FirebasePushUpdateAllTip");
            }
            if (FirebasePushPeersLabel != null)
                FirebasePushPeersLabel.Text = UiStrings.T("FirebasePushPeers");
            if (FirebasePushColServer != null) FirebasePushColServer.Header = UiStrings.T("PeerSyncColServer");
            if (FirebasePushColHealth != null) FirebasePushColHealth.Header = UiStrings.T("PeerSyncColHealth");
            if (FirebasePushColToken != null) FirebasePushColToken.Header = UiStrings.T("PeerSyncColToken");
            if (FirebasePushColVersion != null) FirebasePushColVersion.Header = UiStrings.T("PeerSyncColVersion");
            if (FirebasePushColClients != null) FirebasePushColClients.Header = UiStrings.T("PeerSyncColClients");
            if (FirebasePushColSeen != null) FirebasePushColSeen.Header = UiStrings.T("PeerSyncColSeen");
            UpdateFirebasePushStatusUi();
            UpdateFirebaseDashboardEmptyHint();
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

        private void UpdateFirebaseDashboardEmptyHint()
        {
            if (FirebasePushPeersEmptyHint == null) return;
            FirebasePushPeersEmptyHint.Text = UiStrings.T("FirebasePushPeersEmpty");
            FirebasePushPeersEmptyHint.Visibility = _peerDashboardRows.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private async Task RunFirebasePushTestAsync(int replyClientId)
        {
            if (_settings == null)
            {
                if (replyClientId > 0) SendAosAck(replyClientId, false, "No settings");
                return;
            }
            CaptureFirebasePushFromUi();
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
