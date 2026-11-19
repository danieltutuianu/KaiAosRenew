using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using KaiAosRenew;

namespace KaiAosNotifyClient
{
    public partial class MainWindow : Window
    {
        private CancellationTokenSource _scanCts;
        private DateTime _rttSentUtc = DateTime.MinValue;
        private int _lastRttMs = -1;
        private DateTime _nextRttProbeUtc = DateTime.MinValue;

        private void UpdateFavHostButton()
        {
            if (FavHostBtn == null) return;
            var host = GetHostText();
            var fav = _settings != null ? _settings.FindFavoriteHost(host) : null;
            FavHostBtn.Content = fav != null ? "★" : "☆";
            FavHostBtn.ToolTip = ClientUiStrings.T("FavoriteHost");
            FavHostBtn.IsEnabled = !string.IsNullOrWhiteSpace(host);
        }

        private void FavHost_Click(object sender, RoutedEventArgs e)
        {
            var host = GetHostText();
            if (string.IsNullOrWhiteSpace(host))
            {
                SetConnStatus(ClientUiStrings.T("InvalidHost"));
                return;
            }
            var existing = _settings != null ? _settings.FindFavoriteHost(host) : null;
            var alias = existing != null ? (existing.Alias ?? "") : "";
            string newAlias;
            var choice = FavoriteHostDialog.Show(this, host, alias, existing != null, out newAlias);
            if (choice == FavoriteHostChoice.Cancel) return;
            if (_settings == null) return;
            if (choice == FavoriteHostChoice.Remove)
            {
                _settings.RemoveFavoriteHost(host);
                _settings.Save();
                RefreshHostCombo();
                SetHostText(host);
                UpdateFavHostButton();
                return;
            }
            _settings.SetFavoriteHost(host, newAlias);
            _settings.PushRecentHost(host);
            _settings.Save();
            RefreshHostCombo();
            SetHostText(host);
            UpdateFavHostButton();
        }

        private void OnRttPong()
        {
            if (_rttSentUtc == DateTime.MinValue) return;
            var ms = (int)Math.Max(0, (DateTime.UtcNow - _rttSentUtc).TotalMilliseconds);
            _rttSentUtc = DateTime.MinValue;
            _lastRttMs = ms;
            UpdateRttUi();
        }

        private void UpdateRttUi()
        {
            if (RttText == null) return;
            if (_lastRttMs < 0 || _client == null || !_client.IsConnected)
            {
                RttText.Visibility = Visibility.Collapsed;
                RttText.Text = "";
                return;
            }
            RttText.Text = string.Format(ClientUiStrings.T("RttMs"), _lastRttMs);
            RttText.Visibility = Visibility.Visible;
        }

        private void MaybeSendRttProbe()
        {
            if (_client == null || !_client.IsConnected) return;
            var now = DateTime.UtcNow;
            if (now < _nextRttProbeUtc) return;
            _nextRttProbeUtc = now.AddSeconds(15);
            _rttSentUtc = now;
            try
            {
                _client.Send(new NotifyMessage
                {
                    Kind = "discoverPing",
                    Level = "info",
                    Title = "",
                    Text = ""
                });
            }
            catch
            {
                _rttSentUtc = DateTime.MinValue;
            }
        }

        private void ClearRtt()
        {
            _lastRttMs = -1;
            _rttSentUtc = DateTime.MinValue;
            _nextRttProbeUtc = DateTime.MinValue;
            UpdateRttUi();
        }

        /// <summary>Called from StatusTimer_Tick (connected path).</summary>
        private void NetDiagOnStatusTick()
        {
            MaybeSendRttProbe();
            UpdateRttUi();
        }

        private void ScanLan_Click(object sender, RoutedEventArgs e)
        {
            if (_scanBusy)
            {
                // Second click cancels an in-flight scan.
                try
                {
                    if (_scanCts != null) _scanCts.Cancel();
                }
                catch { }
                SetConnStatus(ClientUiStrings.T("ScanCancelled"));
                return;
            }
            _scanBusy = true;
            if (ScanLanBtn != null)
            {
                ScanLanBtn.Content = ClientUiStrings.T("ScanCancel");
                ScanLanBtn.IsEnabled = true;
            }
            SetConnStatus(ClientUiStrings.T("Scanning"));
            int port;
            if (!int.TryParse((PortBox != null ? PortBox.Text : _settings.Port) ?? "", out port) || port < 1 || port > 65535)
                port = 17890;
            _settings.Port = port.ToString();
            if (PortBox != null) PortBox.Text = _settings.Port;

            var cts = new CancellationTokenSource();
            var old = Interlocked.Exchange(ref _scanCts, cts);
            if (old != null)
            {
                try { old.Cancel(); }
                catch { }
                try { old.Dispose(); }
                catch { }
            }

            Task.Run(delegate
            {
                string found = null;
                var pairingRequired = false;
                try
                {
                    found = LanServerDiscovery.FindFirstOpenHost(port, cts.Token,
                        progress =>
                        {
                            if (progress == null) return;
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                if (!_scanBusy) return;
                                var phase = string.Equals(progress.Phase, "tcp", StringComparison.OrdinalIgnoreCase)
                                    ? "TCP" : "UDP";
                                SetConnStatus(string.Format(
                                    ClientUiStrings.T("ScanningProgress"),
                                    phase, progress.Checked, progress.Total));
                            }));
                        },
                        out pairingRequired);
                }
                catch (OperationCanceledException) { }
                catch { }
                var needPairing = pairingRequired;
                var cancelled = cts.IsCancellationRequested;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _scanBusy = false;
                    if (ScanLanBtn != null)
                    {
                        ScanLanBtn.Content = ClientUiStrings.T("ScanLan");
                        ScanLanBtn.IsEnabled = true;
                    }
                    try
                    {
                        if (ReferenceEquals(_scanCts, cts))
                        {
                            _scanCts = null;
                            cts.Dispose();
                        }
                    }
                    catch { }
                    if (cancelled)
                    {
                        SetConnStatus(ClientUiStrings.T("ScanCancelled"));
                        RefreshTrayStatus();
                        return;
                    }
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
                        var code = PairingCodeBox != null
                            ? (PairingCodeBox.Text ?? "").Trim()
                            : (_settings.PairingCode ?? "").Trim();
                        if (needPairing && string.IsNullOrEmpty(code))
                        {
                            SetConnStatus(ClientUiStrings.T("PairingRequiredHint"));
                            FocusPairingCodeBox();
                            RefreshTrayStatus();
                        }
                        else
                        {
                            SetConnStatus(string.Format(ClientUiStrings.T("FoundServerConnect"), found, port));
                            DoConnect(found, port);
                        }
                    }
                    else
                    {
                        SetConnStatus(ClientUiStrings.T("NoServerFound"));
                        RefreshTrayStatus();
                    }
                }));
            });
        }

        private void SrvDownloadDiagBundle_Click(object sender, RoutedEventArgs e)
        {
            SendDownloadCmd("getZip|diag");
        }
    }
}
