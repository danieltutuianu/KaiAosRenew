using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
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
        private sealed class LangItem
        {
            public string Name { get; set; }
            public string Code { get; set; }
        }

        private enum LogLevel
        {
            Info,
            Success,
            Warning,
            Error,
            Verbose
        }

        private sealed class LogEntry
        {
            public DateTime Time { get; set; }
            public LogLevel Level { get; set; }
            public string Text { get; set; }
        }

        private sealed class RegisterHistoryRow
        {
            public DateTime At { get; set; }
            public bool Ok { get; set; }
            public string Text { get; set; }
            public string AgeText { get; set; }
            public string ResultText { get; set; }

            public string TimeText
            {
                get { return At.ToString("yyyy-MM-dd HH:mm:ss"); }
            }
        }

        private readonly DispatcherTimer _timer = new DispatcherTimer();
        private readonly List<LogEntry> _logEntries = new List<LogEntry>();
        private readonly List<RegisterHistoryRow> _registerHistory = new List<RegisterHistoryRow>();
        private const int RegisterHistoryMax = 200;
        private string _registerFilter = "all";
        private bool _registerFilterReady;
        private WinForms.NotifyIcon _tray;
        private Drawing.Icon _appIcon;
        private Drawing.Icon _errorIcon;
        private AppSettings _settings;
        private DateTime _nextDueUtc = DateTime.MaxValue;
        private DateTime _nextHealthUtc = DateTime.MaxValue;
        private DateTime _nextServiceCheckUtc = DateTime.MaxValue;
        private bool _busy;
        private bool _kaiServiceCmdBusy;
        private bool _loadingUi;
        private bool _saveInFlight;
        private DateTime _saveClickUtc = DateTime.MinValue;
        private bool _retrySchedule;
        private bool _hasFailureAlert;
        private bool _timerActive;
        private bool _exitRequested;
        private bool _startedHiddenToTray;
        private HwndSource _hwndSource;
        private string _lastValidConfigPath;
        private string _logFilter = "all";
        private string _lastServiceProblemSignature;
        private bool _serviceCheckDoneOnce;
        private bool? _tokenFooterOk;
        private string _footerStatusMode = "stopped";
        private string _footerTokenMode = "none";
        private string _debugTokenModeKey;
        private string _footerSvcMode = "none";
        private bool _footerPublishQueued;
        private bool _aboutVisible;
        private bool _suppressTabChange;
        private int _tabIndexBeforeAbout;
        private bool _kaiServiceAlert;
        private BmwServicesReport _lastBmwReport;
        private bool _renewAlert;
        private bool _alertBlinkOn = true;
        private readonly DispatcherTimer _alertBlinkTimer = new DispatcherTimer();
        private bool _authGuardsBlinkOn = true;
        private bool _authCooldownOn;
        private bool _authBlockedOn;
        private readonly DispatcherTimer _authGuardsBlinkTimer = new DispatcherTimer();
        private bool _servicesFail;
        private bool _servicesBlinkOn = true;
        private readonly DispatcherTimer _servicesBlinkTimer = new DispatcherTimer();
        private string _registerAgeBlinkKey;
        private bool _registerAgeBlinkOn = true;
        private int _registerAgeBlinkTicksLeft;
        private readonly DispatcherTimer _registerAgeBlinkTimer = new DispatcherTimer();
        private BrowserPasswordReader.Result _cachedBrowserMeta;
        private string _cachedBrowserMetaEmail;
        private DateTime _cachedBrowserMetaUtc = DateTime.MinValue;
        private DateTime _lastBrowserNewerBalloonUtc = DateTime.MinValue;
        private DateTime _lastSilentBrowserCheckUtc = DateTime.MinValue;
        private bool _hintTrayIcon;
        private bool _powerEventsHooked;
        private bool? _lastOsLight;
        private volatile string _uiOp = "";
        private bool _focusPasswordOnActivate;
        private string _lastCopiedSiteForPassCheck;
        private string _clipboardPassCandidate;
        private string _pendingForcePassword;
        private bool _pendingForceRegister;
        private bool _pendingIgnoreAuthGuards;
        private bool _pendingLockRegisterAfter;
        private bool _pendingStartTimerOnSuccess;
        private readonly KeyActivityMonitor _keyMonitor = new KeyActivityMonitor();
        private readonly KeyReaderLogTailer _keyReaderLogTailer = new KeyReaderLogTailer();
        private readonly DispatcherTimer _keysRefreshTimer = new DispatcherTimer();
        private readonly DispatcherTimer _keysHighlightTimer = new DispatcherTimer();
        private bool _keysAlert;
        private List<KeyActivityEvent> _keysAllEvents = new List<KeyActivityEvent>();
        private string _keysFilter = "all";
        private string _keysSearchText = "";
        private string _keysLastAppliedSearch = "";
        private bool _keysFilterReady;
        private string _keysHighlightVin;
        private ListViewItem _keysHighlightedItem;
        private DateTime _keysHighlightUntilUtc = DateTime.MinValue;
        private const double KeysHighlightSeconds = 20;
        private string _keysSortColumn = "";
        private bool _keysSortAscending;
        private int _keysRefreshBusy;
        private bool _keysRefreshCoalesce;
        private bool _keysRefreshAnnouncePending;
        private string _trayVinMenuSignature;
        private string _lastKeySyncSignature;
        /// <summary>After Clear Keys: hide events at or before this local time; never push empty keySync.</summary>
        private DateTime? _keysClearedAfterLocal;
        private Button _keysCopyFeedbackBtn;
        private readonly DispatcherTimer _keysCopyFeedbackTimer = new DispatcherTimer();
        private readonly DispatcherTimer _intervalDebounceTimer = new DispatcherTimer();
        private readonly DispatcherTimer _updatePeekTimer = new DispatcherTimer();
        private UpdateInfo _pendingUpdate;
        private bool _updateCheckBusy;
        private bool _remoteServerUpdateBusy;
        private string _feedServerVer = "";
        private string _feedClientVer = "";
        private readonly HashSet<int> _clientsUpdating = new HashSet<int>();
        private System.Drawing.Icon _outdatedIcon;
        private bool _debugMode;
        private int _aboutVersionClicks;
        private DateTime _aboutVersionClickUtc = DateTime.MinValue;
        private DateTime _lastKeys403RemediateUtc = DateTime.MinValue;
        private string _lastKeys403RemediateKey;
        private readonly HashSet<string> _bmwFailVinTipKeys = new HashSet<string>(StringComparer.Ordinal);
        private string _pending403RetryVin;
        private string _pending403RetryReaderId;
        private bool _keysRetryBusy;
        private static readonly SolidColorBrush KeysSearchHitBrush =
            new SolidColorBrush(Color.FromArgb(0xFF, 0xF9, 0xE2, 0xA8));
        private static readonly SolidColorBrush KeysCopyOkBrush =
            new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
        private const string KeysCopyGlyph = "\uE8C8";
        private const string KeysCopyOkGlyph = "\uE73E";

        public MainWindow()
        {
            InitializeComponent();
            if (KeysSearchHitBrush.CanFreeze) KeysSearchHitBrush.Freeze();
            if (KeysCopyOkBrush.CanFreeze) KeysCopyOkBrush.Freeze();

            InitLanguageCombo();
            InitThemeCombo();
            InitLogFilterCombo();
            if (KeysEventsList != null)
                KeysEventsList.AddHandler(GridViewColumnHeader.ClickEvent,
                    new RoutedEventHandler(KeysColumnHeader_Click));
            LogBox.Document = new FlowDocument
            {
                PagePadding = new Thickness(6),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11
            };

            _timer.Interval = TimeSpan.FromSeconds(30);
            _timer.Tick += Timer_Tick;
            _alertBlinkTimer.Interval = TimeSpan.FromMilliseconds(450);
            _alertBlinkTimer.Tick += AlertBlinkTimer_Tick;
            _authGuardsBlinkTimer.Interval = TimeSpan.FromMilliseconds(500);
            _authGuardsBlinkTimer.Tick += AuthGuardsBlinkTimer_Tick;
            _servicesBlinkTimer.Interval = TimeSpan.FromMilliseconds(500);
            _servicesBlinkTimer.Tick += ServicesBlinkTimer_Tick;
            _registerAgeBlinkTimer.Interval = TimeSpan.FromMilliseconds(450);
            _registerAgeBlinkTimer.Tick += RegisterAgeBlinkTimer_Tick;
            _keysRefreshTimer.Interval = TimeSpan.FromSeconds(3);
            _keysRefreshTimer.Tick += KeysRefreshTimer_Tick;
            _keysHighlightTimer.Tick += KeysHighlightTimer_Tick;
            _keysCopyFeedbackTimer.Interval = TimeSpan.FromMilliseconds(1500);
            _keysCopyFeedbackTimer.Tick += KeysCopyFeedbackTimer_Tick;
            _intervalDebounceTimer.Interval = TimeSpan.FromMilliseconds(500);
            _intervalDebounceTimer.Tick += IntervalDebounceTimer_Tick;
            _updatePeekTimer.Interval = TimeSpan.FromHours(6);
            _updatePeekTimer.Tick += UpdatePeekTimer_Tick;

            InitTray();
            LoadSettingsToUi();
            ApplyKeysRefreshInterval();
            StartMenuShortcut.Ensure("KAI - AOS Renew", ThemeManager.AppUserModelId);
            SyncStartupFromRegistry();
            if (_settings.ConsecutiveFailures > 0)
                ApplyFailureAlert(_settings.LastMessage ?? "Last run failed.");
            SetTimerStatus(false);
            SeedRegisterTimesFromLog();
            SeedRegisterActivityFromRenewLog();
            LoadRegisterHistoryFromLog();
            InitRegisterFilterCombo();
            UpdateTokenAgeText();
            InitTokenFooterFromSettings();
            _debugMode = _settings != null && _settings.DebugMode;
            ApplyDebugModeUi();
            ApplyTabLayoutVisibility();
            LoadAboutChangelog();
            HookPowerEvents();
            ApplyKeysMonitorSettings();
            NotifyServer.Instance.ClientConnected += OnNotifyClientConnected;
            NotifyServer.Instance.ClientDisconnected += OnNotifyClientDisconnected;
            NotifyServer.Instance.MessageFromClient += OnNotifyMessageFromClient;
            ApplyNotifyServerFromSettings();
            _keyMonitor.Start(OnKeyActivityChanged);
            StartKeyReaderLogTailer();
            StartUiHangWatchdog();
            StartShowWindowListener();
            AppendLog(UiStrings.T("Ready"));
        }

        /// <summary>
        /// Pin/taskbar second instance signals Local\KaiAosRenew.ShowWindow; restore without relying on HWND alone.
        /// </summary>
        private void StartShowWindowListener()
        {
            var ev = Program.ShowWindowEvent;
            if (ev == null) return;
            var dispatcher = Dispatcher;
            var t = new System.Threading.Thread(delegate()
            {
                while (!_exitRequested)
                {
                    try
                    {
                        if (!ev.WaitOne(500)) continue;
                        try
                        {
                            dispatcher.BeginInvoke(new Action(ShowFromTray));
                        }
                        catch { return; }
                    }
                    catch { return; }
                }
            });
            t.IsBackground = true;
            t.Name = "KaiAosRenew.ShowWindow";
            t.Start();
        }

        private void StartUiHangWatchdog()
        {
            var dispatcher = Dispatcher;
            var t = new System.Threading.Thread(delegate()
            {
                var pong = new System.Threading.ManualResetEvent(false);
                while (!_exitRequested)
                {
                    System.Threading.Thread.Sleep(500);
                    pong.Reset();
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        dispatcher.BeginInvoke(new Action(() => pong.Set()), DispatcherPriority.Input);
                    }
                    catch { return; }
                    if (pong.WaitOne(2000)) continue;
                    // Log immediately so killing a frozen process never loses evidence.
                    var op = _uiOp;
                    WriteUiHangLine(sw.ElapsedMilliseconds, op + " (input starved)");
                    if (pong.WaitOne(58000))
                        WriteUiHangLine(sw.ElapsedMilliseconds, op + " (recovered)");
                }
            });
            t.IsBackground = true;
            t.Name = "UiHangWatchdog";
            t.Start();
        }

        private static void WriteUiHangLine(long ms, string op)
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KaiAosRenew");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "ui-hang.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  UI blocked " + ms + " ms; op=" +
                    (string.IsNullOrEmpty(op) ? "?" : op) + Environment.NewLine);
            }
            catch { }
        }

        /// <summary>True when Settings want the window hidden on launch (with Start with Windows).</summary>
        public bool PreferStartHidden
        {
            get
            {
                return _settings != null
                    && _settings.StartWithWindows
                    && _settings.MinimizeToTray;
            }
        }

        public void MarkStartedHiddenToTray()
        {
            _startedHiddenToTray = true;
        }

        private void HookPowerEvents()
        {
            if (_powerEventsHooked) return;
            try
            {
                SystemEvents.PowerModeChanged += OnPowerModeChanged;
                _powerEventsHooked = true;
            }
            catch { }
        }

        private void UnhookPowerEvents()
        {
            if (!_powerEventsHooked) return;
            try { SystemEvents.PowerModeChanged -= OnPowerModeChanged; }
            catch { }
            _powerEventsHooked = false;
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode != PowerModes.Resume) return;
            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var delay = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
                    delay.Tick += delegate
                    {
                        delay.Stop();
                        OnWakeFromSleep();
                    };
                    delay.Start();
                }));
            }
            catch { }
        }

        private void OnWakeFromSleep()
        {
            if (_busy || _settings == null) return;
            if (string.IsNullOrEmpty(_settings.Email) || string.IsNullOrEmpty(_settings.GetPassword()))
                return;

            var renewDue = false;
            if (_timerActive && _nextDueUtc != DateTime.MaxValue && DateTime.UtcNow >= _nextDueUtc)
                renewDue = true;
            else if (_settings.LastSuccessUtc.HasValue)
            {
                var hours = _settings.IntervalHours < 1 ? 1 : _settings.IntervalHours;
                if (DateTime.UtcNow >= _settings.LastSuccessUtc.Value.AddHours(hours))
                    renewDue = true;
            }
            else
                renewDue = true;

            if (renewDue)
                AppendLog(UiStrings.T("WakeRenewDue"), LogLevel.Info);
            else
                AppendLog(UiStrings.T("WakeHealth"), LogLevel.Info);
            RunAutoHealthWithLimitedRegister();
        }

        internal static string AppVersionDisplay
        {
            get
            {
                try
                {
                    var v = Assembly.GetExecutingAssembly().GetName().Version;
                    if (v == null) return "1.2.0";
                    return v.Major + "." + v.Minor + "." + v.Build;
                }
                catch
                {
                    return "1.2.0";
                }
            }
        }

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

        private void InitLogFilterCombo()
        {
            RefreshLogFilterComboItems();
            LogFilterCombo.SelectedValue = "all";
            _logFilter = "all";
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

        private void RefreshLogFilterComboItems()
        {
            var selected = LogFilterCombo.SelectedValue as string ?? _logFilter;
            // Keep in sync with KaiAosNotifyClient RefreshLogFilterComboItems order.
            LogFilterCombo.ItemsSource = new List<LangItem>
            {
                new LangItem { Name = UiStrings.T("LogAll"), Code = "all" },
                new LangItem { Name = UiStrings.T("LogInfo"), Code = "info" },
                new LangItem { Name = UiStrings.T("LogSuccess"), Code = "success" },
                new LangItem { Name = UiStrings.T("LogWarning"), Code = "warning" },
                new LangItem { Name = UiStrings.T("LogError"), Code = "error" },
                new LangItem { Name = UiStrings.T("LogRegistration"), Code = "registration" },
                new LangItem { Name = UiStrings.T("LogKeyReader"), Code = "keyreader" }
            };
            LogFilterCombo.SelectedValue = selected;
        }

        private void ApplyLanguage()
        {
            Title = UiStrings.T("WindowTitle");
            HeaderTitleText.Text = UiStrings.T("HeaderTitle");
            SectionScheduleText.Text = UiStrings.T("SectionSchedule");
            IntervalLabel.Text = UiStrings.T("IntervalHours");
            StartWithWindowsLabel.Text = UiStrings.T("StartWithWindows");
            StartWithWindowsCheck.Content = UiStrings.T("Yes");
            MinimizeToTrayLabel.Text = UiStrings.T("MinimizeToTray");
            MinimizeToTrayCheck.Content = UiStrings.T("Yes");
            if (LogVerboseCheck != null)
                LogVerboseCheck.Content = UiStrings.T("LogVerboseOption");
            if (AuthCooldownMinutesLabel != null)
                AuthCooldownMinutesLabel.Text = UiStrings.T("AuthCooldownMinutes");
            if (MinimizeOnCloseLabel != null)
                MinimizeOnCloseLabel.Text = UiStrings.T("MinimizeOnClose");
            if (MinimizeOnCloseCheck != null)
                MinimizeOnCloseCheck.Content = UiStrings.T("MinimizeOnCloseYes");
            if (AutoUpdateLabel != null)
                AutoUpdateLabel.Text = UiStrings.T("AutoUpdate");
            if (AutoUpdateCheck != null)
            {
                AutoUpdateCheck.Content = UiStrings.T("Yes");
                AutoUpdateCheck.ToolTip = UiStrings.T("TipAutoUpdate");
            }
            if (CheckForUpdatesBtn != null)
                CheckForUpdatesBtn.Content = UiStrings.T("CheckForUpdates");
            if (_pendingUpdate != null)
                ShowPendingUpdate(_pendingUpdate);
            else if (_debugMode)
                ShowForcedDebugUpdateUi();
            else if (FooterUpdateBtn != null)
                FooterUpdateBtn.Content = UiStrings.T("Update");
            if (SectionConfigText != null) SectionConfigText.Text = UiStrings.T("SectionConfig");
            if (ConfigPathLabel != null) ConfigPathLabel.Text = UiStrings.T("ConfigPath");
            if (BrowseBtn != null) BrowseBtn.Content = UiStrings.T("Browse");
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
            OpenLogBtn.Content = UiStrings.T("OpenLog");
            ExportBtn.Content = UiStrings.T("ExportSettings");
            ImportBtn.Content = UiStrings.T("ImportSettings");
            SectionAosText.Text = UiStrings.T("SectionAos");
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
                        string.IsNullOrEmpty(current) ? "—" : current);
                if (ClipboardPassClipText != null)
                    ClipboardPassClipText.Text = string.Format(UiStrings.T("ClipboardPassClip"),
                        _clipboardPassCandidate);
            }
            if (CheckBrowserPassBtn != null)
                CheckBrowserPassBtn.Content = UiStrings.T("CheckBrowserPass");
            if (OpenBmwSsoBtn != null)
                OpenBmwSsoBtn.Content = UiStrings.T("OpenBmwSso");
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
            if (AboutSettingsPathText != null)
                AboutSettingsPathText.Text = UiStrings.T("AboutSettingsPath") + " " + AppSettings.SettingsPath;
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
            if (NotifyServerPortLabel != null)
                NotifyServerPortLabel.Text = UiStrings.T("NotifyServerPort");
            if (NotifyServerIpLabel != null)
                NotifyServerIpLabel.Text = UiStrings.T("NotifyServerIpLabel");
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
            SaveAosBtn.Content = UiStrings.T("Save");
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
            RefreshAuthCooldownBanner();
            ActivityLogLabel.Text = UiStrings.T("ActivityLog");
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

        private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ReferenceEquals(e.OriginalSource, MainTabs)) return;
            if (_suppressTabChange) return;
            if (_aboutVisible && TabDummy != null && !TabDummy.IsSelected)
            {
                // User clicked Renew or KAI — leave About
                _aboutVisible = false;
                if (AboutPanel != null) AboutPanel.Visibility = Visibility.Collapsed;
                if (AboutTabBtn != null) AboutTabBtn.Tag = null;
            }
            ApplyTabLayoutVisibility();
            if (!_aboutVisible && TabKai != null && TabKai.IsSelected)
                CheckBmwServices(false, false);
            UpdateKeysTabPolling();
            if (!_aboutVisible && TabKeys != null && TabKeys.IsSelected)
                RefreshKeysTab(false);
            if (!_aboutVisible && TabActivity != null && TabActivity.IsSelected)
                ScrollActivityLogToEnd();
            if (!_aboutVisible && TabRegister != null && TabRegister.IsSelected)
            {
                RefreshRegisterAges();
                ScrollRegisterHistoryToEnd();
            }
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

        /// <summary>First visible non-dummy tab (currently Keys) — always the startup/default tab.</summary>
        private int GetDefaultTabIndex()
        {
            if (MainTabs == null) return -1;
            for (var i = 0; i < MainTabs.Items.Count; i++)
            {
                var ti = MainTabs.Items[i] as TabItem;
                if (ti == null || ReferenceEquals(ti, TabDummy)) continue;
                if (ti.Visibility == Visibility.Collapsed) continue;
                return i;
            }
            return MainTabs.Items.Count > 1 ? 1 : 0;
        }

        private void SelectDefaultTab()
        {
            if (MainTabs == null) return;
            var idx = GetDefaultTabIndex();
            if (idx >= 0 && idx < MainTabs.Items.Count)
                MainTabs.SelectedIndex = idx;
        }

        private void ApplyTabLayoutVisibility()
        {
            if (ButtonsBorder == null) return;
            var showButtons = !_aboutVisible && TabRenew != null && TabRenew.IsSelected;
            ButtonsBorder.Visibility = showButtons ? Visibility.Visible : Visibility.Collapsed;
            if (!_aboutVisible && TabActivity != null && TabActivity.IsSelected)
                ScrollActivityLogToEnd();
            if (!_aboutVisible && TabRegister != null && TabRegister.IsSelected)
            {
                RefreshRegisterAges();
                ScrollRegisterHistoryToEnd();
            }
            UpdateKeysTabPolling();
        }

        private void ScrollActivityLogToEnd()
        {
            if (LogBox == null) return;
            // Defer until layout is ready — immediate ScrollToEnd often no-ops on tab switch.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (LogBox == null) return;
                    LogBox.UpdateLayout();
                    LogBox.ScrollToEnd();
                }
                catch { }
            }), DispatcherPriority.Loaded);
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
            if (RegisterColTime != null) RegisterColTime.Header = UiStrings.T("RegisterColTime");
            if (RegisterColResult != null) RegisterColResult.Header = UiStrings.T("RegisterColResult");
            if (RegisterColAge != null) RegisterColAge.Header = UiStrings.T("RegisterColAge");
            if (RegisterColDetail != null) RegisterColDetail.Header = UiStrings.T("RegisterColDetail");
            InitRegisterFilterCombo();
            RefreshRegisterResultTexts();
            ApplyRegisterFilter();
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
                new LangItem { Code = "fail", Name = UiStrings.T("RegisterFilterFail") }
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

        private void ApplyKeysTabStrings()
        {
            if (KeysRefreshBtn != null) KeysRefreshBtn.Content = UiStrings.T("KeysRefresh");
            if (KeysClearBtn != null) KeysClearBtn.Content = UiStrings.T("ClearKeys");
            if (KeysRetryBmwBtn != null)
            {
                KeysRetryBmwBtn.Content = UiStrings.T("KeysRetryBmw");
                KeysRetryBmwBtn.ToolTip = UiStrings.T("KeysRetryBmwTip");
            }
            if (KeysOpenUploaderLogBtn != null) KeysOpenUploaderLogBtn.Content = UiStrings.T("KeysOpenUploaderLog");
            if (KeysOpenKaiLogBtn != null) KeysOpenKaiLogBtn.Content = UiStrings.T("KeysOpenKaiLog");
            if (KeysFilterLabel != null) KeysFilterLabel.Text = UiStrings.T("KeysFilter");
            if (KeysSearchLabel != null) KeysSearchLabel.Text = UiStrings.T("KeysSearch");
            if (KeysSearchBox != null) KeysSearchBox.ToolTip = UiStrings.T("KeysSearchTip");
            if (KeysReaderSection != null) KeysReaderSection.Text = UiStrings.T("KeysReaderSection");
            if (KeysEventsSection != null) KeysEventsSection.Text = UiStrings.T("KeysEventsSection");
            if (KeysHintText != null) KeysHintText.Text = UiStrings.T("KeysHint");
            if (KeysBalloonCheck != null) KeysBalloonCheck.Content = UiStrings.T("KeysBalloonOption");
            if (KeysVinBmwFailOnlyCheck != null)
                KeysVinBmwFailOnlyCheck.Content = UiStrings.T("KeysVinBmwFailOnly");
            if (KeysStallAlertCheck != null) KeysStallAlertCheck.Content = UiStrings.T("KeysStallAlertOption");
            SyncKeysVinBmwFailOnlyUi();
            if (TestBalloonTipsBtn != null) TestBalloonTipsBtn.Content = UiStrings.T("TrayTestBalloon");
            if (KeysAutoCopyVinCheck != null) KeysAutoCopyVinCheck.Content = UiStrings.T("KeysAutoCopyVin");
            if (KeysColVin != null) KeysColVin.Header = UiStrings.T("KeysColVin");
            if (KeysColPaint != null) KeysColPaint.Header = UiStrings.T("KeysColPaint");
            if (KeysColUpholstery != null) KeysColUpholstery.Header = UiStrings.T("KeysColUpholstery");
            if (KeysColILevel != null) KeysColILevel.Header = UiStrings.T("KeysColILevel");
            if (KeysColReader != null) KeysColReader.Header = UiStrings.T("KeysColReader");
            if (KeysColLocal != null) KeysColLocal.Header = UiStrings.T("KeysColLocal");
            if (KeysColBmw != null) KeysColBmw.Header = UiStrings.T("KeysColBmw");
            if (KeysColDetail != null) KeysColDetail.Header = UiStrings.T("KeysColDetail");
            ApplyKeysSortHeaders();
            InitKeysFilterCombo();
            _trayVinMenuSignature = null; // force rebuild for new language strings
            RebuildTrayMenu();
        }

        private void ApplyKeysSortHeaders()
        {
            if (KeysColTime != null)
            {
                var h = UiStrings.T("KeysColTime");
                if (_keysSortColumn == "time")
                    h += _keysSortAscending ? " ▲" : " ▼";
                KeysColTime.Header = h;
            }
            if (KeysColStatus != null)
            {
                var h = UiStrings.T("KeysColStatus");
                if (_keysSortColumn == "status")
                    h += _keysSortAscending ? " ▲" : " ▼";
                KeysColStatus.Header = h;
            }
        }

        private void KeysColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            var header = e.OriginalSource as GridViewColumnHeader;
            if (header == null || header.Column == null) return;
            string col = null;
            if (ReferenceEquals(header.Column, KeysColTime)) col = "time";
            else if (ReferenceEquals(header.Column, KeysColStatus)) col = "status";
            if (col == null) return;

            if (_keysSortColumn == col)
                _keysSortAscending = !_keysSortAscending;
            else
            {
                _keysSortColumn = col;
                // Time: newest first; Status: ascending then newest first as secondary
                _keysSortAscending = col == "status";
            }
            ApplyKeysSortHeaders();
            ApplyKeysFilter();
        }

        private void KeysHighlightCell_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyKeysSearchHighlight(sender as TextBlock);
        }

        private void KeysHighlightCell_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            ApplyKeysSearchHighlight(sender as TextBlock);
        }

        private void ApplyKeysSearchHighlight(TextBlock tb)
        {
            if (tb == null) return;
            var ev = tb.DataContext as KeyActivityEvent;
            var tag = Convert.ToString(tb.Tag);
            string text;
            if (tag == "ilevel")
                text = ev == null || string.IsNullOrEmpty(ev.ILevelCurrent) ? "—" : ev.ILevelCurrent;
            else
                text = ev == null || string.IsNullOrEmpty(ev.Vin) ? "—" : ev.Vin;

            var q = _keysSearchText;
            if (string.IsNullOrEmpty(q) || text == "—" || text.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
            {
                tb.Inlines.Clear();
                tb.Text = text;
                return;
            }

            tb.Inlines.Clear();
            var idx = 0;
            while (idx < text.Length)
            {
                var hit = text.IndexOf(q, idx, StringComparison.OrdinalIgnoreCase);
                if (hit < 0)
                {
                    tb.Inlines.Add(new Run(text.Substring(idx)));
                    break;
                }
                if (hit > idx)
                    tb.Inlines.Add(new Run(text.Substring(idx, hit - idx)));
                var matchRun = new Run(text.Substring(hit, q.Length));
                matchRun.Background = KeysSearchHitBrush;
                tb.Inlines.Add(matchRun);
                idx = hit + q.Length;
            }
        }

        private static List<KeyActivityEvent> SortKeysEvents(
            IEnumerable<KeyActivityEvent> src, string column, bool ascending)
        {
            var list = new List<KeyActivityEvent>();
            if (src != null)
            {
                foreach (var ev in src)
                    if (ev != null) list.Add(ev);
            }
            if (string.IsNullOrEmpty(column) || list.Count < 2)
                return list;

            list.Sort(delegate(KeyActivityEvent a, KeyActivityEvent b)
            {
                int cmp;
                if (column == "status")
                {
                    cmp = ((int)a.Status).CompareTo((int)b.Status);
                    if (!ascending) cmp = -cmp;
                    if (cmp == 0)
                        cmp = b.DetectedLocal.CompareTo(a.DetectedLocal);
                }
                else
                {
                    cmp = a.DetectedLocal.CompareTo(b.DetectedLocal);
                    if (!ascending) cmp = -cmp;
                }
                return cmp;
            });
            return list;
        }

        private void InitKeysFilterCombo()
        {
            if (KeysFilterCombo == null) return;
            var prev = _keysFilter;
            _keysFilterReady = false;
            KeysFilterCombo.Items.Clear();
            KeysFilterCombo.DisplayMemberPath = "Name";
            KeysFilterCombo.SelectedValuePath = "Code";
            KeysFilterCombo.Items.Add(new LangItem { Code = "all", Name = UiStrings.T("KeysFilterAll") });
            KeysFilterCombo.Items.Add(new LangItem { Code = "today", Name = UiStrings.T("KeysFilterToday") });
            KeysFilterCombo.Items.Add(new LangItem { Code = "failed", Name = UiStrings.T("KeysFilterFailed") });
            KeysFilterCombo.Items.Add(new LangItem { Code = "bmw", Name = UiStrings.T("KeysFilterBmwOk") });
            KeysFilterCombo.Items.Add(new LangItem { Code = "local", Name = UiStrings.T("KeysFilterLocal") });
            KeysFilterCombo.Items.Add(new LangItem { Code = "waiting", Name = UiStrings.T("KeysFilterWaiting") });
            KeysFilterCombo.SelectedValue = prev;
            if (KeysFilterCombo.SelectedValue == null)
                KeysFilterCombo.SelectedValue = "all";
            _keysFilterReady = true;
        }

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

        private void KeysRefreshTimer_Tick(object sender, EventArgs e)
        {
            if (TabKeys != null && TabKeys.IsSelected && !_aboutVisible)
                RefreshKeysTab(false);
        }

        private void OnKeyActivityChanged()
        {
            try
            {
                Dispatcher.BeginInvoke(new Action(() => RefreshKeysTab(false)));
            }
            catch { }
        }

        private void KeysRefresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshKeysTab(true);
        }

        private void KeysClear_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(
                this,
                UiStrings.T("ClearKeysConfirm"),
                UiStrings.T("ClearKeys"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
            if (confirm != MessageBoxResult.Yes)
                return;

            _keysClearedAfterLocal = DateTime.Now;
            _keysAllEvents = new List<KeyActivityEvent>();
            ClearKeysRowHighlight();
            ApplyKeysFilter();
            UpdateKeysTodayBadge(0);
            UpdateKeysRetryButton();
            // Do not broadcast empty keySync — clients keep their existing lists.
            _lastKeySyncSignature = "";
            AppendLog(UiStrings.T("ClearKeysDone"), LogLevel.Info);
        }

        private void KeysOpenUploaderLog_Click(object sender, RoutedEventArgs e)
        {
            var path = _settings != null && !string.IsNullOrWhiteSpace(_settings.KeysUploaderLogDir)
                ? _settings.KeysUploaderLogDir
                : KeyActivityMonitor.DefaultUploaderLogDir;
            OpenFolder(path);
        }

        private void KeysOpenKaiLog_Click(object sender, RoutedEventArgs e)
        {
            var path = _settings != null && !string.IsNullOrWhiteSpace(_settings.KeysKaiLogDir)
                ? _settings.KeysKaiLogDir
                : KeyActivityMonitor.DefaultKaiLogDir;
            OpenFolder(path);
        }

        private void KeysEventsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateKeysRetryButton();
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

        private async void KeysRetryBmw_Click(object sender, RoutedEventArgs e)
        {
            var ev = KeyActivityMonitor.GetNewestEvent(_keysAllEvents);
            await RetryBmwUploadAsync(ev, false);
        }

        private async void RetryBmwUploadAfter403(string vin, string readerId)
        {
            KeyActivityEvent ev = null;
            foreach (var e in _keysAllEvents ?? new List<KeyActivityEvent>())
            {
                if (e == null || string.IsNullOrEmpty(e.Vin)) continue;
                if (!string.Equals(e.Vin, vin, StringComparison.OrdinalIgnoreCase)) continue;
                if (ev == null || e.DetectedLocal > ev.DetectedLocal)
                    ev = e;
            }
            if (ev == null)
            {
                ev = new KeyActivityEvent
                {
                    Vin = vin,
                    ReaderId = readerId,
                    Failed = true,
                    DetectedLocal = DateTime.Now
                };
            }
            else if (!string.IsNullOrEmpty(readerId) && string.IsNullOrEmpty(ev.ReaderId))
                ev.ReaderId = readerId;
            await RetryBmwUploadAsync(ev, true);
        }

        private async System.Threading.Tasks.Task RetryBmwUploadAsync(KeyActivityEvent ev, bool from403Auto)
        {
            if (_keysRetryBusy || _keyMonitor == null) return;
            if (!_keyMonitor.CanRetryBmwUpload(ev))
            {
                AppendLog(UiStrings.T("KeysRetryBmwNoBin"), LogLevel.Warning);
                UpdateKeysRetryButton();
                return;
            }

            _keysRetryBusy = true;
            UpdateKeysRetryButton();
            var vin = ev.Vin;
            var readerId = ev.ReaderId;
            try
            {
                if (from403Auto)
                    AppendLog(UiStrings.T("KeysBmw403RetryUpload"), LogLevel.Info);
                AppendLog(string.Format(UiStrings.T("KeysRetryBmwStart"), vin), LogLevel.Info);

                await System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        var report = ServiceMonitor.Check();
                        if (report != null && !report.AllOk)
                            ServiceMonitor.TryFixAll();
                    }
                    catch { }

                    var pickup = _keyMonitor.ActivePickupFolder;
                    if (string.IsNullOrWhiteSpace(pickup))
                        pickup = KeyActivityMonitor.DefaultPickupFolder;
                    if (!Directory.Exists(pickup))
                        Directory.CreateDirectory(pickup);
                    var dest = Path.Combine(pickup, "keyreader.bin");
                    File.Copy(KeyActivityMonitor.TransferredBin, dest, true);
                    try { File.SetLastWriteTimeUtc(dest, DateTime.UtcNow); }
                    catch { }

                    var probe = KeyUploadProbe.ProbeTransferredBin(_settings, readerId);
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var msg = string.Format(UiStrings.T("KeysRetryBmwProbe"),
                            probe != null ? probe.Summary : "?");
                        AppendLog(msg, probe != null && probe.Ok ? LogLevel.Success : LogLevel.Warning);
                        if (probe != null && !string.IsNullOrEmpty(probe.Body))
                        {
                            RenewEngine.Log("BMW upload probe body: " + probe.Body);
                            AppendLog("BMW response body: " + TruncateForLog(probe.Body, 500), LogLevel.Info);
                        }
                        else if (probe != null)
                            RenewEngine.Log("BMW upload probe: " + probe.Summary);
                    }));
                });

                AppendLog(UiStrings.T("KeysRetryBmwOk"), LogLevel.Success);
                RefreshKeysTab(true);
            }
            catch (Exception ex)
            {
                AppendLog(string.Format(UiStrings.T("LogRetryBmwFail"), ex.Message), LogLevel.Error);
            }
            finally
            {
                _keysRetryBusy = false;
                UpdateKeysRetryButton();
            }
        }

        private static string TruncateForLog(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (s.Length <= max) return s;
            return s.Substring(0, max - 3) + "...";
        }

        private void KeysFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_keysFilterReady || KeysFilterCombo == null) return;
            var code = KeysFilterCombo.SelectedValue as string;
            if (string.IsNullOrEmpty(code)) code = "all";
            _keysFilter = code;
            ApplyKeysFilter();
        }

        private void KeysSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _keysSearchText = KeysSearchBox != null ? (KeysSearchBox.Text ?? "").Trim() : "";
            ApplyKeysFilter();
        }

        private void KeysOptions_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingUi || _settings == null) return;
            if (KeysBalloonCheck != null)
                _settings.KeysBalloonEnabled = KeysBalloonCheck.IsChecked == true;
            SyncKeysVinBmwFailOnlyUi();
            if (KeysVinBmwFailOnlyCheck != null && KeysVinBmwFailOnlyCheck.IsEnabled)
                _settings.KeysVinTipBmwFailOnly = KeysVinBmwFailOnlyCheck.IsChecked == true;
            else
                _settings.KeysVinTipBmwFailOnly = false;
            if (KeysStallAlertCheck != null)
                _settings.KeysStallAlertEnabled = KeysStallAlertCheck.IsChecked == true;
            if (KeysAutoCopyVinCheck != null)
                _settings.KeysAutoCopyVin = KeysAutoCopyVinCheck.IsChecked == true;
            try { _settings.Save(); }
            catch { }
            UpdateKeysAlertFromCache();
        }

        private void SyncKeysVinBmwFailOnlyUi()
        {
            if (KeysVinBmwFailOnlyCheck == null) return;
            var parentOn = KeysBalloonCheck != null && KeysBalloonCheck.IsChecked == true;
            KeysVinBmwFailOnlyCheck.IsEnabled = parentOn;
            if (!parentOn)
            {
                KeysVinBmwFailOnlyCheck.IsChecked = false;
                if (_settings != null)
                    _settings.KeysVinTipBmwFailOnly = false;
            }
        }

        private void ApplyKeysMonitorSettings()
        {
            if (_settings == null) return;
            _settings.NormalizeClamps();
            var folder = string.IsNullOrWhiteSpace(_settings.KeysPickupFolder)
                ? KeyActivityMonitor.DefaultPickupFolder
                : _settings.KeysPickupFolder.Trim();
            _settings.KeysPickupFolder = folder;
            _keyMonitor.Configure(
                folder,
                _settings.KeysStallMinutes,
                _settings.KeysUploaderLogDir,
                _settings.KeysKaiLogDir,
                _settings.KeysSqlitePath);
            ApplyKeysRefreshInterval();
        }

        private void ApplyKeysRefreshInterval()
        {
            if (_settings == null) return;
            var sec = _settings.KeysRefreshSeconds;
            if (sec < 1) sec = 1;
            if (sec > 60) sec = 60;
            _keysRefreshTimer.Interval = TimeSpan.FromSeconds(sec);
        }

        private void KeysStallMinutes_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_loadingUi) return;
            SaveKeysPathSettings();
        }

        private void SaveKeysPathSettings()
        {
            if (_settings == null || _loadingUi) return;
            // Watch folder comes from DataBin parent (Desktop KeyReader Plus).
            var folder = _settings.KeysPickupFolder;
            if (string.IsNullOrWhiteSpace(folder))
                folder = KeyActivityMonitor.DefaultPickupFolder;
            try
            {
                if (KeyReaderDataBinBox != null && !string.IsNullOrWhiteSpace(KeyReaderDataBinBox.Text))
                {
                    var dir = Path.GetDirectoryName(KeyReaderDataBinBox.Text.Trim());
                    if (!string.IsNullOrEmpty(dir))
                        folder = dir;
                }
            }
            catch { }
            var stall = 2;
            if (KeysStallMinutesBox != null)
            {
                int parsed;
                if (!int.TryParse(KeysStallMinutesBox.Text.Trim(), out parsed) || parsed < 1)
                    parsed = 2;
                if (parsed > 60) parsed = 60;
                stall = parsed;
                KeysStallMinutesBox.Text = stall.ToString();
            }
            _settings.KeysPickupFolder = folder;
            _settings.KeysStallMinutes = stall;
            try { _settings.Save(); }
            catch { }
            ApplyKeysMonitorSettings();
            UpdateKeysAlertFromCache();
        }

        private void LoadKeyReaderPathsToUi()
        {
            try
            {
                var paths = DesktopKeyReaderReg.ReadAbsolute();
                if (KeyReaderDataBinBox != null)
                    KeyReaderDataBinBox.Text = paths.DataBin ?? "";
                if (KeyReaderLogPathBox != null)
                    KeyReaderLogPathBox.Text = paths.LogPath ?? "";
                if (KeyReaderEncPathBox != null)
                    KeyReaderEncPathBox.Text = paths.EncPath ?? "";
                RefreshKeyReaderLogLevelCombo();
                if (KeyReaderLogLevelCombo != null)
                    KeyReaderLogLevelCombo.SelectedValue = paths.LogLevel.ToString();
            }
            catch { }
        }

        private void RefreshKeyReaderLogLevelCombo()
        {
            if (KeyReaderLogLevelCombo == null) return;
            var selected = KeyReaderLogLevelCombo.SelectedValue as string;
            KeyReaderLogLevelCombo.ItemsSource = new List<LangItem>
            {
                new LangItem { Name = UiStrings.T("KeyReaderLog0"), Code = "0" },
                new LangItem { Name = UiStrings.T("KeyReaderLog1"), Code = "1" },
                new LangItem { Name = UiStrings.T("KeyReaderLog2"), Code = "2" },
                new LangItem { Name = UiStrings.T("KeyReaderLog3"), Code = "3" },
                new LangItem { Name = UiStrings.T("KeyReaderLog4"), Code = "4" }
            };
            if (!string.IsNullOrEmpty(selected))
                KeyReaderLogLevelCombo.SelectedValue = selected;
        }

        private void KeyReaderLogLevel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingUi) return;
            SaveKeyReaderPathsFromUi(false);
        }

        private void SaveKeyReaderPathsFromUi(bool syncPickup)
        {
            if (_loadingUi) return;
            var level = 4;
            if (KeyReaderLogLevelCombo != null && KeyReaderLogLevelCombo.SelectedValue != null)
            {
                int parsed;
                if (int.TryParse(Convert.ToString(KeyReaderLogLevelCombo.SelectedValue), out parsed))
                    level = parsed;
            }
            if (level < 0) level = 0;
            if (level > 4) level = 4;
            var paths = new DesktopKeyReaderReg.Paths
            {
                DataBin = KeyReaderDataBinBox != null ? KeyReaderDataBinBox.Text.Trim() : "",
                LogPath = KeyReaderLogPathBox != null ? KeyReaderLogPathBox.Text.Trim() : "",
                EncPath = KeyReaderEncPathBox != null ? KeyReaderEncPathBox.Text.Trim() : "",
                LogLevel = level
            };
            try
            {
                DesktopKeyReaderReg.WriteAbsolute(paths);
            }
            catch (Exception ex)
            {
                AppendLog(ex.Message, LogLevel.Error);
                return;
            }

            StartKeyReaderLogTailer(paths);

            if (!syncPickup || string.IsNullOrEmpty(paths.DataBin) || _settings == null)
                return;
            try
            {
                var dir = Path.GetDirectoryName(paths.DataBin);
                if (string.IsNullOrEmpty(dir)) return;
                _settings.KeysPickupFolder = dir;
                try { _settings.Save(); }
                catch { }
                ApplyKeysMonitorSettings();
            }
            catch { }
        }

        private void StartKeyReaderLogTailer()
        {
            DesktopKeyReaderReg.Paths paths = null;
            try { paths = DesktopKeyReaderReg.ReadAbsolute(); }
            catch { }
            StartKeyReaderLogTailer(paths);
        }

        private void StartKeyReaderLogTailer(DesktopKeyReaderReg.Paths paths)
        {
            if (paths == null)
            {
                try { paths = DesktopKeyReaderReg.ReadAbsolute(); }
                catch { paths = null; }
            }
            var logDir = paths != null ? paths.LogPath : null;
            var dataBin = paths != null ? paths.DataBin : null;
            if (KeyReaderLogPathBox != null && !string.IsNullOrWhiteSpace(KeyReaderLogPathBox.Text))
                logDir = KeyReaderLogPathBox.Text.Trim();
            if (KeyReaderDataBinBox != null && !string.IsNullOrWhiteSpace(KeyReaderDataBinBox.Text))
                dataBin = KeyReaderDataBinBox.Text.Trim();
            try
            {
                _keyReaderLogTailer.Start(logDir, dataBin, OnKeyReaderActivityLine);
            }
            catch (Exception ex)
            {
                AppendLog("KeyReader: tailer start failed: " + ex.Message, LogLevel.Warning);
            }
        }

        private void OnKeyReaderActivityLine(string text, KeyReaderActivityKind kind)
        {
            LogLevel level;
            switch (kind)
            {
                case KeyReaderActivityKind.Success:
                    level = LogLevel.Success;
                    break;
                case KeyReaderActivityKind.Warning:
                    level = LogLevel.Warning;
                    break;
                case KeyReaderActivityKind.Error:
                    level = LogLevel.Error;
                    break;
                case KeyReaderActivityKind.Verbose:
                    level = LogLevel.Verbose;
                    break;
                default:
                    level = LogLevel.Info;
                    break;
            }
            if (kind == KeyReaderActivityKind.Verbose)
                AppendLog(text, level, true);
            else
                AppendLog(text, level);
        }

        private void KeyReaderPaths_LostFocus(object sender, RoutedEventArgs e)
        {
            SaveKeyReaderPathsFromUi(true);
        }

        private void KeyReaderDataBinBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog();
            dlg.Filter = "keyreader.bin|keyreader.bin|BIN|*.bin|All|*.*";
            dlg.Title = UiStrings.T("KeyReaderDataBin");
            try
            {
                var cur = KeyReaderDataBinBox != null ? KeyReaderDataBinBox.Text.Trim() : "";
                if (!string.IsNullOrEmpty(cur))
                {
                    dlg.FileName = Path.GetFileName(cur);
                    var dir = Path.GetDirectoryName(cur);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        dlg.InitialDirectory = dir;
                }
            }
            catch { }
            if (dlg.ShowDialog(this) != true) return;
            if (KeyReaderDataBinBox != null)
                KeyReaderDataBinBox.Text = dlg.FileName;
            SaveKeyReaderPathsFromUi(true);
        }

        private void KeyReaderLogPathBrowse_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new WinForms.FolderBrowserDialog())
            {
                dlg.Description = UiStrings.T("KeyReaderLogPath");
                try
                {
                    var cur = KeyReaderLogPathBox != null ? KeyReaderLogPathBox.Text.Trim() : "";
                    if (!string.IsNullOrEmpty(cur) && Directory.Exists(cur))
                        dlg.SelectedPath = cur;
                }
                catch { }
                if (dlg.ShowDialog() != WinForms.DialogResult.OK) return;
                if (KeyReaderLogPathBox != null)
                    KeyReaderLogPathBox.Text = dlg.SelectedPath;
                SaveKeyReaderPathsFromUi(false);
            }
        }

        private void KeyReaderEncPathBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog();
            dlg.Filter = "ENC|*.enc|All|*.*";
            dlg.Title = UiStrings.T("KeyReaderEncPath");
            try
            {
                var cur = KeyReaderEncPathBox != null ? KeyReaderEncPathBox.Text.Trim() : "";
                if (!string.IsNullOrEmpty(cur))
                {
                    dlg.FileName = Path.GetFileName(cur);
                    var dir = Path.GetDirectoryName(cur);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        dlg.InitialDirectory = dir;
                }
            }
            catch { }
            if (dlg.ShowDialog(this) != true) return;
            if (KeyReaderEncPathBox != null)
                KeyReaderEncPathBox.Text = dlg.FileName;
            SaveKeyReaderPathsFromUi(false);
        }

        private void LoadKeysDataPathsToUi()
        {
            if (_settings == null) return;
            if (KeysUploaderLogDirBox != null)
                KeysUploaderLogDirBox.Text = _settings.KeysUploaderLogDir ?? "";
            if (KeysKaiLogDirBox != null)
                KeysKaiLogDirBox.Text = _settings.KeysKaiLogDir ?? "";
            if (KeysSqlitePathBox != null)
                KeysSqlitePathBox.Text = _settings.KeysSqlitePath ?? "";
            if (KeysRefreshSecondsBox != null)
                KeysRefreshSecondsBox.Text = _settings.KeysRefreshSeconds.ToString();
            if (KeysVinTipSecondsBox != null)
                KeysVinTipSecondsBox.Text = _settings.KeysVinTipSeconds.ToString();
            if (AuthCooldownMinutesBox != null)
                AuthCooldownMinutesBox.Text = _settings.AuthCooldownMinutes.ToString();
            if (LogVerboseCheck != null)
                LogVerboseCheck.IsChecked = _settings.LogVerbose;
            if (MinimizeOnCloseCheck != null)
                MinimizeOnCloseCheck.IsChecked = _settings.MinimizeOnClose;
            if (AutoUpdateCheck != null)
                AutoUpdateCheck.IsChecked = _settings.AutoUpdate;
        }

        private void SaveKeysDataPathsFromUi()
        {
            if (_settings == null || _loadingUi) return;
            if (KeysUploaderLogDirBox != null && !string.IsNullOrWhiteSpace(KeysUploaderLogDirBox.Text))
                _settings.KeysUploaderLogDir = KeysUploaderLogDirBox.Text.Trim();
            if (KeysKaiLogDirBox != null && !string.IsNullOrWhiteSpace(KeysKaiLogDirBox.Text))
                _settings.KeysKaiLogDir = KeysKaiLogDirBox.Text.Trim();
            if (KeysSqlitePathBox != null && !string.IsNullOrWhiteSpace(KeysSqlitePathBox.Text))
                _settings.KeysSqlitePath = KeysSqlitePathBox.Text.Trim();
            ParseAndSetKeysRefreshSeconds();
            ParseAndSetKeysVinTipSeconds();
            ParseAndSetAuthCooldownMinutes();
            _settings.NormalizeClamps();
            try { _settings.Save(); }
            catch { }
            ApplyKeysMonitorSettings();
            LoadKeysDataPathsToUi();
        }

        private void KeysDataPaths_LostFocus(object sender, RoutedEventArgs e)
        {
            SaveKeysDataPathsFromUi();
        }

        private void KeysUploaderLogDirBrowse_Click(object sender, RoutedEventArgs e)
        {
            BrowseFolderInto(KeysUploaderLogDirBox, UiStrings.T("KeysUploaderLogDir"));
            SaveKeysDataPathsFromUi();
        }

        private void KeysKaiLogDirBrowse_Click(object sender, RoutedEventArgs e)
        {
            BrowseFolderInto(KeysKaiLogDirBox, UiStrings.T("KeysKaiLogDir"));
            SaveKeysDataPathsFromUi();
        }

        private void KeysSqlitePathBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog();
            dlg.Filter = "SQLite|*.db|All|*.*";
            dlg.Title = UiStrings.T("KeysSqlitePath");
            try
            {
                var cur = KeysSqlitePathBox != null ? KeysSqlitePathBox.Text.Trim() : "";
                if (!string.IsNullOrEmpty(cur))
                {
                    dlg.FileName = Path.GetFileName(cur);
                    var dir = Path.GetDirectoryName(cur);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        dlg.InitialDirectory = dir;
                }
            }
            catch { }
            if (dlg.ShowDialog(this) != true) return;
            if (KeysSqlitePathBox != null)
                KeysSqlitePathBox.Text = dlg.FileName;
            SaveKeysDataPathsFromUi();
        }

        private void BrowseFolderInto(TextBox box, string description)
        {
            if (box == null) return;
            using (var dlg = new WinForms.FolderBrowserDialog())
            {
                dlg.Description = description ?? "";
                try
                {
                    var cur = box.Text != null ? box.Text.Trim() : "";
                    if (!string.IsNullOrEmpty(cur) && Directory.Exists(cur))
                        dlg.SelectedPath = cur;
                }
                catch { }
                if (dlg.ShowDialog() != WinForms.DialogResult.OK) return;
                box.Text = dlg.SelectedPath;
            }
        }

        private void KeysRefreshSeconds_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_loadingUi) return;
            ParseAndSetKeysRefreshSeconds();
            try { if (_settings != null) _settings.Save(); }
            catch { }
            ApplyKeysRefreshInterval();
        }

        private void KeysVinTipSeconds_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_loadingUi) return;
            ParseAndSetKeysVinTipSeconds();
            try { if (_settings != null) _settings.Save(); }
            catch { }
        }

        private void AuthCooldownMinutes_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_loadingUi) return;
            ParseAndSetAuthCooldownMinutes();
            try { if (_settings != null) _settings.Save(); }
            catch { }
        }

        private void LogVerbose_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingUi || _settings == null) return;
            _settings.LogVerbose = LogVerboseCheck != null && LogVerboseCheck.IsChecked == true;
            try { _settings.Save(); }
            catch { }
        }

        private void MinimizeOnClose_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingUi || _settings == null) return;
            _settings.MinimizeOnClose = MinimizeOnCloseCheck == null || MinimizeOnCloseCheck.IsChecked == true;
            try { _settings.Save(); }
            catch { }
        }

        private void ParseAndSetKeysRefreshSeconds()
        {
            if (_settings == null) return;
            var sec = 3;
            if (KeysRefreshSecondsBox != null)
            {
                int parsed;
                if (!int.TryParse(KeysRefreshSecondsBox.Text.Trim(), out parsed) || parsed < 1)
                    parsed = 3;
                if (parsed > 60) parsed = 60;
                sec = parsed;
                KeysRefreshSecondsBox.Text = sec.ToString();
            }
            _settings.KeysRefreshSeconds = sec;
        }

        private void ParseAndSetKeysVinTipSeconds()
        {
            if (_settings == null) return;
            var sec = 8;
            if (KeysVinTipSecondsBox != null)
            {
                int parsed;
                if (!int.TryParse(KeysVinTipSecondsBox.Text.Trim(), out parsed) || parsed < 3)
                    parsed = 8;
                if (parsed > 60) parsed = 60;
                sec = parsed;
                KeysVinTipSecondsBox.Text = sec.ToString();
            }
            _settings.KeysVinTipSeconds = sec;
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
        }

        private void KeysEventsList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

        private void KeysEventsList_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

        private void KeysEventsList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                CopyVinFromSelectionOrLast();
                e.Handled = true;
            }
        }

        private void KeysVinCopyIcon_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateVinCopyIconVisibility(sender as Button);
        }

        private void KeysVinCopyIcon_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            UpdateVinCopyIconVisibility(sender as Button);
        }

        private static void UpdateVinCopyIconVisibility(Button btn)
        {
            if (btn == null) return;
            var ev = btn.DataContext as KeyActivityEvent;
            var tipText = UiStrings.T("KeysCopyVin");
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

        private void KeysVinCopyIcon_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Handled stops ListViewItem selection; also runs copy (Click would not fire after this).
            e.Handled = true;
            var btn = sender as Button;
            if (btn == null) return;
            var ev = btn.DataContext as KeyActivityEvent;
            if (ev == null || string.IsNullOrEmpty(ev.Vin)) return;
            CopyVinToClipboard(ev.Vin);
            ShowVinCopyIconFeedback(btn);
        }

        private void KeysVinCopyIcon_Click(object sender, RoutedEventArgs e)
        {
            // Keyboard / residual Click path (Focusable=False normally skips this).
            e.Handled = true;
            var btn = sender as Button;
            if (btn == null) return;
            var ev = btn.DataContext as KeyActivityEvent;
            if (ev == null || string.IsNullOrEmpty(ev.Vin)) return;
            CopyVinToClipboard(ev.Vin);
            ShowVinCopyIconFeedback(btn);
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

        private static void OpenFolder(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return;
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
                DiagProcess.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private void RefreshKeysTab(bool announce)
        {
            if (KeysBalloonCheck != null && !_loadingUi && _settings != null)
                KeysBalloonCheck.IsChecked = _settings.KeysBalloonEnabled;
            if (KeysVinBmwFailOnlyCheck != null && !_loadingUi && _settings != null)
            {
                KeysVinBmwFailOnlyCheck.IsChecked = _settings.KeysBalloonEnabled && _settings.KeysVinTipBmwFailOnly;
                SyncKeysVinBmwFailOnlyUi();
            }
            if (KeysStallAlertCheck != null && !_loadingUi && _settings != null)
                KeysStallAlertCheck.IsChecked = _settings.KeysStallAlertEnabled;
            if (KeysAutoCopyVinCheck != null && !_loadingUi && _settings != null)
                KeysAutoCopyVinCheck.IsChecked = _settings.KeysAutoCopyVin;

            if (announce)
                _keysRefreshAnnouncePending = true;
            if (System.Threading.Interlocked.Exchange(ref _keysRefreshBusy, 1) == 1)
            {
                _keysRefreshCoalesce = true;
                return;
            }

            Task.Run(delegate
            {
                KeyActivitySnapshot snap = null;
                BmwServicesReport report = null;
                Exception error = null;
                try
                {
                    snap = _keyMonitor.Refresh();
                    try { report = ServiceMonitor.Check(); }
                    catch { }
                }
                catch (Exception ex)
                {
                    error = ex;
                }

                try
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        try
                        {
                            var doAnnounce = _keysRefreshAnnouncePending;
                            _keysRefreshAnnouncePending = false;
                            if (error != null)
                            {
                                if (KeysServicesText != null)
                                    KeysServicesText.Text = error.Message;
                                if (doAnnounce)
                                    AppendLog(error.Message, LogLevel.Error);
                                return;
                            }
                            ApplyKeysSnapshot(snap, report, doAnnounce);
                        }
                        finally
                        {
                            System.Threading.Interlocked.Exchange(ref _keysRefreshBusy, 0);
                            if (_keysRefreshCoalesce)
                            {
                                _keysRefreshCoalesce = false;
                                RefreshKeysTab(false);
                            }
                        }
                    }), DispatcherPriority.Background);
                }
                catch
                {
                    System.Threading.Interlocked.Exchange(ref _keysRefreshBusy, 0);
                }
            });
        }

        private void ApplyKeysSnapshot(KeyActivitySnapshot snap, BmwServicesReport report, bool announce)
        {
            if (snap == null) return;
            _uiOp = "ApplyKeysSnapshot";
            try
            {
                try
                {
                    if (report != null)
                    {
                        var uploader = report.Services != null && report.Services.Count > 1
                            ? report.Services[1]
                            : null;
                        var kai = report.Services != null && report.Services.Count > 0
                            ? report.Services[0]
                            : null;
                        var readerLine = "";
                        if (snap.Reader != null && snap.Reader.Ok && !string.IsNullOrEmpty(snap.Reader.KeyReaderId))
                        {
                            readerLine = snap.Reader.KeyReaderId;
                            if (snap.Reader.LastKeyReceivedLocal.HasValue)
                                readerLine += " — " + snap.Reader.LastKeyReceivedLocal.Value.ToString("g");
                        }
                        if (KeysServicesText != null)
                        {
                            var u = uploader != null
                                ? ((uploader.DisplayName ?? uploader.ServiceName) + ": " + (uploader.StatusText ?? "?"))
                                : "Key Uploader: —";
                            var k = kai != null
                                ? ((kai.DisplayName ?? kai.ServiceName) + ": " + (kai.StatusText ?? "?"))
                                : "KAI: —";
                            KeysServicesText.Text = string.IsNullOrEmpty(readerLine)
                                ? (u + "  |  " + k)
                                : (readerLine + "  |  " + u + "  |  " + k);
                        }
                    }
                }
                catch
                {
                    if (KeysServicesText != null)
                        KeysServicesText.Text = "—";
                }

                var raw = snap.Events ?? new List<KeyActivityEvent>();
                _keysAllEvents = FilterAfterKeysClear(raw);
                ApplyKeysFilter();
                UpdateKeysTodayBadge(CountKeysToday(_keysAllEvents));
                HandleKeysNotifications(snap);
                UpdateKeysAlert(snap);
                RebuildTrayMenu();
                MaybeBroadcastKeySync();

                if (announce)
                    AppendLog(UiStrings.T("KeysRefreshed"), LogLevel.Info);
            }
            catch (Exception ex)
            {
                if (KeysServicesText != null)
                    KeysServicesText.Text = ex.Message;
                if (announce)
                    AppendLog(ex.Message, LogLevel.Error);
            }
        }

        private void ApplyKeysFilter()
        {
            if (KeysEventsList == null) return;
            _uiOp = "ApplyKeysFilter";
            // Hide orphan "Picked up" rows with no Local/BMW outcome (Status Unknown).
            IEnumerable<KeyActivityEvent> src = FilterMeaningful(_keysAllEvents);
            switch (_keysFilter)
            {
                case "today":
                    src = FilterToday(src);
                    break;
                case "failed":
                    src = FilterByStatus(src, KeyEventStatus.Failed);
                    break;
                case "bmw":
                    src = FilterByStatus(src, KeyEventStatus.BmwOk);
                    break;
                case "local":
                    src = FilterByStatus(src, KeyEventStatus.LocalOk);
                    break;
                case "waiting":
                    src = FilterByStatus(src, KeyEventStatus.Waiting);
                    break;
            }
            if (!string.IsNullOrEmpty(_keysSearchText))
                src = FilterBySearch(src, _keysSearchText);
            src = SortKeysEvents(src, _keysSortColumn, _keysSortAscending);

            var keep = KeysEventsList.SelectedItem as KeyActivityEvent;
            var keepVin = keep != null ? keep.Vin : null;
            var keepDetected = keep != null ? keep.DetectedLocal : DateTime.MinValue;

            var list = src as List<KeyActivityEvent> ?? new List<KeyActivityEvent>(src);
            if (string.Equals(_keysLastAppliedSearch, _keysSearchText, StringComparison.Ordinal)
                && KeysListUnchanged(KeysEventsList.ItemsSource as IList<KeyActivityEvent>
                    ?? KeysEventsList.ItemsSource as System.Collections.IEnumerable, list))
                return;

            _keysLastAppliedSearch = _keysSearchText;
            KeysEventsList.ItemsSource = list;
            KeyActivityEvent restore = null;
            if (keep != null)
            {
                foreach (var ev in list)
                {
                    if (ev == null) continue;
                    if (ReferenceEquals(ev, keep))
                    {
                        restore = ev;
                        break;
                    }
                }
                if (restore == null && !string.IsNullOrEmpty(keepVin))
                {
                    foreach (var ev in list)
                    {
                        if (ev == null) continue;
                        if (string.Equals(ev.Vin, keepVin, StringComparison.OrdinalIgnoreCase)
                            && ev.DetectedLocal == keepDetected)
                        {
                            restore = ev;
                            break;
                        }
                    }
                }
                if (restore == null && !string.IsNullOrEmpty(keepVin))
                {
                    foreach (var ev in list)
                    {
                        if (ev != null && string.Equals(ev.Vin, keepVin, StringComparison.OrdinalIgnoreCase))
                        {
                            restore = ev;
                            break;
                        }
                    }
                }
            }
            if (restore != null)
                KeysEventsList.SelectedItem = restore;
            ReapplyKeysRowHighlightIfNeeded();
            UpdateKeysRetryButton();
        }

        private static bool KeysListUnchanged(
            System.Collections.IEnumerable current, List<KeyActivityEvent> next)
        {
            if (next == null) return current == null;
            if (current == null) return next.Count == 0;
            var i = 0;
            foreach (var obj in current)
            {
                if (i >= next.Count) return false;
                if (!SameKeyEvent(obj as KeyActivityEvent, next[i])) return false;
                i++;
            }
            return i == next.Count;
        }

        private static bool SameKeyEvent(KeyActivityEvent a, KeyActivityEvent b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            return a.DetectedLocal == b.DetectedLocal
                && a.Status == b.Status
                && a.LocalSent == b.LocalSent
                && a.BmwUploaded == b.BmwUploaded
                && a.Failed == b.Failed
                && a.Failed == b.Failed
                && string.Equals(a.FileId ?? "", b.FileId ?? "", StringComparison.Ordinal)
                && string.Equals(a.Vin ?? "", b.Vin ?? "", StringComparison.Ordinal)
                && string.Equals(a.Paint ?? "", b.Paint ?? "", StringComparison.Ordinal)
                && string.Equals(a.Upholstery ?? "", b.Upholstery ?? "", StringComparison.Ordinal)
                && string.Equals(a.ILevelCurrent ?? "", b.ILevelCurrent ?? "", StringComparison.Ordinal)
                && string.Equals(a.ReaderId ?? "", b.ReaderId ?? "", StringComparison.Ordinal)
                && string.Equals(a.DetailText ?? "", b.DetailText ?? "", StringComparison.Ordinal);
        }

        private static List<KeyActivityEvent> FilterBySearch(IEnumerable<KeyActivityEvent> src, string query)
        {
            var list = new List<KeyActivityEvent>();
            if (src == null) return list;
            if (string.IsNullOrEmpty(query))
            {
                foreach (var ev in src)
                    if (ev != null) list.Add(ev);
                return list;
            }
            foreach (var ev in src)
            {
                if (ev == null) continue;
                if (ContainsIgnoreCase(ev.Vin, query)
                    || ContainsIgnoreCase(ev.Paint, query)
                    || ContainsIgnoreCase(ev.ILevelCurrent, query))
                    list.Add(ev);
            }
            return list;
        }

        private static bool ContainsIgnoreCase(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle))
                return false;
            return haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static List<KeyActivityEvent> FilterMeaningful(IEnumerable<KeyActivityEvent> src)
        {
            var list = new List<KeyActivityEvent>();
            if (src == null) return list;
            foreach (var ev in src)
            {
                if (ev == null) continue;
                if (string.IsNullOrWhiteSpace(ev.Vin))
                    continue;
                if (ev.Status == KeyEventStatus.Unknown
                    && !ev.LocalSent && !ev.BmwUploaded && !ev.Failed)
                    continue;
                list.Add(ev);
            }
            return list;
        }

        private List<KeyActivityEvent> FilterAfterKeysClear(IEnumerable<KeyActivityEvent> src)
        {
            var list = new List<KeyActivityEvent>();
            if (src == null) return list;
            foreach (var ev in src)
            {
                if (ev == null) continue;
                if (_keysClearedAfterLocal.HasValue
                    && ev.DetectedLocal <= _keysClearedAfterLocal.Value)
                    continue;
                list.Add(ev);
            }
            return list;
        }

        private static int CountKeysToday(IEnumerable<KeyActivityEvent> src)
        {
            var today = DateTime.Now.Date;
            var n = 0;
            if (src == null) return 0;
            foreach (var ev in src)
            {
                if (ev != null && ev.DetectedLocal.Date == today)
                    n++;
            }
            return n;
        }

        private static List<KeyActivityEvent> FilterByStatus(IEnumerable<KeyActivityEvent> src, KeyEventStatus status)
        {
            var list = new List<KeyActivityEvent>();
            foreach (var ev in src)
            {
                if (ev != null && ev.Status == status)
                    list.Add(ev);
            }
            return list;
        }

        private static List<KeyActivityEvent> FilterToday(IEnumerable<KeyActivityEvent> src)
        {
            var list = new List<KeyActivityEvent>();
            if (src == null) return list;
            var today = DateTime.Today;
            foreach (var ev in src)
            {
                if (ev != null && ev.DetectedLocal.Date == today)
                    list.Add(ev);
            }
            return list;
        }

        private void UpdateKeysTodayBadge(int count)
        {
            if (KeysTodayBadge == null) return;
            KeysTodayBadge.Text = count > 0
                ? string.Format(UiStrings.T("KeysTodayBadge"), count)
                : "";
        }

        private void CopyVinFromSelectionOrLast()
        {
            string vin = null;
            var ev = KeysEventsList != null ? KeysEventsList.SelectedItem as KeyActivityEvent : null;
            if (ev != null && !string.IsNullOrEmpty(ev.Vin))
                vin = ev.Vin;
            if (string.IsNullOrEmpty(vin))
                vin = _keyMonitor.GetLastKnownVin();
            CopyVinToClipboard(vin);
        }

        private void CopyVinToClipboard(string vin)
        {
            _uiOp = "CopyVinToClipboard";
            if (string.IsNullOrEmpty(vin))
            {
                AppendLog(UiStrings.T("KeysVinMissing"), LogLevel.Warning);
                return;
            }
            try
            {
                if (!NativeMethods.TrySetClipboardText(vin))
                    Clipboard.SetDataObject(vin, false);
                AppendLog(string.Format(UiStrings.T("KeysVinCopied"), vin), LogLevel.Success);
            }
            catch (Exception ex)
            {
                AppendLog(ex.Message, LogLevel.Error);
            }
        }

        private void HandleKeysNotifications(KeyActivitySnapshot snap)
        {
            if (snap == null || _settings == null) return;
            // VIN tips: local balloon is optional; remote clients always get the broadcast.
            MaybeShowVinInfoBalloons(snap);
            MaybeShowVinBmwFailTips(snap);
            var balloonOn = _settings.KeysBalloonEnabled;
            if (_settings.KeysStallAlertEnabled && snap.StallEvents != null)
            {
                foreach (var ev in snap.StallEvents)
                {
                    if (ev == null) continue;
                    var vin = string.IsNullOrEmpty(ev.Vin) ? "?" : ev.Vin;
                    var stallMin = _settings.KeysStallMinutes < 1 ? 2 : _settings.KeysStallMinutes;
                    var msg = string.Format(UiStrings.T("KeysStallBalloon"), vin, stallMin);
                    AppendLog(msg, LogLevel.Warning);
                    if (balloonOn)
                        ShowBalloon(WinForms.ToolTipIcon.Warning, TruncateBalloon(msg));
                }
            }
            MaybeRemediateKeysBmw403(snap, balloonOn);
        }

        /// <summary>
        /// On new BMW 403 Failed: one automatic Register, then Register blocked until password change.
        /// </summary>
        private void MaybeRemediateKeysBmw403(KeyActivitySnapshot snap, bool balloonOn)
        {
            if (snap == null || snap.Events == null || _settings == null) return;
            KeyActivityEvent hit = null;
            foreach (var ev in snap.Events)
            {
                if (ev == null || !ev.Failed || ev.BmwUploaded) continue;
                var detail = ev.Detail ?? "";
                if (detail.IndexOf("403", StringComparison.OrdinalIgnoreCase) < 0
                    && detail.IndexOf("Forbidden", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if ((DateTime.Now - ev.DetectedLocal).TotalMinutes > 10)
                    continue;
                hit = ev;
                break;
            }
            if (hit == null) return;

            var key = (hit.Vin ?? "") + "|" + hit.DetectedLocal.ToString("o");
            if (string.Equals(key, _lastKeys403RemediateKey, StringComparison.Ordinal))
                return;
            if (_lastKeys403RemediateUtc != DateTime.MinValue
                && (DateTime.UtcNow - _lastKeys403RemediateUtc).TotalMinutes < 60)
                return;

            _lastKeys403RemediateKey = key;
            _lastKeys403RemediateUtc = DateTime.UtcNow;

            var vin = string.IsNullOrEmpty(hit.Vin) ? "?" : hit.Vin;
            var balloon = string.Format(UiStrings.T("KeysBmw403Balloon"), vin);
            AppendLog(balloon, LogLevel.Warning);
            if (balloonOn)
                ShowBalloon(WinForms.ToolTipIcon.Warning, TruncateBalloon(balloon));

            if (IsAuthRegisterBlocked())
            {
                AppendLog(UiStrings.T("KeysBmw403AlreadyBlocked"), LogLevel.Warning);
                return;
            }

            AppendLog(UiStrings.T("KeysBmw403AutoRegister"), LogLevel.Warning);
            _pending403RetryVin = hit.Vin;
            _pending403RetryReaderId = hit.ReaderId;
            // One Register (bypass cooldown for this shot), then lock auto Register until password change,
            // then retry key upload once.
            RunNowAsync(false, true, null, true, true);
        }

        /// <summary>
        /// Info tip as soon as VIN is read from .bin/Sqlite — does not wait for BMW Server upload.
        /// Dedup is done in KeyActivityMonitor.NewVinReads.
        /// When KeysVinTipBmwFailOnly is on, skip local tip here (see MaybeShowVinBmwFailTips).
        /// </summary>
        private void MaybeShowVinInfoBalloons(KeyActivitySnapshot snap)
        {
            if (snap.NewVinReads == null || snap.NewVinReads.Count == 0)
                return;
            HighlightNewVinRows(snap.NewVinReads);
            var showLocal = _settings != null
                && _settings.KeysBalloonEnabled
                && !_settings.KeysVinTipBmwFailOnly;
            foreach (var vin in snap.NewVinReads)
            {
                if (string.IsNullOrEmpty(vin)) continue;
                var msg = string.Format(UiStrings.T("KeysNewKeyBalloon"), vin);
                AppendLog(msg, LogLevel.Info);
                if (_settings != null && _settings.KeysAutoCopyVin)
                    CopyVinToClipboard(vin);
                // Local tip only if enabled (and not fail-only mode); clients always receive broadcast.
                ShowVinInfoBalloon(vin, msg, showLocal);
            }
        }

        /// <summary>
        /// When KeysVinTipBmwFailOnly: info tip after BMW upload fails for a recent VIN.
        /// </summary>
        private void MaybeShowVinBmwFailTips(KeyActivitySnapshot snap)
        {
            if (_settings == null || !_settings.KeysBalloonEnabled || !_settings.KeysVinTipBmwFailOnly)
                return;
            if (snap == null || snap.Events == null) return;
            var now = DateTime.Now;
            foreach (var ev in snap.Events)
            {
                if (ev == null || !ev.Failed || ev.BmwUploaded) continue;
                if (string.IsNullOrEmpty(ev.Vin)) continue;
                if ((now - ev.DetectedLocal).TotalMinutes > 15) continue;
                var key = (!string.IsNullOrEmpty(ev.FileId) ? ("F:" + ev.FileId) : null)
                    ?? ("T:" + ev.DetectedLocal.ToString("yyyyMMddHHmmss") + ":V:" + ev.Vin);
                if (!_bmwFailVinTipKeys.Add(key)) continue;
                while (_bmwFailVinTipKeys.Count > 200)
                {
                    // Trim oldest-ish by clearing half when oversized.
                    var drop = new List<string>();
                    var n = 0;
                    foreach (var k in _bmwFailVinTipKeys)
                    {
                        drop.Add(k);
                        if (++n >= 100) break;
                    }
                    for (var i = 0; i < drop.Count; i++)
                        _bmwFailVinTipKeys.Remove(drop[i]);
                }
                var msg = string.Format(UiStrings.T("KeysVinBmwFailTip"), ev.Vin);
                AppendLog(msg, LogLevel.Warning);
                ShowVinInfoBalloon(ev.Vin, msg, true);
            }
        }

        private void ShowVinInfoBalloon(string vin, string message, bool showLocalTip = true)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => ShowVinInfoBalloon(vin, message, showLocalTip)));
                return;
            }
            var text = TruncateBalloon(message);
            var tipSec = _settings != null ? _settings.KeysVinTipSeconds : 8;
            if (tipSec < 3) tipSec = 3;
            if (tipSec > 60) tipSec = 60;
            var capture = vin;
            if (showLocalTip)
            {
                // Custom tip only — no Windows toast (NotifyIcon.ShowBalloonTip).
                TrayBalloonWindow.ShowTip(
                    "KAI - AOS Renew",
                    text,
                    WinForms.ToolTipIcon.Info,
                    UiStrings.T("KeysCopyVin"),
                    () => CopyVinToClipboard(capture),
                    tipSec,
                    () => ShowKeysTabAndSelectVin(capture));
            }
            var msg = new NotifyMessage
            {
                Kind = "vin",
                Level = "info",
                Title = "KAI - AOS Renew",
                Text = text,
                Vin = capture,
                Action = "copyVin",
                Seconds = tipSec
            };
            var ev = FindKeyEventByVin(capture);
            if (ev != null)
                NotifyMessage.ApplyKeyEventFields(msg, ev);
            else
            {
                msg.At = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
                msg.Status = "Waiting";
                msg.Local = 0;
                msg.Bmw = 0;
                msg.Failed = 0;
            }
            BroadcastNotify(msg);
        }

        private KeyActivityEvent FindKeyEventByVin(string vin)
        {
            if (string.IsNullOrEmpty(vin) || _keysAllEvents == null) return null;
            KeyActivityEvent best = null;
            foreach (var ev in _keysAllEvents)
            {
                if (ev == null || string.IsNullOrEmpty(ev.Vin)) continue;
                if (!string.Equals(ev.Vin, vin, StringComparison.OrdinalIgnoreCase)) continue;
                if (best == null || ev.DetectedLocal > best.DetectedLocal)
                    best = ev;
            }
            return best;
        }

        private List<KeyActivityEvent> GetRecentKeyRows(int max)
        {
            var result = new List<KeyActivityEvent>();
            if (max < 1) return result;
            var src = FilterMeaningful(_keysAllEvents);
            src.Sort(delegate(KeyActivityEvent a, KeyActivityEvent b)
            {
                return b.DetectedLocal.CompareTo(a.DetectedLocal);
            });
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ev in src)
            {
                if (ev == null || string.IsNullOrEmpty(ev.Vin)) continue;
                if (!seen.Add(ev.Vin)) continue;
                result.Add(ev);
                if (result.Count >= max) break;
            }
            return result;
        }

        private static string KeySyncSignature(IList<KeyActivityEvent> rows)
        {
            if (rows == null || rows.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            foreach (var ev in rows)
            {
                if (ev == null) continue;
                sb.Append(ev.Vin).Append('\t')
                    .Append(ev.DetectedLocal.Ticks).Append('\t')
                    .Append(ev.LocalSent ? '1' : '0').Append('\t')
                    .Append(ev.BmwUploaded ? '1' : '0').Append('\t')
                    .Append(ev.Failed ? '1' : '0').Append('\t')
                    .Append(NotifyMessage.StatusName(ev.Status)).Append('\t')
                    .Append(ev.DetailText ?? "").Append('\n');
            }
            return sb.ToString();
        }

        private void MaybeBroadcastKeySync()
        {
            try
            {
                if (_settings == null || !_settings.NotifyServerEnabled) return;
                if (!NotifyServer.Instance.IsRunning || NotifyServer.Instance.ClientCount <= 0) return;
                var rows = GetRecentKeyRows(10) ?? new List<KeyActivityEvent>();
                // Never push empty snapshot — would wipe client lists (ApplyKeySync is authoritative).
                if (rows.Count == 0)
                {
                    _lastKeySyncSignature = "";
                    return;
                }
                var sig = KeySyncSignature(rows);
                if (string.Equals(sig, _lastKeySyncSignature, StringComparison.Ordinal)) return;
                _lastKeySyncSignature = sig;
                BroadcastNotify(new NotifyMessage
                {
                    Kind = "keySync",
                    Level = "info",
                    Title = "KAI - AOS Renew",
                    Text = NotifyMessage.EncodeKeySync(rows) ?? ""
                });
            }
            catch { }
        }

        private void ShowKeysTabAndSelectVin(string vin)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => ShowKeysTabAndSelectVin(vin)));
                return;
            }
            ShowFromTray();
            if (_aboutVisible)
                HideAboutPanel();
            if (TabKeys != null)
                TabKeys.IsSelected = true;
            SelectKeysRowByVin(vin, true);
        }

        private void HighlightNewVinRows(IList<string> vins)
        {
            if (vins == null || vins.Count == 0 || KeysEventsList == null) return;
            string target = null;
            for (var i = vins.Count - 1; i >= 0; i--)
            {
                if (!string.IsNullOrEmpty(vins[i]))
                {
                    target = vins[i];
                    break;
                }
            }
            if (string.IsNullOrEmpty(target)) return;
            SelectKeysRowByVin(target, true);
        }

        private void SelectKeysRowByVin(string vin, bool highlight)
        {
            if (string.IsNullOrEmpty(vin) || KeysEventsList == null) return;
            KeysEventsList.UpdateLayout();
            KeyActivityEvent match = null;
            foreach (var item in KeysEventsList.Items)
            {
                var ev = item as KeyActivityEvent;
                if (ev != null && string.Equals(ev.Vin, vin, StringComparison.OrdinalIgnoreCase))
                {
                    match = ev;
                    break;
                }
            }
            if (match == null) return;
            KeysEventsList.SelectedItem = match;
            KeysEventsList.ScrollIntoView(match);
            if (!highlight) return;
            ClearKeysRowHighlightVisual();
            var lvi = KeysEventsList.ItemContainerGenerator.ContainerFromItem(match) as ListViewItem;
            if (lvi == null)
            {
                // Container may not exist until layout; retry shortly.
                _keysHighlightVin = vin;
                _keysHighlightUntilUtc = DateTime.UtcNow.AddSeconds(KeysHighlightSeconds);
                var retry = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                var capture = match;
                retry.Tick += delegate
                {
                    retry.Stop();
                    var item = KeysEventsList.ItemContainerGenerator.ContainerFromItem(capture) as ListViewItem;
                    if (item != null)
                        ApplyKeysRowHighlight(item, vin, true);
                    else
                        ScheduleKeysHighlightTimer();
                };
                retry.Start();
                return;
            }
            ApplyKeysRowHighlight(lvi, vin, true);
        }

        private void ApplyKeysRowHighlight(ListViewItem lvi, string vin, bool resetDeadline)
        {
            if (lvi == null) return;
            ClearKeysRowHighlightVisual();
            _keysHighlightedItem = lvi;
            _keysHighlightVin = vin;
            if (resetDeadline || _keysHighlightUntilUtc < DateTime.UtcNow)
                _keysHighlightUntilUtc = DateTime.UtcNow.AddSeconds(KeysHighlightSeconds);
            var highlight = TryFindResource("KeysNewVinHighlightBrush") as Brush;
            var border = TryFindResource("KeysNewVinBorderBrush") as Brush ?? highlight;
            if (highlight != null)
                lvi.Background = highlight;
            if (border != null)
                lvi.BorderBrush = border;
            lvi.BorderThickness = new Thickness(3, 0, 0, 0);
            var fg = TryFindResource("TextPrimaryBrush") as Brush;
            if (fg != null)
                lvi.Foreground = fg;
            ScheduleKeysHighlightTimer();
        }

        private void ScheduleKeysHighlightTimer()
        {
            _keysHighlightTimer.Stop();
            var remain = _keysHighlightUntilUtc - DateTime.UtcNow;
            if (remain.TotalMilliseconds < 50)
            {
                ClearKeysRowHighlight();
                return;
            }
            _keysHighlightTimer.Interval = remain;
            _keysHighlightTimer.Start();
        }

        private void ReapplyKeysRowHighlightIfNeeded()
        {
            if (string.IsNullOrEmpty(_keysHighlightVin)
                || _keysHighlightUntilUtc == DateTime.MinValue
                || DateTime.UtcNow >= _keysHighlightUntilUtc)
            {
                if (_keysHighlightedItem != null || !string.IsNullOrEmpty(_keysHighlightVin))
                    ClearKeysRowHighlight();
                return;
            }
            if (KeysEventsList == null) return;
            KeysEventsList.UpdateLayout();
            KeyActivityEvent match = null;
            foreach (var item in KeysEventsList.Items)
            {
                var ev = item as KeyActivityEvent;
                if (ev != null && string.Equals(ev.Vin, _keysHighlightVin, StringComparison.OrdinalIgnoreCase))
                {
                    match = ev;
                    break;
                }
            }
            if (match == null) return;
            var lvi = KeysEventsList.ItemContainerGenerator.ContainerFromItem(match) as ListViewItem;
            if (lvi == null)
            {
                var captureVin = _keysHighlightVin;
                var retry = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                retry.Tick += delegate
                {
                    retry.Stop();
                    if (string.IsNullOrEmpty(_keysHighlightVin)
                        || !string.Equals(_keysHighlightVin, captureVin, StringComparison.OrdinalIgnoreCase))
                        return;
                    ReapplyKeysRowHighlightIfNeeded();
                };
                retry.Start();
                return;
            }
            ApplyKeysRowHighlight(lvi, _keysHighlightVin, false);
        }

        private void KeysHighlightTimer_Tick(object sender, EventArgs e)
        {
            _keysHighlightTimer.Stop();
            ClearKeysRowHighlight();
        }

        private void ClearKeysRowHighlightVisual()
        {
            if (_keysHighlightedItem == null) return;
            try
            {
                _keysHighlightedItem.ClearValue(Control.BackgroundProperty);
                _keysHighlightedItem.ClearValue(Control.BorderBrushProperty);
                _keysHighlightedItem.ClearValue(Control.BorderThicknessProperty);
            }
            catch { }
            _keysHighlightedItem = null;
        }

        private void ClearKeysRowHighlight()
        {
            _keysHighlightTimer.Stop();
            ClearKeysRowHighlightVisual();
            _keysHighlightVin = null;
            _keysHighlightUntilUtc = DateTime.MinValue;
        }

        private void UpdateKeysAlertFromCache()
        {
            var snap = new KeyActivitySnapshot();
            snap.Events = _keysAllEvents;
            UpdateKeysAlert(snap);
        }

        private const double KeysAlertMaxMinutes = 1;

        private void UpdateKeysAlert(KeyActivitySnapshot snap)
        {
            _keysAlert = false;
            var stallOn = _settings == null || _settings.KeysStallAlertEnabled;
            // Alert only from the newest event — and only within KeysAlertMaxMinutes.
            var ev = snap != null ? KeyActivityMonitor.GetNewestEvent(snap.Events) : null;
            if (ev != null)
            {
                var ageMin = (DateTime.Now - ev.DetectedLocal).TotalMinutes;
                if (ageMin < KeysAlertMaxMinutes)
                {
                    if (ev.Status == KeyEventStatus.Failed
                        || ev.Status == KeyEventStatus.Waiting)
                    {
                        _keysAlert = true;
                    }
                    else
                    {
                        var stallMin = _settings != null ? _settings.KeysStallMinutes : 2;
                        if (stallMin < 1) stallMin = 1;
                        if (stallOn
                            && ev.LocalSent && !ev.BmwUploaded && !ev.Failed
                            && ageMin > stallMin)
                        {
                            _keysAlert = true;
                        }
                    }
                }
            }
            if (KeysTabAlertDot != null)
                KeysTabAlertDot.Visibility = _keysAlert ? Visibility.Visible : Visibility.Collapsed;
            SyncAlertBlinkTimer();
            RefreshTrayIcon();
            UpdateTrayTooltip();
        }

        private void RefreshTrayIcon()
        {
            if (_tray == null) return;
            var alert = _hasFailureAlert || _hintTrayIcon || _keysAlert || _renewAlert;
            var outdated = _debugMode || _pendingUpdate != null;
            if (alert)
            {
                if (_errorIcon == null)
                    _errorIcon = IconFactory.GetErrorTrayIcon();
                _tray.Icon = _errorIcon;
            }
            else if (outdated)
            {
                if (_outdatedIcon == null)
                    _outdatedIcon = IconFactory.GetOutdatedTrayIcon(_appIcon);
                _tray.Icon = _outdatedIcon;
            }
            else if (_appIcon != null)
            {
                _tray.Icon = _appIcon;
            }
        }

        private void ApplyThemeChrome()
        {
            try
            {
                Background = (Brush)FindResource("WindowBgBrush");
            }
            catch { }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (_aboutVisible)
                HideAboutPanel();
            SelectDefaultTab();
            ApplyTabLayoutVisibility();
            CheckBmwServices(true, false);
            ScheduleNextServiceCheck();
            RefreshPasswordHintBanners(false);
            if (HasPasswordProblem())
                TrySilentBrowserPassRefresh(true);
            if (!string.IsNullOrEmpty(_settings.Email) && !string.IsNullOrEmpty(_settings.GetPassword()))
            {
                AppendLog(UiStrings.T("StartupAuto"));
                RunAutoHealthWithLimitedRegister();
                StartTimer(false);
            }
            else if (!_timer.IsEnabled)
            {
                // Wake-up tick so IntervalHours service recheck can fire
                _timer.Start();
            }
            // Hidden at Show() when PreferStartHidden / --minimized; keep that way (no flash Hide).
            if (_startedHiddenToTray
                || (_settings != null && _settings.StartWithWindows && _settings.MinimizeToTray
                    && Visibility != Visibility.Visible))
            {
                ShowInTaskbar = false;
                if (IsVisible) Hide();
            }
            RunStartupSelfCheck();
            MaybeCheckForUpdatesOnStart();
        }

        private bool _updateCheckStarted;

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
            string serverVer = "";
            string clientVer = "";
            try { serverVer = await UpdateService.PeekLatestChannelVersionAsync(UpdateService.ServerChannel) ?? ""; }
            catch { serverVer = ""; }
            try { clientVer = await UpdateService.PeekLatestChannelVersionAsync(UpdateService.ClientChannel) ?? ""; }
            catch { clientVer = ""; }
            _feedServerVer = serverVer;
            _feedClientVer = clientVer;
            await ApplyFeedToSelfAsync();
            BroadcastUpdateFeed();
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

        private void BroadcastUpdateFeed()
        {
            if (_settings == null || !_settings.NotifyServerEnabled) return;
            if (!NotifyServer.Instance.IsRunning) return;
            if (string.IsNullOrEmpty(_feedServerVer) && string.IsNullOrEmpty(_feedClientVer)) return;
            BroadcastNotify(new NotifyMessage
            {
                Kind = "updateFeed",
                Level = "info",
                Title = "KAI - AOS Renew",
                Text = NotifyMessage.EncodeUpdateFeed(_feedServerVer, _feedClientVer)
            });
        }

        private void SendUpdateFeedTo(int clientId, bool allowEmpty = false)
        {
            if (_settings == null || !_settings.NotifyServerEnabled) return;
            if (!NotifyServer.Instance.IsRunning) return;
            if (!allowEmpty
                && string.IsNullOrEmpty(_feedServerVer)
                && string.IsNullOrEmpty(_feedClientVer))
                return;
            try
            {
                NotifyServer.Instance.SendTo(clientId, new NotifyMessage
                {
                    Kind = "updateFeed",
                    Level = "info",
                    Title = "KAI - AOS Renew",
                    Text = NotifyMessage.EncodeUpdateFeed(_feedServerVer, _feedClientVer)
                });
            }
            catch { }
        }

        private async Task HandleInboundUpdateFeedAsync(string text)
        {
            string serverVer;
            string clientVer;
            if (!NotifyMessage.TryParseUpdateFeed(text, out serverVer, out clientVer)) return;
            _feedServerVer = serverVer ?? "";
            _feedClientVer = clientVer ?? "";
            await ApplyFeedToSelfAsync();
            BroadcastUpdateFeed();
        }

        private void ShowPendingUpdate(UpdateInfo update)
        {
            _pendingUpdate = update;
            if (update == null)
            {
                if (_debugMode)
                {
                    ShowForcedDebugUpdateUi();
                    return;
                }
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

        private void ShowForcedDebugUpdateUi()
        {
            var ver = !string.IsNullOrEmpty(_feedServerVer) ? _feedServerVer : AppVersionDisplay;
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

        private void DebugModeText_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!_debugMode) return;
            _debugMode = false;
            PersistDebugMode();
            ApplyDebugModeUi();
        }

        private void PersistDebugMode()
        {
            if (_settings == null) return;
            _settings.DebugMode = _debugMode;
            try { _settings.Save(); }
            catch { }
        }

        private void ApplyDebugModeUi()
        {
            if (DebugModeText != null)
            {
                DebugModeText.Text = UiStrings.T("DebugModeOn");
                DebugModeText.Visibility = _debugMode ? Visibility.Visible : Visibility.Collapsed;
            }
            if (_debugMode)
                ShowForcedDebugUpdateUi();
            else
                ShowPendingUpdate(_pendingUpdate);
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
                if (FooterUpdateBtn != null && !_remoteServerUpdateBusy
                    && (_pendingUpdate != null || _debugMode))
                    FooterUpdateBtn.IsEnabled = true;
            }
        }

        private void BroadcastServerUpdating(string version)
        {
            BroadcastNotify(new NotifyMessage
            {
                Kind = "serverUpdating",
                Level = "info",
                Title = "KAI - AOS Renew",
                Text = version ?? ""
            });
        }

        private void BroadcastServerUpdateFailed(string error)
        {
            BroadcastNotify(new NotifyMessage
            {
                Kind = "serverUpdateFailed",
                Level = "error",
                Title = "KAI - AOS Renew",
                Text = error ?? ""
            });
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

                if (UpdateAvailableText != null)
                {
                    UpdateAvailableText.Text = UiStrings.T("ServerRestarting");
                    UpdateAvailableText.Visibility = Visibility.Visible;
                }
                if (FooterUpdateBtn != null)
                    FooterUpdateBtn.IsEnabled = false;

                await System.Threading.Tasks.Task.Delay(2500);

                AppendLog(string.Format(UiStrings.T("UpdateApplied"), ver), LogLevel.Info);
                await UpdateService.ApplyUpdateAsync(this, update, UiStrings.T, false);
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
                ShowPendingUpdate(_pendingUpdate ?? update);
            }
            finally
            {
                _remoteServerUpdateBusy = false;
                RefreshAosActionButtons();
            }
        }

        private void AutoUpdate_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingUi || _settings == null) return;
            _settings.AutoUpdate = AutoUpdateCheck != null && AutoUpdateCheck.IsChecked == true;
            _settings.Save();
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

            if (issues.Count == 0)
            {
                AppendLog(UiStrings.T("SelfCheckOk") + " (" + mutexOk + ")", LogLevel.Info);
                return;
            }

            // Log only — no balloon (avoids interrupting UI; DataBin is not checked).
            var msg = string.Format(UiStrings.T("SelfCheckWarn"), string.Join("; ", issues));
            AppendLog(msg + " [" + mutexOk + "]", LogLevel.Warning);
        }

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            var helper = new WindowInteropHelper(this);
            _hwndSource = HwndSource.FromHwnd(helper.Handle);
            if (_hwndSource != null)
                _hwndSource.AddHook(WndProcHook);
        }

        private void Window_Activated(object sender, EventArgs e)
        {
            _uiOp = "Window_Activated";
            if (_focusPasswordOnActivate)
            {
                _focusPasswordOnActivate = false;
                FocusPasswordField();
                TryShowClipboardPassBanner();
            }
            if (_settings == null) return;
            if (ThemeManager.NormalizeMode(_settings.ThemeMode) != "system") return;
            // Re-theming restyles the whole tree; only do it when the OS light/dark actually flipped.
            var osLight = ThemeManager.IsOsLightTheme();
            if (_lastOsLight.HasValue && _lastOsLight.Value == osLight) return;
            var first = !_lastOsLight.HasValue;
            _lastOsLight = osLight;
            if (first) return;
            _uiOp = "Window_Activated theme";
            ThemeManager.Apply("system");
            ApplyThemeChrome();
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

        private void TryShowClipboardPassBanner()
        {
            if (ClipboardPassBanner == null) return;
            string clip = null;
            try { clip = Clipboard.GetText(); }
            catch { }
            if (string.IsNullOrWhiteSpace(clip))
            {
                HideClipboardPassBanner();
                return;
            }
            clip = NormalizeClipboardPassword(clip);
            if (string.IsNullOrEmpty(clip))
            {
                HideClipboardPassBanner();
                return;
            }
            if (!string.IsNullOrEmpty(_lastCopiedSiteForPassCheck)
                && string.Equals(clip, _lastCopiedSiteForPassCheck, StringComparison.OrdinalIgnoreCase))
            {
                HideClipboardPassBanner();
                return;
            }
            var current = GetPasswordUi() ?? "";
            if (string.Equals(clip, current, StringComparison.Ordinal))
            {
                HideClipboardPassBanner();
                return;
            }

            _clipboardPassCandidate = clip;
            if (ClipboardPassHeaderText != null)
                ClipboardPassHeaderText.Text = UiStrings.T("ClipboardPassHeader");
            if (ClipboardPassCurrentText != null)
                ClipboardPassCurrentText.Text = string.Format(UiStrings.T("ClipboardPassCurrent"),
                    string.IsNullOrEmpty(current) ? "—" : current);
            if (ClipboardPassClipText != null)
                ClipboardPassClipText.Text = string.Format(UiStrings.T("ClipboardPassClip"), clip);
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
                ClipboardPassPasteSaveBtn.Content = UiStrings.T("ClipboardPassPasteSave");
            if (ClipboardPassDismissBtn != null)
                ClipboardPassDismissBtn.Content = UiStrings.T("ClipboardPassDismiss");
        }

        private void ClipboardPassPasteSave_Click(object sender, RoutedEventArgs e)
        {
            // Capture before Hide clears the candidate; pass explicitly so renew
            // never re-reads a stale PasswordBox/PlainBox value.
            var pass = _clipboardPassCandidate;
            if (string.IsNullOrEmpty(pass))
            {
                HideClipboardPassBanner();
                return;
            }
            var force = CredentialsWouldChange(pass);
            SetPasswordUi(pass);
            HideClipboardPassBanner();
            SaveFromUi(true, pass);
            RefreshPasswordRotateBanner(false);
            if (TrySavePreflight())
                RunNowAsync(true, force, pass);
        }

        /// <summary>Like CredentialsChangedInUi, but with an explicit new password (before SaveFromUi).</summary>
        private bool CredentialsWouldChange(string newPassword)
        {
            if (_settings == null) return true;
            var email = EmailBox != null ? EmailBox.Text.Trim() : "";
            var oldEmail = (_settings.Email ?? "").Trim();
            if (!string.Equals(email, oldEmail, StringComparison.OrdinalIgnoreCase))
                return true;
            string oldPass = null;
            try { oldPass = _settings.GetPassword(); }
            catch { oldPass = null; }
            return !string.Equals(newPassword ?? "", oldPass ?? "", StringComparison.Ordinal);
        }

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

        private void ClipboardPassDismiss_Click(object sender, RoutedEventArgs e)
        {
            HideClipboardPassBanner();
        }

        private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Program.WmShowMe)
            {
                // Pin/taskbar second instance — marshal to dispatcher (may arrive off UI path).
                Dispatcher.BeginInvoke(new Action(ShowFromTray));
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void InitTray()
        {
            _appIcon = IconFactory.GetAppIcon();
            _tray = new WinForms.NotifyIcon();
            _tray.Text = "KAI - AOS Renew";
            _tray.Visible = true;
            _tray.Icon = _appIcon;
            _tray.DoubleClick += delegate { ShowFromTray(); };
            RebuildTrayMenu();
        }

        private void RebuildTrayMenu()
        {
            if (_tray == null) return;
            var lastVin = _keyMonitor.GetLastKnownVin();
            var sig = string.IsNullOrEmpty(lastVin) ? "-" : lastVin;
            if (string.Equals(_trayVinMenuSignature, sig, StringComparison.Ordinal)
                && _tray.ContextMenuStrip != null)
                return;
            _trayVinMenuSignature = sig;

            var menu = new WinForms.ContextMenuStrip();
            menu.Items.Add(UiStrings.T("TrayOpen"), null, delegate { ShowFromTray(); });

            if (string.IsNullOrEmpty(lastVin))
            {
                var empty = menu.Items.Add(UiStrings.T("TrayCopyVin") + " — " + UiStrings.T("KeysVinMissing"));
                empty.Enabled = false;
            }
            else
            {
                var capture = lastVin;
                menu.Items.Add(string.Format(UiStrings.T("TrayVinItem"), capture), null, delegate
                {
                    Dispatcher.BeginInvoke(new Action(() => CopyVinToClipboard(capture)));
                });
            }

            menu.Items.Add(UiStrings.T("TrayAbout"), null, delegate { ShowAbout(); });
            menu.Items.Add(UiStrings.T("TrayExit"), null, delegate { ExitApp(); });
            _tray.ContextMenuStrip = menu;
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
            if (AboutSettingsPathText != null)
                AboutSettingsPathText.Text = UiStrings.T("AboutSettingsPath") + " " + AppSettings.SettingsPath;
            try
            {
                var path = FindChangelogPath();
                if (path != null && File.Exists(path))
                    AboutChangelogBox.Text = File.ReadAllText(path);
                else
                    AboutChangelogBox.Text = UiStrings.T("AboutChangelogMissing");
            }
            catch (Exception ex)
            {
                AboutChangelogBox.Text = ex.Message;
            }
        }

        private static string FindChangelogPath()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var nextToExe = Path.Combine(baseDir, "CHANGELOG.md");
                if (File.Exists(nextToExe)) return nextToExe;
                // Dev: repo root (bin\Release\..\..\..)
                var probe = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "CHANGELOG.md"));
                if (File.Exists(probe)) return probe;
            }
            catch { }
            return null;
        }

        private void TestBalloonTipsBtn_Click(object sender, RoutedEventArgs e)
        {
            TestBalloonTips_Click();
        }

        private async void TestBalloonTips_Click()
        {
            AppendLog(UiStrings.T("BalloonWait"), LogLevel.Info);
            await Task.Delay(3000);
            ShowBalloon(WinForms.ToolTipIcon.Info, UiStrings.T("BalloonInfo"), true);
            AppendLog(UiStrings.T("BalloonInfo"), LogLevel.Info);
            await Task.Delay(4500);
            ShowBalloon(WinForms.ToolTipIcon.Warning, UiStrings.T("BalloonWarning"));
            AppendLog(UiStrings.T("BalloonWarning"), LogLevel.Warning);
            await Task.Delay(4500);
            ShowBalloon(WinForms.ToolTipIcon.Error, UiStrings.T("BalloonError"));
            AppendLog(UiStrings.T("BalloonError"), LogLevel.Error);
            AppendLog(UiStrings.T("BalloonDone"), LogLevel.Info);
        }

        private static string TruncateBalloon(string text)
        {
            if (string.IsNullOrEmpty(text)) return " ";
            text = text.Trim();
            if (text.Length <= 180) return text;
            return text.Substring(0, 177) + "...";
        }

        private void ShowBalloon(WinForms.ToolTipIcon icon, string text, bool allowInfo = false)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(new Action(() => ShowBalloon(icon, text, allowInfo)));
                return;
            }
            // Only Warning/Error in normal use; Info only for tray "Test balloon tips".
            if (icon == WinForms.ToolTipIcon.Info && !allowInfo)
                return;
            if (icon == WinForms.ToolTipIcon.None)
                return;
            text = TruncateBalloon(text);
            // Custom in-app tip only — do not call NotifyIcon.ShowBalloonTip (Windows toast).
            TrayBalloonWindow.ShowTip("KAI - AOS Renew", text, icon);
            var level = "info";
            var kind = "info";
            if (icon == WinForms.ToolTipIcon.Warning)
            {
                level = "warning";
                kind = "warning";
            }
            else if (icon == WinForms.ToolTipIcon.Error)
            {
                level = "error";
                kind = "error";
            }
            BroadcastNotify(new NotifyMessage
            {
                Kind = kind,
                Level = level,
                Title = "KAI - AOS Renew",
                Text = text,
                Seconds = 8
            });
        }

        private static void BroadcastNotify(NotifyMessage msg)
        {
            try { NotifyServer.Instance.Broadcast(msg); }
            catch { }
        }

        private void QueueFooterPublish()
        {
            if (_footerPublishQueued) return;
            _footerPublishQueued = true;
            Dispatcher.BeginInvoke(new Action(PublishFooterNow), DispatcherPriority.Background);
        }

        private void PublishFooterNow()
        {
            _footerPublishQueued = false;
            if (_settings == null || !_settings.NotifyServerEnabled) return;
            if (!NotifyServer.Instance.IsRunning) return;
            try
            {
                BroadcastNotify(new NotifyMessage
                {
                    Kind = "footer",
                    Level = "info",
                    Title = "KAI - AOS Renew",
                    Text = BuildFooterPayload()
                });
            }
            catch { }
        }

        private void SendFooterTo(int clientId)
        {
            if (_settings == null || !_settings.NotifyServerEnabled) return;
            if (!NotifyServer.Instance.IsRunning) return;
            try
            {
                NotifyServer.Instance.SendTo(clientId, new NotifyMessage
                {
                    Kind = "footer",
                    Level = "info",
                    Title = "KAI - AOS Renew",
                    Text = BuildFooterPayload()
                });
            }
            catch { }
        }

        private string BuildFooterPayload()
        {
            var regMain = "";
            var regFail = "";
            if (RegisterAgeText != null)
            {
                var i = 0;
                foreach (var inline in RegisterAgeText.Inlines)
                {
                    var run = inline as Run;
                    if (run == null) continue;
                    if (i == 0) regMain = run.Text ?? "";
                    else regFail += run.Text ?? "";
                    i++;
                }
                if (i == 0)
                    regMain = RegisterAgeText.Text ?? "";
            }
            var tip = RegisterAgeText != null ? (RegisterAgeText.ToolTip as string ?? "") : "";
            var authCooldown = "";
            var authBlocked = "";
            var authCooldownMode = "off";
            var authBlockedMode = "off";
            if (AuthGuardsFooterText != null)
            {
                var i = 0;
                foreach (var inline in AuthGuardsFooterText.Inlines)
                {
                    var run = inline as Run;
                    if (run == null) continue;
                    // runs: cooldown, separator, blocked
                    if (i == 0) authCooldown = run.Text ?? "";
                    else if (i == 2) authBlocked = run.Text ?? "";
                    i++;
                }
            }
            if (IsAuthCooldownActive()) authCooldownMode = "on";
            if (IsAuthRegisterBlocked()) authBlockedMode = "on";
            var authTip = AuthGuardsFooterText != null ? (AuthGuardsFooterText.ToolTip as string ?? "") : "";
            var parts = new[]
            {
                _footerStatusMode ?? "stopped",
                StatusText != null ? (StatusText.Text ?? "") : "",
                NextRunText != null ? (NextRunText.Text ?? "") : "",
                TokenAgeText != null ? (TokenAgeText.Text ?? "") : "",
                _footerTokenMode ?? "none",
                TokenFooterText != null ? (TokenFooterText.Text ?? "") : "",
                _footerSvcMode ?? "none",
                ServicesFooterText != null ? (ServicesFooterText.Text ?? "") : "",
                regMain ?? "",
                regFail ?? "",
                AppVersionDisplay ?? "",
                tip ?? "",
                ThemeManager.IsNight ? "night" : "day",
                authCooldownMode,
                authCooldown ?? "",
                authBlockedMode,
                authBlocked ?? "",
                authTip ?? ""
            };
            return string.Join("\u001f", parts);
        }

        private void OnNotifyMessageFromClient(int clientId, NotifyMessage msg)
        {
            if (msg == null) return;
            var id = clientId;
            var copy = msg;
            Dispatcher.BeginInvoke(new Action(() => HandleClientNotify(id, copy)));
        }

        private void HandleClientNotify(int clientId, NotifyMessage msg)
        {
            if (msg == null) return;
            var kind = (msg.Kind ?? "").Trim().ToLowerInvariant();
            if (kind == "aosapply")
            {
                ApplyAosFromClient(clientId, msg.Text);
                return;
            }
            if (kind == "aoscmd")
            {
                RunAosCmdFromClient(clientId, msg.Text);
                return;
            }
            if (kind == "serverupdate")
            {
                BeginServerUpdateFromClient(clientId);
                return;
            }
            if (kind == "updatefeed")
            {
                var ignore = HandleInboundUpdateFeedAsync(msg.Text);
                return;
            }
            if (kind == "aossyncreq")
            {
                SendAosSyncTo(clientId);
                return;
            }
            if (kind == "updatefeedreq")
            {
                var ignore = HandleUpdateFeedReqAsync(clientId);
                return;
            }
            if (kind == "clientupdating")
            {
                MarkClientUpdating(clientId, true, msg.Text);
                return;
            }
            if (kind == "clientupdatedone")
            {
                MarkClientUpdating(clientId, false, null);
                return;
            }
        }

        private async Task HandleUpdateFeedReqAsync(int clientId)
        {
            try { await RefreshUpdateFeedAsync(); }
            catch { }
            SendUpdateFeedTo(clientId, true);
        }

        private void MarkClientUpdating(int clientId, bool updating, string version)
        {
            if (clientId <= 0) return;
            if (updating)
            {
                _clientsUpdating.Add(clientId);
                AppendLog(string.Format(UiStrings.T("ClientUpdateStarted"), clientId,
                    string.IsNullOrEmpty(version) ? "?" : version), LogLevel.Info);
            }
            else
            {
                _clientsUpdating.Remove(clientId);
            }
            RefreshNotifyClientsCombo();
        }

        private async void BeginServerUpdateFromClient(int clientId)
        {
            try
            {
                var mgr = UpdateService.CreateManager();
                if (!mgr.IsInstalled)
                {
                    SendAosAck(clientId, false, UiStrings.T("UpdateNotInstalled"));
                    return;
                }
                var update = await UpdateService.PeekAsync();
                if (update == null)
                {
                    SendAosAck(clientId, false, UiStrings.T("UpdateNone"));
                    return;
                }
                await ApplySelfUpdateAsync(update, false, clientId,
                    string.Format(UiStrings.T("UpdateSourceClient"), clientId));
            }
            catch (Exception ex)
            {
                try { SendAosAck(clientId, false, ex.Message ?? "Update failed"); }
                catch { }
            }
        }

        private string BuildAosSyncPayload()
        {
            if (_settings == null) return NotifyMessage.EncodeAosSync(1, "", "");
            var hours = _settings.IntervalHours < 1 ? 1 : _settings.IntervalHours;
            string pass = "";
            try { pass = _settings.GetPassword() ?? ""; }
            catch { pass = ""; }
            return NotifyMessage.EncodeAosSync(hours, _settings.Email ?? "", pass);
        }

        private void BroadcastAosSync()
        {
            if (_settings == null || !_settings.NotifyServerEnabled) return;
            try
            {
                BroadcastNotify(new NotifyMessage
                {
                    Kind = "aosSync",
                    Level = "info",
                    Title = "KAI - AOS Renew",
                    Text = BuildAosSyncPayload()
                });
            }
            catch { }
        }

        private void SendAosSyncTo(int clientId)
        {
            if (_settings == null || !_settings.NotifyServerEnabled) return;
            try
            {
                NotifyServer.Instance.SendTo(clientId, new NotifyMessage
                {
                    Kind = "aosSync",
                    Level = "info",
                    Title = "KAI - AOS Renew",
                    Text = BuildAosSyncPayload()
                });
            }
            catch { }
        }

        private string BuildKaiSyncPayload()
        {
            var report = _lastBmwReport;
            if (report == null)
            {
                try { report = ServiceMonitor.Check(); _lastBmwReport = report; }
                catch { report = null; }
            }

            var rows = new List<KaiSvcWireRow>();
            var anyRunning = false;
            if (report != null && report.Services != null)
            {
                for (var i = 0; i < report.Services.Count; i++)
                {
                    var s = report.Services[i];
                    if (s == null) continue;
                    if (s.IsRunning) anyRunning = true;
                    if (rows.Count >= 2) continue;
                    rows.Add(new KaiSvcWireRow
                    {
                        Name = s.DisplayName ?? s.ServiceName ?? "",
                        StatusText = s.Exists ? (s.StatusText ?? "") : UiStrings.T("SvcMissing"),
                        StartTypeText = s.Exists ? (s.StartTypeText ?? "") : "",
                        Ok = s.Ok
                    });
                }
            }

            var allOk = report != null && report.AllOk;
            var svcAlert = report == null || !report.AllOk;
            return NotifyMessage.EncodeKaiSync(
                KaiInstallText != null ? KaiInstallText.Text : "",
                KaiUrlRegisterBox != null ? KaiUrlRegisterBox.Text : "",
                KaiUrlWebEamBox != null ? KaiUrlWebEamBox.Text : "",
                KaiUrlUploadBox != null ? KaiUrlUploadBox.Text : "",
                KaiTokenExpiryText != null ? KaiTokenExpiryText.Text : "",
                allOk, anyRunning, svcAlert, _kaiServiceCmdBusy, rows);
        }

        private void BroadcastKaiSync()
        {
            if (_settings == null || !_settings.NotifyServerEnabled) return;
            try
            {
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.BeginInvoke(new Action(BroadcastKaiSync));
                    return;
                }
                BroadcastNotify(new NotifyMessage
                {
                    Kind = "kaiSync",
                    Level = "info",
                    Title = "KAI - AOS Renew",
                    Text = BuildKaiSyncPayload()
                });
            }
            catch { }
        }

        private void SendKaiSyncTo(int clientId)
        {
            if (_settings == null || !_settings.NotifyServerEnabled) return;
            try
            {
                NotifyServer.Instance.SendTo(clientId, new NotifyMessage
                {
                    Kind = "kaiSync",
                    Level = "info",
                    Title = "KAI - AOS Renew",
                    Text = BuildKaiSyncPayload()
                });
            }
            catch { }
        }

        private async void RunKaiServiceCmdFromClient(int clientId, bool fix)
        {
            try
            {
                var err = fix ? await KaiFixServicesCoreAsync() : await KaiStopServicesCoreAsync();
                if (err == null)
                    SendAosAck(clientId, true, fix ? "fixServices" : "restartServices");
                else
                    SendAosAck(clientId, false, err);
                BroadcastKaiSync();
            }
            catch (Exception ex)
            {
                SendAosAck(clientId, false, ex.Message);
            }
        }

        private void SendAosAck(int clientId, bool ok, string text)
        {
            try
            {
                NotifyServer.Instance.SendTo(clientId, new NotifyMessage
                {
                    Kind = "aosAck",
                    Level = ok ? "success" : "error",
                    Title = "KAI - AOS Renew",
                    Text = text ?? ""
                });
            }
            catch { }
        }

        private void ApplyAosFromClient(int clientId, string payload)
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
            int hours;
            string email;
            string password;
            bool? forceRegisterOpt;
            if (!NotifyMessage.TryParseAosSync(payload, out hours, out email, out password, out forceRegisterOpt))
            {
                SendAosAck(clientId, false, "Invalid AOS payload");
                return;
            }
            email = (email ?? "").Trim();
            password = (password ?? "").Trim();
            if (string.IsNullOrEmpty(email))
            {
                SendAosAck(clientId, false, UiStrings.T("MissingEmail"));
                return;
            }
            if (email.IndexOf('@') < 0 || email.IndexOf(' ') >= 0)
            {
                SendAosAck(clientId, false, UiStrings.T("InvalidUser"));
                return;
            }
            if (string.IsNullOrEmpty(password))
            {
                SendAosAck(clientId, false, UiStrings.T("MissingPassword"));
                return;
            }
            if (hours < 1 || hours > 168)
            {
                SendAosAck(clientId, false, UiStrings.T("InvalidInterval"));
                return;
            }
            try
            {
                if (EmailBox != null) EmailBox.Text = email;
                SetPasswordUi(password);
                if (IntervalBox != null) IntervalBox.Text = hours.ToString();
                var forceRegister = forceRegisterOpt.HasValue
                    ? forceRegisterOpt.Value
                    : CredentialsChangedInUi();
                SaveFromUi(true);
                string fail;
                if (!TrySavePreflight(out fail, false))
                {
                    SendAosAck(clientId, false, fail ?? "Preflight failed");
                    BroadcastAosSync();
                    return;
                }
                RunNowAsync(true, forceRegister);
                SendAosAck(clientId, true, "OK");
                BroadcastAosSync();
            }
            catch (Exception ex)
            {
                SendAosAck(clientId, false, ex.Message);
            }
        }

        private void RunAosCmdFromClient(int clientId, string cmd)
        {
            var c = (cmd ?? "").Trim().ToLowerInvariant();
            try
            {
                if (c == "checktoken")
                {
                    RunHealthOnlyAsync();
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
                if (c == "forceregister")
                {
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
                    AppendLog(UiStrings.T("ForceRegisterStarted"), LogLevel.Warning);
                    RunNowAsync(false, true, null, true);
                    SendAosAck(clientId, true, "forceRegister");
                    BroadcastAosSync();
                    return;
                }
                if (c == "refreshkeys")
                {
                    RefreshKeysTab(true);
                    var rows = GetRecentKeyRows(10) ?? new List<KeyActivityEvent>();
                    // Skip empty keySync after Clear Keys — do not wipe the requesting client.
                    if (rows.Count > 0)
                    {
                        var payload = NotifyMessage.EncodeKeySync(rows) ?? "";
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
                    RunKaiServiceCmdFromClient(clientId, true);
                    return;
                }
                if (c == "stopservices" || c == "restartservices")
                {
                    RunKaiServiceCmdFromClient(clientId, false);
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
                SendAosAck(clientId, false, "Unknown command");
            }
            catch (Exception ex)
            {
                SendAosAck(clientId, false, ex.Message);
            }
        }

        private void OnNotifyClientDisconnected(int clientId)
        {
            var idCopy = clientId;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_clientsUpdating.Remove(idCopy))
                    RefreshNotifyClientsCombo();
            }));
        }

        private void OnNotifyClientConnected(int clientId)
        {
            var idCopy = clientId;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                SendFooterTo(idCopy);
                SendActivitySyncTo(idCopy);
                SendRegisterSyncTo(idCopy);
                SendAosSyncTo(idCopy);
                SendKaiSyncTo(idCopy);
                SendUpdateFeedTo(idCopy);
            }));
            try
            {
                if (_settings == null || !_settings.NotifyServerEnabled) return;
                // Authoritative snapshot only — never invent Waiting stubs from VIN cache.
                // Skip empty after Clear Keys so reconnect does not wipe the client's history.
                var rows = GetRecentKeyRows(10) ?? new List<KeyActivityEvent>();
                if (rows.Count == 0)
                    return;
                var payload = NotifyMessage.EncodeKeySync(rows) ?? "";
                _lastKeySyncSignature = KeySyncSignature(rows);
                NotifyServer.Instance.SendTo(clientId, new NotifyMessage
                {
                    Kind = "keySync",
                    Level = "info",
                    Title = "KAI - AOS Renew",
                    Text = payload
                });
            }
            catch (Exception ex)
            {
                try { RenewEngine.Log("Notify keySync: " + ex.Message); }
                catch { }
            }
        }

        private void RefreshNotifyServerIpDisplay()
        {
            if (NotifyServerIpBox == null) return;
            try { NotifyServerIpBox.Text = NotifyServer.GetLocalIpv4Summary(); }
            catch { NotifyServerIpBox.Text = "127.0.0.1"; }
        }

        private void ParseAndSetNotifyServerPort()
        {
            if (_settings == null) return;
            int port = NotifyServer.DefaultPort;
            if (NotifyServerPortBox != null)
            {
                int parsed;
                if (int.TryParse(NotifyServerPortBox.Text.Trim(), out parsed))
                    port = parsed;
            }
            if (port < 1024 || port > 65535)
                port = NotifyServer.DefaultPort;
            _settings.NotifyServerPort = port;
            if (NotifyServerPortBox != null)
                NotifyServerPortBox.Text = port.ToString();
        }

        private void ApplyNotifyServerFromSettings()
        {
            if (_settings == null) return;
            try
            {
                NotifyServer.Instance.ApplySettings(_settings.NotifyServerEnabled, _settings.NotifyServerPort);
            }
            catch (Exception ex)
            {
                AppendLog(string.Format(UiStrings.T("LogNotifyServer"), ex.Message), LogLevel.Warning);
            }
            RefreshNotifyServerIpDisplay();
            RefreshNotifyClientsCombo();
        }

        private void NotifyServer_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingUi || _settings == null) return;
            _settings.NotifyServerEnabled = NotifyServerEnableCheck != null && NotifyServerEnableCheck.IsChecked == true;
            ParseAndSetNotifyServerPort();
            try { _settings.Save(); }
            catch { }
            ApplyNotifyServerFromSettings();
        }

        private void NotifyServerPort_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_loadingUi || _settings == null) return;
            ParseAndSetNotifyServerPort();
            try { _settings.Save(); }
            catch { }
            ApplyNotifyServerFromSettings();
        }

        private sealed class NotifySendTarget
        {
            public int ClientId; // 0 = all
            public string Label { get; set; }
        }

        private void RefreshNotifyClientsCombo()
        {
            if (NotifySendToCombo == null) return;
            var prevId = -1;
            var prev = NotifySendToCombo.SelectedItem as NotifySendTarget;
            if (prev != null) prevId = prev.ClientId;

            var items = new List<NotifySendTarget>();
            items.Add(new NotifySendTarget
            {
                ClientId = 0,
                Label = UiStrings.T("NotifyAllClients") + " (" + NotifyServer.Instance.ClientCount + ")"
            });
            try
            {
                foreach (var c in NotifyServer.Instance.GetClients())
                {
                    var label = c.Display ?? ("#" + c.Id);
                    if (_clientsUpdating.Contains(c.Id))
                        label = string.Format(UiStrings.T("ClientUpdatingLabel"), label);
                    items.Add(new NotifySendTarget
                    {
                        ClientId = c.Id,
                        Label = label
                    });
                }
            }
            catch { }

            NotifySendToCombo.ItemsSource = items;
            NotifySendTarget select = items[0];
            if (prevId > 0)
            {
                foreach (var it in items)
                {
                    if (it.ClientId == prevId)
                    {
                        select = it;
                        break;
                    }
                }
            }
            NotifySendToCombo.SelectedItem = select;
            UpdateNotifyFirewallHint();
        }

        private void UpdateNotifyFirewallHint()
        {
            if (NotifyFirewallHintText == null) return;
            var enabled = _settings != null && _settings.NotifyServerEnabled && NotifyServer.Instance.IsRunning;
            var noClients = NotifyServer.Instance.ClientCount <= 0;
            if (enabled && noClients)
            {
                var port = NotifyServer.Instance.Port;
                if (port <= 0 && _settings != null) port = _settings.NotifyServerPort;
                if (port <= 0) port = NotifyServer.DefaultPort;
                NotifyFirewallHintText.Text = string.Format(UiStrings.T("NotifyFirewallHint"), port);
                NotifyFirewallHintText.Visibility = Visibility.Visible;
            }
            else
            {
                NotifyFirewallHintText.Visibility = Visibility.Collapsed;
            }
        }

        private void NotifyRefreshClients_Click(object sender, RoutedEventArgs e)
        {
            RefreshNotifyClientsCombo();
            RefreshNotifyServerIpDisplay();
            UpdateNotifyFirewallHint();
        }

        private void NotifyTestVin_Click(object sender, RoutedEventArgs e)
        {
            var tipSec = _settings != null ? _settings.KeysVinTipSeconds : 8;
            if (tipSec < 3) tipSec = 3;
            SendNotifyTest(new NotifyMessage
            {
                Kind = "vin",
                Level = "info",
                Title = "KAI - AOS Renew",
                Text = "Test VIN WBATESTVIN0000001",
                Vin = "WBATESTVIN0000001",
                Action = "copyVin",
                Seconds = tipSec
            }, "VIN");
        }

        private void NotifyTestInfo_Click(object sender, RoutedEventArgs e)
        {
            SendNotifyTest(new NotifyMessage
            {
                Kind = "info",
                Level = "info",
                Title = "KAI - AOS Renew",
                Text = "Test info tip (remote)",
                Seconds = 8
            }, "Info");
        }

        private void NotifyTestWarning_Click(object sender, RoutedEventArgs e)
        {
            SendNotifyTest(new NotifyMessage
            {
                Kind = "warning",
                Level = "warning",
                Title = "KAI - AOS Renew",
                Text = "Test warning tip (remote)",
                Seconds = 8
            }, "Warning");
        }

        private void NotifyTestError_Click(object sender, RoutedEventArgs e)
        {
            SendNotifyTest(new NotifyMessage
            {
                Kind = "error",
                Level = "error",
                Title = "KAI - AOS Renew",
                Text = "Test error tip (remote)",
                Seconds = 8
            }, "Error");
        }

        private void SendNotifyTest(NotifyMessage msg, string kindLabel)
        {
            if (_settings == null || !_settings.NotifyServerEnabled || !NotifyServer.Instance.IsRunning)
            {
                AppendLog(UiStrings.T("NotifyTestServerOff"), LogLevel.Warning);
                return;
            }
            RefreshNotifyClientsCombo();
            if (NotifyServer.Instance.ClientCount <= 0)
            {
                AppendLog(UiStrings.T("NotifyTestNoClients"), LogLevel.Warning);
                return;
            }
            var target = NotifySendToCombo != null
                ? NotifySendToCombo.SelectedItem as NotifySendTarget
                : null;
            string destLabel;
            if (target == null || target.ClientId == 0)
            {
                NotifyServer.Instance.Broadcast(msg);
                destLabel = UiStrings.T("NotifyAllClients") + " (" + NotifyServer.Instance.ClientCount + ")";
            }
            else
            {
                if (!NotifyServer.Instance.SendTo(target.ClientId, msg))
                {
                    AppendLog(UiStrings.T("NotifyTestNoClients"), LogLevel.Warning);
                    RefreshNotifyClientsCombo();
                    return;
                }
                destLabel = target.Label;
            }
            AppendLog(string.Format(UiStrings.T("NotifyTestSent"), kindLabel, destLabel), LogLevel.Info);
        }

        private void ShowFromTray()
        {
            _startedHiddenToTray = false;
            ShowActivated = true;
            ShowInTaskbar = true;
            Visibility = Visibility.Visible;
            Show();
            WindowState = WindowState.Normal;
            Activate();
            try
            {
                Topmost = true;
                Topmost = false;
            }
            catch { }
            Activate();
        }

        private void RenewTabAlertDot_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            e.Handled = true;
            JumpToAlertTab(TabRenew);
        }

        private void KaiTabAlertDot_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            e.Handled = true;
            JumpToAlertTab(TabKai);
        }

        private void JumpToAlertTab(TabItem tab)
        {
            if (tab == null) return;
            ShowFromTray();
            if (_aboutVisible)
                HideAboutPanel();
            tab.IsSelected = true;
        }

        private void ExitApp()
        {
            _exitRequested = true;
            try { _keysRefreshTimer.Stop(); }
            catch { }
            try { _keysHighlightTimer.Stop(); }
            catch { }
            try { _keyMonitor.Dispose(); }
            catch { }
            try { _keyReaderLogTailer.Dispose(); }
            catch { }
            try { NotifyServer.Instance.ClientConnected -= OnNotifyClientConnected; }
            catch { }
            try { NotifyServer.Instance.ClientDisconnected -= OnNotifyClientDisconnected; }
            catch { }
            try { NotifyServer.Instance.MessageFromClient -= OnNotifyMessageFromClient; }
            catch { }
            try { NotifyServer.Instance.Dispose(); }
            catch { }
            if (_tray != null) _tray.Visible = false;
            Application.Current.Shutdown();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_exitRequested) return;
            var minimizeOnClose = _settings == null || _settings.MinimizeOnClose;
            if (minimizeOnClose)
            {
                e.Cancel = true;
                Hide();
                ShowInTaskbar = false;
                return;
            }
            // Real exit (same as tray Exit) — required with OnExplicitShutdown.
            ExitApp();
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

        private void StartWithWindows_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingUi || _settings == null) return;
            _settings.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
            _settings.Save();
            ApplyStartup(_settings.StartWithWindows);
            UpdateMinimizeToTrayEnabled();
            AppendLog(_settings.StartWithWindows
                ? UiStrings.T("AutostartOn")
                : UiStrings.T("AutostartOff"), true);
        }

        private void UpdateMinimizeToTrayEnabled()
        {
            // Always editable; minimize-on-launch still requires Start with Windows at runtime.
            if (MinimizeToTrayCheck != null)
                MinimizeToTrayCheck.IsEnabled = true;
            if (MinimizeToTrayLabel != null)
                MinimizeToTrayLabel.Opacity = 1.0;
        }

        private void LoadSettingsToUi()
        {
            _loadingUi = true;
            try
            {
                _settings = AppSettings.Load();
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
                RefreshNotifyServerIpDisplay();
                RefreshNotifyClientsCombo();
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
            }
        }

        private void UseSystemProxy_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingUi || _settings == null) return;
            _settings.UseSystemProxy = UseSystemProxyCheck.IsChecked == true;
            _settings.Save();
        }

        private void BrowserPassHint_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingUi || _settings == null) return;
            _settings.BrowserPasswordHintOnAuthFail = BrowserPassHintCheck.IsChecked == true;
            _settings.Save();
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
                this, found.BrowserName, msg, copiedHint, statusLine, statusOk, hint, offerFillSave);
            if (dlg.OpenedPasswordManager)
                _focusPasswordOnActivate = true;
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
                    RunNowAsync(true, force, pass);
            }
        }

        private static string SiteForClipboard(string sourceUrl)
        {
            if (string.IsNullOrWhiteSpace(sourceUrl))
                return null;
            try
            {
                var uri = new Uri(sourceUrl);
                if (!string.IsNullOrEmpty(uri.Host))
                    return uri.Host;
            }
            catch { }
            return sourceUrl.Trim();
        }

        private const string BmwAosPortalUrl = "https://aos.bmwgroup.com/";

        private void OpenBmwSso_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                DiagProcess.Start(BmwAosPortalUrl);
            }
            catch (Exception ex)
            {
                AppendLog(string.Format(UiStrings.T("OpenBmwSsoFail"), ex.Message), LogLevel.Warning);
                MessageBox.Show(this, string.Format(UiStrings.T("OpenBmwSsoFail"), ex.Message),
                    "KAI - AOS Renew", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void KaiRefresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshKaiDiagnostics(true);
        }

        private async void KaiFixServices_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            if (KaiFixServicesBtn != null) KaiFixServicesBtn.IsEnabled = false;
            if (KaiStopServicesBtn != null) KaiStopServicesBtn.IsEnabled = false;
            await KaiFixServicesCoreAsync();
        }

        /// <returns>Null on success; otherwise error text.</returns>
        private async Task<string> KaiFixServicesCoreAsync()
        {
            if (_busy) return "Busy";
            SetKaiServiceCmdBusy(true);
            SetBusy(true);
            try
            {
                var result = await Task.Run(() => ServiceMonitor.TryFixAll());
                if (!result.Ok && result.NeedsElevation)
                {
                    var elevated = await Task.Run(() => RunElevatedServiceAction(ServiceMonitor.ArgFix));
                    if (elevated == null)
                    {
                        AppendLog(UiStrings.T("KaiElevateCancelled"), LogLevel.Warning);
                        ShowBalloon(WinForms.ToolTipIcon.Warning, UiStrings.T("KaiElevateCancelled"));
                        CheckBmwServices(false, true);
                        return UiStrings.T("KaiElevateCancelled");
                    }
                    result = elevated;
                }

                if (result.Ok)
                    AppendLog(UiStrings.T("KaiFixOk") + " " + result.Message, LogLevel.Success);
                else
                {
                    var fail = string.Format(UiStrings.T("KaiFixFail"), result.Message);
                    AppendLog(fail, LogLevel.Error);
                    ShowBalloon(WinForms.ToolTipIcon.Error, TruncateBalloon(fail));
                }
                CheckBmwServices(false, true);
                return result.Ok ? null : (result.Message ?? "Fix failed");
            }
            catch (Exception ex)
            {
                AppendLog(string.Format(UiStrings.T("KaiFixFail"), ex.Message), LogLevel.Error);
                ShowBalloon(WinForms.ToolTipIcon.Error, TruncateBalloon(ex.Message));
                CheckBmwServices(false, true);
                return ex.Message;
            }
            finally
            {
                SetKaiServiceCmdBusy(false);
                SetBusy(false);
            }
        }

        private async void KaiStopServices_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            if (KaiFixServicesBtn != null) KaiFixServicesBtn.IsEnabled = false;
            if (KaiStopServicesBtn != null) KaiStopServicesBtn.IsEnabled = false;
            await KaiStopServicesCoreAsync();
        }

        /// <returns>Null on success; otherwise error text.</returns>
        private async Task<string> KaiStopServicesCoreAsync()
        {
            if (_busy) return "Busy";
            SetKaiServiceCmdBusy(true);
            SetBusy(true);
            try
            {
                var result = await Task.Run(() => ServiceMonitor.TryRestartAll());
                if (!result.Ok && result.NeedsElevation)
                {
                    var elevated = await Task.Run(() => RunElevatedServiceAction(ServiceMonitor.ArgRestart));
                    if (elevated == null)
                    {
                        AppendLog(UiStrings.T("KaiElevateCancelled"), LogLevel.Warning);
                        ShowBalloon(WinForms.ToolTipIcon.Warning, UiStrings.T("KaiElevateCancelled"));
                        CheckBmwServices(false, true);
                        return UiStrings.T("KaiElevateCancelled");
                    }
                    result = elevated;
                }

                if (result.Ok)
                    AppendLog(UiStrings.T("KaiStopOk") + " " + result.Message, LogLevel.Success);
                else
                {
                    var fail = string.Format(UiStrings.T("KaiStopFail"), result.Message);
                    AppendLog(fail, LogLevel.Error);
                    ShowBalloon(WinForms.ToolTipIcon.Error, TruncateBalloon(fail));
                }
                CheckBmwServices(false, true);
                return result.Ok ? null : (result.Message ?? "Restart failed");
            }
            catch (Exception ex)
            {
                AppendLog(string.Format(UiStrings.T("KaiStopFail"), ex.Message), LogLevel.Error);
                ShowBalloon(WinForms.ToolTipIcon.Error, TruncateBalloon(ex.Message));
                CheckBmwServices(false, true);
                return ex.Message;
            }
            finally
            {
                SetKaiServiceCmdBusy(false);
                SetBusy(false);
            }
        }

        private void SetKaiServiceCmdBusy(bool busy)
        {
            if (_kaiServiceCmdBusy == busy) return;
            _kaiServiceCmdBusy = busy;
            BroadcastKaiSync();
        }

        /// <returns>Null if UAC cancelled; otherwise result from elevated process exit + recheck message.</returns>
        private static BmwServiceActionResult RunElevatedServiceAction(string arg)
        {
            try
            {
                var exe = DiagProcess.GetCurrentProcess().MainModule.FileName;
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = arg,
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (var p = DiagProcess.Start(psi))
                {
                    if (p == null) return null;
                    p.WaitForExit(120000);
                    var ok = p.HasExited && p.ExitCode == 0;
                    var after = ServiceMonitor.Check();
                    string message;
                    if (arg == ServiceMonitor.ArgStop)
                        message = ok ? "Elevated stop done." : (after.AlertMessage ?? ("Elevated stop exit " + p.ExitCode));
                    else if (arg == ServiceMonitor.ArgRestart)
                        message = ok && after.AllOk
                            ? "Elevated restart done."
                            : (after.AlertMessage ?? ("Elevated restart exit " + p.ExitCode));
                    else if (ok && after.AllOk)
                        message = "Elevated fix done.";
                    else if (ok)
                        message = after.AlertMessage ?? "Elevated fix incomplete.";
                    else
                        message = after.AlertMessage ?? ("Elevated fix exit " + p.ExitCode);
                    return new BmwServiceActionResult
                    {
                        Ok = arg == ServiceMonitor.ArgStop ? ok : (ok && after.AllOk),
                        NeedsElevation = false,
                        Message = message
                    };
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // User cancelled UAC
                return null;
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

        /// <param name="forceBalloon">Startup: always balloon if problem.</param>
        /// <param name="forceBalloonOnProblem">Refresh: balloon if problem even if signature unchanged.</param>
        private void CheckBmwServices(bool forceBalloon, bool forceBalloonOnProblem)
        {
            _uiOp = "CheckBmwServices";
            try
            {
                var report = ServiceMonitor.Check();
                _lastBmwReport = report;
                UpdateServicesFooter(report.AllOk);
                PopulateServicesGrid(report);
                UpdateServiceActionButtons(report);

                if (report.AllOk)
                {
                    if (_serviceCheckDoneOnce && _lastServiceProblemSignature != "")
                        AppendLog(UiStrings.T("KaiServicesOk"), LogLevel.Success);
                    _lastServiceProblemSignature = "";
                    _serviceCheckDoneOnce = true;
                    return;
                }

                var msg = string.Format(UiStrings.T("KaiServicesProblem"), report.AlertMessage ?? "");
                var sig = report.ProblemSignature ?? msg;
                var changed = !string.Equals(sig, _lastServiceProblemSignature, StringComparison.Ordinal);
                var shouldBalloon = forceBalloon || forceBalloonOnProblem || changed;
                var shouldLog = forceBalloon || forceBalloonOnProblem || changed || !_serviceCheckDoneOnce;

                if (shouldLog)
                    AppendLog(msg, LogLevel.Error);
                if (shouldBalloon)
                    ShowBalloon(WinForms.ToolTipIcon.Error, TruncateBalloon(msg));

                _lastServiceProblemSignature = sig;
                _serviceCheckDoneOnce = true;
            }
            catch (Exception ex)
            {
                _lastBmwReport = null;
                UpdateServicesFooter(false);
                UpdateServiceActionButtons(null);
                AppendLog(ex.Message, LogLevel.Error);
                ShowBalloon(WinForms.ToolTipIcon.Error, TruncateBalloon(ex.Message));
            }
            finally
            {
                BroadcastKaiSync();
            }
        }

        private void PopulateServicesGrid(BmwServicesReport report)
        {
            if (KaiSvc1Name == null) return;
            var list = report != null ? report.Services : null;
            SetServiceRow(0, list);
            SetServiceRow(1, list);
        }

        private void SetServiceRow(int index, System.Collections.Generic.List<BmwServiceStatus> list)
        {
            TextBlock nameTb = index == 0 ? KaiSvc1Name : KaiSvc2Name;
            TextBlock statusTb = index == 0 ? KaiSvc1Status : KaiSvc2Status;
            TextBlock startTb = index == 0 ? KaiSvc1Start : KaiSvc2Start;
            if (nameTb == null) return;

            if (list == null || index >= list.Count)
            {
                nameTb.Text = "—";
                statusTb.Text = "—";
                startTb.Text = "—";
                return;
            }

            var s = list[index];
            nameTb.Text = s.DisplayName ?? s.ServiceName ?? "—";
            if (!s.Exists)
            {
                statusTb.Text = UiStrings.T("SvcMissing");
                startTb.Text = "—";
            }
            else
            {
                statusTb.Text = s.StatusText ?? "—";
                startTb.Text = s.StartTypeText ?? "—";
            }

            Brush primary;
            Brush okBrush;
            Brush errBrush;
            try
            {
                primary = (Brush)FindResource("TextPrimaryBrush");
                okBrush = (Brush)FindResource("StatusOkBrush");
                errBrush = (Brush)FindResource("StatusErrorBrush");
            }
            catch
            {
                primary = Brushes.Black;
                okBrush = Brushes.ForestGreen;
                errBrush = Brushes.OrangeRed;
            }

            nameTb.Foreground = s.Ok ? primary : errBrush;
            statusTb.Foreground = s.Exists && s.IsRunning ? okBrush : errBrush;
            if (!s.Exists)
                startTb.Foreground = errBrush;
            else
                startTb.Foreground = s.IsAutomatic ? okBrush : errBrush;
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

        private void SetKaiServiceAlert(bool on)
        {
            _kaiServiceAlert = on;
            if (KaiTabAlertDot != null)
                KaiTabAlertDot.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            SyncAlertBlinkTimer();
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
            var any = _kaiServiceAlert || _renewAlert || _keysAlert;
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

        private void ApplyAlertBlinkVisuals()
        {
            var opacity = _alertBlinkOn ? 1.0 : 0.15;
            if (KaiTabAlertDot != null)
                KaiTabAlertDot.Opacity = _kaiServiceAlert ? opacity : 1.0;
            if (RenewTabAlertDot != null)
                RenewTabAlertDot.Opacity = _renewAlert ? opacity : 1.0;
            if (KeysTabAlertDot != null)
                KeysTabAlertDot.Opacity = _keysAlert ? opacity : 1.0;
            if (KaiFixServicesBtn != null)
                KaiFixServicesBtn.Opacity = (_kaiServiceAlert
                    && KaiFixServicesBtn.Visibility == Visibility.Visible
                    && KaiFixServicesBtn.IsEnabled)
                    ? opacity
                    : 1.0;
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

        private void ApplyServicesBlinkVisuals()
        {
            if (ServicesFooterText == null) return;
            ServicesFooterText.Opacity = _servicesFail ? (_servicesBlinkOn ? 1.0 : 0.2) : 1.0;
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
            var icon = showFail
                ? WinForms.ToolTipIcon.Warning
                : WinForms.ToolTipIcon.Info;
            AppendLog(title + " — " + DateTime.Now.ToString("g"),
                showFail ? LogLevel.Warning : LogLevel.Info);
            TrayBalloonWindow.ShowSticky(title, body, icon);
        }

        private static string TrimLabelColon(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Trim().TrimEnd(':').Trim();
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
            QueueFooterPublish();
        }

        private void MaybeDebugTokenSticky(string mode)
        {
            mode = string.IsNullOrEmpty(mode) ? "none" : mode;
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
            var title = UiStrings.T(fail ? "TokenFooterFail" : "TokenFooterOk");
            var body = TokenFooterText != null ? (TokenFooterText.Text ?? title) : title;
            AppendLog(title + " — " + DateTime.Now.ToString("g"),
                fail ? LogLevel.Warning : LogLevel.Info);
            TrayBalloonWindow.ShowSticky(title, body,
                fail ? WinForms.ToolTipIcon.Warning : WinForms.ToolTipIcon.Info);
        }

        private TimeSpan? GetTokenRemaining()
        {
            if (_settings == null) return null;
            if (_settings.LastTokenExpiryUtc.HasValue)
                return _settings.LastTokenExpiryUtc.Value - DateTime.UtcNow;
            if (_settings.LastTokenExpiresInSeconds > 0 && _settings.LastSuccessUtc.HasValue)
            {
                var end = _settings.LastSuccessUtc.Value.AddSeconds(_settings.LastTokenExpiresInSeconds);
                return end - DateTime.UtcNow;
            }
            return null;
        }

        private void RefreshKaiTokenExpiryLabel()
        {
            if (KaiTokenExpiryText == null || _settings == null) return;
            if (_settings.LastTokenExpiryUtc.HasValue)
            {
                var local = _settings.LastTokenExpiryUtc.Value.ToLocalTime();
                KaiTokenExpiryText.Text = string.Format(UiStrings.T("KaiTokenExpiryAt"), local.ToString("g"));
            }
            else
            {
                KaiTokenExpiryText.Text = UiStrings.T("KaiTokenExpiryNone");
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
        }

        private void MinimizeToTray_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingUi || _settings == null) return;
            _settings.MinimizeToTray = MinimizeToTrayCheck.IsChecked == true;
            _settings.Save();
        }

        private void LogFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingUi) return;
            var code = LogFilterCombo.SelectedValue as string;
            if (string.IsNullOrEmpty(code)) return;
            _logFilter = code;
            RefreshLogView();
        }

        private void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            _logEntries.Clear();
            RefreshLogView();
            BroadcastActivityClear();
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

        private bool TryBeginSaveGesture()
        {
            if (_remoteServerUpdateBusy)
            {
                try { Activate(); } catch { }
                MessageBox.Show(this, UiStrings.T("AosSaveUpdating"), "KAI - AOS Renew",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (_busy || _saveInFlight)
                return false;
            if ((DateTime.UtcNow - _saveClickUtc).TotalMilliseconds < 500)
                return false;
            _saveClickUtc = DateTime.UtcNow;
            _saveInFlight = true;
            return true;
        }

        private void EndSaveGesture()
        {
            _saveInFlight = false;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!TryBeginSaveGesture())
                return;
            try
            {
                if (!ValidateAosCredentials(true))
                    return;
                int hours;
                if (!ValidateInterval(out hours, true))
                    return;
                var path = ConfigPathBox != null ? ConfigPathBox.Text.Trim() : "";
                if (!ValidateConfigPath(path, true))
                    return;

                var forceRegister = CredentialsChangedInUi();
                if (forceRegister)
                {
                    var confirm = MessageBox.Show(
                        this,
                        UiStrings.T("CredentialsChangedConfirm"),
                        "KAI - AOS Renew",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question,
                        MessageBoxResult.Yes);
                    if (confirm != MessageBoxResult.Yes)
                        forceRegister = false;
                }
                HideClipboardPassBanner();
                ClearAosFieldInvalidHighlights();
                SaveFromUi(true);
                if (!TrySavePreflight())
                    return;
                RunNowAsync(true, forceRegister);
                BroadcastAosSync();
            }
            finally
            {
                EndSaveGesture();
            }
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
                AppendLog(UiStrings.T("ForceRegisterStarted"), LogLevel.Warning);
                RunNowAsync(false, true, null, true);
                BroadcastAosSync();
            }
            finally
            {
                EndSaveGesture();
            }
        }

        private bool CredentialsChangedInUi()
        {
            if (_settings == null) return true;
            var email = EmailBox != null ? EmailBox.Text.Trim() : "";
            var oldEmail = (_settings.Email ?? "").Trim();
            if (!string.Equals(email, oldEmail, StringComparison.OrdinalIgnoreCase))
                return true;
            string oldPass = null;
            try { oldPass = _settings.GetPassword(); }
            catch { oldPass = null; }
            var newPass = (GetPasswordUi() ?? "").Trim();
            return !string.Equals(newPass, oldPass ?? "", StringComparison.Ordinal);
        }

        /// <summary>Path + services must exist before renew after Save. Stopped services still OK.</summary>
        private bool TrySavePreflight()
        {
            string unused;
            return TrySavePreflight(out unused, true);
        }

        private bool TrySavePreflight(out string failMessage, bool showUi)
        {
            failMessage = null;
            var path = _settings != null ? _settings.ConfigPath : null;
            if (!IsValidConfigPath(path))
            {
                failMessage = UiStrings.T("PreflightPathFail");
                AppendLog(failMessage, LogLevel.Warning);
                if (showUi)
                    MessageBox.Show(this, failMessage, "KAI - AOS Renew", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            try
            {
                var report = ServiceMonitor.Check();
                if (report == null || report.Services == null || report.Services.Count == 0)
                {
                    failMessage = UiStrings.T("PreflightServicesMissing");
                    AppendLog(failMessage, LogLevel.Warning);
                    if (showUi)
                        MessageBox.Show(this, failMessage, "KAI - AOS Renew", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
                for (var i = 0; i < report.Services.Count; i++)
                {
                    var s = report.Services[i];
                    if (s == null || !s.Exists)
                    {
                        failMessage = UiStrings.T("PreflightServicesMissing");
                        AppendLog(failMessage, LogLevel.Warning);
                        if (showUi)
                            MessageBox.Show(this, failMessage, "KAI - AOS Renew", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                failMessage = string.Format(UiStrings.T("PreflightServicesMissing") + " ({0})", ex.Message);
                AppendLog(failMessage, LogLevel.Warning);
                if (showUi)
                    MessageBox.Show(this, failMessage, "KAI - AOS Renew", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return true;
        }

        private void StartTimer_Click(object sender, RoutedEventArgs e)
        {
            StartTimer(true);
        }

        private void StopTimer_Click(object sender, RoutedEventArgs e)
        {
            StopTimer();
        }

        private void CheckToken_Click(object sender, RoutedEventArgs e)
        {
            RunHealthOnlyAsync();
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
            if (KeysAutoCopyVinCheck != null)
                _settings.KeysAutoCopyVin = KeysAutoCopyVinCheck.IsChecked == true;
            if (NotifyServerEnableCheck != null)
                _settings.NotifyServerEnabled = NotifyServerEnableCheck.IsChecked == true;
            ParseAndSetNotifyServerPort();
            if (KeysStallMinutesBox != null)
            {
                int stall;
                if (!int.TryParse(KeysStallMinutesBox.Text.Trim(), out stall) || stall < 1)
                    stall = 2;
                if (stall > 60) stall = 60;
                _settings.KeysStallMinutes = stall;
                KeysStallMinutesBox.Text = stall.ToString();
            }
            if (LogVerboseCheck != null)
                _settings.LogVerbose = LogVerboseCheck.IsChecked == true;
            if (MinimizeOnCloseCheck != null)
                _settings.MinimizeOnClose = MinimizeOnCloseCheck.IsChecked == true;
            if (AutoUpdateCheck != null)
                _settings.AutoUpdate = AutoUpdateCheck.IsChecked == true;
            ParseAndSetAuthCooldownMinutes();
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
            if (announce)
                AppendLog(UiStrings.T("SettingsSaved"), true);
            RefreshPasswordHintBanners(false);
        }

        private void IntervalBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !Regex.IsMatch(e.Text, "^[0-9]+$");
        }

        private void IntervalBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (!e.DataObject.GetDataPresent(typeof(string)))
            {
                e.CancelCommand();
                return;
            }
            var text = e.DataObject.GetData(typeof(string)) as string;
            if (string.IsNullOrEmpty(text) || !Regex.IsMatch(text, "^[0-9]+$"))
                e.CancelCommand();
        }

        private void IntervalBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ClearFieldInvalid(IntervalBox);
            if (_loadingUi || _settings == null) return;
            _intervalDebounceTimer.Stop();
            _intervalDebounceTimer.Start();
        }

        private void IntervalDebounceTimer_Tick(object sender, EventArgs e)
        {
            _intervalDebounceTimer.Stop();
            ApplyIntervalFromBox(false);
        }

        private void IntervalBox_LostFocus(object sender, RoutedEventArgs e)
        {
            ApplyIntervalFromBox(true);
        }

        private void IntervalBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            ApplyIntervalFromBox(true);
        }

        private void ApplyIntervalFromBox(bool coerceIncomplete)
        {
            if (_loadingUi || _settings == null) return;
            _intervalDebounceTimer.Stop();
            var raw = IntervalBox.Text != null ? IntervalBox.Text.Trim() : "";
            int hours;
            var invalid = !int.TryParse(raw, out hours) || hours < 1 || hours > 168;
            if (invalid)
            {
                if (!coerceIncomplete) return;
                MarkFieldInvalid(IntervalBox);
                try { Activate(); } catch { }
                if (IntervalBox != null) IntervalBox.Focus();
                MessageBox.Show(this, UiStrings.T("InvalidInterval"), "KAI - AOS Renew",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                if (!int.TryParse(raw, out hours) || hours < 1)
                    hours = 1;
                if (hours > 168) hours = 168;
            }
            else
                ClearFieldInvalid(IntervalBox);
            var normalized = hours.ToString();
            if (!string.Equals(IntervalBox.Text, normalized, StringComparison.Ordinal))
            {
                var prevLoading = _loadingUi;
                _loadingUi = true;
                try { IntervalBox.Text = normalized; }
                finally { _loadingUi = prevLoading; }
            }
            if (hours == _settings.IntervalHours) return;
            _settings.IntervalHours = hours;
            _settings.Save();
            ScheduleNextServiceCheck();
            if (!_timerActive)
            {
                StartTimer(true);
                return;
            }
            _retrySchedule = false;
            ScheduleNext(true);
            AppendLog(string.Format(UiStrings.T("LogIntervalUpdated"), hours), true);
            UpdateNextLabel();
            UpdateTrayTooltip();
        }

        private void StartTimer(bool announce, bool allowMinimize = true)
        {
            SaveFromUi(false);
            if (!(_retrySchedule && _settings.ConsecutiveFailures > 0))
            {
                _retrySchedule = false;
                ScheduleNext(true);
            }
            _timerActive = true;
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
            // Keep _timer running for UI refresh + BMW service checks
            if (!_timer.IsEnabled)
                _timer.Start();
            SetTimerStatus(false);
            NextRunText.Text = "";
            UpdateTrayTooltip();
            AppendLog(UiStrings.T("TimerStopped"));
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

        private void RefreshStatusBadge()
        {
            if (!_timerActive)
            {
                SetTimerStatus(false);
                return;
            }
            if (_hasFailureAlert || _settings.ConsecutiveFailures > 0)
            {
                StatusText.Text = UiStrings.T("ErrorRetry");
                StatusText.Foreground = (Brush)FindResource("StatusErrorBrush");
                StatusDot.Fill = (Brush)FindResource("StatusErrorBrush");
                _footerStatusMode = "error";
            }
            else
            {
                StatusText.Text = UiStrings.T("TimerActive");
                StatusText.Foreground = (Brush)FindResource("StatusOkBrush");
                StatusDot.Fill = (Brush)FindResource("StatusOkBrush");
                _footerStatusMode = "ok";
            }
            QueueFooterPublish();
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new WinForms.FolderBrowserDialog())
            {
                dlg.Description = UiStrings.T("SelectConfigFolder");
                dlg.ShowNewFolderButton = false;
                var current = ConfigPathBox.Text.Trim();
                if (!string.IsNullOrEmpty(current) && Directory.Exists(current))
                    dlg.SelectedPath = current;
                if (dlg.ShowDialog() == WinForms.DialogResult.OK)
                {
                    ConfigPathBox.Text = dlg.SelectedPath;
                    ValidateConfigPath(dlg.SelectedPath, true);
                }
            }
        }

        private void ConfigPath_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_loadingUi) return;
            ValidateConfigPath(ConfigPathBox.Text.Trim(), true);
        }

        private static bool IsValidConfigPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return false;
            return File.Exists(Path.Combine(path, "KAI_Properties.cfg"));
        }

        private bool ValidateConfigPath(string path, bool showMessage)
        {
            if (!IsValidConfigPath(path))
            {
                string msg;
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                    msg = string.Format(UiStrings.T("FolderMissing"), path);
                else
                    msg = string.Format(UiStrings.T("PropsMissing"), path);
                AppendLog(msg);
                if (showMessage)
                    MessageBox.Show(this, msg, "KAI - AOS Renew", MessageBoxButton.OK, MessageBoxImage.Warning);
                var revert = _lastValidConfigPath;
                if (string.IsNullOrEmpty(revert) && _settings != null)
                    revert = _settings.ConfigPath;
                ConfigPathBox.Text = revert ?? "";
                return false;
            }
            _lastValidConfigPath = path;
            if (_settings != null &&
                !string.Equals(_settings.ConfigPath, path, StringComparison.OrdinalIgnoreCase))
            {
                _settings.ConfigPath = path;
                _settings.Save();
            }
            AppendLog(string.Format(UiStrings.T("PropsOk"), path), true);
            return true;
        }

        private void MarkFieldInvalid(Control c)
        {
            if (c == null) return;
            var brush = TryFindResource("StatusErrorBrush") as Brush;
            if (brush == null) brush = Brushes.IndianRed;
            c.BorderBrush = brush;
            c.BorderThickness = new Thickness(2);
        }

        private void ClearFieldInvalid(Control c)
        {
            if (c == null) return;
            c.ClearValue(Control.BorderBrushProperty);
            c.ClearValue(Control.BorderThicknessProperty);
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

        private bool ValidateInterval(out int hours, bool showUi)
        {
            hours = 1;
            var raw = IntervalBox != null ? (IntervalBox.Text ?? "").Trim() : "";
            if (!int.TryParse(raw, out hours) || hours < 1 || hours > 168)
            {
                hours = 1;
                return ShowAosFieldError("InvalidInterval", IntervalBox, showUi);
            }
            ClearFieldInvalid(IntervalBox);
            return true;
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
        }

        private void AosPassword_Changed(object sender, RoutedEventArgs e)
        {
            ClearFieldInvalid(PasswordBox);
            ClearFieldInvalid(PasswordPlainBox);
        }

        private void AosPasswordPlain_TextChanged(object sender, TextChangedEventArgs e)
        {
            ClearFieldInvalid(PasswordBox);
            ClearFieldInvalid(PasswordPlainBox);
        }

        private void SetActionEnabled(Button btn, bool enabled, string disabledTipKey, string enabledTipKey = null)
        {
            if (btn == null) return;
            btn.IsEnabled = enabled;
            if (!enabled && !string.IsNullOrEmpty(disabledTipKey))
                btn.ToolTip = UiStrings.T(disabledTipKey);
            else if (!string.IsNullOrEmpty(enabledTipKey))
                btn.ToolTip = UiStrings.T(enabledTipKey);
            else if (enabled)
                btn.ToolTip = null;
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
            UpdateNextLabel();
            UpdateTrayTooltip();
        }

        private void ScheduleRetryInOneHour()
        {
            _retrySchedule = true;
            _nextDueUtc = DateTime.UtcNow.AddHours(1);
            UpdateNextLabel();
            UpdateTrayTooltip();
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
                RunAutoHealthWithLimitedRegister();
                ScheduleNext(true);
                return;
            }
            if (_timerActive && now >= _nextHealthUtc)
            {
                _nextHealthUtc = now.AddHours(1);
                RunAutoHealthWithLimitedRegister();
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

        /// <summary>Silent browser metadata refresh (no dialog). Throttled unless force.</summary>
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
            AppendLog(string.Format(UiStrings.T("AuthCooldownStarted"), until), LogLevel.Warning);
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
                RefreshAuthCooldownBanner();
                return;
            }
            _settings.AuthRegisterBlocked = blocked;
            try { _settings.Save(); }
            catch { }
            RefreshAuthCooldownBanner();
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
                    AuthCooldownStatusLine.Foreground = new SolidColorBrush(Color.FromRgb(0xB2, 0x6A, 0x00));
                }
                else
                {
                    AuthCooldownStatusLine.Text = UiStrings.T("AuthCooldownStatusInactive");
                    AuthCooldownStatusLine.Foreground = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
                }
            }
            if (AuthRegisterBlockedStatusLine != null)
            {
                if (blocked)
                {
                    AuthRegisterBlockedStatusLine.Text = UiStrings.T("AuthRegisterBlockedStatusActive");
                    AuthRegisterBlockedStatusLine.Foreground = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
                }
                else
                {
                    AuthRegisterBlockedStatusLine.Text = UiStrings.T("AuthRegisterBlockedStatusInactive");
                    AuthRegisterBlockedStatusLine.Foreground = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
                }
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

        private void UpdateAuthGuardsFooter(bool cooldown, bool blocked, string untilLocal)
        {
            if (AuthGuardsFooterText == null) return;
            AuthGuardsFooterText.Inlines.Clear();

            var warn = new SolidColorBrush(Color.FromRgb(0xB2, 0x6A, 0x00));
            var err = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
            var ok = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
            if (warn.CanFreeze) warn.Freeze();
            if (err.CanFreeze) err.Freeze();
            if (ok.CanFreeze) ok.Freeze();

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

            var muted = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
            if (muted.CanFreeze) muted.Freeze();
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

        private void MaybeTrayHint(string text, ref DateTime lastUtc, TimeSpan minInterval)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (lastUtc != DateTime.MinValue && (DateTime.UtcNow - lastUtc) < minInterval)
                return;
            lastUtc = DateTime.UtcNow;
            ApplyHintTrayIcon(true);
            ShowBalloon(WinForms.ToolTipIcon.Warning, TruncateBalloon(text));
        }

        private void ApplyHintTrayIcon(bool on)
        {
            if (_tray == null) return;
            _hintTrayIcon = on;
            RefreshTrayIcon();
            UpdateTrayTooltip();
        }

        /// <summary>
        /// Resets the "change password every N hours" clock (banner + tray nag).
        /// </summary>
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
                    foreach (var line in File.ReadLines(paths[p]))
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

        private void SeedRegisterActivityFromRenewLog()
        {
            try
            {
                var path = AppSettings.LogPath;
                if (!File.Exists(path)) return;
                var lines = new List<string>();
                foreach (var line in File.ReadLines(path))
                {
                    lines.Add(line);
                    if (lines.Count > 300)
                        lines.RemoveAt(0);
                }
                var existing = new HashSet<string>(StringComparer.Ordinal);
                for (var i = 0; i < _logEntries.Count; i++)
                {
                    var e = _logEntries[i];
                    if (e == null) continue;
                    existing.Add(e.Time.ToString("yyyy-MM-dd HH:mm:ss") + "\t" + (e.Text ?? ""));
                }
                var seeded = new List<LogEntry>();
                for (var i = 0; i < lines.Count; i++)
                {
                    var line = lines[i];
                    if (line == null || line.Length < 21) continue;
                    DateTime local;
                    if (!DateTime.TryParseExact(line.Substring(0, 19), "yyyy-MM-dd HH:mm:ss",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out local))
                        continue;
                    var text = line.Substring(19).TrimStart();
                    if (!IsRegisterActivitySeedText(text)) continue;
                    var key = local.ToString("yyyy-MM-dd HH:mm:ss") + "\t" + text;
                    if (existing.Contains(key)) continue;
                    existing.Add(key);
                    seeded.Add(new LogEntry
                    {
                        Time = local,
                        Level = InferRegisterActivityLevel(text),
                        Text = text
                    });
                }
                if (seeded.Count == 0) return;
                _logEntries.InsertRange(0, seeded);
                _logEntries.Sort((a, b) => a.Time.CompareTo(b.Time));
                if (_logEntries.Count > 2000)
                    _logEntries.RemoveRange(0, _logEntries.Count - 2000);
                RefreshLogView();
            }
            catch { }
        }

        private static bool IsRegisterActivitySeedText(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return text.IndexOf("Registration OK", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("Register failed", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("Register rejected", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("Register returned empty", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("Force Register", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("forcing Register", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static LogLevel InferRegisterActivityLevel(string text)
        {
            if (text.IndexOf("Registration OK", StringComparison.OrdinalIgnoreCase) >= 0)
                return LogLevel.Success;
            if (text.IndexOf("Register failed", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("Register rejected", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("Register returned empty", StringComparison.OrdinalIgnoreCase) >= 0)
                return LogLevel.Error;
            return LogLevel.Info;
        }

        private static bool TryClassifyRegisterHistory(string text, out bool ok)
        {
            ok = false;
            if (string.IsNullOrEmpty(text)) return false;
            var t = text.Trim();
            if (t.StartsWith("OK:", StringComparison.OrdinalIgnoreCase))
                t = t.Substring(3).TrimStart();
            else if (t.StartsWith("FAIL:", StringComparison.OrdinalIgnoreCase))
                t = t.Substring(5).TrimStart();
            if (t.IndexOf("Registration OK", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                ok = true;
                return true;
            }
            if (t.IndexOf("Register failed", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("Register rejected", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("Register returned empty", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("KAI_Properties.cfg was not created", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("KAI_Properties.cfg invalid", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                ok = false;
                return true;
            }
            return false;
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
                    foreach (var line in File.ReadLines(paths[p]))
                    {
                        if (line == null || line.Length < 21) continue;
                        DateTime local;
                        if (!DateTime.TryParseExact(line.Substring(0, 19), "yyyy-MM-dd HH:mm:ss",
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out local))
                            continue;
                        var text = line.Substring(19).TrimStart();
                        bool ok;
                        if (!TryClassifyRegisterHistory(text, out ok)) continue;
                        var key = local.ToString("yyyy-MM-dd HH:mm:ss") + "\t" + (ok ? "1" : "0") + "\t" + text;
                        if (!seen.Add(key)) continue;
                        _registerHistory.Add(new RegisterHistoryRow
                        {
                            At = local,
                            Ok = ok,
                            Text = text,
                            AgeText = "",
                            ResultText = ok ? UiStrings.T("RegisterResultOk") : UiStrings.T("RegisterResultFail")
                        });
                    }
                }
                _registerHistory.Sort((a, b) => a.At.CompareTo(b.At));
                while (_registerHistory.Count > RegisterHistoryMax)
                    _registerHistory.RemoveAt(0);
                RefreshRegisterAges();
                ApplyRegisterFilter();
            }
            catch { }
        }

        private void MaybeAppendRegisterHistory(string text, DateTime at)
        {
            bool ok;
            if (!TryClassifyRegisterHistory(text, out ok)) return;
            var clean = text ?? "";
            if (clean.StartsWith("OK:", StringComparison.OrdinalIgnoreCase))
                clean = clean.Substring(3).TrimStart();
            else if (clean.StartsWith("FAIL:", StringComparison.OrdinalIgnoreCase))
                clean = clean.Substring(5).TrimStart();
            var key = at.ToString("yyyy-MM-dd HH:mm:ss") + "\t" + (ok ? "1" : "0") + "\t" + clean;
            for (var i = 0; i < _registerHistory.Count; i++)
            {
                var r = _registerHistory[i];
                if (r == null) continue;
                var existing = r.At.ToString("yyyy-MM-dd HH:mm:ss") + "\t" + (r.Ok ? "1" : "0") + "\t" + (r.Text ?? "");
                if (string.Equals(existing, key, StringComparison.Ordinal)) return;
            }
            var span = DateTime.Now - at;
            if (span < TimeSpan.Zero) span = TimeSpan.Zero;
            _registerHistory.Add(new RegisterHistoryRow
            {
                At = at,
                Ok = ok,
                Text = clean,
                AgeText = string.Format(UiStrings.T("TimeAgo"), FormatAge(span)),
                ResultText = ok ? UiStrings.T("RegisterResultOk") : UiStrings.T("RegisterResultFail")
            });
            while (_registerHistory.Count > RegisterHistoryMax)
                _registerHistory.RemoveAt(0);
            ApplyRegisterFilter();
            if (TabRegister != null && TabRegister.IsSelected)
                ScrollRegisterHistoryToEnd();
            BroadcastRegisterSync();
        }

        private void RefreshRegisterResultTexts()
        {
            for (var i = 0; i < _registerHistory.Count; i++)
            {
                var r = _registerHistory[i];
                if (r == null) continue;
                r.ResultText = r.Ok ? UiStrings.T("RegisterResultOk") : UiStrings.T("RegisterResultFail");
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
            IEnumerable<RegisterHistoryRow> src = _registerHistory;
            if (string.Equals(_registerFilter, "ok", StringComparison.OrdinalIgnoreCase))
            {
                var list = new List<RegisterHistoryRow>();
                for (var i = 0; i < _registerHistory.Count; i++)
                {
                    var r = _registerHistory[i];
                    if (r != null && r.Ok) list.Add(r);
                }
                src = list;
            }
            else if (string.Equals(_registerFilter, "fail", StringComparison.OrdinalIgnoreCase))
            {
                var list = new List<RegisterHistoryRow>();
                for (var i = 0; i < _registerHistory.Count; i++)
                {
                    var r = _registerHistory[i];
                    if (r != null && !r.Ok) list.Add(r);
                }
                src = list;
            }
            RegisterHistoryList.ItemsSource = null;
            RegisterHistoryList.ItemsSource = src;
        }

        private static bool IsRegistrationFilterMatch(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return text.IndexOf("Registration", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("Register", StringComparison.OrdinalIgnoreCase) >= 0;
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

        private static string FormatAge(TimeSpan span)
        {
            var days = (int)span.TotalDays;
            var hours = span.Hours;
            var minutes = span.Minutes;
            if (days > 0)
                return days + "d " + hours + "h " + minutes + "m";
            if (hours > 0)
                return hours + "h " + minutes + "m";
            if (minutes > 0)
                return minutes + "m";
            return "<1m";
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

        private static string FormatTtl(int seconds)
        {
            if (seconds >= 86400)
            {
                var d = seconds / 86400.0;
                if (Math.Abs(d - Math.Round(d)) < 0.05)
                    return ((int)Math.Round(d)) + "d";
                return d.ToString("0.#") + "d";
            }
            if (seconds >= 3600)
            {
                var h = seconds / 3600.0;
                if (Math.Abs(h - Math.Round(h)) < 0.05)
                    return ((int)Math.Round(h)) + "h";
                return h.ToString("0.#") + "h";
            }
            if (seconds >= 60)
                return (seconds / 60) + "m";
            return seconds + "s";
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            StartTimerBtn.IsEnabled = !busy;
            StopTimerBtn.IsEnabled = !busy;
            CheckTokenBtn.IsEnabled = !busy;
            // Check browser / Open BMW AOS / Paste & Save stay usable (queue if busy).
            RefreshAosActionButtons();
            if (KaiRefreshBtn != null) KaiRefreshBtn.IsEnabled = !busy;
            if (KaiFixServicesBtn != null) KaiFixServicesBtn.IsEnabled = !busy;
            if (KaiStopServicesBtn != null) KaiStopServicesBtn.IsEnabled = !busy;
        }

        private async void RunNowAsync(
            bool startTimerOnSuccess = false,
            bool forceRegister = false,
            string passwordOverride = null,
            bool ignoreAuthGuards = false,
            bool lockRegisterAfter = false)
        {
            if (ignoreAuthGuards)
            {
                ClearAuthCooldown();
                // 403 one-shot keeps blocked until after this run; Force Register clears it.
                if (!lockRegisterAfter)
                    SetAuthRegisterBlocked(false);
                forceRegister = true;
            }

            var cooldown = ignoreAuthGuards ? false : IsAuthCooldownActive();
            if (cooldown && forceRegister)
            {
                AppendLog(UiStrings.T("AuthCooldownRegisterDeferred"), LogLevel.Warning);
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
                        AppendLog(UiStrings.T("AuthCooldownRegisterDeferred"), LogLevel.Warning);
                        return;
                    }
                    _pendingForcePassword = passwordOverride ?? (_settings != null ? _settings.GetPassword() : null);
                    _pendingForceRegister = true;
                    _pendingIgnoreAuthGuards = ignoreAuthGuards;
                    _pendingLockRegisterAfter = lockRegisterAfter || _pendingLockRegisterAfter;
                    _pendingStartTimerOnSuccess = startTimerOnSuccess || _pendingStartTimerOnSuccess;
                    AppendLog(ignoreAuthGuards
                        ? (lockRegisterAfter
                            ? "Renew busy — queued 403 Register (then block)."
                            : "Renew busy — queued Force Register (bypass guards).")
                        : "Renew busy — queued force Register with updated password.", LogLevel.Info);
                }
                return;
            }
            if (passwordOverride != null)
                SetPasswordUi(passwordOverride);
            if (!ValidateAosCredentials(true))
            {
                if (lockRegisterAfter)
                {
                    _pending403RetryVin = null;
                    _pending403RetryReaderId = null;
                    SetAuthRegisterBlocked(true);
                }
                return;
            }
            SetBusy(true);
            SaveFromUi(false, passwordOverride);
            // Re-check after save (settings object may have changed / block cleared on password change).
            if (ignoreAuthGuards)
            {
                ClearAuthCooldown();
                if (!lockRegisterAfter)
                    SetAuthRegisterBlocked(false);
            }
            cooldown = ignoreAuthGuards ? false : IsAuthCooldownActive();
            var blocked = ignoreAuthGuards ? false : IsAuthRegisterBlocked();
            var allowRegister = ignoreAuthGuards || (!cooldown && !blocked);
            var settings = _settings;
            var force = forceRegister && allowRegister;
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
                    AppendLog(UiStrings.T("AuthCooldownHealthOnly"), LogLevel.Warning);
                else
                    AppendLog(UiStrings.T("AuthRegisterBlockedHealthOnly"), LogLevel.Warning);
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
            if (allowRegister && !string.IsNullOrEmpty(password))
                AppendLog("Register password length=" + password.Length, LogLevel.Info, true);

            RenewResult result = null;
            try
            {
                var allow = allowRegister;
                var forceCapture = force;
                var passCapture = password;
                result = await Task.Run(() => RenewEngine.Run(settings, forceCapture, passCapture, allow));
            }
            catch (Exception ex)
            {
                result = new RenewResult { Ok = false, Message = ex.Message };
            }

            RenewEngine.Log(result.Message);
            if (result.Ok)
            {
                if (result.RegisterAuthRejected)
                {
                    AppendLog(result.Message, LogLevel.Error);
                    ShowBalloon(WinForms.ToolTipIcon.Warning, TruncateBalloon(result.Message));
                }
                else
                {
                    AppendLog("OK: " + result.Message, LogLevel.Success);
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
                ScheduleNext(true);
                UpdateTokenAgeText();
                SetTokenFooter(true);
                if (startTimerOnSuccess && !_timerActive)
                    StartTimer(true, false);
            }
            else
            {
                AppendLog("FAIL: " + result.Message, LogLevel.Error);
                MaybeAppendBrowserPasswordHint(result.Message);
                _settings.ConsecutiveFailures = _settings.ConsecutiveFailures + 1;
                _settings.LastMessage = result.Message;
                _settings.Save();
                ApplyFailureAlert(result.Message);
                ShowBalloon(WinForms.ToolTipIcon.Error, TruncateBalloon(result.Message));
                var msg = result.Message ?? "";
                var skipBlockMsg = msg.IndexOf("Auth cooldown", StringComparison.OrdinalIgnoreCase) >= 0
                    || msg.IndexOf("Auth register blocked", StringComparison.OrdinalIgnoreCase) >= 0
                    || msg.IndexOf("Register blocked", StringComparison.OrdinalIgnoreCase) >= 0
                    || msg.IndexOf("Health failed (no Register)", StringComparison.OrdinalIgnoreCase) >= 0
                    || msg.IndexOf("Register skipped", StringComparison.OrdinalIgnoreCase) >= 0;
                // Any Register attempt that failed → block until password changes (closes unrecognized-auth gap).
                if (result.RegisterAttempted && !skipBlockMsg)
                    SetAuthRegisterBlocked(true);
                if (LooksLikeAuthFailure(msg) && !skipBlockMsg)
                {
                    SetAuthRegisterBlocked(true);
                    BeginAuthCooldown();
                }
                // Retry schedule stays Health-only (timer never Registers).
                ScheduleRetryInOneHour();
                SetTokenFooter(false);
            }
            if (lockRegisterAfter)
            {
                SetAuthRegisterBlocked(true);
                var retryVin = _pending403RetryVin;
                var retryReader = _pending403RetryReaderId;
                _pending403RetryVin = null;
                _pending403RetryReaderId = null;
                if (!string.IsNullOrEmpty(retryVin))
                    Dispatcher.BeginInvoke(new Action(() => RetryBmwUploadAfter403(retryVin, retryReader)));
            }
            RefreshPasswordHintBanners(false);
            RefreshStatusBadge();
            SetBusy(false);
            DrainPendingForceRegister();
        }

        private void DrainPendingForceRegister()
        {
            if (string.IsNullOrEmpty(_pendingForcePassword) && !_pendingForceRegister)
                return;
            var pass = _pendingForcePassword;
            var startTimer = _pendingStartTimerOnSuccess;
            var ignoreGuards = _pendingIgnoreAuthGuards;
            var lockAfter = _pendingLockRegisterAfter;
            _pendingForcePassword = null;
            _pendingForceRegister = false;
            _pendingIgnoreAuthGuards = false;
            _pendingLockRegisterAfter = false;
            _pendingStartTimerOnSuccess = false;
            if (string.IsNullOrEmpty(pass) && !ignoreGuards)
                return;
            if (!ignoreGuards && IsAuthCooldownActive())
            {
                AppendLog(UiStrings.T("AuthCooldownRegisterDeferred"), LogLevel.Warning);
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
            RunNowAsync(startTimer, true, pass, ignoreGuards, lockAfter);
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
                AppendLog(hint, LogLevel.Warning);
                ShowBalloon(WinForms.ToolTipIcon.Warning, TruncateBalloon(hint));
            }
            catch { }
        }

        private const double AutoRegisterMinHours = 12;

        /// <summary>
        /// Auto path: Health first; if Health fails, Register at most once per 12 hours
        /// (unless AuthRegisterBlocked). Manual Save is unaffected.
        /// </summary>
        private async void RunAutoHealthWithLimitedRegister()
        {
            if (_busy) return;
            if (!ValidateAosCredentials(false)) return;
            SetBusy(true);
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
                AppendLog("OK: " + result.Message, LogLevel.Verbose, true);
                if (settings.LastTokenExpiresInSeconds > 0)
                {
                    _settings.LastTokenExpiresInSeconds = settings.LastTokenExpiresInSeconds;
                    _settings.Save();
                }
                UpdateTokenAgeText();
                SetTokenFooter(true);
                if (_timerActive)
                    _nextHealthUtc = DateTime.UtcNow.AddHours(1);
                ClearFailureAlert();
                RefreshStatusBadge();
                SetBusy(false);
                return;
            }

            AppendLog("FAIL: " + result.Message, LogLevel.Error);
            if (LooksLikeAuthFailure(result.Message))
                TrySilentBrowserPassRefresh(true);
            ApplyFailureAlert(result.Message);
            SetTokenFooter(false);

            if (IsAuthRegisterBlocked())
            {
                AppendLog(UiStrings.T("AuthRegisterBlockedHealthOnly"), LogLevel.Warning);
                RefreshStatusBadge();
                SetBusy(false);
                return;
            }

            if (_settings.LastAutoRegisterUtc.HasValue
                && (DateTime.UtcNow - _settings.LastAutoRegisterUtc.Value).TotalHours < AutoRegisterMinHours)
            {
                var next = _settings.LastAutoRegisterUtc.Value.AddHours(AutoRegisterMinHours).ToLocalTime().ToString("g");
                AppendLog("Auto Register skipped (max 1 per 12h). Next allowed after " + next + ".", LogLevel.Warning);
                RefreshStatusBadge();
                SetBusy(false);
                return;
            }

            _settings.LastAutoRegisterUtc = DateTime.UtcNow;
            try { _settings.Save(); }
            catch { }
            AppendLog("Health failed — auto Register (max 1 per 12h)...", LogLevel.Warning);
            RefreshStatusBadge();
            SetBusy(false);
            // forceRegister=false: engine Registers because Health already failed.
            RunNowAsync(false, false);
        }

        private async void RunHealthOnlyAsync(bool interactive = true)
        {
            if (_busy) return;
            if (!ValidateAosCredentials(interactive)) return;
            SetBusy(true);
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
                if (interactive)
                {
                    AppendLog("OK: " + result.Message, LogLevel.Success);
                    ShowBalloon(WinForms.ToolTipIcon.Info, TruncateBalloon(result.Message));
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
                SetTokenFooter(true);
                if (_timerActive)
                    _nextHealthUtc = DateTime.UtcNow.AddHours(1);
            }
            else
            {
                AppendLog("FAIL: " + result.Message, LogLevel.Error);
                if (LooksLikeAuthFailure(result.Message))
                    TrySilentBrowserPassRefresh(true);
                ApplyFailureAlert(result.Message);
                ShowBalloon(WinForms.ToolTipIcon.Error, TruncateBalloon(result.Message));
                SetTokenFooter(false);
            }
            RefreshStatusBadge();
            SetBusy(false);
        }

        private void ApplyFailureAlert(string message)
        {
            _hasFailureAlert = true;
            RefreshTrayIcon();
            UpdateTrayTooltip();
            SetRenewAlert(true);
        }

        private void ClearFailureAlert()
        {
            _hasFailureAlert = false;
            RefreshTrayIcon();
            UpdateTrayTooltip();
            SetRenewAlert(_tokenFooterOk == false ||
                (_settings != null && _settings.ConsecutiveFailures > 0));
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
                AppendLog(string.Format(UiStrings.T("ImportOk"), dlg.FileName));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(UiStrings.T("ImportFail"), ex.Message), "KAI - AOS Renew",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void AppendLog(string line, bool verboseOnly = false)
        {
            AppendLog(line, null, verboseOnly);
        }

        private void AppendLog(string line, LogLevel level)
        {
            AppendLog(line, (LogLevel?)level, false);
        }

        private void AppendLog(string line, LogLevel level, bool verboseOnly)
        {
            AppendLog(line, (LogLevel?)level, verboseOnly);
        }

        private void AppendLog(string line, LogLevel? level, bool verboseOnly)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => AppendLog(line, level, verboseOnly)));
                return;
            }
            RenewEngine.Log(line);
            var resolved = level ?? InferLogLevel(line, verboseOnly);
            if (verboseOnly)
                resolved = LogLevel.Verbose;
            var entry = new LogEntry
            {
                Time = DateTime.Now,
                Level = resolved,
                Text = line ?? ""
            };
            _logEntries.Add(entry);
            if (_logEntries.Count > 2000)
                _logEntries.RemoveRange(0, _logEntries.Count - 2000);
            if (ShouldShowLogEntry(entry))
            {
                WriteLogLine(entry);
                if (LogBox != null && TabActivity != null && TabActivity.IsSelected)
                    LogBox.ScrollToEnd();
            }
            BroadcastActivityEntry(entry);
            MaybeAppendRegisterHistory(entry.Text, entry.Time);
        }

        private static LogLevel InferLogLevel(string line, bool verboseOnly)
        {
            if (verboseOnly) return LogLevel.Verbose;
            if (string.IsNullOrEmpty(line)) return LogLevel.Info;
            if (line.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase))
                return LogLevel.Error;
            if (line.StartsWith("OK:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Last success", StringComparison.OrdinalIgnoreCase))
                return LogLevel.Success;
            if (line.IndexOf("Balloon tip: Error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("Balloon-Tipp: Fehler", StringComparison.OrdinalIgnoreCase) >= 0)
                return LogLevel.Error;
            if (line.IndexOf("Balloon tip: Warning", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("Balloon-Tipp: Warnung", StringComparison.OrdinalIgnoreCase) >= 0)
                return LogLevel.Warning;
            if (line.IndexOf("does not exist", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("Missing ", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("invalid", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("lipsește", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("nu există", StringComparison.OrdinalIgnoreCase) >= 0)
                return LogLevel.Error;
            return LogLevel.Info;
        }

        private bool ShouldShowLogEntry(LogEntry entry)
        {
            if (_logFilter == "keyreader")
            {
                var t = entry.Text ?? "";
                return t.StartsWith("KeyReader:", StringComparison.OrdinalIgnoreCase);
            }
            if (_logFilter == "registration")
                return IsRegistrationFilterMatch(entry.Text);
            if (entry.Level == LogLevel.Verbose)
                return _settings != null && _settings.LogVerbose && _logFilter == "all";
            if (_logFilter == "all") return true;
            if (_logFilter == "info") return entry.Level == LogLevel.Info;
            if (_logFilter == "success") return entry.Level == LogLevel.Success;
            if (_logFilter == "warning") return entry.Level == LogLevel.Warning;
            if (_logFilter == "error") return entry.Level == LogLevel.Error;
            return true;
        }

        private void RefreshLogView()
        {
            if (LogBox == null) return;
            _uiOp = "RefreshLogView";
            var doc = new FlowDocument
            {
                PagePadding = new Thickness(6),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11
            };
            LogBox.Document = doc;
            var start = Math.Max(0, _logEntries.Count - MaxLogBlocks * 2);
            for (var i = start; i < _logEntries.Count; i++)
            {
                if (ShouldShowLogEntry(_logEntries[i]))
                    WriteLogLine(_logEntries[i]);
            }
            if (LogBox != null && TabActivity != null && TabActivity.IsSelected)
                LogBox.ScrollToEnd();
        }

        private const int MaxLogBlocks = 500;
        private const int ActivitySyncMax = 500;

        private static string LogLevelWireName(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Success: return "success";
                case LogLevel.Warning: return "warning";
                case LogLevel.Error: return "error";
                case LogLevel.Verbose: return "verbose";
                default: return "info";
            }
        }

        private void BroadcastActivityEntry(LogEntry entry)
        {
            if (entry == null || _settings == null || !_settings.NotifyServerEnabled) return;
            try
            {
                BroadcastNotify(new NotifyMessage
                {
                    Kind = "activity",
                    Level = LogLevelWireName(entry.Level),
                    Title = "KAI - AOS Renew",
                    Text = entry.Text ?? "",
                    At = entry.Time.ToString("yyyy-MM-ddTHH:mm:ss")
                });
            }
            catch { }
        }

        private void BroadcastActivityClear()
        {
            if (_settings == null || !_settings.NotifyServerEnabled) return;
            try
            {
                BroadcastNotify(new NotifyMessage
                {
                    Kind = "activityClear",
                    Level = "info",
                    Title = "KAI - AOS Renew",
                    Text = ""
                });
            }
            catch { }
        }

        private void SendActivitySyncTo(int clientId)
        {
            if (_settings == null || !_settings.NotifyServerEnabled) return;
            try
            {
                var list = new List<ActivityWireRow>();
                for (var i = 0; i < _logEntries.Count; i++)
                {
                    var e = _logEntries[i];
                    if (e == null) continue;
                    list.Add(new ActivityWireRow
                    {
                        At = e.Time,
                        Level = LogLevelWireName(e.Level),
                        Text = e.Text ?? ""
                    });
                }
                if (list.Count > ActivitySyncMax)
                    list = list.GetRange(list.Count - ActivitySyncMax, ActivitySyncMax);
                var payload = NotifyMessage.EncodeActivitySync(list);
                NotifyServer.Instance.SendTo(clientId, new NotifyMessage
                {
                    Kind = "activitySync",
                    Level = "info",
                    Title = "KAI - AOS Renew",
                    Text = payload ?? ""
                });
            }
            catch (Exception ex)
            {
                try { RenewEngine.Log("Notify activitySync: " + ex.Message); }
                catch { }
            }
        }

        private void SendRegisterSyncTo(int clientId)
        {
            if (_settings == null || !_settings.NotifyServerEnabled) return;
            try
            {
                var list = new List<RegisterWireRow>();
                for (var i = 0; i < _registerHistory.Count; i++)
                {
                    var r = _registerHistory[i];
                    if (r == null) continue;
                    list.Add(new RegisterWireRow
                    {
                        At = r.At,
                        Ok = r.Ok,
                        Text = r.Text ?? ""
                    });
                }
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

        private void BroadcastRegisterSync()
        {
            if (_settings == null || !_settings.NotifyServerEnabled) return;
            try
            {
                var list = new List<RegisterWireRow>();
                for (var i = 0; i < _registerHistory.Count; i++)
                {
                    var r = _registerHistory[i];
                    if (r == null) continue;
                    list.Add(new RegisterWireRow
                    {
                        At = r.At,
                        Ok = r.Ok,
                        Text = r.Text ?? ""
                    });
                }
                var payload = NotifyMessage.EncodeRegisterSync(list);
                BroadcastNotify(new NotifyMessage
                {
                    Kind = "registerSync",
                    Level = "info",
                    Title = "KAI - AOS Renew",
                    Text = payload ?? ""
                });
            }
            catch (Exception ex)
            {
                try { RenewEngine.Log("Notify registerSync broadcast: " + ex.Message); }
                catch { }
            }
        }

        private void WriteLogLine(LogEntry entry)
        {
            var para = new Paragraph
            {
                Margin = new Thickness(0),
                Padding = new Thickness(0)
            };
            var run = new Run(entry.Time.ToString("HH:mm:ss") + "  " + entry.Text)
            {
                Foreground = BrushForLogLevel(entry.Level)
            };
            para.Inlines.Add(run);
            var blocks = LogBox.Document.Blocks;
            blocks.Add(para);
            while (blocks.Count > MaxLogBlocks && blocks.FirstBlock != null)
                blocks.Remove(blocks.FirstBlock);
        }

        private Brush BrushForLogLevel(LogLevel level)
        {
            string key;
            switch (level)
            {
                case LogLevel.Success:
                    key = "StatusOkBrush";
                    break;
                case LogLevel.Warning:
                    key = "StatusWarnBrush";
                    break;
                case LogLevel.Error:
                    key = "StatusErrorBrush";
                    break;
                case LogLevel.Verbose:
                    key = "TextMutedBrush";
                    break;
                default:
                    key = "TextPrimaryBrush";
                    break;
            }
            try
            {
                var b = FindResource(key) as Brush;
                if (b != null) return b;
            }
            catch { }
            var night = ThemeManager.IsNight;
            Color c;
            switch (level)
            {
                case LogLevel.Success:
                    c = night ? Color.FromRgb(0x6B, 0xCF, 0x7F) : Color.FromRgb(0x1B, 0x7A, 0x3D);
                    break;
                case LogLevel.Warning:
                    c = night ? Color.FromRgb(0xE0, 0xB0, 0x40) : Color.FromRgb(0x9A, 0x6B, 0x00);
                    break;
                case LogLevel.Error:
                    c = night ? Color.FromRgb(0xF0, 0x70, 0x68) : Color.FromRgb(0xC0, 0x28, 0x20);
                    break;
                case LogLevel.Verbose:
                    c = night ? Color.FromRgb(0xA8, 0xA8, 0xA8) : Color.FromRgb(0x55, 0x55, 0x55);
                    break;
                default:
                    c = night ? Color.FromRgb(0xE8, 0xE8, 0xE8) : Color.FromRgb(0x1A, 0x1A, 0x1A);
                    break;
            }
            var brush = new SolidColorBrush(c);
            brush.Freeze();
            return brush;
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

        protected override void OnClosed(EventArgs e)
        {
            UnhookPowerEvents();
            if (_hwndSource != null)
            {
                _hwndSource.RemoveHook(WndProcHook);
                _hwndSource = null;
            }
            _timer.Stop();
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
                _tray = null;
            }
            if (_errorIcon != null)
            {
                _errorIcon.Dispose();
                _errorIcon = null;
            }
            base.OnClosed(e);
        }
    }
}
