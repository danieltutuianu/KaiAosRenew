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

        private readonly NotifyClient _client = new NotifyClient();
        private readonly List<ClientKeyRow> _keyAllRows = new List<ClientKeyRow>();
        private readonly ObservableCollection<ClientKeyRow> _keyRows = new ObservableCollection<ClientKeyRow>();
        private readonly ObservableCollection<ActivityRow> _activityRows = new ObservableCollection<ActivityRow>();
        private readonly List<RegisterHistoryRow> _registerHistory = new List<RegisterHistoryRow>();
        private const int ActivityMaxRows = 500;
        private const int ActivityFilteredMax = 2000;
        private const int RegisterHistoryMax = 5000;
        private const int RegisterUnfilteredDisplayMax = 500;
        private readonly ObservableCollection<ActivityRow> _activityHistoryRows = new ObservableCollection<ActivityRow>();
        private ICollectionView _activityHistoryView;
        private bool _activityHistoryMode;
        private int _activityHistoryReqId;
        private int _activityHistoryPendingId;
        private bool _activityHistoryLoading;
        private readonly List<RegisterHistoryRow> _registerHistoryViewRows = new List<RegisterHistoryRow>();
        private bool _registerHistoryMode;
        private int _registerHistoryReqId;
        private int _registerHistoryPendingId;
        private bool _registerHistoryLoading;
        private string _activitySearchText = "";
        private string _registerSearchText = "";
        private DispatcherTimer _activitySearchDebounceTimer;
        private DispatcherTimer _registerSearchDebounceTimer;
        private PendingLogExport _pendingLogExport;
        private DispatcherTimer _logExportTimeoutTimer;
        private bool _logExportLoading;

        private sealed class PendingLogExport
        {
            public string Kind;
            public DateTime Day;
            public bool ToClipboard;
            public Func<DateTime, string> LocalFallback;
        }
        private bool _aboutVisible;
        private bool _suppressTabChange;
        private int _tabIndexBeforeAbout = -1;
        private string _keysFilter = "all";
        private string _keysSearchText = "";
        private bool _keysFilterReady;
        private string _registerFilter = "all";
        private bool _registerFilterReady;
        private bool _servicesFail;
        private bool _servicesBlinkOn = true;
        private readonly DispatcherTimer _servicesBlinkTimer = new DispatcherTimer();
        private string _registerAgeBlinkKey;
        private bool _registerAgeBlinkOn = true;
        private int _registerAgeBlinkTicksLeft;
        private readonly DispatcherTimer _registerAgeBlinkTimer = new DispatcherTimer();

        // Activity log filter
        private string _activityFilter = "all";
        private ICollectionView _activityView;
        private bool _logFilterReady;

        // Tab alert dots + blink
        private bool _keysAlert;
        private bool _renewAlert;
        private bool _kaiServiceAlert;
        private bool _alertBlinkOn = true;
        private readonly DispatcherTimer _alertBlinkTimer = new DispatcherTimer();
        private const double KeysStallMinutes = 2;
        /// <summary>Stop blinking Keys tab after this many minutes (Waiting/Failed/stall).</summary>
        private const double KeysAlertMaxMinutes = 1;

        // Keys VIN highlight (simplified port from server)
        private const double KeysHighlightSeconds = 20;
        private readonly DispatcherTimer _keysHighlightTimer = new DispatcherTimer();
        private string _keysHighlightVin;
        private ListViewItem _keysHighlightedItem;
        private DateTime _keysHighlightUntilUtc = DateTime.MinValue;

        // KAI tab
        private bool _kaiBusy;
        private readonly DispatcherTimer _kaiBusyTimer = new DispatcherTimer();

        private sealed class LangItem
        {
            public string Code { get; set; }
            public string Name { get; set; }
        }
        private const string TestVinPlaceholder = "WBATESTVIN0000001";
        private const string BmwAosPortalUrl = "https://aos.bmwgroup.com/";
        private bool _loadingAosUi;
        private bool _aosDirty;
        private bool _aosAwaitingAck;
        private bool _awaitingAosIntervalOnly;
        private bool _aosApplyInFlight;
        private DateTime _aosSaveClickUtc = DateTime.MinValue;
        private string _lastSyncedEmail = "";
        private string _lastSyncedPass = "";
        private int _lastSyncedHours = 1;
        private bool _explicitAosSave;
        private readonly DispatcherTimer _aosAckTimer = new DispatcherTimer();
        private bool _activityAlert;
        private bool _authGuardsBlinkOn = true;
        private bool _authCooldownOn;
        private bool _authBlockedOn;
        private string _authCooldownUntilLocal;
        private readonly DispatcherTimer _authGuardsBlinkTimer = new DispatcherTimer();

        private ClientSettings _settings;
        private WinForms.NotifyIcon _tray;
        private System.Drawing.Icon _appIcon;
        private DispatcherTimer _statusTimer;
        private readonly DispatcherTimer _keysCopyFeedbackTimer = new DispatcherTimer();
        private Button _keysCopyFeedbackBtn;
        private static readonly SolidColorBrush KeysCopyOkBrush =
            new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
        private const string KeysCopyGlyph = "\uE8C8";
        private const string KeysCopyOkGlyph = "\uE73E";
        private bool _exitRequested;
        private bool _loadingUi;
        private bool _loadingSrvSettings;
        private bool _srvSettingsDirty;
        private bool _srvSettingsConflict;
        private bool _awaitingSettingsApply;
        private bool _awaitingSettingsImport;
        private Dictionary<string, string> _lastServerSettingsMap;
        private Dictionary<string, string> _conflictServerSettingsMap;
        private bool _startedHiddenToTray;
        private bool _wantConnectUi;
        private bool _scanBusy;
        private DateTime _lastHeardUtc = DateTime.MinValue;
        private bool _offlineAlertShown;
        private string _lastVin;
        private string _trayStatus = "Offline";
        private bool _hasServerFooter;
        private string _lastTokenMode = "none";
        private string _serverVersionFromFooter = "";
        private string _latestServerReleaseVer = "";
        private string _feedServerVer = "";
        private string _feedClientVer = "";
        private bool _serverUpdateCheckBusy;
        private bool _awaitingServerUpdateReconnect;
        private string _pendingServerUpdateVer = "";
        private readonly DispatcherTimer _updatePeekTimer = new DispatcherTimer();
        private UpdateInfo _pendingUpdate;
        private bool _updateCheckBusy;
        private bool _updateCheckStarted;
        private bool _clientUpdateBusy;
        private System.Drawing.Icon _outdatedIcon;
        private bool _debugMode;
        private string _debugTokenModeKey;
        private int _aboutVersionClicks;
        private readonly Dictionary<string, FolderZipTransfer> _folderZipTransfers =
            new Dictionary<string, FolderZipTransfer>(StringComparer.OrdinalIgnoreCase);

        private sealed class FolderZipTransfer
        {
            public string FileName;
            public long TotalBytes;
            public int NextSeq;
            public MemoryStream Stream = new MemoryStream();
        }
        private DateTime _aboutVersionClickUtc = DateTime.MinValue;
        private Brush _footerOk = FooterOkDay;
        private Brush _footerError = FooterErrorDay;
        private Brush _footerStopped = FooterStoppedDay;
        private Brush _footerMuted = FooterMutedDay;
        private static readonly SolidColorBrush FooterOkDay = CreateBrush(0x2E, 0x7D, 0x32);
        private static readonly SolidColorBrush FooterErrorDay = CreateBrush(0xC6, 0x28, 0x28);
        private static readonly SolidColorBrush FooterStoppedDay = CreateBrush(0x8C, 0x8C, 0x8C);
        private static readonly SolidColorBrush FooterMutedDay = CreateBrush(0x6E, 0x6E, 0x6E);
        private static readonly SolidColorBrush FooterBgDay = CreateBrush(0xD4, 0xC4, 0xA8);
        private static readonly SolidColorBrush FooterOkNight = CreateBrush(0x4C, 0xAF, 0x50);
        private static readonly SolidColorBrush FooterErrorNight = CreateBrush(0xEF, 0x53, 0x50);
        private static readonly SolidColorBrush FooterStoppedNight = CreateBrush(0x9A, 0x9A, 0x9A);
        private static readonly SolidColorBrush FooterMutedNight = CreateBrush(0xA0, 0xA0, 0xA0);
        private static readonly SolidColorBrush FooterBgNight = CreateBrush(0x3A, 0x34, 0x2C);

        private static SolidColorBrush CreateBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        public bool PreferStartHidden
        {
            get { return _settings != null && _settings.StartWithWindows && _settings.MinimizeToTray; }
        }

        public static string VersionDisplay
        {
            get
            {
                try
                {
                    var v = Assembly.GetExecutingAssembly().GetName().Version;
                    if (v == null) return "1.2.0";
                    return v.Major + "." + v.Minor + "." + v.Build;
                }
                catch { return "1.2.0"; }
            }
        }

        public MainWindow()
        {
            _settings = ClientSettings.Load();
            ClientUiStrings.SetLanguage(_settings.Language);
            ClientThemeManager.SetModeProvider(() => _settings != null ? _settings.Theme : "system");
            _loadingUi = true;
            InitializeComponent();
            // Apply theme after XAML load so a failed swap cannot blank the tree.
            try { ClientThemeManager.Apply(_settings.Theme); }
            catch { }
            if (KeysCopyOkBrush.CanFreeze) KeysCopyOkBrush.Freeze();
            _keysCopyFeedbackTimer.Interval = TimeSpan.FromMilliseconds(900);
            _keysCopyFeedbackTimer.Tick += KeysCopyFeedbackTimer_Tick;
            _aosAckTimer.Interval = TimeSpan.FromSeconds(10);
            _aosAckTimer.Tick += AosAckTimer_Tick;
            _authGuardsBlinkTimer.Interval = TimeSpan.FromMilliseconds(500);
            _authGuardsBlinkTimer.Tick += AuthGuardsBlinkTimer_Tick;
            _servicesBlinkTimer.Interval = TimeSpan.FromMilliseconds(500);
            _servicesBlinkTimer.Tick += ServicesBlinkTimer_Tick;
            _registerAgeBlinkTimer.Interval = TimeSpan.FromMilliseconds(450);
            _registerAgeBlinkTimer.Tick += RegisterAgeBlinkTimer_Tick;
            _alertBlinkTimer.Interval = TimeSpan.FromMilliseconds(450);
            _alertBlinkTimer.Tick += AlertBlinkTimer_Tick;
            _keysHighlightTimer.Tick += KeysHighlightTimer_Tick;
            _kaiBusyTimer.Interval = TimeSpan.FromSeconds(120);
            _kaiBusyTimer.Tick += KaiBusyTimer_Tick;
            _updatePeekTimer.Interval = TimeSpan.FromHours(6);
            _updatePeekTimer.Tick += UpdatePeekTimer_Tick;
            ApplyAppIcon();
            try
            {
                InitLanguageCombo();
                InitThemeCombo();
                ApplyLanguageToUi();
                ApplyConnectionUi();
                LoadSettingsToUi();
                RefreshVersionLabel();
                if (AboutTitleText != null)
                    AboutTitleText.Text = ClientUiStrings.T("AppTitle");
                if (AboutVersionText != null)
                    AboutVersionText.Text = string.Format(ClientUiStrings.T("Version"), VersionDisplay);
                InitKeysFilterCombo();
                LoadKeysHistory();
                if (KeysEventsList != null)
                    KeysEventsList.ItemsSource = _keyRows;
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
                _activityView = CollectionViewSource.GetDefaultView(_activityRows);
                if (_activityView != null)
                    _activityView.Filter = ShouldShowActivityRow;
                _activityHistoryView = CollectionViewSource.GetDefaultView(_activityHistoryRows);
                if (_activityHistoryView != null)
                    _activityHistoryView.Filter = ShouldShowActivityHistoryRow;
                InitLogFilterCombo();
                InitRegisterFilterCombo();
                UpdateKeysTodayBadge();
                UpdateAosSyncStatus();
                UpdateAosLoginStatus("none");
                UpdateAosControlsEnabled();
                UpdateServerSettingsEnabled();
                UpdateConnectTargetEnabled();
                UpdateConnectButtonLabel();
                UpdateClearButtonsEnabled();
                RefreshAuthGuardsPanel(false, "", false, "", "");
                _debugMode = _settings != null && _settings.DebugMode;
                ApplyDebugModeUi();
                if (MainTabs != null)
                {
                    MainTabs.SelectionChanged += MainTabs_SelectionChanged;
                    if (TabKeys != null)
                        MainTabs.SelectedItem = TabKeys;
                }
            }
            finally
            {
                _loadingUi = false;
            }
            _client.Status += OnClientStatus;
            _client.Connected += OnClientConnected;
            _client.Disconnected += OnClientDisconnected;
            _client.MessageReceived += OnMessageReceived;
            _client.AuthFailed += OnClientAuthFailed;
            InitTray();
            RebuildTrayMenu();
            SourceInitialized += MainWindow_SourceInitialized;
            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _statusTimer.Tick += StatusTimer_Tick;
            _statusTimer.Start();
            RefreshTrayStatus();
        }

        public void MarkStartedHiddenToTray()
        {
            _startedHiddenToTray = true;
        }



        private void RefreshHostCombo()
        {
            if (HostCombo == null) return;
            var text = GetHostText();
            HostCombo.Items.Clear();
            if (_settings.RecentHosts != null)
            {
                foreach (var h in _settings.RecentHosts)
                    HostCombo.Items.Add(h);
            }
            SetHostText(text);
        }

        private string GetHostText()
        {
            if (HostCombo == null) return _settings != null ? (_settings.Host ?? "") : "";
            if (HostCombo.IsEditable)
            {
                var t = HostCombo.Text;
                if (!string.IsNullOrEmpty(t)) return t.Trim();
            }
            if (HostCombo.SelectedItem != null)
                return HostCombo.SelectedItem.ToString();
            return (HostCombo.Text ?? "").Trim();
        }

        private void SetHostText(string host)
        {
            if (HostCombo == null) return;
            host = host ?? "";
            HostCombo.Text = host;
        }

        private void MainWindow_SourceInitialized(object sender, EventArgs e)
        {
            var source = PresentationSource.FromVisual(this) as HwndSource;
            if (source != null)
                source.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Program.WmShowMe)
            {
                handled = true;
                Dispatcher.BeginInvoke(new Action(ShowFromTray));
            }
            return IntPtr.Zero;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (_startedHiddenToTray || (PreferStartHidden && Visibility != Visibility.Visible))
            {
                ShowInTaskbar = false;
                if (IsVisible) Hide();
            }
            if (_settings.AutoConnect && !string.IsNullOrWhiteSpace(_settings.Host))
                DoConnect();
            MaybeCheckForUpdatesOnStart();
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
                DebugModeText.Text = ClientUiStrings.T("DebugModeOn");
                DebugModeText.Visibility = _debugMode ? Visibility.Visible : Visibility.Collapsed;
            }
            if (KeysColBin != null)
                KeysColBin.Width = _debugMode ? 36 : 0;
            if (KeysColBinPad != null)
                KeysColBinPad.Width = _debugMode ? 18 : 0;
            if (SrvDownloadKeyDebugFolderBtn != null)
                SrvDownloadKeyDebugFolderBtn.Visibility = _debugMode ? Visibility.Visible : Visibility.Collapsed;
            ShowPendingUpdate(_pendingUpdate);
            RefreshServerUpdateFromFeed();
            RefreshTrayOutdatedIcon();
        }











        private void RequestNotifyClients()
        {
            if (_client == null || !_client.IsConnected) return;
            try
            {
                _client.Send(new NotifyMessage
                {
                    Kind = "notifyClientsReq",
                    Level = "info",
                    Title = "KAI - AOS Renew - Client",
                    Text = ""
                });
            }
            catch { }
        }










        private void RefreshVersionLabel()
        {
            if (VersionText == null) return;
            if (!string.IsNullOrEmpty(_serverVersionFromFooter))
            {
                VersionText.Text = string.Format(
                    ClientUiStrings.T("VersionWithServer"), VersionDisplay, _serverVersionFromFooter);
            }
            else
            {
                VersionText.Text = string.Format(ClientUiStrings.T("Version"), VersionDisplay);
            }
        }


        private void ApplyAppIcon()
        {
            try
            {
                var icoPath = FindAppIconPath();
                if (icoPath != null)
                {
                    _appIcon = new System.Drawing.Icon(icoPath);
                    Icon = BitmapFrame.Create(new Uri(icoPath, UriKind.Absolute));
                    return;
                }

                // Release pack ships exe only — use Win32 icon embedded via ApplicationIcon.
                var exe = Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
                    exe = Process.GetCurrentProcess().MainModule.FileName;
                using (var assoc = System.Drawing.Icon.ExtractAssociatedIcon(exe))
                {
                    if (assoc == null) return;
                    _appIcon = (System.Drawing.Icon)assoc.Clone();
                }
                if (_appIcon == null) return;
                var src = Imaging.CreateBitmapSourceFromHIcon(
                    _appIcon.Handle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                Icon = src;
            }
            catch { }
        }

        private static string FindAppIconPath()
        {
            try
            {
                var dir = AppDomain.CurrentDomain.BaseDirectory;
                var candidates = new[]
                {
                    Path.Combine(dir, "app.ico"),
                    Path.Combine(dir, "assets", "app.ico"),
                    Path.Combine(dir, "..", "..", "assets", "app.ico")
                };
                foreach (var p in candidates)
                {
                    var full = Path.GetFullPath(p);
                    if (File.Exists(full)) return full;
                }
            }
            catch { }
            return null;
        }

        private void InitTray()
        {
            try
            {
                _tray = new WinForms.NotifyIcon();
                if (_appIcon != null)
                    _tray.Icon = _appIcon;
                else
                    _tray.Icon = System.Drawing.SystemIcons.Application;
                _tray.Visible = true;
                _tray.DoubleClick += delegate { ShowFromTray(); };
            }
            catch { }
        }

        private void RebuildTrayMenu()
        {
            if (_tray == null) return;
            try
            {
                var menu = new WinForms.ContextMenu();
                menu.MenuItems.Add(ClientUiStrings.T("TrayOpen"), delegate { ShowFromTray(); });
                var copyLabel = string.IsNullOrEmpty(_lastVin)
                    ? ClientUiStrings.T("TrayCopyVin")
                    : string.Format(ClientUiStrings.T("TrayCopyVinWith"), _lastVin);
                var copyItem = menu.MenuItems.Add(copyLabel, delegate
                {
                    if (!string.IsNullOrEmpty(_lastVin))
                        CopyVin(_lastVin);
                });
                copyItem.Enabled = !string.IsNullOrEmpty(_lastVin);
                menu.MenuItems.Add(ClientUiStrings.T("TrayAbout"), delegate
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        ShowFromTray();
                        ShowAboutPanel();
                    }));
                });
                menu.MenuItems.Add(ClientUiStrings.T("TrayExit"), delegate { ExitApp(); });
                _tray.ContextMenu = menu;
                ApplyTrayTooltip();
            }
            catch { }
        }

        private void TestBalloonTipsBtn_Click(object sender, RoutedEventArgs e)
        {
            RunTestBalloons();
        }

        private async void RunTestBalloons()
        {
            try
            {
                SetConnStatus(ClientUiStrings.T("BalloonWait"));
                await Task.Delay(1000);
                ToastWindow.ShowTip(ClientUiStrings.T("AppTitle"), ClientUiStrings.T("BalloonInfo"), "info", null, null, 4);
                SetConnStatus(ClientUiStrings.T("BalloonInfo"));
                await Task.Delay(4500);
                ToastWindow.ShowTip(ClientUiStrings.T("AppTitle"), ClientUiStrings.T("BalloonWarning"), "warning", null, null, 4);
                SetConnStatus(ClientUiStrings.T("BalloonWarning"));
                await Task.Delay(4500);
                ToastWindow.ShowTip(ClientUiStrings.T("AppTitle"), ClientUiStrings.T("BalloonError"), "error", null, null, 4);
                SetConnStatus(ClientUiStrings.T("BalloonError"));
                await Task.Delay(4500);
                SetConnStatus("");
                RefreshTrayStatus();
            }
            catch { }
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
        }


        private void AutoConnect_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingUi) return;
            _settings.AutoConnect = AutoConnectCheck != null && AutoConnectCheck.IsChecked == true;
            CaptureConnectionFields();
            _settings.Save();
        }






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



        private static string FindChangelogPath()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var nextToExe = Path.Combine(baseDir, "CHANGELOG.md");
                if (File.Exists(nextToExe)) return nextToExe;
                // Dev: KaiAosNotifyClient\bin\Release -> repo root
                var probe = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "CHANGELOG.md"));
                if (File.Exists(probe)) return probe;
                probe = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "CHANGELOG.md"));
                if (File.Exists(probe)) return probe;
            }
            catch { }
            return null;
        }




























        private static IEnumerable<ClientKeyRow> FilterByStatus(IEnumerable<ClientKeyRow> src, string status)
        {
            var list = new List<ClientKeyRow>();
            foreach (var r in src)
            {
                if (r != null && string.Equals(r.Status, status, StringComparison.OrdinalIgnoreCase))
                    list.Add(r);
            }
            return list;
        }

        private static bool ContainsIgnoreCase(string hay, string needle)
        {
            if (string.IsNullOrEmpty(hay) || string.IsNullOrEmpty(needle)) return false;
            return hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ReferenceEquals(e.OriginalSource, MainTabs)) return;
            if (_suppressTabChange) return;

            var leftServerSettings = false;
            if (e.RemovedItems != null)
            {
                for (var i = 0; i < e.RemovedItems.Count; i++)
                {
                    if (ReferenceEquals(e.RemovedItems[i], TabServerSettings))
                    {
                        leftServerSettings = true;
                        break;
                    }
                }
            }
            if (leftServerSettings && _srvSettingsDirty)
                DiscardUnsavedServerSettings();

            if (_aboutVisible && TabDummy != null && !TabDummy.IsSelected)
            {
                _aboutVisible = false;
                if (AboutPanel != null) AboutPanel.Visibility = Visibility.Collapsed;
                if (AboutTabBtn != null) AboutTabBtn.Tag = null;
            }
            if (!_aboutVisible && TabActivity != null && TabActivity.IsSelected)
            {
                ClearActivityAlert();
                ScrollActivityToEnd();
            }
            if (!_aboutVisible && TabRegister != null && TabRegister.IsSelected)
            {
                RefreshRegisterAges();
                ScrollRegisterHistoryToEnd();
            }
            if (!_aboutVisible && TabServerSettings != null && TabServerSettings.IsSelected)
            {
                if (_client != null && _client.IsConnected)
                    RequestServerSettingsSync();
                else if (_lastServerSettingsMap != null && _lastServerSettingsMap.Count > 0)
                    DiscardUnsavedServerSettings();
            }
            UpdateAosButtonsVisibility();
        }











        private void JumpToActivityWithAlert()
        {
            _activityAlert = true;
            UpdateActivityAlertDot();
            if (MainTabs != null && TabActivity != null)
                MainTabs.SelectedItem = TabActivity;
            ClearActivityAlert();
        }

        private void MaybeRaiseActivityAlert(string level)
        {
            var l = (level ?? "").Trim().ToLowerInvariant();
            if (l != "error") return;
            if (TabActivity != null && TabActivity.IsSelected) return;
            _activityAlert = true;
            UpdateActivityAlertDot();
            SyncAlertBlinkTimer();
        }

        private void ClearActivityAlert()
        {
            _activityAlert = false;
            UpdateActivityAlertDot();
            SyncAlertBlinkTimer();
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
            var raw = AosIntervalBox != null ? (AosIntervalBox.Text ?? "").Trim() : "";
            if (!int.TryParse(raw, out hours) || hours < 1 || hours > 168)
            {
                hours = 1;
                return ShowAosFieldError("InvalidInterval", AosIntervalBox, showUi);
            }
            ClearFieldInvalid(AosIntervalBox);
            return true;
        }


        private void SetActionEnabled(Button btn, bool enabled, string disabledTipKey, string enabledTipKey = null)
        {
            if (btn == null) return;
            btn.IsEnabled = enabled;
            if (!enabled && !string.IsNullOrEmpty(disabledTipKey))
                btn.ToolTip = ClientUiStrings.T(disabledTipKey);
            else if (!string.IsNullOrEmpty(enabledTipKey))
                btn.ToolTip = ClientUiStrings.T(enabledTipKey);
            else if (enabled)
                btn.ToolTip = null;
        }

        private static bool IsValidHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host)) return false;
            host = host.Trim();
            if (host.IndexOf(' ') >= 0) return false;
            IPAddress ip;
            if (IPAddress.TryParse(host, out ip)) return true;
            var kind = Uri.CheckHostName(host);
            return kind == UriHostNameType.Dns || kind == UriHostNameType.IPv4 || kind == UriHostNameType.IPv6;
        }



        private void SendDownloadCmd(string cmd)
        {
            if (_client == null || !_client.IsConnected)
            {
                SetSrvSettingsStatus(string.Format(ClientUiStrings.T("SrvDownloadFail"), "not connected"), false);
                return;
            }
            SetSrvSettingsStatus(ClientUiStrings.T("SrvDownloadBusy"), false);
            var ok = _client.Send(new NotifyMessage
            {
                Kind = "aosCmd",
                Level = "info",
                Title = "KAI - AOS Renew - Client",
                Text = cmd ?? ""
            });
            if (!ok)
            {
                SetSrvSettingsStatus(string.Format(ClientUiStrings.T("SrvDownloadFail"), ClientUiStrings.T("AosSyncSendFail")), false);
                AppendActivityRow(null, "error", ClientUiStrings.T("AosSyncSendFail"));
            }
        }








        private void HandleBinaryDownloadPayload(string text, string subFolder)
        {
            try
            {
                if (string.IsNullOrEmpty(text))
                {
                    SetSrvSettingsStatus(string.Format(ClientUiStrings.T("SrvDownloadFail"), "empty"), false);
                    return;
                }
                var pipe = text.IndexOf('|');
                if (pipe <= 0 || pipe >= text.Length - 1)
                {
                    SetSrvSettingsStatus(string.Format(ClientUiStrings.T("SrvDownloadFail"), "bad payload"), false);
                    return;
                }
                var name = text.Substring(0, pipe).Trim();
                var b64 = text.Substring(pipe + 1).Trim();
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(b64))
                {
                    SetSrvSettingsStatus(string.Format(ClientUiStrings.T("SrvDownloadFail"), "bad payload"), false);
                    return;
                }
                foreach (var c in Path.GetInvalidFileNameChars())
                    name = name.Replace(c, '_');
                var dir = Path.Combine(ClientSettings.StoreDir, subFolder ?? "downloads");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, name);
                File.WriteAllBytes(path, Convert.FromBase64String(b64));
                FinishBinaryDownload(path);
            }
            catch (Exception ex)
            {
                SetSrvSettingsStatus(string.Format(ClientUiStrings.T("SrvDownloadFail"), ex.Message), false);
                AppendActivityRow(null, "error", string.Format(ClientUiStrings.T("SrvDownloadFail"), ex.Message));
            }
        }

        private void HandleFolderZipStart(string text)
        {
            try
            {
                var parts = (text ?? "").Split('|');
                if (parts.Length < 3) return;
                var id = (parts[0] ?? "").Trim();
                var name = (parts[1] ?? "").Trim();
                long total;
                if (string.IsNullOrEmpty(id) || !long.TryParse(parts[2], out total) || total < 0) return;
                foreach (var c in Path.GetInvalidFileNameChars())
                    name = name.Replace(c, '_');
                FolderZipTransfer old;
                if (_folderZipTransfers.TryGetValue(id, out old) && old != null && old.Stream != null)
                {
                    try { old.Stream.Dispose(); }
                    catch { }
                }
                _folderZipTransfers[id] = new FolderZipTransfer
                {
                    FileName = string.IsNullOrEmpty(name) ? "download.zip" : name,
                    TotalBytes = total,
                    NextSeq = 0,
                    Stream = new MemoryStream()
                };
            }
            catch { }
        }

        private void HandleFolderZipChunk(string text)
        {
            try
            {
                var parts = (text ?? "").Split(new[] { '|' }, 3);
                if (parts.Length < 3) return;
                var id = (parts[0] ?? "").Trim();
                int seq;
                if (string.IsNullOrEmpty(id) || !int.TryParse(parts[1], out seq)) return;
                FolderZipTransfer t;
                if (!_folderZipTransfers.TryGetValue(id, out t) || t == null || t.Stream == null) return;
                if (seq != t.NextSeq) return;
                var raw = Convert.FromBase64String(parts[2] ?? "");
                t.Stream.Write(raw, 0, raw.Length);
                t.NextSeq++;
            }
            catch { }
        }

        private void HandleFolderZipEnd(string text)
        {
            try
            {
                var parts = (text ?? "").Split('|');
                if (parts.Length < 1) return;
                var id = (parts[0] ?? "").Trim();
                FolderZipTransfer t;
                if (string.IsNullOrEmpty(id) || !_folderZipTransfers.TryGetValue(id, out t) || t == null)
                    return;
                _folderZipTransfers.Remove(id);
                try
                {
                    var name = t.FileName ?? "download.zip";
                    if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
                        name = parts[1].Trim();
                    foreach (var c in Path.GetInvalidFileNameChars())
                        name = name.Replace(c, '_');
                    var dir = Path.Combine(ClientSettings.StoreDir, "downloads");
                    Directory.CreateDirectory(dir);
                    var path = Path.Combine(dir, name);
                    File.WriteAllBytes(path, t.Stream.ToArray());
                    FinishBinaryDownload(path);
                }
                finally
                {
                    try { t.Stream.Dispose(); }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                SetSrvSettingsStatus(string.Format(ClientUiStrings.T("SrvDownloadFail"), ex.Message), false);
                AppendActivityRow(null, "error", string.Format(ClientUiStrings.T("SrvDownloadFail"), ex.Message));
            }
        }

        private void FinishBinaryDownload(string path)
        {
            SetSrvSettingsStatus(string.Format(ClientUiStrings.T("SrvDownloadOk"), path), true);
            AppendActivityRow(null, "success", string.Format(ClientUiStrings.T("SrvDownloadOk"), path));
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "/select,\"" + path + "\"",
                    UseShellExecute = true
                });
            }
            catch { }
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













        private void SendClientHello()
        {
            if (_client == null || !_client.IsConnected) return;
            try
            {
                if (_settings != null)
                {
                    _settings.EnsureInstanceGuid();
                    try { _settings.Save(); }
                    catch { }
                }
                _client.Send(new NotifyMessage
                {
                    Kind = "clientHello",
                    Level = "info",
                    Title = "KAI - AOS Renew - Client",
                    Text = VersionDisplay ?? "",
                    Detail = _settings != null ? (_settings.InstanceGuid ?? "") : ""
                });
            }
            catch { }
        }

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
            const double chrome = 36;
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

        private static string CheckboxLabel(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            return text.TrimEnd(' ', ':');
        }



        private void ClearNotifyClientsCombo()
        {
            if (SrvNotifySendToCombo == null) return;
            var items = new List<SrvNotifySendTarget>
            {
                new SrvNotifySendTarget
                {
                    ClientId = 0,
                    Label = ClientUiStrings.T("NotifyAllClients") + " (0)"
                }
            };
            SrvNotifySendToCombo.ItemsSource = items;
            SrvNotifySendToCombo.SelectedItem = items[0];
            SizeComboToLongestLabel(SrvNotifySendToCombo, items.ConvertAll(it => it.Label));
        }


        private void HandleMessage(NotifyMessage msg)
        {
            _lastHeardUtc = DateTime.UtcNow;
            if (_offlineAlertShown)
                _offlineAlertShown = false;

            var kind = (msg.Kind ?? "").Trim().ToLowerInvariant();
            if (kind == "footer")
            {
                ApplyServerFooter(msg.Text);
                RefreshTrayStatus();
                return;
            }
            if (kind == "updatefeed")
            {
                string serverVer;
                string clientVer;
                if (NotifyMessage.TryParseUpdateFeed(msg.Text, out serverVer, out clientVer))
                    ApplyCachedFeedVersions(serverVer, clientVer);
                RefreshTrayStatus();
                return;
            }
            if (kind == "serverupdating")
            {
                BeginAwaitServerUpdate(msg.Text);
                RefreshTrayStatus();
                return;
            }
            if (kind == "serverupdatefailed")
            {
                HandleServerUpdateFailed(msg.Text);
                RefreshTrayStatus();
                return;
            }
            if (kind == "ping")
            {
                RefreshTrayStatus();
                return;
            }
            if (kind == "activitysync")
            {
                ApplyActivitySync(msg.Text);
                RefreshTrayStatus();
                return;
            }
            if (kind == "clientupdate")
            {
                HandleForcedClientUpdate(msg);
                RefreshTrayStatus();
                return;
            }
            if (kind == "registersync")
            {
                ApplyRegisterSync(msg.Text);
                RefreshTrayStatus();
                return;
            }
            if (kind == "registerhistory")
            {
                ApplyRegisterHistory(msg);
                RefreshTrayStatus();
                return;
            }
            if (kind == "register")
            {
                AppendRegisterFromNotify(msg);
                RefreshTrayStatus();
                return;
            }
            if (kind == "activity")
            {
                AppendActivityRow(msg.At, msg.Level ?? "info", msg.Text ?? "");
                MaybeRaiseActivityAlert(msg.Level);
                RefreshTrayStatus();
                return;
            }
            if (kind == "activityhistory")
            {
                ApplyActivityHistory(msg);
                RefreshTrayStatus();
                return;
            }
            if (kind == "activityexport")
            {
                HandleLogExportReply("activity", msg);
                RefreshTrayStatus();
                return;
            }
            if (kind == "registerexport")
            {
                HandleLogExportReply("register", msg);
                RefreshTrayStatus();
                return;
            }
            if (kind == "activityclear")
            {
                _activityRows.Clear();
                _activityHistoryRows.Clear();
                RefreshActivityView();
                RefreshTrayStatus();
                return;
            }
            if (kind == "kaisync")
            {
                ApplyKaiSync(msg.Text);
                RefreshTrayStatus();
                return;
            }
            if (kind == "aossync")
            {
                ApplyAosSync(msg.Text);
                RefreshTrayStatus();
                return;
            }
            if (kind == "settingssync")
            {
                ApplyServerSettingsSync(msg.Text);
                RequestNotifyClients();
                RefreshTrayStatus();
                return;
            }
            if (kind == "notifyclients")
            {
                ApplyNotifyClientsList(msg.Text);
                RefreshTrayStatus();
                return;
            }
            if (kind == "notifytestack")
            {
                HandleNotifyTestAck(msg);
                RefreshTrayStatus();
                return;
            }
            if (kind == "settingsexport")
            {
                HandleSettingsExportPayload(msg.Text);
                RefreshTrayStatus();
                return;
            }
            if (kind == "keybin")
            {
                HandleBinaryDownloadPayload(msg.Text, "key-bins");
                RefreshTrayStatus();
                return;
            }
            if (kind == "folderzip")
            {
                HandleBinaryDownloadPayload(msg.Text, "downloads");
                RefreshTrayStatus();
                return;
            }
            if (kind == "folderzipstart")
            {
                HandleFolderZipStart(msg.Text);
                RefreshTrayStatus();
                return;
            }
            if (kind == "folderzipchunk")
            {
                HandleFolderZipChunk(msg.Text);
                RefreshTrayStatus();
                return;
            }
            if (kind == "folderzipend")
            {
                HandleFolderZipEnd(msg.Text);
                RefreshTrayStatus();
                return;
            }
            if (kind == "aosack")
            {
                HandleAosAck(msg);
                RefreshTrayStatus();
                return;
            }

            if (kind == "keyreaderwarn")
            {
                ApplyKeysReaderWarn(msg.Text);
                RefreshTrayStatus();
                return;
            }

            if (kind == "keysync" || kind == "vinsync")
            {
                ApplyKeySync(msg.Text);
                RebuildTrayMenu();
                RefreshTrayStatus();
                return;
            }

            if (kind == "vin" || !string.IsNullOrEmpty(msg.Vin))
            {
                var row = ClientKeySync.FromNotifyMessage(msg);
                if (row != null && !string.IsNullOrEmpty(row.Vin) && !IsTestVinPlaceholder(row.Vin))
                {
                    UpsertKeyRow(row);
                    if (kind == "vin")
                        HighlightNewVin(row.Vin);
                    _lastVin = row.Vin;
                    RebuildTrayMenu();
                    if (_settings.AutoCopyVin)
                        CopyVin(row.Vin);
                }
            }

            var level = (msg.Level ?? msg.Kind ?? "info").ToLowerInvariant();
            var isVinInfo = kind == "vin"
                || (!string.IsNullOrEmpty(msg.Vin) && level == "info" && kind != "warning" && kind != "error");
            if (isVinInfo)
            {
                if (!_settings.ShowVinTips)
                {
                    RefreshTrayStatus();
                    return;
                }
                if (_settings.ShowVinTipsBmwFailOnly)
                {
                    var bmwFailed = msg.Failed == 1
                        || string.Equals(msg.Status, "Failed", StringComparison.OrdinalIgnoreCase);
                    if (!bmwFailed)
                    {
                        RefreshTrayStatus();
                        return;
                    }
                }
            }
            else if (level == "warning" || level == "error" || kind == "warning" || kind == "error")
            {
                if (!_settings.ShowAlertTips)
                {
                    RefreshTrayStatus();
                    return;
                }
            }

            ShowToast(msg);
            RefreshTrayStatus();
        }





        private void ApplyActivitySync(string text)
        {
            _activityRows.Clear();
            var rows = NotifyMessage.ParseActivitySync(text);
            foreach (var row in rows)
            {
                if (row == null) continue;
                _activityRows.Add(ActivityRow.FromWire(row));
            }
            while (_activityRows.Count > ActivityMaxRows)
                _activityRows.RemoveAt(0);
            RefreshActivityView();
            ScrollActivityToEnd();
        }









        private void AppendActivityRow(string atText, string level, string text)
        {
            DateTime at;
            if (string.IsNullOrEmpty(atText) || !DateTime.TryParse(atText, out at))
                at = DateTime.Now;
            else
                at = DateTime.SpecifyKind(at, DateTimeKind.Local);
            var lvl = string.IsNullOrEmpty(level) ? "info" : level.Trim().ToLowerInvariant();
            var row = new ActivityRow
            {
                At = at,
                Level = lvl,
                Text = text ?? ""
            };
            _activityRows.Add(row);
            while (_activityRows.Count > ActivityMaxRows)
                _activityRows.RemoveAt(0);
            if (_activityHistoryMode && ShouldShowActivityRow(row))
            {
                _activityHistoryRows.Add(row);
                while (_activityHistoryRows.Count > ActivityFilteredMax)
                    _activityHistoryRows.RemoveAt(0);
                try { if (_activityHistoryView != null) _activityHistoryView.Refresh(); } catch { }
            }
            ScrollActivityToEnd();
            MaybeRaiseActivityAlert(lvl);
            UpdateClearButtonsEnabled();
        }

        private void ApplyActivityHistory(NotifyMessage msg)
        {
            if (msg == null) return;
            var reqIdText = (msg.Title ?? "").Trim();
            int reqId;
            if (!int.TryParse(reqIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out reqId))
                reqId = 0;
            if (reqId != 0 && reqId != _activityHistoryPendingId) return;
            var filter = (msg.Level ?? "").Trim().ToLowerInvariant();
            if (!string.Equals(_activityFilter, filter, StringComparison.OrdinalIgnoreCase)) return;
            if (!_activityHistoryMode) return;
            SetActivityLoading(false);
            _activityHistoryRows.Clear();
            var rows = NotifyMessage.ParseActivitySync(msg.Text);
            foreach (var row in rows)
            {
                if (row == null) continue;
                _activityHistoryRows.Add(ActivityRow.FromWire(row));
            }
            while (_activityHistoryRows.Count > ActivityFilteredMax)
                _activityHistoryRows.RemoveAt(0);
            RefreshActivityView();
            ScrollActivityToEnd();
        }

        private void ScrollActivityToEnd()
        {
            if (ActivityList == null || ActivityList.Items.Count == 0) return;
            // Defer until layout is ready — immediate ScrollIntoView often no-ops on tab switch.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (ActivityList == null || ActivityList.Items.Count == 0) return;
                    ActivityList.UpdateLayout();
                    var last = ActivityList.Items[ActivityList.Items.Count - 1];
                    ActivityList.ScrollIntoView(last);
                }
                catch { }
            }), DispatcherPriority.Loaded);
        }

        private void ActivityClear_Click(object sender, RoutedEventArgs e)
        {
            if (_client != null && _client.IsConnected)
            {
                try
                {
                    _client.Send(new NotifyMessage
                    {
                        Kind = "activityClearReq",
                        Level = "info",
                        Title = "KAI - AOS Renew - Client",
                        Text = ""
                    });
                }
                catch { }
                return;
            }
            _activityRows.Clear();
            _activityHistoryRows.Clear();
            RefreshActivityView();
            UpdateClearButtonsEnabled();
        }

        private void ActivityList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (IsGridViewHeaderClick(e.OriginalSource as DependencyObject)) return;
            CopySelectedActivityText();
        }


        private bool CopySelectedActivityText()
        {
            var entry = ActivityList != null ? ActivityList.SelectedItem as ActivityRow : null;
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
                    AppendActivityRow(null, "info", feedback);
            }
            catch { }
        }

        private void SaveActivityLog_Click(object sender, RoutedEventArgs e)
        {
            BeginLogExport("activity", ClientUiStrings.T("SaveLogTitleActivity"), CollectActivityLogForDay);
        }

        private string CollectActivityLogForDay(DateTime day)
        {
            var sb = new StringBuilder();
            var dayStart = day.Date;
            var dayEnd = dayStart.AddDays(1);
            var src = _activityHistoryMode ? (IList<ActivityRow>)_activityHistoryRows : _activityRows;
            for (var i = 0; i < src.Count; i++)
            {
                var r = src[i];
                if (r == null) continue;
                if (r.At < dayStart || r.At >= dayEnd) continue;
                sb.Append(r.At.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.Append('\t').Append(r.LevelText ?? r.Level ?? "");
                sb.Append('\t').AppendLine(r.Text ?? "");
            }
            return sb.ToString();
        }

        private void BeginLogExport(string kind, string title, Func<DateTime, string> localCollector)
        {
            var dlg = new SaveLogDialog(title, DateTime.Today, ClientUiStrings.T) { Owner = this };
            if (dlg.ShowDialog() != true || !dlg.Confirmed) return;
            var day = dlg.SelectedDay.Date;
            if (_client != null && _client.IsConnected)
            {
                _pendingLogExport = new PendingLogExport
                {
                    Kind = kind,
                    Day = day,
                    ToClipboard = dlg.ToClipboard,
                    LocalFallback = localCollector
                };
                EnsureLogExportTimeoutTimer();
                _logExportTimeoutTimer.Stop();
                _logExportTimeoutTimer.Start();
                SetExportLoading(true, kind);
                var reqKind = string.Equals(kind, "register", StringComparison.OrdinalIgnoreCase)
                    ? "registerExportReq"
                    : "activityExportReq";
                try
                {
                    _client.Send(new NotifyMessage
                    {
                        Kind = reqKind,
                        Level = "info",
                        Title = "KAI - AOS Renew - Client",
                        Text = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    });
                }
                catch
                {
                    CancelPendingLogExport(true);
                }
                return;
            }
            FinishLogExport(kind, day, dlg.ToClipboard, localCollector != null ? localCollector(day) : "");
        }

        private void EnsureLogExportTimeoutTimer()
        {
            if (_logExportTimeoutTimer != null) return;
            _logExportTimeoutTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _logExportTimeoutTimer.Tick += (s, e) => CancelPendingLogExport(true);
        }

        private void CancelPendingLogExport(bool useFallback)
        {
            if (_logExportTimeoutTimer != null) _logExportTimeoutTimer.Stop();
            var pending = _pendingLogExport;
            _pendingLogExport = null;
            SetExportLoading(false, pending != null ? pending.Kind : null);
            if (!useFallback || pending == null) return;
            string body = "";
            try
            {
                if (pending.LocalFallback != null)
                    body = pending.LocalFallback(pending.Day) ?? "";
            }
            catch (Exception ex)
            {
                AppendActivityRow(null, "error", ex.Message);
                return;
            }
            FinishLogExport(pending.Kind, pending.Day, pending.ToClipboard, body);
        }

        private void HandleLogExportReply(string kind, NotifyMessage msg)
        {
            var pending = _pendingLogExport;
            if (pending == null || !string.Equals(pending.Kind, kind, StringComparison.OrdinalIgnoreCase))
                return;
            DateTime day = pending.Day;
            if (!string.IsNullOrWhiteSpace(msg != null ? msg.At : null))
            {
                DateTime parsed;
                if (DateTime.TryParseExact((msg.At ?? "").Trim(), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                    day = parsed.Date;
            }
            if (_logExportTimeoutTimer != null) _logExportTimeoutTimer.Stop();
            _pendingLogExport = null;
            SetExportLoading(false, kind);
            FinishLogExport(kind, day, pending.ToClipboard, msg != null ? (msg.Text ?? "") : "");
        }

        private void FinishLogExport(string kind, DateTime day, bool toClipboard, string text)
        {
            var filePrefix = string.Equals(kind, "register", StringComparison.OrdinalIgnoreCase) ? "register" : "activity";
            if (string.IsNullOrWhiteSpace(text))
            {
                AppendActivityRow(null, "warning", ClientUiStrings.T("SaveLogEmpty"));
                return;
            }
            if (toClipboard)
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

        // ---------- Activity filter ----------

        private static string TL(string key, string fallback)
        {
            var v = ClientUiStrings.T(key);
            return string.IsNullOrEmpty(v) || string.Equals(v, key, StringComparison.Ordinal) ? fallback : v;
        }

        private void InitLogFilterCombo()
        {
            RefreshLogFilterComboItems();
        }

        private void RefreshLogFilterComboItems()
        {
            if (LogFilterCombo == null) return;
            var prev = _activityFilter;
            var sel = LogFilterCombo.SelectedValue as string;
            if (!string.IsNullOrEmpty(sel)) prev = sel;
            _logFilterReady = false;
            try
            {
                LogFilterCombo.DisplayMemberPath = "Name";
                LogFilterCombo.SelectedValuePath = "Code";
                // Keep in sync with server MainWindow.RefreshLogFilterComboItems order.
                LogFilterCombo.ItemsSource = new List<LangItem>
                {
                    new LangItem { Code = "all", Name = TL("LogAll", "All") },
                    new LangItem { Code = "info", Name = TL("LogInfo", "Info") },
                    new LangItem { Code = "success", Name = TL("LogSuccess", "Success") },
                    new LangItem { Code = "warning", Name = TL("LogWarning", "Warning") },
                    new LangItem { Code = "error", Name = TL("LogError", "Error") },
                    new LangItem { Code = "registration", Name = TL("LogRegistration", "Registration") },
                    new LangItem { Code = "keyreader", Name = TL("LogKeyReader", "KeyReader") }
                };
                LogFilterCombo.SelectedValue = string.IsNullOrEmpty(prev) ? "all" : prev;
                if (LogFilterCombo.SelectedValue == null)
                    LogFilterCombo.SelectedValue = "all";
                _activityFilter = (LogFilterCombo.SelectedValue as string) ?? "all";
            }
            finally
            {
                _logFilterReady = true;
            }
            ApplyActivityFilterMode();
        }

        private void LogFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_logFilterReady || LogFilterCombo == null) return;
            var code = LogFilterCombo.SelectedValue as string;
            _activityFilter = string.IsNullOrEmpty(code) ? "all" : code;
            ApplyActivityFilterMode();
            ScrollActivityToEnd();
        }

        private void ActivitySearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            EnsureActivitySearchDebounceTimer();
            _activitySearchDebounceTimer.Stop();
            _activitySearchDebounceTimer.Start();
        }

        private void RegisterSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
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
                if (_activityHistoryMode)
                    RequestActivityHistory();
                else
                    RefreshActivityView();
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
                if (_registerHistoryMode)
                    RequestRegisterHistory();
                else
                    ApplyRegisterFilter();
            };
        }

        private static bool MatchesLogSearch(string text, string needle)
        {
            return ActivityRegisterLogic.MatchesLogSearch(text, needle);
        }

        private void ApplyActivityFilterMode()
        {
            var wantHistory = !string.IsNullOrEmpty(_activityFilter)
                && !string.Equals(_activityFilter, "all", StringComparison.OrdinalIgnoreCase);
            if (!wantHistory)
            {
                _activityHistoryPendingId = 0;
                _activityHistoryMode = false;
                SetActivityLoading(false);
                _activityHistoryRows.Clear();
                if (ActivityList != null)
                    ActivityList.ItemsSource = _activityRows;
                RefreshActivityView();
                return;
            }
            _activityHistoryMode = true;
            _activityHistoryRows.Clear();
            if (ActivityList != null)
                ActivityList.ItemsSource = _activityHistoryRows;
            RequestActivityHistory();
            RefreshActivityView();
        }

        private void RequestActivityHistory()
        {
            if (!_activityHistoryMode) return;
            _activityHistoryRows.Clear();
            if (_client != null && _client.IsConnected)
            {
                _activityHistoryReqId++;
                _activityHistoryPendingId = _activityHistoryReqId;
                SetActivityLoading(true);
                try
                {
                    _client.Send(new NotifyMessage
                    {
                        Kind = "activityHistoryReq",
                        Level = "info",
                        Title = _activityHistoryPendingId.ToString(CultureInfo.InvariantCulture),
                        Text = _activityFilter,
                        Detail = _activitySearchText ?? ""
                    });
                }
                catch
                {
                    SetActivityLoading(false);
                }
            }
            else
            {
                SetActivityLoading(false);
                for (var i = 0; i < _activityRows.Count; i++)
                {
                    var r = _activityRows[i];
                    if (r == null || !MatchesActivityFilterOnly(r)) continue;
                    if (!MatchesLogSearch(r.Text, _activitySearchText)) continue;
                    _activityHistoryRows.Add(r);
                }
            }
        }

        private void SetActivityLoading(bool on)
        {
            _activityHistoryLoading = on;
            if (ActivityLoadingHint != null)
            {
                ActivityLoadingHint.Text = ClientUiStrings.T("LogsLoading");
                ActivityLoadingHint.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            }
            if (on && ActivityNoMatchesHint != null)
                ActivityNoMatchesHint.Visibility = Visibility.Collapsed;
            if (on && ActivityEmptyHint != null)
                ActivityEmptyHint.Visibility = Visibility.Collapsed;
        }

        private void SetRegisterLoading(bool on)
        {
            _registerHistoryLoading = on;
            if (RegisterLoadingHint != null)
            {
                RegisterLoadingHint.Text = ClientUiStrings.T("LogsLoading");
                RegisterLoadingHint.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            }
            if (on && RegisterNoMatchesHint != null)
                RegisterNoMatchesHint.Visibility = Visibility.Collapsed;
        }

        private void SetExportLoading(bool on, string kind = null)
        {
            _logExportLoading = on;
            var isReg = string.Equals(kind, "register", StringComparison.OrdinalIgnoreCase);
            if (on)
            {
                if (isReg)
                {
                    if (RegisterLoadingHint != null)
                    {
                        RegisterLoadingHint.Text = ClientUiStrings.T("LogsLoading");
                        RegisterLoadingHint.Visibility = Visibility.Visible;
                    }
                }
                else if (ActivityLoadingHint != null && !_activityHistoryLoading)
                {
                    ActivityLoadingHint.Text = ClientUiStrings.T("LogsLoading");
                    ActivityLoadingHint.Visibility = Visibility.Visible;
                }
            }
            else
            {
                if (!_activityHistoryLoading && ActivityLoadingHint != null)
                    ActivityLoadingHint.Visibility = Visibility.Collapsed;
                if (!_registerHistoryLoading && RegisterLoadingHint != null)
                    RegisterLoadingHint.Visibility = Visibility.Collapsed;
            }
        }

        private bool MatchesActivityFilterOnly(ActivityRow row)
        {
            if (row == null) return false;
            if (string.Equals(_activityFilter, "keyreader", StringComparison.OrdinalIgnoreCase))
            {
                var t = row.Text ?? "";
                return t.StartsWith("KeyReader:", StringComparison.OrdinalIgnoreCase);
            }
            if (string.Equals(_activityFilter, "registration", StringComparison.OrdinalIgnoreCase))
            {
                var t = row.Text ?? "";
                return t.IndexOf("Registration", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.IndexOf("Register", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            if (string.IsNullOrEmpty(_activityFilter) || _activityFilter == "all") return true;
            return string.Equals(row.LevelText, _activityFilter, StringComparison.OrdinalIgnoreCase);
        }

        private bool ShouldShowActivityRow(object item)
        {
            var row = item as ActivityRow;
            if (row == null) return false;
            if (!MatchesLogSearch(row.Text, _activitySearchText)) return false;
            if (_activityHistoryMode) return true;
            return MatchesActivityFilterOnly(row);
        }

        private bool ShouldShowActivityHistoryRow(object item)
        {
            var row = item as ActivityRow;
            if (row == null) return false;
            return MatchesLogSearch(row.Text, _activitySearchText);
        }

        private void RefreshActivityView()
        {
            try
            {
                if (_activityHistoryMode)
                {
                    if (_activityHistoryView != null) _activityHistoryView.Refresh();
                }
                else if (_activityView != null)
                {
                    _activityView.Refresh();
                }
            }
            catch { }
            var visible = 0;
            var view = _activityHistoryMode ? _activityHistoryView : _activityView;
            var sourceCount = _activityHistoryMode
                ? _activityHistoryRows.Count
                : (_activityRows != null ? _activityRows.Count : 0);
            if (view != null)
            {
                foreach (var _ in view)
                    visible++;
            }
            if (_activityHistoryLoading)
            {
                UpdateEmptyHint(ActivityEmptyHint, false);
                UpdateNoMatchesHint(ActivityNoMatchesHint, false, 1);
            }
            else
            {
                UpdateEmptyHint(ActivityEmptyHint, sourceCount == 0 && !_activityHistoryMode);
                UpdateNoMatchesHint(ActivityNoMatchesHint, sourceCount > 0 || _activityHistoryMode, visible);
            }
            UpdateClearButtonsEnabled();
        }

        // ---------- Alert dots + blink ----------



        private void ApplyAlertBlinkVisuals()
        {
            var op = _alertBlinkOn ? 1.0 : 0.15;
            if (KeysTabAlertDot != null)
                KeysTabAlertDot.Opacity = _keysAlert ? op : 1.0;
            if (RenewTabAlertDot != null)
                RenewTabAlertDot.Opacity = _renewAlert ? op : 1.0;
            if (KaiTabAlertDot != null)
                KaiTabAlertDot.Opacity = _kaiServiceAlert ? op : 1.0;
            if (ActivityTabAlertDot != null)
                ActivityTabAlertDot.Opacity = _activityAlert ? op : 1.0;
            if (KaiFixServicesBtn != null)
                KaiFixServicesBtn.Opacity = (_kaiServiceAlert
                    && KaiFixServicesBtn.Visibility == Visibility.Visible
                    && KaiFixServicesBtn.IsEnabled)
                    ? op
                    : 1.0;
        }



        private void SetKaiServiceAlert(bool on)
        {
            _kaiServiceAlert = on;
            if (KaiTabAlertDot != null)
                KaiTabAlertDot.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            SyncAlertBlinkTimer();
        }

        private void SelectTabFromAlertDot(TabItem tab, MouseButtonEventArgs e)
        {
            if (e != null) e.Handled = true;
            if (MainTabs == null || tab == null) return;
            MainTabs.SelectedItem = tab;
        }



        private void KaiTabAlertDot_Click(object sender, MouseButtonEventArgs e)
        {
            SelectTabFromAlertDot(TabKai, e);
        }

        // ---------- Keys VIN highlight ----------






        // Keys list is rebuilt on filter/search/sync; re-paint the highlight on the new containers.




        // ---------- KAI tab ----------


        private void ApplyKaiSync(string text)
        {
            var d = NotifyMessage.ParseKaiSync(text);
            if (d == null) return;
            // Mirror server Fix/Stop busy so clients stay disabled while server runs the command.
            SetKaiBusy(d.ServiceCmdBusy);
            if (KaiInstallText != null) KaiInstallText.Text = string.IsNullOrEmpty(d.Install) ? "—" : d.Install;
            if (KaiUrlRegisterBox != null) KaiUrlRegisterBox.Text = d.RegisterUrl ?? "";
            if (KaiUrlWebEamBox != null) KaiUrlWebEamBox.Text = d.WebEamUrl ?? "";
            if (KaiUrlUploadBox != null) KaiUrlUploadBox.Text = d.UploadUrl ?? "";
            if (KaiTokenExpiryText != null)
                KaiTokenExpiryText.Text = string.IsNullOrEmpty(d.TokenExpiryText) ? "—" : d.TokenExpiryText;

            var primary = ThemeBrush("TextPrimaryBrush", Brushes.Black);
            var okBrush = ThemeBrush("StatusOkBrush", FooterOkDay);
            var errBrush = ThemeBrush("StatusErrorBrush", FooterErrorDay);
            ApplyKaiSvcRow(KaiSvc1Name, KaiSvc1Status, KaiSvc1Start, d.Services.Count > 0 ? d.Services[0] : null, primary, okBrush, errBrush);
            ApplyKaiSvcRow(KaiSvc2Name, KaiSvc2Status, KaiSvc2Start, d.Services.Count > 1 ? d.Services[1] : null, primary, okBrush, errBrush);

            UpdateServiceActionButtons(d.AllOk, d.AnyRunning);
            SetKaiServiceAlert(d.ServiceAlert);
        }

        private static void ApplyKaiSvcRow(TextBlock nameTb, TextBlock statusTb, TextBlock startTb,
            KaiSvcWireRow s, Brush primary, Brush okBrush, Brush errBrush)
        {
            if (nameTb == null || statusTb == null || startTb == null) return;
            if (s == null)
            {
                nameTb.Text = "—";
                statusTb.Text = "—";
                startTb.Text = "—";
                nameTb.Foreground = primary;
                statusTb.Foreground = primary;
                startTb.Foreground = primary;
                return;
            }
            nameTb.Text = string.IsNullOrEmpty(s.Name) ? "—" : s.Name;
            statusTb.Text = string.IsNullOrEmpty(s.StatusText) ? "—" : s.StatusText;
            startTb.Text = string.IsNullOrEmpty(s.StartTypeText) ? "—" : s.StartTypeText;
            nameTb.Foreground = s.Ok ? primary : errBrush;
            statusTb.Foreground = s.Ok ? okBrush : errBrush;
            startTb.Foreground = s.Ok ? okBrush : errBrush;
        }


        private void SetKaiBusy(bool busy)
        {
            _kaiBusy = busy;
            _kaiBusyTimer.Stop();
            if (busy) _kaiBusyTimer.Start();
            UpdateAosControlsEnabled();
        }


        private void KaiRefresh_Click(object sender, RoutedEventArgs e)
        {
            SendAosCmd("refreshKai");
        }

        private void KaiFixServices_Click(object sender, RoutedEventArgs e)
        {
            if (_client == null || !_client.IsConnected) return;
            if (_kaiBusy) return;
            SetKaiBusy(true);
            SendAosCmd("fixServices");
        }

        private void KaiStopServices_Click(object sender, RoutedEventArgs e)
        {
            if (_client == null || !_client.IsConnected) return;
            if (_kaiBusy) return;
            SetKaiBusy(true);
            SendAosCmd("restartServices");
        }












        private void ShowToast(NotifyMessage msg)
        {
            var level = msg.Level;
            if (string.IsNullOrEmpty(level))
                level = msg.Kind;
            string actionLabel = null;
            Action action = null;
            if (!string.IsNullOrEmpty(msg.Vin)
                && (string.Equals(msg.Action, "copyVin", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(msg.Kind, "vin", StringComparison.OrdinalIgnoreCase)))
            {
                actionLabel = ClientUiStrings.T("CopyVin");
                var vin = msg.Vin;
                action = () => CopyVin(vin);
            }
            var seconds = msg.Seconds;
            if (string.Equals(msg.Kind, "vin", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(msg.Vin))
                seconds = _settings.VinTipSeconds;
            if (seconds <= 0) seconds = 8;
            var kind = (msg.Kind ?? "").Trim().ToLowerInvariant();
            var lvl = (level ?? "").Trim().ToLowerInvariant();
            var isVin = kind == "vin" || !string.IsNullOrEmpty(msg.Vin);
            var isAlert = lvl == "warning" || lvl == "error" || kind == "warning" || kind == "error";
            Action onBody = null;
            if (isVin)
            {
                onBody = () =>
                {
                    ShowFromTray();
                    if (TabKeys != null) TabKeys.IsSelected = true;
                };
            }
            else if (isAlert)
            {
                onBody = () =>
                {
                    ShowFromTray();
                    if (TabActivity != null) TabActivity.IsSelected = true;
                };
            }
            ToastWindow.ShowTip(msg.Title, msg.Text, level, actionLabel, action, seconds, onBody);
        }



        private string BuildReconnectDetailText()
        {
            if (_client == null) return ClientUiStrings.T("Reconnecting");

            var secs = 0;
            var next = _client.NextRetryUtc;
            if (next != DateTime.MinValue && next > DateTime.UtcNow)
                secs = (int)Math.Ceiling((next - DateTime.UtcNow).TotalSeconds);
            if (secs < 0) secs = 0;

            var reason = _client.LastDisconnectReason;
            if (string.IsNullOrWhiteSpace(reason))
                reason = ClientUiStrings.T("ReconnectWaiting");

            var attempt = _client.ReconnectAttempt;
            if (attempt < 1) attempt = 1;

            return string.Format(
                ClientUiStrings.T("ReconnectDetail"),
                _client.Host,
                _client.Port,
                attempt,
                secs,
                reason);
        }

        private void RefreshReconnectStatusUi()
        {
            if (_client == null || _client.IsConnected) return;
            if (!_wantConnectUi && !_client.WantConnect) return;
            if (_awaitingServerUpdateReconnect) return;

            var detail = BuildReconnectDetailText();
            ApplyStatusMode("error", detail);
            if (StatusText != null)
                StatusText.ToolTip = detail;
            SetConnStatus("");
        }

        private void MaybeAlertOffline()
        {
            if (!_client.IsConnected && !_client.WantConnect) return;
            if (_lastHeardUtc == DateTime.MinValue) return;
            var mins = _settings.OfflineMinutes;
            if (mins < 1) mins = 1;
            if ((DateTime.UtcNow - _lastHeardUtc).TotalMinutes <= mins) return;
            if (_offlineAlertShown) return;
            _offlineAlertShown = true;
            ToastWindow.ShowTip(ClientUiStrings.T("AppTitle"), ClientUiStrings.T("ServerOffline"), "warning", null, null, 8);
            RefreshTrayStatus();
        }

        private void RefreshTrayStatus()
        {
            string statusKey;
            if (_client.IsConnected)
            {
                var mins = _settings.OfflineMinutes;
                if (mins < 1) mins = 1;
                if (_lastHeardUtc != DateTime.MinValue
                    && (DateTime.UtcNow - _lastHeardUtc).TotalMinutes > mins)
                    statusKey = "Offline";
                else
                    statusKey = "Connected";
            }
            else if (_client.WantConnect || _wantConnectUi)
                statusKey = "Reconnecting";
            else
                statusKey = "Offline";

            _trayStatus = ClientUiStrings.T(statusKey);
            if (!(_client.IsConnected && _hasServerFooter))
            {
                if (_client.IsConnected)
                    ApplyStatusMode("ok", _trayStatus);
                else
                    ShowDisconnectedFooter();
            }
            ApplyTrayTooltip();
            RefreshTrayOutdatedIcon();
        }

        private void RefreshTrayOutdatedIcon()
        {
            if (_tray == null) return;
            var outdated = _pendingUpdate != null
                || (!_awaitingServerUpdateReconnect
                    && !string.IsNullOrEmpty(_latestServerReleaseVer)
                    && !string.IsNullOrEmpty(_serverVersionFromFooter)
                    && UpdateService.IsNewerVersion(_latestServerReleaseVer, _serverVersionFromFooter));
            try
            {
                if (outdated)
                {
                    if (_outdatedIcon == null)
                        _outdatedIcon = KaiAosRenew.IconFactory.GetOutdatedTrayIcon(_appIcon);
                    _tray.Icon = _outdatedIcon;
                }
                else if (_appIcon != null)
                {
                    _tray.Icon = _appIcon;
                }
            }
            catch { }
        }

        private void SetConnStatus(string text)
        {
            if (ConnStatusText == null) return;
            ConnStatusText.Text = text ?? "";
            ConnStatusText.Visibility = string.IsNullOrEmpty(ConnStatusText.Text)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void ApplyStatusMode(string mode, string text)
        {
            var brush = _footerStopped;
            if (mode == "ok") brush = _footerOk;
            else if (mode == "error" || mode == "fail") brush = _footerError;
            if (StatusDot != null) StatusDot.Fill = brush;
            if (StatusText != null)
            {
                StatusText.Text = text ?? "";
                StatusText.Foreground = brush;
                if (mode != "error" && mode != "fail")
                    StatusText.ToolTip = null;
            }
        }

        private void ApplyTaggedText(TextBlock block, string mode, string text)
        {
            if (block == null) return;
            block.Text = text ?? "";
            if (mode == "ok") block.Foreground = _footerOk;
            else if (mode == "fail" || mode == "error") block.Foreground = _footerError;
            else block.Foreground = _footerMuted;
        }

        private void ShowDisconnectedFooter()
        {
            _hasServerFooter = false;
            ApplyFooterChrome(false);
            var reconnecting = _client != null && (_client.WantConnect || _wantConnectUi);
            if (reconnecting)
            {
                var detail = BuildReconnectDetailText();
                ApplyStatusMode("error", detail);
                if (StatusText != null)
                    StatusText.ToolTip = detail;
                SetConnStatus("");
            }
            else
            {
                ApplyStatusMode("stopped", ClientUiStrings.T("Offline"));
                if (StatusText != null)
                    StatusText.ToolTip = null;
            }
            if (NextRunText != null) NextRunText.Text = "";
            if (TokenAgeText != null) TokenAgeText.Text = "";
            ApplyTaggedText(TokenFooterText, "none", "");
            ApplyTaggedText(ServicesFooterText, "none", "");
            UpdateAosLoginStatus("none");
            _servicesFail = false;
            SyncServicesBlinkTimer();
            SetRenewAlert(false);
            if (RegisterAgeText != null)
            {
                RegisterAgeText.Inlines.Clear();
                RegisterAgeText.ToolTip = null;
                RegisterAgeText.Opacity = 1.0;
            }
            _registerAgeBlinkKey = null;
            _registerAgeBlinkTicksLeft = 0;
            if (_registerAgeBlinkTimer.IsEnabled)
                _registerAgeBlinkTimer.Stop();
            _registerAgeBlinkOn = true;
            _debugTokenModeKey = null;
            if (AuthGuardsFooterText != null)
            {
                AuthGuardsFooterText.Inlines.Clear();
                AuthGuardsFooterText.ToolTip = null;
            }
            RefreshAuthGuardsPanel(false, "", false, "", "");
            _serverVersionFromFooter = "";
            _latestServerReleaseVer = "";
            RefreshVersionLabel();
            RefreshServerUpdateUi();
        }

        private void ApplyServerFooter(string text)
        {
            var parts = (text ?? "").Split('\u001f');
            ApplyFooterChrome(ClientThemeManager.IsNight || FooterPart(parts, 12) == "night");
            ApplyStatusMode(FooterPart(parts, 0), FooterPart(parts, 1));
            if (NextRunText != null) NextRunText.Text = FooterPart(parts, 2);
            if (TokenAgeText != null) TokenAgeText.Text = FooterPart(parts, 3);
            var tokenMode = FooterPart(parts, 4);
            ApplyTaggedText(TokenFooterText, tokenMode, FooterPart(parts, 5));
            SetRenewAlert(tokenMode == "fail" || tokenMode == "error");
            UpdateAosLoginStatus(tokenMode);
            MaybeDebugTokenSticky(tokenMode, FooterPart(parts, 5));
            var svcMode = FooterPart(parts, 6);
            ApplyTaggedText(ServicesFooterText, svcMode, FooterPart(parts, 7));
            _servicesFail = svcMode == "fail" || svcMode == "error";
            SyncServicesBlinkTimer();
            if (RegisterAgeText != null)
            {
                RegisterAgeText.Inlines.Clear();
                var main = FooterPart(parts, 8);
                var fail = FooterPart(parts, 9);
                if (main.Length > 0)
                    RegisterAgeText.Inlines.Add(new Run(main) { Foreground = _footerMuted });
                if (fail.Length > 0)
                    RegisterAgeText.Inlines.Add(new Run(fail) { Foreground = _footerError });
                var tip = FooterPart(parts, 11);
                RegisterAgeText.ToolTip = tip.Length == 0 ? null : tip;
                if (tip.Length > 0)
                    MaybeBlinkRegisterAge(tip);
            }
            var prevServerVer = _serverVersionFromFooter ?? "";
            var raw = (FooterPart(parts, 10) ?? "").Trim();
            // Strip legacy "Version X" / "Server version: X" if an old server still sends a label.
            var colon = raw.LastIndexOf(':');
            if (colon >= 0 && colon + 1 < raw.Length)
                raw = raw.Substring(colon + 1).Trim();
            _serverVersionFromFooter = raw;
            RefreshVersionLabel();
            if (_awaitingServerUpdateReconnect && !string.IsNullOrEmpty(raw))
            {
                var finish = FinishServerUpdateReconnectAsync();
            }
            else if (!string.Equals(raw, prevServerVer, StringComparison.Ordinal))
            {
                if (string.IsNullOrEmpty(_feedServerVer) && string.IsNullOrEmpty(_feedClientVer))
                {
                    var ignore = RefreshUpdateFeedAsync(true);
                }
                else
                {
                    RefreshServerUpdateFromFeed();
                }
            }
            else
            {
                RefreshServerUpdateFromFeed();
            }
            var cdMode = FooterPart(parts, 13);
            var cdText = FooterPart(parts, 14);
            var blMode = FooterPart(parts, 15);
            var blText = FooterPart(parts, 16);
            var authTip = FooterPart(parts, 17);
            var untilUtcRaw = FooterPart(parts, 18);
            string untilLocal = null;
            DateTime untilUtc;
            if (!string.IsNullOrEmpty(untilUtcRaw) && DateTime.TryParse(untilUtcRaw, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out untilUtc))
                untilLocal = untilUtc.ToLocalTime().ToString("g");
            _authCooldownUntilLocal = cdMode == "on" ? untilLocal : null;

            if (cdMode == "on")
            {
                cdText = untilLocal != null
                    ? string.Format(ClientUiStrings.T("FooterCooldownOnUntil"), untilLocal)
                    : ClientUiStrings.T("FooterCooldownOn");
            }
            else if (cdMode == "off" || cdText.Length > 0)
                cdText = ClientUiStrings.T("FooterCooldownOff");

            if (blMode == "on")
                blText = ClientUiStrings.T("FooterBlockedOn");
            else if (blMode == "off" || blText.Length > 0)
                blText = ClientUiStrings.T("FooterBlockedOff");

            var authPanelCd = cdMode == "on"
                ? (untilLocal != null
                    ? string.Format(ClientUiStrings.T("AuthCooldownStatusActive"), untilLocal)
                    : ClientUiStrings.T("AuthCooldownStatusActiveShort"))
                : null;

            if (AuthGuardsFooterText != null)
            {
                AuthGuardsFooterText.Inlines.Clear();
                if (cdText.Length > 0 || blText.Length > 0)
                {
                    var cdBrush = cdMode == "on" ? CreateBrush(0xB2, 0x6A, 0x00) : _footerOk;
                    var blBrush = blMode == "on" ? _footerError : _footerOk;
                    if (cdText.Length > 0)
                        AuthGuardsFooterText.Inlines.Add(new Run(cdText) { Foreground = cdBrush });
                    if (cdText.Length > 0 && blText.Length > 0)
                        AuthGuardsFooterText.Inlines.Add(new Run("  ·  ") { Foreground = _footerMuted });
                    if (blText.Length > 0)
                        AuthGuardsFooterText.Inlines.Add(new Run(blText) { Foreground = blBrush });
                    AuthGuardsFooterText.ToolTip = authTip.Length == 0 ? null : authTip;
                }
            }
            RefreshAuthGuardsPanel(cdMode == "on", authPanelCd, blMode == "on", null, authTip);
            _hasServerFooter = true;
            SetConnStatus("");
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


        private void ApplyFooterChrome(bool night)
        {
            // Prefer theme resources (StatusOkBrush etc.); fall back to built-in palette if missing.
            _footerOk = ThemeBrush("StatusOkBrush", night ? FooterOkNight : FooterOkDay);
            _footerError = ThemeBrush("StatusErrorBrush", night ? FooterErrorNight : FooterErrorDay);
            _footerStopped = ThemeBrush("StatusStoppedBrush", night ? FooterStoppedNight : FooterStoppedDay);
            _footerMuted = ThemeBrush("TextMutedBrush", night ? FooterMutedNight : FooterMutedDay);
            if (FooterBar != null)
                FooterBar.Background = night ? FooterBgNight : FooterBgDay;
            if (NextRunText != null) NextRunText.Foreground = _footerMuted;
            if (TokenAgeText != null) TokenAgeText.Foreground = _footerMuted;
            if (VersionText != null) VersionText.Foreground = _footerMuted;
            if (RegisterAgeText != null) RegisterAgeText.Foreground = _footerMuted;
            if (AuthGuardsFooterText != null) AuthGuardsFooterText.Foreground = _footerMuted;
        }

        private static string FooterPart(string[] parts, int index)
        {
            if (parts == null || index < 0 || index >= parts.Length || parts[index] == null)
                return "";
            return parts[index];
        }

        private void ApplyTrayTooltip()
        {
            if (_tray == null) return;
            var tip = "KAI - AOS Renew - Client — " + _trayStatus;
            if (tip.Length > 63) tip = tip.Substring(0, 63);
            try { _tray.Text = tip; }
            catch { }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_exitRequested) return;
            CaptureConnectionFields();
            _settings.Save();
            if (_settings.MinimizeOnClose)
            {
                e.Cancel = true;
                Hide();
                ShowInTaskbar = false;
                return;
            }
            ExitApp();
        }

        internal void PrepareForRestart()
        {
            _exitRequested = true;
            try { PersistKeysHistory(); }
            catch { }
            try
            {
                CaptureConnectionFields();
                _settings.Save();
            }
            catch { }
            try { if (_statusTimer != null) _statusTimer.Stop(); }
            catch { }
            try { _client.Dispose(); }
            catch { }
            if (_tray != null)
            {
                try { _tray.Visible = false; }
                catch { }
                try { _tray.Dispose(); }
                catch { }
                _tray = null;
            }
            if (_outdatedIcon != null)
            {
                try { _outdatedIcon.Dispose(); }
                catch { }
                _outdatedIcon = null;
            }
            if (_appIcon != null)
            {
                try { _appIcon.Dispose(); }
                catch { }
                _appIcon = null;
            }
            Program.ReleaseSingleInstanceForRestart();
        }

        private void ExitApp()
        {
            PrepareForRestart();
            Application.Current.Shutdown();
        }

        // ---------- Server Settings tab ----------





        private static bool KvFlag(Dictionary<string, string> map, string key, bool fallback)
        {
            string v;
            if (map == null || !map.TryGetValue(key, out v) || string.IsNullOrEmpty(v)) return fallback;
            return v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static string KvStr(Dictionary<string, string> map, string key, string fallback)
        {
            string v;
            if (map == null || !map.TryGetValue(key, out v) || v == null) return fallback;
            return v;
        }






        private sealed class SrvNotifySendTarget
        {
            public int ClientId;
            public string Label { get; set; }
        }

        private void ApplyNotifyClientsList(string payload)
        {
            if (SrvNotifySendToCombo == null) return;
            var prevId = -1;
            var prev = SrvNotifySendToCombo.SelectedItem as SrvNotifySendTarget;
            if (prev != null) prevId = prev.ClientId;

            var map = NotifyMessage.ParseKvMap(payload);
            var items = new List<SrvNotifySendTarget>();
            string allLabel;
            if (!map.TryGetValue("0", out allLabel) || string.IsNullOrEmpty(allLabel))
                allLabel = ClientUiStrings.T("NotifyAllClients");
            items.Add(new SrvNotifySendTarget { ClientId = 0, Label = allLabel });
            foreach (var kv in map)
            {
                int id;
                if (!int.TryParse(kv.Key, out id) || id <= 0) continue;
                items.Add(new SrvNotifySendTarget { ClientId = id, Label = kv.Value ?? ("#" + id) });
            }
            items.Sort((a, b) =>
            {
                if (a.ClientId == 0) return -1;
                if (b.ClientId == 0) return 1;
                return a.ClientId.CompareTo(b.ClientId);
            });

            SrvNotifySendToCombo.ItemsSource = items;
            SrvNotifySendTarget select = items[0];
            if (prevId > 0)
            {
                for (var i = 0; i < items.Count; i++)
                {
                    if (items[i].ClientId == prevId)
                    {
                        select = items[i];
                        break;
                    }
                }
            }
            SrvNotifySendToCombo.SelectedItem = select;
            SizeComboToLongestLabel(SrvNotifySendToCombo, items.ConvertAll(it => it.Label));
        }







        private void HandleNotifyTestAck(NotifyMessage msg)
        {
            var level = (msg.Level ?? "").Trim().ToLowerInvariant();
            var text = msg.Text ?? "";
            if (level == "error")
            {
                var err = string.IsNullOrEmpty(text)
                    ? ClientUiStrings.T("NotifyTestNoClients")
                    : text;
                SetNotifyTestResult(err, true);
                AppendActivityRow(null, "warning", err);
                return;
            }
            if (!string.IsNullOrEmpty(text))
                SetNotifyTestResult(text, false);
        }

        private void SetNotifyTestResult(string text, bool error)
        {
            if (NotifyTestResultText == null) return;
            NotifyTestResultText.Text = text ?? "";
            NotifyTestResultText.Visibility = string.IsNullOrEmpty(NotifyTestResultText.Text)
                ? Visibility.Collapsed
                : Visibility.Visible;
            NotifyTestResultText.Foreground = error ? _footerError : _footerMuted;
        }

        private Dictionary<string, string> CaptureServerSettingsMap()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            map["language"] = (SrvLanguageCombo != null ? SrvLanguageCombo.SelectedValue as string : null) ?? "en";
            map["themeMode"] = (SrvThemeCombo != null ? SrvThemeCombo.SelectedValue as string : null) ?? "system";
            map["logVerbose"] = "1";
            map["startWithWindows"] = SrvStartWithWindowsCheck != null && SrvStartWithWindowsCheck.IsChecked == true ? "1" : "0";
            map["minimizeToTray"] = SrvMinimizeToTrayCheck != null && SrvMinimizeToTrayCheck.IsChecked == true ? "1" : "0";
            map["minimizeOnClose"] = SrvMinimizeOnCloseCheck != null && SrvMinimizeOnCloseCheck.IsChecked == true ? "1" : "0";
            map["autoUpdate"] = SrvAutoUpdateCheck != null && SrvAutoUpdateCheck.IsChecked == true ? "1" : "0";
            map["keysRefreshSeconds"] = SrvKeysRefreshBox != null ? (SrvKeysRefreshBox.Text ?? "3").Trim() : "3";
            map["keysVinTipSeconds"] = SrvKeysVinTipBox != null ? (SrvKeysVinTipBox.Text ?? "10").Trim() : "10";
            map["keysStallMinutes"] = SrvKeysStallBox != null ? (SrvKeysStallBox.Text ?? "2").Trim() : "2";
            map["keysBalloon"] = SrvKeysBalloonCheck != null && SrvKeysBalloonCheck.IsChecked == true ? "1" : "0";
            map["keysVinBmwFailOnly"] = SrvKeysVinBmwFailOnlyCheck != null && SrvKeysVinBmwFailOnlyCheck.IsChecked == true ? "1" : "0";
            map["keysAutoCopyVin"] = SrvKeysAutoCopyCheck != null && SrvKeysAutoCopyCheck.IsChecked == true ? "1" : "0";
            map["keysStallAlert"] = SrvKeysStallAlertCheck != null && SrvKeysStallAlertCheck.IsChecked == true ? "1" : "0";
            map["browserPassHint"] = SrvBrowserPassHintCheck != null && SrvBrowserPassHintCheck.IsChecked == true ? "1" : "0";
            map["useSystemProxy"] = SrvUseSystemProxyCheck != null && SrvUseSystemProxyCheck.IsChecked == true ? "1" : "0";
            map["authCooldownMinutes"] = SrvAuthCooldownBox != null ? (SrvAuthCooldownBox.Text ?? "60").Trim() : "60";
            map["authRegisterBlockAfter"] = SrvAuthBlockAfterBox != null ? (SrvAuthBlockAfterBox.Text ?? "3").Trim() : "3";
            map["keyReaderLogLevel"] = (SrvKrLogLevelCombo != null ? SrvKrLogLevelCombo.SelectedValue as string : null) ?? "4";
            return map;
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



    }
}
