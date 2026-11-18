using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace KaiAosRenew
{
    public partial class MainWindow : Window
    {
        private int _peerSyncBusy;
        private int _peerSyncPending;
        private int _firebaseBootstrapBusy;

        private void InitPeerSync()
        {
            InitFirebasePush();
        }

        /// <summary>True when AOS Access email and/or password are missing (UI or settings).</summary>
        private bool IsLocalAosCredentialsIncomplete()
        {
            var email = "";
            if (EmailBox != null && !string.IsNullOrWhiteSpace(EmailBox.Text))
                email = EmailBox.Text.Trim();
            else if (_settings != null)
                email = (_settings.Email ?? "").Trim();

            var pass = "";
            try
            {
                pass = (GetPasswordUi() ?? "").Trim();
                if (string.IsNullOrEmpty(pass) && _settings != null)
                    pass = (_settings.GetPassword() ?? "").Trim();
            }
            catch
            {
                try { pass = _settings != null ? (_settings.GetPassword() ?? "").Trim() : ""; }
                catch { pass = ""; }
            }

            return string.IsNullOrEmpty(email) || string.IsNullOrEmpty(pass);
        }

        /// <summary>
        /// When local AOS email/password are empty and Firebase has creds: confirm, then force Register and save.
        /// Bypasses seq/own filters so bootstrap works even if the event was already acknowledged.
        /// </summary>
        private async Task TryBootstrapAosCredsFromFirebaseAsync()
        {
            if (_settings == null || !IsFirebasePushActive()) return;
            if (!IsLocalAosCredentialsIncomplete()) return;
            if (Interlocked.CompareExchange(ref _firebaseBootstrapBusy, 1, 0) != 0)
                return;

            try
            {
                string url, secret, serverName;
                if (!TryGetActivePeerRelay(out url, out secret, out serverName))
                    return;

                AppendLog(UiStrings.T("PeerSyncBootstrapStart"), LogLevel.Info);
                ResetFirebaseEtagsIfScopeChanged(url, secret);

                PeerRelayPollResult poll;
                try
                {
                    poll = await PeerSyncFirebaseClient.SnapshotAsync(
                        url, secret, null, true, CancellationToken.None).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    AppendLog(string.Format(UiStrings.T("PeerSyncBootstrapNoCreds"), ex.Message ?? "snapshot"),
                        LogLevel.Warning);
                    return;
                }

                if (poll == null || !poll.Ok)
                {
                    AppendLog(string.Format(UiStrings.T("PeerSyncBootstrapNoCreds"),
                        poll != null ? (poll.Error ?? "poll") : "poll"), LogLevel.Warning);
                    return;
                }
                PersistFirebaseCredsEtag(poll.ETag);

                var ev = poll.LatestCreds;
                if (ev == null || string.IsNullOrWhiteSpace(ev.PayloadEnc))
                {
                    AppendLog(string.Format(UiStrings.T("PeerSyncBootstrapNoCreds"), "no latestCreds"),
                        LogLevel.Info);
                    return;
                }

                PeerCredsPayload payload;
                if (!PeerSyncCrypto.TryDecryptCreds(secret, ev.PayloadEnc, out payload) || payload == null)
                {
                    AppendLog(string.Format(UiStrings.T("PeerSyncBootstrapNoCreds"), "decrypt"),
                        LogLevel.Warning);
                    return;
                }

                var remoteEmail = (payload.Email ?? "").Trim();
                var remotePass = payload.Password ?? "";
                if (string.IsNullOrEmpty(remoteEmail) || string.IsNullOrEmpty(remotePass))
                {
                    AppendLog(string.Format(UiStrings.T("PeerSyncBootstrapNoCreds"), "empty payload"),
                        LogLevel.Info);
                    return;
                }

                // Still incomplete? (user may have typed meanwhile)
                if (!IsLocalAosCredentialsIncomplete()) return;

                var confirmText = string.Format(UiStrings.T("PeerSyncBootstrapConfirm"), remoteEmail);
                var answer = MessageBoxResult.No;
                if (!Dispatcher.CheckAccess())
                {
                    answer = (MessageBoxResult)Dispatcher.Invoke(new Func<MessageBoxResult>(() =>
                        MessageBox.Show(this, confirmText, "KAI - AOS Renew",
                            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)));
                }
                else
                {
                    answer = MessageBox.Show(this, confirmText, "KAI - AOS Renew",
                        MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
                }

                if (answer != MessageBoxResult.Yes)
                {
                    AppendLog(UiStrings.T("PeerSyncBootstrapCancelled"), LogLevel.Info);
                    return;
                }

                // Direct apply — skip ShouldProcessInboundCreds (seq/own) for empty-local bootstrap.
                await HandleInboundPeerCredsAsync(ev, secret).ConfigureAwait(true);
            }
            finally
            {
                Interlocked.Exchange(ref _firebaseBootstrapBusy, 0);
            }
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
            if (_settings == null || !IsPeerRelayActive()) return;
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
            if (_settings == null || !IsPeerRelayActive()) return;
            string url, secret, serverName;
            if (!TryGetActivePeerRelay(out url, out secret, out serverName))
            {
                if (manual)
                    SetActiveRelayStatus(string.Format(UiStrings.T("FirebasePushStatusFail"), "URL/secret"));
                return;
            }
            var serverId = _settings.PeerSyncServerId ?? "";
            ResetFirebaseEtagsIfScopeChanged(url, secret);
            PeerRelayPollResult poll;
            try
            {
                poll = await PeerSyncFirebaseClient.SnapshotAsync(
                        url, secret,
                        manual ? null : (_settings.FirebasePushCredsEtag ?? ""),
                        manual,
                        CancellationToken.None)
                    .ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                SetActiveRelayStatus(string.Format(UiStrings.T("FirebasePushStatusFail"), ex.Message ?? "poll"));
                return;
            }
            if (poll == null || !poll.Ok)
            {
                SetActiveRelayStatus(string.Format(
                    UiStrings.T("FirebasePushStatusFail"),
                    poll != null ? poll.Error : "poll"));
                return;
            }
            PersistFirebaseCredsEtag(poll.ETag);
            SetActiveRelayStatus(UiStrings.T("FirebasePushStatusOk"));
            if (poll.Unchanged) return;

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
                _settings.PeerSyncLastSeq = poll.Seq;
                _settings.PeerSyncLastEventId = poll.LatestCreds.EventId ?? _settings.PeerSyncLastEventId;
                try { _settings.Save(); }
                catch { }
            }
        }

        /// <summary>Apply credentials delivered directly in an SSE put (no Snapshot GET).</summary>
        private async Task HandleInboundFromSseAsync(PeerCredsEvent creds)
        {
            if (_settings == null || !IsPeerRelayActive() || creds == null) return;
            string url, secret, serverName;
            if (!TryGetActivePeerRelay(out url, out secret, out serverName)) return;
            var serverId = _settings.PeerSyncServerId ?? "";
            SetActiveRelayStatus(UiStrings.T("FirebasePushStatusOk"));
            if (PeerSyncLogic.ShouldProcessInboundCreds(
                creds, serverId, _settings.PeerSyncLastSeq, _settings.PeerSyncLastEventId))
            {
                await HandleInboundPeerCredsAsync(creds, secret).ConfigureAwait(true);
            }
            else if (creds.Seq > _settings.PeerSyncLastSeq
                     && PeerSyncLogic.ShouldIgnoreOwnEvent(creds.OriginServerId, serverId))
            {
                _settings.PeerSyncLastSeq = creds.Seq;
                _settings.PeerSyncLastEventId = creds.EventId ?? _settings.PeerSyncLastEventId;
                try { _settings.Save(); }
                catch { }
            }
        }

        private bool IsFirebasePushPublishAllowed()
        {
            return IsPeerRelayActive();
        }

        private async Task PublishPeerCredentialsAsync(string email, string password, int intervalHours, int blockAfter)
        {
            if (_settings == null || !IsFirebasePushPublishAllowed()) return;
            string url, secret, serverName;
            if (!TryGetActivePeerRelay(out url, out secret, out serverName)) return;

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
            PeerRelayActionResult published;
            try
            {
                published = await PeerSyncFirebaseClient.PublishAsync(
                    url, secret, eventId,
                    _settings.PeerSyncServerId, serverName,
                    enc, _settings.PeerSyncLastSeq, CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                AppendLog(string.Format(UiStrings.T("PeerSyncPublishFail"), ex.Message ?? "publish"), LogLevel.Warning);
                return;
            }
            if (published == null || !published.Ok)
            {
                AppendLog(string.Format(UiStrings.T("PeerSyncPublishFail"), published != null ? published.Error : "publish"), LogLevel.Warning);
                return;
            }
            _settings.PeerSyncLastSeq = Math.Max(_settings.PeerSyncLastSeq, published.Seq);
            _settings.PeerSyncLastEventId = eventId;
            _settings.PeerSyncLastCredsHash = PeerSyncLogic.ComputeCredsHash(email, password);
            ClearFirebaseEtagsPersistent();
            try { _settings.Save(); }
            catch { }
            AppendLog(string.Format(UiStrings.T("PeerSyncPublished"), eventId), LogLevel.Info);
            SetActiveRelayStatus(UiStrings.T("FirebasePushStatusOk"));
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
                _settings.PeerSyncLastCredsHash = PeerSyncLogic.ComputeCredsHash(prevEmail, prevPass);
                try { _settings.Save(); }
                catch { }
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
                return;
            }

            var rejectMsg = result != null ? (result.Message ?? "Register failed") : "Register failed";
            AppendLog(string.Format(UiStrings.T("PeerSyncRejected"), originLabel, rejectMsg), LogLevel.Warning);

            var rollbackAttempted = !string.IsNullOrEmpty(prevEmail) && !string.IsNullOrEmpty(prevPass);
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
                var rollbackOk = PeerSyncLogic.ShouldCommitCredentials(
                    rollback != null && rollback.Ok,
                    rollback != null && rollback.RegisterAttempted,
                    rollback != null && rollback.RegisterAuthRejected,
                    rollback != null && rollback.SkippedRegister)
                    || (rollback != null && rollback.Ok);
                if (rollbackOk)
                {
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

            _settings.PeerSyncLastSeq = Math.Max(_settings.PeerSyncLastSeq, ev.Seq);
            _settings.PeerSyncLastEventId = ev.EventId ?? _settings.PeerSyncLastEventId;
            try { _settings.Save(); }
            catch { }

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
            _settings.PeerSyncLastCredsHash = PeerSyncLogic.ComputeCredsHash(payload.Email, payload.Password);
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

        /// <summary>
        /// Called after a successful local Force Register that changed credentials.
        /// </summary>
        private void MaybePublishPeerCredentialsAfterLocalRegister(RenewResult result, string email, string password)
        {
            if (_settings == null || !IsFirebasePushPublishAllowed()) return;
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
            // Skip republish when credentials did not actually change (e.g. token refresh).
            if (PeerSyncLogic.CredsHashMatches(email, password, _settings.PeerSyncLastCredsHash))
                return;
            var hours = _settings.IntervalHours < 1 ? 1 : _settings.IntervalHours;
            var block = GetAuthRegisterBlockAfter();
            Forget(PublishPeerCredentialsAsync(email, password, hours, block), "PublishPeerCredentials");
        }

        /// <summary>
        /// On local auth failure / cooldown block, pull newer Firebase credentials once and retry Register.
        /// Returns true if credentials were applied successfully (caller should treat run as OK).
        /// </summary>
        private async Task<bool> TryPullNewerPeerCredsOnAuthFailAsync(string currentEmail, string currentPassword)
        {
            if (_settings == null || !IsPeerRelayActive()) return false;
            string url, secret, serverName;
            if (!TryGetActivePeerRelay(out url, out secret, out serverName)) return false;
            ResetFirebaseEtagsIfScopeChanged(url, secret);

            // Cheap seq check first — avoid downloading latestCreds when nothing changed.
            try
            {
                var seqCheck = await PeerSyncFirebaseClient.CheckLatestSeqAsync(
                    url, secret, _settings.FirebasePushSeqEtag, CancellationToken.None).ConfigureAwait(true);
                if (seqCheck != null)
                    PersistFirebaseSeqEtag(seqCheck.ETag);
                if (seqCheck != null && seqCheck.Ok
                    && (seqCheck.Unchanged || seqCheck.Seq <= _settings.PeerSyncLastSeq))
                    return false;
            }
            catch { }

            PeerRelayPollResult poll;
            try
            {
                poll = await PeerSyncFirebaseClient.SnapshotAsync(
                    url, secret, null, true, CancellationToken.None).ConfigureAwait(true);
            }
            catch
            {
                return false;
            }
            if (poll == null || !poll.Ok || poll.LatestCreds == null) return false;
            PersistFirebaseCredsEtag(poll.ETag);

            var serverId = _settings.PeerSyncServerId ?? "";
            var ev = poll.LatestCreds;
            if (PeerSyncLogic.ShouldIgnoreOwnEvent(ev.OriginServerId, serverId))
                return false;

            PeerCredsPayload payload;
            if (!PeerSyncCrypto.TryDecryptCreds(secret, ev.PayloadEnc, out payload) || payload == null)
                return false;
            if (!PeerSyncLogic.CredentialsDiffer(currentEmail, currentPassword, payload.Email, payload.Password))
            {
                if (ev.Seq > _settings.PeerSyncLastSeq)
                {
                    _settings.PeerSyncLastSeq = ev.Seq;
                    _settings.PeerSyncLastEventId = ev.EventId ?? _settings.PeerSyncLastEventId;
                    try { _settings.Save(); } catch { }
                }
                return false;
            }

            var candidate = CloneSettingsForPeerRegister(payload.Email, payload.Password, payload.IntervalHours, payload.BlockAfter);
            RenewResult retry = null;
            try
            {
                retry = await Task.Run(() => RenewEngine.Run(candidate, true, payload.Password, true)).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                retry = new RenewResult { Ok = false, Message = ex.Message, RegisterAttempted = true };
            }

            if (!PeerSyncLogic.ShouldCommitCredentials(
                retry != null && retry.Ok,
                retry != null && retry.RegisterAttempted,
                retry != null && retry.RegisterAuthRejected,
                retry != null && retry.SkippedRegister))
                return false;

            ApplyPeerCredentialsCommit(payload, retry);
            _settings.PeerSyncLastSeq = Math.Max(_settings.PeerSyncLastSeq, ev.Seq);
            _settings.PeerSyncLastEventId = ev.EventId ?? _settings.PeerSyncLastEventId;
            try { _settings.Save(); } catch { }
            var originLabel = string.IsNullOrEmpty(ev.OriginName) ? (ev.OriginServerId ?? "?") : ev.OriginName;
            AppendLog(string.Format(UiStrings.T("PeerSyncApplied"), originLabel), LogLevel.Success);
            BroadcastAosSync();
            BroadcastServerSettings();
            return true;
        }

        private async Task PushPeerCredentialsNowAsync(int replyClientId)
        {
            if (_settings == null || !IsPeerRelayActive())
            {
                var off = UiStrings.T("FirebasePushStatusDisabled");
                SetActiveRelayStatus(off);
                if (replyClientId > 0) SendAosAck(replyClientId, false, off);
                return;
            }
            CaptureFirebasePushFromUi();
            if (!IsPeerRelayActive())
            {
                var off = UiStrings.T("FirebasePushStatusDisabled");
                SetActiveRelayStatus(off);
                if (replyClientId > 0) SendAosAck(replyClientId, false, off);
                return;
            }
            var email = (_settings.Email ?? "").Trim();
            string password;
            try { password = _settings.GetPassword() ?? ""; }
            catch { password = ""; }
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                var fail = string.Format(UiStrings.T("PeerSyncPublishFail"), "empty credentials");
                SetActiveRelayStatus(fail);
                AppendLog(fail, LogLevel.Warning);
                if (replyClientId > 0) SendAosAck(replyClientId, false, fail);
                return;
            }
            var hours = _settings.IntervalHours < 1 ? 1 : _settings.IntervalHours;
            var block = GetAuthRegisterBlockAfter();
            await PublishPeerCredentialsAsync(email, password, hours, block).ConfigureAwait(true);
            AppendLog(UiStrings.T("FirebasePushPushDone"), LogLevel.Info);
            if (replyClientId > 0)
                SendAosAck(replyClientId, true, "firebasePushPush");
        }
    }
}
