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

        private void ApplyRegisterTabStrings()
        {
            if (RegisterHistoryLabel != null) RegisterHistoryLabel.Text = UiStrings.T("RegisterHistory");
            if (RegisterFilterLabel != null) RegisterFilterLabel.Text = UiStrings.T("RegisterFilter");
            if (RegisterSearchLabel != null) RegisterSearchLabel.Text = UiStrings.T("RegisterSearch");
            if (RegisterSearchBox != null) RegisterSearchBox.ToolTip = UiStrings.T("RegisterSearchTip");
            if (RegisterRefreshBtn != null) RegisterRefreshBtn.Content = UiStrings.T("KeysRefresh");
            if (RegisterColTime != null) RegisterColTime.Header = UiStrings.T("RegisterColTime");
            if (RegisterColResult != null) RegisterColResult.Header = UiStrings.T("RegisterColResult");
            if (RegisterColAge != null) RegisterColAge.Header = UiStrings.T("RegisterColAge");
            if (RegisterColDetail != null) RegisterColDetail.Header = UiStrings.T("RegisterColDetail");
            InitRegisterFilterCombo();
            RefreshRegisterResultTexts();
            ApplyRegisterFilter();
        }

        private void RegisterRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadRegisterHistoryFromLog();
            BroadcastRegisterSync();
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
                new LangItem { Code = "all", Name = UiStrings.T("RegisterFilterAll") },
                new LangItem { Code = "ok", Name = UiStrings.T("RegisterFilterOk") },
                new LangItem { Code = "fail", Name = UiStrings.T("RegisterFilterFail") },
                new LangItem { Code = "info", Name = UiStrings.T("RegisterFilterInfo") }
            };
            RegisterFilterCombo.SelectedValue = string.IsNullOrEmpty(prev) ? "all" : prev;
            if (RegisterFilterCombo.SelectedValue == null)
                RegisterFilterCombo.SelectedValue = "all";
            _registerFilter = (RegisterFilterCombo.SelectedValue as string) ?? "all";
            _registerFilterReady = true;
        }

        private void RegisterFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_registerFilterReady || RegisterFilterCombo == null) return;
            var code = RegisterFilterCombo.SelectedValue as string;
            if (string.IsNullOrEmpty(code)) code = "all";
            _registerFilter = code;
            ApplyRegisterFilter();
        }

        private void AuthCooldownMinutes_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_loadingUi) return;
            ParseAndSetAuthCooldownMinutes();
            try { if (_settings != null) _settings.Save(); }
            catch { }
            BroadcastServerSettings();
        }

        private void ParseAndSetAuthCooldownMinutes()
        {
            if (_settings == null) return;
            var min = 60;
            if (AuthCooldownMinutesBox != null)
            {
                int parsed;
                if (!int.TryParse(AuthCooldownMinutesBox.Text.Trim(), out parsed) || parsed < 5)
                    parsed = 60;
                if (parsed > 240) parsed = 240;
                min = parsed;
                AuthCooldownMinutesBox.Text = min.ToString();
            }
            _settings.AuthCooldownMinutes = min;
        }

        private void FocusPasswordField()
        {
            try
            {
                if (_aboutVisible)
                    HideAboutPanel();
                if (TabRenew != null)
                    TabRenew.IsSelected = true;
                ApplyTabLayoutVisibility();
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (PasswordPlainBox != null
                        && PasswordPlainBox.Visibility == Visibility.Visible)
                    {
                        PasswordPlainBox.Focus();
                        PasswordPlainBox.SelectAll();
                    }
                    else if (PasswordBox != null)
                        PasswordBox.Focus();
                }), DispatcherPriority.Input);
            }
            catch { }
        }

        private void RunAosCmdFromClient(int clientId, string cmd)
        {
            var rawCmd = (cmd ?? "").Trim();
            var c = rawCmd.ToLowerInvariant();
            try
            {
                if (c.StartsWith("getkeybin|", StringComparison.Ordinal))
                {
                    var vin = rawCmd.IndexOf('|') >= 0
                        ? rawCmd.Substring(rawCmd.IndexOf('|') + 1).Trim()
                        : "";
                    SendKeyBinToClient(clientId, vin);
                    return;
                }
                if (c.StartsWith("getzip|", StringComparison.Ordinal))
                {
                    var kind = rawCmd.IndexOf('|') >= 0
                        ? rawCmd.Substring(rawCmd.IndexOf('|') + 1).Trim()
                        : "";
                    SendFolderZipToClient(clientId, kind);
                    return;
                }
                if (c == "checktoken")
                {
                    Forget(RunHealthOnlyAsync(), "RunHealthOnly");
                    SendAosAck(clientId, true, "checkToken");
                    return;
                }
                if (c == "starttimer")
                {
                    StartTimer(true);
                    SendAosAck(clientId, true, "startTimer");
                    return;
                }
                if (c == "stoptimer")
                {
                    StopTimer();
                    SendAosAck(clientId, true, "stopTimer");
                    return;
                }
                if (c == "checkbrowserpass")
                {
                    CheckBrowserPass_Click(null, null);
                    SendAosAck(clientId, true, "checkBrowserPass");
                    return;
                }
                if (c == "authclearcooldown")
                {
                    if (IsAuthCooldownActive())
                    {
                        ClearAuthCooldown();
                        AppendLog(UiStrings.T("AuthCooldownCleared"), LogLevel.Info);
                    }
                    SendAosAck(clientId, true, "authClearCooldown");
                    BroadcastAosSync();
                    return;
                }
                if (c == "authclearblocked")
                {
                    if (IsAuthRegisterBlocked())
                    {
                        SetAuthRegisterBlocked(false);
                        QueueFooterPublish();
                        BroadcastServerSettings();
                        AppendLog(UiStrings.T("AuthRegisterBlockedCleared"), LogLevel.Info);
                    }
                    SendAosAck(clientId, true, "authClearBlocked");
                    BroadcastAosSync();
                    return;
                }
                if (c == "forceregister")
                {
                    if (_busy)
                    {
                        SendAosAck(clientId, false, UiStrings.T("AosBusyRetry"));
                        return;
                    }
                    Forget(ForceRegisterFromClientAsync(clientId), "ForceRegisterRemote");
                    return;
                }
                if (c == "refreshkeys")
                {
                    RefreshKeysTab(true);
                    var rows = GetRecentKeyRows(NotifyMessage.MaxKeySyncRows) ?? new List<KeyActivityEvent>();
                    // Skip empty keySync after Clear Keys — do not wipe the requesting client.
                    if (rows.Count > 0)
                    {
                        var payload = NotifyKeySync.EncodeKeySync(rows) ?? "";
                        _lastKeySyncSignature = KeySyncSignature(rows);
                        NotifyServer.Instance.SendTo(clientId, new NotifyMessage
                        {
                            Kind = "keySync",
                            Level = "info",
                            Title = "KAI - AOS Renew",
                            Text = payload
                        });
                    }
                    SendAosAck(clientId, true, "refreshKeys");
                    return;
                }
                if (c == "fixservices")
                {
                    Forget(RunKaiServiceCmdFromClient(clientId, true), "FixServicesRemote");
                    return;
                }
                if (c == "stopservices" || c == "restartservices")
                {
                    Forget(RunKaiServiceCmdFromClient(clientId, false), "RestartServicesRemote");
                    return;
                }
                if (c == "refreshkai")
                {
                    RefreshKaiDiagnostics(true);
                    SendKaiSyncTo(clientId);
                    SendAosAck(clientId, true, "refreshKai");
                    return;
                }
                if (c == "retrybmw")
                {
                    KeysRetryBmw_Click(null, null);
                    SendAosAck(clientId, true, "retryBmw");
                    return;
                }
                if (c == "firebasepushnow")
                {
                    Forget(BeginPeerSyncPollAsync(true), "FirebasePushNow");
                    SendAosAck(clientId, true, "firebasePushNow");
                    return;
                }
                if (c == "firebasepushtest")
                {
                    Forget(RunFirebasePushTestAsync(clientId), "FirebasePushTest");
                    return;
                }
                if (c == "firebasepushpush")
                {
                    Forget(PushPeerCredentialsNowAsync(clientId), "FirebasePushPush");
                    return;
                }
                if (c == "firebasepushupdateall")
                {
                    SendAosAck(clientId, false, "Firebase update-all removed");
                    return;
                }
                SendAosAck(clientId, false, "Unknown command");
            }
            catch (Exception ex)
            {
                SendAosAck(clientId, false, ex.Message);
            }
        }

        private async Task ForceRegisterFromClientAsync(int clientId)
        {
            try
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
                ClearAuthCooldown();
                SetAuthRegisterBlocked(false);
                HideClipboardPassBanner();
                SaveFromUi(true);
                string fail;
                if (!TrySavePreflight(out fail, false))
                {
                    SendAosAck(clientId, false, fail ?? "Preflight failed");
                    return;
                }
                AppendLog(UiStrings.T("ForceRegisterStarted"), LogLevel.Info);
                BroadcastAosSync();
                var result = await RunNowAsync(false, true, null, true).ConfigureAwait(true);
                var loginOk = result != null && result.Ok && !result.RegisterAuthRejected;
                if (loginOk)
                    SendAosAck(clientId, true, "forceRegister");
                else
                    SendAosAck(clientId, false, result != null && !string.IsNullOrEmpty(result.Message) ? result.Message : "failed");
                BroadcastAosSync();
            }
            catch (Exception ex)
            {
                SendAosAck(clientId, false, ex.Message);
            }
        }

        private void RenewTabAlertDot_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            e.Handled = true;
            JumpToAlertTab(TabRenew);
        }

        private void ShowPassword_Changed(object sender, RoutedEventArgs e)
        {
            if (ShowPasswordCheck.IsChecked == true)
            {
                PasswordPlainBox.Text = PasswordBox.Password;
                PasswordBox.Visibility = Visibility.Collapsed;
                PasswordPlainBox.Visibility = Visibility.Visible;
            }
            else
            {
                PasswordBox.Password = PasswordPlainBox.Text;
                PasswordPlainBox.Visibility = Visibility.Collapsed;
                PasswordBox.Visibility = Visibility.Visible;
            }
        }

        private string GetPasswordUi()
        {
            // Always prefer the visible control; keep the other in sync for later toggles.
            if (ShowPasswordCheck != null && ShowPasswordCheck.IsChecked == true)
            {
                var plain = PasswordPlainBox != null ? (PasswordPlainBox.Text ?? "") : "";
                if (PasswordBox != null && PasswordBox.Password != plain)
                    PasswordBox.Password = plain;
                return plain;
            }
            var box = PasswordBox != null ? (PasswordBox.Password ?? "") : "";
            if (PasswordPlainBox != null && PasswordPlainBox.Text != box)
                PasswordPlainBox.Text = box;
            return box;
        }

        private void SetPasswordUi(string password)
        {
            password = password ?? "";
            if (PasswordBox != null)
                PasswordBox.Password = password;
            if (PasswordPlainBox != null)
                PasswordPlainBox.Text = password;
        }

        private void SetRenewAlert(bool on)
        {
            _renewAlert = on;
            if (RenewTabAlertDot != null)
                RenewTabAlertDot.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            SyncAlertBlinkTimer();
            RefreshTrayIcon();
        }

        private void SyncAlertBlinkTimer()
        {
            var any = _kaiServiceAlert || _renewAlert || _keysAlert || _activityAlert;
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
                body = UiStrings.T("RegisterOkNone");
            var title = TrimLabelColon(UiStrings.T(showFail ? "RegisterFailAge" : "RegisterOkAge"));
            if (string.IsNullOrEmpty(title))
                title = showFail ? "Register fail" : "Register OK";
            AppendLog(title + " — " + DateTime.Now.ToString("g"),
                showFail ? LogLevel.Warning : LogLevel.Info);
            TrayTipWindow.ShowSticky(title, body,
                showFail ? TrayTipLevel.Warning : TrayTipLevel.Info);
        }

        private void InitTokenFooterFromSettings()
        {
            if (_settings == null)
            {
                SetTokenFooter(null);
                return;
            }
            if (_settings.ConsecutiveFailures > 0 || _hasFailureAlert)
                SetTokenFooter(false);
            else if (_settings.LastSuccessUtc.HasValue)
                SetTokenFooter(true);
            else
                SetTokenFooter(null);
        }

        private void SetTokenFooter(bool? ok)
        {
            _tokenFooterOk = ok;
            UpdateTokenFooterDisplay();
            SetRenewAlert(ok == false || _hasFailureAlert ||
                (_settings != null && _settings.ConsecutiveFailures > 0));
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
            TryCopyTextToClipboard(entry.Text, null);
            return true;
        }

        private void SaveRegisterLog_Click(object sender, RoutedEventArgs e)
        {
            ExportLogForDay("register", UiStrings.T("SaveLogTitleRegister"), CollectRegisterLogForDay);
        }

        private void ClearRegisterLog_Click(object sender, RoutedEventArgs e)
        {
            _registerHistory.Clear();
            RefreshRegisterAges();
            ApplyRegisterFilter();
            BroadcastRegisterSync();
            UpdateClearButtonsEnabled();
        }

        private string CollectRegisterLogForDay(DateTime day)
        {
            var sb = new StringBuilder();
            var dayStart = day.Date;
            var dayEnd = dayStart.AddDays(1);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                var paths = new[] { AppSettings.LogPath + ".1", AppSettings.LogPath };
                for (var p = 0; p < paths.Length; p++)
                {
                    if (!File.Exists(paths[p])) continue;
                    foreach (var line in ReadSharedLogLines(paths[p]))
                    {
                        if (line == null || line.Length < 21) continue;
                        DateTime local;
                        if (!DateTime.TryParseExact(line.Substring(0, 19), "yyyy-MM-dd HH:mm:ss",
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out local))
                            continue;
                        if (local < dayStart || local >= dayEnd) continue;
                        var text = line.Substring(19).TrimStart();
                        int kind;
                        if (!TryClassifyRegisterHistory(text, out kind)) continue;
                        var key = local.ToString("yyyy-MM-dd HH:mm:ss") + "\t" + kind + "\t" + text;
                        if (!seen.Add(key)) continue;
                        sb.Append(local.ToString("yyyy-MM-dd HH:mm:ss"));
                        sb.Append('\t').Append(RegisterResultTextForKind(kind));
                        sb.Append('\t').AppendLine(text);
                    }
                }
            }
            catch { }
            if (sb.Length > 0) return sb.ToString();
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

        private void AuthRegisterBlockAfter_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_loadingUi || _settings == null) return;
            ParseAndSetAuthRegisterBlockAfter();
            try { _settings.Save(); }
            catch { }
            BroadcastAosSync();
            BroadcastServerSettings();
        }

        private void ParseAndSetAuthRegisterBlockAfter()
        {
            if (_settings == null) return;
            var n = 3;
            if (AuthRegisterBlockAfterBox != null)
            {
                int parsed;
                if (!int.TryParse(AuthRegisterBlockAfterBox.Text.Trim(), out parsed) || parsed < 1)
                    parsed = 3;
                if (parsed > 10) parsed = 10;
                n = parsed;
                AuthRegisterBlockAfterBox.Text = n.ToString();
            }
            _settings.AuthRegisterBlockAfter = n;
        }

        private void ForceRegister_Click(object sender, RoutedEventArgs e)
        {
            if (!TryBeginSaveGesture())
                return;
            try
            {
                var confirm = MessageBox.Show(
                    this,
                    UiStrings.T("ForceRegisterConfirm"),
                    UiStrings.T("ForceRegister"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);
                if (confirm != MessageBoxResult.Yes)
                    return;

                if (!ValidateAosCredentials(true))
                    return;
                int hours;
                if (!ValidateInterval(out hours, true))
                    return;
                var path = ConfigPathBox != null ? ConfigPathBox.Text.Trim() : "";
                if (!ValidateConfigPath(path, true))
                    return;
                ClearAuthCooldown();
                SetAuthRegisterBlocked(false);
                HideClipboardPassBanner();
                SaveFromUi(true);
                if (!TrySavePreflight())
                    return;
                AppendLog(UiStrings.T("ForceRegisterStarted"), LogLevel.Info);
                Forget(RunNowAsync(false, true, null, true), "ForceRegister");
                BroadcastAosSync();
            }
            finally
            {
                EndSaveGesture();
            }
        }

        private string PeekPasswordUi()
        {
            if (ShowPasswordCheck != null && ShowPasswordCheck.IsChecked == true)
                return PasswordPlainBox != null ? (PasswordPlainBox.Text ?? "") : "";
            return PasswordBox != null ? (PasswordBox.Password ?? "") : "";
        }

        private void CaptureAosSavedCredentials()
        {
            _aosSavedEmail = EmailBox != null ? (EmailBox.Text ?? "").Trim() : "";
            _aosSavedPass = (GetPasswordUi() ?? "").Trim();
        }

        private void StartTimer_Click(object sender, RoutedEventArgs e)
        {
            StartTimer(true);
        }

        private void StopTimer_Click(object sender, RoutedEventArgs e)
        {
            StopTimer();
        }

        private void IntervalDebounceTimer_Tick(object sender, EventArgs e)
        {
            _intervalDebounceTimer.Stop();
            ApplyIntervalFromBox(false);
        }

        private void EnsureAutoTimer(bool announce)
        {
            if (_settings == null) return;
            if (_settings.TimerUserStopped) return;
            if (_settings.IntervalHours < 1) return;
            if (_timerActive) return;
            // Interval-only auto-start: do not SaveFromUi (would persist unsaved email/password).
            StartTimer(announce, true, false);
        }

        private void StartTimer(bool announce, bool allowMinimize = true, bool pullUi = true)
        {
            if (pullUi)
                SaveFromUi(false);
            if (!(_retrySchedule && _settings.ConsecutiveFailures > 0))
            {
                _retrySchedule = false;
                ScheduleNext(true);
            }
            _timerActive = true;
            if (_settings != null && _settings.TimerUserStopped)
            {
                _settings.TimerUserStopped = false;
                try { _settings.Save(); }
                catch { }
            }
            _nextHealthUtc = DateTime.UtcNow.AddHours(1);
            _timer.Start();
            RefreshStatusBadge();
            if (announce)
                AppendLog(string.Format(UiStrings.T("TimerStarted"), _settings.IntervalHours));
            UpdateNextLabel();
            UpdateTrayTooltip();
            // MinimizeToTray only applies at Windows startup (not after Save/timer).
        }

        private void StopTimer()
        {
            _timerActive = false;
            _retrySchedule = false;
            _nextDueUtc = DateTime.MaxValue;
            _nextHealthUtc = DateTime.MaxValue;
            CancelRenewInFlight();
            if (_settings != null)
            {
                _settings.TimerUserStopped = true;
                try { _settings.Save(); }
                catch { }
            }
            // Keep _timer running for UI refresh + BMW service checks
            if (!_timer.IsEnabled)
                _timer.Start();
            SetTimerStatus(false);
            NextRunText.Text = "";
            UpdateTrayTooltip();
            AppendLog(UiStrings.T("TimerStopped"));
        }

        private void CancelRenewInFlight()
        {
            try
            {
                var old = _renewCts;
                _renewCts = new CancellationTokenSource();
                if (old != null)
                {
                    try { old.Cancel(); }
                    catch { }
                    try { old.Dispose(); }
                    catch { }
                }
            }
            catch { }
        }

        private void SetTimerStatus(bool active)
        {
            if (!active)
            {
                StatusText.Text = UiStrings.T("Stopped");
                StatusText.Foreground = (Brush)FindResource("StatusStoppedBrush");
                StatusDot.Fill = (Brush)FindResource("StatusStoppedBrush");
                _footerStatusMode = "stopped";
                QueueFooterPublish();
                return;
            }
            RefreshStatusBadge();
        }

        private void ClearAosFieldInvalidHighlights()
        {
            ClearFieldInvalid(EmailBox);
            ClearFieldInvalid(PasswordBox);
            ClearFieldInvalid(PasswordPlainBox);
            ClearFieldInvalid(IntervalBox);
        }

        private void FocusAosPasswordUi()
        {
            if (ShowPasswordCheck != null && ShowPasswordCheck.IsChecked == true && PasswordPlainBox != null
                && PasswordPlainBox.Visibility == Visibility.Visible)
            {
                PasswordPlainBox.Focus();
                return;
            }
            if (PasswordBox != null) PasswordBox.Focus();
        }

        private bool ShowAosFieldError(string msgKey, Control focusField, bool showUi)
        {
            var msg = UiStrings.T(msgKey);
            ClearAosFieldInvalidHighlights();
            if (showUi)
            {
                MarkFieldInvalid(focusField);
                try { Activate(); } catch { }
                if (focusField != null) focusField.Focus();
                MessageBox.Show(this, msg, "KAI - AOS Renew", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            AppendLog(msg, LogLevel.Warning);
            return false;
        }

        private bool ValidateAosCredentials(bool showUi)
        {
            var email = EmailBox != null ? EmailBox.Text.Trim() : "";
            var password = (GetPasswordUi() ?? "").Trim();
            if (string.IsNullOrEmpty(email))
                return ShowAosFieldError("MissingEmail", EmailBox, showUi);
            if (email.IndexOf('@') < 0 || email.IndexOf(' ') >= 0)
                return ShowAosFieldError("InvalidUser", EmailBox, showUi);
            if (string.IsNullOrEmpty(password))
            {
                Control focus = PasswordBox;
                if (ShowPasswordCheck != null && ShowPasswordCheck.IsChecked == true
                    && PasswordPlainBox != null && PasswordPlainBox.Visibility == Visibility.Visible)
                    focus = PasswordPlainBox;
                return ShowAosFieldError("MissingPassword", focus, showUi);
            }
            ClearAosFieldInvalidHighlights();
            return true;
        }

        private void AosEmail_TextChanged(object sender, TextChangedEventArgs e)
        {
            ClearFieldInvalid(EmailBox);
            RefreshAosCredentialsDirty();
        }

        private void AosPassword_Changed(object sender, RoutedEventArgs e)
        {
            ClearFieldInvalid(PasswordBox);
            ClearFieldInvalid(PasswordPlainBox);
            RefreshAosCredentialsDirty();
        }

        private void AosPasswordPlain_TextChanged(object sender, TextChangedEventArgs e)
        {
            ClearFieldInvalid(PasswordBox);
            ClearFieldInvalid(PasswordPlainBox);
            RefreshAosCredentialsDirty();
        }

        private void RefreshAosCredentialsDirty()
        {
            if (_loadingUi)
                return;
            _aosCredentialsDirty = CredentialsChangedInUi();
            UpdateAosDirtyStatus();
        }

        private void RefreshAosActionButtons()
        {
            var updating = _remoteServerUpdateBusy;
            var busy = _busy;
            string tip = null;
            if (updating) tip = "TipAosUpdating";
            else if (busy) tip = "TipAosBusy";
            var on = !updating && !busy;
            SetActionEnabled(SaveAosBtn, on, tip);
            SetActionEnabled(ForceRegisterBtn, on, tip, "TipForceRegister");
        }

        private void ScheduleNext(bool fromNow)
        {
            var hours = _settings.IntervalHours;
            if (hours < 1) hours = 1;
            if (fromNow)
                _nextDueUtc = DateTime.UtcNow.AddHours(hours);
            else if (_settings.LastSuccessUtc.HasValue)
            {
                _nextDueUtc = _settings.LastSuccessUtc.Value.AddHours(hours);
                if (_nextDueUtc <= DateTime.UtcNow)
                    _nextDueUtc = DateTime.UtcNow;
            }
            else
                _nextDueUtc = DateTime.UtcNow;

            ApplyPreventiveRenewClamp();
            UpdateNextLabel();
            UpdateTrayTooltip();
        }

        /// <summary>
        /// Pull next renew/health forward to LastTokenExpiryUtc − 10 minutes (min +2 minutes from now).
        /// </summary>
        private void ApplyPreventiveRenewClamp()
        {
            if (_retrySchedule || _settings == null || !_settings.LastTokenExpiryUtc.HasValue)
                return;
            var preventiveUtc = _settings.LastTokenExpiryUtc.Value.AddMinutes(-10);
            var floor = DateTime.UtcNow.AddMinutes(2);
            if (preventiveUtc < floor)
                preventiveUtc = floor;
            if (preventiveUtc < _nextDueUtc)
                _nextDueUtc = preventiveUtc;
            if (preventiveUtc < _nextHealthUtc)
                _nextHealthUtc = preventiveUtc;
        }

        private void ScheduleRetryInOneHour()
        {
            _retrySchedule = true;
            _nextDueUtc = DateTime.UtcNow.AddHours(1);
            UpdateNextLabel();
            UpdateTrayTooltip();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            _uiOp = "Timer_Tick";
            UpdateNextLabel();
            UpdateTrayTooltip();
            UpdateTokenAgeText();
            if (TabRegister != null && TabRegister.IsSelected)
                RefreshRegisterAges();
            UpdateTokenFooterDisplay();
            RefreshAuthCooldownBanner();
            RefreshPasswordHintBanners(true);
            MaybeAlertTokenExpiry();
            MaybeAlertSqliteMissing();
            if (HasPasswordProblem())
                TrySilentBrowserPassRefresh(false);
            if (_busy) return;
            var now = DateTime.UtcNow;
            if (now >= _nextServiceCheckUtc)
            {
                CheckBmwServices(false, false);
                ScheduleNextServiceCheck();
            }
            if (_timerActive && now >= _nextDueUtc)
            {
                Forget(RunAutoHealthWithLimitedRegister(), "TimerAutoHealth");
                ScheduleNext(true);
                return;
            }
            if (_timerActive && now >= _nextHealthUtc)
            {
                _nextHealthUtc = now.AddHours(1);
                Forget(RunAutoHealthWithLimitedRegister(), "HourlyAutoHealth");
            }
        }

        private bool HasPasswordProblem()
        {
            if (_settings == null) return false;
            var email = EmailBox != null ? EmailBox.Text.Trim() : (_settings.Email ?? "").Trim();
            if (string.IsNullOrEmpty(email))
                email = (_settings.Email ?? "").Trim();
            if (string.IsNullOrEmpty(email))
                return false;

            var pass = GetPasswordUi();
            if (string.IsNullOrEmpty(pass))
                pass = _settings.GetPassword();
            if (string.IsNullOrEmpty(pass))
                return true;

            if (_settings.AuthRegisterBlocked)
                return true;
            return _settings.ConsecutiveFailures > 0
                && LooksLikeAuthFailure(_settings.LastMessage);
        }

        private void RefreshPasswordHintBanners(bool allowNotify)
        {
            RefreshBrowserPassNewerBanner(false);
            RefreshPasswordRotateBanner(allowNotify);
            RefreshAuthCooldownBanner();
        }

        private bool IsAuthCooldownActive()
        {
            if (_settings == null || !_settings.AuthCooldownUntilUtc.HasValue)
                return false;
            if (DateTime.UtcNow >= _settings.AuthCooldownUntilUtc.Value)
            {
                _settings.AuthCooldownUntilUtc = null;
                try { _settings.Save(); }
                catch { }
                return false;
            }
            return true;
        }

        private void BeginAuthCooldown()
        {
            if (_settings == null) return;
            // Do not stack/extend if already in cooldown.
            if (IsAuthCooldownActive())
            {
                RefreshAuthCooldownBanner();
                return;
            }
            var minutes = _settings.AuthCooldownMinutes;
            if (minutes < 5) minutes = 5;
            if (minutes > 240) minutes = 240;
            _settings.AuthCooldownUntilUtc = DateTime.UtcNow.AddMinutes(minutes);
            try { _settings.Save(); }
            catch { }
            RefreshAuthCooldownBanner();
            var until = _settings.AuthCooldownUntilUtc.Value.ToLocalTime().ToString("g");
            AppendLog(string.Format(UiStrings.T("AuthCooldownStarted"), until), LogLevel.Info);
            ShowBalloon(WinForms.ToolTipIcon.Warning,
                TruncateBalloon(string.Format(UiStrings.T("AuthCooldownStarted"), until)));
        }

        private void ClearAuthCooldown()
        {
            if (_settings == null || !_settings.AuthCooldownUntilUtc.HasValue)
                return;
            _settings.AuthCooldownUntilUtc = null;
            try { _settings.Save(); }
            catch { }
            RefreshAuthCooldownBanner();
            QueueFooterPublish();
            BroadcastServerSettings();
        }

        private void ClearAuthCooldown_Click(object sender, RoutedEventArgs e)
        {
            if (!IsAuthCooldownActive()) return;
            ClearAuthCooldown();
            AppendLog(UiStrings.T("AuthCooldownCleared"), LogLevel.Info);
        }

        private void ClearAuthRegisterBlocked_Click(object sender, RoutedEventArgs e)
        {
            if (!IsAuthRegisterBlocked()) return;
            SetAuthRegisterBlocked(false);
            QueueFooterPublish();
            BroadcastServerSettings();
            AppendLog(UiStrings.T("AuthRegisterBlockedCleared"), LogLevel.Info);
        }

        private bool IsAuthRegisterBlocked()
        {
            return _settings != null && _settings.AuthRegisterBlocked;
        }

        private void SetAuthRegisterBlocked(bool blocked)
        {
            if (_settings == null) return;
            if (_settings.AuthRegisterBlocked == blocked)
            {
                if (!blocked)
                    ResetAuthRegisterStrikes();
                RefreshAuthCooldownBanner();
                return;
            }
            _settings.AuthRegisterBlocked = blocked;
            if (!blocked)
                _settings.AuthRegisterStrikeCount = 0;
            try { _settings.Save(); }
            catch { }
            RefreshAuthCooldownBanner();
            UpdateAosLoginStatus();
        }

        private int GetAuthRegisterBlockAfter()
        {
            if (_settings == null) return 3;
            var n = _settings.AuthRegisterBlockAfter;
            if (n < 1) n = 1;
            if (n > 10) n = 10;
            return n;
        }

        private void ResetAuthRegisterStrikes()
        {
            if (_settings == null) return;
            if (_settings.AuthRegisterStrikeCount == 0) return;
            _settings.AuthRegisterStrikeCount = 0;
            try { _settings.Save(); }
            catch { }
        }

        private bool NoteAuthRegisterStrike(string reason)
        {
            if (_settings == null) return true;
            var limit = GetAuthRegisterBlockAfter();
            var next = _settings.AuthRegisterStrikeCount + 1;
            if (next < 0) next = 1;
            _settings.AuthRegisterStrikeCount = next;
            try { _settings.Save(); }
            catch { }
            AppendLog(string.Format(UiStrings.T("RegisterStrike"), next, limit)
                + (string.IsNullOrEmpty(reason) ? "" : " — " + reason), LogLevel.Info);
            return next >= limit;
        }

        private void RefreshAuthCooldownBanner()
        {
            var cooldown = IsAuthCooldownActive();
            var blocked = IsAuthRegisterBlocked();
            string untilLocal = null;
            if (cooldown && _settings != null && _settings.AuthCooldownUntilUtc.HasValue)
                untilLocal = _settings.AuthCooldownUntilUtc.Value.ToLocalTime().ToString("g");

            if (AuthCooldownStatusLine != null)
            {
                if (cooldown && untilLocal != null)
                {
                    AuthCooldownStatusLine.Text = string.Format(UiStrings.T("AuthCooldownStatusActive"), untilLocal);
                    AuthCooldownStatusLine.Foreground = StatusWarn();
                }
                else
                {
                    AuthCooldownStatusLine.Text = UiStrings.T("AuthCooldownStatusInactive");
                    AuthCooldownStatusLine.Foreground = StatusOk();
                }
            }
            if (AuthRegisterBlockedStatusLine != null)
            {
                if (blocked)
                {
                    AuthRegisterBlockedStatusLine.Text = UiStrings.T("AuthRegisterBlockedStatusActive");
                    AuthRegisterBlockedStatusLine.Foreground = StatusErr();
                }
                else
                {
                    AuthRegisterBlockedStatusLine.Text = UiStrings.T("AuthRegisterBlockedStatusInactive");
                    AuthRegisterBlockedStatusLine.Foreground = StatusOk();
                }
            }

            if (ClearAuthCooldownBtn != null)
            {
                ClearAuthCooldownBtn.Visibility = cooldown ? Visibility.Visible : Visibility.Collapsed;
                ClearAuthCooldownBtn.IsEnabled = cooldown;
            }
            if (ClearAuthRegisterBlockedBtn != null)
            {
                ClearAuthRegisterBlockedBtn.Visibility = blocked ? Visibility.Visible : Visibility.Collapsed;
                ClearAuthRegisterBlockedBtn.IsEnabled = blocked;
            }

            UpdateAuthGuardsFooter(cooldown, blocked, untilLocal);

            if (AuthCooldownBanner == null || AuthCooldownText == null)
                return;
            if (!cooldown && !blocked)
            {
                AuthCooldownBanner.Visibility = Visibility.Collapsed;
                return;
            }
            if (blocked)
            {
                AuthCooldownText.Text = UiStrings.T("AuthRegisterBlockedBanner");
                if (cooldown && untilLocal != null)
                    AuthCooldownText.Text += " " + string.Format(UiStrings.T("AuthCooldownBanner"), untilLocal);
            }
            else if (untilLocal != null)
            {
                AuthCooldownText.Text = string.Format(UiStrings.T("AuthCooldownBanner"), untilLocal);
            }
            AuthCooldownBanner.Visibility = Visibility.Visible;
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

        private void MarkPasswordFresh(string reason)
        {
            if (_settings == null) return;
            _settings.LastPasswordChangeUtc = DateTime.UtcNow;
            _settings.LastPasswordRemindUtc = null;
            try { _settings.Save(); }
            catch { }
            if (!string.IsNullOrEmpty(reason))
                AppendLog(string.Format(UiStrings.T("PasswordFreshMarked"), reason), LogLevel.Info);
        }

        private bool AppPasswordMatchesBrowserCache()
        {
            if (_cachedBrowserMeta == null) return false;
            if (_cachedBrowserMeta.DecryptStatus != ChromiumCrypto.DecryptStatus.Ok)
                return false;
            if (string.IsNullOrEmpty(_cachedBrowserMeta.Password)) return false;
            // Cache older than 24h is too stale to suppress the reminder.
            if ((DateTime.UtcNow - _cachedBrowserMetaUtc).TotalHours > 24)
                return false;
            string appPass = null;
            try { appPass = GetPasswordUi(); }
            catch { }
            if (string.IsNullOrEmpty(appPass) && _settings != null)
            {
                try { appPass = _settings.GetPassword(); }
                catch { }
            }
            return string.Equals(appPass ?? "", _cachedBrowserMeta.Password, StringComparison.Ordinal);
        }

        private void RefreshPasswordRotateBanner(bool allowNotify)
        {
            // Password-rotate reminder removed — keep stub so call sites compile.
        }

        private void ScheduleNextServiceCheck()
        {
            var hours = _settings != null ? _settings.IntervalHours : 1;
            if (hours < 1) hours = 1;
            if (hours > 168) hours = 168;
            _nextServiceCheckUtc = DateTime.UtcNow.AddHours(hours);
        }

        private void SeedRegisterTimesFromLog()
        {
            if (_settings == null)
                return;
            if (_settings.LastRegisterOkUtc.HasValue || _settings.LastRegisterFailUtc.HasValue)
                return;

            DateTime? okLocal = null;
            DateTime? failLocal = null;
            try
            {
                var paths = new[] { AppSettings.LogPath + ".1", AppSettings.LogPath };
                for (var p = 0; p < paths.Length; p++)
                {
                    if (!File.Exists(paths[p]))
                        continue;
                    foreach (var line in ReadSharedLogLines(paths[p]))
                    {
                        if (line == null || line.Length < 21)
                            continue;
                        DateTime local;
                        if (!DateTime.TryParseExact(line.Substring(0, 19), "yyyy-MM-dd HH:mm:ss",
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out local))
                            continue;
                        var text = line.Substring(19);
                        if (text.IndexOf("Registration OK", StringComparison.OrdinalIgnoreCase) >= 0)
                            okLocal = local;
                        else if (text.IndexOf("Register failed", StringComparison.OrdinalIgnoreCase) >= 0
                            || text.IndexOf("Register rejected", StringComparison.OrdinalIgnoreCase) >= 0
                            || text.IndexOf("Register returned empty", StringComparison.OrdinalIgnoreCase) >= 0
                            || text.IndexOf("KAI_Properties.cfg was not created", StringComparison.OrdinalIgnoreCase) >= 0
                            || text.IndexOf("KAI_Properties.cfg invalid", StringComparison.OrdinalIgnoreCase) >= 0)
                            failLocal = local;
                    }
                }
            }
            catch
            {
                return;
            }

            var changed = false;
            if (okLocal.HasValue)
            {
                _settings.LastRegisterOkUtc = okLocal.Value.ToUniversalTime();
                changed = true;
            }
            if (failLocal.HasValue && (!okLocal.HasValue || failLocal.Value > okLocal.Value))
            {
                _settings.LastRegisterFailUtc = failLocal.Value.ToUniversalTime();
                changed = true;
            }
            if (!changed)
                return;
            try { _settings.Save(); }
            catch { }
        }

        private static DateTime LogRetentionCutoffLocal()
        {
            return ActivityRegisterLogic.LogRetentionCutoffLocal();
        }

        private static bool IsWithinLogRetention(DateTime local)
        {
            return ActivityRegisterLogic.IsWithinLogRetention(local);
        }

        private static bool IsActivityUiNoise(string text)
        {
            return ActivityRegisterLogic.IsActivityUiNoise(text);
        }

        /// <summary>Seed Activity with the last MaxLogBlocks in-window lines (not a full 3-day dump).</summary>
        private void SeedActivityHistoryFromLog()
        {
            try
            {
                var cutoff = LogRetentionCutoffLocal();
                var paths = new[] { AppSettings.LogPath + ".1", AppSettings.LogPath };
                // Ring buffer of the newest eligible lines while scanning files in order.
                var ring = new List<LogEntry>(MaxLogBlocks + 1);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (var p = 0; p < paths.Length; p++)
                {
                    if (!File.Exists(paths[p])) continue;
                    LogEntry pending = null;
                    foreach (var line in ReadSharedLogLines(paths[p]))
                    {
                        if (line == null) continue;
                        DateTime local;
                        string text;
                        if (TryParseActivityLogLine(line, out local, out text))
                        {
                            if (pending != null)
                                AddSeededActivityEntry(ring, seen, pending, MaxLogBlocks);
                            pending = null;
                            if (local < cutoff) continue;
                            if (string.IsNullOrEmpty(text) || IsActivityUiNoise(text)) continue;
                            pending = new LogEntry
                            {
                                Time = local,
                                Level = InferLogLevel(text, false),
                                Text = text
                            };
                        }
                        else if (pending != null && line.Length > 0)
                        {
                            // Continuation of REQUEST/RESPONSE multi-line dump.
                            pending.Text = (pending.Text ?? "") + "\n" + line;
                        }
                    }
                    if (pending != null)
                        AddSeededActivityEntry(ring, seen, pending, MaxLogBlocks);
                }
                _logEntries.Clear();
                _logEntries.AddRange(ring);
                RefreshLogView();
            }
            catch { }
        }

        private static bool TryParseActivityLogLine(string line, out DateTime local, out string text)
        {
            local = DateTime.MinValue;
            text = null;
            if (line == null || line.Length < 21) return false;
            if (!DateTime.TryParseExact(line.Substring(0, 19), "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out local))
                return false;
            text = line.Substring(19).TrimStart();
            return true;
        }

        private static void AddSeededActivityEntry(
            List<LogEntry> ring, HashSet<string> seen, LogEntry entry, int maxBlocks)
        {
            if (ring == null || entry == null) return;
            var key = entry.Time.ToString("yyyy-MM-dd HH:mm:ss") + "\t" + (entry.Text ?? "");
            if (seen != null && !seen.Add(key)) return;
            ring.Add(entry);
            if (maxBlocks > 0 && ring.Count > maxBlocks)
                ring.RemoveAt(0);
        }

        private void TrimLogEntriesToRetention()
        {
            var cutoff = LogRetentionCutoffLocal();
            while (_logEntries.Count > 0)
            {
                var e = _logEntries[0];
                if (e != null && e.Time >= cutoff) break;
                _logEntries.RemoveAt(0);
            }
            if (_logEntries.Count > MaxLogEntriesMemory)
                _logEntries.RemoveRange(0, _logEntries.Count - MaxLogEntriesMemory);
        }

        private void TrimRegisterHistoryToRetention()
        {
            var cutoff = LogRetentionCutoffLocal();
            for (var i = _registerHistory.Count - 1; i >= 0; i--)
            {
                var r = _registerHistory[i];
                if (r == null || r.At < cutoff)
                    _registerHistory.RemoveAt(i);
            }
            if (_registerHistory.Count > RegisterHistoryMax)
                _registerHistory.RemoveRange(0, _registerHistory.Count - RegisterHistoryMax);
        }

        private static bool TryClassifyRegisterHistory(string text, out int kind)
        {
            return ActivityRegisterLogic.TryClassifyRegisterHistory(text, out kind);
        }

        private static string RegisterResultTextForKind(int kind)
        {
            if (kind == 1) return UiStrings.T("RegisterResultOk");
            if (kind == 2) return UiStrings.T("RegisterResultInfo");
            return UiStrings.T("RegisterResultFail");
        }

        private void LoadRegisterHistoryFromLog()
        {
            try
            {
                _registerHistory.Clear();
                var paths = new[] { AppSettings.LogPath + ".1", AppSettings.LogPath };
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (var p = 0; p < paths.Length; p++)
                {
                    if (!File.Exists(paths[p])) continue;
                    string pendingText = null;
                    DateTime pendingAt = DateTime.MinValue;
                    foreach (var line in ReadSharedLogLines(paths[p]))
                    {
                        if (line == null) continue;
                        DateTime local;
                        string text;
                        if (TryParseActivityLogLine(line, out local, out text))
                        {
                            if (pendingText != null)
                                AddRegisterHistoryFromParsed(seen, pendingAt, pendingText);
                            pendingText = null;
                            if (!IsWithinLogRetention(local)) continue;
                            pendingAt = local;
                            pendingText = text;
                        }
                        else if (pendingText != null && line.Length > 0)
                        {
                            pendingText = pendingText + "\n" + line;
                        }
                    }
                    if (pendingText != null)
                        AddRegisterHistoryFromParsed(seen, pendingAt, pendingText);
                }
                _registerHistory.Sort((a, b) => a.At.CompareTo(b.At));
                TrimRegisterHistoryToRetention();
                RefreshRegisterAges();
                ApplyRegisterFilter();
            }
            catch { }
        }

        private void AddRegisterHistoryFromParsed(HashSet<string> seen, DateTime local, string text)
        {
            int kind;
            if (!TryClassifyRegisterHistory(text, out kind)) return;
            var key = local.ToString("yyyy-MM-dd HH:mm:ss") + "\t" + kind + "\t" + text;
            if (seen != null && !seen.Add(key)) return;
            _registerHistory.Add(new RegisterHistoryRow
            {
                At = local,
                Kind = kind,
                Ok = kind == 1,
                Text = text,
                AgeText = "",
                ResultText = RegisterResultTextForKind(kind)
            });
        }

        private void MaybeAppendRegisterHistory(string text, DateTime at)
        {
            if (!IsWithinLogRetention(at)) return;
            int kind;
            if (!TryClassifyRegisterHistory(text, out kind)) return;
            var clean = text ?? "";
            if (clean.StartsWith("OK:", StringComparison.OrdinalIgnoreCase))
                clean = clean.Substring(3).TrimStart();
            else if (clean.StartsWith("FAIL:", StringComparison.OrdinalIgnoreCase))
                clean = clean.Substring(5).TrimStart();
            var key = at.ToString("yyyy-MM-dd HH:mm:ss") + "\t" + kind + "\t" + clean;
            for (var i = 0; i < _registerHistory.Count; i++)
            {
                var r = _registerHistory[i];
                if (r == null) continue;
                var existing = r.At.ToString("yyyy-MM-dd HH:mm:ss") + "\t" + r.Kind + "\t" + (r.Text ?? "");
                if (string.Equals(existing, key, StringComparison.Ordinal)) return;
            }
            var span = DateTime.Now - at;
            if (span < TimeSpan.Zero) span = TimeSpan.Zero;
            var added = new RegisterHistoryRow
            {
                At = at,
                Kind = kind,
                Ok = kind == 1,
                Text = clean,
                AgeText = string.Format(UiStrings.T("TimeAgo"), FormatAge(span)),
                ResultText = RegisterResultTextForKind(kind)
            };
            _registerHistory.Add(added);
            TrimRegisterHistoryToRetention();
            ApplyRegisterFilter();
            if (TabRegister != null && TabRegister.IsSelected)
                ScrollRegisterHistoryToEnd();
            BroadcastRegisterEntry(added);
        }

        private void RefreshRegisterResultTexts()
        {
            for (var i = 0; i < _registerHistory.Count; i++)
            {
                var r = _registerHistory[i];
                if (r == null) continue;
                r.ResultText = RegisterResultTextForKind(r.Kind);
            }
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
                r.AgeText = string.Format(UiStrings.T("TimeAgo"), FormatAge(span));
            }
            ApplyRegisterFilter();
        }

        private void ApplyRegisterFilter()
        {
            if (RegisterHistoryList == null) return;
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
                // Unfiltered: cap display to newest N (full 3-day buffer stays in memory).
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
            UpdateEmptyHint(RegisterEmptyHint, _registerHistory.Count == 0);
            UpdateClearButtonsEnabled();
        }

        private async Task<RenewResult> RunNowAsync(
            bool startTimerOnSuccess = false,
            bool forceRegister = false,
            string passwordOverride = null,
            bool ignoreAuthGuards = false,
            bool lockRegisterAfter = false)
        {
            // Do not mutate auth guards here — that belongs on the non-busy execution path
            // so a queued Force/403 Register cannot clear cooldown/block for the active run.
            if (ignoreAuthGuards)
                forceRegister = true;

            var cooldown = ignoreAuthGuards ? false : IsAuthCooldownActive();
            if (cooldown && forceRegister)
            {
                AppendLog(UiStrings.T("AuthCooldownRegisterDeferred"), LogLevel.Info);
                forceRegister = false;
            }

            if (_busy)
            {
                // Queue a follow-up (e.g. Paste & Save while a run is active).
                if (!string.IsNullOrEmpty(passwordOverride) || forceRegister || ignoreAuthGuards)
                {
                    if (!string.IsNullOrEmpty(passwordOverride))
                    {
                        SetPasswordUi(passwordOverride);
                        if (_settings != null)
                        {
                            // Clear block when queued password differs from stored.
                            string oldQueued = null;
                            try { oldQueued = _settings.GetPassword(); }
                            catch { }
                            if (!string.Equals(passwordOverride, oldQueued ?? "", StringComparison.Ordinal))
                                _settings.AuthRegisterBlocked = false;
                            _settings.SetPassword(passwordOverride);
                            _settings.Save();
                        }
                    }
                    if (cooldown && !ignoreAuthGuards)
                    {
                        AppendLog(UiStrings.T("AuthCooldownRegisterDeferred"), LogLevel.Info);
                        return new RenewResult { Ok = false, Message = UiStrings.T("AuthCooldownRegisterDeferred") };
                    }
                    // Park 403 VIN on the queued run so the active run does not consume it.
                    _renewQueue.Enqueue(
                        passwordOverride ?? (_settings != null ? _settings.GetPassword() : null),
                        ignoreAuthGuards,
                        lockRegisterAfter,
                        startTimerOnSuccess,
                        ref _pending403RetryVin,
                        ref _pending403RetryReaderId);
                    AppendLog(ignoreAuthGuards
                        ? (lockRegisterAfter
                            ? "Renew busy — queued 403 Register (then block)."
                            : "Renew busy — queued Force Register (bypass guards).")
                        : "Renew busy — queued force Register with updated password.", LogLevel.Info);
                    return new RenewResult { Ok = false, Message = UiStrings.T("AosBusyRetry") };
                }
                return new RenewResult { Ok = false, Message = UiStrings.T("AosBusyRetry") };
            }
            if (passwordOverride != null)
                SetPasswordUi(passwordOverride);
            // Remote forceregister / ignoreAuthGuards must not pop a modal MessageBox.
            if (!ValidateAosCredentials(!ignoreAuthGuards))
            {
                if (lockRegisterAfter)
                {
                    _pending403RetryVin = null;
                    _pending403RetryReaderId = null;
                    _renewQueue.Clear403Queued();
                    SetAuthRegisterBlocked(true);
                }
                return new RenewResult { Ok = false, Message = UiStrings.T("MissingPassword") };
            }
            _busyExpectRegister = forceRegister;
            SetBusy(true);
            RenewResult result = null;
            try
            {
                SaveFromUi(false, passwordOverride);
                // Re-check after save (settings object may have changed / block cleared on password change).
                if (ignoreAuthGuards)
                {
                    ClearAuthCooldown();
                    // 403 one-shot keeps blocked until after this run; Force Register clears it.
                    if (!lockRegisterAfter)
                        SetAuthRegisterBlocked(false);
                }
                cooldown = ignoreAuthGuards ? false : IsAuthCooldownActive();
                var blocked = ignoreAuthGuards ? false : IsAuthRegisterBlocked();
                var allowRegister = ignoreAuthGuards || (!cooldown && !blocked);
                var settings = _settings;
                var force = forceRegister && allowRegister;
                _busyExpectRegister = force;
                UpdateAosDirtyStatus();
                string password;
                try
                {
                    password = !string.IsNullOrEmpty(passwordOverride)
                        ? passwordOverride
                        : settings.GetPassword();
                }
                catch
                {
                    password = passwordOverride;
                }
                if (!allowRegister)
                {
                    if (cooldown)
                        AppendLog(UiStrings.T("AuthCooldownHealthOnly"), LogLevel.Info);
                    else
                        AppendLog(UiStrings.T("AuthRegisterBlockedHealthOnly"), LogLevel.Info);
                    RenewEngine.Log(cooldown
                        ? "Run start (auth cooldown — Health only)"
                        : "Run start (auth register blocked — Health only)");
                }
                else
                {
                    AppendLog(force
                        ? UiStrings.T("RunStartForceRegister")
                        : UiStrings.T("RunStart"));
                    RenewEngine.Log(force ? "Run start (force Register)" : "Run start");
                }
                try
                {
                    var allow = allowRegister;
                    var forceCapture = force;
                    var passCapture = password;
                    var ct = _renewCts != null ? _renewCts.Token : CancellationToken.None;
                    result = await Task.Run(() => RenewEngine.Run(settings, forceCapture, passCapture, allow, ct), ct);
                }
                catch (OperationCanceledException)
                {
                    result = new RenewResult { Ok = false, Message = "Cancelled" };
                }
                catch (Exception ex)
                {
                    result = new RenewResult { Ok = false, Message = ex.Message };
                }

                if (result == null)
                    result = new RenewResult { Ok = false, Message = "failed" };
                RenewEngine.Log(result.Message);

                if (result != null && !result.Ok)
                    _healthFailStreak++;
                else if (result != null && result.Ok)
                    _healthFailStreak = 0;

                // Rare password change on another site: pull Firebase once (also when already in cooldown/block,
                // or after N consecutive Health/renew fails).
                var healthStreakPull = result != null && !result.Ok
                    && DnsHealthRetryLogic.ShouldTryFirebasePullOnHealthStreak(_healthFailStreak);
                if (result != null
                    && IsFirebasePushActive()
                    && (!allowRegister
                        || (result.Ok && result.RegisterAuthRejected)
                        || (!result.Ok && LooksLikeAuthFailure(result.Message ?? ""))
                        || healthStreakPull))
                {
                    var emailTry = settings != null ? (settings.Email ?? "") : (_settings != null ? (_settings.Email ?? "") : "");
                    var passTry = password ?? "";
                    if (string.IsNullOrEmpty(passTry) && _settings != null)
                    {
                        try { passTry = _settings.GetPassword() ?? ""; }
                        catch { passTry = ""; }
                    }
                    try
                    {
                        if (healthStreakPull)
                            AppendLog(string.Format(UiStrings.T("HealthFailStreakFirebasePull"), _healthFailStreak),
                                LogLevel.Info);
                        if (await TryPullNewerPeerCredsOnAuthFailAsync(emailTry, passTry).ConfigureAwait(true))
                        {
                            _healthFailStreak = 0;
                            result = new RenewResult
                            {
                                Ok = true,
                                RegisterAttempted = true,
                                RegisterAuthRejected = false,
                                SkippedRegister = false,
                                Message = "OK (Firebase credentials applied)"
                            };
                            try { password = _settings != null ? (_settings.GetPassword() ?? password) : password; }
                            catch { }
                        }
                    }
                    catch { }
                }

                // Suppress intermediate Error/Warning balloons while a key 403 remediation is pending;
                // final tip is decided after RetryBmwUploadAfter403Async.
                var suppressAuthBalloons = !string.IsNullOrEmpty(_pending403RetryVin);
                if (result.Ok)
                {
                    if (result.RegisterAuthRejected)
                    {
                        AppendLog(result.Message, LogLevel.Error);
                        if (!suppressAuthBalloons)
                            ShowBalloon(WinForms.ToolTipIcon.Warning, TruncateBalloon(result.Message));
                    }
                    else
                    {
                        AppendLog("OK: " + result.Message, LogLevel.Success);
                        if (!suppressAuthBalloons)
                            ShowBalloon(WinForms.ToolTipIcon.Info, TruncateBalloon(result.Message));
                    }
                    // Reload settings so LastTokenExpiresInSeconds / LastSuccessUtc from engine persist
                    var cooldownUntil = _settings != null ? _settings.AuthCooldownUntilUtc : null;
                    var registerBlocked = _settings != null && _settings.AuthRegisterBlocked;
                    _settings = AppSettings.Load();
                    // Keep the password we just registered if Load somehow misses it.
                    if (!string.IsNullOrEmpty(password))
                        _settings.SetPassword(password);
                    // Preserve in-memory cooldown / block across Load unless cleared below.
                    if (cooldownUntil.HasValue)
                        _settings.AuthCooldownUntilUtc = cooldownUntil;
                    _settings.AuthRegisterBlocked = registerBlocked;
                    if (result.RegisterAuthRejected)
                        _settings.AuthRegisterBlocked = true;
                    else if (!result.SkippedRegister && !lockRegisterAfter)
                    {
                        _settings.AuthCooldownUntilUtc = null;
                        _settings.AuthRegisterBlocked = false;
                    }
                    _settings.ConsecutiveFailures = 0;
                    _settings.Save();
                    ClearFailureAlert();
                    _retrySchedule = false;
                    _healthFailStreak = 0;
                    if (!result.RegisterSkippedDueToDns)
                        _dnsHealthRetryStreak = 0;
                    ScheduleNext(true);
                    UpdateTokenAgeText();
                    if (result.RegisterAuthRejected)
                    {
                        SetTokenFooter(false);
                    }
                    else
                    {
                        _aosBmw403Fail = false;
                        SetTokenFooter(true);
                    }
                    if (startTimerOnSuccess && !_timerActive)
                        StartTimer(true, false);
                    MaybePublishPeerCredentialsAfterLocalRegister(result, settings != null ? settings.Email : null, password);
                    // Keep clients' LastSuccessUtc (browser-newer banner) in sync.
                    BroadcastServerSettings();
                }
                else
                {
                    AppendLog("FAIL: " + result.Message, LogLevel.Error);
                    if (!suppressAuthBalloons)
                        MaybeAppendBrowserPasswordHint(result.Message);
                    _settings.ConsecutiveFailures = _settings.ConsecutiveFailures + 1;
                    _settings.LastMessage = result.Message;
                    _settings.Save();
                    ApplyFailureAlert(result.Message);
                    var msg = result.Message ?? "";
                    var healthOnlyFail = msg.IndexOf("Health failed (no Register)", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!suppressAuthBalloons && !healthOnlyFail)
                        ShowBalloon(WinForms.ToolTipIcon.Error, TruncateBalloon(result.Message));
                    var skipBlockMsg = msg.IndexOf("Auth cooldown", StringComparison.OrdinalIgnoreCase) >= 0
                        || msg.IndexOf("Auth register blocked", StringComparison.OrdinalIgnoreCase) >= 0
                        || msg.IndexOf("Register blocked", StringComparison.OrdinalIgnoreCase) >= 0
                        || healthOnlyFail
                        || msg.IndexOf("Register skipped", StringComparison.OrdinalIgnoreCase) >= 0;
                    // Failed Register → strike; block only at AuthRegisterBlockAfter threshold.
                    if (result.RegisterAttempted && !skipBlockMsg)
                    {
                        bool shouldBlock;
                        if (_registerStrikeNotedThisRun)
                            shouldBlock = _settings.AuthRegisterStrikeCount >= GetAuthRegisterBlockAfter();
                        else
                            shouldBlock = NoteAuthRegisterStrike("Register failed");
                        _registerStrikeNotedThisRun = false;
                        if (shouldBlock)
                            SetAuthRegisterBlocked(true);
                    }
                    if (LooksLikeAuthFailure(msg) && !skipBlockMsg)
                        BeginAuthCooldown();
                    // Retry schedule stays Health-only (timer never Registers).
                    ScheduleRetryInOneHour();
                    SetTokenFooter(false);
                }
                if (lockRegisterAfter)
                    SetAuthRegisterBlocked(true);
                var retryVin = _pending403RetryVin;
                var retryReader = _pending403RetryReaderId;
                _pending403RetryVin = null;
                _pending403RetryReaderId = null;
                _registerStrikeNotedThisRun = false;
                if (!string.IsNullOrEmpty(retryVin))
                    Forget(RetryBmwUploadAfter403Async(retryVin, retryReader), "RetryBmwUploadAfter403");
                RefreshPasswordHintBanners(false);
                RefreshStatusBadge();
                return result;
            }
            finally
            {
                SetBusy(false);
                DrainPendingForceRegister();
            }
        }

        private void DrainPendingForceRegister()
        {
            PendingRenewWork work;
            if (!_renewQueue.TryDequeue(out work) || work == null)
                return;
            var pass = work.Password;
            var startTimer = work.StartTimerOnSuccess;
            var ignoreGuards = work.IgnoreAuthGuards;
            var lockAfter = work.LockRegisterAfter;
            // Promote parked 403 VIN onto the run that is about to execute.
            if (!string.IsNullOrEmpty(work.Retry403Vin))
            {
                _pending403RetryVin = work.Retry403Vin;
                _pending403RetryReaderId = work.Retry403ReaderId;
            }
            if (string.IsNullOrEmpty(pass) && !ignoreGuards)
                return;
            if (!ignoreGuards && IsAuthCooldownActive())
            {
                AppendLog(UiStrings.T("AuthCooldownRegisterDeferred"), LogLevel.Info);
                SetPasswordUi(pass);
                if (_settings != null)
                {
                    string oldPass = null;
                    try { oldPass = _settings.GetPassword(); }
                    catch { }
                    if (!string.Equals(pass, oldPass ?? "", StringComparison.Ordinal))
                        _settings.AuthRegisterBlocked = false;
                    _settings.SetPassword(pass);
                    _settings.Save();
                }
                return;
            }
            AppendLog(ignoreGuards
                ? (lockAfter
                    ? "Running queued 403 Register (then block)..."
                    : "Running queued Force Register (bypass)...")
                : "Running queued force Register...", LogLevel.Info);
            Forget(RunNowAsync(startTimer, true, pass, ignoreGuards, lockAfter), "DrainPendingForceRegister");
        }

        private static bool LooksLikeAuthFailure(string message)
        {
            if (string.IsNullOrEmpty(message)) return false;
            var m = message.ToLowerInvariant();
            return m.IndexOf("401", StringComparison.Ordinal) >= 0
                || m.IndexOf("403", StringComparison.Ordinal) >= 0
                || m.IndexOf("unauthorized", StringComparison.Ordinal) >= 0
                || m.IndexOf("forbidden", StringComparison.Ordinal) >= 0
                || m.IndexOf("password", StringComparison.Ordinal) >= 0
                || m.IndexOf("credential", StringComparison.Ordinal) >= 0
                || m.IndexOf("authentication", StringComparison.Ordinal) >= 0
                || m.IndexOf("access denied", StringComparison.Ordinal) >= 0
                || m.IndexOf("login failed", StringComparison.Ordinal) >= 0
                || m.IndexOf("invalid user", StringComparison.Ordinal) >= 0
                || m.IndexOf("bad credentials", StringComparison.Ordinal) >= 0;
        }

        private void MaybeAppendBrowserPasswordHint(string failMessage)
        {
            if (!LooksLikeAuthFailure(failMessage))
                return;
            TrySilentBrowserPassRefresh(true);
            if (_settings == null || !_settings.BrowserPasswordHintOnAuthFail)
                return;
            try
            {
                var email = (_settings.Email ?? "").Trim();
                if (string.IsNullOrEmpty(email) && EmailBox != null)
                    email = EmailBox.Text.Trim();
                if (string.IsNullOrEmpty(email))
                    return;
                var meta = TryGetBrowserMeta(email, false);
                if (meta == null || !meta.Found)
                    return;
                string hint;
                if (meta.LastSavedLocal.HasValue)
                    hint = string.Format(UiStrings.T("BrowserPassHint"),
                        meta.BrowserName, FormatBrowserSavedWhen(meta.LastSavedLocal));
                else
                    hint = string.Format(UiStrings.T("BrowserPassHintNoDate"), meta.BrowserName);
                AppendLog(hint, LogLevel.Info);
                ShowBalloon(WinForms.ToolTipIcon.Warning, TruncateBalloon(hint));
            }
            catch { }
        }

        private async Task RunAutoHealthWithLimitedRegister()
        {
            if (_busy) return;
            if (!ValidateAosCredentials(false)) return;
            _busyExpectRegister = false;
            SetBusy(true);
            var needAutoRegister = false;
            var dnsOnlyFail = false;
            try
            {
                SaveFromUi(false);
                AppendLog(UiStrings.T("HealthOnly"), true);
                var settings = _settings;
                RenewResult result = null;
                try
                {
                    result = await Task.Run(() => RenewEngine.HealthOnly(settings));
                }
                catch (Exception ex)
                {
                    result = new RenewResult { Ok = false, Message = ex.Message };
                }
                RenewEngine.Log(result.Message);
                if (result.Ok)
                {
                    _dnsHealthRetryStreak = 0;
                    _healthFailStreak = 0;
                    AppendLog("OK: " + result.Message, LogLevel.Verbose, true);
                    if (settings.LastTokenExpiresInSeconds > 0)
                    {
                        _settings.LastTokenExpiresInSeconds = settings.LastTokenExpiresInSeconds;
                        _settings.Save();
                    }
                    UpdateTokenAgeText();
                    _aosBmw403Fail = false;
                    SetTokenFooter(true);
                    if (_timerActive)
                        _nextHealthUtc = DateTime.UtcNow.AddHours(1);
                    ApplyPreventiveRenewClamp();
                    UpdateNextLabel();
                    UpdateTrayTooltip();
                    ClearFailureAlert();
                    RefreshStatusBadge();
                    return;
                }

                _healthFailStreak++;
                dnsOnlyFail = RenewEngine.IsTransientDnsFailure(result.Message);
                AppendLog("FAIL: " + result.Message, LogLevel.Error);
                if (dnsOnlyFail)
                    AppendLog(UiStrings.T("HealthDnsNetworkHint"), LogLevel.Info);
                if (LooksLikeAuthFailure(result.Message))
                    TrySilentBrowserPassRefresh(true);
                ApplyFailureAlert(result.Message);
                SetTokenFooter(false);

                // After N consecutive Health fails, try newer Firebase credentials (even if Register is blocked/cooldown).
                if (IsFirebasePushActive()
                    && DnsHealthRetryLogic.ShouldTryFirebasePullOnHealthStreak(_healthFailStreak))
                {
                    AppendLog(string.Format(UiStrings.T("HealthFailStreakFirebasePull"), _healthFailStreak),
                        LogLevel.Info);
                    string emailTry = _settings != null ? (_settings.Email ?? "") : "";
                    string passTry = "";
                    try { passTry = _settings != null ? (_settings.GetPassword() ?? "") : ""; }
                    catch { passTry = ""; }
                    try
                    {
                        if (await TryPullNewerPeerCredsOnAuthFailAsync(emailTry, passTry).ConfigureAwait(true))
                        {
                            _healthFailStreak = 0;
                            _dnsHealthRetryStreak = 0;
                            ClearFailureAlert();
                            SetTokenFooter(true);
                            if (_timerActive)
                                _nextHealthUtc = DateTime.UtcNow.AddHours(1);
                            ApplyPreventiveRenewClamp();
                            UpdateNextLabel();
                            UpdateTrayTooltip();
                            RefreshStatusBadge();
                            return;
                        }
                    }
                    catch { }
                }

                if (IsAuthRegisterBlocked())
                {
                    if (dnsOnlyFail && _timerActive)
                        ScheduleDnsHealthRetry();
                    AppendLog(UiStrings.T("AuthRegisterBlockedHealthOnly"), LogLevel.Info);
                    RefreshStatusBadge();
                    return;
                }

                if (_settings.LastAutoRegisterUtc.HasValue
                    && (DateTime.UtcNow - _settings.LastAutoRegisterUtc.Value).TotalHours < AutoRegisterMinHours)
                {
                    if (dnsOnlyFail && _timerActive)
                        ScheduleDnsHealthRetry();
                    var next = _settings.LastAutoRegisterUtc.Value.AddHours(AutoRegisterMinHours).ToLocalTime().ToString("g");
                    AppendLog("Auto Register skipped (max 1 per 12h). Next allowed after " + next + ".", LogLevel.Info);
                    RefreshStatusBadge();
                    return;
                }

                // Do NOT stamp LastAutoRegisterUtc yet — only after Register API actually runs.
                AppendLog("Health failed — auto Register (max 1 per 12h)...", LogLevel.Info);
                RefreshStatusBadge();
                needAutoRegister = true;
            }
            finally
            {
                SetBusy(false);
            }

            if (!needAutoRegister)
                return;

            // forceRegister=false: engine Registers because Health already failed (unless DNS skip / Health recovers).
            RenewResult runResult = null;
            try
            {
                runResult = await RunNowAsync(false, false);
            }
            catch (Exception ex)
            {
                AppendLog("Auto Register error: " + ex.Message, LogLevel.Error);
            }

            if (DnsHealthRetryLogic.ShouldStampAutoRegisterCooldown(
                runResult != null && runResult.RegisterAttempted))
            {
                _settings.LastAutoRegisterUtc = DateTime.UtcNow;
                try { _settings.Save(); }
                catch { }
            }
            else
            {
                AppendLog(UiStrings.T("AutoRegisterCooldownNotConsumed"), LogLevel.Info);
                var stillDns = runResult == null
                    || runResult.RegisterSkippedDueToDns
                    || RenewEngine.IsTransientDnsFailure(runResult.Message);
                if (_timerActive && stillDns)
                    ScheduleDnsHealthRetry();
            }
        }

        /// <summary>Pull next Health forward after DNS fail (5→10→15 min), not +1h.</summary>
        private void ScheduleDnsHealthRetry()
        {
            _dnsHealthRetryStreak++;
            var minutes = DnsHealthRetryLogic.NextRetryMinutes(_dnsHealthRetryStreak);
            _nextHealthUtc = DateTime.UtcNow.AddMinutes(minutes);
            AppendLog(string.Format(UiStrings.T("HealthDnsRetryScheduled"), minutes), LogLevel.Info);
            UpdateNextLabel();
            UpdateTrayTooltip();
        }

        private async Task RunHealthOnlyAsync(bool interactive = true)
        {
            if (_busy) return;
            if (!ValidateAosCredentials(interactive)) return;
            _busyExpectRegister = false;
            SetBusy(true);
            try
            {
                SaveFromUi(false);
                AppendLog(UiStrings.T("HealthOnly"), !interactive);
                var settings = _settings;
                RenewResult result = null;
                try
                {
                    result = await Task.Run(() => RenewEngine.HealthOnly(settings));
                }
                catch (Exception ex)
                {
                    result = new RenewResult { Ok = false, Message = ex.Message };
                }
                RenewEngine.Log(result.Message);
                if (result.Ok)
                {
                    _dnsHealthRetryStreak = 0;
                    _healthFailStreak = 0;
                    if (interactive)
                    {
                        AppendLog("OK: " + result.Message, LogLevel.Success);
                        ClearFailureAlert();
                    }
                    else
                    {
                        AppendLog("OK: " + result.Message, LogLevel.Verbose, true);
                    }
                    if (settings.LastTokenExpiresInSeconds > 0)
                    {
                        _settings.LastTokenExpiresInSeconds = settings.LastTokenExpiresInSeconds;
                        _settings.Save();
                    }
                    UpdateTokenAgeText();
                    _aosBmw403Fail = false;
                    ClearFailureAlert();
                    SetTokenFooter(true);
                    if (_timerActive)
                        _nextHealthUtc = DateTime.UtcNow.AddHours(1);
                    ApplyPreventiveRenewClamp();
                    UpdateNextLabel();
                    UpdateTrayTooltip();
                }
                else
                {
                    AppendLog("FAIL: " + result.Message, LogLevel.Error);
                    if (RenewEngine.IsTransientDnsFailure(result.Message))
                    {
                        AppendLog(UiStrings.T("HealthDnsNetworkHint"), LogLevel.Info);
                        if (_timerActive)
                            ScheduleDnsHealthRetry();
                    }
                    if (LooksLikeAuthFailure(result.Message))
                        TrySilentBrowserPassRefresh(true);
                    ApplyFailureAlert(result.Message);
                    SetTokenFooter(false);
                }
                RefreshStatusBadge();
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void SendRegisterSyncTo(int clientId)
        {
            if (_settings == null || !_settings.NotifyServerEnabled) return;
            try
            {
                var list = BuildRegisterSyncWireRows(RegisterUnfilteredDisplayMax);
                var payload = NotifyMessage.EncodeRegisterSync(list);
                NotifyServer.Instance.SendTo(clientId, new NotifyMessage
                {
                    Kind = "registerSync",
                    Level = "info",
                    Title = "KAI - AOS Renew",
                    Text = payload ?? ""
                });
            }
            catch (Exception ex)
            {
                try { RenewEngine.Log("Notify registerSync: " + ex.Message); }
                catch { }
            }
        }

    }
}
