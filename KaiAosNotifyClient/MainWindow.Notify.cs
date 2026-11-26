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

        private void ApplyConnectionUi()
        {
            RefreshHostCombo();
            SetHostText(_settings.Host);
            if (PortBox != null) PortBox.Text = _settings.Port;
            if (PairingCodeBox != null) PairingCodeBox.Text = _settings.PairingCode ?? "";
            if (AutoConnectCheck != null) AutoConnectCheck.IsChecked = _settings.AutoConnect;
        }

        // ScanLan_Click lives in MainWindow.NetDiag.cs (progress + cancel).

        private void FocusPairingCodeBox()
        {
            try
            {
                if (PairingCodeBox == null) return;
                MarkFieldInvalid(PairingCodeBox);
                PairingCodeBox.Focus();
                PairingCodeBox.SelectAll();
            }
            catch { }
        }

        private void HostPort_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_loadingUi) return;
            CaptureConnectionFields();
            _settings.Save();
            RefreshHostCombo();
        }

        private void CaptureConnectionFields()
        {
            _settings.Host = GetHostText();
            if (PortBox != null)
                _settings.Port = (PortBox.Text ?? "").Trim();
            if (PairingCodeBox != null)
                _settings.PairingCode = (PairingCodeBox.Text ?? "").Trim();
            if (AutoConnectCheck != null)
                _settings.AutoConnect = AutoConnectCheck.IsChecked == true;
            _settings.Normalize();
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

            var host = (_settings.Host ?? "").Trim();
            ClearFieldInvalid(HostCombo);
            ClearFieldInvalid(PortBox);
            if (string.IsNullOrWhiteSpace(host))
            {
                MarkFieldInvalid(HostCombo);
                try { Activate(); } catch { }
                if (HostCombo != null) HostCombo.Focus();
                MessageBox.Show(this, ClientUiStrings.T("EnterIp"), ClientUiStrings.T("AppTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                SetConnStatus(ClientUiStrings.T("EnterIp"));
                RefreshTrayStatus();
                return;
            }
            if (!IsValidHost(host))
            {
                MarkFieldInvalid(HostCombo);
                try { Activate(); } catch { }
                if (HostCombo != null) HostCombo.Focus();
                MessageBox.Show(this, ClientUiStrings.T("InvalidHost"), ClientUiStrings.T("AppTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                SetConnStatus(ClientUiStrings.T("InvalidHost"));
                RefreshTrayStatus();
                return;
            }
            _settings.Host = host;

            int port = portOverride;
            if (port < 1 || port > 65535)
            {
                if (!int.TryParse((_settings.Port ?? "").Trim(), out port) || port < 1 || port > 65535)
                {
                    // Explicit Connect: do not silently fall back to 17890.
                    if (portOverride == 0)
                    {
                        MarkFieldInvalid(PortBox);
                        try { Activate(); } catch { }
                        if (PortBox != null) PortBox.Focus();
                        MessageBox.Show(this, ClientUiStrings.T("InvalidPort"), ClientUiStrings.T("AppTitle"),
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        RefreshTrayStatus();
                        return;
                    }
                    port = 17890;
                }
            }
            _settings.Port = port.ToString();
            if (PortBox != null) PortBox.Text = _settings.Port;
            if (PairingCodeBox != null)
                _settings.PairingCode = (PairingCodeBox.Text ?? "").Trim();
            _settings.PushRecentHost(_settings.Host);
            RefreshHostCombo();
            SetHostText(_settings.Host);
            _settings.Save();

            // Switching notify servers must not keep Keys/Activity/AOS from the previous one.
            if (!string.IsNullOrEmpty(_lastConnectedHost)
                && (!string.Equals(_lastConnectedHost, host, StringComparison.OrdinalIgnoreCase)
                    || _lastConnectedPort != port))
            {
                ClearServerBoundState();
            }

            _wantConnectUi = true;
            _offlineAlertShown = false;
            _client.Connect(_settings.Host, port, _settings.PairingCode ?? "");
            // Keep Connect enabled while reconnecting so it acts as Retry now.
            ConnectBtn.IsEnabled = true;
            DisconnectBtn.IsEnabled = true;
            UpdateConnectTargetEnabled();
            UpdateConnectButtonLabel();
            RefreshReconnectStatusUi();
            RefreshTrayStatus();
        }

        private void DisconnectBtn_Click(object sender, RoutedEventArgs e)
        {
            _wantConnectUi = false;
            _client.Disconnect();
            ConnectBtn.IsEnabled = true;
            DisconnectBtn.IsEnabled = false;
            UpdateConnectTargetEnabled();
            UpdateConnectButtonLabel();
            RefreshTrayStatus();
        }

        private void OnClientStatus(string status)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_client.IsConnected)
                {
                    ShowDisconnectedFooter();
                    if (_awaitingServerUpdateReconnect)
                        SetConnStatus(ClientUiStrings.T("UpdateServerWaiting"));
                    else if (_wantConnectUi || _client.WantConnect)
                        RefreshReconnectStatusUi();
                    else if (!string.IsNullOrEmpty(status)
                        && string.Equals(status, "Disconnected", StringComparison.OrdinalIgnoreCase))
                        SetConnStatus("");
                }
                else if (!string.IsNullOrEmpty(status)
                    && status.StartsWith("Connected", StringComparison.OrdinalIgnoreCase))
                {
                    SetConnStatus("");
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
                UpdateConnectTargetEnabled();
                UpdateConnectButtonLabel();
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
                int connectedPort;
                if (!int.TryParse((_settings.Port ?? "").Trim(), out connectedPort) || connectedPort < 1)
                    connectedPort = 17890;
                _lastConnectedHost = (_settings.Host ?? "").Trim();
                _lastConnectedPort = connectedPort;
                UpdateAosControlsEnabled();
                UpdateServerSettingsEnabled();
                RefreshTrayStatus();
                SendClientHello();
                RequestAosSync();
                RequestUpdateFeed();
                SendUpdateFeed();
            }));
        }

        /// <summary>
        /// Wipe UI + persisted keys from a previous notify server before connecting to another.
        /// Same-host reconnect does not call this (offline key history survives).
        /// </summary>
        private void ClearServerBoundState()
        {
            // Keys
            if (_keyAllRows != null)
                _keyAllRows.Clear();
            _keysSearchServerRows = new List<ClientKeyRow>();
            _lastVin = null;
            PersistKeysHistory();
            ClearKeysRowHighlight();
            ApplyKeysFilter();
            UpdateKeysTodayBadge();
            UpdateKeysAlert();
            UpdateKeysRetryButton();
            ApplyKeysReaderWarn("");

            // Activity
            _activityRows.Clear();
            _activityHistoryRows.Clear();
            RefreshActivityView();

            // Register history
            _registerHistory.Clear();
            if (_registerHistoryViewRows != null)
                _registerHistoryViewRows.Clear();
            ApplyRegisterFilter();

            // AOS Access
            CancelAosAckWait();
            _awaitingBrowserPassFirebaseAuto = false;
            _awaitingPullCloudAuto = false;
            _pendingPullCloudEmail = "";
            _pendingPullCloudPass = "";
            _browserPassFirebaseAutoInFlight = false;
            _aosForceRegisterOverride = null;
            _loadingAosUi = true;
            try
            {
                if (AosEmailBox != null) AosEmailBox.Text = "";
                SetAosPasswordUi("");
                if (AosIntervalBox != null) AosIntervalBox.Text = "1";
            }
            finally
            {
                _loadingAosUi = false;
            }
            _lastSyncedEmail = "";
            _lastSyncedPass = "";
            _lastSyncedHours = 1;
            _aosDirty = false;
            HideClipboardPassBanner();
            if (BrowserPassNewerBanner != null)
                BrowserPassNewerBanner.Visibility = Visibility.Collapsed;
            if (_settings != null)
            {
                _settings.LastSuccessUtc = null;
                try { _settings.Save(); }
                catch { }
            }
            UpdateAosSyncStatus();
            UpdateAosControlsEnabled();
            UpdateAosPullCloudButtonVisibility();

            // Server Settings sync cache
            _lastServerSettingsMap = null;
            _lastServerSettingsCapture = null;
            ClearSrvSettingsConflict();
            MarkSrvSettingsDirty(false);
            SetSrvSettingsStatus("", false);
            UpdateServerSettingsEnabled();

            // Kai services panel
            SetKaiBusy(false);
            if (KaiInstallText != null) KaiInstallText.Text = "—";
            if (KaiUrlRegisterBox != null) KaiUrlRegisterBox.Text = "";
            if (KaiUrlWebEamBox != null) KaiUrlWebEamBox.Text = "";
            if (KaiUrlUploadBox != null) KaiUrlUploadBox.Text = "";
            if (KaiTokenExpiryText != null) KaiTokenExpiryText.Text = "—";
            ApplyKaiSvcRow(KaiSvc1Name, KaiSvc1Status, KaiSvc1Start, null,
                ThemeBrush("TextPrimaryBrush", Brushes.Black),
                ThemeBrush("StatusOkBrush", FooterOkDay),
                ThemeBrush("StatusErrorBrush", FooterErrorDay));
            ApplyKaiSvcRow(KaiSvc2Name, KaiSvc2Status, KaiSvc2Start, null,
                ThemeBrush("TextPrimaryBrush", Brushes.Black),
                ThemeBrush("StatusOkBrush", FooterOkDay),
                ThemeBrush("StatusErrorBrush", FooterErrorDay));
            UpdateServiceActionButtons(false, false);
            SetKaiServiceAlert(false);

            ClearNotifyClientsCombo();
            UpdateClearButtonsEnabled();
            RebuildTrayMenu();
            RefreshTrayStatus();
        }

        private void OnClientDisconnected()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_wantConnectUi || _client.WantConnect)
                {
                    ConnectBtn.IsEnabled = true; // Retry now
                    DisconnectBtn.IsEnabled = true;
                }
                else
                {
                    ConnectBtn.IsEnabled = true;
                    DisconnectBtn.IsEnabled = false;
                }
                UpdateConnectTargetEnabled();
                UpdateConnectButtonLabel();
                CancelAosAckWait();
                SetKaiBusy(false);
                if (_awaitingServerUpdateReconnect)
                    SetConnStatus(ClientUiStrings.T("UpdateServerWaiting"));
                else
                    RefreshReconnectStatusUi();
                UpdateAosControlsEnabled();
                UpdateAosSyncStatus();
                RefreshTrayStatus();
                ClearNotifyClientsCombo();
                SetSrvSettingsStatus("", false);
                MarkSrvSettingsDirty(false);
                UpdateServerSettingsEnabled();
            }));
        }

        private void OnClientAuthFailed(string reason)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _wantConnectUi = false;
                ConnectBtn.IsEnabled = true;
                DisconnectBtn.IsEnabled = false;
                UpdateConnectTargetEnabled();
                UpdateConnectButtonLabel();
                var needCode = string.Equals((reason ?? "").Trim(), "PAIRING_REQUIRED", StringComparison.OrdinalIgnoreCase);
                var msg = needCode
                    ? ClientUiStrings.T("PairingRequiredHint")
                    : ClientUiStrings.T("PairingAuthFailed");
                SetConnStatus(msg);
                if (needCode)
                    FocusPairingCodeBox();
                RefreshTrayStatus();
                try
                {
                    MessageBox.Show(this, msg, ClientUiStrings.T("AppTitle"),
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch { }
            }));
        }

        private void OnMessageReceived(NotifyMessage msg)
        {
            if (msg == null) return;
            Dispatcher.BeginInvoke(new Action(() => HandleMessage(msg)));
        }

    }
}
