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

        private static bool IsTestVinPlaceholder(string vin)
        {
            return !string.IsNullOrEmpty(vin)
                && string.Equals(vin.Trim(), TestVinPlaceholder, StringComparison.OrdinalIgnoreCase);
        }

        private void SyncShowVinTipsBmwFailOnlyUi()
        {
            if (ShowVinTipsBmwFailOnlyCheck == null) return;
            var parentOn = ShowVinTipsCheck != null && ShowVinTipsCheck.IsChecked == true;
            ShowVinTipsBmwFailOnlyCheck.IsEnabled = parentOn;
            if (!parentOn)
            {
                ShowVinTipsBmwFailOnlyCheck.IsChecked = false;
                if (_settings != null)
                    _settings.ShowVinTipsBmwFailOnly = false;
            }
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

        private void KeysClear_Click(object sender, RoutedEventArgs e)
        {
            if (_keyAllRows == null || _keyAllRows.Count == 0)
            {
                UpdateClearButtonsEnabled();
                return;
            }
            var confirm = MessageBox.Show(
                this,
                ClientUiStrings.T("ClearKeysConfirm"),
                ClientUiStrings.T("ClearKeys"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
            if (confirm != MessageBoxResult.Yes)
                return;

            _keyAllRows.Clear();
            PersistKeysHistory();
            ClearKeysRowHighlight();
            _lastVin = null;
            ApplyKeysFilter();
            UpdateKeysTodayBadge();
            UpdateKeysAlert();
            UpdateKeysRetryButton();
            RefreshTrayStatus();
            AppendActivityRow(null, "info", ClientUiStrings.T("ClearKeysDone"));
            UpdateClearButtonsEnabled();
        }

        private void KeysRetryBmw_Click(object sender, RoutedEventArgs e)
        {
            if (_client == null || !_client.IsConnected) return;
            SendAosCmd("retryBmw");
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
                        || ContainsIgnoreCase(r.KmText, q)
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
                if (_keyRows.Count >= ClientKeyHistory.MaxEvents) break;
            }
            UpdateKeysTodayBadge();
            UpdateKeysRetryButton();
            ReapplyKeysRowHighlightIfNeeded();
            var hasKeys = _keyAllRows != null && _keyAllRows.Count > 0;
            UpdateEmptyHint(KeysEmptyHint, !hasKeys);
            UpdateNoMatchesHint(KeysNoMatchesHint, hasKeys, _keyRows.Count);
            UpdateClearButtonsEnabled();
        }

        private void KeysDownloadBinBtn_Loaded(object sender, RoutedEventArgs e)
        {
            SyncKeysDownloadBinBtn(sender as Button);
        }

        private void KeysDownloadBinBtn_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            SyncKeysDownloadBinBtn(sender as Button);
        }

        private void SyncKeysDownloadBinBtn(Button btn)
        {
            if (btn == null) return;
            var row = btn.DataContext as ClientKeyRow;
            var ok = _debugMode && row != null && row.HasBin && !string.IsNullOrEmpty(row.Vin);
            btn.IsEnabled = ok && _client != null && _client.IsConnected;
            btn.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
            // Content is the icon Viewbox from XAML — do not replace with text.
            btn.ToolTip = ClientUiStrings.T("KeysDownloadBinTip");
        }

        private void KeysDownloadBinBtn_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var row = btn != null ? btn.DataContext as ClientKeyRow : null;
            if (row == null || string.IsNullOrEmpty(row.Vin) || !row.HasBin) return;
            SendDownloadCmd("getKeyBin|" + row.Vin);
        }

        private void SrvDownloadKeyDebugFolder_Click(object sender, RoutedEventArgs e)
        {
            if (!_debugMode) return;
            SendDownloadCmd("getZip|keyDebug");
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

        private void ApplyKeySync(string text)
        {
            // Authoritative: replace local list with server snapshot (empty clears phantoms).
            // Disconnect does not send keySync, so history survives offline.
            _keyAllRows.Clear();
            if (!string.IsNullOrWhiteSpace(text))
            {
                var rows = ClientKeySync.ParseKeySync(text);
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

        /// <summary>Server-driven reader warning (Inactive / ENC missing). Empty text hides the banner.</summary>
        private void ApplyKeysReaderWarn(string text)
        {
            var t = (text ?? "").Trim();
            if (KeysReaderWarnText != null)
                KeysReaderWarnText.Text = t;
            if (KeysReaderWarnBanner != null)
                KeysReaderWarnBanner.Visibility = t.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
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

        private void ActivityList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.C || (Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
                return;
            if (IsGridViewHeaderClick(e.OriginalSource as DependencyObject)) return;
            if (CopySelectedActivityText())
                e.Handled = true;
        }

        private void RegisterHistoryList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.C || (Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
                return;
            if (IsGridViewHeaderClick(e.OriginalSource as DependencyObject)) return;
            if (CopySelectedRegisterText())
                e.Handled = true;
        }

        private void KeysTabAlertDot_Click(object sender, MouseButtonEventArgs e)
        {
            SelectTabFromAlertDot(TabKeys, e);
        }

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

        private void KeysEventsList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.C || (Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
                return;
            var row = KeysEventsList != null ? KeysEventsList.SelectedItem as ClientKeyRow : null;
            if (row == null || string.IsNullOrEmpty(row.Vin)) return;
            CopyVin(row.Vin);
            e.Handled = true;
        }

        private void KeysVinCopyIcon_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateVinCopyIconVisibility(sender as Button);
        }

        private void KeysVinCopyIcon_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            UpdateVinCopyIconVisibility(sender as Button);
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

        private void KeysKmCopyIcon_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateKmCopyIconVisibility(sender as Button);
        }

        private void KeysKmCopyIcon_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            UpdateKmCopyIconVisibility(sender as Button);
        }

        private void KeysKmCopyIcon_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            var btn = sender as Button;
            if (btn == null) return;
            var row = btn.DataContext as ClientKeyRow;
            if (row == null || !row.MileageKm.HasValue || row.MileageKm.Value <= 0) return;
            CopyKm(row.MileageKm.Value);
            ShowVinCopyIconFeedback(btn);
        }

        private void KeysKmCopyIcon_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            var btn = sender as Button;
            if (btn == null) return;
            var row = btn.DataContext as ClientKeyRow;
            if (row == null || !row.MileageKm.HasValue || row.MileageKm.Value <= 0) return;
            CopyKm(row.MileageKm.Value);
            ShowVinCopyIconFeedback(btn);
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

        private static void CopyKm(int km)
        {
            if (km <= 0) return;
            var text = km.ToString(System.Globalization.CultureInfo.InvariantCulture);
            try { Clipboard.SetText(text); }
            catch
            {
                try { WinForms.Clipboard.SetText(text); }
                catch { }
            }
        }

        private void SrvNotifyTestVin_Click(object sender, RoutedEventArgs e)
        {
            SendSrvNotifyTest("vin");
        }

    }
}
