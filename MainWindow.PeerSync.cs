using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace KaiAosRenew
{
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer _peerSyncTimer = new DispatcherTimer();
        private readonly DispatcherTimer _peerHeartbeatDebounce = new DispatcherTimer();
        private int _peerSyncBusy;
        private int _peerSyncPending;
        private string _peerSyncStatusText = "";
        private long _peerSyncLastPeersSeq;
        private bool _peerLastSyncOk = true;
        private string _peerLastSyncUtc = "";
        private string _peerLastSyncMsg = "";
        private readonly ObservableCollection<PeerDashboardWire> _peerDashboardRows =
            new ObservableCollection<PeerDashboardWire>();
        private string _peerStatusSyncSignature = "";

        private void InitPeerSync()
        {
            _peerSyncTimer.Tick -= PeerSyncTimer_Tick;
            _peerSyncTimer.Tick += PeerSyncTimer_Tick;
            _peerHeartbeatDebounce.Interval = TimeSpan.FromSeconds(8);
            _peerHeartbeatDebounce.Tick -= PeerHeartbeatDebounce_Tick;
            _peerHeartbeatDebounce.Tick += PeerHeartbeatDebounce_Tick;
            if (PeerSyncPeersList != null)
                PeerSyncPeersList.ItemsSource = _peerDashboardRows;
            ApplyPeerSyncFromSettings(true);
        }

        private void ApplyPeerSyncFromSettings(bool pollNow)
        {
            if (_settings == null) return;
            _settings.EnsurePeerSyncServerId();
            _settings.NormalizeClamps();
            UpdatePeerSyncStatusUi();
            _peerSyncTimer.Stop();
            if (!_settings.PeerSyncEnabled) return;
            var minutes = PeerSyncLogic.ClampPollMinutes(_settings.PeerSyncPollMinutes);
            _peerSyncTimer.Interval = TimeSpan.FromMinutes(minutes);
            _peerSyncTimer.Start();
            if (pollNow)
                BeginPeerSyncPoll(false);
        }

        private void PeerSyncTimer_Tick(object sender, EventArgs e)
        {
            BeginPeerSyncPoll(false);
        }

        private void PeerSync_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingUi || _settings == null) return;
            CapturePeerSyncFromUi();
            try { _settings.Save(); }
            catch { }
            BroadcastServerSettings();
            ApplyPeerSyncFromSettings(true);
        }

        private void PeerSyncField_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_loadingUi || _settings == null) return;
            CapturePeerSyncFromUi();
            try { _settings.Save(); }
            catch { }
            BroadcastServerSettings();
            ApplyPeerSyncFromSettings(false);
        }

        private void PeerSyncGenerate_Click(object sender, RoutedEventArgs e)
        {
            if (_settings == null) return;
            var secret = NotifyPairing.GenerateCode() + NotifyPairing.GenerateCode();
            _settings.SetPeerSyncSecret(secret);
            if (PeerSyncSecretBox != null)
                PeerSyncSecretBox.Text = secret;
            try { _settings.Save(); }
            catch { }
            BroadcastServerSettings();
            ApplyPeerSyncFromSettings(false);
        }

        private void PeerSyncCopy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var secret = PeerSyncSecretBox != null
                    ? (PeerSyncSecretBox.Text ?? "").Trim()
                    : (_settings != null ? (_settings.GetPeerSyncSecret() ?? "") : "");
                if (string.IsNullOrEmpty(secret)) return;
                NativeMethods.TrySetClipboardText(secret);
            }
            catch { }
        }

        private async void PeerSyncNow_Click(object sender, RoutedEventArgs e)
        {
            await BeginPeerSyncPollAsync(true);
        }

        private async void PeerSyncTest_Click(object sender, RoutedEventArgs e)
        {
            await RunPeerSyncTestAsync(0);
        }

        private async void PeerSyncPush_Click(object sender, RoutedEventArgs e)
        {
            await PushPeerCredentialsNowAsync(0).ConfigureAwait(true);
        }

        private async void PeerSyncUpdateAll_Click(object sender, RoutedEventArgs e)
        {
            var ask = UiStrings.T("PeerSyncUpdateAllConfirm");
            var result = MessageBox.Show(this, ask, UiStrings.T("PeerSyncUpdateAll"),
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (result != MessageBoxResult.Yes) return;
            await TriggerPeerUpdateAllAsync(0).ConfigureAwait(true);
        }

        private void CapturePeerSyncFromUi()
        {
            if (_settings == null) return;
            _settings.PeerSyncEnabled = PeerSyncEnableCheck != null && PeerSyncEnableCheck.IsChecked == true;
            if (PeerSyncUrlBox != null)
                _settings.PeerSyncUrl = (PeerSyncUrlBox.Text ?? "").Trim();
            if (PeerSyncSecretBox != null)
                _settings.SetPeerSyncSecret((PeerSyncSecretBox.Text ?? "").Trim());
            if (PeerSyncServerNameBox != null)
            {
                var name = (PeerSyncServerNameBox.Text ?? "").Trim();
                if (!string.IsNullOrEmpty(name))
                    _settings.PeerSyncServerName = name;
            }
            int minutes = 60;
            if (PeerSyncPollBox != null)
            {
                int parsed;
                if (!int.TryParse((PeerSyncPollBox.Text ?? "").Trim(), out parsed) || parsed < 1)
                    parsed = 60;
                minutes = PeerSyncLogic.ClampPollMinutes(parsed);
                PeerSyncPollBox.Text = minutes.ToString();
            }
            _settings.PeerSyncPollMinutes = minutes;
            _settings.EnsurePeerSyncServerId();
            _settings.NormalizeClamps();
        }

        private void LoadPeerSyncToUi()
        {
            if (_settings == null) return;
            _settings.EnsurePeerSyncServerId();
            if (PeerSyncEnableCheck != null)
                PeerSyncEnableCheck.IsChecked = _settings.PeerSyncEnabled;
            if (PeerSyncUrlBox != null)
                PeerSyncUrlBox.Text = _settings.PeerSyncUrl ?? "";
            if (PeerSyncSecretBox != null)
            {
                try { PeerSyncSecretBox.Text = _settings.GetPeerSyncSecret() ?? ""; }
                catch { PeerSyncSecretBox.Text = ""; }
            }
            if (PeerSyncServerNameBox != null)
                PeerSyncServerNameBox.Text = _settings.PeerSyncServerName ?? "";
            if (PeerSyncPollBox != null)
                PeerSyncPollBox.Text = PeerSyncLogic.ClampPollMinutes(_settings.PeerSyncPollMinutes).ToString();
            UpdatePeerSyncStatusUi();
        }

        private void ApplyPeerSyncLanguage()
        {
            if (SettingsPeerSyncGroup != null)
                SettingsPeerSyncGroup.Header = UiStrings.T("SettingsPeerSyncGroup");
            if (PeerSyncEnableCheck != null)
                PeerSyncEnableCheck.Content = UiStrings.T("PeerSyncEnable");
            if (PeerSyncUrlLabel != null)
                PeerSyncUrlLabel.Text = UiStrings.T("PeerSyncUrl");
            if (PeerSyncSecretLabel != null)
            {
                PeerSyncSecretLabel.Text = UiStrings.T("PeerSyncSecret");
                PeerSyncSecretLabel.ToolTip = UiStrings.T("PeerSyncSecretTip");
            }
            if (PeerSyncSecretBox != null)
                PeerSyncSecretBox.ToolTip = UiStrings.T("PeerSyncSecretTip");
            if (PeerSyncGenerateBtn != null)
            {
                PeerSyncGenerateBtn.Content = UiStrings.T("PeerSyncGenerate");
                PeerSyncGenerateBtn.ToolTip = UiStrings.T("PeerSyncGenerateTip");
            }
            if (PeerSyncCopyBtn != null)
                PeerSyncCopyBtn.ToolTip = UiStrings.T("PeerSyncCopyTip");
            if (PeerSyncServerNameLabel != null)
                PeerSyncServerNameLabel.Text = UiStrings.T("PeerSyncServerName");
            if (PeerSyncPollLabel != null)
            {
                PeerSyncPollLabel.Text = UiStrings.T("PeerSyncPollMinutes");
                PeerSyncPollLabel.ToolTip = UiStrings.T("PeerSyncPollTip");
            }
            if (PeerSyncPollBox != null)
                PeerSyncPollBox.ToolTip = UiStrings.T("PeerSyncPollTip");
            if (PeerSyncNowBtn != null)
                PeerSyncNowBtn.Content = UiStrings.T("PeerSyncNow");
            if (PeerSyncTestBtn != null)
                PeerSyncTestBtn.Content = UiStrings.T("PeerSyncTest");
            if (PeerSyncPushBtn != null)
            {
                PeerSyncPushBtn.Content = UiStrings.T("PeerSyncPush");
                PeerSyncPushBtn.ToolTip = UiStrings.T("PeerSyncPushTip");
            }
            if (PeerSyncUpdateAllBtn != null)
            {
                PeerSyncUpdateAllBtn.Content = UiStrings.T("PeerSyncUpdateAll");
                PeerSyncUpdateAllBtn.ToolTip = UiStrings.T("PeerSyncUpdateAllTip");
            }
            if (PeerSyncPeersLabel != null)
                PeerSyncPeersLabel.Text = UiStrings.T("PeerSyncPeers");
            if (PeerSyncColServer != null) PeerSyncColServer.Header = UiStrings.T("PeerSyncColServer");
            if (PeerSyncColHealth != null) PeerSyncColHealth.Header = UiStrings.T("PeerSyncColHealth");
            if (PeerSyncColToken != null) PeerSyncColToken.Header = UiStrings.T("PeerSyncColToken");
            if (PeerSyncColVersion != null) PeerSyncColVersion.Header = UiStrings.T("PeerSyncColVersion");
            if (PeerSyncColClients != null) PeerSyncColClients.Header = UiStrings.T("PeerSyncColClients");
            if (PeerSyncColSeen != null) PeerSyncColSeen.Header = UiStrings.T("PeerSyncColSeen");
            UpdatePeerSyncStatusUi();
            UpdatePeerDashboardEmptyHint();
        }

        private void UpdatePeerSyncStatusUi()
        {
            if (PeerSyncStatusText == null) return;
            if (_settings == null || !_settings.PeerSyncEnabled)
            {
                PeerSyncStatusText.Text = UiStrings.T("PeerSyncStatusDisabled");
                return;
            }
            if (!string.IsNullOrEmpty(_peerSyncStatusText))
            {
                PeerSyncStatusText.Text = _peerSyncStatusText;
                return;
            }
            PeerSyncStatusText.Text = UiStrings.T("PeerSyncStatusIdle");
        }

        private void SetPeerSyncStatus(string text)
        {
            _peerSyncStatusText = text ?? "";
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(UpdatePeerSyncStatusUi));
                return;
            }
            UpdatePeerSyncStatusUi();
        }

        private void BeginPeerSyncPoll(bool manual)
        {
            Forget(BeginPeerSyncPollAsync(manual), "BeginPeerSyncPoll");
        }

        /// <summary>Observe a fire-and-forget task: log faults to the activity log instead of dropping them (CS4014).</summary>
        private void Forget(Task task, string context)
        {
            if (task == null) return;
            task.ContinueWith(t =>
            {
                try
                {
                    var ex = t.Exception;
                    if (ex != null)
                        Dispatcher.BeginInvoke(new Action(() =>
                            AppendLog("Async: " + (context ?? "?") + " — " + ex.GetBaseException().Message, LogLevel.Warning)));
                }
                catch { }
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        private async Task BeginPeerSyncPollAsync(bool manual)
        {
            if (_settings == null || !_settings.PeerSyncEnabled) return;
            if (Interlocked.CompareExchange(ref _peerSyncBusy, 1, 0) != 0)
            {
                Interlocked.Exchange(ref _peerSyncPending, 1);
                return;
            }
            try
            {
                do
                {
                    Interlocked.Exchange(ref _peerSyncPending, 0);
                    await PollPeerSyncOnceAsync(manual).ConfigureAwait(true);
                } while (Interlocked.Exchange(ref _peerSyncPending, 0) == 1);
            }
            finally
            {
                Interlocked.Exchange(ref _peerSyncBusy, 0);
            }
        }

        private async Task PollPeerSyncOnceAsync(bool manual)
        {
            if (_settings == null || !_settings.PeerSyncEnabled) return;
            string url = _settings.PeerSyncUrl ?? "";
            string secret;
            try { secret = _settings.GetPeerSyncSecret() ?? ""; }
            catch { secret = ""; }
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(secret))
            {
                if (manual)
                    SetPeerSyncStatus(string.Format(UiStrings.T("PeerSyncStatusFail"), "URL/secret"));
                return;
            }
            var serverId = _settings.PeerSyncServerId ?? "";
            var sinceSeq = _settings.PeerSyncLastSeq;
            var sinceAck = _settings.PeerSyncLastAckSeq;
            var sinceCmd = _settings.PeerSyncLastCmdSeq;
            var sincePeers = _peerSyncLastPeersSeq;
            // Heartbeat first so our presence is in the peers list we receive.
            await SendPeerHeartbeatAsync(url, secret).ConfigureAwait(true);
            PeerSyncClient.PollResult poll;
            try
            {
                poll = await PeerSyncClient.PollAsync(
                    url, secret, serverId, sinceSeq, sinceAck, sinceCmd, sincePeers, CancellationToken.None)
                    .ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                SetPeerSyncStatus(string.Format(UiStrings.T("PeerSyncStatusFail"), ex.Message ?? "poll"));
                return;
            }
            if (poll == null || !poll.Ok)
            {
                SetPeerSyncStatus(string.Format(UiStrings.T("PeerSyncStatusFail"), poll != null ? poll.Error : "poll"));
                return;
            }
            SetPeerSyncStatus(UiStrings.T("PeerSyncStatusOk"));

            if (poll.Peers != null && poll.Peers.Count > 0)
                ApplyPeerDashboardFromWire(poll.Peers, secret);
            if (poll.PeersSeq > _peerSyncLastPeersSeq)
                _peerSyncLastPeersSeq = poll.PeersSeq;

            if (poll.Acks != null)
            {
                foreach (var ack in poll.Acks)
                {
                    if (!PeerSyncLogic.ShouldProcessInboundAck(ack, serverId, _settings.PeerSyncLastAckSeq))
                        continue;
                    await HandlePeerAckAsync(ack, secret).ConfigureAwait(true);
                    if (ack.AckSeq > _settings.PeerSyncLastAckSeq)
                    {
                        _settings.PeerSyncLastAckSeq = ack.AckSeq;
                        try { _settings.Save(); }
                        catch { }
                    }
                }
            }

            if (poll.LatestCommand != null
                && PeerSyncLogic.ShouldProcessInboundCommand(
                    poll.LatestCommand, serverId, _settings.PeerSyncLastCmdSeq, _settings.PeerSyncLastCmdId))
            {
                await HandleInboundPeerCommandAsync(poll.LatestCommand, secret).ConfigureAwait(true);
            }
            else if (poll.CmdSeq > _settings.PeerSyncLastCmdSeq
                     && poll.LatestCommand != null
                     && PeerSyncLogic.ShouldIgnoreOwnEvent(poll.LatestCommand.OriginServerId, serverId))
            {
                _settings.PeerSyncLastCmdSeq = poll.CmdSeq;
                _settings.PeerSyncLastCmdId = poll.LatestCommand.CmdId ?? _settings.PeerSyncLastCmdId;
                try { _settings.Save(); }
                catch { }
            }

            if (poll.LatestCreds != null
                && PeerSyncLogic.ShouldProcessInboundCreds(
                    poll.LatestCreds, serverId, _settings.PeerSyncLastSeq, _settings.PeerSyncLastEventId))
            {
                await HandleInboundPeerCredsAsync(poll.LatestCreds, secret).ConfigureAwait(true);
            }
            else if (poll.Seq > _settings.PeerSyncLastSeq
                     && poll.LatestCreds != null
                     && PeerSyncLogic.ShouldIgnoreOwnEvent(poll.LatestCreds.OriginServerId, serverId))
            {
                // Advance cursor past our own published event.
                _settings.PeerSyncLastSeq = poll.Seq;
                _settings.PeerSyncLastEventId = poll.LatestCreds.EventId ?? _settings.PeerSyncLastEventId;
                try { _settings.Save(); }
                catch { }
            }
        }

        private async Task RunPeerSyncTestAsync(int replyClientId)
        {
            if (_settings == null)
            {
                if (replyClientId > 0) SendAosAck(replyClientId, false, "No settings");
                return;
            }
            CapturePeerSyncFromUi();
            string url = _settings.PeerSyncUrl ?? "";
            string secret;
            try { secret = _settings.GetPeerSyncSecret() ?? ""; }
            catch { secret = ""; }
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(secret))
            {
                var fail = string.Format(UiStrings.T("PeerSyncStatusFail"), "URL/secret");
                SetPeerSyncStatus(fail);
                if (replyClientId > 0) SendAosAck(replyClientId, false, fail);
                return;
            }
            PeerSyncClient.ActionResult ping;
            try
            {
                ping = await PeerSyncClient.PingAsync(url, secret, CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                var fail = string.Format(UiStrings.T("PeerSyncStatusFail"), ex.Message ?? "ping");
                SetPeerSyncStatus(fail);
                if (replyClientId > 0) SendAosAck(replyClientId, false, fail);
                return;
            }
            if (ping == null || !ping.Ok)
            {
                var fail = string.Format(UiStrings.T("PeerSyncStatusFail"), ping != null ? ping.Error : "ping");
                SetPeerSyncStatus(fail);
                if (replyClientId > 0) SendAosAck(replyClientId, false, fail);
                return;
            }
            var ok = string.Format(UiStrings.T("PeerSyncTestOk"), ping.Seq);
            SetPeerSyncStatus(ok);
            AppendLog(ok, LogLevel.Info);
            if (replyClientId > 0) SendAosAck(replyClientId, true, "peerSyncTest");
        }

        private async Task PublishPeerCredentialsAsync(string email, string password, int intervalHours, int blockAfter)
        {
            if (_settings == null || !_settings.PeerSyncEnabled) return;
            string url = _settings.PeerSyncUrl ?? "";
            string secret;
            try { secret = _settings.GetPeerSyncSecret() ?? ""; }
            catch { secret = ""; }
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(secret)) return;

            var payload = new PeerCredsPayload
            {
                Email = email ?? "",
                Password = password ?? "",
                IntervalHours = intervalHours < 1 ? 1 : intervalHours,
                BlockAfter = blockAfter < 1 ? 3 : blockAfter
            };
            string enc;
            try { enc = PeerSyncCrypto.EncryptCreds(secret, payload); }
            catch (Exception ex)
            {
                AppendLog(string.Format(UiStrings.T("PeerSyncPublishFail"), ex.Message ?? "encrypt"), LogLevel.Warning);
                return;
            }
            var eventId = Guid.NewGuid().ToString("N");
            var statusEnc = BuildLocalPeerStatusEnc(secret);
            PeerSyncClient.ActionResult published;
            try
            {
                published = await PeerSyncClient.PublishAsync(
                    url, secret, eventId,
                    _settings.PeerSyncServerId,
                    _settings.PeerSyncServerName,
                    enc,
                    statusEnc,
                    CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                AppendLog(string.Format(UiStrings.T("PeerSyncPublishFail"), ex.Message ?? "publish"), LogLevel.Warning);
                NotePeerSyncOutcome(false, ex.Message ?? "publish");
                return;
            }
            if (published == null || !published.Ok)
            {
                AppendLog(string.Format(UiStrings.T("PeerSyncPublishFail"), published != null ? published.Error : "publish"), LogLevel.Warning);
                NotePeerSyncOutcome(false, published != null ? published.Error : "publish");
                return;
            }
            _settings.PeerSyncLastSeq = Math.Max(_settings.PeerSyncLastSeq, published.Seq);
            _settings.PeerSyncLastEventId = eventId;
            try { _settings.Save(); }
            catch { }
            AppendLog(string.Format(UiStrings.T("PeerSyncPublished"), eventId), LogLevel.Info);
            SetPeerSyncStatus(UiStrings.T("PeerSyncStatusOk"));
            NotePeerSyncOutcome(true, "published " + eventId);
        }

        private async Task HandleInboundPeerCredsAsync(PeerCredsEvent ev, string secret)
        {
            if (_settings == null || ev == null) return;
            PeerCredsPayload payload;
            if (!PeerSyncCrypto.TryDecryptCreds(secret, ev.PayloadEnc, out payload) || payload == null)
            {
                AppendLog(string.Format(UiStrings.T("PeerSyncRejected"),
                    string.IsNullOrEmpty(ev.OriginName) ? ev.OriginServerId : ev.OriginName,
                    "decrypt"), LogLevel.Warning);
                return;
            }

            // Wait while a local renew is running.
            var waitUntil = DateTime.UtcNow.AddMinutes(2);
            while (_busy && DateTime.UtcNow < waitUntil)
                await Task.Delay(250).ConfigureAwait(true);
            if (_busy)
            {
                AppendLog(string.Format(UiStrings.T("PeerSyncRejected"),
                    string.IsNullOrEmpty(ev.OriginName) ? ev.OriginServerId : ev.OriginName,
                    "busy"), LogLevel.Warning);
                return;
            }

            string prevEmail = _settings.Email ?? "";
            string prevPass;
            try { prevPass = _settings.GetPassword() ?? ""; }
            catch { prevPass = ""; }
            var prevHours = _settings.IntervalHours < 1 ? 1 : _settings.IntervalHours;
            var prevBlock = GetAuthRegisterBlockAfter();

            if (!PeerSyncLogic.CredentialsDiffer(prevEmail, prevPass, payload.Email, payload.Password))
            {
                _settings.PeerSyncLastSeq = Math.Max(_settings.PeerSyncLastSeq, ev.Seq);
                _settings.PeerSyncLastEventId = ev.EventId ?? _settings.PeerSyncLastEventId;
                try { _settings.Save(); }
                catch { }
                await SendPeerAckAsync(ev, true, false, false, "already current").ConfigureAwait(true);
                return;
            }

            var candidate = CloneSettingsForPeerRegister(payload.Email, payload.Password, payload.IntervalHours, payload.BlockAfter);
            RenewResult result = null;
            try
            {
                result = await Task.Run(() => RenewEngine.Run(candidate, true, payload.Password, true)).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                result = new RenewResult { Ok = false, Message = ex.Message, RegisterAttempted = true };
            }

            var originLabel = string.IsNullOrEmpty(ev.OriginName) ? (ev.OriginServerId ?? "?") : ev.OriginName;
            var commit = PeerSyncLogic.ShouldCommitCredentials(
                result != null && result.Ok,
                result != null && result.RegisterAttempted,
                result != null && result.RegisterAuthRejected,
                result != null && result.SkippedRegister);

            if (commit)
            {
                ApplyPeerCredentialsCommit(payload, result);
                _settings.PeerSyncLastSeq = Math.Max(_settings.PeerSyncLastSeq, ev.Seq);
                _settings.PeerSyncLastEventId = ev.EventId ?? _settings.PeerSyncLastEventId;
                try { _settings.Save(); }
                catch { }
                AppendLog(string.Format(UiStrings.T("PeerSyncApplied"), originLabel), LogLevel.Success);
                BroadcastAosSync();
                BroadcastServerSettings();
                await SendPeerAckAsync(ev, true, false, false, result.Message ?? "OK").ConfigureAwait(true);
                return;
            }

            var rejectMsg = result != null ? (result.Message ?? "Register failed") : "Register failed";
            AppendLog(string.Format(UiStrings.T("PeerSyncRejected"), originLabel, rejectMsg), LogLevel.Warning);

            var rollbackAttempted = !string.IsNullOrEmpty(prevEmail) && !string.IsNullOrEmpty(prevPass);
            var rollbackOk = false;
            if (rollbackAttempted)
            {
                var prevCandidate = CloneSettingsForPeerRegister(prevEmail, prevPass, prevHours, prevBlock);
                RenewResult rollback = null;
                try
                {
                    rollback = await Task.Run(() => RenewEngine.Run(prevCandidate, true, prevPass, true)).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    rollback = new RenewResult { Ok = false, Message = ex.Message };
                }
                rollbackOk = PeerSyncLogic.ShouldCommitCredentials(
                    rollback != null && rollback.Ok,
                    rollback != null && rollback.RegisterAttempted,
                    rollback != null && rollback.RegisterAuthRejected,
                    rollback != null && rollback.SkippedRegister)
                    || (rollback != null && rollback.Ok); // soft OK with existing token is fine for rollback
                if (rollbackOk)
                {
                    // Keep previous settings values; refresh token metadata if present.
                    if (rollback != null && prevCandidate.LastTokenExpiryUtc.HasValue)
                        _settings.LastTokenExpiryUtc = prevCandidate.LastTokenExpiryUtc;
                    if (rollback != null && prevCandidate.LastTokenExpiresInSeconds > 0)
                        _settings.LastTokenExpiresInSeconds = prevCandidate.LastTokenExpiresInSeconds;
                    try { _settings.Save(); }
                    catch { }
                    AppendLog(UiStrings.T("PeerSyncRollbackOk"), LogLevel.Info);
                }
                else
                {
                    AppendLog(string.Format(UiStrings.T("PeerSyncRollbackFail"),
                        rollback != null ? rollback.Message : "fail"), LogLevel.Error);
                }
            }

            // Advance cursor so we do not retry the same bad event forever.
            _settings.PeerSyncLastSeq = Math.Max(_settings.PeerSyncLastSeq, ev.Seq);
            _settings.PeerSyncLastEventId = ev.EventId ?? _settings.PeerSyncLastEventId;
            try { _settings.Save(); }
            catch { }

            await SendPeerAckAsync(ev, false, rollbackAttempted, rollbackOk, rejectMsg).ConfigureAwait(true);

            try
            {
                BroadcastNotify(new NotifyMessage
                {
                    Kind = "error",
                    Level = "error",
                    Title = "KAI - AOS Renew",
                    Text = string.Format(UiStrings.T("PeerSyncRejected"), originLabel, rejectMsg),
                    Seconds = 10
                });
            }
            catch { }
            ShowBalloon(WinForms.ToolTipIcon.Warning,
                TruncateBalloon(string.Format(UiStrings.T("PeerSyncRejected"), originLabel, rejectMsg)));
        }

        private AppSettings CloneSettingsForPeerRegister(string email, string password, int hours, int blockAfter)
        {
            var s = new AppSettings();
            s.Email = (email ?? "").Trim();
            s.SetPassword(password ?? "");
            s.IntervalHours = hours < 1 ? 1 : (hours > 168 ? 168 : hours);
            s.AuthRegisterBlockAfter = blockAfter < 1 ? 3 : (blockAfter > 10 ? 10 : blockAfter);
            s.ConfigPath = _settings.ConfigPath;
            s.RegisterUrl = _settings.RegisterUrl;
            s.WebEamUrl = _settings.WebEamUrl;
            s.UseSystemProxy = _settings.UseSystemProxy;
            s.NormalizeClamps();
            return s;
        }

        private void ApplyPeerCredentialsCommit(PeerCredsPayload payload, RenewResult result)
        {
            if (_settings == null || payload == null) return;
            _settings.Email = (payload.Email ?? "").Trim();
            _settings.SetPassword(payload.Password ?? "");
            if (payload.IntervalHours >= 1 && payload.IntervalHours <= 168)
                _settings.IntervalHours = payload.IntervalHours;
            if (payload.BlockAfter >= 1 && payload.BlockAfter <= 10)
                _settings.AuthRegisterBlockAfter = payload.BlockAfter;
            _settings.AuthCooldownUntilUtc = null;
            _settings.AuthRegisterBlocked = false;
            _settings.AuthRegisterStrikeCount = 0;
            _settings.ConsecutiveFailures = 0;
            _settings.LastPasswordChangeUtc = DateTime.UtcNow;
            if (result != null)
            {
                if (!string.IsNullOrEmpty(result.Message))
                    _settings.LastMessage = result.Message;
                _settings.LastSuccessUtc = DateTime.UtcNow;
                _settings.LastRegisterOkUtc = DateTime.UtcNow;
                _settings.LastRegisterFailUtc = null;
            }
            try { _settings.Save(); }
            catch { }

            _loadingUi = true;
            try
            {
                if (EmailBox != null) EmailBox.Text = _settings.Email ?? "";
                SetPasswordUi(payload.Password ?? "");
                if (IntervalBox != null) IntervalBox.Text = _settings.IntervalHours.ToString();
                if (AuthRegisterBlockAfterBox != null)
                    AuthRegisterBlockAfterBox.Text = _settings.AuthRegisterBlockAfter.ToString();
            }
            finally
            {
                _loadingUi = false;
            }
            CaptureAosSavedCredentials();
            _aosCredentialsDirty = false;
            UpdateAosDirtyStatus();
            RefreshAuthCooldownBanner();
            UpdateTokenAgeText();
            SetTokenFooter(true);
            RefreshStatusBadge();
        }

        private async Task SendPeerAckAsync(PeerCredsEvent ev, bool ok, bool rollbackAttempted, bool rollbackOk, string message)
        {
            if (_settings == null || ev == null) return;
            string url = _settings.PeerSyncUrl ?? "";
            string secret;
            try { secret = _settings.GetPeerSyncSecret() ?? ""; }
            catch { secret = ""; }
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(secret)) return;
            var ackPayload = new PeerAckPayload
            {
                Ok = ok,
                RollbackAttempted = rollbackAttempted,
                RollbackOk = rollbackOk,
                Message = message ?? ""
            };
            string enc;
            try { enc = PeerSyncCrypto.EncryptAck(secret, ackPayload); }
            catch { return; }
            var ackId = Guid.NewGuid().ToString("N");
            var statusEnc = BuildLocalPeerStatusEnc(secret);
            try
            {
                await PeerSyncClient.AckAsync(
                    url, secret, ackId, ev.EventId,
                    ev.OriginServerId,
                    _settings.PeerSyncServerId,
                    _settings.PeerSyncServerName,
                    enc,
                    statusEnc,
                    CancellationToken.None).ConfigureAwait(true);
            }
            catch { }
        }

        private async Task HandlePeerAckAsync(PeerAckEvent ack, string secret)
        {
            if (ack == null) return;
            PeerAckPayload payload;
            if (!PeerSyncCrypto.TryDecryptAck(secret, ack.PayloadEnc, out payload) || payload == null)
                return;
            var name = string.IsNullOrEmpty(ack.ResponderName) ? (ack.ResponderServerId ?? "?") : ack.ResponderName;
            if (payload.Ok)
            {
                AppendLog(string.Format(UiStrings.T("PeerSyncAckOk"), name), LogLevel.Success);
            }
            else
            {
                var msg = string.Format(UiStrings.T("PeerSyncAckFail"), name, payload.Message ?? "");
                AppendLog(msg, LogLevel.Warning);
                try
                {
                    BroadcastNotify(new NotifyMessage
                    {
                        Kind = "warning",
                        Level = "warning",
                        Title = "KAI - AOS Renew",
                        Text = msg,
                        Seconds = 10
                    });
                }
                catch { }
                ShowBalloon(WinForms.ToolTipIcon.Warning, TruncateBalloon(msg));
            }
            await Task.Yield();
        }

        /// <summary>
        /// Called after a successful local Force Register that changed credentials.
        /// </summary>
        private void MaybePublishPeerCredentialsAfterLocalRegister(RenewResult result, string email, string password)
        {
            if (_settings == null || !_settings.PeerSyncEnabled) return;
            if (result == null) return;
            if (!PeerSyncLogic.ShouldPublishAfterLocalRegister(
                result.Ok, result.RegisterAttempted, result.RegisterAuthRejected, result.SkippedRegister))
                return;
            email = (email ?? _settings.Email ?? "").Trim();
            if (string.IsNullOrEmpty(password))
            {
                try { password = _settings.GetPassword() ?? ""; }
                catch { password = ""; }
            }
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password)) return;
            var hours = _settings.IntervalHours < 1 ? 1 : _settings.IntervalHours;
            var block = GetAuthRegisterBlockAfter();
            Forget(PublishPeerCredentialsAsync(email, password, hours, block), "PublishPeerCredentials");
        }

        private async Task PushPeerCredentialsNowAsync(int replyClientId)
        {
            if (_settings == null || !_settings.PeerSyncEnabled)
            {
                var off = UiStrings.T("PeerSyncStatusDisabled");
                SetPeerSyncStatus(off);
                if (replyClientId > 0) SendAosAck(replyClientId, false, off);
                return;
            }
            CapturePeerSyncFromUi();
            var email = (_settings.Email ?? "").Trim();
            string password;
            try { password = _settings.GetPassword() ?? ""; }
            catch { password = ""; }
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                var fail = string.Format(UiStrings.T("PeerSyncPublishFail"), "empty credentials");
                SetPeerSyncStatus(fail);
                AppendLog(fail, LogLevel.Warning);
                if (replyClientId > 0) SendAosAck(replyClientId, false, fail);
                return;
            }
            var hours = _settings.IntervalHours < 1 ? 1 : _settings.IntervalHours;
            var block = GetAuthRegisterBlockAfter();
            await PublishPeerCredentialsAsync(email, password, hours, block).ConfigureAwait(true);
            AppendLog(UiStrings.T("PeerSyncPushDone"), LogLevel.Info);
            if (replyClientId > 0) SendAosAck(replyClientId, true, "peerSyncPush");
        }

        private async Task TriggerPeerUpdateAllAsync(int replyClientId)
        {
            if (_settings == null || !_settings.PeerSyncEnabled)
            {
                var off = UiStrings.T("PeerSyncStatusDisabled");
                if (replyClientId > 0) SendAosAck(replyClientId, false, off);
                return;
            }
            CapturePeerSyncFromUi();
            string url = _settings.PeerSyncUrl ?? "";
            string secret;
            try { secret = _settings.GetPeerSyncSecret() ?? ""; }
            catch { secret = ""; }
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(secret))
            {
                var fail = string.Format(UiStrings.T("PeerSyncStatusFail"), "URL/secret");
                if (replyClientId > 0) SendAosAck(replyClientId, false, fail);
                return;
            }
            try { await RefreshUpdateFeedAsync().ConfigureAwait(true); }
            catch { }
            var targetVer = (_feedServerVer ?? "").Trim();
            if (string.IsNullOrEmpty(targetVer))
                targetVer = AppVersionDisplay ?? "";
            var cmdPayload = new PeerCommandPayload
            {
                Type = PeerSyncLogic.CommandTypeUpdateAll,
                TargetVersion = targetVer,
                IncludeLocal = true
            };
            string enc;
            try { enc = PeerSyncCrypto.EncryptCommand(secret, cmdPayload); }
            catch (Exception ex)
            {
                var fail = string.Format(UiStrings.T("PeerSyncCmdFail"), ex.Message ?? "encrypt");
                AppendLog(fail, LogLevel.Warning);
                if (replyClientId > 0) SendAosAck(replyClientId, false, fail);
                return;
            }
            var cmdId = Guid.NewGuid().ToString("N");
            var statusEnc = BuildLocalPeerStatusEnc(secret);
            PeerSyncClient.ActionResult published;
            try
            {
                published = await PeerSyncClient.PublishCommandAsync(
                    url, secret, cmdId, PeerSyncLogic.CommandTypeUpdateAll,
                    _settings.PeerSyncServerId, _settings.PeerSyncServerName,
                    enc, statusEnc, CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                var fail = string.Format(UiStrings.T("PeerSyncCmdFail"), ex.Message ?? "command");
                AppendLog(fail, LogLevel.Warning);
                if (replyClientId > 0) SendAosAck(replyClientId, false, fail);
                return;
            }
            if (published == null || !published.Ok)
            {
                var fail = string.Format(UiStrings.T("PeerSyncCmdFail"), published != null ? published.Error : "command");
                AppendLog(fail, LogLevel.Warning);
                if (replyClientId > 0) SendAosAck(replyClientId, false, fail);
                return;
            }
            _settings.PeerSyncLastCmdSeq = Math.Max(_settings.PeerSyncLastCmdSeq, published.CmdSeq);
            _settings.PeerSyncLastCmdId = cmdId;
            try { _settings.Save(); }
            catch { }
            AppendLog(string.Format(UiStrings.T("PeerSyncCmdSent"), targetVer), LogLevel.Info);
            if (replyClientId > 0) SendAosAck(replyClientId, true, "peerSyncUpdateAll");

            // Also update local site when includeLocal.
            if (cmdPayload.IncludeLocal && !_remoteServerUpdateBusy)
            {
                try
                {
                    var update = await UpdateService.PeekAsync().ConfigureAwait(true);
                    if (update != null)
                        await ApplySelfUpdateAsync(update, false, null, UiStrings.T("PeerSyncUpdateAll")).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    AppendLog(string.Format(UiStrings.T("UpdateFailed"), ex.Message ?? "?"), LogLevel.Warning);
                }
            }
        }

        private async Task HandleInboundPeerCommandAsync(PeerCommandEvent ev, string secret)
        {
            if (_settings == null || ev == null) return;
            PeerCommandPayload payload;
            if (!PeerSyncCrypto.TryDecryptCommand(secret, ev.PayloadEnc, out payload) || payload == null)
            {
                AppendLog(string.Format(UiStrings.T("PeerSyncCmdReject"),
                    string.IsNullOrEmpty(ev.OriginName) ? ev.OriginServerId : ev.OriginName, "decrypt"), LogLevel.Warning);
                return;
            }
            _settings.PeerSyncLastCmdSeq = Math.Max(_settings.PeerSyncLastCmdSeq, ev.CmdSeq);
            _settings.PeerSyncLastCmdId = ev.CmdId ?? _settings.PeerSyncLastCmdId;
            try { _settings.Save(); }
            catch { }

            if (!string.Equals(payload.Type, PeerSyncLogic.CommandTypeUpdateAll, StringComparison.OrdinalIgnoreCase))
                return;

            var origin = string.IsNullOrEmpty(ev.OriginName) ? (ev.OriginServerId ?? "?") : ev.OriginName;
            AppendLog(string.Format(UiStrings.T("PeerSyncCmdRecv"), origin, payload.TargetVersion ?? "?"), LogLevel.Info);
            if (_remoteServerUpdateBusy) return;
            try
            {
                var update = await UpdateService.PeekAsync().ConfigureAwait(true);
                if (update == null)
                {
                    AppendLog(UiStrings.T("UpdateNone"), LogLevel.Info);
                    return;
                }
                await ApplySelfUpdateAsync(update, false, null, UiStrings.T("PeerSyncUpdateAll")).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                AppendLog(string.Format(UiStrings.T("UpdateFailed"), ex.Message ?? "?"), LogLevel.Warning);
            }
        }

        private void PeerHeartbeatDebounce_Tick(object sender, EventArgs e)
        {
            _peerHeartbeatDebounce.Stop();
            if (_settings == null || !_settings.PeerSyncEnabled) return;
            Forget(SendPeerHeartbeatStandaloneAsync(), "PeerHeartbeat");
        }

        private void SchedulePeerHeartbeat()
        {
            if (_settings == null || !_settings.PeerSyncEnabled) return;
            _peerHeartbeatDebounce.Stop();
            _peerHeartbeatDebounce.Start();
        }

        private async Task SendPeerHeartbeatStandaloneAsync()
        {
            if (_settings == null || !_settings.PeerSyncEnabled) return;
            string url = _settings.PeerSyncUrl ?? "";
            string secret;
            try { secret = _settings.GetPeerSyncSecret() ?? ""; }
            catch { secret = ""; }
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(secret)) return;
            await SendPeerHeartbeatAsync(url, secret).ConfigureAwait(true);
        }

        private async Task SendPeerHeartbeatAsync(string url, string secret)
        {
            if (_settings == null) return;
            var statusEnc = BuildLocalPeerStatusEnc(secret);
            try
            {
                var hb = await PeerSyncClient.HeartbeatAsync(
                    url, secret,
                    _settings.PeerSyncServerId,
                    _settings.PeerSyncServerName,
                    statusEnc,
                    CancellationToken.None).ConfigureAwait(true);
                if (hb != null && hb.Ok && hb.PeersSeq > _peerSyncLastPeersSeq)
                    _peerSyncLastPeersSeq = hb.PeersSeq;
            }
            catch { }
        }

        private string BuildLocalPeerStatusEnc(string secret)
        {
            if (_settings == null || string.IsNullOrEmpty(secret)) return "";
            var status = new PeerStatusPayload
            {
                ServerName = _settings.PeerSyncServerName ?? "",
                AppVersion = AppVersionDisplay ?? "",
                TokenStatus = BuildLocalTokenStatusLabel(),
                TokenExpiryUtc = _settings.LastTokenExpiryUtc.HasValue
                    ? _settings.LastTokenExpiryUtc.Value.ToUniversalTime().ToString("o")
                    : "",
                ClientCount = SafeNotifyClientCount(),
                LastSyncOk = _peerLastSyncOk,
                LastSyncUtc = _peerLastSyncUtc ?? "",
                LastSyncMsg = _peerLastSyncMsg ?? ""
            };
            try { return PeerSyncCrypto.EncryptStatus(secret, status); }
            catch { return ""; }
        }

        private string BuildLocalTokenStatusLabel()
        {
            if (_settings == null) return "?";
            if (IsAuthCooldownActive()) return "Cooldown";
            if (_settings.AuthRegisterBlocked) return "Blocked";
            if (_settings.LastTokenExpiryUtc.HasValue)
            {
                if (_settings.LastTokenExpiryUtc.Value.ToUniversalTime() <= DateTime.UtcNow)
                    return "Expired";
                return "OK";
            }
            return string.IsNullOrEmpty(_settings.Email) ? "—" : "Unknown";
        }

        private static int SafeNotifyClientCount()
        {
            try { return NotifyServer.Instance.ClientCount; }
            catch { return 0; }
        }

        private void NotePeerSyncOutcome(bool ok, string message)
        {
            _peerLastSyncOk = ok;
            _peerLastSyncUtc = DateTime.UtcNow.ToString("o");
            _peerLastSyncMsg = message ?? "";
            SchedulePeerHeartbeat();
        }

        private void ApplyPeerDashboardFromWire(List<PeerRecord> wirePeers, string secret)
        {
            if (wirePeers == null) return;
            var pollMin = _settings != null
                ? PeerSyncLogic.ClampPollMinutes(_settings.PeerSyncPollMinutes)
                : 60;
            var localId = _settings != null ? (_settings.PeerSyncServerId ?? "") : "";
            var now = DateTime.UtcNow;
            var decoded = new List<PeerRecord>();
            foreach (var p in wirePeers)
            {
                if (p == null || string.IsNullOrWhiteSpace(p.ServerId)) continue;
                PeerStatusPayload status = null;
                if (!string.IsNullOrEmpty(p.StatusEnc) && !string.IsNullOrEmpty(secret))
                    PeerSyncCrypto.TryDecryptStatus(secret, p.StatusEnc, out status);
                p.Status = status;
                p.Health = PeerSyncLogic.ClassifyPeerHealth(p.LastSeenUtc, pollMin, now);
                decoded.Add(p);
            }
            var sorted = PeerSyncLogic.SortPeersForDisplay(decoded, localId);
            var rows = new List<PeerDashboardWire>();
            foreach (var p in sorted)
            {
                var st = p.Status;
                var name = st != null && !string.IsNullOrEmpty(st.ServerName)
                    ? st.ServerName
                    : (p.ServerId ?? "?");
                if (PeerSyncLogic.ShouldIgnoreOwnEvent(p.ServerId, localId))
                    name = name + " (" + UiStrings.T("PeerSyncThisServer") + ")";
                rows.Add(new PeerDashboardWire
                {
                    ServerId = p.ServerId ?? "",
                    ServerName = name,
                    Health = p.Health ?? PeerSyncLogic.HealthOffline,
                    TokenStatus = st != null ? (st.TokenStatus ?? "—") : "—",
                    AppVersion = st != null ? (st.AppVersion ?? "—") : "—",
                    ClientCount = st != null ? st.ClientCount.ToString(CultureInfo.InvariantCulture) : "—",
                    LastSeen = FormatPeerSeen(p.LastSeenUtc),
                    LastSync = st != null ? FormatPeerSyncSummary(st) : "—"
                });
            }
            _peerDashboardRows.Clear();
            foreach (var r in rows)
                _peerDashboardRows.Add(r);
            UpdatePeerDashboardEmptyHint();
            BroadcastPeerStatusSync(rows);
        }

        private static string FormatPeerSeen(string lastSeenUtc)
        {
            DateTime utc;
            if (!PeerSyncLogic.TryParseUtc(lastSeenUtc, out utc)) return "—";
            try { return utc.ToLocalTime().ToString("g"); }
            catch { return lastSeenUtc; }
        }

        private static string FormatPeerSyncSummary(PeerStatusPayload st)
        {
            if (st == null) return "—";
            var ok = st.LastSyncOk ? "OK" : "Fail";
            DateTime utc;
            if (PeerSyncLogic.TryParseUtc(st.LastSyncUtc, out utc))
            {
                try { return ok + " · " + utc.ToLocalTime().ToString("g"); }
                catch { }
            }
            return ok;
        }

        private void UpdatePeerDashboardEmptyHint()
        {
            if (PeerSyncPeersEmptyHint == null) return;
            PeerSyncPeersEmptyHint.Text = UiStrings.T("PeerSyncPeersEmpty");
            PeerSyncPeersEmptyHint.Visibility = _peerDashboardRows.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void BroadcastPeerStatusSync(List<PeerDashboardWire> rows)
        {
            if (_settings == null || !_settings.NotifyServerEnabled) return;
            var payload = PeerDashboardWire.Encode(rows);
            if (string.Equals(payload, _peerStatusSyncSignature, StringComparison.Ordinal))
                return;
            _peerStatusSyncSignature = payload;
            try
            {
                BroadcastNotify(new NotifyMessage
                {
                    Kind = "peerStatusSync",
                    Level = "info",
                    Title = "KAI - AOS Renew",
                    Text = payload
                });
            }
            catch { }
        }

        private void SendPeerStatusSyncTo(int clientId)
        {
            if (_settings == null || !_settings.NotifyServerEnabled) return;
            try
            {
                var rows = new List<PeerDashboardWire>(_peerDashboardRows);
                NotifyServer.Instance.SendTo(clientId, new NotifyMessage
                {
                    Kind = "peerStatusSync",
                    Level = "info",
                    Title = "KAI - AOS Renew",
                    Text = PeerDashboardWire.Encode(rows)
                });
            }
            catch { }
        }
    }
}
