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

        private sealed class FirebaseSyncModeItem
        {
            public string Code { get; set; }
            public string Name { get; set; }
        }

        private void EnsureSrvFirebaseSyncModeComboItems()
        {
            if (SrvFirebasePushSyncModeCombo == null) return;
            var prev = SrvFirebasePushSyncModeCombo.SelectedValue as string;
            SrvFirebasePushSyncModeCombo.ItemsSource = new[]
            {
                new FirebaseSyncModeItem { Code = "ondemand", Name = ClientUiStrings.T("FirebasePushSyncModeOnDemand") },
                new FirebaseSyncModeItem { Code = "poll", Name = ClientUiStrings.T("FirebasePushSyncModePoll") }
            };
            if (!string.IsNullOrEmpty(prev)
                && !string.Equals(prev, "sse", StringComparison.OrdinalIgnoreCase))
                SrvFirebasePushSyncModeCombo.SelectedValue = prev;
            else
                SrvFirebasePushSyncModeCombo.SelectedValue = "ondemand";
            UpdateSrvFirebasePollMinutesVisibility();
        }

        private void UpdateSrvFirebasePollMinutesVisibility()
        {
            var poll = SrvFirebasePushSyncModeCombo != null
                && string.Equals(SrvFirebasePushSyncModeCombo.SelectedValue as string, "poll", StringComparison.OrdinalIgnoreCase);
            var vis = poll ? Visibility.Visible : Visibility.Collapsed;
            if (SrvFirebasePushPollMinutesBox != null) SrvFirebasePushPollMinutesBox.Visibility = vis;
            if (SrvFirebasePushPollMinutesSuffix != null) SrvFirebasePushPollMinutesSuffix.Visibility = vis;
        }

        private void ApplyLanguageToUi()
        {
            Title = ClientUiStrings.T("AppTitle");
            if (TitleText != null) TitleText.Text = ClientUiStrings.T("AppTitle");
            if (IpLabel != null) IpLabel.Text = ClientUiStrings.T("Ip");
            if (PortLabel != null) PortLabel.Text = ClientUiStrings.T("Port");
            if (PairingCodeLabel != null)
            {
                PairingCodeLabel.Text = ClientUiStrings.T("PairingCode");
                PairingCodeLabel.ToolTip = ClientUiStrings.T("PairingCodeTip");
            }
            if (PairingCodeBox != null)
                PairingCodeBox.ToolTip = ClientUiStrings.T("PairingCodeTip");
            if (PairingCodePasteBtn != null)
                PairingCodePasteBtn.ToolTip = ClientUiStrings.T("NotifyPairingPasteTip");
            if (ScanLanBtn != null) ScanLanBtn.Content = ClientUiStrings.T("ScanLan");
            try { ApplyShortcutStrings(); }
            catch { }
            UpdateConnectButtonLabel();
            if (DisconnectBtn != null) DisconnectBtn.Content = ClientUiStrings.T("Disconnect");
            if (KeysEmptyHint != null) KeysEmptyHint.Text = ClientUiStrings.T("KeysEmptyHint");
            if (KeysNoMatchesHint != null) KeysNoMatchesHint.Text = ClientUiStrings.T("NoMatchesHint");
            if (ActivityEmptyHint != null) ActivityEmptyHint.Text = ClientUiStrings.T("ActivityEmptyHint");
            if (ActivityNoMatchesHint != null) ActivityNoMatchesHint.Text = ClientUiStrings.T("NoMatchesHint");
            if (RegisterNoMatchesHint != null) RegisterNoMatchesHint.Text = ClientUiStrings.T("NoMatchesHint");
            if (AutoConnectCheck != null) AutoConnectCheck.Content = ClientUiStrings.T("AutoConnect");
            if (TabKeysHeaderText != null) TabKeysHeaderText.Text = ClientUiStrings.T("TabKeys");
            if (TabAosHeaderText != null) TabAosHeaderText.Text = ClientUiStrings.T("TabAos");
            if (TabActivityHeaderText != null) TabActivityHeaderText.Text = ClientUiStrings.T("TabActivity");
            if (TabRegisterHeaderText != null) TabRegisterHeaderText.Text = ClientUiStrings.T("TabRegister");
            ApplyRegisterTabStrings();
            if (TabKaiHeaderText != null) TabKaiHeaderText.Text = TL("TabKai", "KAI");
            if (KaiInstallSection != null) KaiInstallSection.Text = TL("KaiInstall", "Installation:");
            if (KaiUrlsSection != null) KaiUrlsSection.Text = TL("KaiUrls", "Kai Zentral URLs:");
            if (KaiUrlRegisterLabel != null) KaiUrlRegisterLabel.Text = TL("KaiRegister", "Register:");
            if (KaiUrlWebEamLabel != null) KaiUrlWebEamLabel.Text = TL("KaiWebEam", "WebEAM:");
            if (KaiUrlUploadLabel != null) KaiUrlUploadLabel.Text = TL("KaiKeyUpload", "Key upload:");
            if (KaiTokenSection != null) KaiTokenSection.Text = TL("KaiToken", "WebEAM token:");
            if (KaiServicesSection != null) KaiServicesSection.Text = TL("KaiServices", "Services:");
            if (KaiFixServicesBtn != null) KaiFixServicesBtn.Content = TL("KaiFixNow", "Fix now");
            if (KaiStopServicesBtn != null) KaiStopServicesBtn.Content = TL("KaiStopServices", "Restart");
            if (KaiRefreshBtn != null) KaiRefreshBtn.Content = TL("KaiRefresh", "Refresh");
            if (KaiSvcColName != null) KaiSvcColName.Text = TL("KaiSvcName", "Service");
            if (KaiSvcColStatus != null) KaiSvcColStatus.Text = TL("KaiSvcStatus", "Status");
            if (KaiSvcColStart != null) KaiSvcColStart.Text = TL("KaiSvcStart", "Start type");
            RefreshLogFilterComboItems();
            if (TabSettings != null) TabSettings.Header = ClientUiStrings.T("TabSettings");
            if (ActivitySection != null) ActivitySection.Text = ClientUiStrings.T("ActivitySection");
            if (ActivityFilterLabel != null) ActivityFilterLabel.Text = ClientUiStrings.T("RegisterFilter");
            if (ActivitySearchLabel != null) ActivitySearchLabel.Text = ClientUiStrings.T("ActivitySearch");
            if (ActivitySearchBox != null) ActivitySearchBox.ToolTip = ClientUiStrings.T("ActivitySearchTip");
            if (ActivityLoadingHint != null) ActivityLoadingHint.Text = ClientUiStrings.T("LogsLoading");
            if (RegisterSearchLabel != null) RegisterSearchLabel.Text = ClientUiStrings.T("RegisterSearch");
            if (RegisterSearchBox != null) RegisterSearchBox.ToolTip = ClientUiStrings.T("RegisterSearchTip");
            if (RegisterLoadingHint != null) RegisterLoadingHint.Text = ClientUiStrings.T("LogsLoading");
            if (ActivityRefreshBtn != null) ActivityRefreshBtn.Content = ClientUiStrings.T("KeysRefresh");
            if (RegisterRefreshBtn != null) RegisterRefreshBtn.Content = ClientUiStrings.T("KeysRefresh");
            if (ActivityClearBtn != null) ActivityClearBtn.Content = ClientUiStrings.T("ClearLog");
            if (SaveActivityLogBtn != null) SaveActivityLogBtn.Content = ClientUiStrings.T("SaveLog");
            if (SaveRegisterLogBtn != null) SaveRegisterLogBtn.Content = ClientUiStrings.T("SaveLog");
            if (ClearRegisterLogBtn != null) ClearRegisterLogBtn.Content = ClientUiStrings.T("ClearLog");
            if (TabServerSettingsHeaderText != null)
                TabServerSettingsHeaderText.Text = ClientUiStrings.T("TabServerSettings");
            ApplyServerSettingsTabStrings();
            InitSrvLanguageCombo();
            InitSrvThemeCombo();
            InitSrvKrLogLevelCombo();
            if (ActivityColTime != null) ActivityColTime.Header = ClientUiStrings.T("KeysColTime");
            if (ActivityColLevel != null) ActivityColLevel.Header = ClientUiStrings.T("ActivityColLevel");
            if (ActivityColText != null) ActivityColText.Header = ClientUiStrings.T("ActivityColText");
            UpdateAosLoginStatus(_lastTokenMode);
            if (AosIntervalLabel != null) AosIntervalLabel.Text = ClientUiStrings.T("CheckHealthInterval");
            if (AosUserLabel != null) AosUserLabel.Text = ClientUiStrings.T("AosUsername");
            if (AosPassLabel != null) AosPassLabel.Text = ClientUiStrings.T("AosPassword");
            if (AosShowPasswordCheck != null) AosShowPasswordCheck.Content = ClientUiStrings.T("ShowPassword");
            if (AosSaveBtnText != null) AosSaveBtnText.Text = ClientUiStrings.T("Save");
            if (AosPullCloudBtnText != null) AosPullCloudBtnText.Text = ClientUiStrings.T("AosPullFromCloud");
            if (AosCheckBrowserPassBtnText != null) AosCheckBrowserPassBtnText.Text = ClientUiStrings.T("CheckBrowserPass");
            ApplyClipboardPassBannerStrings();
            if (AosOpenBmwBtnText != null) AosOpenBmwBtnText.Text = ClientUiStrings.T("OpenBmwSso");
            UpdateAosPullCloudButtonVisibility();
            if (AosCheckTokenBtn != null) AosCheckTokenBtn.Content = ClientUiStrings.T("CheckToken");
            if (AosStartTimerBtn != null) AosStartTimerBtn.Content = ClientUiStrings.T("StartTimer");
            if (AosStopTimerBtn != null) AosStopTimerBtn.Content = ClientUiStrings.T("StopTimer");
            if (ForceRegisterBtn != null)
            {
                ForceRegisterBtn.Content = ClientUiStrings.T("ForceRegister");
                ForceRegisterBtn.ToolTip = ClientUiStrings.T("TipForceRegister");
            }
            if (AuthGuardsStatusTitle != null)
                AuthGuardsStatusTitle.Text = ClientUiStrings.T("AuthGuardsStatusTitle");
            if (AuthCooldownExplainText != null)
                AuthCooldownExplainText.Text = ClientUiStrings.T("AuthCooldownExplain");
            if (AuthRegisterBlockedExplainText != null)
                AuthRegisterBlockedExplainText.Text = ClientUiStrings.T("AuthRegisterBlockedExplain");
            if (ClearAuthCooldownBtn != null)
            {
                ClearAuthCooldownBtn.Content = ClientUiStrings.T("AuthGuardClear");
                ClearAuthCooldownBtn.ToolTip = ClientUiStrings.T("TipAuthGuardClearCooldown");
            }
            if (ClearAuthRegisterBlockedBtn != null)
            {
                ClearAuthRegisterBlockedBtn.Content = ClientUiStrings.T("AuthGuardClear");
                ClearAuthRegisterBlockedBtn.ToolTip = ClientUiStrings.T("TipAuthGuardClearBlocked");
            }
            string authCdPanel = null;
            if (_authCooldownOn)
            {
                authCdPanel = !string.IsNullOrEmpty(_authCooldownUntilLocal)
                    ? string.Format(ClientUiStrings.T("AuthCooldownStatusActive"), _authCooldownUntilLocal)
                    : ClientUiStrings.T("AuthCooldownStatusActiveShort");
            }
            RefreshAuthGuardsPanel(_authCooldownOn, authCdPanel, _authBlockedOn, null, null);
            if (AuthGuardsFooterText != null && (_authCooldownOn || _authBlockedOn || AuthGuardsFooterText.Inlines.Count > 0))
            {
                AuthGuardsFooterText.Inlines.Clear();
                var cdBrush = _authCooldownOn ? CreateBrush(0xB2, 0x6A, 0x00) : _footerOk;
                var blBrush = _authBlockedOn ? _footerError : _footerOk;
                var cdFooter = _authCooldownOn
                    ? (!string.IsNullOrEmpty(_authCooldownUntilLocal)
                        ? string.Format(ClientUiStrings.T("FooterCooldownOnUntil"), _authCooldownUntilLocal)
                        : ClientUiStrings.T("FooterCooldownOn"))
                    : ClientUiStrings.T("FooterCooldownOff");
                var blFooter = _authBlockedOn
                    ? ClientUiStrings.T("FooterBlockedOn")
                    : ClientUiStrings.T("FooterBlockedOff");
                AuthGuardsFooterText.Inlines.Add(new Run(cdFooter) { Foreground = cdBrush });
                AuthGuardsFooterText.Inlines.Add(new Run("  ·  ") { Foreground = _footerMuted });
                AuthGuardsFooterText.Inlines.Add(new Run(blFooter) { Foreground = blBrush });
            }
            UpdateAosSyncStatus();
            UpdateAosButtonsVisibility();
            if (KeysEventsSection != null) KeysEventsSection.Text = ClientUiStrings.T("KeysRecent");
            ApplyKeysSortHeaders();
            if (KeysColDetail != null) KeysColDetail.Header = ClientUiStrings.T("KeysColDetail");

            if (KeysColBin != null) KeysColBin.Header = ClientUiStrings.T("KeysColBin");
            if (ConnectionGroup != null) ConnectionGroup.Header = ClientUiStrings.T("Connection");
            if (OfflineAfterLabel != null) OfflineAfterLabel.Text = ClientUiStrings.T("OfflineAfter");
            if (GeneralGroup != null) GeneralGroup.Header = ClientUiStrings.T("SettingsGeneral");
            if (LanguageLabel != null) LanguageLabel.Text = ClientUiStrings.T("Language");
            if (ThemeLabel != null) ThemeLabel.Text = ClientUiStrings.T("Theme");
            if (OpenSettingsFolderBtn != null) OpenSettingsFolderBtn.Content = ClientUiStrings.T("AboutOpenSettings");
            if (OpenLogsFolderBtn != null) OpenLogsFolderBtn.Content = ClientUiStrings.T("AboutOpenLogs");
            if (ExportClientSettingsBtn != null) ExportClientSettingsBtn.Content = ClientUiStrings.T("ExportSettings");
            if (ImportClientSettingsBtn != null) ImportClientSettingsBtn.Content = ClientUiStrings.T("ImportSettings");
            if (AuthGroup != null) AuthGroup.Header = ClientUiStrings.T("SettingsAuth");
            if (BrowserPassHintCheck != null) BrowserPassHintCheck.Content = ClientUiStrings.T("BrowserPassHintOption");
            RefreshThemeComboItems();
            if (StartupGroup != null) StartupGroup.Header = ClientUiStrings.T("Startup");
            if (StartWithWindowsCheck != null)
                StartWithWindowsCheck.Content = ClientUiStrings.T("StartWithWindowsYes");
            if (MinimizeToTrayCheck != null)
                MinimizeToTrayCheck.Content = ClientUiStrings.T("MinimizeToTrayYes");
            if (MinimizeOnCloseCheck != null)
                MinimizeOnCloseCheck.Content = CheckboxLabel(ClientUiStrings.T("MinimizeOnClose"));
            if (AutoUpdateCheck != null)
            {
                AutoUpdateCheck.Content = CheckboxLabel(ClientUiStrings.T("AutoUpdate"));
                AutoUpdateCheck.ToolTip = ClientUiStrings.T("TipAutoUpdate");
            }
            if (CheckForUpdatesBtn != null) CheckForUpdatesBtn.Content = ClientUiStrings.T("CheckForUpdates");
            if (_pendingUpdate != null)
                ShowPendingUpdate(_pendingUpdate);
            else if (FooterUpdateBtn != null)
                FooterUpdateBtn.Content = ClientUiStrings.T("Update");
            RefreshVersionLabel();
            RefreshServerUpdateUi();
            if (KeysSettingsGroup != null) KeysSettingsGroup.Header = ClientUiStrings.T("TabKeys");
            if (VinTipDurationLabel != null) VinTipDurationLabel.Text = ClientUiStrings.T("VinTipDuration");
            if (ShowVinTipsCheck != null) ShowVinTipsCheck.Content = ClientUiStrings.T("ShowVinTips");
            if (ShowVinTipsBmwFailOnlyCheck != null)
                ShowVinTipsBmwFailOnlyCheck.Content = ClientUiStrings.T("ShowVinTipsBmwFailOnly");
            SyncShowVinTipsBmwFailOnlyUi();
            if (AutoCopyVinCheck != null) AutoCopyVinCheck.Content = ClientUiStrings.T("AutoCopyVin");
            TrayTipWindow.CopyVinLabel = ClientUiStrings.T("CopyVin");
            if (ShowAlertTipsCheck != null) ShowAlertTipsCheck.Content = ClientUiStrings.T("ShowAlertTips");
            if (TestBalloonTipsBtn != null) TestBalloonTipsBtn.Content = ClientUiStrings.T("TrayTestTips");
            if (ClientSimulationGroup != null) ClientSimulationGroup.Header = ClientUiStrings.T("SimGroup");
            if (SimBmwFailBtn != null) SimBmwFailBtn.Content = ClientUiStrings.T("SimBmwFail");
            if (SimBmwFailAgainBtn != null) SimBmwFailAgainBtn.Content = ClientUiStrings.T("SimBmwFailAgain");
            if (SimBmwOkBtn != null) SimBmwOkBtn.Content = ClientUiStrings.T("SimBmwOk");
            if (SimVinReadBtn != null) SimVinReadBtn.Content = ClientUiStrings.T("SimVinRead");
            if (SimVinStickyBtn != null) SimVinStickyBtn.Content = ClientUiStrings.T("SimVinSticky");
            if (SimAltVinFailBtn != null) SimAltVinFailBtn.Content = ClientUiStrings.T("SimAltVinFail");
            if (SimWarningOverStickyBtn != null) SimWarningOverStickyBtn.Content = ClientUiStrings.T("SimWarningOverSticky");
            if (Sim403StillFailBtn != null) Sim403StillFailBtn.Content = ClientUiStrings.T("Sim403StillFail");
            if (SimLocalOkBtn != null) SimLocalOkBtn.Content = ClientUiStrings.T("SimLocalOkRow");
            if (SimBmwUploadedRowBtn != null) SimBmwUploadedRowBtn.Content = ClientUiStrings.T("SimBmwUploadedRow");
            if (SimRetryBusyBtn != null) SimRetryBusyBtn.Content = ClientUiStrings.T("SimRetryBusy");
            if (SimStallFailOnlyBtn != null) SimStallFailOnlyBtn.Content = ClientUiStrings.T("SimStallFailOnly");
            if (SimStallBtn != null) SimStallBtn.Content = ClientUiStrings.T("SimStall");
            if (SimToastInfoBtn != null) SimToastInfoBtn.Content = ClientUiStrings.T("SimToastInfo");
            if (SimToastWarningBtn != null) SimToastWarningBtn.Content = ClientUiStrings.T("SimToastWarning");
            if (SimToastErrorBtn != null) SimToastErrorBtn.Content = ClientUiStrings.T("SimToastError");
            if (SimServerOfflineBtn != null) SimServerOfflineBtn.Content = ClientUiStrings.T("SimServerOffline");
            if (SimAuthCooldownBtn != null) SimAuthCooldownBtn.Content = ClientUiStrings.T("SimAuthCooldown");
            if (SimRegisterBlockedBtn != null) SimRegisterBlockedBtn.Content = ClientUiStrings.T("SimRegisterBlocked");
            if (SimClipboardPassBtn != null) SimClipboardPassBtn.Content = ClientUiStrings.T("SimClipboardPass");
            if (SimServicesProblemBtn != null) SimServicesProblemBtn.Content = ClientUiStrings.T("SimServicesProblem");
            if (SimUpdateAvailableBtn != null) SimUpdateAvailableBtn.Content = ClientUiStrings.T("SimUpdateAvailable");
            if (SimPeerConflictBtn != null) SimPeerConflictBtn.Content = ClientUiStrings.T("SimPeerConflict");
            if (SimBinAvailableBtn != null) SimBinAvailableBtn.Content = ClientUiStrings.T("SimBinAvailable");
            if (SimRegisterOkBtn != null) SimRegisterOkBtn.Content = ClientUiStrings.T("SimRegisterOk");
            if (SimRegisterFailBtn != null) SimRegisterFailBtn.Content = ClientUiStrings.T("SimRegisterFail");
            if (SimCloseTipBtn != null) SimCloseTipBtn.Content = ClientUiStrings.T("SimCloseTip");
            if (KeysRefreshBtn != null) KeysRefreshBtn.Content = ClientUiStrings.T("KeysRefresh");
            if (KeysClearBtn != null) KeysClearBtn.Content = ClientUiStrings.T("ClearKeys");
            if (KeysExportBtn != null)
            {
                KeysExportBtn.Content = ClientUiStrings.T("KeysExport");
                KeysExportBtn.ToolTip = ClientUiStrings.T("KeysExportTip");
            }
            if (KeysRetryBmwBtn != null)
            {
                KeysRetryBmwBtn.Content = ClientUiStrings.T("KeysRetryBmw");
                KeysRetryBmwBtn.ToolTip = ClientUiStrings.T("KeysRetryBmwTip");
            }
            if (KeysFilterLabel != null) KeysFilterLabel.Text = ClientUiStrings.T("KeysFilter");
            if (KeysSearchLabel != null) KeysSearchLabel.Text = ClientUiStrings.T("KeysSearch");
            if (KeysSearchBox != null) KeysSearchBox.ToolTip = ClientUiStrings.T("KeysSearchTip");
            InitKeysFilterCombo();
            if (AboutTabBtn != null) AboutTabBtn.Content = ClientUiStrings.T("TabAbout");
            if (AboutTitleText != null) AboutTitleText.Text = ClientUiStrings.T("AppTitle");
            if (AboutVersionText != null)
                AboutVersionText.Text = string.Format(ClientUiStrings.T("Version"), VersionDisplay);
            if (DebugModeText != null)
            {
                DebugModeText.Text = ClientUiStrings.T("DebugModeOn");
                DebugModeText.Visibility = _debugMode ? Visibility.Visible : Visibility.Collapsed;
            }
            if (AboutChangelogLabel != null)
                AboutChangelogLabel.Text = ClientUiStrings.T("AboutChangelogLabel");
            if (_aboutVisible)
                LoadAboutChangelog();
            UpdateKeysTodayBadge();
            if (!_hasServerFooter)
                ShowDisconnectedFooter();
        }

        private void AboutVersionText_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var now = DateTime.UtcNow;
            if ((now - _aboutVersionClickUtc).TotalSeconds > 2)
                _aboutVersionClicks = 0;
            _aboutVersionClickUtc = now;
            _aboutVersionClicks++;
            if (_aboutVersionClicks < 3) return;
            _aboutVersionClicks = 0;
            _debugMode = !_debugMode;
            PersistDebugMode();
            ApplyDebugModeUi();
        }

        private void RequestServerSettingsSync()
        {
            if (_client == null || !_client.IsConnected) return;
            try
            {
                _client.Send(new NotifyMessage
                {
                    Kind = "settingsSyncReq",
                    Level = "info",
                    Title = "KAI - AOS Renew - Client",
                    Text = ""
                });
            }
            catch { }
        }

        private static void ApplyStartup(bool enable)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(ClientSettings.RunKeyPath, true))
                {
                    if (key == null) return;
                    var exe = Process.GetCurrentProcess().MainModule.FileName;
                    if (enable)
                        key.SetValue(ClientSettings.RunValueName, "\"" + exe + "\" --minimized");
                    else if (key.GetValue(ClientSettings.RunValueName) != null)
                        key.DeleteValue(ClientSettings.RunValueName, false);
                }
            }
            catch { }
        }

        private void AboutTabBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_aboutVisible)
                return;
            ShowAboutPanel();
        }

        private void ShowAboutPanel()
        {
            if (MainTabs != null)
                _tabIndexBeforeAbout = MainTabs.SelectedIndex;
            _aboutVisible = true;
            if (AboutPanel != null) AboutPanel.Visibility = Visibility.Visible;
            if (AboutTabBtn != null) AboutTabBtn.Tag = "Selected";
            if (TabDummy != null)
            {
                _suppressTabChange = true;
                try { TabDummy.IsSelected = true; }
                finally { _suppressTabChange = false; }
            }
            LoadAboutChangelog();
            UpdateAosButtonsVisibility();
        }

        private void HideAboutPanel()
        {
            _aboutVisible = false;
            if (AboutPanel != null) AboutPanel.Visibility = Visibility.Collapsed;
            if (AboutTabBtn != null) AboutTabBtn.Tag = null;
            if (MainTabs != null && MainTabs.Items.Count > 0)
            {
                _suppressTabChange = true;
                try
                {
                    var idx = _tabIndexBeforeAbout;
                    if (idx < 0 || idx >= MainTabs.Items.Count ||
                        (TabDummy != null && ReferenceEquals(MainTabs.Items[idx], TabDummy)))
                        idx = GetDefaultTabIndex();
                    if (idx < 0) idx = GetDefaultTabIndex();
                    MainTabs.SelectedIndex = idx;
                }
                finally { _suppressTabChange = false; }
            }
            UpdateAosButtonsVisibility();
        }

        private void LoadAboutChangelog()
        {
            if (AboutChangelogBox == null) return;
            try
            {
                var path = FindChangelogPath();
                if (path != null && File.Exists(path))
                    AboutChangelogBox.Text = FormatChangelogForAbout(File.ReadAllText(path));
                else
                    AboutChangelogBox.Text = ClientUiStrings.T("AboutChangelogMissing");
            }
            catch (Exception ex)
            {
                AboutChangelogBox.Text = ex.Message;
            }
        }

        private static string FormatChangelogForAbout(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw ?? "";
            var lines = raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var outLines = new List<string>();
            var skippingPreamble = true;
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trim = (line ?? "").Trim();
                if (skippingPreamble)
                {
                    if (trim.Length == 0) continue;
                    if (trim.StartsWith("Format:", StringComparison.OrdinalIgnoreCase)) continue;
                    if (trim.StartsWith("# Changelog", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(trim, "## [Unreleased]", StringComparison.OrdinalIgnoreCase)) continue;
                    skippingPreamble = false;
                }
                outLines.Add(line);
            }
            return string.Join(Environment.NewLine, outLines.ToArray()).TrimStart();
        }

        private void LoadSettingsToUi()
        {
            if (_settings == null) return;
            if (StartWithWindowsCheck != null)
                StartWithWindowsCheck.IsChecked = _settings.StartWithWindows;
            if (MinimizeToTrayCheck != null)
                MinimizeToTrayCheck.IsChecked = _settings.MinimizeToTray;
            if (MinimizeOnCloseCheck != null)
                MinimizeOnCloseCheck.IsChecked = _settings.MinimizeOnClose;
            if (AutoUpdateCheck != null)
                AutoUpdateCheck.IsChecked = _settings.AutoUpdate;
            if (VinTipSecondsBox != null)
                VinTipSecondsBox.Text = _settings.VinTipSeconds.ToString();
            if (ShowVinTipsCheck != null)
                ShowVinTipsCheck.IsChecked = _settings.ShowVinTips;
            if (ShowVinTipsBmwFailOnlyCheck != null)
            {
                ShowVinTipsBmwFailOnlyCheck.IsChecked = _settings.ShowVinTips && _settings.ShowVinTipsBmwFailOnly;
                SyncShowVinTipsBmwFailOnlyUi();
            }
            if (AutoCopyVinCheck != null)
                AutoCopyVinCheck.IsChecked = _settings.AutoCopyVin;
            if (ShowAlertTipsCheck != null)
                ShowAlertTipsCheck.IsChecked = _settings.ShowAlertTips;
            if (BrowserPassHintCheck != null)
                BrowserPassHintCheck.IsChecked = _settings.BrowserPasswordHintOnAuthFail;
            if (OfflineMinutesBox != null)
                OfflineMinutesBox.Text = _settings.OfflineMinutes.ToString();
            if (LanguageCombo != null)
            {
                var lang = string.IsNullOrWhiteSpace(_settings.Language) ? "en" : _settings.Language;
                LanguageCombo.SelectedValue = lang;
                if (LanguageCombo.SelectedItem == null)
                    LanguageCombo.SelectedValue = "en";
            }
            if (ThemeCombo != null)
            {
                var theme = ThemeManager.NormalizeMode(_settings.Theme);
                ThemeCombo.SelectedValue = theme;
                if (ThemeCombo.SelectedItem == null)
                    ThemeCombo.SelectedValue = "system";
            }
        }

        private void InitLanguageCombo()
        {
            if (LanguageCombo == null) return;
            var items = new List<LangItem>
            {
                new LangItem { Name = "Chinese", Code = "zh" },
                new LangItem { Name = "Czech", Code = "cs" },
                new LangItem { Name = "Danish", Code = "da" },
                new LangItem { Name = "Dutch", Code = "nl" },
                new LangItem { Name = "English UK", Code = "en" },
                new LangItem { Name = "French", Code = "fr" },
                new LangItem { Name = "German", Code = "de" },
                new LangItem { Name = "Italian", Code = "it" },
                new LangItem { Name = "Japanese", Code = "ja" },
                new LangItem { Name = "Korean", Code = "ko" },
                new LangItem { Name = "Norwegian", Code = "no" },
                new LangItem { Name = "Polish", Code = "pl" },
                new LangItem { Name = "Portugal", Code = "pt" },
                new LangItem { Name = "Romanian", Code = "ro" },
                new LangItem { Name = "Russian", Code = "ru" },
                new LangItem { Name = "Spanish", Code = "es" },
                new LangItem { Name = "Swedish", Code = "sv" },
                new LangItem { Name = "Thai", Code = "th" },
                new LangItem { Name = "Turkish", Code = "tr" }
            };
            items.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            LanguageCombo.ItemsSource = items;
        }

        private void InitThemeCombo()
        {
            RefreshThemeComboItems();
        }

        private void RefreshThemeComboItems()
        {
            if (ThemeCombo == null) return;
            var selected = ThemeCombo.SelectedValue as string;
            ThemeCombo.ItemsSource = new List<LangItem>
            {
                new LangItem { Name = ClientUiStrings.T("ThemeSystem"), Code = "system" },
                new LangItem { Name = ClientUiStrings.T("ThemeDay"), Code = "day" },
                new LangItem { Name = ClientUiStrings.T("ThemeNight"), Code = "night" }
            };
            if (!string.IsNullOrEmpty(selected))
                ThemeCombo.SelectedValue = selected;
        }

        private void CaptureSettingsFromUi()
        {
            if (_settings == null) return;
            if (StartWithWindowsCheck != null)
                _settings.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
            if (MinimizeToTrayCheck != null)
                _settings.MinimizeToTray = MinimizeToTrayCheck.IsChecked == true;
            if (MinimizeOnCloseCheck != null)
                _settings.MinimizeOnClose = MinimizeOnCloseCheck.IsChecked == true;
            if (AutoUpdateCheck != null)
                _settings.AutoUpdate = AutoUpdateCheck.IsChecked == true;
            if (ShowVinTipsCheck != null)
                _settings.ShowVinTips = ShowVinTipsCheck.IsChecked == true;
            SyncShowVinTipsBmwFailOnlyUi();
            if (ShowVinTipsBmwFailOnlyCheck != null && ShowVinTipsBmwFailOnlyCheck.IsEnabled)
                _settings.ShowVinTipsBmwFailOnly = ShowVinTipsBmwFailOnlyCheck.IsChecked == true;
            else
                _settings.ShowVinTipsBmwFailOnly = false;
            if (AutoCopyVinCheck != null && AutoCopyVinCheck.IsEnabled)
                _settings.AutoCopyVin = AutoCopyVinCheck.IsChecked == true;
            else
                _settings.AutoCopyVin = false;
            if (ShowAlertTipsCheck != null)
                _settings.ShowAlertTips = ShowAlertTipsCheck.IsChecked == true;
            if (BrowserPassHintCheck != null)
                _settings.BrowserPasswordHintOnAuthFail = BrowserPassHintCheck.IsChecked == true;
            int sec = 10;
            if (VinTipSecondsBox == null || !int.TryParse((VinTipSecondsBox.Text ?? "").Trim(), out sec)
                || (sec != 0 && sec < 3))
                sec = 10;
            if (sec > 60) sec = 60;
            if (VinTipSecondsBox != null)
                VinTipSecondsBox.Text = sec.ToString();
            _settings.VinTipSeconds = sec;
            int off = 2;
            if (OfflineMinutesBox == null || !int.TryParse((OfflineMinutesBox.Text ?? "").Trim(), out off))
                off = 2;
            _settings.OfflineMinutes = off;
            var lang = LanguageCombo != null ? LanguageCombo.SelectedValue as string : null;
            if (!string.IsNullOrWhiteSpace(lang))
                _settings.Language = lang;
            var theme = ThemeCombo != null ? ThemeCombo.SelectedValue as string : null;
            if (!string.IsNullOrWhiteSpace(theme))
                _settings.Theme = theme;
            CaptureConnectionFields();
            _settings.Normalize();
        }

        private void PersistSettingsFromUi()
        {
            if (_loadingUi || _settings == null) return;
            var prevLang = _settings.Language;
            var prevTheme = _settings.Theme;
            CaptureSettingsFromUi();
            _settings.Save();
            ApplyStartup(_settings.StartWithWindows);
            if (!string.Equals(prevLang, _settings.Language, StringComparison.OrdinalIgnoreCase))
            {
                ClientUiStrings.SetLanguage(_settings.Language);
                ApplyLanguageToUi();
                RebuildTrayMenu();
            }
            if (!string.Equals(prevTheme, _settings.Theme, StringComparison.OrdinalIgnoreCase))
            {
                ThemeManager.Apply(_settings.Theme);
                ApplyFooterChrome(ThemeManager.IsNight);
            }
            RefreshTrayStatus();
        }

        private void SettingsCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (!_loadingUi)
                SyncShowVinTipsBmwFailOnlyUi();
            PersistSettingsFromUi();
        }

        private void SettingsField_Changed(object sender, RoutedEventArgs e)
        {
            PersistSettingsFromUi();
        }

        private void OpenSettingsFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(ClientSettings.StoreDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(ClientUiStrings.T("CannotOpenLog"), ex.Message),
                    ClientUiStrings.T("AppTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OpenLogsFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(ClientSettings.LogDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(ClientUiStrings.T("CannotOpenLog"), ex.Message),
                    ClientUiStrings.T("AppTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ExportClientSettings_Click(object sender, RoutedEventArgs e)
        {
            if (_settings == null) return;
            try
            {
                CaptureSettingsFromUi();
                var dlg = new SaveFileDialog
                {
                    Filter = "XML (*.xml)|*.xml|All files (*.*)|*.*",
                    FileName = "KaiAosNotifyClient-settings.xml",
                    Title = ClientUiStrings.T("ClientExportTitle")
                };
                if (dlg.ShowDialog(this) != true) return;
                _settings.ExportPortable(dlg.FileName);
                AppendActivityRow(null, "success", string.Format(ClientUiStrings.T("ClientExportOk"), dlg.FileName));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(ClientUiStrings.T("ExportFail"), ex.Message),
                    ClientUiStrings.T("AppTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ImportClientSettings_Click(object sender, RoutedEventArgs e)
        {
            if (_settings == null) return;
            try
            {
                var dlg = new OpenFileDialog
                {
                    Filter = "XML (*.xml)|*.xml|All files (*.*)|*.*",
                    Title = ClientUiStrings.T("ClientImportTitle")
                };
                if (dlg.ShowDialog(this) != true) return;
                var xml = File.ReadAllText(dlg.FileName);
                if (string.IsNullOrWhiteSpace(xml))
                {
                    MessageBox.Show(this, string.Format(ClientUiStrings.T("ImportFail"), "empty"),
                        ClientUiStrings.T("AppTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                _settings.ImportPortableFromString(xml);
                _settings.Save();
                _loadingUi = true;
                try
                {
                    LoadSettingsToUi();
                    ApplyConnectionUi();
                }
                finally
                {
                    _loadingUi = false;
                }
                ApplyStartup(_settings.StartWithWindows);
                ClientUiStrings.SetLanguage(_settings.Language);
                ApplyLanguageToUi();
                RebuildTrayMenu();
                ThemeManager.Apply(_settings.Theme);
                ApplyFooterChrome(ThemeManager.IsNight);
                AppendActivityRow(null, "success", string.Format(ClientUiStrings.T("ClientImportOk"), dlg.FileName));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(ClientUiStrings.T("ImportFail"), ex.Message),
                    ClientUiStrings.T("AppTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            PersistSettingsFromUi();
        }

        private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            PersistSettingsFromUi();
        }

        private void DiscardUnsavedServerSettings()
        {
            if (_lastServerSettingsMap == null || _lastServerSettingsMap.Count == 0)
            {
                MarkSrvSettingsDirty(false);
                return;
            }
            ApplyServerSettingsSync(NotifyMessage.EncodeKvMap(_lastServerSettingsMap));
        }

        /// <summary>
        /// If Server Settings has unsaved edits, prompt Save / Discard.
        /// Returns true when the caller may continue (tab leave / window close).
        /// Always recomputes dirty from Capture vs Capture-shaped baseline (not the dirty flag alone).
        /// </summary>
        private bool TryResolveUnsavedServerSettings()
        {
            var current = CaptureServerSettingsMap();
            var differs = _lastServerSettingsCapture != null
                && !ServerSettingsCaptureMatchesBaseline(_lastServerSettingsCapture, current);
            if (!differs)
            {
                MarkSrvSettingsDirty(false);
                return true;
            }
            MarkSrvSettingsDirty(true);
            var diff = BuildServerSettingsDiff();
            if (diff == null || diff.Count == 0)
            {
                // Capture differs but no displayable rows — still treat as dirty for safety.
                diff = new List<ServerSettingsDiffRow>
                {
                    new ServerSettingsDiffRow
                    {
                        Key = "settings",
                        Label = ClientUiStrings.T("TabSettings"),
                        OldDisplay = "",
                        NewDisplay = ClientUiStrings.T("ServerSettingsDirty")
                    }
                };
            }
            var leave = UnsavedServerSettingsDialog.Show(this, diff);
            if (leave == UnsavedSettingsChoice.Save)
            {
                SrvSettingsSaveCore(skipConfirm: true);
                return !_srvSettingsDirty;
            }
            DiscardUnsavedServerSettings();
            return true;
        }

        private void SrvDownloadSettingsFolder_Click(object sender, RoutedEventArgs e)
        {
            SendDownloadCmd("getZip|settings");
        }

        private void SrvDownloadLogsFolder_Click(object sender, RoutedEventArgs e)
        {
            SendDownloadCmd("getZip|logs");
        }

        private static string FormatBrowserSavedWhen(DateTime? local)
        {
            if (!local.HasValue)
                return "?";
            var age = DateTime.Now - local.Value;
            if (age < TimeSpan.Zero)
                age = TimeSpan.Zero;
            return string.Format(ClientUiStrings.T("TimeAgo"), FormatAge(age));
        }

        private void ExportLogForDay(string filePrefix, string title, Func<DateTime, string> collector)
        {
            var dlg = new SaveLogDialog(title, DateTime.Today, ClientUiStrings.T) { Owner = this };
            if (dlg.ShowDialog() != true || !dlg.Confirmed) return;
            var day = dlg.SelectedDay.Date;
            string text;
            try { text = collector(day) ?? ""; }
            catch (Exception ex)
            {
                AppendActivityRow(null, "error", ex.Message);
                return;
            }
            if (string.IsNullOrWhiteSpace(text))
            {
                AppendActivityRow(null, "warning", ClientUiStrings.T("SaveLogEmpty"));
                return;
            }
            if (dlg.ToClipboard)
            {
                try
                {
                    Clipboard.SetText(text);
                    AppendActivityRow(null, "success", string.Format(ClientUiStrings.T("SaveLogCopied"),
                        SaveLogDialog.FormatDayFileName(filePrefix, day)));
                }
                catch (Exception ex)
                {
                    AppendActivityRow(null, "error", ex.Message);
                }
                return;
            }
            var path = SaveLogDialog.PromptSavePath(this, SaveLogDialog.FormatDayFileName(filePrefix, day));
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                File.WriteAllText(path, text, Encoding.UTF8);
                AppendActivityRow(null, "success", string.Format(ClientUiStrings.T("SaveLogSaved"), path));
            }
            catch (Exception ex)
            {
                AppendActivityRow(null, "error", ex.Message);
            }
        }

        private Brush ThemeBrush(string key, Brush fallback)
        {
            try
            {
                var b = TryFindResource(key) as Brush;
                if (b != null) return b;
            }
            catch { }
            return fallback;
        }

        private void ApplyServerSettingsTabStrings()
        {
            if (SrvGeneralGroup != null) SrvGeneralGroup.Header = ClientUiStrings.T("SettingsGeneral");
            if (SrvStartupGroup != null) SrvStartupGroup.Header = ClientUiStrings.T("SettingsStartup");
            if (SrvKeysGroup != null) SrvKeysGroup.Header = ClientUiStrings.T("SettingsKeys");
            if (SrvAuthGroup != null) SrvAuthGroup.Header = ClientUiStrings.T("SettingsAuth");
            if (SrvNotifyGroup != null) SrvNotifyGroup.Header = ClientUiStrings.T("SettingsNotifyGroup");
            if (SrvPathsGroup != null) SrvPathsGroup.Header = ClientUiStrings.T("SettingsPaths");
            if (SrvLanguageLabel != null) SrvLanguageLabel.Text = ClientUiStrings.T("Language");
            if (SrvThemeLabel != null) SrvThemeLabel.Text = ClientUiStrings.T("Theme");
            if (SrvStartWithWindowsCheck != null)
                SrvStartWithWindowsCheck.Content = ClientUiStrings.T("StartWithWindowsYes");
            if (SrvMinimizeToTrayCheck != null)
                SrvMinimizeToTrayCheck.Content = ClientUiStrings.T("MinimizeToTrayYes");
            if (SrvMinimizeOnCloseCheck != null)
                SrvMinimizeOnCloseCheck.Content = CheckboxLabel(ClientUiStrings.T("MinimizeOnClose"));
            if (SrvAutoUpdateCheck != null)
                SrvAutoUpdateCheck.Content = CheckboxLabel(ClientUiStrings.T("AutoUpdate"));
            if (SrvKeysRefreshLabel != null) SrvKeysRefreshLabel.Text = ClientUiStrings.T("KeysRefreshSeconds");
            if (SrvKeysVinTipLabel != null) SrvKeysVinTipLabel.Text = ClientUiStrings.T("KeysVinTipSeconds");
            if (SrvKeysStallLabel != null) SrvKeysStallLabel.Text = ClientUiStrings.T("KeysStallMinutes");
            if (SrvKeysBalloonCheck != null) SrvKeysBalloonCheck.Content = ClientUiStrings.T("KeysBalloonOption");
            if (SrvKeysVinBmwFailOnlyCheck != null) SrvKeysVinBmwFailOnlyCheck.Content = ClientUiStrings.T("KeysVinBmwFailOnly");
            if (SrvKeysAutoCopyCheck != null) SrvKeysAutoCopyCheck.Content = ClientUiStrings.T("KeysAutoCopyVin");
            if (SrvKeysStallAlertCheck != null) SrvKeysStallAlertCheck.Content = ClientUiStrings.T("KeysStallAlert");
            SyncSrvKeysVinTipUi();
            if (SrvBrowserPassHintCheck != null) SrvBrowserPassHintCheck.Content = ClientUiStrings.T("BrowserPassHintOption");
            if (SrvUseSystemProxyCheck != null) SrvUseSystemProxyCheck.Content = ClientUiStrings.T("UseSystemProxy");
            if (SrvAuthCooldownLabel != null) SrvAuthCooldownLabel.Text = ClientUiStrings.T("AuthCooldownMinutes");
            if (SrvAuthBlockAfterLabel != null) SrvAuthBlockAfterLabel.Text = ClientUiStrings.T("AuthRegisterBlockAfter");
            if (SrvNotifyEnabledCheck != null) SrvNotifyEnabledCheck.Content = ClientUiStrings.T("NotifyServerEnable");
            if (SrvNotifyPortLabel != null) SrvNotifyPortLabel.Text = ClientUiStrings.T("NotifyServerPort");
            if (SrvNotifyIpLabel != null) SrvNotifyIpLabel.Text = ClientUiStrings.T("NotifyServerIpLabel");
            if (SrvFirebasePushGroup != null) SrvFirebasePushGroup.Header = ClientUiStrings.T("SettingsFirebasePushGroup");
            if (SrvFirebasePushEnableCheck != null) SrvFirebasePushEnableCheck.Content = ClientUiStrings.T("FirebasePushEnable");
            if (SrvFirebasePushSyncModeLabel != null)
                SrvFirebasePushSyncModeLabel.Text = ClientUiStrings.T("FirebasePushSyncMode");
            if (SrvFirebasePushSyncModeCombo != null)
                SrvFirebasePushSyncModeCombo.ToolTip = ClientUiStrings.T("FirebasePushSyncModeTip");
            EnsureSrvFirebaseSyncModeComboItems();
            if (SrvFirebasePushPollMinutesSuffix != null)
                SrvFirebasePushPollMinutesSuffix.Text = ClientUiStrings.T("FirebasePushPollMinutesSuffix");
            if (SrvFirebasePushUrlLabel != null) SrvFirebasePushUrlLabel.Text = ClientUiStrings.T("FirebasePushUrl");
            if (SrvFirebasePushUrlCopyBtn != null)
                SrvFirebasePushUrlCopyBtn.ToolTip = ClientUiStrings.T("FirebasePushUrlCopyTip");
            if (SrvFirebasePushUrlPasteBtn != null)
                SrvFirebasePushUrlPasteBtn.ToolTip = ClientUiStrings.T("FirebasePushUrlPasteTip");
            if (SrvFirebasePushSecretLabel != null)
            {
                SrvFirebasePushSecretLabel.Text = ClientUiStrings.T("FirebasePushSecret");
                SrvFirebasePushSecretLabel.ToolTip = ClientUiStrings.T("FirebasePushSecretTip");
            }
            if (SrvFirebasePushSecretBox != null)
                SrvFirebasePushSecretBox.ToolTip = ClientUiStrings.T("FirebasePushSecretTip");
            if (SrvFirebasePushSecretPlainBox != null)
                SrvFirebasePushSecretPlainBox.ToolTip = ClientUiStrings.T("FirebasePushSecretTip");
            if (SrvFirebasePushShowSecretCheck != null)
                SrvFirebasePushShowSecretCheck.Content = ClientUiStrings.T("ShowPassword");
            if (SrvFirebasePushGenerateBtn != null)
            {
                SrvFirebasePushGenerateBtn.Content = ClientUiStrings.T("FirebasePushGenerate");
                SrvFirebasePushGenerateBtn.ToolTip = ClientUiStrings.T("FirebasePushGenerateTip");
            }
            if (SrvFirebasePushCopyBtn != null)
                SrvFirebasePushCopyBtn.ToolTip = ClientUiStrings.T("FirebasePushCopyTip");
            if (SrvFirebasePushSecretPasteBtn != null)
                SrvFirebasePushSecretPasteBtn.ToolTip = ClientUiStrings.T("FirebasePushSecretPasteTip");
            if (SrvFirebasePushServerNameLabel != null)
                SrvFirebasePushServerNameLabel.Text = ClientUiStrings.T("FirebasePushServerName");
            if (SrvFirebasePushNowBtn != null) SrvFirebasePushNowBtn.Content = ClientUiStrings.T("FirebasePushNow");
            if (SrvFirebasePushPushBtn != null)
            {
                SrvFirebasePushPushBtn.Content = ClientUiStrings.T("FirebasePushPush");
                SrvFirebasePushPushBtn.ToolTip = ClientUiStrings.T("FirebasePushPushTip");
            }
            if (SrvFirebasePushTestBtn != null) SrvFirebasePushTestBtn.Content = ClientUiStrings.T("FirebasePushTest");
            if (SrvPathsKaiConfig != null) SrvPathsKaiConfig.Text = ClientUiStrings.T("SectionConfig");
            if (SrvConfigPathLabel != null) SrvConfigPathLabel.Text = ClientUiStrings.T("ConfigPath");
            if (SrvPathsKeyReader != null) SrvPathsKeyReader.Text = ClientUiStrings.T("SettingsKeyReader");
            if (SrvDataBinLabel != null) SrvDataBinLabel.Text = ClientUiStrings.T("KeyReaderDataBin");
            if (SrvKrLogPathLabel != null) SrvKrLogPathLabel.Text = ClientUiStrings.T("KeyReaderLogPath");
            if (SrvEncPathLabel != null) SrvEncPathLabel.Text = ClientUiStrings.T("KeyReaderEncPath");
            if (SrvKrLogLevelLabel != null) SrvKrLogLevelLabel.Text = ClientUiStrings.T("KeyReaderLogLevel");
            if (SrvPathsKaiData != null) SrvPathsKaiData.Text = ClientUiStrings.T("SettingsKaiData");
            if (SrvUploaderLogLabel != null) SrvUploaderLogLabel.Text = ClientUiStrings.T("KeysUploaderLogDir");
            if (SrvKaiLogLabel != null) SrvKaiLogLabel.Text = ClientUiStrings.T("KeysKaiLogDir");
            if (SrvSqliteLabel != null) SrvSqliteLabel.Text = ClientUiStrings.T("KeysSqlitePath");
            if (SrvExportBtn != null) SrvExportBtn.Content = ClientUiStrings.T("ExportSettings");
            if (SrvImportBtn != null) SrvImportBtn.Content = ClientUiStrings.T("ImportSettings");
            if (SrvSettingsSaveBtn != null) SrvSettingsSaveBtn.Content = ClientUiStrings.T("ServerSettingsSave");
            if (SrvSettingsResetBtn != null) SrvSettingsResetBtn.Content = ClientUiStrings.T("ServerSettingsReset");
            if (SrvDownloadSettingsFolderBtn != null)
                SrvDownloadSettingsFolderBtn.Content = ClientUiStrings.T("SrvDownloadSettingsFolder");
            if (SrvDownloadLogsFolderBtn != null)
                SrvDownloadLogsFolderBtn.Content = ClientUiStrings.T("SrvDownloadLogsFolder");
            if (SrvDownloadKeyDebugFolderBtn != null)
                SrvDownloadKeyDebugFolderBtn.Content = ClientUiStrings.T("SrvDownloadKeyDebugFolder");
            if (SrvNotifySendToLabel != null) SrvNotifySendToLabel.Text = ClientUiStrings.T("NotifySendTo");
            if (SrvNotifyRefreshBtn != null) SrvNotifyRefreshBtn.Content = ClientUiStrings.T("NotifyRefreshClients");
            if (SrvNotifyTestVinBtn != null) SrvNotifyTestVinBtn.Content = ClientUiStrings.T("NotifyTestVin");
            if (SrvNotifyTestInfoBtn != null) SrvNotifyTestInfoBtn.Content = ClientUiStrings.T("NotifyTestInfo");
            if (SrvNotifyTestWarningBtn != null) SrvNotifyTestWarningBtn.Content = ClientUiStrings.T("NotifyTestWarning");
            if (SrvNotifyTestErrorBtn != null) SrvNotifyTestErrorBtn.Content = ClientUiStrings.T("NotifyTestError");
            if (SrvUpdateAllClientsBtn != null)
            {
                SrvUpdateAllClientsBtn.Content = ClientUiStrings.T("UpdateAllClients");
                SrvUpdateAllClientsBtn.ToolTip = ClientUiStrings.T("TipUpdateAllClients");
            }
            InitSrvKrLogLevelCombo();
            InitSrvThemeCombo();
        }

        private void InitSrvLanguageCombo()
        {
            if (SrvLanguageCombo == null) return;
            SrvLanguageCombo.ItemsSource = LanguageCombo != null ? LanguageCombo.ItemsSource : null;
        }

        private void InitSrvThemeCombo()
        {
            if (SrvThemeCombo == null) return;
            var selected = SrvThemeCombo.SelectedValue as string;
            SrvThemeCombo.ItemsSource = new List<LangItem>
            {
                new LangItem { Name = ClientUiStrings.T("ThemeSystem"), Code = "system" },
                new LangItem { Name = ClientUiStrings.T("ThemeDay"), Code = "day" },
                new LangItem { Name = ClientUiStrings.T("ThemeNight"), Code = "night" }
            };
            if (!string.IsNullOrEmpty(selected))
                SrvThemeCombo.SelectedValue = selected;
        }

        private void InitSrvKrLogLevelCombo()
        {
            if (SrvKrLogLevelCombo == null) return;
            var selected = SrvKrLogLevelCombo.SelectedValue as string;
            SrvKrLogLevelCombo.ItemsSource = new List<LangItem>
            {
                new LangItem { Name = ClientUiStrings.T("KeyReaderLog0"), Code = "0" },
                new LangItem { Name = ClientUiStrings.T("KeyReaderLog1"), Code = "1" },
                new LangItem { Name = ClientUiStrings.T("KeyReaderLog2"), Code = "2" },
                new LangItem { Name = ClientUiStrings.T("KeyReaderLog3"), Code = "3" },
                new LangItem { Name = ClientUiStrings.T("KeyReaderLog4"), Code = "4" }
            };
            if (!string.IsNullOrEmpty(selected))
                SrvKrLogLevelCombo.SelectedValue = selected;
        }

        private void ApplyServerSettingsSync(string payload)
        {
            var map = NotifyMessage.ParseKvMap(payload);
            if (map.Count == 0) return;

            if (_srvSettingsDirty
                && _lastServerSettingsMap != null
                && _lastServerSettingsMap.Count > 0
                && !ServerSettingsMapsEqual(_lastServerSettingsMap, map))
            {
                _conflictServerSettingsMap = new Dictionary<string, string>(map, StringComparer.OrdinalIgnoreCase);
                _srvSettingsConflict = true;
                SetSrvSettingsStatus(ClientUiStrings.T("ServerSettingsConflict"), false);
                UpdateSrvSettingsResetEnabled();
                return;
            }

            ApplyServerSettingsMapToUi(map, true);
        }

        private void ApplyServerSettingsMapToUi(Dictionary<string, string> map, bool markSynced)
        {
            if (map == null || map.Count == 0) return;
            _loadingSrvSettings = true;
            try
            {
                if (SrvLanguageCombo != null) SrvLanguageCombo.SelectedValue = KvStr(map, "language", "en");
                if (SrvThemeCombo != null) SrvThemeCombo.SelectedValue = KvStr(map, "themeMode", "system");
                if (SrvStartWithWindowsCheck != null) SrvStartWithWindowsCheck.IsChecked = KvFlag(map, "startWithWindows", false);
                if (SrvMinimizeToTrayCheck != null) SrvMinimizeToTrayCheck.IsChecked = KvFlag(map, "minimizeToTray", false);
                if (SrvMinimizeOnCloseCheck != null) SrvMinimizeOnCloseCheck.IsChecked = KvFlag(map, "minimizeOnClose", true);
                if (SrvAutoUpdateCheck != null) SrvAutoUpdateCheck.IsChecked = KvFlag(map, "autoUpdate", true);
                if (SrvKeysRefreshBox != null) SrvKeysRefreshBox.Text = KvStr(map, "keysRefreshSeconds", "3");
                if (SrvKeysVinTipBox != null) SrvKeysVinTipBox.Text = KvStr(map, "keysVinTipSeconds", "10");
                if (SrvKeysStallBox != null) SrvKeysStallBox.Text = KvStr(map, "keysStallMinutes", "2");
                if (SrvKeysBalloonCheck != null) SrvKeysBalloonCheck.IsChecked = KvFlag(map, "keysBalloon", true);
                if (SrvKeysVinBmwFailOnlyCheck != null) SrvKeysVinBmwFailOnlyCheck.IsChecked = KvFlag(map, "keysVinBmwFailOnly", false);
                if (SrvKeysAutoCopyCheck != null) SrvKeysAutoCopyCheck.IsChecked = KvFlag(map, "keysAutoCopyVin", false);
                if (SrvKeysStallAlertCheck != null) SrvKeysStallAlertCheck.IsChecked = KvFlag(map, "keysStallAlert", true);
                SyncSrvKeysVinTipUi();
                if (SrvBrowserPassHintCheck != null) SrvBrowserPassHintCheck.IsChecked = KvFlag(map, "browserPassHint", true);
                if (SrvUseSystemProxyCheck != null) SrvUseSystemProxyCheck.IsChecked = KvFlag(map, "useSystemProxy", false);
                if (SrvAuthCooldownBox != null) SrvAuthCooldownBox.Text = KvStr(map, "authCooldownMinutes", "60");
                if (SrvAuthBlockAfterBox != null) SrvAuthBlockAfterBox.Text = KvStr(map, "authRegisterBlockAfter", "3");
                if (SrvNotifyEnabledCheck != null) SrvNotifyEnabledCheck.IsChecked = KvFlag(map, "notifyEnabled", false);
                if (SrvNotifyPortBox != null) SrvNotifyPortBox.Text = KvStr(map, "notifyPort", "17890");
                if (SrvNotifyIpBox != null)
                {
                    SrvNotifyIpBox.Text = KvStr(map, "notifyIps", "");
                    SizeTextBoxToContent(SrvNotifyIpBox);
                }
                if (SrvFirebasePushEnableCheck != null) SrvFirebasePushEnableCheck.IsChecked = KvFlag(map, "firebasePushEnabled", false);
                EnsureSrvFirebaseSyncModeComboItems();
                if (SrvFirebasePushSyncModeCombo != null)
                {
                    var mode = KvStr(map, "firebasePushSyncMode", "");
                    if (string.IsNullOrEmpty(mode) && KvFlag(map, "firebasePushUsePolling", false))
                        mode = "poll";
                    if (string.IsNullOrEmpty(mode)
                        || string.Equals(mode, "sse", StringComparison.OrdinalIgnoreCase))
                        mode = "ondemand";
                    else if (!string.Equals(mode, "poll", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(mode, "ondemand", StringComparison.OrdinalIgnoreCase))
                        mode = "ondemand";
                    SrvFirebasePushSyncModeCombo.SelectedValue = mode;
                }
                if (SrvFirebasePushPollMinutesBox != null) SrvFirebasePushPollMinutesBox.Text = KvStr(map, "firebasePushPollMinutes", "360");
                UpdateSrvFirebasePollMinutesVisibility();
                if (SrvFirebasePushUrlBox != null) SrvFirebasePushUrlBox.Text = KvStr(map, "firebasePushUrl", "");
                SetSrvFirebaseSecretUi(KvStr(map, "firebasePushSecret", ""));
                if (SrvFirebasePushServerNameBox != null) SrvFirebasePushServerNameBox.Text = KvStr(map, "firebasePushServerName", "");
                if (SrvFirebasePushStatusText != null)
                {
                    SrvFirebasePushStatusText.Text = KvFlag(map, "firebasePushEnabled", false)
                        ? ClientUiStrings.T("FirebasePushStatusIdle")
                        : ClientUiStrings.T("FirebasePushStatusDisabled");
                }
                if (SrvConfigPathBox != null) SrvConfigPathBox.Text = KvStr(map, "configPath", "");
                if (SrvDataBinBox != null) SrvDataBinBox.Text = KvStr(map, "keyReaderDataBin", "");
                if (SrvKrLogPathBox != null) SrvKrLogPathBox.Text = KvStr(map, "keyReaderLogPath", "");
                if (SrvEncPathBox != null) SrvEncPathBox.Text = KvStr(map, "keyReaderEncPath", "");
                if (SrvKrLogLevelCombo != null) SrvKrLogLevelCombo.SelectedValue = KvStr(map, "keyReaderLogLevel", "4");
                if (SrvUploaderLogBox != null) SrvUploaderLogBox.Text = KvStr(map, "keysUploaderLogDir", "");
                if (SrvKaiLogBox != null) SrvKaiLogBox.Text = KvStr(map, "keysKaiLogDir", "");
                if (SrvSqliteBox != null) SrvSqliteBox.Text = KvStr(map, "keysSqlitePath", "");

                if (_settings != null)
                {
                    int n;
                    if (int.TryParse(KvStr(map, "authRegisterBlockAfter", "3"), out n) && n >= 1)
                    {
                        _settings.AuthRegisterBlockAfter = n > 10 ? 10 : n;
                    }
                    var lastSuccessRaw = KvStr(map, "lastSuccessUtc", "");
                    if (!string.IsNullOrWhiteSpace(lastSuccessRaw))
                    {
                        DateTime parsed;
                        if (DateTime.TryParse(lastSuccessRaw, CultureInfo.InvariantCulture,
                                DateTimeStyles.RoundtripKind, out parsed))
                            _settings.LastSuccessUtc = parsed.ToUniversalTime();
                    }
                    try { _settings.Save(); } catch { }
                }
                _lastServerSettingsMap = new Dictionary<string, string>(map, StringComparer.OrdinalIgnoreCase);
                _conflictServerSettingsMap = null;
                _srvSettingsConflict = false;
                MarkSrvSettingsDirty(false);
                if (markSynced)
                    SetSrvSettingsStatus(ClientUiStrings.T("ServerSettingsSynced"), true);
                UpdateSrvSettingsResetEnabled();
                UpdateAosPullCloudButtonVisibility();
                RefreshBrowserPassNewerBanner(false);
            }
            finally
            {
                _loadingSrvSettings = false;
                // Capture-shaped baseline so dirty compare matches editable UI keys/formatting.
                _lastServerSettingsCapture = CaptureServerSettingsMap();
            }
        }

        private static bool ServerSettingsMapsEqual(Dictionary<string, string> a, Dictionary<string, string> b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var k in a.Keys) keys.Add(k);
            foreach (var k in b.Keys) keys.Add(k);
            foreach (var key in keys)
            {
                if (string.Equals(key, "notifyIps", StringComparison.OrdinalIgnoreCase))
                    continue;
                string va;
                string vb;
                a.TryGetValue(key, out va);
                b.TryGetValue(key, out vb);
                if (!string.Equals(va ?? "", vb ?? "", StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

        private List<ServerSettingsDiffRow> BuildServerSettingsDiff()
        {
            var baseline = _lastServerSettingsCapture
                ?? _lastServerSettingsMap
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var current = CaptureServerSettingsMap() ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Only editable UI keys (Capture). Baseline-only keys (paths, notify, etc.) are read-only
            // on the client and would falsely appear as value → (empty).
            var ordered = new List<string>(current.Keys);
            ordered.Sort(StringComparer.OrdinalIgnoreCase);

            var rows = new List<ServerSettingsDiffRow>();
            foreach (var key in ordered)
            {
                if (string.Equals(key, "notifyIps", StringComparison.OrdinalIgnoreCase))
                    continue;
                string oldRaw;
                string newRaw;
                baseline.TryGetValue(key, out oldRaw);
                current.TryGetValue(key, out newRaw);
                oldRaw = oldRaw ?? "";
                newRaw = newRaw ?? "";
                if (string.Equals(oldRaw, newRaw, StringComparison.Ordinal))
                    continue;
                rows.Add(new ServerSettingsDiffRow
                {
                    Key = key,
                    Label = ServerSettingsDiffLabel(key),
                    OldDisplay = FormatServerSettingsDiffValue(key, oldRaw),
                    NewDisplay = FormatServerSettingsDiffValue(key, newRaw)
                });
            }
            return rows;
        }

        private static string ServerSettingsDiffLabel(string key)
        {
            string uiKey = null;
            if (string.Equals(key, "language", StringComparison.OrdinalIgnoreCase)) uiKey = "Language";
            else if (string.Equals(key, "themeMode", StringComparison.OrdinalIgnoreCase)) uiKey = "Theme";
            else if (string.Equals(key, "startWithWindows", StringComparison.OrdinalIgnoreCase)) uiKey = "StartWithWindowsYes";
            else if (string.Equals(key, "minimizeToTray", StringComparison.OrdinalIgnoreCase)) uiKey = "MinimizeToTrayYes";
            else if (string.Equals(key, "minimizeOnClose", StringComparison.OrdinalIgnoreCase)) uiKey = "MinimizeOnClose";
            else if (string.Equals(key, "autoUpdate", StringComparison.OrdinalIgnoreCase)) uiKey = "AutoUpdate";
            else if (string.Equals(key, "keysRefreshSeconds", StringComparison.OrdinalIgnoreCase)) uiKey = "KeysRefreshSeconds";
            else if (string.Equals(key, "keysVinTipSeconds", StringComparison.OrdinalIgnoreCase)) uiKey = "KeysVinTipSeconds";
            else if (string.Equals(key, "keysStallMinutes", StringComparison.OrdinalIgnoreCase)) uiKey = "KeysStallMinutes";
            else if (string.Equals(key, "keysBalloon", StringComparison.OrdinalIgnoreCase)) uiKey = "KeysBalloonOption";
            else if (string.Equals(key, "keysVinBmwFailOnly", StringComparison.OrdinalIgnoreCase)) uiKey = "KeysVinBmwFailOnly";
            else if (string.Equals(key, "keysAutoCopyVin", StringComparison.OrdinalIgnoreCase)) uiKey = "KeysAutoCopyVin";
            else if (string.Equals(key, "keysStallAlert", StringComparison.OrdinalIgnoreCase)) uiKey = "KeysStallAlert";
            else if (string.Equals(key, "browserPassHint", StringComparison.OrdinalIgnoreCase)) uiKey = "BrowserPassHintOption";
            else if (string.Equals(key, "useSystemProxy", StringComparison.OrdinalIgnoreCase)) uiKey = "UseSystemProxy";
            else if (string.Equals(key, "authCooldownMinutes", StringComparison.OrdinalIgnoreCase)) uiKey = "AuthCooldownMinutes";
            else if (string.Equals(key, "authRegisterBlockAfter", StringComparison.OrdinalIgnoreCase)) uiKey = "AuthRegisterBlockAfter";
            else if (string.Equals(key, "keyReaderLogLevel", StringComparison.OrdinalIgnoreCase)) uiKey = "KeyReaderLogLevel";
            else if (string.Equals(key, "firebasePushEnabled", StringComparison.OrdinalIgnoreCase)) uiKey = "FirebasePushEnable";
            else if (string.Equals(key, "firebasePushSyncMode", StringComparison.OrdinalIgnoreCase)) uiKey = "FirebasePushSyncMode";
            else if (string.Equals(key, "firebasePushPollMinutes", StringComparison.OrdinalIgnoreCase)) uiKey = "FirebasePushSyncModePoll";
            else if (string.Equals(key, "firebasePushUrl", StringComparison.OrdinalIgnoreCase)) uiKey = "FirebasePushUrl";
            else if (string.Equals(key, "firebasePushSecret", StringComparison.OrdinalIgnoreCase)) uiKey = "FirebasePushSecret";
            else if (string.Equals(key, "firebasePushServerName", StringComparison.OrdinalIgnoreCase)) uiKey = "FirebasePushServerName";
            else if (string.Equals(key, "logVerbose", StringComparison.OrdinalIgnoreCase)) uiKey = "LogVerbose";

            var label = !string.IsNullOrEmpty(uiKey) ? ClientUiStrings.T(uiKey) : key;
            if (string.IsNullOrEmpty(label) || label == uiKey)
                label = key;
            return StripTrailingColon(label);
        }

        private static string StripTrailingColon(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            s = s.TrimEnd();
            while (s.Length > 0 && (s[s.Length - 1] == ':' || s[s.Length - 1] == '：'))
                s = s.Substring(0, s.Length - 1).TrimEnd();
            return s;
        }

        private static bool IsServerSettingsToggleKey(string key)
        {
            return string.Equals(key, "startWithWindows", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "minimizeToTray", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "minimizeOnClose", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "autoUpdate", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "keysBalloon", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "keysVinBmwFailOnly", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "keysAutoCopyVin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "keysStallAlert", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "browserPassHint", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "useSystemProxy", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "firebasePushEnabled", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "logVerbose", StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatServerSettingsDiffValue(string key, string raw)
        {
            raw = raw ?? "";
            if (string.Equals(key, "firebasePushSecret", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(raw))
                    return ClientUiStrings.T("ServerSettingsDiffEmptyValue");
                // Never show the secret; presence in the diff list means it changed.
                return ClientUiStrings.T("ServerSettingsDiffSecretChanged");
            }
            if (IsServerSettingsToggleKey(key))
            {
                if (raw == "1" || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase))
                    return ClientUiStrings.T("ServerSettingsDiffOn");
                if (raw == "0" || string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase) || raw.Length == 0)
                    return ClientUiStrings.T("ServerSettingsDiffOff");
            }
            if (string.Equals(key, "firebasePushSyncMode", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(raw, "poll", StringComparison.OrdinalIgnoreCase))
                    return ClientUiStrings.T("FirebasePushSyncModePoll");
                if (string.Equals(raw, "sse", StringComparison.OrdinalIgnoreCase))
                    return ClientUiStrings.T("FirebasePushSyncModeSse");
                return ClientUiStrings.T("FirebasePushSyncModeOnDemand");
            }
            if (string.Equals(key, "keyReaderLogLevel", StringComparison.OrdinalIgnoreCase))
            {
                var lvlKey = "KeyReaderLog" + (raw ?? "").Trim();
                var lvl = ClientUiStrings.T(lvlKey);
                if (!string.IsNullOrEmpty(lvl) && lvl != lvlKey)
                    return lvl;
            }
            if (string.IsNullOrEmpty(raw))
                return ClientUiStrings.T("ServerSettingsDiffEmptyValue");
            return raw;
        }

        private void ClearSrvSettingsConflict()
        {
            _srvSettingsConflict = false;
            _conflictServerSettingsMap = null;
        }

        private void SrvSettingsReset_Click(object sender, RoutedEventArgs e)
        {
            // Prefer reloading the newer conflict snapshot when present.
            var map = _srvSettingsConflict && _conflictServerSettingsMap != null && _conflictServerSettingsMap.Count > 0
                ? _conflictServerSettingsMap
                : _lastServerSettingsMap;
            if (map == null || map.Count == 0)
            {
                UpdateSrvSettingsResetEnabled();
                SetSrvSettingsStatus(ClientUiStrings.T("ServerSettingsResetNone"), false);
                return;
            }
            ClearSrvSettingsConflict();
            ApplyServerSettingsMapToUi(map, true);
        }

        private void SrvNotifyRefresh_Click(object sender, RoutedEventArgs e)
        {
            RequestNotifyClients();
        }

        private void SrvNotifyTestInfo_Click(object sender, RoutedEventArgs e)
        {
            SendSrvNotifyTest("info");
        }

        private void SrvNotifyTestWarning_Click(object sender, RoutedEventArgs e)
        {
            SendSrvNotifyTest("warning");
        }

        private void SrvNotifyTestError_Click(object sender, RoutedEventArgs e)
        {
            SendSrvNotifyTest("error");
        }

        private void SrvUpdateAllClients_Click(object sender, RoutedEventArgs e)
        {
            if (_client == null || !_client.IsConnected)
            {
                var offline = ClientUiStrings.T("TipAosOffline");
                SetNotifyTestResult(offline, true);
                AppendActivityRow(null, "warning", offline);
                return;
            }
            var ask = ClientUiStrings.T("UpdateAllClientsConfirm");
            var result = MessageBox.Show(this, ask, ClientUiStrings.T("UpdateAllClients"),
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (result != MessageBoxResult.Yes) return;
            try
            {
                var ok = _client.Send(new NotifyMessage
                {
                    Kind = "clientUpdateAllReq",
                    Level = "info",
                    Title = "KAI - AOS Renew - Client",
                    Text = ""
                });
                if (!ok)
                {
                    var fail = ClientUiStrings.T("AosSyncSendFail");
                    SetNotifyTestResult(fail, true);
                    AppendActivityRow(null, "error", fail);
                }
                else
                {
                    AppendActivityRow(null, "info", ClientUiStrings.T("UpdateAllClients"));
                }
            }
            catch (Exception ex)
            {
                SetNotifyTestResult(ex.Message ?? "clientUpdateAllReq", true);
                AppendActivityRow(null, "error", ex.Message ?? "clientUpdateAllReq");
            }
        }

        private void SendSrvNotifyTest(string kind)
        {
            if (_client == null || !_client.IsConnected)
            {
                var offline = ClientUiStrings.T("TipAosOffline");
                SetNotifyTestResult(offline, true);
                AppendActivityRow(null, "warning", offline);
                return;
            }
            SetNotifyTestResult(ClientUiStrings.T("NotifyTestSending"), false);
            var target = SrvNotifySendToCombo != null
                ? SrvNotifySendToCombo.SelectedItem as SrvNotifySendTarget
                : null;
            var targetId = target != null ? target.ClientId : 0;
            try
            {
                var ok = _client.Send(new NotifyMessage
                {
                    Kind = "notifyTest",
                    Level = "info",
                    Title = "KAI - AOS Renew - Client",
                    Text = (kind ?? "info") + "|" + targetId
                });
                if (!ok)
                {
                    var fail = ClientUiStrings.T("AosSyncSendFail");
                    SetNotifyTestResult(fail, true);
                    AppendActivityRow(null, "error", fail);
                }
            }
            catch (Exception ex)
            {
                var fail = ex.Message ?? "notifyTest";
                SetNotifyTestResult(fail, true);
                AppendActivityRow(null, "error", fail);
            }
        }

        private void MarkSrvSettingsDirty(bool dirty)
        {
            _srvSettingsDirty = dirty;
            UpdateSrvSettingsActionButtons();
        }

        private void SetSrvSettingsStatus(string text, bool syncedOk)
        {
            var hasText = !string.IsNullOrEmpty(text);
            if (SrvSettingsStatus != null)
            {
                SrvSettingsStatus.Text = text ?? "";
                SrvSettingsStatus.Foreground = syncedOk
                    ? new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32))
                    : (TryFindResource("TextMutedBrush") as Brush
                       ?? new SolidColorBrush(Color.FromRgb(0x8C, 0x8C, 0x8C)));
            }
            if (SrvSettingsOkMark != null)
                SrvSettingsOkMark.Visibility = syncedOk ? Visibility.Visible : Visibility.Collapsed;
            if (SrvSettingsPendingMark != null)
                SrvSettingsPendingMark.Visibility = (!syncedOk && hasText) ? Visibility.Visible : Visibility.Collapsed;
        }

        private bool _srvSecretSyncing;

        private string GetSrvFirebaseSecretFromUi()
        {
            if (SrvFirebasePushShowSecretCheck != null && SrvFirebasePushShowSecretCheck.IsChecked == true
                && SrvFirebasePushSecretPlainBox != null)
                return (SrvFirebasePushSecretPlainBox.Text ?? "").Trim();
            return SrvFirebasePushSecretBox != null ? (SrvFirebasePushSecretBox.Password ?? "").Trim() : "";
        }

        private void SetSrvFirebaseSecretUi(string secret)
        {
            secret = secret ?? "";
            var prev = _srvSecretSyncing;
            _srvSecretSyncing = true;
            try
            {
                if (SrvFirebasePushSecretBox != null && SrvFirebasePushSecretBox.Password != secret)
                    SrvFirebasePushSecretBox.Password = secret;
                if (SrvFirebasePushSecretPlainBox != null && SrvFirebasePushSecretPlainBox.Text != secret)
                    SrvFirebasePushSecretPlainBox.Text = secret;
            }
            finally { _srvSecretSyncing = prev; }
        }

        private void SrvFirebasePushShowSecret_Changed(object sender, RoutedEventArgs e)
        {
            if (SrvFirebasePushSecretBox == null || SrvFirebasePushSecretPlainBox == null) return;
            var prev = _srvSecretSyncing;
            _srvSecretSyncing = true;
            try
            {
                if (SrvFirebasePushShowSecretCheck != null && SrvFirebasePushShowSecretCheck.IsChecked == true)
                {
                    SrvFirebasePushSecretPlainBox.Text = SrvFirebasePushSecretBox.Password ?? "";
                    SrvFirebasePushSecretBox.Visibility = Visibility.Collapsed;
                    SrvFirebasePushSecretPlainBox.Visibility = Visibility.Visible;
                }
                else
                {
                    SrvFirebasePushSecretBox.Password = SrvFirebasePushSecretPlainBox.Text ?? "";
                    SrvFirebasePushSecretPlainBox.Text = "";
                    SrvFirebasePushSecretPlainBox.Visibility = Visibility.Collapsed;
                    SrvFirebasePushSecretBox.Visibility = Visibility.Visible;
                }
            }
            finally { _srvSecretSyncing = prev; }
        }

        private void SyncSrvKeysVinTipUi()
        {
            var parentOn = SrvKeysBalloonCheck != null && SrvKeysBalloonCheck.IsChecked == true;
            if (SrvKeysVinTipLabel != null)
                SrvKeysVinTipLabel.IsEnabled = parentOn;
            if (SrvKeysVinTipBox != null)
                SrvKeysVinTipBox.IsEnabled = parentOn;
            if (SrvKeysVinBmwFailOnlyCheck != null)
            {
                SrvKeysVinBmwFailOnlyCheck.IsEnabled = parentOn;
                if (!parentOn)
                    SrvKeysVinBmwFailOnlyCheck.IsChecked = false;
            }
            if (SrvKeysAutoCopyCheck != null)
            {
                SrvKeysAutoCopyCheck.IsEnabled = parentOn;
                if (!parentOn)
                    SrvKeysAutoCopyCheck.IsChecked = false;
            }
        }

        private void SrvSettings_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingSrvSettings || _srvSecretSyncing) return;
            if (sender == SrvFirebasePushSyncModeCombo)
                UpdateSrvFirebasePollMinutesVisibility();
            if (sender == SrvKeysBalloonCheck
                || sender == SrvKeysVinBmwFailOnlyCheck
                || sender == SrvKeysAutoCopyCheck)
                SyncSrvKeysVinTipUi();
            if (sender == SrvFirebasePushEnableCheck
                || sender == SrvFirebasePushUrlBox
                || sender == SrvFirebasePushSecretBox
                || sender == SrvFirebasePushSecretPlainBox)
                UpdateAosPullCloudButtonVisibility();
            var current = CaptureServerSettingsMap();
            if (_lastServerSettingsCapture != null
                && ServerSettingsCaptureMatchesBaseline(_lastServerSettingsCapture, current))
            {
                MarkSrvSettingsDirty(false);
                return;
            }
            MarkSrvSettingsDirty(true);
            SetSrvSettingsStatus(ClientUiStrings.T("ServerSettingsDirty"), false);
        }

        /// <summary>
        /// Compare only Capture (editable) keys against baseline.
        /// Baseline may include read-only keys that Capture omits — those must not mark dirty.
        /// </summary>
        private static bool ServerSettingsCaptureMatchesBaseline(
            Dictionary<string, string> baseline, Dictionary<string, string> capture)
        {
            if (capture == null) return true;
            if (baseline == null) return capture.Count == 0;
            foreach (var kv in capture)
            {
                if (string.Equals(kv.Key, "notifyIps", StringComparison.OrdinalIgnoreCase))
                    continue;
                string oldVal;
                baseline.TryGetValue(kv.Key, out oldVal);
                if (!string.Equals(oldVal ?? "", kv.Value ?? "", StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

        private void SrvSettingsField_LostFocus(object sender, RoutedEventArgs e)
        {
            SrvSettings_Changed(sender, e);
        }

        private void SrvSettingsSave_Click(object sender, RoutedEventArgs e)
        {
            SrvSettingsSaveCore(skipConfirm: false);
        }

        private void SrvSettingsSaveCore(bool skipConfirm)
        {
            if (!_srvSettingsDirty) return;
            if (_client == null || !_client.IsConnected)
            {
                SetSrvSettingsStatus(ClientUiStrings.T("AosSaveOffline"), false);
                return;
            }

            if (_srvSettingsConflict)
            {
                var conflictChoice = SettingsConflictDialog.Show(this);
                if (conflictChoice == SettingsConflictChoice.Cancel)
                    return;
                if (conflictChoice == SettingsConflictChoice.Reload)
                {
                    // Reload server snapshot; discard local draft.
                    var reload = _conflictServerSettingsMap;
                    ClearSrvSettingsConflict();
                    if (reload != null && reload.Count > 0)
                        ApplyServerSettingsMapToUi(reload, true);
                    else
                    {
                        MarkSrvSettingsDirty(false);
                        SetSrvSettingsStatus(ClientUiStrings.T("ServerSettingsSynced"), true);
                        UpdateSrvSettingsResetEnabled();
                    }
                    return;
                }
                // Overwrite — fall through and send local UI.
            }
            else if (!skipConfirm)
            {
                var confirm = MessageBox.Show(this, ClientUiStrings.T("ServerSettingsSaveConfirm"),
                    ClientUiStrings.T("AppTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirm != MessageBoxResult.Yes)
                    return;
            }

            try
            {
                var payload = NotifyMessage.EncodeKvMap(CaptureServerSettingsMap());
                var ok = _client.Send(new NotifyMessage
                {
                    Kind = "settingsApply",
                    Level = "info",
                    Title = "KAI - AOS Renew - Client",
                    Text = payload
                });
                if (!ok)
                {
                    MarkSrvSettingsDirty(true);
                    SetSrvSettingsStatus(ClientUiStrings.T("AosSyncSendFail"), false);
                    return;
                }
                ClearSrvSettingsConflict();
                MarkSrvSettingsDirty(false);
                SetSrvSettingsStatus(ClientUiStrings.T("ServerSettingsSaving"), false);
                _awaitingSettingsApply = true;
                BeginAosAckWait();
            }
            catch (Exception ex)
            {
                _awaitingSettingsApply = false;
                MarkSrvSettingsDirty(true);
                SetSrvSettingsStatus(ex.Message, false);
            }
        }

        private void SrvExport_Click(object sender, RoutedEventArgs e)
        {
            if (_client == null || !_client.IsConnected)
            {
                MessageBox.Show(this, ClientUiStrings.T("ExportOffline"), "KAI - AOS Renew - Client",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                var ok = _client.Send(new NotifyMessage
                {
                    Kind = "settingsExportReq",
                    Level = "info",
                    Title = "KAI - AOS Renew - Client",
                    Text = ""
                });
                if (!ok)
                {
                    MessageBox.Show(this, ClientUiStrings.T("AosSyncSendFail"), "KAI - AOS Renew - Client",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(ClientUiStrings.T("ExportFail"), ex.Message),
                    "KAI - AOS Renew - Client", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void HandleSettingsExportPayload(string xml)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(xml))
                {
                    MessageBox.Show(this, string.Format(ClientUiStrings.T("ExportFail"), "empty"),
                        "KAI - AOS Renew - Client", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var dlg = new SaveFileDialog();
                dlg.Filter = "XML settings|*.xml|All|*.*";
                dlg.FileName = "KaiAosRenew-settings.xml";
                dlg.Title = ClientUiStrings.T("ExportTitle");
                if (dlg.ShowDialog(this) != true) return;
                File.WriteAllText(dlg.FileName, xml, Encoding.UTF8);
                AppendActivityRow(null, "success", string.Format(ClientUiStrings.T("ExportOk"), dlg.FileName));
                SetSrvSettingsStatus(string.Format(ClientUiStrings.T("ExportOk"), dlg.FileName), false);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(ClientUiStrings.T("ExportFail"), ex.Message),
                    "KAI - AOS Renew - Client", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SrvImport_Click(object sender, RoutedEventArgs e)
        {
            if (_client == null || !_client.IsConnected)
            {
                MessageBox.Show(this, ClientUiStrings.T("ImportOffline"), "KAI - AOS Renew - Client",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                var dlg = new OpenFileDialog();
                dlg.Filter = "XML settings|*.xml|All|*.*";
                dlg.Title = ClientUiStrings.T("ImportTitle");
                if (dlg.ShowDialog(this) != true) return;
                var xml = File.ReadAllText(dlg.FileName);
                if (string.IsNullOrWhiteSpace(xml))
                {
                    MessageBox.Show(this, string.Format(ClientUiStrings.T("ImportFail"), "empty"),
                        "KAI - AOS Renew - Client", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var ok = _client.Send(new NotifyMessage
                {
                    Kind = "settingsImport",
                    Level = "info",
                    Title = "KAI - AOS Renew - Client",
                    Text = xml
                });
                if (!ok)
                {
                    MessageBox.Show(this, ClientUiStrings.T("AosSyncSendFail"), "KAI - AOS Renew - Client",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                _awaitingSettingsImport = true;
                SetSrvSettingsStatus(ClientUiStrings.T("ServerSettingsSaving"), false);
                BeginAosAckWait();
            }
            catch (Exception ex)
            {
                _awaitingSettingsImport = false;
                MessageBox.Show(this, string.Format(ClientUiStrings.T("ImportFail"), ex.Message),
                    "KAI - AOS Renew - Client", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

    }
}
