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

        private void InitLanguageCombo()
        {
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
            var selected = ThemeCombo.SelectedValue as string;
            ThemeCombo.ItemsSource = new List<LangItem>
            {
                new LangItem { Name = UiStrings.T("ThemeSystem"), Code = "system" },
                new LangItem { Name = UiStrings.T("ThemeDay"), Code = "day" },
                new LangItem { Name = UiStrings.T("ThemeNight"), Code = "night" }
            };
            if (!string.IsNullOrEmpty(selected))
                ThemeCombo.SelectedValue = selected;
        }

        private void ApplyLanguage()
        {
            Title = UiStrings.T("WindowTitle");
            HeaderTitleText.Text = UiStrings.T("HeaderTitle");
            if (IntervalLabel != null) IntervalLabel.Text = UiStrings.T("CheckHealthInterval");
            if (StartWithWindowsCheck != null)
                StartWithWindowsCheck.Content = CheckboxLabel(UiStrings.T("StartWithWindows"));
            if (MinimizeToTrayCheck != null)
                MinimizeToTrayCheck.Content = CheckboxLabel(UiStrings.T("MinimizeToTray"));
            if (AuthCooldownMinutesLabel != null)
                AuthCooldownMinutesLabel.Text = UiStrings.T("AuthCooldownMinutes");
            if (AuthRegisterBlockAfterLabel != null)
                AuthRegisterBlockAfterLabel.Text = UiStrings.T("AuthRegisterBlockAfter");
            if (ActivityRefreshBtn != null) ActivityRefreshBtn.Content = UiStrings.T("KeysRefresh");
            if (RegisterRefreshBtn != null) RegisterRefreshBtn.Content = UiStrings.T("KeysRefresh");
            if (SaveActivityLogBtn != null) SaveActivityLogBtn.Content = UiStrings.T("SaveLog");
            if (SaveRegisterLogBtn != null) SaveRegisterLogBtn.Content = UiStrings.T("SaveLog");
            if (ClearRegisterLogBtn != null) ClearRegisterLogBtn.Content = UiStrings.T("ClearLog");
            if (MinimizeOnCloseCheck != null)
                MinimizeOnCloseCheck.Content = CheckboxLabel(UiStrings.T("MinimizeOnClose"));
            if (AutoUpdateCheck != null)
            {
                AutoUpdateCheck.Content = CheckboxLabel(UiStrings.T("AutoUpdate"));
                AutoUpdateCheck.ToolTip = UiStrings.T("TipAutoUpdate");
            }
            if (CheckForUpdatesBtn != null)
                CheckForUpdatesBtn.Content = UiStrings.T("CheckForUpdates");
            if (_pendingUpdate != null)
                ShowPendingUpdate(_pendingUpdate);
            else if (FooterUpdateBtn != null)
                FooterUpdateBtn.Content = UiStrings.T("Update");
            if (SectionConfigText != null) SectionConfigText.Text = UiStrings.T("SectionConfig");
            if (ConfigPathLabel != null) ConfigPathLabel.Text = UiStrings.T("ConfigPath");
            if (BrowseBtn != null) BrowseBtn.Content = UiStrings.T("Browse");
            var openExplorer = UiStrings.T("OpenInExplorer");
            var tipOpenExplorer = UiStrings.T("TipOpenInExplorer");
            if (ConfigPathOpenBtn != null)
            {
                ConfigPathOpenBtn.Content = openExplorer;
                ConfigPathOpenBtn.ToolTip = tipOpenExplorer;
            }
            if (KeyReaderDataBinOpenBtn != null)
            {
                KeyReaderDataBinOpenBtn.Content = openExplorer;
                KeyReaderDataBinOpenBtn.ToolTip = tipOpenExplorer;
            }
            if (KeyReaderLogPathOpenBtn != null)
            {
                KeyReaderLogPathOpenBtn.Content = openExplorer;
                KeyReaderLogPathOpenBtn.ToolTip = tipOpenExplorer;
            }
            if (KeysSqlitePathOpenBtn != null)
            {
                KeysSqlitePathOpenBtn.Content = openExplorer;
                KeysSqlitePathOpenBtn.ToolTip = tipOpenExplorer;
            }
            if (KeyReaderEncPathOpenBtn != null)
            {
                KeyReaderEncPathOpenBtn.Content = openExplorer;
                KeyReaderEncPathOpenBtn.ToolTip = tipOpenExplorer;
            }
            if (KeysUploaderLogDirOpenBtn != null)
            {
                KeysUploaderLogDirOpenBtn.Content = openExplorer;
                KeysUploaderLogDirOpenBtn.ToolTip = tipOpenExplorer;
            }
            if (KeysKaiLogDirOpenBtn != null)
            {
                KeysKaiLogDirOpenBtn.Content = openExplorer;
                KeysKaiLogDirOpenBtn.ToolTip = tipOpenExplorer;
            }
            if (SettingsKeyReaderSection != null)
                SettingsKeyReaderSection.Text = UiStrings.T("SettingsKeyReader");
            if (KeyReaderDataBinLabel != null)
                KeyReaderDataBinLabel.Text = UiStrings.T("KeyReaderDataBin");
            if (KeyReaderLogPathLabel != null)
                KeyReaderLogPathLabel.Text = UiStrings.T("KeyReaderLogPath");
            if (KeyReaderEncPathLabel != null)
                KeyReaderEncPathLabel.Text = UiStrings.T("KeyReaderEncPath");
            if (KeyReaderLogLevelLabel != null)
                KeyReaderLogLevelLabel.Text = UiStrings.T("KeyReaderLogLevel");
            RefreshKeyReaderLogLevelCombo();
            if (SettingsKaiDataSection != null)
                SettingsKaiDataSection.Text = UiStrings.T("SettingsKaiData");
            if (KeysUploaderLogDirLabel != null)
                KeysUploaderLogDirLabel.Text = UiStrings.T("KeysUploaderLogDir");
            if (KeysKaiLogDirLabel != null)
                KeysKaiLogDirLabel.Text = UiStrings.T("KeysKaiLogDir");
            if (KeysSqlitePathLabel != null)
                KeysSqlitePathLabel.Text = UiStrings.T("KeysSqlitePath");
            var browse = UiStrings.T("Browse");
            if (KeyReaderDataBinBrowseBtn != null) KeyReaderDataBinBrowseBtn.Content = browse;
            if (KeyReaderLogPathBrowseBtn != null) KeyReaderLogPathBrowseBtn.Content = browse;
            if (KeyReaderEncPathBrowseBtn != null) KeyReaderEncPathBrowseBtn.Content = browse;
            if (KeysUploaderLogDirBrowseBtn != null) KeysUploaderLogDirBrowseBtn.Content = browse;
            if (KeysKaiLogDirBrowseBtn != null) KeysKaiLogDirBrowseBtn.Content = browse;
            if (KeysSqlitePathBrowseBtn != null) KeysSqlitePathBrowseBtn.Content = browse;
            LanguageLabel.Text = UiStrings.T("Language");
            ThemeLabel.Text = UiStrings.T("Theme");
            RefreshThemeComboItems();
            RefreshLogFilterComboItems();
            ExportBtn.Content = UiStrings.T("ExportSettings");
            ImportBtn.Content = UiStrings.T("ImportSettings");
            if (DiagBundleBtn != null)
            {
                DiagBundleBtn.Content = UiStrings.T("DiagBundle");
                DiagBundleBtn.ToolTip = UiStrings.T("DiagBundleTip");
            }
            if (AosLoginStatusText != null) UpdateAosLoginStatus();
            AosUserLabel.Text = UiStrings.T("AosUsername");
            AosPassLabel.Text = UiStrings.T("AosPassword");
            ShowPasswordCheck.Content = UiStrings.T("ShowPassword");
            UseSystemProxyCheck.Content = UiStrings.T("UseSystemProxy");
            ApplyClipboardPassBannerStrings();
            if (ClipboardPassBanner != null
                && ClipboardPassBanner.Visibility == Visibility.Visible
                && !string.IsNullOrEmpty(_clipboardPassCandidate))
            {
                if (ClipboardPassHeaderText != null)
                    ClipboardPassHeaderText.Text = UiStrings.T("ClipboardPassHeader");
                var current = GetPasswordUi() ?? "";
                if (ClipboardPassCurrentText != null)
                    ClipboardPassCurrentText.Text = string.Format(UiStrings.T("ClipboardPassCurrent"),
                        PasswordDisplayMask.Mask(current));
                if (ClipboardPassClipText != null)
                    ClipboardPassClipText.Text = string.Format(UiStrings.T("ClipboardPassClip"),
                        PasswordDisplayMask.Mask(_clipboardPassCandidate));
            }
            if (AosPullCloudBtnText != null)
                AosPullCloudBtnText.Text = UiStrings.T("AosPullFromCloud");
            if (CheckBrowserPassBtnText != null)
                CheckBrowserPassBtnText.Text = UiStrings.T("CheckBrowserPass");
            if (OpenBmwSsoBtnText != null)
                OpenBmwSsoBtnText.Text = UiStrings.T("OpenBmwSso");
            UpdateAosPullCloudButtonVisibility();
            if (BrowserPassHintCheck != null)
                BrowserPassHintCheck.Content = UiStrings.T("BrowserPassHintOption");
            if (TabRenewHeaderText != null) TabRenewHeaderText.Text = UiStrings.T("TabRenew");
            if (TabKaiHeaderText != null) TabKaiHeaderText.Text = UiStrings.T("TabKai");
            if (TabKeysHeaderText != null) TabKeysHeaderText.Text = UiStrings.T("TabKeys");
            if (TabActivityHeaderText != null) TabActivityHeaderText.Text = UiStrings.T("TabActivity");
            if (TabRegisterHeaderText != null) TabRegisterHeaderText.Text = UiStrings.T("TabRegister");
            ApplyRegisterTabStrings();
            if (TabSettingsHeaderText != null) TabSettingsHeaderText.Text = UiStrings.T("TabSettings");
            if (AboutTabBtn != null) AboutTabBtn.Content = UiStrings.T("TabAbout");
            ApplyKeysTabStrings();
            AboutTitleText.Text = "KAI - AOS Renew";
            AboutVersionText.Text = string.Format(UiStrings.T("Version"), AppVersionDisplay);
            if (DebugModeText != null)
            {
                DebugModeText.Text = UiStrings.T("DebugModeOn");
                DebugModeText.Visibility = _debugMode ? Visibility.Visible : Visibility.Collapsed;
            }
            if (AboutOpenSettingsBtn != null)
                AboutOpenSettingsBtn.Content = UiStrings.T("AboutOpenSettings");
            if (AboutOpenLogsBtn != null)
                AboutOpenLogsBtn.Content = UiStrings.T("AboutOpenLogs");
            AboutChangelogLabel.Text = UiStrings.T("AboutChangelogLabel");
            if (SettingsGeneralGroup != null)
                SettingsGeneralGroup.Header = UiStrings.T("SettingsGeneral");
            if (SettingsStartupGroup != null)
                SettingsStartupGroup.Header = UiStrings.T("SettingsStartup");
            if (SettingsAuthGroup != null)
                SettingsAuthGroup.Header = UiStrings.T("SettingsAuth");
            if (SettingsPathsGroup != null)
                SettingsPathsGroup.Header = UiStrings.T("SettingsPaths");
            if (SettingsKeysGroup != null)
                SettingsKeysGroup.Header = UiStrings.T("SettingsKeys");
            if (SettingsNotifyGroup != null)
                SettingsNotifyGroup.Header = UiStrings.T("SettingsNotifyGroup");
            if (NotifyServerEnableCheck != null)
                NotifyServerEnableCheck.Content = UiStrings.T("NotifyServerEnable");
            ApplyFirebasePushLanguage();
            if (NotifyServerPortLabel != null)
                NotifyServerPortLabel.Text = UiStrings.T("NotifyServerPort");
            if (NotifyPairingCodeLabel != null)
            {
                NotifyPairingCodeLabel.Text = UiStrings.T("NotifyPairingCode");
                NotifyPairingCodeLabel.ToolTip = UiStrings.T("NotifyPairingCodeTip");
            }
            if (NotifyPairingCodeBox != null)
                NotifyPairingCodeBox.ToolTip = UiStrings.T("NotifyPairingCodeTip");
            UpdateNotifyPairingWarning();
            if (NotifyPairingGenerateBtn != null)
            {
                NotifyPairingGenerateBtn.Content = UiStrings.T("NotifyPairingGenerate");
                NotifyPairingGenerateBtn.ToolTip = UiStrings.T("NotifyPairingGenerateTip");
            }
            if (NotifyPairingCopyBtn != null)
                NotifyPairingCopyBtn.ToolTip = UiStrings.T("NotifyPairingCopyTip");
            if (NotifyPairingPasteBtn != null)
                NotifyPairingPasteBtn.ToolTip = UiStrings.T("NotifyPairingPasteTip");
            if (NotifyServerIpLabel != null)
                NotifyServerIpLabel.Text = UiStrings.T("NotifyServerIpLabel");
            if (NotifyServerIpCopyBtn != null)
                NotifyServerIpCopyBtn.ToolTip = UiStrings.T("NotifyServerIpCopyTip");
            if (NotifySendToLabel != null)
                NotifySendToLabel.Text = UiStrings.T("NotifySendTo");
            if (NotifyRefreshClientsBtn != null)
                NotifyRefreshClientsBtn.Content = UiStrings.T("NotifyRefreshClients");
            if (NotifyTestVinBtn != null)
                NotifyTestVinBtn.Content = UiStrings.T("NotifyTestVin");
            if (NotifyTestInfoBtn != null)
                NotifyTestInfoBtn.Content = UiStrings.T("NotifyTestInfo");
            if (NotifyTestWarningBtn != null)
                NotifyTestWarningBtn.Content = UiStrings.T("NotifyTestWarning");
            if (NotifyTestErrorBtn != null)
                NotifyTestErrorBtn.Content = UiStrings.T("NotifyTestError");
            if (UpdateAllClientsBtn != null)
            {
                UpdateAllClientsBtn.Content = UiStrings.T("UpdateAllClients");
                UpdateAllClientsBtn.ToolTip = UiStrings.T("TipUpdateAllClients");
            }
            RefreshNotifyServerIpDisplay();
            RefreshNotifyClientsCombo();
            ApplySettingsTooltips();
            if (KeysRefreshSecondsLabel != null)
                KeysRefreshSecondsLabel.Text = UiStrings.T("KeysRefreshSeconds");
            if (KeysVinTipSecondsLabel != null)
                KeysVinTipSecondsLabel.Text = UiStrings.T("KeysVinTipSeconds");
            if (KeysStallMinutesLabel != null)
                KeysStallMinutesLabel.Text = UiStrings.T("KeysStallMinutes");
            if (KeysAutoCopyVinCheck != null)
                KeysAutoCopyVinCheck.Content = UiStrings.T("KeysAutoCopyVin");
            KaiRefreshBtn.Content = UiStrings.T("KaiRefresh");
            KaiFixServicesBtn.Content = UiStrings.T("KaiFixNow");
            KaiStopServicesBtn.Content = UiStrings.T("KaiStopServices");
            KaiInstallSection.Text = UiStrings.T("KaiInstallSection");
            KaiUrlsSection.Text = UiStrings.T("KaiUrlsSection");
            KaiUrlRegisterLabel.Text = UiStrings.T("KaiUrlRegister");
            KaiUrlWebEamLabel.Text = UiStrings.T("KaiUrlWebEam");
            KaiUrlUploadLabel.Text = UiStrings.T("KaiUrlUpload");
            KaiTokenSection.Text = UiStrings.T("KaiTokenSection");
            KaiServicesSection.Text = UiStrings.T("KaiServicesSection");
            if (KaiSvcColName != null) KaiSvcColName.Text = UiStrings.T("KaiSvcColName");
            if (KaiSvcColStatus != null) KaiSvcColStatus.Text = UiStrings.T("KaiSvcColStatus");
            if (KaiSvcColStart != null) KaiSvcColStart.Text = UiStrings.T("KaiSvcColStart");
            if (SaveAosBtnText != null)
                SaveAosBtnText.Text = UiStrings.T("Save");
            UpdateAosDirtyStatus();
            if (ForceRegisterBtn != null)
            {
                ForceRegisterBtn.Content = UiStrings.T("ForceRegister");
                ForceRegisterBtn.ToolTip = UiStrings.T("TipForceRegister");
            }
            if (AuthGuardsStatusTitle != null)
                AuthGuardsStatusTitle.Text = UiStrings.T("AuthGuardsStatusTitle");
            if (AuthCooldownExplainText != null)
                AuthCooldownExplainText.Text = UiStrings.T("AuthCooldownExplain");
            if (AuthRegisterBlockedExplainText != null)
                AuthRegisterBlockedExplainText.Text = UiStrings.T("AuthRegisterBlockedExplain");
            if (ClearAuthCooldownBtn != null)
            {
                ClearAuthCooldownBtn.Content = UiStrings.T("AuthGuardClear");
                ClearAuthCooldownBtn.ToolTip = UiStrings.T("TipAuthGuardClearCooldown");
            }
            if (ClearAuthRegisterBlockedBtn != null)
            {
                ClearAuthRegisterBlockedBtn.Content = UiStrings.T("AuthGuardClear");
                ClearAuthRegisterBlockedBtn.ToolTip = UiStrings.T("TipAuthGuardClearBlocked");
            }
            RefreshAuthCooldownBanner();
            ActivityLogLabel.Text = UiStrings.T("ActivityLog");
            if (ActivityFilterLabel != null) ActivityFilterLabel.Text = UiStrings.T("RegisterFilter");
            if (ActivitySearchLabel != null) ActivitySearchLabel.Text = UiStrings.T("ActivitySearch");
            if (ActivitySearchBox != null) ActivitySearchBox.ToolTip = UiStrings.T("ActivitySearchTip");
            if (RegisterSearchLabel != null) RegisterSearchLabel.Text = UiStrings.T("RegisterSearch");
            if (RegisterSearchBox != null) RegisterSearchBox.ToolTip = UiStrings.T("RegisterSearchTip");
            ApplyShortcutStrings();
            if (ActivityColTime != null) ActivityColTime.Header = UiStrings.T("KeysColTime");
            if (ActivityColLevel != null) ActivityColLevel.Header = UiStrings.T("ActivityColLevel");
            if (ActivityColText != null) ActivityColText.Header = UiStrings.T("ActivityColText");
            ClearLogBtn.Content = UiStrings.T("ClearLog");
            CheckTokenBtn.Content = UiStrings.T("CheckToken");
            StartTimerBtn.Content = UiStrings.T("StartTimer");
            StopTimerBtn.Content = UiStrings.T("StopTimer");
            VersionText.Text = string.Format(UiStrings.T("Version"), AppVersionDisplay);
            RefreshStatusBadge();
            UpdateNextLabel();
            UpdateTokenAgeText();
            RefreshKaiTokenExpiryLabel();
            try
            {
                UpdateServicesFooter(ServiceMonitor.Check().AllOk);
            }
            catch { }
            UpdateTokenFooterDisplay();
            _trayVinMenuSignature = null;
            RebuildTrayMenu();
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
            if (MainTabs != null) MainTabs.Visibility = Visibility.Visible;
            if (AboutTabBtn != null) AboutTabBtn.Tag = "Selected";
            if (TabDummy != null)
            {
                _suppressTabChange = true;
                try { TabDummy.IsSelected = true; }
                finally { _suppressTabChange = false; }
            }
            LoadAboutChangelog();
            ApplyTabLayoutVisibility();
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
            ApplyTabLayoutVisibility();
        }

        private void ApplySettingsTooltips()
        {
            if (StartWithWindowsCheck != null)
                StartWithWindowsCheck.ToolTip = UiStrings.T("TipStartWithWindows");
            if (MinimizeToTrayCheck != null)
                MinimizeToTrayCheck.ToolTip = UiStrings.T("TipMinimizeToTray");
            if (MinimizeOnCloseCheck != null)
                MinimizeOnCloseCheck.ToolTip = UiStrings.T("TipMinimizeOnClose");
            if (AuthCooldownMinutesBox != null)
                AuthCooldownMinutesBox.ToolTip = UiStrings.T("TipAuthCooldown");
            if (AuthRegisterBlockAfterBox != null)
                AuthRegisterBlockAfterBox.ToolTip = UiStrings.T("TipAuthRegisterBlockAfter");
        }

        private void ApplyThemeChrome()
        {
            try
            {
                // Dynamic reference so theme switches update the window background.
                SetResourceReference(BackgroundProperty, "WindowBgBrush");
            }
            catch
            {
                try { Background = (Brush)FindResource("WindowBgBrush"); }
                catch { }
            }
            RefreshThemeDependentBrushes();
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

        private void RunStartupSelfCheck()
        {
            var issues = new List<string>();
            if (_tray == null || !_tray.Visible)
                issues.Add(UiStrings.T("SelfCheckTrayFail"));

            // This process owns the single-instance mutex if the UI is running.
            var mutexOk = UiStrings.T("SelfCheckMutexOk");

            var sqlite = _settings != null && !string.IsNullOrWhiteSpace(_settings.KeysSqlitePath)
                ? _settings.KeysSqlitePath
                : KeyActivityMonitor.DefaultSqlitePath;
            if (!File.Exists(sqlite))
                issues.Add(string.Format(UiStrings.T("SelfCheckSqliteFail"), sqlite));

            var warnState = 0;
            try
            {
                var encPath = DesktopKeyReaderReg.ReadAbsolute().EncPath;
                if (string.IsNullOrEmpty(encPath) || !File.Exists(encPath))
                {
                    warnState |= KeysReaderWarnEncBit;
                    _keysReaderEncPath = encPath ?? "";
                    issues.Add(string.Format(UiStrings.T("SelfCheckEncMissing"), encPath ?? "—"));
                }
            }
            catch { }
            try
            {
                if (File.Exists(sqlite) && _keyMonitor.PeekReaderInactive() == true)
                {
                    warnState |= KeysReaderWarnInactiveBit;
                    issues.Add(UiStrings.T("SelfCheckReaderInactive"));
                }
            }
            catch { }
            if (warnState != 0)
            {
                // Seed anti-spam state so the first Keys refresh does not repeat the balloon.
                _keysReaderWarnState |= warnState;
                ApplyKeysReaderWarnUi();
            }

            if (issues.Count == 0)
            {
                AppendLog(UiStrings.T("SelfCheckOk") + " (" + mutexOk + ")", LogLevel.Info);
                return;
            }

            // Log only — no balloon (avoids interrupting UI; DataBin is not checked).
            var msg = string.Format(UiStrings.T("SelfCheckWarn"), string.Join("; ", issues));
            AppendLog(msg + " [" + mutexOk + "]", LogLevel.Warning);
        }

        private void ShowAbout()
        {
            ShowFromTray();
            ShowAboutPanel();
        }

        private void AboutOpenLogs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dir = Path.GetDirectoryName(AppSettings.LogPath);
                if (string.IsNullOrEmpty(dir))
                    dir = Path.GetDirectoryName(AppSettings.SettingsPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                    Process.Start(dir);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(UiStrings.T("CannotOpenLog"), ex.Message), "KAI - AOS Renew",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void AboutOpenSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dir = Path.GetDirectoryName(AppSettings.SettingsPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                    Process.Start(dir);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(UiStrings.T("CannotOpenLog"), ex.Message), "KAI - AOS Renew",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
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
                    AboutChangelogBox.Text = UiStrings.T("AboutChangelogMissing");
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

        private string BuildServerSettingsPayload()
        {
            return NotifyMessage.EncodeKvMap(BuildServerSettingsMap());
        }

        private const int FolderZipChunkRawBytes = 512 * 1024;

        private void SendFolderZipToClient(int clientId, string kind)
        {
            var k = (kind ?? "").Trim().ToLowerInvariant();
            string stageDir = null;
            string tempZip = null;
            try
            {
                if (k == "diag")
                {
                    Forget(BuildAndSaveDiagBundleAsync(clientId), "DiagBundleRemote");
                    return;
                }
                if (k != "settings" && k != "logs" && k != "keydebug")
                {
                    SendAosAck(clientId, false, "Unknown zip kind");
                    return;
                }
                stageDir = Path.Combine(Path.GetTempPath(),
                    "kai-aos-stage-" + k + "-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(stageDir);
                var staged = StageFolderZipFiles(k, stageDir);
                if (!staged)
                {
                    SendAosAck(clientId, false, "Folder empty");
                    return;
                }

                tempZip = Path.Combine(Path.GetTempPath(),
                    "kai-aos-" + k + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".zip");
                if (File.Exists(tempZip)) File.Delete(tempZip);
                System.IO.Compression.ZipFile.CreateFromDirectory(
                    stageDir, tempZip, System.IO.Compression.CompressionLevel.Optimal, false);
                var fi = new FileInfo(tempZip);
                if (!fi.Exists || fi.Length == 0)
                {
                    SendAosAck(clientId, false, "Folder empty");
                    return;
                }
                if (fi.Length > MaxFolderZipBytes)
                {
                    SendAosAck(clientId, false, "Zip too large (>" + (MaxFolderZipBytes / (1024 * 1024)) + " MB)");
                    return;
                }
                var bytes = File.ReadAllBytes(tempZip);
                var name = "kai-aos-" + k + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip";
                SendFolderZipChunked(clientId, name, bytes);
                SendAosAck(clientId, true, "getZip|" + k);
            }
            catch (Exception ex)
            {
                SendAosAck(clientId, false, ex.Message);
            }
            finally
            {
                if (!string.IsNullOrEmpty(tempZip))
                {
                    try { File.Delete(tempZip); }
                    catch { }
                }
                if (!string.IsNullOrEmpty(stageDir))
                {
                    try { Directory.Delete(stageDir, true); }
                    catch { }
                }
            }
        }

        private static bool StageFolderZipFiles(string kind, string stageDir)
        {
            var n = 0;
            if (kind == "settings")
            {
                var settingsPath = AppSettings.SettingsPath;
                n += CopyIfExists(settingsPath, stageDir);
                n += CopyIfExists(settingsPath + ".bak", stageDir);
            }
            else if (kind == "logs")
            {
                var dir = Path.GetDirectoryName(AppSettings.LogPath);
                if (string.IsNullOrEmpty(dir))
                    dir = Path.GetDirectoryName(AppSettings.SettingsPath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    foreach (var path in Directory.GetFiles(dir, "*.log*"))
                    {
                        var name = Path.GetFileName(path) ?? "";
                        if (name.StartsWith("key-reader-parse.log", StringComparison.OrdinalIgnoreCase))
                            continue;
                        n += CopyIfExists(path, stageDir);
                    }
                }
            }
            else if (kind == "keydebug")
            {
                var store = KeyActivityMonitor.KeyDebugStoreDirectory;
                if (!string.IsNullOrEmpty(store) && Directory.Exists(store))
                {
                    foreach (var path in Directory.GetFiles(store, "key-reader-parse.log*"))
                        n += CopyIfExists(path, stageDir);
                    var bins = Path.Combine(store, "key-bins");
                    if (Directory.Exists(bins))
                    {
                        var binsStage = Path.Combine(stageDir, "key-bins");
                        Directory.CreateDirectory(binsStage);
                        foreach (var path in Directory.GetFiles(bins, "*.bin"))
                            n += CopyIfExists(path, binsStage);
                    }
                }
            }
            else
                return false;
            return n > 0;
        }

        private static int CopyIfExists(string path, string destDir)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return 0;
                var name = Path.GetFileName(path);
                if (string.IsNullOrEmpty(name)) return 0;
                File.Copy(path, Path.Combine(destDir, name), true);
                return 1;
            }
            catch
            {
                return 0;
            }
        }

        private void SendFolderZipChunked(int clientId, string fileName, byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return;
            var transferId = Guid.NewGuid().ToString("N");
            NotifyServer.Instance.SendTo(clientId, new NotifyMessage
            {
                Kind = "folderZipStart",
                Level = "info",
                Title = "KAI - AOS Renew",
                Text = transferId + "|" + fileName + "|" + bytes.Length
            });
            var seq = 0;
            for (var off = 0; off < bytes.Length; off += FolderZipChunkRawBytes)
            {
                var len = Math.Min(FolderZipChunkRawBytes, bytes.Length - off);
                var chunk = Convert.ToBase64String(bytes, off, len);
                NotifyServer.Instance.SendTo(clientId, new NotifyMessage
                {
                    Kind = "folderZipChunk",
                    Level = "info",
                    Title = "KAI - AOS Renew",
                    Text = transferId + "|" + seq + "|" + chunk
                });
                seq++;
            }
            NotifyServer.Instance.SendTo(clientId, new NotifyMessage
            {
                Kind = "folderZipEnd",
                Level = "info",
                Title = "KAI - AOS Renew",
                Text = transferId + "|" + fileName
            });
        }

        private void ApplyNotifyServerFromSettings()
        {
            if (_settings == null) return;
            try
            {
                NotifyServer.Instance.ApplySettings(
                    _settings.NotifyServerEnabled,
                    _settings.NotifyServerPort,
                    _settings.GetNotifyPairingCode());
            }
            catch (Exception ex)
            {
                AppendLog(string.Format(UiStrings.T("LogNotifyServer"), ex.Message), LogLevel.Warning);
            }
            RefreshNotifyServerIpDisplay();
            RefreshNotifyClientsCombo();
        }

        private void LoadSettingsToUi()
        {
            _loadingUi = true;
            try
            {
                _settings = AppSettings.Load();
                var loadWarning = AppSettings.TakeLoadWarning();
                if (!string.IsNullOrEmpty(loadWarning))
                {
                    try { AppendLog(loadWarning, LogLevel.Warning); }
                    catch { }
                }
                if (string.IsNullOrWhiteSpace(_settings.ConfigPath))
                    _settings.ConfigPath = @"C:\ProgramData\BMW\ISPI\config\KAI\KAI";
                EmailBox.Text = _settings.Email ?? "";
                SetPasswordUi(_settings.GetPassword());
                var hours = _settings.IntervalHours <= 0 ? 1 : _settings.IntervalHours;
                if (hours < 1) hours = 1;
                if (hours > 168) hours = 168;
                IntervalBox.Text = hours.ToString();
                ConfigPathBox.Text = _settings.ConfigPath;
                _lastValidConfigPath = _settings.ConfigPath;
                StartWithWindowsCheck.IsChecked = _settings.StartWithWindows;
                MinimizeToTrayCheck.IsChecked = _settings.MinimizeToTray;
                if (AutoUpdateCheck != null)
                    AutoUpdateCheck.IsChecked = _settings.AutoUpdate;
                UpdateMinimizeToTrayEnabled();
                UseSystemProxyCheck.IsChecked = _settings.UseSystemProxy;
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
                if (NotifyServerEnableCheck != null)
                    NotifyServerEnableCheck.IsChecked = _settings.NotifyServerEnabled;
                if (NotifyServerPortBox != null)
                    NotifyServerPortBox.Text = (_settings.NotifyServerPort <= 0 ? NotifyServer.DefaultPort : _settings.NotifyServerPort).ToString();
                if (NotifyPairingCodeBox != null)
                    NotifyPairingCodeBox.Text = _settings.GetNotifyPairingCode();
                UpdateNotifyPairingWarning();
                RefreshNotifyServerIpDisplay();
                RefreshNotifyClientsCombo();
                LoadFirebasePushToUi();
                if (string.IsNullOrWhiteSpace(_settings.KeysPickupFolder))
                    _settings.KeysPickupFolder = KeyActivityMonitor.DefaultPickupFolder;
                _settings.NormalizeClamps();
                if (KeysStallMinutesBox != null)
                    KeysStallMinutesBox.Text = _settings.KeysStallMinutes.ToString();
                LoadKeyReaderPathsToUi();
                LoadKeysDataPathsToUi();
                // Keep Keys watch folder aligned with DataBin parent.
                try
                {
                    var paths = DesktopKeyReaderReg.ReadAbsolute();
                    if (!string.IsNullOrEmpty(paths.DataBin))
                    {
                        var dir = Path.GetDirectoryName(paths.DataBin);
                        if (!string.IsNullOrEmpty(dir))
                            _settings.KeysPickupFolder = dir;
                    }
                }
                catch { }
                var lang = string.IsNullOrWhiteSpace(_settings.Language) ? "en" : _settings.Language.Trim();
                UiStrings.SetLanguage(lang);
                LanguageCombo.SelectedValue = lang;
                if (LanguageCombo.SelectedItem == null)
                {
                    LanguageCombo.SelectedValue = "en";
                    UiStrings.SetLanguage("en");
                }
                var theme = ThemeManager.NormalizeMode(_settings.ThemeMode);
                _settings.ThemeMode = theme;
                ThemeCombo.SelectedValue = theme;
                ThemeManager.Apply(theme);
                ApplyThemeChrome();
            ApplyLanguage();
            if (_settings.LastSuccessUtc.HasValue)
            {
                AppendLog("Last success: " + _settings.LastSuccessUtc.Value.ToLocalTime().ToString("g") + " — " + _settings.LastMessage, LogLevel.Success);
            }
            UpdateTokenAgeText();
            RefreshKaiDiagnostics(false);
            }
            finally
            {
                _loadingUi = false;
                CaptureAosSavedCredentials();
                _aosCredentialsDirty = false;
                UpdateAosDirtyStatus();
            }
        }

        private void BrowserPassHint_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingUi || _settings == null) return;
            _settings.BrowserPasswordHintOnAuthFail = BrowserPassHintCheck.IsChecked == true;
            _settings.Save();
            BroadcastServerSettings();
        }

        private void CheckBrowserPass_Click(object sender, RoutedEventArgs e)
        {
            var email = EmailBox != null ? EmailBox.Text.Trim() : (_settings != null ? _settings.Email : null);

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
                var failMsg = string.Format(UiStrings.T("BrowserPassCheckFail"), err);
                AppendLog(failMsg, LogLevel.Warning);
                ShowBalloon(WinForms.ToolTipIcon.Warning, TruncateBalloon(failMsg));
                MessageBox.Show(this, failMsg,
                    "KAI - AOS Renew", MessageBoxButton.OK, MessageBoxImage.Warning);
                RefreshPasswordHintBanners(false);
                return;
            }

            if (string.IsNullOrEmpty(email) && !string.IsNullOrWhiteSpace(found.Username) && EmailBox != null)
            {
                EmailBox.Text = found.Username.Trim();
                email = EmailBox.Text.Trim();
            }

            var when = FormatBrowserSavedWhen(found.LastSavedLocal);
            var msg = string.Format(UiStrings.T("BrowserPassCheckOk"),
                found.BrowserName, when);
            var hint = string.Format(UiStrings.T("BrowserPassHintNoDate"), found.BrowserName);
            var site = SiteForClipboard(found.SourceUrl);
            var copiedHint = "";
            if (!string.IsNullOrEmpty(site))
            {
                try
                {
                    Clipboard.SetText(site);
                    _lastCopiedSiteForPassCheck = site;
                    copiedHint = string.Format(UiStrings.T("BrowserPassSiteCopiedHint"), site);
                }
                catch { }
            }
            string statusLine = null;
            var statusOk = false;
            var offerFillSave = false;
            var previousPass = GetPasswordUi() ?? "";
            if (found.DecryptStatus == ChromiumCrypto.DecryptStatus.Ok
                && !string.IsNullOrEmpty(found.Password))
            {
                var match = string.Equals(found.Password, previousPass, StringComparison.Ordinal);
                SetPasswordUi(found.Password);
                statusLine = UiStrings.T("BrowserPassDecryptOk") + "\n"
                    + (match ? UiStrings.T("BrowserPassMatch") : UiStrings.T("BrowserPassMismatch"));
                statusOk = match;
                offerFillSave = true;
            }
            // ABE / decrypt failure: no status text in dialog or log (metadata + Open passwords only).
            _cachedBrowserMeta = found;
            _cachedBrowserMetaEmail = email;
            _cachedBrowserMetaUtc = DateTime.UtcNow;
            // App password already equals browser → treat as fresh (clear rotate reminder).
            if (statusOk)
                MarkPasswordFresh("browser match");
            AppendLog(msg, LogLevel.Info);
            if (!string.IsNullOrEmpty(copiedHint))
                AppendLog(copiedHint, LogLevel.Info);
            if (!string.IsNullOrEmpty(statusLine))
                AppendLog(statusLine.Replace("\n", " | "),
                    statusOk ? LogLevel.Success : LogLevel.Warning);
            var dlg = BrowserPassResultWindow.ShowFor(
                this, found.BrowserName, msg, copiedHint, statusLine, statusOk, hint, offerFillSave,
                new BrowserPassResultWindow.Labels
                {
                    WindowTitle = "KAI - AOS Renew",
                    OpenPasswords = string.Format(UiStrings.T("OpenBrowserPasswords"), found.BrowserName ?? ""),
                    OpenFail = UiStrings.T("OpenBrowserPasswordsFail"),
                    FillSave = UiStrings.T("BrowserPassFillSave"),
                    Ok = "OK"
                });
            if (dlg.OpenedPasswordManager)
            {
                _focusPasswordOnActivate = true;
                StartClipboardPasswordWatch();
            }
            RefreshBrowserPassNewerBanner(true);
            RefreshPasswordRotateBanner(false);
            if (dlg.FillAndSaveRequested)
            {
                var pass = found.Password;
                var force = CredentialsWouldChange(pass);
                if (!string.IsNullOrEmpty(pass))
                    SetPasswordUi(pass);
                SaveFromUi(true, pass);
                if (TrySavePreflight())
                    Forget(RunNowAsync(true, force, pass), "IntervalSaveRunNow");
            }
        }

        private void RefreshKaiDiagnostics(bool announce)
        {
            try
            {
                var path = ConfigPathBox != null ? ConfigPathBox.Text.Trim() : (_settings != null ? _settings.ConfigPath : null);
                if (string.IsNullOrWhiteSpace(path) && _settings != null)
                    path = _settings.ConfigPath;

                var install = KaiDiag.ReadInstallation(path);
                KaiInstallText.Text = install.Summary ?? "—";

                var urls = KaiDiag.ReadUrls(path);
                KaiUrlRegisterBox.Text = urls.Register ?? "";
                KaiUrlWebEamBox.Text = urls.WebEam ?? "";
                KaiUrlUploadBox.Text = urls.KeyUpload ?? "";

                RefreshKaiTokenExpiryLabel();
                CheckBmwServices(false, announce);

                if (announce)
                    AppendLog(UiStrings.T("KaiRefreshed"), LogLevel.Info);
            }
            catch (Exception ex)
            {
                KaiInstallText.Text = ex.Message;
                if (announce)
                    AppendLog(ex.Message, LogLevel.Error);
                BroadcastKaiSync();
            }
        }

        private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingUi || _settings == null) return;
            var code = LanguageCombo.SelectedValue as string;
            if (string.IsNullOrEmpty(code)) return;
            _settings.Language = code;
            _settings.Save();
            UiStrings.SetLanguage(code);
            ApplyLanguage();
            BroadcastServerSettings();
        }

        private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingUi || _settings == null) return;
            var code = ThemeCombo.SelectedValue as string;
            if (string.IsNullOrEmpty(code)) return;
            _settings.ThemeMode = ThemeManager.NormalizeMode(code);
            _settings.Save();
            ThemeManager.Apply(_settings.ThemeMode);
            ApplyThemeChrome();
            RefreshStatusBadge();
            RefreshLogView();
            BroadcastServerSettings();
        }

        private void ExportLogForDay(string filePrefix, string title, Func<DateTime, string> collector)
        {
            var dlg = new SaveLogDialog(title, DateTime.Today, UiStrings.T) { Owner = this };
            if (dlg.ShowDialog() != true || !dlg.Confirmed) return;
            var day = dlg.SelectedDay.Date;
            string text;
            try { text = collector(day) ?? ""; }
            catch (Exception ex)
            {
                AppendLog(ex.Message, LogLevel.Error);
                return;
            }
            if (string.IsNullOrWhiteSpace(text))
            {
                AppendLog(UiStrings.T("SaveLogEmpty"), LogLevel.Warning);
                return;
            }
            if (dlg.ToClipboard)
            {
                try
                {
                    Clipboard.SetText(text);
                    AppendLog(string.Format(UiStrings.T("SaveLogCopied"),
                        SaveLogDialog.FormatDayFileName(filePrefix, day)), LogLevel.Success);
                }
                catch (Exception ex)
                {
                    AppendLog(ex.Message, LogLevel.Error);
                }
                return;
            }
            var path = SaveLogDialog.PromptSavePath(this, SaveLogDialog.FormatDayFileName(filePrefix, day));
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                File.WriteAllText(path, text, Encoding.UTF8);
                AppendLog(string.Format(UiStrings.T("SaveLogSaved"), path), LogLevel.Success);
            }
            catch (Exception ex)
            {
                AppendLog(ex.Message, LogLevel.Error);
            }
        }

        private void SyncStartupFromRegistry()
        {
            var inRegistry = IsStartupEnabled();
            if (_settings.StartWithWindows && !inRegistry)
            {
                ApplyStartup(true);
                AppendLog("Autostart: re-applied in registry.", true);
            }
            else if (!_settings.StartWithWindows && inRegistry)
            {
                _loadingUi = true;
                try
                {
                    StartWithWindowsCheck.IsChecked = true;
                    _settings.StartWithWindows = true;
                    _settings.Save();
                }
                finally
                {
                    _loadingUi = false;
                }
                AppendLog("Autostart: found in registry, checkbox synced.", true);
            }
        }

        private static bool IsStartupEnabled()
        {
            const string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
            using (var key = Registry.CurrentUser.OpenSubKey(keyPath, false))
            {
                if (key == null) return false;
                return key.GetValue("KaiAosRenew") != null;
            }
        }

        private void SaveFromUi(bool announce, string passwordOverride = null)
        {
            _settings.Email = EmailBox.Text.Trim();
            var newPass = (passwordOverride ?? GetPasswordUi() ?? "").Trim();
            if (passwordOverride != null)
                SetPasswordUi(newPass);
            string oldPass = null;
            try { oldPass = _settings.GetPassword(); }
            catch { oldPass = null; }
            // Explicit override (Paste & Save / Fill & Save) always counts as a password refresh
            // so the rotate-reminder banner clears even if settings already held the same value.
            var passwordChanged = !string.Equals(newPass ?? "", oldPass ?? "", StringComparison.Ordinal);
            if (passwordOverride != null || passwordChanged)
                _settings.LastPasswordChangeUtc = DateTime.UtcNow;
            // Clear auto-Register block only when the user actually changes the password.
            if (passwordChanged)
                _settings.AuthRegisterBlocked = false;
            _settings.SetPassword(newPass);
            int hours;
            if (!int.TryParse(IntervalBox.Text.Trim(), out hours) || hours < 1)
                hours = 1;
            if (hours > 168) hours = 168;
            IntervalBox.Text = hours.ToString();
            _settings.IntervalHours = hours;
            var path = ConfigPathBox.Text.Trim();
            if (IsValidConfigPath(path))
            {
                _settings.ConfigPath = path;
                _lastValidConfigPath = path;
            }
            else
            {
                ConfigPathBox.Text = _lastValidConfigPath ?? _settings.ConfigPath;
            }
            _settings.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
            _settings.UseSystemProxy = UseSystemProxyCheck.IsChecked == true;
            if (BrowserPassHintCheck != null)
                _settings.BrowserPasswordHintOnAuthFail = BrowserPassHintCheck.IsChecked == true;
            if (KeysBalloonCheck != null)
                _settings.KeysBalloonEnabled = KeysBalloonCheck.IsChecked == true;
            SyncKeysVinBmwFailOnlyUi();
            if (KeysVinBmwFailOnlyCheck != null && KeysVinBmwFailOnlyCheck.IsEnabled)
                _settings.KeysVinTipBmwFailOnly = KeysVinBmwFailOnlyCheck.IsChecked == true;
            else
                _settings.KeysVinTipBmwFailOnly = false;
            if (KeysStallAlertCheck != null)
                _settings.KeysStallAlertEnabled = KeysStallAlertCheck.IsChecked == true;
            if (KeysAutoCopyVinCheck != null && KeysAutoCopyVinCheck.IsEnabled)
                _settings.KeysAutoCopyVin = KeysAutoCopyVinCheck.IsChecked == true;
            else
                _settings.KeysAutoCopyVin = false;
            if (NotifyServerEnableCheck != null)
                _settings.NotifyServerEnabled = NotifyServerEnableCheck.IsChecked == true;
            ParseAndSetNotifyServerPort();
            ParseAndSetNotifyPairingCode();
            CaptureFirebasePushFromUi();
            if (KeysStallMinutesBox != null)
            {
                int stall;
                if (!int.TryParse(KeysStallMinutesBox.Text.Trim(), out stall) || stall < 1)
                    stall = 2;
                if (stall > 60) stall = 60;
                _settings.KeysStallMinutes = stall;
                KeysStallMinutesBox.Text = stall.ToString();
            }
            _settings.LogVerbose = true;
            if (MinimizeOnCloseCheck != null)
                _settings.MinimizeOnClose = MinimizeOnCloseCheck.IsChecked == true;
            if (AutoUpdateCheck != null)
                _settings.AutoUpdate = AutoUpdateCheck.IsChecked == true;
            ParseAndSetAuthCooldownMinutes();
            ParseAndSetAuthRegisterBlockAfter();
            ParseAndSetKeysRefreshSeconds();
            ParseAndSetKeysVinTipSeconds();
            if (KeysUploaderLogDirBox != null && !string.IsNullOrWhiteSpace(KeysUploaderLogDirBox.Text))
                _settings.KeysUploaderLogDir = KeysUploaderLogDirBox.Text.Trim();
            if (KeysKaiLogDirBox != null && !string.IsNullOrWhiteSpace(KeysKaiLogDirBox.Text))
                _settings.KeysKaiLogDir = KeysKaiLogDirBox.Text.Trim();
            if (KeysSqlitePathBox != null && !string.IsNullOrWhiteSpace(KeysSqlitePathBox.Text))
                _settings.KeysSqlitePath = KeysSqlitePathBox.Text.Trim();
            _settings.NormalizeClamps();
            ApplyKeysMonitorSettings();
            SaveKeyReaderPathsFromUi(true);
            var lang = LanguageCombo.SelectedValue as string;
            if (!string.IsNullOrEmpty(lang))
                _settings.Language = lang;
            _settings.Save();
            ApplyStartup(_settings.StartWithWindows);
            ApplyNotifyServerFromSettings();
            CaptureAosSavedCredentials();
            _aosCredentialsDirty = false;
            UpdateAosDirtyStatus();
            if (announce)
                AppendLog(UiStrings.T("SettingsSaved"), true);
            RefreshPasswordHintBanners(false);
            BroadcastServerSettings();
        }

        private void TrySilentBrowserPassRefresh(bool force)
        {
            if (_settings == null) return;
            if (!force
                && _lastSilentBrowserCheckUtc != DateTime.MinValue
                && (DateTime.UtcNow - _lastSilentBrowserCheckUtc) < TimeSpan.FromMinutes(30))
            {
                return;
            }
            var email = EmailBox != null ? EmailBox.Text.Trim() : (_settings.Email ?? "").Trim();
            if (string.IsNullOrEmpty(email))
                email = (_settings.Email ?? "").Trim();
            if (string.IsNullOrEmpty(email))
                return;

            _lastSilentBrowserCheckUtc = DateTime.UtcNow;
            try
            {
                TryGetBrowserMeta(email, true);
                RefreshBrowserPassNewerBanner(true);
            }
            catch { }
        }

        private BrowserPasswordReader.Result TryGetBrowserMeta(string email, bool forceRefresh)
        {
            if (string.IsNullOrWhiteSpace(email))
                return null;
            email = email.Trim();
            if (!forceRefresh
                && _cachedBrowserMeta != null
                && string.Equals(_cachedBrowserMetaEmail, email, StringComparison.OrdinalIgnoreCase)
                && (DateTime.UtcNow - _cachedBrowserMetaUtc).TotalMinutes < 5)
            {
                return _cachedBrowserMeta;
            }
            try
            {
                _cachedBrowserMeta = BrowserPasswordReader.TryFindBmwAosLoginMeta(email);
            }
            catch (Exception ex)
            {
                _cachedBrowserMeta = new BrowserPasswordReader.Result { Error = ex.Message };
            }
            _cachedBrowserMetaEmail = email;
            _cachedBrowserMetaUtc = DateTime.UtcNow;
            return _cachedBrowserMeta;
        }

        private void RefreshBrowserPassNewerBanner(bool forceRefresh)
        {
            if (BrowserPassNewerBanner == null || BrowserPassNewerText == null)
                return;
            try
            {
                if (_settings == null || !_settings.LastSuccessUtc.HasValue)
                {
                    BrowserPassNewerBanner.Visibility = Visibility.Collapsed;
                    return;
                }
                var email = EmailBox != null ? EmailBox.Text.Trim() : (_settings.Email ?? "").Trim();
                if (string.IsNullOrEmpty(email))
                {
                    BrowserPassNewerBanner.Visibility = Visibility.Collapsed;
                    return;
                }
                var meta = TryGetBrowserMeta(email, forceRefresh);
                if (meta == null || !meta.Found || !meta.LastSavedLocal.HasValue)
                {
                    BrowserPassNewerBanner.Visibility = Visibility.Collapsed;
                    return;
                }
                var lastSuccessLocal = _settings.LastSuccessUtc.Value.ToLocalTime();
                if (meta.LastSavedLocal.Value <= lastSuccessLocal)
                {
                    BrowserPassNewerBanner.Visibility = Visibility.Collapsed;
                    return;
                }
                BrowserPassNewerText.Text = string.Format(UiStrings.T("BrowserPassNewer"),
                    FormatBrowserSavedWhen(meta.LastSavedLocal));
                BrowserPassNewerBanner.Visibility = Visibility.Visible;
                MaybeTrayHint(BrowserPassNewerText.Text, ref _lastBrowserNewerBalloonUtc, TimeSpan.FromHours(6));
            }
            catch
            {
                BrowserPassNewerBanner.Visibility = Visibility.Collapsed;
            }
        }

        private static string FormatBrowserSavedWhen(DateTime? local)
        {
            if (!local.HasValue)
                return "?";
            var age = DateTime.Now - local.Value;
            if (age < TimeSpan.Zero)
                age = TimeSpan.Zero;
            return string.Format(UiStrings.T("TimeAgo"), FormatAge(age));
        }

        private void OpenLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var path = AppSettings.LogPath;
                if (!File.Exists(path))
                    RenewEngine.Log("Log created.");
                Process.Start(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(UiStrings.T("CannotOpenLog"), ex.Message), "KAI - AOS Renew",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveFromUi(false);
                var dlg = new SaveFileDialog();
                dlg.Filter = "XML settings|*.xml|All|*.*";
                dlg.FileName = "KaiAosRenew-settings.xml";
                dlg.Title = UiStrings.T("ExportTitle");
                if (dlg.ShowDialog(this) != true) return;
                _settings.ExportPortable(dlg.FileName);
                AppendLog(string.Format(UiStrings.T("ExportOk"), dlg.FileName));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(UiStrings.T("ExportFail"), ex.Message), "KAI - AOS Renew",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new OpenFileDialog();
                dlg.Filter = "XML settings|*.xml|All|*.*";
                dlg.Title = UiStrings.T("ImportTitle");
                if (dlg.ShowDialog(this) != true) return;
                _settings.ImportPortable(dlg.FileName);
                _settings.Save();
                _loadingUi = true;
                try
                {
                    EmailBox.Text = _settings.Email ?? "";
                    var hours = _settings.IntervalHours <= 0 ? 1 : _settings.IntervalHours;
                    if (hours < 1) hours = 1;
                    if (hours > 168) hours = 168;
                    IntervalBox.Text = hours.ToString();
                    ConfigPathBox.Text = _settings.ConfigPath ?? "";
                    _lastValidConfigPath = _settings.ConfigPath;
                    StartWithWindowsCheck.IsChecked = _settings.StartWithWindows;
                    MinimizeToTrayCheck.IsChecked = _settings.MinimizeToTray;
                    UpdateMinimizeToTrayEnabled();
                    UseSystemProxyCheck.IsChecked = _settings.UseSystemProxy;
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
                    if (string.IsNullOrWhiteSpace(_settings.KeysPickupFolder))
                        _settings.KeysPickupFolder = KeyActivityMonitor.DefaultPickupFolder;
                    _settings.NormalizeClamps();
                    if (KeysStallMinutesBox != null)
                        KeysStallMinutesBox.Text = _settings.KeysStallMinutes.ToString();
                    LoadKeyReaderPathsToUi();
                    LoadKeysDataPathsToUi();
                    try
                    {
                        var kr = DesktopKeyReaderReg.ReadAbsolute();
                        if (!string.IsNullOrEmpty(kr.DataBin))
                        {
                            var dir = Path.GetDirectoryName(kr.DataBin);
                            if (!string.IsNullOrEmpty(dir))
                                _settings.KeysPickupFolder = dir;
                        }
                    }
                    catch { }
                    var lang = string.IsNullOrWhiteSpace(_settings.Language) ? "en" : _settings.Language.Trim();
                    UiStrings.SetLanguage(lang);
                    LanguageCombo.SelectedValue = lang;
                    var theme = ThemeManager.NormalizeMode(_settings.ThemeMode);
                    _settings.ThemeMode = theme;
                    ThemeCombo.SelectedValue = theme;
                    ThemeManager.Apply(theme);
                    ApplyThemeChrome();
                    ApplyLanguage();
                    RefreshKaiDiagnostics(false);
                    RefreshPasswordHintBanners(false);
                    ApplyKeysMonitorSettings();
                }
                finally
                {
                    _loadingUi = false;
                }
                ApplyStartup(_settings.StartWithWindows);
                LoadFirebasePushToUi();
                ApplyFirebasePushFromSettings(true);
                AppendLog(string.Format(UiStrings.T("ImportOk"), dlg.FileName));
                BroadcastServerSettings();
                BroadcastAosSync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(UiStrings.T("ImportFail"), ex.Message), "KAI - AOS Renew",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static void ApplyStartup(bool enable)
        {
            const string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
            using (var key = Registry.CurrentUser.OpenSubKey(keyPath, true))
            {
                if (key == null) return;
                const string name = "KaiAosRenew";
                var exe = Process.GetCurrentProcess().MainModule.FileName;
                if (enable)
                    key.SetValue(name, "\"" + exe + "\" --minimized");
                else if (key.GetValue(name) != null)
                    key.DeleteValue(name, false);
            }
        }

    }
}
