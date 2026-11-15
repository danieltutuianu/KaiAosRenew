using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
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
        private const int ActivityMaxRows = 500;
        private bool _aboutVisible;
        private bool _suppressTabChange;
        private int _tabIndexBeforeAbout = -1;
        private string _keysFilter = "all";
        private string _keysSearchText = "";
        private bool _keysFilterReady;
        private bool _servicesFail;
        private bool _servicesBlinkOn = true;
        private readonly DispatcherTimer _servicesBlinkTimer = new DispatcherTimer();

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
        private readonly DispatcherTimer _aosAckTimer = new DispatcherTimer();
        private bool _activityAlert;
        private bool _authGuardsBlinkOn = true;
        private bool _authCooldownOn;
        private bool _authBlockedOn;
        private readonly DispatcherTimer _authGuardsBlinkTimer = new DispatcherTimer();

        private static bool IsTestVinPlaceholder(string vin)
        {
            return !string.IsNullOrEmpty(vin)
                && string.Equals(vin.Trim(), TestVinPlaceholder, StringComparison.OrdinalIgnoreCase);
        }
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
        private bool _startedHiddenToTray;
        private bool _wantConnectUi;
        private bool _scanBusy;
        private DateTime _lastHeardUtc = DateTime.MinValue;
        private bool _offlineAlertShown;
        private string _lastVin;
        private string _trayStatus = "Offline";
        private bool _hasServerFooter;
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
                    if (v == null) return "1.0.0";
                    return v.Major + "." + v.Minor + "." + v.Build;
                }
                catch { return "1.0.0"; }
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
                    ActivityList.ItemsSource = _activityRows;
                _activityView = CollectionViewSource.GetDefaultView(_activityRows);
                if (_activityView != null)
                    _activityView.Filter = ShouldShowActivityRow;
                InitLogFilterCombo();
                UpdateKeysTodayBadge();
                UpdateAosSyncStatus();
                UpdateAosControlsEnabled();
                RefreshAuthGuardsPanel(false, "", false, "", "");
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
            InitTray();
            RebuildTrayMenu();
            SourceInitialized += MainWindow_SourceInitialized;
            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _statusTimer.Tick += StatusTimer_Tick;
            _statusTimer.Start();
            RefreshTrayStatus();
        }

        public void MarkStartedHiddenToTray()
        {
            _startedHiddenToTray = true;
        }

        private void ApplyLanguageToUi()
        {
            Title = ClientUiStrings.T("AppTitle");
            if (TitleText != null) TitleText.Text = ClientUiStrings.T("AppTitle");
            if (IpLabel != null) IpLabel.Text = ClientUiStrings.T("Ip");
            if (PortLabel != null) PortLabel.Text = ClientUiStrings.T("Port");
            if (ScanLanBtn != null) ScanLanBtn.Content = ClientUiStrings.T("ScanLan");
            if (ConnectBtn != null) ConnectBtn.Content = ClientUiStrings.T("Connect");
            if (DisconnectBtn != null) DisconnectBtn.Content = ClientUiStrings.T("Disconnect");
            if (AutoConnectCheck != null) AutoConnectCheck.Content = ClientUiStrings.T("AutoConnect");
            if (TabKeysHeaderText != null) TabKeysHeaderText.Text = ClientUiStrings.T("TabKeys");
            if (TabAosHeaderText != null) TabAosHeaderText.Text = ClientUiStrings.T("TabAos");
            if (TabActivityHeaderText != null) TabActivityHeaderText.Text = ClientUiStrings.T("TabActivity");
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
            if (ActivityClearBtn != null) ActivityClearBtn.Content = ClientUiStrings.T("ClearLog");
            if (ActivityColTime != null) ActivityColTime.Header = ClientUiStrings.T("KeysColTime");
            if (ActivityColLevel != null) ActivityColLevel.Header = ClientUiStrings.T("ActivityColLevel");
            if (ActivityColText != null) ActivityColText.Header = ClientUiStrings.T("ActivityColText");
            if (AosSectionSchedule != null) AosSectionSchedule.Text = ClientUiStrings.T("SectionSchedule");
            if (AosIntervalLabel != null) AosIntervalLabel.Text = ClientUiStrings.T("IntervalHours");
            if (AosSectionData != null) AosSectionData.Text = ClientUiStrings.T("SectionAos");
            if (AosUserLabel != null) AosUserLabel.Text = ClientUiStrings.T("AosUsername");
            if (AosPassLabel != null) AosPassLabel.Text = ClientUiStrings.T("AosPassword");
            if (AosShowPasswordCheck != null) AosShowPasswordCheck.Content = ClientUiStrings.T("ShowPassword");
            if (AosSaveBtn != null) AosSaveBtn.Content = ClientUiStrings.T("Save");
            if (AosCheckBrowserPassBtn != null) AosCheckBrowserPassBtn.Content = ClientUiStrings.T("CheckBrowserPass");
            if (AosOpenBmwBtn != null) AosOpenBmwBtn.Content = ClientUiStrings.T("OpenBmwSso");
            if (AosCheckTokenBtn != null) AosCheckTokenBtn.Content = ClientUiStrings.T("CheckToken");
            if (AosStartTimerBtn != null) AosStartTimerBtn.Content = ClientUiStrings.T("StartTimer");
            if (AosStopTimerBtn != null) AosStopTimerBtn.Content = ClientUiStrings.T("StopTimer");
            if (AuthGroup != null) AuthGroup.Header = ClientUiStrings.T("SettingsAuth");
            if (ForceRegisterBtn != null)
            {
                ForceRegisterBtn.Content = ClientUiStrings.T("ForceRegister");
                ForceRegisterBtn.ToolTip = ClientUiStrings.T("TipForceRegister");
            }
            if (ForceRegisterHintText != null)
                ForceRegisterHintText.Text = ClientUiStrings.T("ForceRegisterHint");
            if (AuthGuardsStatusTitle != null)
                AuthGuardsStatusTitle.Text = ClientUiStrings.T("AuthGuardsStatusTitle");
            if (AuthCooldownExplainText != null)
                AuthCooldownExplainText.Text = ClientUiStrings.T("AuthCooldownExplain");
            if (AuthRegisterBlockedExplainText != null)
                AuthRegisterBlockedExplainText.Text = ClientUiStrings.T("AuthRegisterBlockedExplain");
            RefreshAuthGuardsPanel(_authCooldownOn, null, _authBlockedOn, null, null);
            UpdateAosSyncStatus();
            UpdateAosButtonsVisibility();
            if (KeysEventsSection != null) KeysEventsSection.Text = ClientUiStrings.T("KeysRecent");
            if (KeysColTime != null) KeysColTime.Header = ClientUiStrings.T("KeysColTime");
            if (KeysColVin != null) KeysColVin.Header = ClientUiStrings.T("KeysColVin");
            if (KeysColPaint != null) KeysColPaint.Header = ClientUiStrings.T("KeysColPaint");
            if (KeysColUpholstery != null) KeysColUpholstery.Header = ClientUiStrings.T("KeysColUpholstery");
            if (KeysColILevel != null) KeysColILevel.Header = ClientUiStrings.T("KeysColILevel");
            if (KeysColReader != null) KeysColReader.Header = ClientUiStrings.T("KeysColReader");
            if (KeysColLocal != null) KeysColLocal.Header = ClientUiStrings.T("KeysColLocal");
            if (KeysColBmw != null) KeysColBmw.Header = ClientUiStrings.T("KeysColBmw");
            if (KeysColStatus != null) KeysColStatus.Header = ClientUiStrings.T("KeysColStatus");
            if (KeysColDetail != null) KeysColDetail.Header = ClientUiStrings.T("KeysColDetail");
            if (ConnectionGroup != null) ConnectionGroup.Header = ClientUiStrings.T("Connection");
            if (OfflineAfterLabel != null) OfflineAfterLabel.Text = ClientUiStrings.T("OfflineAfter");
            if (GeneralGroup != null) GeneralGroup.Header = ClientUiStrings.T("SettingsGeneral");
            if (LanguageLabel != null) LanguageLabel.Text = ClientUiStrings.T("Language");
            if (ThemeLabel != null) ThemeLabel.Text = ClientUiStrings.T("Theme");
            RefreshThemeComboItems();
            if (StartupGroup != null) StartupGroup.Header = ClientUiStrings.T("Startup");
            if (StartWithWindowsLabel != null) StartWithWindowsLabel.Text = ClientUiStrings.T("StartWithWindows");
            if (StartWithWindowsCheck != null) StartWithWindowsCheck.Content = ClientUiStrings.T("Yes");
            if (MinimizeToTrayLabel != null) MinimizeToTrayLabel.Text = ClientUiStrings.T("MinimizeToTray");
            if (MinimizeToTrayCheck != null) MinimizeToTrayCheck.Content = ClientUiStrings.T("Yes");
            if (MinimizeOnCloseLabel != null) MinimizeOnCloseLabel.Text = ClientUiStrings.T("MinimizeOnClose");
            if (MinimizeOnCloseCheck != null) MinimizeOnCloseCheck.Content = ClientUiStrings.T("MinimizeOnCloseYes");
            if (AutoUpdateLabel != null) AutoUpdateLabel.Text = ClientUiStrings.T("AutoUpdate");
            if (AutoUpdateCheck != null)
            {
                AutoUpdateCheck.Content = ClientUiStrings.T("Yes");
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
            if (AutoCopyVinCheck != null) AutoCopyVinCheck.Content = ClientUiStrings.T("AutoCopyVin");
            if (ShowAlertTipsCheck != null) ShowAlertTipsCheck.Content = ClientUiStrings.T("ShowAlertTips");
            if (TestBalloonTipsBtn != null) TestBalloonTipsBtn.Content = ClientUiStrings.T("TrayTestTips");
            if (KeysRefreshBtn != null) KeysRefreshBtn.Content = ClientUiStrings.T("KeysRefresh");
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
            if (AboutChangelogLabel != null)
                AboutChangelogLabel.Text = ClientUiStrings.T("AboutChangelogLabel");
            if (_aboutVisible)
                LoadAboutChangelog();
            UpdateKeysTodayBadge();
            if (!_hasServerFooter)
                ShowDisconnectedFooter();
        }

        private void ApplyConnectionUi()
        {
            RefreshHostCombo();
            SetHostText(_settings.Host);
            if (PortBox != null) PortBox.Text = _settings.Port;
            if (AutoConnectCheck != null) AutoConnectCheck.IsChecked = _settings.AutoConnect;
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
            try { await Task.Delay(1500); }
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
            try { await RefreshUpdateFeedAsync(true); }
            catch { }
            if (autoApply && peeked != null && !_clientUpdateBusy)
            {
                _clientUpdateBusy = true;
                try
                {
                    SendClientUpdating(UpdateService.FormatUpdateVersion(peeked));
                    await UpdateService.ApplyUpdateAsync(this, peeked, ClientUiStrings.T, false);
                }
                catch
                {
                    SendClientUpdateDone();
                }
                finally
                {
                    _clientUpdateBusy = false;
                }
            }
        }

        private void ShowPendingUpdate(UpdateInfo update)
        {
            _pendingUpdate = update;
            if (update == null)
            {
                if (UpdateAvailableText != null)
                    UpdateAvailableText.Visibility = Visibility.Collapsed;
                if (FooterUpdateBtn != null)
                {
                    FooterUpdateBtn.Visibility = Visibility.Collapsed;
                    FooterUpdateBtn.ToolTip = null;
                }
                RefreshTrayOutdatedIcon();
                return;
            }
            var ver = UpdateService.FormatUpdateVersion(update);
            if (UpdateAvailableText != null)
            {
                UpdateAvailableText.Text = string.Format(ClientUiStrings.T("UpdateAvailableShort"), ver);
                UpdateAvailableText.Visibility = Visibility.Visible;
            }
            if (FooterUpdateBtn != null)
            {
                FooterUpdateBtn.Content = string.Format(ClientUiStrings.T("UpdateClientBtn"), ver);
                FooterUpdateBtn.ToolTip = string.Format(ClientUiStrings.T("UpdateChannelTooltip"), UpdateService.ClientChannel);
                FooterUpdateBtn.Visibility = Visibility.Visible;
                FooterUpdateBtn.IsEnabled = !_clientUpdateBusy;
            }
            RefreshTrayOutdatedIcon();
        }

        private async void FooterUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (_pendingUpdate == null || _clientUpdateBusy) return;
            var ver = UpdateService.FormatUpdateVersion(_pendingUpdate);
            var confirm = string.Format(ClientUiStrings.T("UpdateAvailable"), ver);
            var result = MessageBox.Show(this, confirm, ClientUiStrings.T("CheckForUpdates"),
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            _clientUpdateBusy = true;
            if (FooterUpdateBtn != null) FooterUpdateBtn.IsEnabled = false;
            try
            {
                SendClientUpdating(ver);
                await UpdateService.ApplyUpdateAsync(this, _pendingUpdate, ClientUiStrings.T, false);
            }
            catch (Exception ex)
            {
                SendClientUpdateDone();
                MessageBox.Show(this, string.Format(ClientUiStrings.T("UpdateFailed"), ex.Message),
                    ClientUiStrings.T("CheckForUpdates"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                _clientUpdateBusy = false;
                if (FooterUpdateBtn != null && _pendingUpdate != null)
                    FooterUpdateBtn.IsEnabled = true;
            }
        }

        private void SendClientUpdating(string version)
        {
            if (_client == null || !_client.IsConnected) return;
            try
            {
                _client.Send(new NotifyMessage
                {
                    Kind = "clientUpdating",
                    Level = "info",
                    Title = "KAI - AOS Renew - Client",
                    Text = version ?? ""
                });
            }
            catch { }
        }

        private void SendClientUpdateDone()
        {
            if (_client == null || !_client.IsConnected) return;
            try
            {
                _client.Send(new NotifyMessage
                {
                    Kind = "clientUpdateDone",
                    Level = "info",
                    Title = "KAI - AOS Renew - Client",
                    Text = ""
                });
            }
            catch { }
        }

        private async Task RefreshUpdateFeedAsync(bool sendToServer)
        {
            if (_serverUpdateCheckBusy) return;
            _serverUpdateCheckBusy = true;
            try
            {
                string serverVer = "";
                string clientVer = "";
                try { serverVer = await UpdateService.PeekLatestChannelVersionAsync(UpdateService.ServerChannel) ?? ""; }
                catch { serverVer = ""; }
                try { clientVer = await UpdateService.PeekLatestChannelVersionAsync(UpdateService.ClientChannel) ?? ""; }
                catch { clientVer = ""; }
                _feedServerVer = serverVer;
                _feedClientVer = clientVer;
                RefreshServerUpdateFromFeed();
                await ApplyFeedToSelfAsync();
                if (sendToServer)
                    SendUpdateFeed();
            }
            finally
            {
                _serverUpdateCheckBusy = false;
            }
        }

        private void ApplyCachedFeedVersions(string serverVer, string clientVer)
        {
            _feedServerVer = serverVer ?? "";
            _feedClientVer = clientVer ?? "";
            RefreshServerUpdateFromFeed();
            var ignore = ApplyFeedToSelfAsync();
        }

        private async Task ApplyFeedToSelfAsync()
        {
            if (string.IsNullOrWhiteSpace(_feedClientVer)) return;
            if (!UpdateService.IsNewerVersion(_feedClientVer, VersionDisplay)) return;
            try
            {
                var update = await UpdateService.PeekAsync();
                if (update != null)
                    ShowPendingUpdate(update);
            }
            catch { }
        }

        private void SendUpdateFeed()
        {
            if (_client == null || !_client.IsConnected) return;
            if (string.IsNullOrEmpty(_feedServerVer) && string.IsNullOrEmpty(_feedClientVer)) return;
            try
            {
                _client.Send(new NotifyMessage
                {
                    Kind = "updateFeed",
                    Level = "info",
                    Title = "KAI - AOS Renew - Client",
                    Text = NotifyMessage.EncodeUpdateFeed(_feedServerVer, _feedClientVer)
                });
            }
            catch { }
        }

        private void RefreshServerUpdateFromFeed()
        {
            if (!string.IsNullOrEmpty(_feedServerVer)
                && !string.IsNullOrEmpty(_serverVersionFromFooter)
                && UpdateService.IsNewerVersion(_feedServerVer, _serverVersionFromFooter))
                _latestServerReleaseVer = _feedServerVer;
            else
                _latestServerReleaseVer = "";
            RefreshServerUpdateUi();
        }

        private void RefreshServerUpdateUi()
        {
            var show = !_awaitingServerUpdateReconnect
                && !string.IsNullOrEmpty(_latestServerReleaseVer)
                && !string.IsNullOrEmpty(_serverVersionFromFooter)
                && UpdateService.IsNewerVersion(_latestServerReleaseVer, _serverVersionFromFooter);
            if (ServerUpdateAvailableText != null)
            {
                if (show)
                {
                    ServerUpdateAvailableText.Text = string.Format(
                        ClientUiStrings.T("UpdateServerAvailableShort"), _latestServerReleaseVer);
                    ServerUpdateAvailableText.Visibility = Visibility.Visible;
                }
                else
                {
                    ServerUpdateAvailableText.Visibility = Visibility.Collapsed;
                }
            }
            if (FooterServerUpdateBtn != null)
            {
                FooterServerUpdateBtn.Content = show
                    ? string.Format(ClientUiStrings.T("UpdateServerBtn"), _latestServerReleaseVer)
                    : ClientUiStrings.T("UpdateServer");
                FooterServerUpdateBtn.ToolTip = show
                    ? string.Format(ClientUiStrings.T("UpdateChannelTooltip"), UpdateService.ServerChannel)
                    : null;
                FooterServerUpdateBtn.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                FooterServerUpdateBtn.IsEnabled = show;
            }
            RefreshTrayOutdatedIcon();
        }

        private void BeginAwaitServerUpdate(string targetVer)
        {
            _awaitingServerUpdateReconnect = true;
            if (!string.IsNullOrEmpty(targetVer))
                _pendingServerUpdateVer = targetVer;
            else if (string.IsNullOrEmpty(_pendingServerUpdateVer))
                _pendingServerUpdateVer = _latestServerReleaseVer ?? "";
            _latestServerReleaseVer = "";
            if (ServerUpdateAvailableText != null)
                ServerUpdateAvailableText.Visibility = Visibility.Collapsed;
            if (FooterServerUpdateBtn != null)
            {
                FooterServerUpdateBtn.Visibility = Visibility.Collapsed;
                FooterServerUpdateBtn.IsEnabled = false;
            }
            SetConnStatus(ClientUiStrings.T("ServerRestarting"));
            RefreshTrayOutdatedIcon();
        }

        private void HandleServerUpdateFailed(string error)
        {
            _awaitingServerUpdateReconnect = false;
            _pendingServerUpdateVer = "";
            SetConnStatus("");
            var detail = string.IsNullOrEmpty(error) ? "?" : error;
            AppendActivityRow(null, "error",
                string.Format(ClientUiStrings.T("UpdateServerFailed"), detail));
            JumpToActivityWithAlert();
            RefreshServerUpdateFromFeed();
        }

        private void FooterServerUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_latestServerReleaseVer)) return;
            if (_client == null || !_client.IsConnected)
            {
                MessageBox.Show(this, ClientUiStrings.T("UpdateServerSendFail"),
                    ClientUiStrings.T("UpdateServer"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var targetVer = _latestServerReleaseVer;
            var msg = string.Format(ClientUiStrings.T("UpdateServerConfirm"), targetVer);
            var result = MessageBox.Show(this, msg, ClientUiStrings.T("UpdateServer"),
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            if (FooterServerUpdateBtn != null) FooterServerUpdateBtn.IsEnabled = false;
            _pendingServerUpdateVer = targetVer;
            var ok = _client.Send(new NotifyMessage
            {
                Kind = "serverUpdate",
                Level = "info",
                Title = "KAI - AOS Renew - Client",
                Text = targetVer ?? ""
            });
            if (!ok)
            {
                _pendingServerUpdateVer = "";
                if (FooterServerUpdateBtn != null) FooterServerUpdateBtn.IsEnabled = true;
                MessageBox.Show(this, ClientUiStrings.T("UpdateServerSendFail"),
                    ClientUiStrings.T("UpdateServer"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            BeginAosAckWait();
        }

        private async Task FinishServerUpdateReconnectAsync()
        {
            if (!_awaitingServerUpdateReconnect) return;
            SetConnStatus(ClientUiStrings.T("UpdateServerVerifying"));
            await RefreshUpdateFeedAsync(true);
            if (!_awaitingServerUpdateReconnect) return;

            var expected = _pendingServerUpdateVer ?? "";
            var actual = _serverVersionFromFooter ?? "";
            _awaitingServerUpdateReconnect = false;
            _pendingServerUpdateVer = "";

            if (!string.IsNullOrEmpty(actual)
                && (string.IsNullOrEmpty(expected)
                    || string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)
                    || !UpdateService.IsNewerVersion(expected, actual)))
            {
                AppendActivityRow(null, "success",
                    string.Format(ClientUiStrings.T("UpdateServerDone"), actual));
                SetConnStatus("");
            }
            else
            {
                AppendActivityRow(null, "warning",
                    string.Format(ClientUiStrings.T("UpdateServerStillOld"),
                        string.IsNullOrEmpty(actual) ? "?" : actual));
                SetConnStatus("");
                JumpToActivityWithAlert();
            }
            RefreshServerUpdateUi();
        }

        private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
        {
            if (CheckForUpdatesBtn != null) CheckForUpdatesBtn.IsEnabled = false;
            try
            {
                var update = await UpdateService.PeekAsync();
                ShowPendingUpdate(update);
                if (update == null)
                {
                    MessageBox.Show(this, ClientUiStrings.T("UpdateNone"),
                        ClientUiStrings.T("CheckForUpdates"), MessageBoxButton.OK, MessageBoxImage.Information);
                }
                try { await RefreshUpdateFeedAsync(true); }
                catch { }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(ClientUiStrings.T("UpdateFailed"), ex.Message),
                    ClientUiStrings.T("CheckForUpdates"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                if (CheckForUpdatesBtn != null) CheckForUpdatesBtn.IsEnabled = true;
            }
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

        private void ScanLan_Click(object sender, RoutedEventArgs e)
        {
            if (_scanBusy)
            {
                SetConnStatus(ClientUiStrings.T("ScanBusy"));
                return;
            }
            _scanBusy = true;
            if (ScanLanBtn != null) ScanLanBtn.IsEnabled = false;
            SetConnStatus(ClientUiStrings.T("Scanning"));
            int port;
            if (!int.TryParse((PortBox != null ? PortBox.Text : _settings.Port) ?? "", out port) || port < 1 || port > 65535)
                port = 17890;
            _settings.Port = port.ToString();
            if (PortBox != null) PortBox.Text = _settings.Port;

            Task.Run(delegate
            {
                string found = null;
                try
                {
                    found = LanServerDiscovery.FindFirstOpenHost(port, CancellationToken.None);
                }
                catch { }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _scanBusy = false;
                    if (ScanLanBtn != null) ScanLanBtn.IsEnabled = true;
                    if (!string.IsNullOrEmpty(found))
                    {
                        _settings.Host = found;
                        _settings.Port = port.ToString();
                        if (PortBox != null) PortBox.Text = _settings.Port;
                        _settings.PushRecentHost(found);
                        SetHostText(found);
                        RefreshHostCombo();
                        SetHostText(found);
                        _settings.Save();
                        SetConnStatus(string.Format(ClientUiStrings.T("FoundServerConnect"), found, port));
                        DoConnect(found, port);
                    }
                    else
                    {
                        SetConnStatus(ClientUiStrings.T("NoServerFound"));
                        RefreshTrayStatus();
                    }
                }));
            });
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

        private void HostPort_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_loadingUi) return;
            CaptureConnectionFields();
            _settings.Save();
            RefreshHostCombo();
        }

        private void AutoConnect_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingUi) return;
            _settings.AutoConnect = AutoConnectCheck != null && AutoConnectCheck.IsChecked == true;
            CaptureConnectionFields();
            _settings.Save();
        }

        private void CaptureConnectionFields()
        {
            _settings.Host = GetHostText();
            if (PortBox != null)
                _settings.Port = (PortBox.Text ?? "").Trim();
            if (AutoConnectCheck != null)
                _settings.AutoConnect = AutoConnectCheck.IsChecked == true;
            _settings.Normalize();
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

        private void LoadAboutChangelog()
        {
            if (AboutChangelogBox == null) return;
            try
            {
                var path = FindChangelogPath();
                if (path != null && File.Exists(path))
                    AboutChangelogBox.Text = File.ReadAllText(path);
                else
                    AboutChangelogBox.Text = ClientUiStrings.T("AboutChangelogMissing");
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
                // Dev: KaiAosNotifyClient\bin\Release -> repo root
                var probe = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "CHANGELOG.md"));
                if (File.Exists(probe)) return probe;
                probe = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "CHANGELOG.md"));
                if (File.Exists(probe)) return probe;
            }
            catch { }
            return null;
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
            if (AutoCopyVinCheck != null)
                AutoCopyVinCheck.IsChecked = _settings.AutoCopyVin;
            if (ShowAlertTipsCheck != null)
                ShowAlertTipsCheck.IsChecked = _settings.ShowAlertTips;
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
                var theme = ClientThemeManager.NormalizeMode(_settings.Theme);
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
            if (AutoCopyVinCheck != null)
                _settings.AutoCopyVin = AutoCopyVinCheck.IsChecked == true;
            if (ShowAlertTipsCheck != null)
                _settings.ShowAlertTips = ShowAlertTipsCheck.IsChecked == true;
            int sec = 8;
            if (VinTipSecondsBox == null || !int.TryParse((VinTipSecondsBox.Text ?? "").Trim(), out sec))
                sec = 8;
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
                ClientThemeManager.Apply(_settings.Theme);
                ApplyFooterChrome(ClientThemeManager.IsNight);
            }
            RefreshTrayStatus();
        }

        private void SettingsCheck_Changed(object sender, RoutedEventArgs e)
        {
            PersistSettingsFromUi();
        }

        private void SettingsField_Changed(object sender, RoutedEventArgs e)
        {
            PersistSettingsFromUi();
        }

        private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            PersistSettingsFromUi();
        }

        private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            PersistSettingsFromUi();
        }

        private void UpdateKeysTodayBadge()
        {
            if (KeysTodayBadge == null) return;
            var today = DateTime.Today;
            var count = 0;
            foreach (var row in _keyAllRows)
            {
                if (row != null && row.DetectedLocal.Date == today)
                    count++;
            }
            KeysTodayBadge.Text = count > 0
                ? string.Format(ClientUiStrings.T("KeysTodayBadge"), count)
                : "";
        }

        private void InitKeysFilterCombo()
        {
            if (KeysFilterCombo == null) return;
            var prev = _keysFilter;
            if (KeysFilterCombo.SelectedValue is string)
                prev = (string)KeysFilterCombo.SelectedValue;
            _keysFilterReady = false;
            KeysFilterCombo.Items.Clear();
            KeysFilterCombo.DisplayMemberPath = "Name";
            KeysFilterCombo.SelectedValuePath = "Code";
            KeysFilterCombo.Items.Add(new LangItem { Code = "all", Name = ClientUiStrings.T("KeysFilterAll") });
            KeysFilterCombo.Items.Add(new LangItem { Code = "today", Name = ClientUiStrings.T("KeysFilterToday") });
            KeysFilterCombo.Items.Add(new LangItem { Code = "failed", Name = ClientUiStrings.T("KeysFilterFailed") });
            KeysFilterCombo.Items.Add(new LangItem { Code = "bmw", Name = ClientUiStrings.T("KeysFilterBmwOk") });
            KeysFilterCombo.Items.Add(new LangItem { Code = "local", Name = ClientUiStrings.T("KeysFilterLocal") });
            KeysFilterCombo.Items.Add(new LangItem { Code = "waiting", Name = ClientUiStrings.T("KeysFilterWaiting") });
            KeysFilterCombo.SelectedValue = prev;
            if (KeysFilterCombo.SelectedValue == null)
                KeysFilterCombo.SelectedValue = "all";
            _keysFilterReady = true;
            ApplyKeysFilter();
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

        private void KeysRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (_client != null && _client.IsConnected)
                SendAosCmd("refreshKeys");
            ApplyKeysFilter();
            UpdateKeysRetryButton();
        }

        private void KeysRetryBmw_Click(object sender, RoutedEventArgs e)
        {
            if (_client == null || !_client.IsConnected) return;
            SendAosCmd("retryBmw");
        }

        private void UpdateKeysRetryButton()
        {
            if (KeysRetryBmwBtn == null) return;
            var on = _client != null && _client.IsConnected;
            var newest = _keyAllRows.Count > 0 ? _keyAllRows[0] : null;
            var failed = newest != null && string.Equals(newest.Status, "Failed", StringComparison.OrdinalIgnoreCase);
            KeysRetryBmwBtn.IsEnabled = on && failed;
        }

        private void ApplyKeysFilter()
        {
            if (KeysEventsList == null) return;
            IEnumerable<ClientKeyRow> src = _keyAllRows;
            switch (_keysFilter)
            {
                case "today":
                    {
                        var today = DateTime.Today;
                        var list = new List<ClientKeyRow>();
                        foreach (var r in src)
                            if (r != null && r.DetectedLocal.Date == today)
                                list.Add(r);
                        src = list;
                        break;
                    }
                case "failed":
                    src = FilterByStatus(src, "Failed");
                    break;
                case "bmw":
                    src = FilterByStatus(src, "BmwOk");
                    break;
                case "local":
                    src = FilterByStatus(src, "LocalOk");
                    break;
                case "waiting":
                    src = FilterByStatus(src, "Waiting");
                    break;
            }
            if (!string.IsNullOrEmpty(_keysSearchText))
            {
                var q = _keysSearchText;
                var list = new List<ClientKeyRow>();
                foreach (var r in src)
                {
                    if (r == null) continue;
                    if (ContainsIgnoreCase(r.Vin, q)
                        || ContainsIgnoreCase(r.Paint, q)
                        || ContainsIgnoreCase(r.ILevelCurrent, q)
                        || ContainsIgnoreCase(r.Detail, q))
                        list.Add(r);
                }
                src = list;
            }
            _keyRows.Clear();
            foreach (var r in src)
            {
                if (r == null) continue;
                _keyRows.Add(r);
                if (_keyRows.Count >= 10) break;
            }
            UpdateKeysTodayBadge();
            UpdateKeysRetryButton();
            ReapplyKeysRowHighlightIfNeeded();
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
            if (_aboutVisible && TabDummy != null && !TabDummy.IsSelected)
            {
                _aboutVisible = false;
                if (AboutPanel != null) AboutPanel.Visibility = Visibility.Collapsed;
                if (AboutTabBtn != null) AboutTabBtn.Tag = null;
            }
            if (!_aboutVisible && TabActivity != null && TabActivity.IsSelected)
                ClearActivityAlert();
            UpdateAosButtonsVisibility();
        }

        private void UpdateAosButtonsVisibility()
        {
            if (ButtonsBorder == null) return;
            var show = !_aboutVisible && TabAos != null && TabAos.IsSelected;
            ButtonsBorder.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateAosControlsEnabled()
        {
            var on = _client != null && _client.IsConnected;
            if (AosIntervalBox != null) AosIntervalBox.IsEnabled = on;
            if (AosEmailBox != null) AosEmailBox.IsEnabled = on;
            if (AosPasswordBox != null) AosPasswordBox.IsEnabled = on;
            if (AosPasswordPlainBox != null) AosPasswordPlainBox.IsEnabled = on;
            if (AosShowPasswordCheck != null) AosShowPasswordCheck.IsEnabled = on;
            if (AosSaveBtn != null) AosSaveBtn.IsEnabled = on;
            if (AosCheckBrowserPassBtn != null) AosCheckBrowserPassBtn.IsEnabled = true;
            if (AosCheckTokenBtn != null) AosCheckTokenBtn.IsEnabled = on;
            if (AosStartTimerBtn != null) AosStartTimerBtn.IsEnabled = on;
            if (AosStopTimerBtn != null) AosStopTimerBtn.IsEnabled = on;
            if (ForceRegisterBtn != null) ForceRegisterBtn.IsEnabled = on;
            if (AosOpenBmwBtn != null) AosOpenBmwBtn.IsEnabled = true;
            if (KaiRefreshBtn != null) KaiRefreshBtn.IsEnabled = on;
            if (KaiFixServicesBtn != null) KaiFixServicesBtn.IsEnabled = on && !_kaiBusy;
            if (KaiStopServicesBtn != null) KaiStopServicesBtn.IsEnabled = on && !_kaiBusy;
            UpdateKeysRetryButton();
        }

        private void UpdateAosSyncStatus()
        {
            if (AosSyncStatusText == null) return;
            if (_client == null || !_client.IsConnected)
            {
                AosSyncStatusText.Text = ClientUiStrings.T("AosSyncOffline");
                AosSyncStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x8C, 0x8C, 0x8C));
                return;
            }
            if (_aosAwaitingAck || _aosDirty)
            {
                AosSyncStatusText.Text = ClientUiStrings.T("AosSyncPending");
                AosSyncStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xB2, 0x6A, 0x00));
                return;
            }
            AosSyncStatusText.Text = ClientUiStrings.T("AosSyncSynced");
            AosSyncStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
        }

        private void ApplyAosSync(string payload)
        {
            int hours;
            string email;
            string password;
            if (!NotifyMessage.TryParseAosSync(payload, out hours, out email, out password))
                return;
            _loadingAosUi = true;
            try
            {
                if (AosIntervalBox != null) AosIntervalBox.Text = hours.ToString();
                if (AosEmailBox != null) AosEmailBox.Text = email ?? "";
                SetAosPasswordUi(password ?? "");
                _aosDirty = false;
                CancelAosAckWait();
                UpdateAosSyncStatus();
            }
            finally
            {
                _loadingAosUi = false;
            }
        }

        private void HandleAosAck(NotifyMessage msg)
        {
            CancelAosAckWait();
            SetKaiBusy(false);
            if (FooterServerUpdateBtn != null) FooterServerUpdateBtn.IsEnabled = true;
            var level = (msg.Level ?? "").Trim().ToLowerInvariant();
            var text = msg.Text ?? "";
            if (level == "error")
            {
                if (!string.IsNullOrEmpty(_pendingServerUpdateVer) || _awaitingServerUpdateReconnect)
                {
                    _pendingServerUpdateVer = "";
                    _awaitingServerUpdateReconnect = false;
                    RefreshServerUpdateFromFeed();
                }
                _aosDirty = true;
                UpdateAosSyncStatus();
                AppendActivityRow(null, "error", "AOS: " + (string.IsNullOrEmpty(text) ? "failed" : text));
                JumpToActivityWithAlert();
                return;
            }
            _aosDirty = false;
            UpdateAosSyncStatus();
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
            CancelAosAckWait();
            _aosDirty = true;
            if (FooterServerUpdateBtn != null) FooterServerUpdateBtn.IsEnabled = true;
            UpdateAosSyncStatus();
            AppendActivityRow(null, "error", ClientUiStrings.T("AosSyncTimeout"));
            JumpToActivityWithAlert();
        }

        private void CancelAosAckWait()
        {
            _aosAwaitingAck = false;
            try { _aosAckTimer.Stop(); }
            catch { }
        }

        private void BeginAosAckWait()
        {
            _aosAwaitingAck = true;
            UpdateAosSyncStatus();
            try
            {
                _aosAckTimer.Stop();
                _aosAckTimer.Start();
            }
            catch { }
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

        private void UpdateActivityAlertDot()
        {
            if (ActivityTabAlertDot == null) return;
            ActivityTabAlertDot.Visibility = _activityAlert ? Visibility.Visible : Visibility.Collapsed;
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

        private void SendAosApply()
        {
            if (_loadingAosUi || _loadingUi) return;
            if (_client == null || !_client.IsConnected) return;
            int hours = 1;
            if (AosIntervalBox == null || !int.TryParse((AosIntervalBox.Text ?? "").Trim(), out hours) || hours < 1)
                hours = 1;
            if (hours > 168) hours = 168;
            var email = AosEmailBox != null ? (AosEmailBox.Text ?? "").Trim() : "";
            var pass = GetAosPasswordUi();
            var payload = NotifyMessage.EncodeAosSync(hours, email, pass);
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
            BeginAosAckWait();
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
            BeginAosAckWait();
        }

        private void AosField_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loadingAosUi || _loadingUi) return;
            _aosDirty = true;
            UpdateAosSyncStatus();
        }

        private void AosPassword_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingAosUi || _loadingUi) return;
            _aosDirty = true;
            UpdateAosSyncStatus();
        }

        private void AosField_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_loadingAosUi || _loadingUi || !_aosDirty) return;
            if (_client == null || !_client.IsConnected) return;
            SendAosApply();
        }

        private void AosSave_Click(object sender, RoutedEventArgs e)
        {
            SendAosApply();
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
            if (string.IsNullOrEmpty(email))
            {
                MessageBox.Show(this, ClientUiStrings.T("MissingEmail"), ClientUiStrings.T("AppTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

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

            var when = FormatBrowserSavedWhen(found.LastSavedLocal);
            var msg = string.Format(ClientUiStrings.T("BrowserPassCheckOk"), found.BrowserName, when);
            var hint = string.Format(ClientUiStrings.T("BrowserPassHintNoDate"), found.BrowserName);
            var site = SiteForClipboard(found.SourceUrl);
            var copiedHint = "";
            if (!string.IsNullOrEmpty(site))
            {
                try
                {
                    Clipboard.SetText(site);
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

            if (dlg.FillAndSaveRequested)
            {
                if (!string.IsNullOrEmpty(found.Password))
                    SetAosPasswordUi(found.Password);
                if (_client != null && _client.IsConnected)
                    SendAosApply();
                else
                {
                    _aosDirty = true;
                    UpdateAosSyncStatus();
                }
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

        private static string FormatBrowserSavedWhen(DateTime? local)
        {
            if (!local.HasValue)
                return "?";
            var age = DateTime.Now - local.Value;
            if (age < TimeSpan.Zero)
                age = TimeSpan.Zero;
            return string.Format(ClientUiStrings.T("TimeAgo"), FormatAge(age));
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

        private void AosOpenBmw_Click(object sender, RoutedEventArgs e)
        {
            try { Process.Start(BmwAosPortalUrl); }
            catch (Exception ex)
            {
                AppendActivityRow(null, "error", ex.Message);
                JumpToActivityWithAlert();
            }
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

        private void ConnectBtn_Click(object sender, RoutedEventArgs e)
        {
            DoConnect();
        }

        private void DoConnect(string hostOverride = null, int portOverride = 0)
        {
            if (!string.IsNullOrWhiteSpace(hostOverride))
            {
                _settings.Host = hostOverride.Trim();
                SetHostText(_settings.Host);
            }
            else
                CaptureConnectionFields();

            if (string.IsNullOrWhiteSpace(_settings.Host))
            {
                SetConnStatus(ClientUiStrings.T("EnterIp"));
                RefreshTrayStatus();
                return;
            }

            int port = portOverride;
            if (port < 1 || port > 65535)
            {
                if (!int.TryParse(_settings.Port, out port) || port < 1 || port > 65535)
                    port = 17890;
            }
            _settings.Port = port.ToString();
            if (PortBox != null) PortBox.Text = _settings.Port;
            _settings.PushRecentHost(_settings.Host);
            RefreshHostCombo();
            SetHostText(_settings.Host);
            _settings.Save();
            _wantConnectUi = true;
            _offlineAlertShown = false;
            _client.Connect(_settings.Host, port);
            ConnectBtn.IsEnabled = false;
            DisconnectBtn.IsEnabled = true;
            RefreshTrayStatus();
        }

        private void DisconnectBtn_Click(object sender, RoutedEventArgs e)
        {
            _wantConnectUi = false;
            _client.Disconnect();
            ConnectBtn.IsEnabled = true;
            DisconnectBtn.IsEnabled = false;
            RefreshTrayStatus();
        }

        private void OnClientStatus(string status)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!string.IsNullOrEmpty(status))
                {
                    var steady = status.StartsWith("Connected", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(status, "Disconnected", StringComparison.OrdinalIgnoreCase);
                    SetConnStatus(steady ? "" : status);
                }
                if (!_client.IsConnected)
                {
                    ShowDisconnectedFooter();
                    if (_awaitingServerUpdateReconnect)
                        SetConnStatus(ClientUiStrings.T("UpdateServerWaiting"));
                }
                RefreshTrayStatus();
            }));
        }

        private void OnClientConnected()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ConnectBtn.IsEnabled = false;
                DisconnectBtn.IsEnabled = true;
                _lastHeardUtc = DateTime.UtcNow;
                _offlineAlertShown = false;
                if (_awaitingServerUpdateReconnect)
                    SetConnStatus(ClientUiStrings.T("UpdateServerVerifying"));
                else
                    SetConnStatus("");
                if (!_hasServerFooter)
                    ApplyStatusMode("ok", ClientUiStrings.T("Connected"));
                CaptureConnectionFields();
                if (!string.IsNullOrWhiteSpace(_settings.Host))
                {
                    _settings.PushRecentHost(_settings.Host);
                    RefreshHostCombo();
                    SetHostText(_settings.Host);
                    _settings.Save();
                }
                UpdateAosControlsEnabled();
                RefreshTrayStatus();
                SendUpdateFeed();
            }));
        }

        private void OnClientDisconnected()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_wantConnectUi || _client.WantConnect)
                {
                    ConnectBtn.IsEnabled = false;
                    DisconnectBtn.IsEnabled = true;
                }
                else
                {
                    ConnectBtn.IsEnabled = true;
                    DisconnectBtn.IsEnabled = false;
                }
                CancelAosAckWait();
                SetKaiBusy(false);
                if (_awaitingServerUpdateReconnect)
                    SetConnStatus(ClientUiStrings.T("UpdateServerWaiting"));
                UpdateAosControlsEnabled();
                UpdateAosSyncStatus();
                RefreshTrayStatus();
            }));
        }

        private void OnMessageReceived(NotifyMessage msg)
        {
            if (msg == null) return;
            Dispatcher.BeginInvoke(new Action(() => HandleMessage(msg)));
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
            if (kind == "activity")
            {
                AppendActivityRow(msg.At, msg.Level ?? "info", msg.Text ?? "");
                MaybeRaiseActivityAlert(msg.Level);
                RefreshTrayStatus();
                return;
            }
            if (kind == "activityclear")
            {
                _activityRows.Clear();
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
            if (kind == "aosack")
            {
                HandleAosAck(msg);
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
                var row = NotifyMessage.FromNotifyMessage(msg);
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
            if (kind == "vin" || (!string.IsNullOrEmpty(msg.Vin) && level == "info" && kind != "warning" && kind != "error"))
            {
                if (!_settings.ShowVinTips)
                {
                    RefreshTrayStatus();
                    return;
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

        private void ApplyKeySync(string text)
        {
            // Authoritative: replace local list with server snapshot (empty clears phantoms).
            // Disconnect does not send keySync, so history survives offline.
            _keyAllRows.Clear();
            if (!string.IsNullOrWhiteSpace(text))
            {
                var rows = NotifyMessage.ParseKeySync(text);
                // Backward compat: old vinSync was comma-separated VINs only
                if (rows.Count == 0 && text.IndexOf('|') < 0)
                {
                    var parts = text.Split(',');
                    var now = DateTime.Now;
                    foreach (var part in parts)
                    {
                        var vin = (part ?? "").Trim();
                        if (string.IsNullOrEmpty(vin)) continue;
                        _keyAllRows.Add(new ClientKeyRow
                        {
                            DetectedLocal = now,
                            Vin = vin,
                            Status = "Waiting"
                        });
                    }
                }
                else
                {
                    foreach (var row in rows)
                    {
                        if (row == null || string.IsNullOrEmpty(row.Vin)) continue;
                        _keyAllRows.Add(row);
                    }
                }
            }
            SortAndTrimKeyRows();
            PersistKeysHistory();
            _lastVin = _keyAllRows.Count > 0 ? _keyAllRows[0].Vin : null;
            ApplyKeysFilter();
            UpdateKeysTodayBadge();
            UpdateKeysAlert();
        }

        private void LoadKeysHistory()
        {
            try
            {
                var loaded = ClientKeyHistory.Load();
                _keyAllRows.Clear();
                foreach (var row in loaded)
                {
                    if (row == null) continue;
                    _keyAllRows.Add(row);
                }
                SortAndTrimKeyRows();
                _lastVin = _keyAllRows.Count > 0 ? _keyAllRows[0].Vin : null;
                ApplyKeysFilter();
                UpdateKeysAlert();
            }
            catch { }
        }

        private void PersistKeysHistory()
        {
            try { ClientKeyHistory.Save(_keyAllRows); }
            catch { }
        }

        private void SortAndTrimKeyRows()
        {
            _keyAllRows.Sort((a, b) =>
            {
                if (a == null && b == null) return 0;
                if (a == null) return 1;
                if (b == null) return -1;
                return b.DetectedLocal.CompareTo(a.DetectedLocal);
            });
            while (_keyAllRows.Count > ClientKeyHistory.MaxEvents)
                _keyAllRows.RemoveAt(_keyAllRows.Count - 1);
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
            _activityRows.Add(new ActivityRow
            {
                At = at,
                Level = lvl,
                Text = text ?? ""
            });
            while (_activityRows.Count > ActivityMaxRows)
                _activityRows.RemoveAt(0);
            ScrollActivityToEnd();
            MaybeRaiseActivityAlert(lvl);
        }

        private void ScrollActivityToEnd()
        {
            if (ActivityList == null || ActivityList.Items.Count == 0) return;
            try { ActivityList.ScrollIntoView(ActivityList.Items[ActivityList.Items.Count - 1]); }
            catch { }
        }

        private void ActivityClear_Click(object sender, RoutedEventArgs e)
        {
            _activityRows.Clear();
            RefreshActivityView();
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
                LogFilterCombo.ItemsSource = new List<LangItem>
                {
                    new LangItem { Code = "all", Name = TL("LogAll", "All") },
                    new LangItem { Code = "info", Name = TL("LogInfo", "Info") },
                    new LangItem { Code = "success", Name = TL("LogSuccess", "Success") },
                    new LangItem { Code = "warning", Name = TL("LogWarning", "Warning") },
                    new LangItem { Code = "error", Name = TL("LogError", "Error") },
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
            RefreshActivityView();
        }

        private void LogFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_logFilterReady || LogFilterCombo == null) return;
            var code = LogFilterCombo.SelectedValue as string;
            _activityFilter = string.IsNullOrEmpty(code) ? "all" : code;
            RefreshActivityView();
            ScrollActivityToEnd();
        }

        private bool ShouldShowActivityRow(object item)
        {
            var row = item as ActivityRow;
            if (row == null) return false;
            if (string.Equals(_activityFilter, "keyreader", StringComparison.OrdinalIgnoreCase))
            {
                var t = row.Text ?? "";
                return t.StartsWith("KeyReader:", StringComparison.OrdinalIgnoreCase);
            }
            if (string.IsNullOrEmpty(_activityFilter) || _activityFilter == "all") return true;
            return string.Equals(row.LevelText, _activityFilter, StringComparison.OrdinalIgnoreCase);
        }

        private void RefreshActivityView()
        {
            try
            {
                if (_activityView != null) _activityView.Refresh();
            }
            catch { }
        }

        // ---------- Alert dots + blink ----------

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

        private void UpdateKeysAlert()
        {
            _keysAlert = false;
            var ev = _keyAllRows.Count > 0 ? _keyAllRows[0] : null;
            if (ev != null)
            {
                var age = DateTime.Now - ev.DetectedLocal;
                if (age.TotalMinutes < KeysAlertMaxMinutes)
                {
                    if (string.Equals(ev.Status, "Failed", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(ev.Status, "Waiting", StringComparison.OrdinalIgnoreCase))
                    {
                        _keysAlert = true;
                    }
                    else if (ev.LocalSent && !ev.BmwUploaded && !ev.Failed
                        && age.TotalMinutes > KeysStallMinutes)
                    {
                        _keysAlert = true;
                    }
                }
            }
            if (KeysTabAlertDot != null)
                KeysTabAlertDot.Visibility = _keysAlert ? Visibility.Visible : Visibility.Collapsed;
            SyncAlertBlinkTimer();
        }

        private void SetRenewAlert(bool on)
        {
            _renewAlert = on;
            if (RenewTabAlertDot != null)
                RenewTabAlertDot.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            SyncAlertBlinkTimer();
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

        private void KeysTabAlertDot_Click(object sender, MouseButtonEventArgs e)
        {
            SelectTabFromAlertDot(TabKeys, e);
        }

        private void RenewTabAlertDot_Click(object sender, MouseButtonEventArgs e)
        {
            SelectTabFromAlertDot(TabAos, e);
        }

        private void KaiTabAlertDot_Click(object sender, MouseButtonEventArgs e)
        {
            SelectTabFromAlertDot(TabKai, e);
        }

        // ---------- Keys VIN highlight ----------

        private void HighlightNewVin(string vin)
        {
            if (string.IsNullOrEmpty(vin) || KeysEventsList == null) return;
            _keysHighlightVin = vin;
            _keysHighlightUntilUtc = DateTime.UtcNow.AddSeconds(KeysHighlightSeconds);
            ScheduleKeysHighlightTimer();
            SelectKeysRowByVin(vin, true);
        }

        private ClientKeyRow FindKeysRowByVin(string vin)
        {
            if (string.IsNullOrEmpty(vin)) return null;
            foreach (var r in _keyRows)
            {
                if (r != null && string.Equals(r.Vin, vin, StringComparison.OrdinalIgnoreCase))
                    return r;
            }
            return null;
        }

        private void SelectKeysRowByVin(string vin, bool highlight)
        {
            if (string.IsNullOrEmpty(vin) || KeysEventsList == null) return;
            var match = FindKeysRowByVin(vin);
            if (match == null) return;
            KeysEventsList.SelectedItem = match;
            try { KeysEventsList.ScrollIntoView(match); }
            catch { }
            if (!highlight) return;
            ClearKeysRowHighlightVisual();
            var lvi = KeysEventsList.ItemContainerGenerator.ContainerFromItem(match) as ListViewItem;
            if (lvi != null)
            {
                ApplyKeysRowHighlight(lvi, vin, true);
                return;
            }
            // Container may not exist until layout; retry once after render.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!string.Equals(_keysHighlightVin, vin, StringComparison.OrdinalIgnoreCase)) return;
                var item = KeysEventsList.ItemContainerGenerator.ContainerFromItem(match) as ListViewItem;
                if (item != null)
                    ApplyKeysRowHighlight(item, vin, false);
            }), DispatcherPriority.Loaded);
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

        // Keys list is rebuilt on filter/search/sync; re-paint the highlight on the new containers.
        private void ReapplyKeysRowHighlightIfNeeded()
        {
            if (string.IsNullOrEmpty(_keysHighlightVin)) return;
            if (DateTime.UtcNow >= _keysHighlightUntilUtc)
            {
                ClearKeysRowHighlight();
                return;
            }
            _keysHighlightedItem = null; // old containers were discarded with the list rebuild
            var vin = _keysHighlightVin;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!string.Equals(_keysHighlightVin, vin, StringComparison.OrdinalIgnoreCase)) return;
                if (DateTime.UtcNow >= _keysHighlightUntilUtc) return;
                var match = FindKeysRowByVin(vin);
                if (match == null || KeysEventsList == null) return;
                var item = KeysEventsList.ItemContainerGenerator.ContainerFromItem(match) as ListViewItem;
                if (item != null)
                    ApplyKeysRowHighlight(item, vin, false);
            }), DispatcherPriority.Loaded);
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

        // ---------- KAI tab ----------

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

        private void UpdateServiceActionButtons(bool allOk, bool anyRunning)
        {
            if (KaiFixServicesBtn != null)
                KaiFixServicesBtn.Visibility = allOk ? Visibility.Collapsed : Visibility.Visible;
            if (KaiStopServicesBtn != null)
                KaiStopServicesBtn.Visibility = anyRunning ? Visibility.Visible : Visibility.Collapsed;
            UpdateAosControlsEnabled();
        }

        private void SetKaiBusy(bool busy)
        {
            _kaiBusy = busy;
            _kaiBusyTimer.Stop();
            if (busy) _kaiBusyTimer.Start();
            var on = _client != null && _client.IsConnected;
            if (KaiFixServicesBtn != null) KaiFixServicesBtn.IsEnabled = on && !busy;
            if (KaiStopServicesBtn != null) KaiStopServicesBtn.IsEnabled = on && !busy;
        }

        private void KaiBusyTimer_Tick(object sender, EventArgs e)
        {
            SetKaiBusy(false);
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

        private void UpsertKeyRow(ClientKeyRow row)
        {
            UpsertKeyRowCore(row);
            SortAndTrimKeyRows();
            PersistKeysHistory();
            ApplyKeysFilter();
            UpdateKeysTodayBadge();
            UpdateKeysAlert();
        }

        private void UpsertKeyRowCore(ClientKeyRow row)
        {
            if (row == null || string.IsNullOrEmpty(row.Vin)) return;
            for (var i = _keyAllRows.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_keyAllRows[i].Vin, row.Vin, StringComparison.OrdinalIgnoreCase))
                    _keyAllRows.RemoveAt(i);
            }
            _keyAllRows.Insert(0, row);
        }

        private void KeysVinCopyIcon_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateVinCopyIconVisibility(sender as Button);
        }

        private void KeysVinCopyIcon_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            UpdateVinCopyIconVisibility(sender as Button);
        }

        private void UpdateVinCopyIconVisibility(Button btn)
        {
            if (btn == null) return;
            var row = btn.DataContext as ClientKeyRow;
            var tipText = ClientUiStrings.T("CopyVin");
            var tip = btn.ToolTip as ToolTip;
            if (tip != null)
            {
                var tb = tip.Content as TextBlock;
                if (tb != null) tb.Text = tipText;
            }
            else
                btn.ToolTip = tipText;
            btn.Visibility = (row != null && !string.IsNullOrEmpty(row.Vin))
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void KeysVinCopyIcon_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            var btn = sender as Button;
            if (btn == null) return;
            var row = btn.DataContext as ClientKeyRow;
            if (row == null || string.IsNullOrEmpty(row.Vin)) return;
            CopyVin(row.Vin);
            ShowVinCopyIconFeedback(btn);
        }

        private void KeysVinCopyIcon_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            var btn = sender as Button;
            if (btn == null) return;
            var row = btn.DataContext as ClientKeyRow;
            if (row == null || string.IsNullOrEmpty(row.Vin)) return;
            CopyVin(row.Vin);
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
            ToastWindow.ShowTip(msg.Title, msg.Text, level, actionLabel, action, seconds);
        }

        private static void CopyVin(string vin)
        {
            if (string.IsNullOrEmpty(vin)) return;
            try { Clipboard.SetText(vin); }
            catch
            {
                try { WinForms.Clipboard.SetText(vin); }
                catch { }
            }
        }

        private void StatusTimer_Tick(object sender, EventArgs e)
        {
            RefreshTrayStatus();
            MaybeAlertOffline();
            UpdateKeysAlert(); // time-based stall detection
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
            ApplyStatusMode(reconnecting ? "error" : "stopped",
                ClientUiStrings.T(reconnecting ? "Reconnecting" : "Offline"));
            if (NextRunText != null) NextRunText.Text = "";
            if (TokenAgeText != null) TokenAgeText.Text = "";
            ApplyTaggedText(TokenFooterText, "none", "");
            ApplyTaggedText(ServicesFooterText, "none", "");
            _servicesFail = false;
            SyncServicesBlinkTimer();
            SetRenewAlert(false);
            if (RegisterAgeText != null)
            {
                RegisterAgeText.Inlines.Clear();
                RegisterAgeText.ToolTip = null;
            }
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
            RefreshAuthGuardsPanel(cdMode == "on", cdText, blMode == "on", blText, authTip);
            _hasServerFooter = true;
            SetConnStatus("");
        }

        private void RefreshAuthGuardsPanel(bool cooldownOn, string cooldownFooterText, bool blockedOn, string blockedFooterText, string tip)
        {
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
            SyncAuthGuardsBlinkTimer();
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

        private void ApplyServicesBlinkVisuals()
        {
            if (ServicesFooterText == null) return;
            ServicesFooterText.Opacity = _servicesFail ? (_servicesBlinkOn ? 1.0 : 0.2) : 1.0;
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

        private void ExitApp()
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
                _tray.Visible = false;
                _tray.Dispose();
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
            Application.Current.Shutdown();
        }
    }
}
