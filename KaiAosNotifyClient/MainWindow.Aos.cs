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

        private void RequestAosSync()
        {
            if (_client == null || !_client.IsConnected) return;
            try
            {
                _client.Send(new NotifyMessage
                {
                    Kind = "aosSyncReq",
                    Level = "info",
                    Title = "KAI - AOS Renew - Client",
                    Text = ""
                });
            }
            catch { }
            RequestServerSettingsSync();
            RequestNotifyClients();
        }

        private void ApplyAosSync(string payload)
        {
            int hours;
            string email;
            string password;
            bool? forceUnused;
            int? blockAfter;
            if (!NotifyMessage.TryParseAosSync(payload, out hours, out email, out password, out forceUnused, out blockAfter))
                return;
            _loadingAosUi = true;
            try
            {
                if (AosIntervalBox != null) AosIntervalBox.Text = hours.ToString();
                _lastSyncedHours = hours;
                _lastSyncedEmail = (email ?? "").Trim();
                _lastSyncedPass = (password ?? "").Trim();
                if (!_aosDirty)
                {
                    if (AosEmailBox != null) AosEmailBox.Text = email ?? "";
                    SetAosPasswordUi(password ?? "");
                }
                if (blockAfter.HasValue && _settings != null)
                {
                    var n = blockAfter.Value;
                    if (n < 1) n = 1;
                    if (n > 10) n = 10;
                    _settings.AuthRegisterBlockAfter = n;
                    if (!_loadingSrvSettings && SrvAuthBlockAfterBox != null)
                        SrvAuthBlockAfterBox.Text = n.ToString();
                    try { _settings.Save(); }
                    catch { }
                }
                if (!_aosDirty)
                    _aosDirty = false;
                CancelAosAckWait();
                UpdateAosSyncStatus();
                UpdateAosControlsEnabled();
            }
            finally
            {
                _loadingAosUi = false;
            }
        }

        private void HandleAosAck(NotifyMessage msg)
        {
            var wasSettingsApply = _awaitingSettingsApply;
            var wasSettingsImport = _awaitingSettingsImport;
            var intervalOnlyAck = _awaitingAosIntervalOnly;
            CancelAosAckWait();
            _awaitingAosIntervalOnly = false;
            SetKaiBusy(false);
            if (FooterServerUpdateBtn != null) FooterServerUpdateBtn.IsEnabled = true;
            var level = (msg.Level ?? "").Trim().ToLowerInvariant();
            var text = msg.Text ?? "";
            // Download commands: status only, do not treat as AOS credential sync.
            if (!string.IsNullOrEmpty(text)
                && (text.StartsWith("getKeyBin", StringComparison.OrdinalIgnoreCase)
                    || text.StartsWith("getZip", StringComparison.OrdinalIgnoreCase)
                    || text.IndexOf("Bin not found", StringComparison.OrdinalIgnoreCase) >= 0
                    || text.IndexOf("Zip too large", StringComparison.OrdinalIgnoreCase) >= 0
                    || text.IndexOf("Folder missing", StringComparison.OrdinalIgnoreCase) >= 0
                    || text.IndexOf("Folder empty", StringComparison.OrdinalIgnoreCase) >= 0
                    || text.IndexOf("Unknown zip", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                if (level == "error")
                {
                    SetSrvSettingsStatus(string.Format(ClientUiStrings.T("SrvDownloadFail"), text), false);
                    AppendActivityRow(null, "error", string.Format(ClientUiStrings.T("SrvDownloadFail"), text));
                }
                return;
            }
            if (wasSettingsApply || wasSettingsImport
                || string.Equals(text, "settingsApply", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "settingsImport", StringComparison.OrdinalIgnoreCase))
            {
                if (level == "error")
                {
                    if (wasSettingsApply) MarkSrvSettingsDirty(true);
                    SetSrvSettingsStatus(
                        string.IsNullOrEmpty(text) ? ClientUiStrings.T("AosSyncSendFail") : text, false);
                    AppendActivityRow(null, "error", "Server Settings: " + (string.IsNullOrEmpty(text) ? "failed" : text));
                    return;
                }
                if (wasSettingsApply)
                {
                    ClearSrvSettingsConflict();
                    MarkSrvSettingsDirty(false);
                }
                if (wasSettingsImport)
                {
                    ClearSrvSettingsConflict();
                    SetSrvSettingsStatus(string.Format(ClientUiStrings.T("ImportOk"), "server"), false);
                    AppendActivityRow(null, "success", string.Format(ClientUiStrings.T("ImportOk"), "server"));
                }
                else
                    SetSrvSettingsStatus(ClientUiStrings.T("ServerSettingsSynced"), true);
                return;
            }
            if (level == "error")
            {
                if (!string.IsNullOrEmpty(_pendingServerUpdateVer) || _awaitingServerUpdateReconnect)
                {
                    _pendingServerUpdateVer = "";
                    _awaitingServerUpdateReconnect = false;
                    RefreshServerUpdateFromFeed();
                }
                if (!intervalOnlyAck)
                    _aosDirty = true;
                UpdateAosSyncStatus();
                UpdateAosControlsEnabled();
                AppendActivityRow(null, "error", "AOS: " + (string.IsNullOrEmpty(text) ? "failed" : text));
                JumpToActivityWithAlert();
                return;
            }
            if (!intervalOnlyAck)
            {
                _aosDirty = false;
                _lastSyncedEmail = AosEmailBox != null ? (AosEmailBox.Text ?? "").Trim() : "";
                _lastSyncedPass = (GetAosPasswordUi() ?? "").Trim();
            }
            int ackHours;
            if (AosIntervalBox != null
                && int.TryParse((AosIntervalBox.Text ?? "").Trim(), out ackHours)
                && ackHours >= 1 && ackHours <= 168)
                _lastSyncedHours = ackHours;
            UpdateAosSyncStatus();
            UpdateAosControlsEnabled();
            if (!string.IsNullOrEmpty(text) && !string.Equals(text, "OK", StringComparison.OrdinalIgnoreCase)
                && text.IndexOf("serverUpdate", StringComparison.OrdinalIgnoreCase) < 0)
                AppendActivityRow(null, "success", "AOS: " + text);
            // Server is applying update + restart — wait for reconnect, then re-check version.
            if (!string.IsNullOrEmpty(text) && text.IndexOf("serverUpdate", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var ver = text;
                var sp = text.IndexOf(' ');
                if (sp >= 0 && sp + 1 < text.Length)
                    ver = text.Substring(sp + 1).Trim();
                BeginAwaitServerUpdate(ver);
            }
        }

        private void AosAckTimer_Tick(object sender, EventArgs e)
        {
            var wasSettingsApply = _awaitingSettingsApply;
            var wasSettingsImport = _awaitingSettingsImport;
            var intervalOnlyAck = _awaitingAosIntervalOnly;
            CancelAosAckWait();
            _awaitingAosIntervalOnly = false;
            if (wasSettingsApply || wasSettingsImport)
            {
                if (wasSettingsApply) MarkSrvSettingsDirty(true);
                SetSrvSettingsStatus(ClientUiStrings.T("AosSyncTimeout"), false);
                AppendActivityRow(null, "error", "Server Settings: " + ClientUiStrings.T("AosSyncTimeout"));
                return;
            }
            if (!intervalOnlyAck)
                _aosDirty = true;
            if (FooterServerUpdateBtn != null) FooterServerUpdateBtn.IsEnabled = true;
            UpdateAosSyncStatus();
            UpdateAosControlsEnabled();
            AppendActivityRow(null, "error", ClientUiStrings.T("AosSyncTimeout"));
            JumpToActivityWithAlert();
        }

        private void CancelAosAckWait()
        {
            _aosAwaitingAck = false;
            _awaitingSettingsApply = false;
            _awaitingSettingsImport = false;
            try { _aosAckTimer.Stop(); }
            catch { }
            UpdateAosControlsEnabled();
        }

        private void BeginAosAckWait()
        {
            _aosAwaitingAck = true;
            UpdateAosSyncStatus();
            UpdateAosControlsEnabled();
            try
            {
                _aosAckTimer.Stop();
                _aosAckTimer.Start();
            }
            catch { }
        }

        private string GetAosPasswordUi()
        {
            if (AosShowPasswordCheck != null && AosShowPasswordCheck.IsChecked == true && AosPasswordPlainBox != null)
                return AosPasswordPlainBox.Text ?? "";
            if (AosPasswordBox != null)
                return AosPasswordBox.Password ?? "";
            return "";
        }

        private void SetAosPasswordUi(string password)
        {
            password = password ?? "";
            if (AosPasswordBox != null) AosPasswordBox.Password = password;
            if (AosPasswordPlainBox != null) AosPasswordPlainBox.Text = password;
        }

        private void ClearAosFieldInvalidHighlights()
        {
            ClearFieldInvalid(AosEmailBox);
            ClearFieldInvalid(AosPasswordBox);
            ClearFieldInvalid(AosPasswordPlainBox);
            ClearFieldInvalid(AosIntervalBox);
            ClearFieldInvalid(HostCombo);
            ClearFieldInvalid(PortBox);
        }

        private bool ShowAosFieldError(string msgKey, Control focusField, bool showUi)
        {
            var msg = ClientUiStrings.T(msgKey);
            ClearAosFieldInvalidHighlights();
            if (showUi)
            {
                MarkFieldInvalid(focusField);
                try { Activate(); } catch { }
                if (focusField != null) focusField.Focus();
                MessageBox.Show(this, msg, ClientUiStrings.T("AppTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return false;
        }

        private bool ValidateAosCredentials(bool showUi)
        {
            var email = AosEmailBox != null ? (AosEmailBox.Text ?? "").Trim() : "";
            var pass = (GetAosPasswordUi() ?? "").Trim();
            if (string.IsNullOrEmpty(email))
                return ShowAosFieldError("MissingEmail", AosEmailBox, showUi);
            if (email.IndexOf('@') < 0 || email.IndexOf(' ') >= 0)
                return ShowAosFieldError("InvalidUser", AosEmailBox, showUi);
            if (string.IsNullOrEmpty(pass))
            {
                Control focus = AosPasswordBox;
                if (AosShowPasswordCheck != null && AosShowPasswordCheck.IsChecked == true
                    && AosPasswordPlainBox != null && AosPasswordPlainBox.Visibility == Visibility.Visible)
                    focus = AosPasswordPlainBox;
                return ShowAosFieldError("MissingPassword", focus, showUi);
            }
            ClearFieldInvalid(AosEmailBox);
            ClearFieldInvalid(AosPasswordBox);
            ClearFieldInvalid(AosPasswordPlainBox);
            return true;
        }

        private void SendAosApply()
        {
            if (_loadingAosUi || _loadingUi) return;
            var explicitSave = _explicitAosSave;
            _explicitAosSave = false;

            if (_awaitingServerUpdateReconnect || _clientUpdateBusy)
            {
                if (explicitSave)
                {
                    try { Activate(); } catch { }
                    MessageBox.Show(this, ClientUiStrings.T("AosSaveUpdating"), ClientUiStrings.T("AppTitle"),
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return;
            }

            if (_client == null || !_client.IsConnected)
            {
                if (explicitSave)
                {
                    try { Activate(); } catch { }
                    MessageBox.Show(this, ClientUiStrings.T("AosSaveOffline"), ClientUiStrings.T("AppTitle"),
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return;
            }

            if (_aosApplyInFlight || _aosAwaitingAck)
            {
                if (!explicitSave) return;
                if ((DateTime.UtcNow - _aosSaveClickUtc).TotalMilliseconds < 500)
                    return;
            }
            if (explicitSave)
            {
                if ((DateTime.UtcNow - _aosSaveClickUtc).TotalMilliseconds < 500 && _aosApplyInFlight)
                    return;
                _aosSaveClickUtc = DateTime.UtcNow;
            }

            // LostFocus must never MessageBox — that loops with focus restore.
            // Explicit Save keeps the modal; auto-apply fails silently if incomplete.
            if (!ValidateAosCredentials(explicitSave))
                return;
            int hours;
            if (!ValidateInterval(out hours, explicitSave))
                return;

            var email = AosEmailBox != null ? (AosEmailBox.Text ?? "").Trim() : "";
            var pass = (GetAosPasswordUi() ?? "").Trim();

            bool? forceRegister = null;
            if (explicitSave)
            {
                var credsChanged =
                    !string.Equals(email, _lastSyncedEmail ?? "", StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(pass, _lastSyncedPass ?? "", StringComparison.Ordinal);
                if (credsChanged)
                {
                    var confirm = MessageBox.Show(
                        this,
                        ClientUiStrings.T("CredentialsChangedConfirm"),
                        ClientUiStrings.T("AppTitle"),
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question,
                        MessageBoxResult.Yes);
                    forceRegister = confirm == MessageBoxResult.Yes;
                }
            }

            _aosApplyInFlight = true;
            try
            {
                var blockAfter = _settings != null ? _settings.AuthRegisterBlockAfter : 3;
                if (SrvAuthBlockAfterBox != null)
                {
                    int parsed;
                    if (int.TryParse((SrvAuthBlockAfterBox.Text ?? "").Trim(), out parsed) && parsed >= 1)
                        blockAfter = parsed > 10 ? 10 : parsed;
                }
                var payload = NotifyMessage.EncodeAosSync(hours, email, pass, forceRegister, blockAfter);
                var ok = _client.Send(new NotifyMessage
                {
                    Kind = "aosApply",
                    Level = "info",
                    Title = "KAI - AOS Renew - Client",
                    Text = payload
                });
                if (!ok)
                {
                    AppendActivityRow(null, "error", ClientUiStrings.T("AosSyncSendFail"));
                    JumpToActivityWithAlert();
                    return;
                }
                ClearAosFieldInvalidHighlights();
                _awaitingAosIntervalOnly = false;
                BeginAosAckWait();
            }
            finally
            {
                _aosApplyInFlight = false;
            }
        }

        private void SendAosCmd(string cmd)
        {
            if (_client == null || !_client.IsConnected) return;
            var ok = _client.Send(new NotifyMessage
            {
                Kind = "aosCmd",
                Level = "info",
                Title = "KAI - AOS Renew - Client",
                Text = cmd ?? ""
            });
            if (!ok)
            {
                AppendActivityRow(null, "error", ClientUiStrings.T("AosSyncSendFail"));
                JumpToActivityWithAlert();
                return;
            }
            _awaitingAosIntervalOnly = false;
            BeginAosAckWait();
        }

        private void RecomputeAosDirtyFromUi()
        {
            var email = AosEmailBox != null ? (AosEmailBox.Text ?? "").Trim() : "";
            var pass = (GetAosPasswordUi() ?? "").Trim();
            _aosDirty = !string.Equals(email, _lastSyncedEmail ?? "", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(pass, _lastSyncedPass ?? "", StringComparison.Ordinal);
        }

        private void AosField_TextChanged(object sender, TextChangedEventArgs e)
        {
            ClearFieldInvalid(sender as Control);
            ClearFieldInvalid(AosEmailBox);
            ClearFieldInvalid(AosIntervalBox);
            ClearFieldInvalid(AosPasswordPlainBox);
            if (_loadingAosUi || _loadingUi) return;
            // Interval changes sync alone on LostFocus; only email/password mark dirty.
            if (ReferenceEquals(sender, AosIntervalBox)) return;
            RecomputeAosDirtyFromUi();
            UpdateAosSyncStatus();
            UpdateAosControlsEnabled();
        }

        private void AosPassword_Changed(object sender, RoutedEventArgs e)
        {
            ClearFieldInvalid(AosPasswordBox);
            ClearFieldInvalid(AosPasswordPlainBox);
            if (_loadingAosUi || _loadingUi) return;
            RecomputeAosDirtyFromUi();
            UpdateAosSyncStatus();
            UpdateAosControlsEnabled();
        }

        private void AosField_LostFocus(object sender, RoutedEventArgs e)
        {
            // Interval auto-applies hours only; email/password sync only via Save.
            if (!ReferenceEquals(sender, AosIntervalBox)) return;
            if (_loadingAosUi || _loadingUi) return;
            if (_client == null || !_client.IsConnected) return;
            SendAosIntervalApply();
        }

        private void AosSave_Click(object sender, RoutedEventArgs e)
        {
            _explicitAosSave = true;
            SendAosApply();
        }

        private void SendAosIntervalApply()
        {
            if (_loadingAosUi || _loadingUi) return;
            if (_awaitingServerUpdateReconnect || _clientUpdateBusy) return;
            if (_client == null || !_client.IsConnected) return;
            if (_aosApplyInFlight || _aosAwaitingAck) return;
            int hours;
            if (!ValidateInterval(out hours, false)) return;
            if (hours == _lastSyncedHours) return;
            _aosApplyInFlight = true;
            try
            {
                var ok = _client.Send(new NotifyMessage
                {
                    Kind = "aosInterval",
                    Level = "info",
                    Title = "KAI - AOS Renew - Client",
                    Text = hours.ToString()
                });
                if (!ok)
                {
                    AppendActivityRow(null, "error", ClientUiStrings.T("AosSyncSendFail"));
                    JumpToActivityWithAlert();
                    return;
                }
                _awaitingAosIntervalOnly = true;
                BeginAosAckWait();
            }
            finally
            {
                _aosApplyInFlight = false;
            }
        }

        private void AosShowPassword_Changed(object sender, RoutedEventArgs e)
        {
            var show = AosShowPasswordCheck != null && AosShowPasswordCheck.IsChecked == true;
            if (show)
            {
                if (AosPasswordPlainBox != null && AosPasswordBox != null)
                    AosPasswordPlainBox.Text = AosPasswordBox.Password;
                if (AosPasswordBox != null) AosPasswordBox.Visibility = Visibility.Collapsed;
                if (AosPasswordPlainBox != null) AosPasswordPlainBox.Visibility = Visibility.Visible;
            }
            else
            {
                if (AosPasswordBox != null && AosPasswordPlainBox != null)
                    AosPasswordBox.Password = AosPasswordPlainBox.Text ?? "";
                if (AosPasswordPlainBox != null) AosPasswordPlainBox.Visibility = Visibility.Collapsed;
                if (AosPasswordBox != null) AosPasswordBox.Visibility = Visibility.Visible;
            }
        }

        private void AosCheckBrowserPass_Click(object sender, RoutedEventArgs e)
        {
            var email = AosEmailBox != null ? (AosEmailBox.Text ?? "").Trim() : "";

            BrowserPasswordReader.Result found;
            try
            {
                found = BrowserPasswordReader.TryFindBmwAosLoginMeta(email);
            }
            catch (Exception ex)
            {
                found = new BrowserPasswordReader.Result { Error = ex.Message };
            }

            if (found == null || !found.Found)
            {
                var err = found != null ? found.Error : "unknown";
                var failMsg = string.Format(ClientUiStrings.T("BrowserPassCheckFail"), err);
                AppendActivityRow(null, "warning", failMsg);
                MessageBox.Show(this, failMsg, ClientUiStrings.T("AppTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(email) && !string.IsNullOrWhiteSpace(found.Username) && AosEmailBox != null)
            {
                AosEmailBox.Text = found.Username.Trim();
                email = AosEmailBox.Text.Trim();
            }

            var when = FormatBrowserSavedWhen(found.LastSavedLocal);
            var msg = string.Format(ClientUiStrings.T("BrowserPassCheckOk"), found.BrowserName, when);
            var hint = (_settings == null || _settings.BrowserPasswordHintOnAuthFail)
                ? string.Format(ClientUiStrings.T("BrowserPassHintNoDate"), found.BrowserName)
                : "";
            var site = SiteForClipboard(found.SourceUrl);
            var copiedHint = "";
            if (!string.IsNullOrEmpty(site))
            {
                try
                {
                    Clipboard.SetText(site);
                    _lastCopiedSiteForPassCheck = site;
                    copiedHint = string.Format(ClientUiStrings.T("BrowserPassSiteCopiedHint"), site);
                }
                catch { }
            }

            string statusLine = null;
            var statusOk = false;
            var offerFillSave = false;
            var previousPass = GetAosPasswordUi() ?? "";
            if (found.DecryptStatus == ChromiumCrypto.DecryptStatus.Ok
                && !string.IsNullOrEmpty(found.Password))
            {
                var match = string.Equals(found.Password, previousPass, StringComparison.Ordinal);
                SetAosPasswordUi(found.Password);
                statusLine = ClientUiStrings.T("BrowserPassDecryptOk") + "\n"
                    + (match ? ClientUiStrings.T("BrowserPassMatch") : ClientUiStrings.T("BrowserPassMismatch"));
                statusOk = match;
                offerFillSave = true;
            }

            AppendActivityRow(null, "info", msg);
            if (!string.IsNullOrEmpty(copiedHint))
                AppendActivityRow(null, "info", copiedHint.Replace("\n", " "));
            if (!string.IsNullOrEmpty(statusLine))
                AppendActivityRow(null, statusOk ? "success" : "warning", statusLine.Replace("\n", " | "));

            var dlg = BrowserPassResultWindow.ShowFor(
                this, found.BrowserName, msg, copiedHint, statusLine, statusOk, hint, offerFillSave);

            if (dlg.OpenedPasswordManager)
                StartClipboardPasswordWatch();

            if (dlg.FillAndSaveRequested)
            {
                if (!string.IsNullOrEmpty(found.Password))
                    SetAosPasswordUi(found.Password);
                if (_client != null && _client.IsConnected)
                    SendAosApply();
                else
                {
                    RecomputeAosDirtyFromUi();
                    UpdateAosSyncStatus();
                    UpdateAosControlsEnabled();
                }
            }
        }

        // ---- Clipboard password watch (after opening browser password manager) ----
        private const int ClipboardWatchSeconds = 30;
        private string _clipboardPassCandidate;
        private string _lastCopiedSiteForPassCheck;
        private readonly DispatcherTimer _clipboardWatchTimer = new DispatcherTimer();
        private DateTime _clipboardWatchUntilUtc = DateTime.MinValue;
        private string _clipboardWatchInitial;

        private static string NormalizeClipboardPassword(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            var s = raw.Trim().Trim('\0');
            if (s.Length > 0 && s[0] == '\uFEFF')
                s = s.Substring(1);
            s = s.Replace("\u200B", "").Replace("\u200C", "").Replace("\u200D", "")
                .Replace("\u00A0", " ").Trim();
            return s;
        }

        private static bool LooksLikeDomainOrUrl(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            var t = s.Trim();
            if (t.IndexOf("://", StringComparison.Ordinal) >= 0) return true;
            if (t.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) return true;
            return System.Text.RegularExpressions.Regex.IsMatch(
                t, @"^([A-Za-z0-9-]+\.)+[A-Za-z]{2,}(:\d+)?(/\S*)?$");
        }

        private bool IsClipboardPasswordCandidate(string clip)
        {
            if (string.IsNullOrEmpty(clip)) return false;
            if (clip.IndexOf('\r') >= 0 || clip.IndexOf('\n') >= 0) return false;
            if (clip.Length > 256) return false;
            if (LooksLikeDomainOrUrl(clip)) return false;
            if (!string.IsNullOrEmpty(_lastCopiedSiteForPassCheck)
                && string.Equals(clip, _lastCopiedSiteForPassCheck, StringComparison.OrdinalIgnoreCase))
                return false;
            var current = GetAosPasswordUi() ?? "";
            if (string.Equals(clip, current, StringComparison.Ordinal)) return false;
            return true;
        }

        private void StartClipboardPasswordWatch()
        {
            string initial = null;
            try { initial = Clipboard.GetText(); }
            catch { }
            _clipboardWatchInitial = NormalizeClipboardPassword(initial) ?? "";
            _clipboardWatchUntilUtc = DateTime.UtcNow.AddSeconds(ClipboardWatchSeconds);
            _clipboardWatchTimer.Stop();
            _clipboardWatchTimer.Interval = TimeSpan.FromMilliseconds(400);
            _clipboardWatchTimer.Tick -= ClipboardWatchTimer_Tick;
            _clipboardWatchTimer.Tick += ClipboardWatchTimer_Tick;
            _clipboardWatchTimer.Start();
        }

        private void StopClipboardPasswordWatch()
        {
            _clipboardWatchTimer.Stop();
            _clipboardWatchUntilUtc = DateTime.MinValue;
            _clipboardWatchInitial = null;
        }

        private void ClipboardWatchTimer_Tick(object sender, EventArgs e)
        {
            if (DateTime.UtcNow >= _clipboardWatchUntilUtc)
            {
                StopClipboardPasswordWatch();
                return;
            }
            string raw = null;
            try { raw = Clipboard.GetText(); }
            catch { return; }
            var clip = NormalizeClipboardPassword(raw);
            if (string.IsNullOrEmpty(clip)) return;
            if (string.Equals(clip, _clipboardWatchInitial ?? "", StringComparison.Ordinal)) return;
            if (!IsClipboardPasswordCandidate(clip))
            {
                _clipboardWatchInitial = clip;
                return;
            }

            StopClipboardPasswordWatch();
            ShowFromTray();
            try { Activate(); }
            catch { }
            if (_aboutVisible)
                HideAboutPanel();
            if (TabAos != null)
                TabAos.IsSelected = true;
            ShowClipboardPassBanner(clip);
            if (ClipboardPassBanner != null
                && ClipboardPassBanner.Visibility == Visibility.Visible
                && ClipboardPassPasteSaveBtn != null)
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    try { ClipboardPassPasteSaveBtn.Focus(); }
                    catch { }
                }), DispatcherPriority.Input);
            }
        }

        private void ShowClipboardPassBanner(string clip)
        {
            if (ClipboardPassBanner == null || string.IsNullOrEmpty(clip)) return;
            var current = GetAosPasswordUi() ?? "";
            _clipboardPassCandidate = clip;
            if (ClipboardPassHeaderText != null)
                ClipboardPassHeaderText.Text = ClientUiStrings.T("ClipboardPassHeader");
            if (ClipboardPassCurrentText != null)
                ClipboardPassCurrentText.Text = string.Format(ClientUiStrings.T("ClipboardPassCurrent"),
                    string.IsNullOrEmpty(current) ? "—" : current);
            if (ClipboardPassClipText != null)
                ClipboardPassClipText.Text = string.Format(ClientUiStrings.T("ClipboardPassClip"), clip);
            ApplyClipboardPassBannerStrings();
            ClipboardPassBanner.Visibility = Visibility.Visible;
        }

        private void HideClipboardPassBanner()
        {
            _clipboardPassCandidate = null;
            if (ClipboardPassBanner != null)
                ClipboardPassBanner.Visibility = Visibility.Collapsed;
        }

        private void ApplyClipboardPassBannerStrings()
        {
            if (ClipboardPassPasteSaveBtn != null)
                ClipboardPassPasteSaveBtn.Content = ClientUiStrings.T("ClipboardPassPasteSave");
            if (ClipboardPassDismissBtn != null)
                ClipboardPassDismissBtn.Content = ClientUiStrings.T("ClipboardPassDismiss");
        }

        private void ClipboardPassPasteSave_Click(object sender, RoutedEventArgs e)
        {
            var pass = _clipboardPassCandidate;
            HideClipboardPassBanner();
            if (string.IsNullOrEmpty(pass)) return;
            SetAosPasswordUi(pass);
            if (_client != null && _client.IsConnected)
                SendAosApply();
            else
            {
                RecomputeAosDirtyFromUi();
                UpdateAosSyncStatus();
                UpdateAosControlsEnabled();
            }
        }

        private void ClipboardPassDismiss_Click(object sender, RoutedEventArgs e)
        {
            HideClipboardPassBanner();
        }

        private void AosCheckToken_Click(object sender, RoutedEventArgs e)
        {
            SendAosCmd("checkToken");
        }

        private void AosStartTimer_Click(object sender, RoutedEventArgs e)
        {
            SendAosCmd("startTimer");
        }

        private void AosStopTimer_Click(object sender, RoutedEventArgs e)
        {
            SendAosCmd("stopTimer");
        }

        private void ForceRegister_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(
                this,
                ClientUiStrings.T("ForceRegisterConfirm"),
                ClientUiStrings.T("ForceRegister"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirm != MessageBoxResult.Yes)
                return;
            SendAosCmd("forceRegister");
        }

        private void ApplyRegisterSync(string text)
        {
            _registerHistory.Clear();
            var rows = NotifyMessage.ParseRegisterSync(text);
            foreach (var row in rows)
            {
                if (row == null) continue;
                var local = RegisterHistoryRow.FromWire(row);
                if (local == null) continue;
                local.ResultText = RegisterResultTextForKind(local.Kind);
                _registerHistory.Add(local);
            }
            while (_registerHistory.Count > RegisterUnfilteredDisplayMax)
                _registerHistory.RemoveAt(0);
            RefreshRegisterAges();
            if (_registerHistoryMode)
                RequestRegisterHistory();
            else
                ApplyRegisterFilter();
            if (TabRegister != null && TabRegister.IsSelected)
                ScrollRegisterHistoryToEnd();
        }

        private static string RegisterResultTextForKind(int kind)
        {
            if (kind == 1) return ClientUiStrings.T("RegisterResultOk");
            if (kind == 2) return ClientUiStrings.T("RegisterResultInfo");
            return ClientUiStrings.T("RegisterResultFail");
        }

        private void ApplyRegisterTabStrings()
        {
            if (RegisterHistoryLabel != null) RegisterHistoryLabel.Text = ClientUiStrings.T("RegisterHistory");
            if (RegisterFilterLabel != null) RegisterFilterLabel.Text = ClientUiStrings.T("RegisterFilter");
            if (RegisterSearchLabel != null) RegisterSearchLabel.Text = ClientUiStrings.T("RegisterSearch");
            if (RegisterSearchBox != null) RegisterSearchBox.ToolTip = ClientUiStrings.T("RegisterSearchTip");
            if (ClearRegisterLogBtn != null) ClearRegisterLogBtn.Content = ClientUiStrings.T("ClearLog");
            if (SaveRegisterLogBtn != null) SaveRegisterLogBtn.Content = ClientUiStrings.T("SaveLog");
            if (RegisterColTime != null) RegisterColTime.Header = ClientUiStrings.T("RegisterColTime");
            if (RegisterColResult != null) RegisterColResult.Header = ClientUiStrings.T("RegisterColResult");
            if (RegisterColAge != null) RegisterColAge.Header = ClientUiStrings.T("RegisterColAge");
            if (RegisterColDetail != null) RegisterColDetail.Header = ClientUiStrings.T("RegisterColDetail");
            InitRegisterFilterCombo();
            for (var i = 0; i < _registerHistory.Count; i++)
            {
                var r = _registerHistory[i];
                if (r == null) continue;
                r.ResultText = RegisterResultTextForKind(r.Kind);
            }
            // InitRegisterFilterCombo applies filter mode.
        }

        private void InitRegisterFilterCombo()
        {
            if (RegisterFilterCombo == null) return;
            var prev = _registerFilter;
            _registerFilterReady = false;
            RegisterFilterCombo.DisplayMemberPath = "Name";
            RegisterFilterCombo.SelectedValuePath = "Code";
            RegisterFilterCombo.ItemsSource = new List<LangItem>
            {
                new LangItem { Code = "all", Name = ClientUiStrings.T("RegisterFilterAll") },
                new LangItem { Code = "ok", Name = ClientUiStrings.T("RegisterFilterOk") },
                new LangItem { Code = "fail", Name = ClientUiStrings.T("RegisterFilterFail") },
                new LangItem { Code = "info", Name = ClientUiStrings.T("RegisterFilterInfo") }
            };
            RegisterFilterCombo.SelectedValue = string.IsNullOrEmpty(prev) ? "all" : prev;
            if (RegisterFilterCombo.SelectedValue == null)
                RegisterFilterCombo.SelectedValue = "all";
            _registerFilter = (RegisterFilterCombo.SelectedValue as string) ?? "all";
            _registerFilterReady = true;
            ApplyRegisterFilterMode();
        }

        private void RegisterFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_registerFilterReady || RegisterFilterCombo == null) return;
            var code = RegisterFilterCombo.SelectedValue as string;
            if (string.IsNullOrEmpty(code)) code = "all";
            _registerFilter = code;
            ApplyRegisterFilterMode();
        }

        private void RefreshRegisterAges()
        {
            var now = DateTime.Now;
            for (var i = 0; i < _registerHistory.Count; i++)
            {
                var r = _registerHistory[i];
                if (r == null) continue;
                var span = now - r.At;
                if (span < TimeSpan.Zero) span = TimeSpan.Zero;
                r.AgeText = string.Format(ClientUiStrings.T("TimeAgo"), FormatAge(span));
            }
            ApplyRegisterFilter();
        }

        private bool WantRegisterHistoryMode()
        {
            return string.Equals(_registerFilter, "ok", StringComparison.OrdinalIgnoreCase)
                || string.Equals(_registerFilter, "fail", StringComparison.OrdinalIgnoreCase)
                || string.Equals(_registerFilter, "info", StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyRegisterFilterMode()
        {
            if (!WantRegisterHistoryMode())
            {
                _registerHistoryPendingId = 0;
                _registerHistoryMode = false;
                SetRegisterLoading(false);
                _registerHistoryViewRows.Clear();
                ApplyRegisterFilter();
                return;
            }
            _registerHistoryMode = true;
            _registerHistoryViewRows.Clear();
            RequestRegisterHistory();
        }

        private void RequestRegisterHistory()
        {
            if (!_registerHistoryMode) return;
            _registerHistoryViewRows.Clear();
            if (_client != null && _client.IsConnected)
            {
                _registerHistoryReqId++;
                _registerHistoryPendingId = _registerHistoryReqId;
                SetRegisterLoading(true);
                BindRegisterHistoryView();
                try
                {
                    _client.Send(new NotifyMessage
                    {
                        Kind = "registerHistoryReq",
                        Level = "info",
                        Title = _registerHistoryPendingId.ToString(CultureInfo.InvariantCulture),
                        Text = _registerFilter,
                        Detail = _registerSearchText ?? ""
                    });
                }
                catch
                {
                    SetRegisterLoading(false);
                }
            }
            else
            {
                SetRegisterLoading(false);
                BuildRegisterHistoryFromLocal();
                BindRegisterHistoryView();
            }
        }

        private void BuildRegisterHistoryFromLocal()
        {
            _registerHistoryViewRows.Clear();
            int want = -1;
            if (string.Equals(_registerFilter, "ok", StringComparison.OrdinalIgnoreCase)) want = 1;
            else if (string.Equals(_registerFilter, "fail", StringComparison.OrdinalIgnoreCase)) want = 0;
            else if (string.Equals(_registerFilter, "info", StringComparison.OrdinalIgnoreCase)) want = 2;
            for (var i = 0; i < _registerHistory.Count; i++)
            {
                var r = _registerHistory[i];
                if (r == null || r.Kind != want) continue;
                if (!MatchesLogSearch(r.Text, _registerSearchText) && !MatchesLogSearch(r.ResultText, _registerSearchText))
                    continue;
                _registerHistoryViewRows.Add(r);
            }
        }

        private void BindRegisterHistoryView()
        {
            if (RegisterHistoryList == null) return;
            RegisterHistoryList.ItemsSource = null;
            RegisterHistoryList.ItemsSource = new List<RegisterHistoryRow>(_registerHistoryViewRows);
            if (_registerHistoryLoading)
                UpdateNoMatchesHint(RegisterNoMatchesHint, false, 1);
            else
                UpdateNoMatchesHint(RegisterNoMatchesHint, _registerHistory.Count > 0 || _registerHistoryViewRows.Count > 0, _registerHistoryViewRows.Count);
            UpdateClearButtonsEnabled();
        }

        private void ApplyRegisterHistory(NotifyMessage msg)
        {
            if (msg == null || !_registerHistoryMode) return;
            var reqIdText = (msg.Title ?? "").Trim();
            int reqId;
            if (!int.TryParse(reqIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out reqId))
                reqId = 0;
            if (reqId != 0 && reqId != _registerHistoryPendingId) return;
            var filter = (msg.Level ?? "").Trim().ToLowerInvariant();
            if (!string.Equals(_registerFilter, filter, StringComparison.OrdinalIgnoreCase)) return;
            SetRegisterLoading(false);
            _registerHistoryViewRows.Clear();
            var rows = NotifyMessage.ParseRegisterSync(msg.Text);
            foreach (var row in rows)
            {
                if (row == null) continue;
                var local = RegisterHistoryRow.FromWire(row);
                if (local == null) continue;
                local.ResultText = RegisterResultTextForKind(local.Kind);
                var span = DateTime.Now - local.At;
                if (span < TimeSpan.Zero) span = TimeSpan.Zero;
                local.AgeText = string.Format(ClientUiStrings.T("TimeAgo"), FormatAge(span));
                _registerHistoryViewRows.Add(local);
            }
            BindRegisterHistoryView();
            if (TabRegister != null && TabRegister.IsSelected)
                ScrollRegisterHistoryToEnd();
        }

        private void ApplyRegisterFilter()
        {
            if (RegisterHistoryList == null) return;
            if (_registerHistoryMode)
            {
                BindRegisterHistoryView();
                return;
            }
            List<RegisterHistoryRow> list;
            if (string.Equals(_registerFilter, "ok", StringComparison.OrdinalIgnoreCase))
            {
                list = new List<RegisterHistoryRow>();
                for (var i = 0; i < _registerHistory.Count; i++)
                {
                    var r = _registerHistory[i];
                    if (r != null && r.Kind == 1) list.Add(r);
                }
            }
            else if (string.Equals(_registerFilter, "fail", StringComparison.OrdinalIgnoreCase))
            {
                list = new List<RegisterHistoryRow>();
                for (var i = 0; i < _registerHistory.Count; i++)
                {
                    var r = _registerHistory[i];
                    if (r != null && r.Kind == 0) list.Add(r);
                }
            }
            else if (string.Equals(_registerFilter, "info", StringComparison.OrdinalIgnoreCase))
            {
                list = new List<RegisterHistoryRow>();
                for (var i = 0; i < _registerHistory.Count; i++)
                {
                    var r = _registerHistory[i];
                    if (r != null && r.Kind == 2) list.Add(r);
                }
            }
            else
            {
                list = new List<RegisterHistoryRow>();
                var start = Math.Max(0, _registerHistory.Count - RegisterUnfilteredDisplayMax);
                for (var i = start; i < _registerHistory.Count; i++)
                {
                    var r = _registerHistory[i];
                    if (r != null) list.Add(r);
                }
            }
            if (!string.IsNullOrEmpty(_registerSearchText))
            {
                var needle = _registerSearchText;
                var filtered = new List<RegisterHistoryRow>();
                for (var i = 0; i < list.Count; i++)
                {
                    var r = list[i];
                    if (r == null) continue;
                    if (MatchesLogSearch(r.Text, needle) || MatchesLogSearch(r.ResultText, needle))
                        filtered.Add(r);
                }
                list = filtered;
            }
            RegisterHistoryList.ItemsSource = null;
            RegisterHistoryList.ItemsSource = list;
            UpdateNoMatchesHint(RegisterNoMatchesHint, _registerHistory.Count > 0, list.Count);
            UpdateClearButtonsEnabled();
        }

        private void AppendRegisterFromNotify(NotifyMessage msg)
        {
            if (msg == null) return;
            DateTime at;
            if (string.IsNullOrEmpty(msg.At) || !DateTime.TryParse(msg.At, out at))
                at = DateTime.Now;
            else
                at = DateTime.SpecifyKind(at, DateTimeKind.Local);
            var kind = 0;
            var lvl = (msg.Level ?? "").Trim();
            int parsedKind;
            if (int.TryParse(lvl, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedKind)
                && parsedKind >= 0 && parsedKind <= 2)
                kind = parsedKind;
            else if (string.Equals(lvl, "ok", StringComparison.OrdinalIgnoreCase)
                || string.Equals(lvl, "success", StringComparison.OrdinalIgnoreCase))
                kind = 1;
            else if (string.Equals(lvl, "info", StringComparison.OrdinalIgnoreCase))
                kind = 2;
            var text = msg.Text ?? "";
            var span = DateTime.Now - at;
            if (span < TimeSpan.Zero) span = TimeSpan.Zero;
            var row = new RegisterHistoryRow
            {
                At = at,
                Kind = kind,
                Ok = kind == 1,
                Text = text,
                AgeText = string.Format(ClientUiStrings.T("TimeAgo"), FormatAge(span)),
                ResultText = RegisterResultTextForKind(kind)
            };
            _registerHistory.Add(row);
            while (_registerHistory.Count > RegisterUnfilteredDisplayMax)
                _registerHistory.RemoveAt(0);
            if (_registerHistoryMode)
            {
                int want = -1;
                if (string.Equals(_registerFilter, "ok", StringComparison.OrdinalIgnoreCase)) want = 1;
                else if (string.Equals(_registerFilter, "fail", StringComparison.OrdinalIgnoreCase)) want = 0;
                else if (string.Equals(_registerFilter, "info", StringComparison.OrdinalIgnoreCase)) want = 2;
                if (kind == want
                    && (MatchesLogSearch(row.Text, _registerSearchText) || MatchesLogSearch(row.ResultText, _registerSearchText)))
                {
                    _registerHistoryViewRows.Add(row);
                    BindRegisterHistoryView();
                }
            }
            else
            {
                ApplyRegisterFilter();
            }
            if (TabRegister != null && TabRegister.IsSelected)
                ScrollRegisterHistoryToEnd();
            UpdateClearButtonsEnabled();
        }

        private void ScrollRegisterHistoryToEnd()
        {
            if (RegisterHistoryList == null || RegisterHistoryList.Items.Count == 0) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (RegisterHistoryList == null || RegisterHistoryList.Items.Count == 0) return;
                    RegisterHistoryList.UpdateLayout();
                    var last = RegisterHistoryList.Items[RegisterHistoryList.Items.Count - 1];
                    RegisterHistoryList.ScrollIntoView(last);
                }
                catch { }
            }), DispatcherPriority.Loaded);
        }

        private void RegisterHistoryList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (IsGridViewHeaderClick(e.OriginalSource as DependencyObject)) return;
            CopySelectedRegisterText();
        }

        private bool CopySelectedRegisterText()
        {
            var entry = RegisterHistoryList != null ? RegisterHistoryList.SelectedItem as RegisterHistoryRow : null;
            if (entry == null || string.IsNullOrEmpty(entry.Text)) return false;
            TryCopyTextToClipboard(entry.Text, "Copied to clipboard.");
            return true;
        }

        private void SaveRegisterLog_Click(object sender, RoutedEventArgs e)
        {
            BeginLogExport("register", ClientUiStrings.T("SaveLogTitleRegister"), CollectRegisterLogForDay);
        }

        private void ClearRegisterLog_Click(object sender, RoutedEventArgs e)
        {
            if (_client != null && _client.IsConnected)
            {
                try
                {
                    _client.Send(new NotifyMessage
                    {
                        Kind = "registerClearReq",
                        Level = "info",
                        Title = "KAI - AOS Renew - Client",
                        Text = ""
                    });
                }
                catch { }
                return;
            }
            _registerHistory.Clear();
            ApplyRegisterFilter();
            UpdateClearButtonsEnabled();
        }

        private string CollectRegisterLogForDay(DateTime day)
        {
            var sb = new StringBuilder();
            var dayStart = day.Date;
            var dayEnd = dayStart.AddDays(1);
            for (var i = 0; i < _registerHistory.Count; i++)
            {
                var r = _registerHistory[i];
                if (r == null) continue;
                if (r.At < dayStart || r.At >= dayEnd) continue;
                sb.Append(r.At.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.Append('\t').Append(RegisterResultTextForKind(r.Kind));
                sb.Append('\t').AppendLine(r.Text ?? "");
            }
            return sb.ToString();
        }

        private void SyncAlertBlinkTimer()
        {
            var any = _keysAlert || _renewAlert || _kaiServiceAlert || _activityAlert;
            if (any)
            {
                if (!_alertBlinkTimer.IsEnabled)
                {
                    _alertBlinkOn = true;
                    ApplyAlertBlinkVisuals();
                    _alertBlinkTimer.Start();
                }
            }
            else
            {
                _alertBlinkTimer.Stop();
                _alertBlinkOn = true;
                ApplyAlertBlinkVisuals();
            }
        }

        private void AlertBlinkTimer_Tick(object sender, EventArgs e)
        {
            _alertBlinkOn = !_alertBlinkOn;
            ApplyAlertBlinkVisuals();
        }

        private void SetRenewAlert(bool on)
        {
            _renewAlert = on;
            if (RenewTabAlertDot != null)
                RenewTabAlertDot.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            SyncAlertBlinkTimer();
        }

        private void RenewTabAlertDot_Click(object sender, MouseButtonEventArgs e)
        {
            SelectTabFromAlertDot(TabAos, e);
        }

        private void KaiBusyTimer_Tick(object sender, EventArgs e)
        {
            SetKaiBusy(false);
        }

        private void StatusTimer_Tick(object sender, EventArgs e)
        {
            RefreshTrayStatus();
            RefreshReconnectStatusUi();
            MaybeAlertOffline();
            UpdateKeysAlert(); // time-based stall detection
            if (TabRegister != null && TabRegister.IsSelected)
                RefreshRegisterAges();
        }

        private void RefreshAuthGuardsPanel(bool cooldownOn, string cooldownFooterText, bool blockedOn, string blockedFooterText, string tip)
        {
            var blockedChanged = _authBlockedOn != blockedOn;
            _authCooldownOn = cooldownOn;
            _authBlockedOn = blockedOn;

            if (AuthCooldownStatusLine != null)
            {
                if (cooldownOn)
                {
                    AuthCooldownStatusLine.Text = !string.IsNullOrEmpty(cooldownFooterText)
                        ? cooldownFooterText
                        : ClientUiStrings.T("AuthCooldownStatusActiveShort");
                    AuthCooldownStatusLine.Foreground = CreateBrush(0xB2, 0x6A, 0x00);
                }
                else
                {
                    AuthCooldownStatusLine.Text = ClientUiStrings.T("AuthCooldownStatusInactive");
                    AuthCooldownStatusLine.Foreground = CreateBrush(0x2E, 0x7D, 0x32);
                }
            }
            if (AuthRegisterBlockedStatusLine != null)
            {
                if (blockedOn)
                {
                    AuthRegisterBlockedStatusLine.Text = !string.IsNullOrEmpty(blockedFooterText)
                        ? blockedFooterText
                        : ClientUiStrings.T("AuthRegisterBlockedStatusActive");
                    AuthRegisterBlockedStatusLine.Foreground = CreateBrush(0xC6, 0x28, 0x28);
                }
                else
                {
                    AuthRegisterBlockedStatusLine.Text = ClientUiStrings.T("AuthRegisterBlockedStatusInactive");
                    AuthRegisterBlockedStatusLine.Foreground = CreateBrush(0x2E, 0x7D, 0x32);
                }
            }
            if (AuthGuardsStatusPanel != null)
            {
                AuthGuardsStatusPanel.ToolTip = string.IsNullOrEmpty(tip)
                    ? (ClientUiStrings.T("AuthCooldownExplain") + "\n\n" + ClientUiStrings.T("AuthRegisterBlockedExplain"))
                    : tip;
            }
            if (ClearAuthCooldownBtn != null)
            {
                ClearAuthCooldownBtn.Visibility = cooldownOn ? Visibility.Visible : Visibility.Collapsed;
                ClearAuthCooldownBtn.IsEnabled = cooldownOn && _client != null && _client.IsConnected
                    && !_aosAwaitingAck && !_clientUpdateBusy;
            }
            if (ClearAuthRegisterBlockedBtn != null)
            {
                ClearAuthRegisterBlockedBtn.Visibility = blockedOn ? Visibility.Visible : Visibility.Collapsed;
                ClearAuthRegisterBlockedBtn.IsEnabled = blockedOn && _client != null && _client.IsConnected
                    && !_aosAwaitingAck && !_clientUpdateBusy;
            }
            SyncAuthGuardsBlinkTimer();
            if (blockedChanged)
                UpdateAosLoginStatus(_lastTokenMode);
        }

        private void ClearAuthCooldown_Click(object sender, RoutedEventArgs e)
        {
            if (_client == null || !_client.IsConnected)
            {
                MessageBox.Show(this, ClientUiStrings.T("AuthGuardClearOffline"),
                    ClientUiStrings.T("AppTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            SendAosCmd("authClearCooldown");
        }

        private void ClearAuthRegisterBlocked_Click(object sender, RoutedEventArgs e)
        {
            if (_client == null || !_client.IsConnected)
            {
                MessageBox.Show(this, ClientUiStrings.T("AuthGuardClearOffline"),
                    ClientUiStrings.T("AppTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            SendAosCmd("authClearBlocked");
        }

        private void SyncAuthGuardsBlinkTimer()
        {
            var any = _authCooldownOn || _authBlockedOn;
            if (any)
            {
                if (!_authGuardsBlinkTimer.IsEnabled)
                {
                    _authGuardsBlinkOn = true;
                    ApplyAuthGuardsBlinkVisuals();
                    _authGuardsBlinkTimer.Start();
                }
            }
            else
            {
                _authGuardsBlinkTimer.Stop();
                _authGuardsBlinkOn = true;
                ApplyAuthGuardsBlinkVisuals();
            }
        }

        private void AuthGuardsBlinkTimer_Tick(object sender, EventArgs e)
        {
            _authGuardsBlinkOn = !_authGuardsBlinkOn;
            ApplyAuthGuardsBlinkVisuals();
        }

        private void ApplyAuthGuardsBlinkVisuals()
        {
            var dim = _authGuardsBlinkOn ? 1.0 : 0.2;
            if (AuthGuardsFooterText != null)
                AuthGuardsFooterText.Opacity = (_authCooldownOn || _authBlockedOn) ? dim : 1.0;
            if (AuthCooldownStatusLine != null)
                AuthCooldownStatusLine.Opacity = _authCooldownOn ? dim : 1.0;
            if (AuthRegisterBlockedStatusLine != null)
                AuthRegisterBlockedStatusLine.Opacity = _authBlockedOn ? dim : 1.0;
        }

        private void SyncServicesBlinkTimer()
        {
            if (_servicesFail)
            {
                if (!_servicesBlinkTimer.IsEnabled)
                {
                    _servicesBlinkOn = true;
                    ApplyServicesBlinkVisuals();
                    _servicesBlinkTimer.Start();
                }
            }
            else
            {
                _servicesBlinkTimer.Stop();
                _servicesBlinkOn = true;
                ApplyServicesBlinkVisuals();
            }
        }

        private void ServicesBlinkTimer_Tick(object sender, EventArgs e)
        {
            _servicesBlinkOn = !_servicesBlinkOn;
            ApplyServicesBlinkVisuals();
        }

        private void StartRegisterAgeBlink()
        {
            _registerAgeBlinkTicksLeft = 6;
            _registerAgeBlinkOn = false;
            ApplyRegisterAgeBlinkVisuals();
            if (!_registerAgeBlinkTimer.IsEnabled)
                _registerAgeBlinkTimer.Start();
        }

        private void RegisterAgeBlinkTimer_Tick(object sender, EventArgs e)
        {
            _registerAgeBlinkOn = !_registerAgeBlinkOn;
            ApplyRegisterAgeBlinkVisuals();
            _registerAgeBlinkTicksLeft--;
            if (_registerAgeBlinkTicksLeft > 0) return;
            _registerAgeBlinkTimer.Stop();
            _registerAgeBlinkOn = true;
            ApplyRegisterAgeBlinkVisuals();
        }

        private void ApplyRegisterAgeBlinkVisuals()
        {
            if (RegisterAgeText == null) return;
            RegisterAgeText.Opacity = _registerAgeBlinkOn ? 1.0 : 0.2;
        }

        private void MaybeBlinkRegisterAge(string key)
        {
            key = key ?? "";
            if (_registerAgeBlinkKey == null)
            {
                _registerAgeBlinkKey = key;
                return;
            }
            if (string.Equals(_registerAgeBlinkKey, key, StringComparison.Ordinal))
                return;
            _registerAgeBlinkKey = key;
            StartRegisterAgeBlink();
            if (_debugMode)
                ShowDebugRegisterStickyBalloon();
        }

        private void ShowDebugRegisterStickyBalloon()
        {
            var body = "";
            var showFail = false;
            if (RegisterAgeText != null)
            {
                body = (RegisterAgeText.Text ?? "").Trim();
                var i = 0;
                foreach (var inline in RegisterAgeText.Inlines)
                {
                    if (inline is Run && i > 0)
                    {
                        showFail = true;
                        break;
                    }
                    i++;
                }
            }
            if (string.IsNullOrEmpty(body))
                body = "Register";
            var title = TrimLabelColon(ClientUiStrings.T(showFail ? "RegisterFailAge" : "RegisterOkAge"));
            if (string.IsNullOrEmpty(title) || title.StartsWith("RegisterFail", StringComparison.Ordinal)
                || title.StartsWith("RegisterOk", StringComparison.Ordinal))
                title = showFail ? "Register fail" : "Register OK";
            var level = showFail ? "warning" : "info";
            AppendActivityRow(null, level, title + " — " + DateTime.Now.ToString("g"));
            ToastWindow.ShowSticky(title, body, level);
        }

        private void MaybeDebugTokenSticky(string mode, string body)
        {
            mode = string.IsNullOrEmpty(mode) ? "none" : mode;
            if (string.Equals(mode, "error", StringComparison.OrdinalIgnoreCase))
                mode = "fail";
            if (_debugTokenModeKey == null)
            {
                _debugTokenModeKey = mode;
                return;
            }
            if (string.Equals(_debugTokenModeKey, mode, StringComparison.Ordinal))
                return;
            _debugTokenModeKey = mode;
            if (!_debugMode) return;
            if (mode != "ok" && mode != "fail") return;
            var fail = mode == "fail";
            var title = fail ? "Token: FAIL" : "Token: OK";
            var tip = string.IsNullOrWhiteSpace(body) ? title : body.Trim();
            var level = fail ? "warning" : "info";
            AppendActivityRow(null, level, title + " — " + DateTime.Now.ToString("g"));
            ToastWindow.ShowSticky(title, tip, level);
        }

        private void RegisterHistoryList_SizeChanged(object sender, EventArgs e)
        {
            ResizeRegisterDetailColumn();
        }

        private void ResizeRegisterDetailColumn()
        {
            if (RegisterHistoryList == null || RegisterColDetail == null) return;
            var width = RegisterHistoryList.ActualWidth;
            if (width <= 0) return;
            const double timeW = 140;
            const double resultW = 70;
            const double ageW = 110;
            const double pad = 36;
            var detailW = width - timeW - resultW - ageW - pad;
            if (detailW < 120) detailW = 120;
            RegisterColDetail.Width = detailW;
        }

    }
}
