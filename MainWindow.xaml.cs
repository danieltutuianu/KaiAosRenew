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

            public string TimeText
            {
                get { return Time.ToString("yyyy-MM-dd HH:mm:ss"); }
            }

            public string LevelText
            {
                get
                {
                    switch (Level)
                    {
                        case LogLevel.Success: return "success";
                        case LogLevel.Warning: return "warning";
                        case LogLevel.Error: return "error";
                        case LogLevel.Verbose: return "verbose";
                        default: return "info";
                    }
                }
            }
        }

        private sealed class RegisterHistoryRow
        {
            public DateTime At { get; set; }
            /// <summary>0=fail, 1=ok, 2=info.</summary>
            public int Kind { get; set; }
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
        private CancellationTokenSource _renewCts = new CancellationTokenSource();
        private readonly List<LogEntry> _logEntries = new List<LogEntry>();
        private readonly ObservableCollection<LogEntry> _activityRows = new ObservableCollection<LogEntry>();
        private readonly List<RegisterHistoryRow> _registerHistory = new List<RegisterHistoryRow>();
        private const int RegisterHistoryMax = 5000;
        /// <summary>When Register filter is All, only bind this many newest rows.</summary>
        private const int RegisterUnfilteredDisplayMax = 500;
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
        private bool _aosCredentialsDirty;
        private string _aosSavedEmail = "";
        private string _aosSavedPass = "";
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
        private string _activitySearchText = "";
        private string _registerSearchText = "";
        private DispatcherTimer _activityFilterScanTimer;
        private DispatcherTimer _activitySearchDebounceTimer;
        private DispatcherTimer _registerSearchDebounceTimer;
        private int _activityFilterScanGen;
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
        private readonly DispatcherTimer _clipboardWatchTimer = new DispatcherTimer();
        private DateTime _clipboardWatchUntilUtc = DateTime.MinValue;
        private string _clipboardWatchInitial;
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
        private bool _activityAlert;
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
        private bool _updateFeedBusy;
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
        private bool _aosBmw403Fail;
        private readonly HashSet<string> _bmwFailVinTipKeys = new HashSet<string>(StringComparer.Ordinal);
        private string _pending403RetryVin;
        private string _pending403RetryReaderId;
        private bool _keysRetryBusy;
        private bool _registerStrikeNotedThisRun;
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
            if (ActivityList != null)
            {
                ActivityList.ItemsSource = _activityRows;
                ActivityList.SizeChanged += ActivityList_SizeChanged;
                ActivityList.Loaded += ActivityList_SizeChanged;
            }
            if (RegisterHistoryList != null)
            {
                RegisterHistoryList.SizeChanged += RegisterHistoryList_SizeChanged;
                RegisterHistoryList.Loaded += RegisterHistoryList_SizeChanged;
            }

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
            SeedActivityHistoryFromLog();
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
            BmwHttpLog.UiAppend = text =>
            {
                try
                {
                    if (Dispatcher.CheckAccess())
                        AppendLog(text, LogLevel.Warning);
                    else
                        Dispatcher.BeginInvoke(new Action(() => AppendLog(text, LogLevel.Warning)));
                }
                catch { }
            };
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

            // Token expired or about to expire (<10 min left) while asleep.
            if (!renewDue && _settings.LastTokenExpiryUtc.HasValue
                && DateTime.UtcNow >= _settings.LastTokenExpiryUtc.Value.AddMinutes(-10))
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



        private void InitLogFilterCombo()
        {
            RefreshLogFilterComboItems();
            LogFilterCombo.SelectedValue = "all";
            _logFilter = "all";
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
            {
                ClearActivityAlert();
                ScrollActivityLogToEnd();
            }
            if (!_aboutVisible && TabRegister != null && TabRegister.IsSelected)
            {
                RefreshRegisterAges();
                ScrollRegisterHistoryToEnd();
            }
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
            if (ButtonsBorder != null)
                ButtonsBorder.Visibility = Visibility.Collapsed;
            if (!_aboutVisible && TabActivity != null && TabActivity.IsSelected)
            {
                ClearActivityAlert();
                ScrollActivityLogToEnd();
            }
            if (!_aboutVisible && TabRegister != null && TabRegister.IsSelected)
            {
                RefreshRegisterAges();
                ScrollRegisterHistoryToEnd();
            }
            UpdateKeysTabPolling();
        }

        private void ScrollActivityLogToEnd()
        {
            if (ActivityList == null || _activityRows.Count == 0) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (ActivityList == null || _activityRows.Count == 0) return;
                    ActivityList.UpdateLayout();
                    ActivityList.ScrollIntoView(_activityRows[_activityRows.Count - 1]);
                }
                catch { }
            }), DispatcherPriority.Loaded);
        }

        private void ActivityList_SizeChanged(object sender, EventArgs e)
        {
            ResizeActivityMessageColumn();
        }

        private void ResizeActivityMessageColumn()
        {
            if (ActivityList == null || ActivityColText == null) return;
            var width = ActivityList.ActualWidth;
            if (width <= 0) return;
            const double timeW = 120;
            const double levelW = 70;
            const double pad = 36;
            var msgW = width - timeW - levelW - pad;
            if (msgW < 120) msgW = 120;
            ActivityColText.Width = msgW;
        }




























        private static string TruncateForLog(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (s.Length <= max) return s;
            return s.Substring(0, max - 3) + "...";
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




        private void MinimizeOnClose_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingUi || _settings == null) return;
            _settings.MinimizeOnClose = MinimizeOnCloseCheck == null || MinimizeOnCloseCheck.IsChecked == true;
            try { _settings.Save(); }
            catch { }
            BroadcastServerSettings();
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

        private static void OpenPathInExplorer(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                path = path.Trim();
                if (File.Exists(path))
                {
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir))
                        OpenFolder(dir);
                    return;
                }
                OpenFolder(path);
            }
            catch { }
        }

        private void ConfigPathOpen_Click(object sender, RoutedEventArgs e)
        {
            OpenPathInExplorer(ConfigPathBox != null ? ConfigPathBox.Text : null);
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
                    || ContainsIgnoreCase(ev.ILevelCurrent, query)
                    || ContainsIgnoreCase(ev.KmText, query))
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





        /// <summary>
        /// On new BMW 403 Failed: one automatic Register, then Register blocked until password change.
        /// </summary>

        /// <summary>
        /// Info tip as soon as VIN is read from .bin/Sqlite — does not wait for BMW Server upload.
        /// Dedup is done in KeyActivityMonitor.NewVinReads.
        /// When KeysVinTipBmwFailOnly is on, skip local tip here (see MaybeShowVinBmwFailTips).
        /// </summary>

        /// <summary>
        /// When KeysVinTipBmwFailOnly: info tip after BMW upload fails for a recent VIN.
        /// </summary>
















        private const double KeysAlertMaxMinutes = 1;


        private void RefreshTrayIcon()
        {
            if (_tray == null) return;
            var alert = _hasFailureAlert || _hintTrayIcon || _keysAlert || _renewAlert;
            var outdated = _pendingUpdate != null;
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
            }
            EnsureAutoTimer(false);
            if (!_timer.IsEnabled)
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
            UpdateClearButtonsEnabled();
            UpdateAosLoginStatus();
        }

        private bool _updateCheckStarted;













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
            if (_keyMonitor != null)
                _keyMonitor.KeyDebugEnabled = _debugMode;
            if (KeysColBin != null)
                KeysColBin.Width = _debugMode ? 36 : 0;
            if (KeysColBinPad != null)
                KeysColBinPad.Width = _debugMode ? 18 : 0;
            ShowPendingUpdate(_pendingUpdate);
            RefreshTrayIcon();
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
                if (ClipboardPassBanner != null && ClipboardPassBanner.Visibility == Visibility.Visible)
                    StopClipboardPasswordWatch();
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

        private const int ClipboardWatchSeconds = 30;

        /// <summary>After opening the browser password manager: poll clipboard for ~30s for a copied password.</summary>
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
            var current = GetPasswordUi() ?? "";
            if (string.Equals(clip, current, StringComparison.Ordinal)) return false;
            return true;
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
                // Changed to something that is not a password (e.g. site copy) — only react to later changes.
                _clipboardWatchInitial = clip;
                return;
            }

            StopClipboardPasswordWatch();
            ShowFromTray();
            try { Activate(); }
            catch { }
            if (_aboutVisible)
                HideAboutPanel();
            if (TabRenew != null)
                TabRenew.IsSelected = true;
            TryShowClipboardPassBanner();
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
            var email = EmailBox != null ? EmailBox.Text.Trim() : "";
            if (!string.Equals(email, _aosSavedEmail ?? "", StringComparison.OrdinalIgnoreCase))
                return true;
            return !string.Equals((newPassword ?? "").Trim(), _aosSavedPass ?? "", StringComparison.Ordinal);
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
            var cooldownUntilUtc = "";
            if (authCooldownMode == "on" && _settings != null && _settings.AuthCooldownUntilUtc.HasValue)
                cooldownUntilUtc = _settings.AuthCooldownUntilUtc.Value.ToUniversalTime().ToString("o");
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
                authTip ?? "",
                cooldownUntilUtc
            };
            return string.Join("\u001f", parts);
        }










        private Dictionary<string, string> BuildServerSettingsMap()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (_settings == null) return map;
            map["language"] = _settings.Language ?? "en";
            map["themeMode"] = _settings.ThemeMode ?? "system";
            map["logVerbose"] = "1";
            map["startWithWindows"] = _settings.StartWithWindows ? "1" : "0";
            map["minimizeToTray"] = _settings.MinimizeToTray ? "1" : "0";
            map["minimizeOnClose"] = _settings.MinimizeOnClose ? "1" : "0";
            map["autoUpdate"] = _settings.AutoUpdate ? "1" : "0";
            map["keysRefreshSeconds"] = _settings.KeysRefreshSeconds.ToString();
            map["keysVinTipSeconds"] = _settings.KeysVinTipSeconds.ToString();
            map["keysStallMinutes"] = _settings.KeysStallMinutes.ToString();
            map["keysBalloon"] = _settings.KeysBalloonEnabled ? "1" : "0";
            map["keysVinBmwFailOnly"] = _settings.KeysVinTipBmwFailOnly ? "1" : "0";
            map["keysAutoCopyVin"] = _settings.KeysAutoCopyVin ? "1" : "0";
            map["keysStallAlert"] = _settings.KeysStallAlertEnabled ? "1" : "0";
            map["browserPassHint"] = _settings.BrowserPasswordHintOnAuthFail ? "1" : "0";
            map["useSystemProxy"] = _settings.UseSystemProxy ? "1" : "0";
            map["authCooldownMinutes"] = _settings.AuthCooldownMinutes.ToString();
            map["authRegisterBlockAfter"] = GetAuthRegisterBlockAfter().ToString();
            map["authRegisterBlocked"] = _settings.AuthRegisterBlocked ? "1" : "0";
            map["configPath"] = _settings.ConfigPath ?? "";
            map["keysUploaderLogDir"] = _settings.KeysUploaderLogDir ?? "";
            map["keysKaiLogDir"] = _settings.KeysKaiLogDir ?? "";
            map["keysSqlitePath"] = _settings.KeysSqlitePath ?? "";
            map["notifyEnabled"] = _settings.NotifyServerEnabled ? "1" : "0";
            map["notifyPort"] = _settings.NotifyServerPort.ToString();
            try { map["notifyIps"] = NotifyServer.GetLocalIpv4Summary() ?? ""; }
            catch { map["notifyIps"] = ""; }
            try
            {
                var paths = DesktopKeyReaderReg.ReadAbsolute();
                map["keyReaderDataBin"] = paths.DataBin ?? "";
                map["keyReaderLogPath"] = paths.LogPath ?? "";
                map["keyReaderEncPath"] = paths.EncPath ?? "";
                map["keyReaderLogLevel"] = paths.LogLevel.ToString();
            }
            catch
            {
                map["keyReaderDataBin"] = "";
                map["keyReaderLogPath"] = "";
                map["keyReaderEncPath"] = "";
                map["keyReaderLogLevel"] = "4";
            }
            return map;
        }







        private static bool MapFlag(Dictionary<string, string> map, string key, bool fallback)
        {
            string v;
            if (map == null || !map.TryGetValue(key, out v) || string.IsNullOrEmpty(v))
                return fallback;
            return v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static int MapInt(Dictionary<string, string> map, string key, int fallback, int min, int max)
        {
            string v;
            int n;
            if (map == null || !map.TryGetValue(key, out v) || !int.TryParse(v, out n))
                return fallback;
            if (n < min) n = min;
            if (n > max) n = max;
            return n;
        }

        private static string MapStr(Dictionary<string, string> map, string key, string fallback)
        {
            string v;
            if (map == null || !map.TryGetValue(key, out v) || v == null)
                return fallback;
            return v;
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





        private const long MaxFolderZipBytes = 25L * 1024 * 1024;






        private static void SizeTextBoxToContent(TextBox box)
        {
            if (box == null) return;
            var text = box.Text ?? "";
            if (text.Length == 0)
            {
                box.Width = 80;
                return;
            }
            try
            {
                var typeface = new Typeface(box.FontFamily, box.FontStyle, box.FontWeight, box.FontStretch);
                double pixelsPerDip = 1.0;
                try { pixelsPerDip = VisualTreeHelper.GetDpi(box).PixelsPerDip; }
                catch { }
                var ft = new FormattedText(
                    text,
                    CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    box.FontSize > 0 ? box.FontSize : 12,
                    Brushes.Black,
                    pixelsPerDip);
                var w = Math.Ceiling(ft.WidthIncludingTrailingWhitespace) + 24;
                if (w < 80) w = 80;
                box.Width = w;
            }
            catch
            {
                box.Width = Math.Max(80, text.Length * 8 + 24);
            }
        }

        private static void SizeComboToLongestLabel(ComboBox combo, IEnumerable<string> labels)
        {
            if (combo == null) return;
            const double minW = 160;
            const double chrome = 36; // padding + dropdown glyph
            double max = 0;
            try
            {
                var typeface = new Typeface(combo.FontFamily, combo.FontStyle, combo.FontWeight, combo.FontStretch);
                double pixelsPerDip = 1.0;
                try { pixelsPerDip = VisualTreeHelper.GetDpi(combo).PixelsPerDip; }
                catch { }
                var fontSize = combo.FontSize > 0 ? combo.FontSize : 12;
                if (labels != null)
                {
                    foreach (var label in labels)
                    {
                        var text = label ?? "";
                        if (text.Length == 0) continue;
                        var ft = new FormattedText(
                            text,
                            CultureInfo.CurrentUICulture,
                            FlowDirection.LeftToRight,
                            typeface,
                            fontSize,
                            Brushes.Black,
                            pixelsPerDip);
                        var w = ft.WidthIncludingTrailingWhitespace;
                        if (w > max) max = w;
                    }
                }
            }
            catch { }
            var width = Math.Ceiling(max) + chrome;
            if (width < minW) width = minW;
            combo.MinWidth = minW;
            combo.Width = width;
        }










        private sealed class NotifySendTarget
        {
            public int ClientId; // 0 = all
            public string Label { get; set; }
        }



        private void NotifyRefreshClients_Click(object sender, RoutedEventArgs e)
        {
            RefreshNotifyClientsCombo();
            RefreshNotifyServerIpDisplay();
            UpdateNotifyFirewallHint();
        }


        private void NotifyTestInfo_Click(object sender, RoutedEventArgs e)
        {
            SetNotifyTestResult(UiStrings.T("NotifyTestSending"), false);
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
            SetNotifyTestResult(UiStrings.T("NotifyTestSending"), false);
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
            SetNotifyTestResult(UiStrings.T("NotifyTestSending"), false);
            SendNotifyTest(new NotifyMessage
            {
                Kind = "error",
                Level = "error",
                Title = "KAI - AOS Renew",
                Text = "Test error tip (remote)",
                Seconds = 8
            }, "Error");
        }


        private void SetNotifyTestResult(string text, bool error)
        {
            if (NotifyTestResultText == null) return;
            NotifyTestResultText.Text = text ?? "";
            NotifyTestResultText.Visibility = string.IsNullOrEmpty(NotifyTestResultText.Text)
                ? Visibility.Collapsed
                : Visibility.Visible;
            Brush brush = null;
            try
            {
                brush = error
                    ? (FindResource("StatusErrorBrush") as Brush)
                    : (FindResource("TextMutedBrush") as Brush);
            }
            catch { }
            if (brush != null)
                NotifyTestResultText.Foreground = brush;
        }


        private Dictionary<string, string> BuildNotifyClientsMap(int youClientId)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            map["0"] = UiStrings.T("NotifyAllClients") + " (" + NotifyServer.Instance.ClientCount + ")";
            try
            {
                foreach (var c in NotifyServer.Instance.GetClients())
                {
                    var label = FormatNotifyClientLabel(c.Display ?? ("#" + c.Id), c.Version);
                    if (_clientsUpdating.Contains(c.Id))
                        label = string.Format(UiStrings.T("ClientUpdatingLabel"), label);
                    if (youClientId > 0 && c.Id == youClientId)
                        label = label + " " + UiStrings.T("NotifyClientYou");
                    map[c.Id.ToString()] = label;
                }
            }
            catch { }
            return map;
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

        /// <summary>Stop listeners/tray and free SingleInstance before Velopack restarts the process.</summary>
        internal void PrepareForRestart()
        {
            _exitRequested = true;
            CancelRenewInFlight();
            try { _keysRefreshTimer.Stop(); }
            catch { }
            try { _keysHighlightTimer.Stop(); }
            catch { }
            try { if (_timer != null) _timer.Stop(); }
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
            if (_tray != null)
            {
                try { _tray.Visible = false; }
                catch { }
                try { _tray.Dispose(); }
                catch { }
                _tray = null;
            }
            Program.ReleaseSingleInstanceForRestart();
        }

        private void ExitApp()
        {
            PrepareForRestart();
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
            BroadcastServerSettings();
        }


        private static string CheckboxLabel(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            return text.TrimEnd(' ', ':');
        }


        private void UseSystemProxy_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingUi || _settings == null) return;
            _settings.UseSystemProxy = UseSystemProxyCheck.IsChecked == true;
            _settings.Save();
            BroadcastServerSettings();
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


        /// <param name="forceBalloon">Startup: always balloon if problem.</param>
        /// <param name="forceBalloonOnProblem">Refresh: balloon if problem even if signature unchanged.</param>

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


        private void SetKaiServiceAlert(bool on)
        {
            _kaiServiceAlert = on;
            if (KaiTabAlertDot != null)
                KaiTabAlertDot.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            SyncAlertBlinkTimer();
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
            if (ActivityTabAlertDot != null)
                ActivityTabAlertDot.Opacity = _activityAlert ? opacity : 1.0;
            if (KaiFixServicesBtn != null)
                KaiFixServicesBtn.Opacity = (_kaiServiceAlert
                    && KaiFixServicesBtn.Visibility == Visibility.Visible
                    && KaiFixServicesBtn.IsEnabled)
                    ? opacity
                    : 1.0;
        }




        private void ApplyServicesBlinkVisuals()
        {
            if (ServicesFooterText == null) return;
            ServicesFooterText.Opacity = _servicesFail ? (_servicesBlinkOn ? 1.0 : 0.2) : 1.0;
        }






        private static string TrimLabelColon(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Trim().TrimEnd(':').Trim();
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



        private void MinimizeToTray_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingUi || _settings == null) return;
            _settings.MinimizeToTray = MinimizeToTrayCheck.IsChecked == true;
            _settings.Save();
            BroadcastServerSettings();
        }

        private void LogFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingUi) return;
            var code = LogFilterCombo.SelectedValue as string;
            if (string.IsNullOrEmpty(code)) return;
            _logFilter = code;
            RefreshLogView();
        }

        private void ActivitySearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loadingUi) return;
            EnsureActivitySearchDebounceTimer();
            _activitySearchDebounceTimer.Stop();
            _activitySearchDebounceTimer.Start();
        }

        private void RegisterSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loadingUi) return;
            EnsureRegisterSearchDebounceTimer();
            _registerSearchDebounceTimer.Stop();
            _registerSearchDebounceTimer.Start();
        }

        private void EnsureActivitySearchDebounceTimer()
        {
            if (_activitySearchDebounceTimer != null) return;
            _activitySearchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _activitySearchDebounceTimer.Tick += (s, ev) =>
            {
                _activitySearchDebounceTimer.Stop();
                _activitySearchText = ActivitySearchBox != null ? (ActivitySearchBox.Text ?? "").Trim() : "";
                RefreshLogView();
            };
        }

        private void EnsureRegisterSearchDebounceTimer()
        {
            if (_registerSearchDebounceTimer != null) return;
            _registerSearchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _registerSearchDebounceTimer.Tick += (s, ev) =>
            {
                _registerSearchDebounceTimer.Stop();
                _registerSearchText = RegisterSearchBox != null ? (RegisterSearchBox.Text ?? "").Trim() : "";
                ApplyRegisterFilter();
            };
        }

        private static bool MatchesLogSearch(string text, string needle)
        {
            return ActivityRegisterLogic.MatchesLogSearch(text, needle);
        }

        private void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            _logEntries.Clear();
            RefreshLogView();
            BroadcastActivityClear();
            UpdateClearButtonsEnabled();
        }

        private void ActivityList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (IsGridViewHeaderClick(e.OriginalSource as DependencyObject)) return;
            CopySelectedActivityText();
        }


        private bool CopySelectedActivityText()
        {
            var entry = ActivityList != null ? ActivityList.SelectedItem as LogEntry : null;
            if (entry == null || string.IsNullOrEmpty(entry.Text)) return false;
            TryCopyTextToClipboard(entry.Text, null);
            return true;
        }




        private static bool IsGridViewHeaderClick(DependencyObject source)
        {
            var d = source;
            while (d != null)
            {
                if (d is GridViewColumnHeader) return true;
                d = VisualTreeHelper.GetParent(d);
            }
            return false;
        }

        private void TryCopyTextToClipboard(string text, string feedback)
        {
            try
            {
                Clipboard.SetText(text ?? "");
                if (!string.IsNullOrEmpty(feedback))
                    AppendLog(feedback, LogLevel.Info);
            }
            catch { }
        }

        private void SaveActivityLog_Click(object sender, RoutedEventArgs e)
        {
            ExportLogForDay("activity", UiStrings.T("SaveLogTitleActivity"), CollectActivityLogForDay);
        }




        private string CollectActivityLogForDay(DateTime day)
        {
            var sb = new StringBuilder();
            var dayStart = day.Date;
            var dayEnd = dayStart.AddDays(1);
            var paths = new[] { AppSettings.LogPath + ".1", AppSettings.LogPath };
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var p = 0; p < paths.Length; p++)
            {
                if (!File.Exists(paths[p])) continue;
                foreach (var line in File.ReadLines(paths[p]))
                {
                    if (line == null || line.Length < 19) continue;
                    DateTime local;
                    if (!DateTime.TryParseExact(line.Substring(0, 19), "yyyy-MM-dd HH:mm:ss",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out local))
                        continue;
                    if (local < dayStart || local >= dayEnd) continue;
                    if (!seen.Add(line)) continue;
                    sb.AppendLine(line);
                }
            }
            if (sb.Length == 0 && _logEntries != null)
            {
                for (var i = 0; i < _logEntries.Count; i++)
                {
                    var e = _logEntries[i];
                    if (e == null) continue;
                    if (e.Time < dayStart || e.Time >= dayEnd) continue;
                    sb.Append(e.Time.ToString("yyyy-MM-dd HH:mm:ss"));
                    sb.Append(' ').AppendLine(e.Text ?? "");
                }
            }
            return sb.ToString();
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


        private bool CredentialsChangedInUi()
        {
            var email = EmailBox != null ? EmailBox.Text.Trim() : "";
            if (!string.Equals(email, _aosSavedEmail ?? "", StringComparison.OrdinalIgnoreCase))
                return true;
            var newPass = (PeekPasswordUi() ?? "").Trim();
            return !string.Equals(newPass, _aosSavedPass ?? "", StringComparison.Ordinal);
        }

        /// <summary>Read visible password without syncing the sibling box (avoids re-entrant dirty refresh).</summary>


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



        private void CheckToken_Click(object sender, RoutedEventArgs e)
        {
            RunHealthOnlyAsync();
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


        private void IntervalBox_LostFocus(object sender, RoutedEventArgs e)
        {
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
            if (_timerActive)
            {
                _retrySchedule = false;
                ScheduleNext(true);
                AppendLog(string.Format(UiStrings.T("LogIntervalUpdated"), hours), true);
                UpdateNextLabel();
                UpdateTrayTooltip();
            }
            else
                EnsureAutoTimer(true);
            BroadcastAosSync();
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








        /// <summary>Silent browser metadata refresh (no dialog). Throttled unless force.</summary>











        /// <summary>Increment strike; returns true if Register blocked should activate (at/over limit).</summary>








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









        /// <summary>Classify register-related log. kind: 0=fail, 1=ok, 2=info.</summary>







        private static bool IsRegistrationFilterMatch(string text)
        {
            return ActivityRegisterLogic.IsRegistrationFilterMatch(text);
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





        private const double AutoRegisterMinHours = 12;

        /// <summary>
        /// Auto path: Health first; if Health fails, Register at most once per 12 hours
        /// (unless AuthRegisterBlocked). Manual Save is unaffected.
        /// </summary>


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
            if (!IsActivityUiNoise(entry.Text))
            {
                _logEntries.Add(entry);
                // Cheap cap only — full age trim runs on seed / RefreshLogView.
                while (_logEntries.Count > MaxLogEntriesMemory)
                    _logEntries.RemoveAt(0);
                if (ShouldShowLogEntry(entry) && IsWithinLogRetention(entry.Time))
                {
                    _activityRows.Add(entry);
                    var rowCap = (string.IsNullOrEmpty(_logFilter) ||
                        string.Equals(_logFilter, "all", StringComparison.OrdinalIgnoreCase))
                        ? MaxLogBlocks
                        : ActivityFilteredMax;
                    while (_activityRows.Count > rowCap)
                        _activityRows.RemoveAt(0);
                    if (TabActivity != null && TabActivity.IsSelected)
                        ScrollActivityLogToEnd();
                }
                if (resolved == LogLevel.Error)
                    MaybeRaiseActivityAlert();
                BroadcastActivityEntry(entry);
            }
            MaybeAppendRegisterHistory(entry.Text, entry.Time);
            UpdateClearButtonsEnabled();
        }

        private void MaybeRaiseActivityAlert()
        {
            if (TabActivity != null && TabActivity.IsSelected && !_aboutVisible)
                return;
            _activityAlert = true;
            if (ActivityTabAlertDot != null)
                ActivityTabAlertDot.Visibility = Visibility.Visible;
            SyncAlertBlinkTimer();
        }

        private void ClearActivityAlert()
        {
            _activityAlert = false;
            if (ActivityTabAlertDot != null)
            {
                ActivityTabAlertDot.Visibility = Visibility.Collapsed;
                ActivityTabAlertDot.Opacity = 1.0;
            }
            SyncAlertBlinkTimer();
        }

        private static LogLevel InferLogLevel(string line, bool verboseOnly)
        {
            switch (ActivityRegisterLogic.InferLogLevel(line, verboseOnly))
            {
                case ActivityRegisterLogic.ActivityLogLevel.Success: return LogLevel.Success;
                case ActivityRegisterLogic.ActivityLogLevel.Warning: return LogLevel.Warning;
                case ActivityRegisterLogic.ActivityLogLevel.Error: return LogLevel.Error;
                case ActivityRegisterLogic.ActivityLogLevel.Verbose: return LogLevel.Verbose;
                default: return LogLevel.Info;
            }
        }

        private bool ShouldShowLogEntry(LogEntry entry)
        {
            return ShouldShowLogEntry(entry, _logFilter, _activitySearchText);
        }

        private bool ShouldShowLogEntry(LogEntry entry, string filter, string search)
        {
            if (entry == null || IsActivityUiNoise(entry.Text)) return false;
            if (!MatchesLogSearch(entry.Text, search)) return false;
            filter = string.IsNullOrEmpty(filter) ? "all" : filter.Trim().ToLowerInvariant();
            if (filter == "keyreader")
            {
                var t = entry.Text ?? "";
                return t.StartsWith("KeyReader:", StringComparison.OrdinalIgnoreCase);
            }
            if (filter == "registration")
                return IsRegistrationFilterMatch(entry.Text);
            if (entry.Level == LogLevel.Verbose)
                return _settings != null && _settings.LogVerbose && filter == "all";
            if (filter == "all") return true;
            if (filter == "info") return entry.Level == LogLevel.Info;
            if (filter == "success") return entry.Level == LogLevel.Success;
            if (filter == "warning") return entry.Level == LogLevel.Warning;
            if (filter == "error") return entry.Level == LogLevel.Error;
            return true;
        }

        private void RefreshLogView()
        {
            _uiOp = "RefreshLogView";
            TrimLogEntriesToRetention();
            if (string.IsNullOrEmpty(_logFilter) || string.Equals(_logFilter, "all", StringComparison.OrdinalIgnoreCase))
            {
                _activityFilterScanGen++;
                if (_activityFilterScanTimer != null) _activityFilterScanTimer.Stop();
                _activityRows.Clear();
                for (var i = 0; i < _logEntries.Count; i++)
                {
                    var e = _logEntries[i];
                    if (e == null || !IsWithinLogRetention(e.Time)) continue;
                    if (ShouldShowLogEntry(e))
                        _activityRows.Add(e);
                }
                while (_activityRows.Count > MaxLogBlocks)
                    _activityRows.RemoveAt(0);
                FinishRefreshLogViewHints();
            }
            else
            {
                EnsureActivityFilterScanTimer();
                _activityFilterScanTimer.Stop();
                _activityFilterScanTimer.Start();
            }
        }

        private void EnsureActivityFilterScanTimer()
        {
            if (_activityFilterScanTimer != null) return;
            _activityFilterScanTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _activityFilterScanTimer.Tick += ActivityFilterScanTimer_Tick;
        }

        private void ActivityFilterScanTimer_Tick(object sender, EventArgs e)
        {
            if (_activityFilterScanTimer != null) _activityFilterScanTimer.Stop();
            var filter = _logFilter;
            var search = _activitySearchText;
            var gen = ++_activityFilterScanGen;
            Task.Run(() =>
            {
                var rows = new List<LogEntry>();
                try { CollectFilteredActivityFromDisk(rows, ActivityFilteredMax, filter, search); }
                catch { }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (gen != _activityFilterScanGen) return;
                    if (!string.Equals(_logFilter, filter, StringComparison.OrdinalIgnoreCase)) return;
                    _activityRows.Clear();
                    for (var i = 0; i < rows.Count; i++)
                    {
                        if (rows[i] != null) _activityRows.Add(rows[i]);
                    }
                    FinishRefreshLogViewHints();
                }));
            });
        }

        private void FinishRefreshLogViewHints()
        {
            UpdateEmptyHint(ActivityEmptyHint, _logEntries == null || _logEntries.Count == 0);
            UpdateNoMatchesHint(ActivityNoMatchesHint,
                (_logEntries != null && _logEntries.Count > 0) || _activityRows.Count > 0,
                _activityRows.Count);
            UpdateClearButtonsEnabled();
            if (TabActivity != null && TabActivity.IsSelected)
                ScrollActivityLogToEnd();
        }

        /// <summary>Fill dest with filtered Activity lines from LogPath + .1 within retention.</summary>
        private void CollectFilteredActivityFromDisk(IList<LogEntry> dest, int maxRows)
        {
            CollectFilteredActivityFromDisk(dest, maxRows, _logFilter, _activitySearchText);
        }

        private void CollectFilteredActivityFromDisk(IList<LogEntry> dest, int maxRows, string filter, string search)
        {
            if (dest == null) return;
            try
            {
                var cutoff = LogRetentionCutoffLocal();
                var paths = new[] { AppSettings.LogPath + ".1", AppSettings.LogPath };
                var ring = new List<LogEntry>(Math.Max(16, maxRows + 1));
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
                        if (local < cutoff) continue;
                        var text = line.Substring(19).TrimStart();
                        if (string.IsNullOrEmpty(text) || IsActivityUiNoise(text)) continue;
                        var entry = new LogEntry
                        {
                            Time = local,
                            Level = InferLogLevel(text, false),
                            Text = text
                        };
                        if (!ShouldShowLogEntry(entry, filter, search)) continue;
                        ring.Add(entry);
                        if (ring.Count > maxRows)
                            ring.RemoveAt(0);
                    }
                }
                for (var i = 0; i < ring.Count; i++)
                    dest.Add(ring[i]);
            }
            catch { }
        }

        private const int MaxLogEntriesMemory = 500;
        private const int MaxLogBlocks = 500;
        private const int ActivitySyncMax = 500;
        private const int ActivityFilteredMax = 2000;

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
