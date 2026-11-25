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

        private void ApplyKeysTabStrings()
        {
            if (KeysRefreshBtn != null) KeysRefreshBtn.Content = UiStrings.T("KeysRefresh");
            if (KeysClearBtn != null) KeysClearBtn.Content = UiStrings.T("ClearKeys");
            if (KeysExportBtn != null)
            {
                KeysExportBtn.Content = UiStrings.T("KeysExport");
                KeysExportBtn.ToolTip = UiStrings.T("KeysExportTip");
            }
            if (KeysRetryBmwBtn != null)
            {
                KeysRetryBmwBtn.Content = UiStrings.T("KeysRetryBmw");
                KeysRetryBmwBtn.ToolTip = UiStrings.T("KeysRetryBmwTip");
            }
            if (KeysOpenUploaderLogBtn != null) KeysOpenUploaderLogBtn.Content = UiStrings.T("KeysOpenUploaderLog");
            if (KeysOpenKaiLogBtn != null) KeysOpenKaiLogBtn.Content = UiStrings.T("KeysOpenKaiLog");
            if (KeysFilterLabel != null) KeysFilterLabel.Text = UiStrings.T("KeysFilter");
            if (KeysEmptyHint != null) KeysEmptyHint.Text = UiStrings.T("KeysEmptyHint");
            if (KeysNoMatchesHint != null) KeysNoMatchesHint.Text = UiStrings.T("NoMatchesHint");
            if (ActivityEmptyHint != null) ActivityEmptyHint.Text = UiStrings.T("ActivityEmptyHint");
            if (ActivityNoMatchesHint != null) ActivityNoMatchesHint.Text = UiStrings.T("NoMatchesHint");
            if (RegisterNoMatchesHint != null) RegisterNoMatchesHint.Text = UiStrings.T("NoMatchesHint");
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
            if (KeysOpenDebugFolderBtn != null)
            {
                KeysOpenDebugFolderBtn.Content = UiStrings.T("KeysOpenDebugFolder");
                KeysOpenDebugFolderBtn.ToolTip = UiStrings.T("KeysOpenDebugFolderTip");
            }
            if (KeysAutoCopyVinCheck != null) KeysAutoCopyVinCheck.Content = UiStrings.T("KeysAutoCopyVin");
            if (KeysColVin != null) KeysColVin.Header = UiStrings.T("KeysColVin");
            if (KeysColKm != null) KeysColKm.Header = UiStrings.T("KeysColKm");
            if (KeysColPaint != null) KeysColPaint.Header = UiStrings.T("KeysColPaint");
            if (KeysColUpholstery != null) KeysColUpholstery.Header = UiStrings.T("KeysColUpholstery");
            if (KeysColILevel != null) KeysColILevel.Header = UiStrings.T("KeysColILevel");
            if (KeysColReader != null) KeysColReader.Header = UiStrings.T("KeysColReader");
            if (KeysColLocal != null) KeysColLocal.Header = UiStrings.T("KeysColLocal");
            if (KeysColBmw != null) KeysColBmw.Header = UiStrings.T("KeysColBmw");
            if (KeysColDetail != null) KeysColDetail.Header = UiStrings.T("KeysColDetail");
            if (KeysColBin != null) KeysColBin.Header = UiStrings.T("KeysColBin");
            ApplyKeysReaderWarnUi();
            ApplyKeysSortHeaders();
            InitKeysFilterCombo();
            _trayVinMenuSignature = null; // force rebuild for new language strings
            RebuildTrayMenu();
        }

        private IEnumerable<KeyValuePair<string, GridViewColumn>> KeysSortColumns()
        {
            yield return new KeyValuePair<string, GridViewColumn>("time", KeysColTime);
            yield return new KeyValuePair<string, GridViewColumn>("vin", KeysColVin);
            yield return new KeyValuePair<string, GridViewColumn>("km", KeysColKm);
            yield return new KeyValuePair<string, GridViewColumn>("paint", KeysColPaint);
            yield return new KeyValuePair<string, GridViewColumn>("upholstery", KeysColUpholstery);
            yield return new KeyValuePair<string, GridViewColumn>("ilevel", KeysColILevel);
            yield return new KeyValuePair<string, GridViewColumn>("reader", KeysColReader);
            yield return new KeyValuePair<string, GridViewColumn>("local", KeysColLocal);
            yield return new KeyValuePair<string, GridViewColumn>("bmw", KeysColBmw);
        }

        /// <summary>Localised column headers with a ▲/▼ marker on the active sort column.</summary>
        private void ApplyKeysSortHeaders()
        {
            if (KeysColIndex != null)
                KeysColIndex.Header = "#";
            foreach (var kv in KeysSortColumns())
            {
                if (kv.Value == null) continue;
                var h = UiStrings.T(KeysExportLogic.ColumnHeaderKey(kv.Key));
                if (_keysSortColumn == kv.Key)
                    h += _keysSortAscending ? " ▲" : " ▼";
                kv.Value.Header = h;
            }
        }

        private void KeysColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            var header = e.OriginalSource as GridViewColumnHeader;
            if (header == null || header.Column == null) return;
            string col = null;
            foreach (var kv in KeysSortColumns())
            {
                if (ReferenceEquals(header.Column, kv.Value)) { col = kv.Key; break; }
            }
            if (col == null) return;

            if (_keysSortColumn == col)
                _keysSortAscending = !_keysSortAscending;
            else
            {
                _keysSortColumn = col;
                _keysSortAscending = KeysExportLogic.DefaultAscending(col);
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
                // Pale highlight needs dark text in the Night theme too.
                matchRun.Foreground = Brushes.Black;
                tb.Inlines.Add(matchRun);
                idx = hit + q.Length;
            }
        }

        private static KeysRowView ToKeysRowView(KeyActivityEvent ev)
        {
            if (ev == null) return null;
            return new KeysRowView
            {
                Time = ev.DetectedLocal,
                Vin = ev.Vin ?? "",
                Km = ev.MileageKm,
                Paint = ev.Paint ?? "",
                Upholstery = ev.Upholstery ?? "",
                ILevel = ev.ILevelCurrent ?? "",
                Reader = ev.ReaderId ?? "",
                Local = ev.LocalText,
                Bmw = ev.BmwText,
                Status = ev.StatusText,
                StatusRank = (int)ev.Status,
                Detail = ev.DetailText
            };
        }

        private static List<KeyActivityEvent> SortKeysEvents(
            IEnumerable<KeyActivityEvent> src, string column, bool ascending)
        {
            return KeysExportLogic.Sort<KeyActivityEvent>(src, column, ascending, ToKeysRowView);
        }

        private void KeysExport_Click(object sender, RoutedEventArgs e)
        {
            var list = KeysEventsList != null ? KeysEventsList.ItemsSource as System.Collections.IEnumerable : null;
            var rows = new List<KeysRowView>();
            if (list != null)
            {
                foreach (var obj in list)
                {
                    var v = ToKeysRowView(obj as KeyActivityEvent);
                    if (v != null) rows.Add(v);
                }
            }
            if (rows.Count == 0)
            {
                MessageBox.Show(this, UiStrings.T("KeysExportEmpty"), UiStrings.T("KeysExportTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                var dlg = new SaveFileDialog
                {
                    Title = UiStrings.T("KeysExportTitle"),
                    Filter = UiStrings.T("KeysExportFilter"),
                    FilterIndex = 1,
                    DefaultExt = ".csv",
                    AddExtension = true,
                    OverwritePrompt = true,
                    FileName = KeysExportLogic.SuggestFileName("keys-history-server", DateTime.Now, false)
                };
                if (dlg.ShowDialog(this) != true) return;
                var path = dlg.FileName;
                File.WriteAllText(path, KeysExportLogic.Build(rows, path), new UTF8Encoding(true));
                AppendLog(string.Format(UiStrings.T("KeysExportDone"), rows.Count, path), LogLevel.Info);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(UiStrings.T("KeysExportFail"), ex.Message),
                    UiStrings.T("KeysExportTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
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
            if (_keysAllEvents == null || _keysAllEvents.Count == 0)
            {
                UpdateClearButtonsEnabled();
                return;
            }
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
            UpdateClearButtonsEnabled();
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

        private async void KeysRetryBmw_Click(object sender, RoutedEventArgs e)
        {
            var ev = KeyActivityMonitor.GetNewestEvent(_keysAllEvents);
            await RetryBmwUploadAsync(ev, false);
        }

        private async Task RetryBmwUploadAfter403Async(string vin, string readerId)
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
            if (_simMockRetryBin
                && ev != null
                && string.Equals(ev.Vin, SimTestVin, StringComparison.OrdinalIgnoreCase))
            {
                _simMockRetryBin = false;
                AppendLog(UiStrings.T("SimRetryMockLog"), LogLevel.Info);
                TrayTipWindow.ShowTip("KAI - AOS Renew", UiStrings.T("SimRetryMockLog"), TrayTipLevel.Info);
                UpdateKeysRetryButton();
                return;
            }
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

                var probe = await System.Threading.Tasks.Task.Run(() =>
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

                    // HwAddress resolved inside probe from hint/Sqlite when empty.
                    return KeyUploadProbe.ProbeTransferredBin(_settings, readerId, null);
                });

                var bmwOk = probe != null && probe.Ok;
                if (bmwOk)
                {
                    AppendLog(string.Format(UiStrings.T("KeysRetryBmwProbe"), probe.Summary), LogLevel.Success);
                }
                else if (probe != null && probe.HttpStatus > 0)
                {
                    AppendLog(string.Format(UiStrings.T("KeysRetryBmwProbe"), probe.Summary), LogLevel.Warning);
                    // Full REQUEST/RESPONSE already emitted by KeyUploadProbe via BmwHttpLog when status != 200.
                }
                else
                {
                    var summary = probe != null ? probe.Summary : "?";
                    AppendLog(string.Format(UiStrings.T("KeysRetryBmwProbeSkip"), summary), LogLevel.Info);
                    RenewEngine.Log("BMW upload probe: " + summary);
                }

                // Give KeyUploader / KAI a short window to confirm upload after re-drop.
                if (!bmwOk)
                {
                    try
                    {
                        await System.Threading.Tasks.Task.Delay(2500).ConfigureAwait(true);
                        var confirm = _keyMonitor.Refresh();
                        if (confirm != null && KeyActivityMonitor.VinHasBmwUploaded(confirm.Events, vin))
                            bmwOk = true;
                        else
                        {
                            var latest = _keyMonitor.FindLatestEventForVin(vin);
                            if (latest != null && latest.BmwUploaded)
                                bmwOk = true;
                        }
                    }
                    catch { }
                }

                if (bmwOk)
                {
                    AppendLog(UiStrings.T("KeysRetryBmwOk"), LogLevel.Success);
                    try { TrayTipWindow.CloseCurrent(); }
                    catch { }
                }
                else
                {
                    AppendLog(UiStrings.T("KeysRetryBmwPending"), LogLevel.Warning);
                    // Password tip only for auth-style 403 — not probe INVALID_INPUT / #CKKPTB9009 (400).
                    if (from403Auto && probe != null && probe.HttpStatus == 403)
                        ShowBmw403StillFailTip(vin);
                }
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

        private void ShowBmw403StillFailTip(string vin)
        {
            var capture = string.IsNullOrEmpty(vin) ? "?" : vin;
            var msg = string.Format(UiStrings.T("KeysBmw403StillFail"), capture);
            AppendLog(msg, LogLevel.Warning);
            // Sticky VIN tip with Copy — same style as BMW-fail tip (not a Warning overlay).
            ShowVinInfoBalloon(capture, msg, true, true, true);
        }

        private void KeysFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_keysFilterReady || KeysFilterCombo == null) return;
            var code = KeysFilterCombo.SelectedValue as string;
            if (string.IsNullOrEmpty(code)) code = "all";
            _keysFilter = code;
            ApplyKeysFilter();
        }

        private const int KeysSearchHistoryMax = 200;

        private void KeysSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (KeysSearchClearBtn != null)
            {
                var has = KeysSearchBox != null && !string.IsNullOrEmpty(KeysSearchBox.Text);
                KeysSearchClearBtn.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            }
            if (_loadingUi) return;
            EnsureKeysSearchDebounceTimer();
            _keysSearchDebounceTimer.Stop();
            _keysSearchDebounceTimer.Start();
        }

        private void KeysSearchClear_Click(object sender, RoutedEventArgs e)
        {
            if (_keysSearchDebounceTimer != null)
                _keysSearchDebounceTimer.Stop();
            _keysSearchText = "";
            _keysSearchHistory = new List<KeyActivityEvent>();
            if (KeysSearchBox != null)
                KeysSearchBox.Text = "";
            ApplyKeysFilter();
            if (KeysSearchBox != null)
                KeysSearchBox.Focus();
        }

        private void EnsureKeysSearchDebounceTimer()
        {
            if (_keysSearchDebounceTimer != null) return;
            _keysSearchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _keysSearchDebounceTimer.Tick += delegate
            {
                _keysSearchDebounceTimer.Stop();
                ApplyKeysSearchFromBox();
            };
        }

        private void ApplyKeysSearchFromBox()
        {
            var query = KeysSearchBox != null ? (KeysSearchBox.Text ?? "").Trim() : "";
            _keysSearchText = query;
            if (query.Length == 0)
            {
                _keysSearchHistory = new List<KeyActivityEvent>();
                ApplyKeysFilter();
                return;
            }
            // Show in-memory matches immediately, then merge older history when the lookup returns.
            ApplyKeysFilter();
            var monitor = _keyMonitor;
            if (monitor == null) return;
            Task.Run(delegate
            {
                List<KeyActivityEvent> found = null;
                try { found = monitor.SearchHistory(query, KeysSearchHistoryMax); }
                catch { }
                if (found == null) return;
                try
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        // Ignore stale results if the user kept typing.
                        if (!string.Equals(_keysSearchText, query, StringComparison.Ordinal)) return;
                        _keysSearchHistory = FilterAfterKeysClear(found);
                        ApplyKeysFilter();
                    }), DispatcherPriority.Background);
                }
                catch { }
            });
        }

        private static bool SameKeyIdentity(KeyActivityEvent a, KeyActivityEvent b)
        {
            if (a == null || b == null) return false;
            if (!string.IsNullOrEmpty(a.FileId) && string.Equals(a.FileId, b.FileId, StringComparison.Ordinal))
                return true;
            if (string.IsNullOrEmpty(a.Vin) || !string.Equals(a.Vin, b.Vin, StringComparison.OrdinalIgnoreCase))
                return false;
            return Math.Abs((a.DetectedLocal - b.DetectedLocal).TotalMinutes) <= 3;
        }

        /// <summary>Append history hits that are not already in <paramref name="baseRows"/>.</summary>
        private static List<KeyActivityEvent> MergeKeysSearchHistory(
            List<KeyActivityEvent> baseRows, IList<KeyActivityEvent> history)
        {
            var merged = new List<KeyActivityEvent>(baseRows ?? new List<KeyActivityEvent>());
            if (history == null || history.Count == 0) return merged;
            var baseCount = merged.Count;
            foreach (var h in history)
            {
                if (h == null || string.IsNullOrWhiteSpace(h.Vin)) continue;
                var dup = false;
                for (var i = 0; i < baseCount; i++)
                {
                    if (SameKeyIdentity(merged[i], h)) { dup = true; break; }
                }
                if (!dup) merged.Add(h);
            }
            return merged;
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
            if (KeysAutoCopyVinCheck != null && KeysAutoCopyVinCheck.IsEnabled)
                _settings.KeysAutoCopyVin = KeysAutoCopyVinCheck.IsChecked == true;
            else
                _settings.KeysAutoCopyVin = false;
            try { _settings.Save(); }
            catch { }
            UpdateKeysAlertFromCache();
            BroadcastServerSettings();
        }

        private void SyncKeysVinBmwFailOnlyUi()
        {
            var parentOn = KeysBalloonCheck != null && KeysBalloonCheck.IsChecked == true;
            if (KeysVinTipSecondsLabel != null)
                KeysVinTipSecondsLabel.IsEnabled = parentOn;
            if (KeysVinTipSecondsBox != null)
                KeysVinTipSecondsBox.IsEnabled = parentOn;
            if (KeysVinBmwFailOnlyCheck != null)
            {
                KeysVinBmwFailOnlyCheck.IsEnabled = parentOn;
                if (!parentOn)
                {
                    KeysVinBmwFailOnlyCheck.IsChecked = false;
                    if (_settings != null)
                        _settings.KeysVinTipBmwFailOnly = false;
                }
            }
            if (KeysAutoCopyVinCheck != null)
            {
                KeysAutoCopyVinCheck.IsEnabled = parentOn;
                if (!parentOn)
                {
                    KeysAutoCopyVinCheck.IsChecked = false;
                    if (_settings != null)
                        _settings.KeysAutoCopyVin = false;
                }
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
            // Log watchers/tailers only while BMW services are healthy (avoids 1.2.26 startup pressure).
            ApplyBmwServiceLogObservation(_lastBmwReport != null && _lastBmwReport.AllOk);
        }

        /// <summary>
        /// Gate KeyUploader/KAI FileSystemWatchers + Activity log tailers on service health.
        /// </summary>
        private void ApplyBmwServiceLogObservation(bool enabled)
        {
            try
            {
                if (_keyMonitor != null)
                    _keyMonitor.SetServiceLogWatchEnabled(enabled);
            }
            catch { }

            if (enabled)
                StartBmwServiceLogTailers();
            else
                StopBmwServiceLogTailers();
        }

        private void StartBmwServiceLogTailers()
        {
            if (_settings == null) return;
            var uploaderDir = string.IsNullOrWhiteSpace(_settings.KeysUploaderLogDir)
                ? KeyActivityMonitor.DefaultUploaderLogDir
                : _settings.KeysUploaderLogDir.Trim();
            var kaiDir = string.IsNullOrWhiteSpace(_settings.KeysKaiLogDir)
                ? KeyActivityMonitor.DefaultKaiLogDir
                : _settings.KeysKaiLogDir.Trim();
            try
            {
                _uploaderLogTailer.Start(uploaderDir, null, OnBmwServiceActivityLine, "KeyUploader: ", false);
            }
            catch (Exception ex)
            {
                AppendLog("KeyUploader: tailer start failed: " + ex.Message, LogLevel.Warning);
            }
            try
            {
                _kaiLogTailer.Start(kaiDir, null, OnBmwServiceActivityLine, "KAI: ", false);
            }
            catch (Exception ex)
            {
                AppendLog("KAI: tailer start failed: " + ex.Message, LogLevel.Warning);
            }
        }

        private void StopBmwServiceLogTailers()
        {
            try
            {
                if (_uploaderLogTailer != null) _uploaderLogTailer.Stop();
            }
            catch { }
            try
            {
                if (_kaiLogTailer != null) _kaiLogTailer.Stop();
            }
            catch { }
        }

        private void OnBmwServiceActivityLine(string text, KeyReaderActivityKind kind)
        {
            // Reuse KeyReader Activity sink (classifies Verbose vs Info/Success/Warning/Error).
            OnKeyReaderActivityLine(text, kind);
        }

        private void ApplyKeysRefreshInterval()
        {
            if (_settings == null) return;
            var sec = _settings.KeysRefreshSeconds;
            if (sec < 1) sec = 1;
            if (sec > 60) sec = 60;
            _keysRefreshTimer.Interval = TimeSpan.FromSeconds(sec);
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
            BroadcastServerSettings();
        }

        private void KeysStallMinutes_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_loadingUi) return;
            SaveKeysPathSettings();
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
            BroadcastServerSettings();

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
            if (AuthRegisterBlockAfterBox != null)
                AuthRegisterBlockAfterBox.Text = GetAuthRegisterBlockAfter().ToString();
            _settings.LogVerbose = true;
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

        private void KeysRefreshSeconds_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_loadingUi) return;
            ParseAndSetKeysRefreshSeconds();
            try { if (_settings != null) _settings.Save(); }
            catch { }
            ApplyKeysRefreshInterval();
            BroadcastServerSettings();
        }

        private void KeysVinTipSeconds_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_loadingUi) return;
            ParseAndSetKeysVinTipSeconds();
            try { if (_settings != null) _settings.Save(); }
            catch { }
            BroadcastServerSettings();
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
            var sec = 10;
            if (KeysVinTipSecondsBox != null)
            {
                int parsed;
                if (!int.TryParse(KeysVinTipSecondsBox.Text.Trim(), out parsed)
                    || (parsed != 0 && parsed < 3))
                    parsed = 10;
                if (parsed > 60) parsed = 60;
                sec = parsed;
                KeysVinTipSecondsBox.Text = sec.ToString();
            }
            _settings.KeysVinTipSeconds = sec;
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
            var ev = btn.DataContext as KeyActivityEvent;
            if (ev == null || !ev.MileageKm.HasValue || ev.MileageKm.Value <= 0) return;
            CopyKmToClipboard(ev.MileageKm.Value);
            ShowVinCopyIconFeedback(btn);
        }

        private void KeysKmCopyIcon_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            var btn = sender as Button;
            if (btn == null) return;
            var ev = btn.DataContext as KeyActivityEvent;
            if (ev == null || !ev.MileageKm.HasValue || ev.MileageKm.Value <= 0) return;
            CopyKmToClipboard(ev.MileageKm.Value);
            ShowVinCopyIconFeedback(btn);
        }

        private void KeyReaderDataBinOpen_Click(object sender, RoutedEventArgs e)
        {
            OpenPathInExplorer(KeyReaderDataBinBox != null ? KeyReaderDataBinBox.Text : null);
        }

        private void KeyReaderLogPathOpen_Click(object sender, RoutedEventArgs e)
        {
            OpenPathInExplorer(KeyReaderLogPathBox != null ? KeyReaderLogPathBox.Text : null);
        }

        private void KeysSqlitePathOpen_Click(object sender, RoutedEventArgs e)
        {
            OpenPathInExplorer(KeysSqlitePathBox != null ? KeysSqlitePathBox.Text : null);
        }

        private void KeyReaderEncPathOpen_Click(object sender, RoutedEventArgs e)
        {
            OpenPathInExplorer(KeyReaderEncPathBox != null ? KeyReaderEncPathBox.Text : null);
        }

        private void KeysUploaderLogDirOpen_Click(object sender, RoutedEventArgs e)
        {
            OpenPathInExplorer(KeysUploaderLogDirBox != null ? KeysUploaderLogDirBox.Text : null);
        }

        private void KeysKaiLogDirOpen_Click(object sender, RoutedEventArgs e)
        {
            OpenPathInExplorer(KeysKaiLogDirBox != null ? KeysKaiLogDirBox.Text : null);
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
                UpdateKeysReaderWarning(snap);
                // Use filtered cache so Clear Keys clears the alert / tray error icon.
                UpdateKeysAlertFromCache();
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
            if (!string.IsNullOrEmpty(_keysSearchText) && _keysSearchHistory != null && _keysSearchHistory.Count > 0)
                src = MergeKeysSearchHistory(src as List<KeyActivityEvent>, _keysSearchHistory);
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
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i] != null)
                    list[i].Index = i + 1;
            }
            var hasKeys = _keysAllEvents != null && _keysAllEvents.Count > 0;
            if (string.Equals(_keysLastAppliedSearch, _keysSearchText, StringComparison.Ordinal)
                && KeysListUnchanged(KeysEventsList.ItemsSource as IList<KeyActivityEvent>
                    ?? KeysEventsList.ItemsSource as System.Collections.IEnumerable, list))
            {
                // Still refresh empty/no-match hints (initial load: ItemsSource null + empty list).
                KeysEventsList.Items.Refresh();
                UpdateEmptyHint(KeysEmptyHint, !hasKeys);
                UpdateNoMatchesHint(KeysNoMatchesHint, hasKeys, list.Count);
                UpdateClearButtonsEnabled();
                return;
            }

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
            UpdateEmptyHint(KeysEmptyHint, !hasKeys);
            UpdateNoMatchesHint(KeysNoMatchesHint, hasKeys, list.Count);
            UpdateClearButtonsEnabled();
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
                && a.MileageKm == b.MileageKm
                && string.Equals(a.FileId ?? "", b.FileId ?? "", StringComparison.Ordinal)
                && string.Equals(a.Vin ?? "", b.Vin ?? "", StringComparison.Ordinal)
                && string.Equals(a.Paint ?? "", b.Paint ?? "", StringComparison.Ordinal)
                && string.Equals(a.Upholstery ?? "", b.Upholstery ?? "", StringComparison.Ordinal)
                && string.Equals(a.ILevelCurrent ?? "", b.ILevelCurrent ?? "", StringComparison.Ordinal)
                && string.Equals(a.ReaderId ?? "", b.ReaderId ?? "", StringComparison.Ordinal)
                && string.Equals(a.DetailText ?? "", b.DetailText ?? "", StringComparison.Ordinal);
        }

        private List<KeyActivityEvent> FilterAfterKeysClear(IEnumerable<KeyActivityEvent> src)
        {
            var list = new List<KeyActivityEvent>();
            if (src == null) return list;
            foreach (var ev in src)
            {
                if (ev == null) continue;
                if (!KeysAlertLogic.IsAfterClear(ev.DetectedLocal, _keysClearedAfterLocal))
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

        private void CopyKmToClipboard(int km)
        {
            _uiOp = "CopyKmToClipboard";
            if (km <= 0) return;
            var text = km.ToString(System.Globalization.CultureInfo.InvariantCulture);
            try
            {
                if (!NativeMethods.TrySetClipboardText(text))
                    Clipboard.SetDataObject(text, false);
                AppendLog(string.Format(UiStrings.T("KeysKmCopied"), text), LogLevel.Success);
            }
            catch (Exception ex)
            {
                AppendLog(ex.Message, LogLevel.Error);
            }
        }

        private const int KeysReaderWarnInactiveBit = 1;
        private const int KeysReaderWarnEncBit = 2;
        /// <summary>Bitmask of bad reader states already surfaced (anti-spam); 0 = OK.</summary>
        private int _keysReaderWarnState;
        private string _keysReaderEncPath = "";

        private string BuildKeysReaderWarnText(int state)
        {
            if (state == 0) return "";
            var parts = new List<string>();
            if ((state & KeysReaderWarnInactiveBit) != 0)
                parts.Add(UiStrings.T("KeysReaderWarnInactive"));
            if ((state & KeysReaderWarnEncBit) != 0)
                parts.Add(string.Format(UiStrings.T("KeysReaderWarnEnc"),
                    string.IsNullOrEmpty(_keysReaderEncPath) ? "—" : _keysReaderEncPath));
            return string.Join(" ", parts.ToArray());
        }

        private void ApplyKeysReaderWarnUi()
        {
            var text = BuildKeysReaderWarnText(_keysReaderWarnState);
            if (KeysReaderWarnText != null)
                KeysReaderWarnText.Text = text;
            if (KeysReaderWarnBanner != null)
                KeysReaderWarnBanner.Visibility = string.IsNullOrEmpty(text)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
        }

        private void BroadcastKeysReaderWarn(int clientId)
        {
            try
            {
                if (_settings == null || !_settings.NotifyServerEnabled) return;
                if (!NotifyServer.Instance.IsRunning) return;
                var text = BuildKeysReaderWarnText(_keysReaderWarnState);
                var msg = new NotifyMessage
                {
                    Kind = "keyReaderWarn",
                    Level = string.IsNullOrEmpty(text) ? "info" : "warning",
                    Title = "KAI - AOS Renew",
                    Text = text
                };
                if (clientId > 0)
                    NotifyServer.Instance.SendTo(clientId, msg);
                else
                    BroadcastNotify(msg);
            }
            catch { }
        }

        /// <summary>Show/hide Keys reader banner; warn once per transition into a worse state.</summary>
        private void UpdateKeysReaderWarning(KeyActivitySnapshot snap)
        {
            if (snap == null) return;
            var state = 0;
            if (snap.Reader != null && snap.Reader.Ok && snap.Reader.Inactive == true)
                state |= KeysReaderWarnInactiveBit;
            if (!snap.EncFileExists)
                state |= KeysReaderWarnEncBit;
            _keysReaderEncPath = snap.EncPath ?? "";

            var previous = _keysReaderWarnState;
            var changed = state != previous;
            var worse = (state & ~previous) != 0;
            _keysReaderWarnState = state;
            ApplyKeysReaderWarnUi();
            if (!changed) return;

            // Tell clients so their banner shows/hides (including recovery).
            BroadcastKeysReaderWarn(0);
            if (!worse) return;

            var text = BuildKeysReaderWarnText(state);
            if (string.IsNullOrEmpty(text)) return;
            AppendLog(text, LogLevel.Warning);
            if (_settings != null && _settings.KeysBalloonEnabled)
            {
                // ShowBalloon also broadcasts a warning toast to connected clients.
                ShowBalloon(WinForms.ToolTipIcon.Warning, TruncateBalloon(text));
            }
            else
            {
                BroadcastNotify(new NotifyMessage
                {
                    Kind = "warning",
                    Level = "warning",
                    Title = "KAI - AOS Renew",
                    Text = TruncateBalloon(text),
                    Seconds = 8
                });
            }
        }

        private void HandleKeysNotifications(KeyActivitySnapshot snap)
        {
            if (snap == null || _settings == null) return;
            MaybeLogKeyStatusTransitions(snap);
            // VIN tips: local balloon is optional; remote clients always get the broadcast.
            MaybeShowVinInfoBalloons(snap);
            MaybeShowVinBmwFailTips(snap);
            MaybeDismissStickyVinTipOnBmwOk(snap);
            var balloonOn = _settings.KeysBalloonEnabled;
            var remediating = IsKeysBmwRemediationActive();
            if (_settings.KeysStallAlertEnabled && snap.StallEvents != null)
            {
                foreach (var ev in snap.StallEvents)
                {
                    if (ev == null) continue;
                    if (!KeysAlertLogic.IsAfterClear(ev.DetectedLocal, _keysClearedAfterLocal))
                        continue;
                    if (remediating
                        || KeyActivityMonitor.VinHasBmwUploaded(_keysAllEvents ?? snap.Events, ev.Vin))
                    {
                        // Keep pending until remediation finishes / status settles.
                        ev.StallNotified = false;
                        continue;
                    }
                    var vin = string.IsNullOrEmpty(ev.Vin) ? "?" : ev.Vin;
                    var stallMin = _settings.KeysStallMinutes < 1 ? 2 : _settings.KeysStallMinutes;
                    var msg = string.Format(UiStrings.T("KeysStallBalloon"), vin, stallMin);
                    AppendLog(msg, LogLevel.Warning);
                    // Fail-only VIN tip already covers BMW upload failure — do not overlay Warning.
                    if (balloonOn && !_settings.KeysVinTipBmwFailOnly)
                        ShowBalloon(WinForms.ToolTipIcon.Warning, TruncateBalloon(msg));
                }
            }
            MaybeRemediateKeysBmw403(snap, balloonOn);
        }

        private bool IsKeysBmwRemediationActive()
        {
            return _keysRetryBusy
                || _busy
                || !string.IsNullOrEmpty(_pending403RetryVin);
        }

        private void MaybeLogKeyStatusTransitions(KeyActivitySnapshot snap)
        {
            if (snap == null || snap.Events == null) return;
            var now = DateTime.Now;
            foreach (var ev in snap.Events)
            {
                if (ev == null || string.IsNullOrEmpty(ev.Vin)) continue;
                if ((now - ev.DetectedLocal).TotalMinutes > 30) continue;
                var id = !string.IsNullOrEmpty(ev.FileId)
                    ? ev.FileId
                    : (ev.DetectedLocal.ToString("yyyyMMddHHmmss") + ":" + ev.Vin);
                if (ev.BmwUploaded)
                {
                    if (_loggedKeyStatusKeys.Add("bmw:" + id))
                        AppendLog(FormatKeyStatusSummary(ev, "Uploaded to KAI Central"), LogLevel.Success);
                }
                else if (ev.LocalSent)
                {
                    if (_loggedKeyStatusKeys.Add("local:" + id))
                        AppendLog(FormatKeyStatusSummary(ev, "Sent to KAI Dezentral"), LogLevel.Info);
                }
                else if (ev.Failed)
                {
                    var detail = string.IsNullOrEmpty(ev.Detail) ? "upload failed" : ev.Detail.Trim();
                    if (_loggedKeyStatusKeys.Add("fail:" + id))
                        AppendLog(FormatKeyStatusSummary(ev, "BMW upload failed: " + detail), LogLevel.Warning);
                }
                if (_loggedKeyStatusKeys.Count > 400)
                {
                    _loggedKeyStatusKeys.Clear();
                }
            }
        }

        private static string FormatKeyStatusSummary(KeyActivityEvent ev, string outcome)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("VIN ").Append(ev != null && !string.IsNullOrEmpty(ev.Vin) ? ev.Vin : "?");
            sb.Append(" — ").Append(outcome ?? "");
            if (ev == null) return sb.ToString();
            if (!string.IsNullOrEmpty(ev.Paint))
                sb.Append(" | paint=").Append(ev.Paint);
            if (!string.IsNullOrEmpty(ev.Upholstery))
                sb.Append(" | uph=").Append(ev.Upholstery);
            if (ev.MileageKm > 0)
                sb.Append(" | km=").Append(ev.MileageKm);
            if (!string.IsNullOrEmpty(ev.ILevelCurrent))
                sb.Append(" | I-level=").Append(ev.ILevelCurrent);
            return sb.ToString();
        }

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
            _aosBmw403Fail = true;
            SetTokenFooter(false);

            var vin = string.IsNullOrEmpty(hit.Vin) ? "?" : hit.Vin;
            // Activity only — no premature balloon; final tip comes after retry outcome.
            AppendLog(string.Format(UiStrings.T("KeysBmw403Balloon"), vin), LogLevel.Warning);

            if (IsAuthRegisterBlocked())
            {
                AppendLog(UiStrings.T("KeysBmw403AlreadyBlocked"), LogLevel.Warning);
                return;
            }

            var limit = GetAuthRegisterBlockAfter();
            var nextStrike = _settings.AuthRegisterStrikeCount + 1;
            var willLock = nextStrike >= limit;
            AppendLog(string.Format(UiStrings.T("KeysBmw403AutoRegister"), nextStrike, limit), LogLevel.Warning);
            _pending403RetryVin = hit.Vin;
            _pending403RetryReaderId = hit.ReaderId;
            // Register (bypass cooldown); lock only when strike count reaches AuthRegisterBlockAfter.
            _registerStrikeNotedThisRun = true;
            NoteAuthRegisterStrike("BMW 403");
            Forget(RunNowAsync(false, true, null, true, willLock), "KeysBmw403Register");
        }

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

        private void MaybeShowVinBmwFailTips(KeyActivitySnapshot snap)
        {
            if (_settings == null || !_settings.KeysBalloonEnabled || !_settings.KeysVinTipBmwFailOnly)
                return;
            if (snap == null || snap.Events == null) return;
            // Wait for Register / Firebase / BMW retry — tip only if still failed afterwards.
            if (IsKeysBmwRemediationActive())
                return;
            var now = DateTime.Now;
            foreach (var ev in snap.Events)
            {
                if (ev == null || !ev.Failed || ev.BmwUploaded) continue;
                if (string.IsNullOrEmpty(ev.Vin)) continue;
                if (KeyActivityMonitor.VinHasBmwUploaded(snap.Events, ev.Vin))
                    continue;
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
                var baseMsg = string.Format(UiStrings.T("KeysVinBmwFailTip"), ev.Vin);
                var at = now.ToString("HH:mm:ss");
                var msg = string.Format(UiStrings.T("KeysVinBmwFailTipAt"), baseMsg, at);
                AppendLog(msg, LogLevel.Warning);
                // Same VIN: update sticky tip in place; otherwise show a new sticky tip.
                if (TrayTipWindow.TryUpdateStickyVinTip(ev.Vin, TruncateBalloon(msg)))
                    BroadcastVinFailTip(ev.Vin, msg);
                else
                    ShowVinInfoBalloon(ev.Vin, msg, true, true, true);
            }
        }

        private void MaybeDismissStickyVinTipOnBmwOk(KeyActivitySnapshot snap)
        {
            var vin = TrayTipWindow.CurrentStickyVin;
            if (string.IsNullOrEmpty(vin)) return;
            var events = snap != null ? snap.Events : null;
            if (events == null && _keysAllEvents != null)
                events = _keysAllEvents;
            if (!KeyActivityMonitor.VinHasBmwUploaded(events, vin))
                return;
            if (!TrayTipWindow.CloseIfVin(vin))
                return;
            BroadcastNotify(new NotifyMessage
            {
                Kind = "vindismiss",
                Level = "info",
                Title = "KAI - AOS Renew",
                Text = "",
                Vin = vin,
                Seconds = 0
            });
        }

        private void BroadcastVinFailTip(string vin, string message)
        {
            var text = TruncateBalloon(message);
            var capture = vin;
            var msg = new NotifyMessage
            {
                Kind = "vin",
                Level = "info",
                Title = "KAI - AOS Renew",
                Text = text,
                Vin = capture,
                Action = "copyVin",
                Seconds = 0
            };
            var ev = FindKeyEventByVin(capture);
            if (ev != null)
                NotifyKeySync.ApplyKeyEventFields(msg, ev);
            else
            {
                msg.At = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
                msg.Status = "Failed";
                msg.Local = 0;
                msg.Bmw = 0;
                msg.Failed = 1;
            }
            BroadcastNotify(msg);
        }

        private void ShowVinInfoBalloon(string vin, string message, bool showLocalTip = true, bool forceSticky = false, bool markFailed = false)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => ShowVinInfoBalloon(vin, message, showLocalTip, forceSticky, markFailed)));
                return;
            }
            var text = TruncateBalloon(message);
            var tipSec = forceSticky ? 0 : (_settings != null ? _settings.KeysVinTipSeconds : 10);
            var showSec = tipSec;
            if (tipSec == 0)
                showSec = -1;
            else
            {
                if (showSec < 3) showSec = 3;
                if (showSec > 60) showSec = 60;
            }
            var capture = vin;
            if (showLocalTip)
            {
                // Custom tip only — no Windows toast (NotifyIcon.ShowBalloonTip).
                TrayTipWindow.ShowTip(
                    "KAI - AOS Renew",
                    text,
                    TrayTipLevel.Info,
                    UiStrings.T("KeysCopyVin"),
                    () => CopyVinToClipboard(capture),
                    showSec,
                    () => ShowKeysTabAndSelectVin(capture),
                    showSec < 0 ? capture : null);
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
                NotifyKeySync.ApplyKeyEventFields(msg, ev);
            else
            {
                msg.At = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
                msg.Status = markFailed ? "Failed" : "Waiting";
                msg.Local = 0;
                msg.Bmw = 0;
                msg.Failed = markFailed ? 1 : 0;
            }
            if (markFailed)
            {
                msg.Failed = 1;
                if (string.IsNullOrEmpty(msg.Status) || msg.Status == "Waiting")
                    msg.Status = "Failed";
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
            // Same rows as the Keys tab (FilterMeaningful), newest first — no VIN collapse.
            var src = FilterMeaningful(_keysAllEvents);
            src.Sort(delegate(KeyActivityEvent a, KeyActivityEvent b)
            {
                return b.DetectedLocal.CompareTo(a.DetectedLocal);
            });
            foreach (var ev in src)
            {
                if (ev == null || string.IsNullOrEmpty(ev.Vin)) continue;
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
                    .Append(NotifyKeySync.StatusName(ev.Status)).Append('\t')
                    .Append(ev.MileageKm.HasValue ? ev.MileageKm.Value.ToString() : "").Append('\t')
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
                var rows = GetRecentKeyRows(NotifyMessage.MaxKeySyncRows) ?? new List<KeyActivityEvent>();
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
                    Text = NotifyKeySync.EncodeKeySync(rows) ?? ""
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

        private void KeysOpenDebugFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                OpenFolder(KeyActivityMonitor.KeyDebugStoreDirectory);
            }
            catch { }
        }

        private void KeysOpenBinBtn_Loaded(object sender, RoutedEventArgs e)
        {
            SyncKeysOpenBinBtn(sender as Button);
        }

        private void KeysOpenBinBtn_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            SyncKeysOpenBinBtn(sender as Button);
        }

        private void SyncKeysOpenBinBtn(Button btn)
        {
            if (btn == null) return;
            var ev = btn.DataContext as KeyActivityEvent;
            var path = ev != null ? ev.DebugBinPath : null;
            var ok = _debugMode && !string.IsNullOrEmpty(path) && File.Exists(path);
            btn.IsEnabled = ok;
            btn.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
            // Content is the icon Viewbox from XAML — do not replace with text.
            btn.ToolTip = UiStrings.T("KeysOpenBinTip");
        }

        private void KeysOpenBinBtn_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var ev = btn != null ? btn.DataContext as KeyActivityEvent : null;
            var path = ev != null ? ev.DebugBinPath : null;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                DiagProcess.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "/select,\"" + path + "\"",
                    UseShellExecute = true
                });
            }
            catch
            {
                OpenPathInExplorer(path);
            }
        }

        private void SendKeyBinToClient(int clientId, string vin)
        {
            try
            {
                var path = KeyActivityMonitor.ResolveDebugBinForVin(vin);
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    // Fallback: match in-memory event DebugBinPath
                    if (_keysAllEvents != null)
                    {
                        for (var i = _keysAllEvents.Count - 1; i >= 0; i--)
                        {
                            var ev = _keysAllEvents[i];
                            if (ev == null || string.IsNullOrEmpty(ev.Vin)) continue;
                            if (!string.Equals(ev.Vin, vin, StringComparison.OrdinalIgnoreCase)) continue;
                            if (!string.IsNullOrEmpty(ev.DebugBinPath) && File.Exists(ev.DebugBinPath))
                            {
                                path = ev.DebugBinPath;
                                break;
                            }
                        }
                    }
                }
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    SendAosAck(clientId, false, "Bin not found for VIN");
                    return;
                }
                var bytes = File.ReadAllBytes(path);
                var name = Path.GetFileName(path);
                NotifyServer.Instance.SendTo(clientId, new NotifyMessage
                {
                    Kind = "keyBin",
                    Level = "info",
                    Title = "KAI - AOS Renew",
                    Vin = vin ?? "",
                    Text = name + "|" + Convert.ToBase64String(bytes)
                });
                SendAosAck(clientId, true, "getKeyBin");
            }
            catch (Exception ex)
            {
                SendAosAck(clientId, false, ex.Message);
            }
        }

        private const string SimTestVin = "WBATESTVXN0000001";
        private const string SimTestVinB = "WBATESTVXN0000002";

        private string BuildSimBmwFailTipText(string vin)
        {
            var baseMsg = string.Format(UiStrings.T("KeysVinBmwFailTip"), vin);
            var at = DateTime.Now.ToString("HH:mm:ss");
            return string.Format(UiStrings.T("KeysVinBmwFailTipAt"), baseMsg, at);
        }

        private void ShowSimBmwFailTip(string vin)
        {
            var msg = BuildSimBmwFailTipText(vin);
            AppendLog("Sim: " + msg, LogLevel.Warning);
            if (TrayTipWindow.TryUpdateStickyVinTip(vin, TruncateBalloon(msg)))
            {
                BroadcastVinFailTip(vin, msg);
                return;
            }
            ShowVinInfoBalloon(vin, msg, true, true, true);
        }

        private void SimBmwUploadFail_Click(object sender, RoutedEventArgs e)
        {
            if (!_debugMode) return;
            ShowSimBmwFailTip(SimTestVin);
        }

        private void SimBmwUploadFailAgain_Click(object sender, RoutedEventArgs e)
        {
            if (!_debugMode) return;
            ShowSimBmwFailTip(SimTestVin);
        }

        private void SimBmwUploadOk_Click(object sender, RoutedEventArgs e)
        {
            if (!_debugMode) return;
            AppendLog("Sim: BMW upload OK for " + SimTestVin, LogLevel.Success);
            TrayTipWindow.CloseIfVin(SimTestVin);
            TrayTipWindow.CloseIfVin(SimTestVinB);
            BroadcastNotify(new NotifyMessage
            {
                Kind = "vindismiss",
                Level = "info",
                Title = "KAI - AOS Renew",
                Text = "",
                Vin = SimTestVin,
                Seconds = 0
            });
        }

        private void SimVinRead_Click(object sender, RoutedEventArgs e)
        {
            if (!_debugMode) return;
            var msg = string.Format(UiStrings.T("KeysNewKeyBalloon"), SimTestVin);
            AppendLog("Sim: " + msg, LogLevel.Info);
            ShowVinInfoBalloon(SimTestVin, msg, true, false, false);
        }

        private void SimVinSticky_Click(object sender, RoutedEventArgs e)
        {
            if (!_debugMode) return;
            var msg = string.Format(UiStrings.T("KeysNewKeyBalloon"), SimTestVin);
            AppendLog("Sim sticky: " + msg, LogLevel.Info);
            ShowVinInfoBalloon(SimTestVin, msg, true, true, false);
        }

        private void SimAltVinFail_Click(object sender, RoutedEventArgs e)
        {
            if (!_debugMode) return;
            ShowSimBmwFailTip(SimTestVinB);
        }

        private void SimWarningOverSticky_Click(object sender, RoutedEventArgs e)
        {
            if (!_debugMode) return;
            var stallMin = _settings != null && _settings.KeysStallMinutes >= 1
                ? _settings.KeysStallMinutes
                : 2;
            var msg = string.Format(UiStrings.T("KeysStallBalloon"), SimTestVin, stallMin);
            AppendLog("Sim warning over sticky: " + msg, LogLevel.Warning);
            // No Copy action — sticky VIN tip must remain (TrayTipWindow protection).
            ShowBalloon(WinForms.ToolTipIcon.Warning, TruncateBalloon(msg));
        }

        private void Sim403StillFail_Click(object sender, RoutedEventArgs e)
        {
            if (!_debugMode) return;
            ShowBmw403StillFailTip(SimTestVin);
        }

        private KeyActivityEvent EnsureSimKeyEvent(string vin, bool local, bool bmw, bool failed)
        {
            if (_keysAllEvents == null)
                _keysAllEvents = new List<KeyActivityEvent>();
            KeyActivityEvent ev = null;
            foreach (var e in _keysAllEvents)
            {
                if (e == null || string.IsNullOrEmpty(e.Vin)) continue;
                if (string.Equals(e.Vin, vin, StringComparison.OrdinalIgnoreCase))
                {
                    ev = e;
                    break;
                }
            }
            if (ev == null)
            {
                ev = new KeyActivityEvent
                {
                    Vin = vin,
                    DetectedLocal = DateTime.Now,
                    FileId = "sim:" + vin,
                    Detail = "simulation"
                };
                _keysAllEvents.Insert(0, ev);
            }
            ev.LocalSent = local;
            ev.BmwUploaded = bmw;
            ev.Failed = failed;
            if (failed) ev.Status = KeyEventStatus.Failed;
            else if (bmw) ev.Status = KeyEventStatus.BmwOk;
            else if (local) ev.Status = KeyEventStatus.LocalOk;
            else ev.Status = KeyEventStatus.Waiting;
            ev.DetectedLocal = DateTime.Now;
            return ev;
        }

        private void SimLocalOkRow_Click(object sender, RoutedEventArgs e)
        {
            if (!_debugMode) return;
            EnsureSimKeyEvent(SimTestVin, true, false, false);
            AppendLog("Sim: Local OK row for " + SimTestVin, LogLevel.Info);
            ApplyKeysFilter();
            UpdateKeysRetryButton();
            MaybeBroadcastKeySync();
        }

        private void SimBmwUploadedRow_Click(object sender, RoutedEventArgs e)
        {
            if (!_debugMode) return;
            EnsureSimKeyEvent(SimTestVin, true, true, false);
            AppendLog("Sim: BmwUploaded row for " + SimTestVin + " (tip not dismissed)", LogLevel.Success);
            ApplyKeysFilter();
            UpdateKeysRetryButton();
            MaybeBroadcastKeySync();
        }

        private void SimRetryBmwBusy_Click(object sender, RoutedEventArgs e)
        {
            if (!_debugMode) return;
            _keysRetryBusy = true;
            UpdateKeysRetryButton();
            AppendLog("Sim: Retry BMW busy (5s)", LogLevel.Info);
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            t.Tick += (s, args) =>
            {
                t.Stop();
                _keysRetryBusy = false;
                UpdateKeysRetryButton();
                AppendLog("Sim: Retry BMW busy cleared", LogLevel.Info);
            };
            t.Start();
        }

        private void SimStallFailOnly_Click(object sender, RoutedEventArgs e)
        {
            if (!_debugMode) return;
            var stallMin = _settings != null && _settings.KeysStallMinutes >= 1
                ? _settings.KeysStallMinutes
                : 2;
            var msg = string.Format(UiStrings.T("KeysStallBalloon"), SimTestVin, stallMin);
            // Log only — no balloon (fail-only path).
            AppendLog("Sim stall fail-only (no balloon): " + msg, LogLevel.Warning);
        }

        private void SimStallAlert_Click(object sender, RoutedEventArgs e)
        {
            if (!_debugMode) return;
            var stallMin = _settings != null && _settings.KeysStallMinutes >= 1
                ? _settings.KeysStallMinutes
                : 2;
            var msg = string.Format(UiStrings.T("KeysStallBalloon"), SimTestVin, stallMin);
            AppendLog("Sim: " + msg, LogLevel.Warning);
            ShowBalloon(WinForms.ToolTipIcon.Warning, TruncateBalloon(msg));
        }

        private void SimToastInfo_Click(object sender, RoutedEventArgs e)
        {
            if (!_debugMode) return;
            TrayTipWindow.ShowTip("KAI - AOS Renew", UiStrings.T("BalloonInfo"), TrayTipLevel.Info);
            BroadcastNotify(new NotifyMessage
            {
                Kind = "info",
                Level = "info",
                Title = "KAI - AOS Renew",
                Text = UiStrings.T("BalloonInfo"),
                Seconds = 8
            });
        }

        private void SimToastWarning_Click(object sender, RoutedEventArgs e)
        {
            if (!_debugMode) return;
            ShowBalloon(WinForms.ToolTipIcon.Warning, UiStrings.T("BalloonWarning"));
        }

        private void SimToastError_Click(object sender, RoutedEventArgs e)
        {
            if (!_debugMode) return;
            ShowBalloon(WinForms.ToolTipIcon.Error, UiStrings.T("BalloonError"));
        }

        private void SimAuthCooldown_Click(object sender, RoutedEventArgs e)
        {
            if (!_debugMode) return;
            BeginAuthCooldown();
        }

        private void SimRegisterBlocked_Click(object sender, RoutedEventArgs e)
        {
            if (!_debugMode) return;
            SetAuthRegisterBlocked(true);
            RefreshAuthCooldownBanner();
            QueueFooterPublish();
            BroadcastServerSettings();
            AppendLog("Sim: Register blocked ON", LogLevel.Warning);
        }

        private void SimClipboardPass_Click(object sender, RoutedEventArgs e)
        {
            if (!_debugMode) return;
            ForceShowClipboardPassBanner("sim-pass");
        }

        private void SimServicesProblem_Click(object sender, RoutedEventArgs e)
        {
            if (!_debugMode) return;
            UpdateServicesFooter(false);
            var msg = string.Format(UiStrings.T("KaiServicesProblem"), "simulation");
            AppendLog("Sim: " + msg, LogLevel.Warning);
            ShowBalloon(WinForms.ToolTipIcon.Warning, TruncateBalloon(msg));
        }

        private void SimUpdateAvailable_Click(object sender, RoutedEventArgs e)
        {
            if (!_debugMode) return;
            ShowSimUpdateBanner();
        }

        private void SimBinAvailable_Click(object sender, RoutedEventArgs e)
        {
            if (!_debugMode) return;
            EnsureSimKeyEvent(SimTestVin, true, false, true);
            _simMockRetryBin = true;
            ApplyKeysFilter();
            UpdateKeysRetryButton();
            MaybeBroadcastKeySync();
            AppendLog("Sim: Bin / Retry ready for " + SimTestVin, LogLevel.Info);
        }

        private void SimRegisterOk_Click(object sender, RoutedEventArgs e)
        {
            if (!_debugMode) return;
            var text = "OK: Registration OK (simulation)";
            AppendLog(text, LogLevel.Success);
            MaybeAppendRegisterHistory(text, DateTime.Now);
            TrayTipWindow.ShowTip("KAI - AOS Renew", text, TrayTipLevel.Info);
        }

        private void SimRegisterFail_Click(object sender, RoutedEventArgs e)
        {
            if (!_debugMode) return;
            var text = "FAIL: Register failed (simulation)";
            AppendLog(text, LogLevel.Error);
            MaybeAppendRegisterHistory(text, DateTime.Now);
            ShowBalloon(WinForms.ToolTipIcon.Error, TruncateBalloon(text));
        }

        private void SimCloseTip_Click(object sender, RoutedEventArgs e)
        {
            if (!_debugMode) return;
            TrayTipWindow.CloseCurrent();
        }

        private void NotifyTestVin_Click(object sender, RoutedEventArgs e)
        {
            SetNotifyTestResult(UiStrings.T("NotifyTestSending"), false);
            var tipSec = _settings != null ? _settings.KeysVinTipSeconds : 10;
            if (tipSec != 0)
            {
                if (tipSec < 3) tipSec = 3;
                if (tipSec > 60) tipSec = 60;
            }
            SendNotifyTest(new NotifyMessage
            {
                Kind = "vin",
                Level = "info",
                Title = "KAI - AOS Renew",
                Text = "Test VIN WBATESTVXN0000001",
                Vin = "WBATESTVXN0000001",
                Action = "copyVin",
                Seconds = tipSec
            }, "VIN");
        }

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
                    StopStartupServiceRecheck();
                    ApplyBmwServiceLogObservation(true);
                    ResetAutoFixCounters();
                    return;
                }

                // Unhealthy: stop log watchers/tailers so we do not hammer dying services.
                ApplyBmwServiceLogObservation(false);

                // Startup: refresh UI now, but wait ~12s before balloon/log — services often still StartPending.
                if (forceBalloon && !_startupServiceBalloonDeferred)
                {
                    _lastServiceProblemSignature = report.ProblemSignature
                        ?? string.Format(UiStrings.T("KaiServicesProblem"), report.AlertMessage ?? "");
                    _serviceCheckDoneOnce = true;
                    ScheduleStartupServiceRecheck();
                    return;
                }

                var msg = string.Format(UiStrings.T("KaiServicesProblem"), report.AlertMessage ?? "");
                var sig = report.ProblemSignature ?? msg;
                var changed = !string.Equals(sig, _lastServiceProblemSignature, StringComparison.Ordinal);
                var shouldBalloon = forceBalloon || forceBalloonOnProblem || changed;
                var shouldLog = forceBalloon || forceBalloonOnProblem || changed || !_serviceCheckDoneOnce;

                if (shouldLog)
                    AppendLog(msg, LogLevel.Error);

                // After startup defer (and on later Stopped transitions): rate-limited auto-fix.
                if (ServicesExistButUnhealthy(report) && !_autoFixInFlight && !_busy)
                {
                    string limitReason;
                    if (TryBeginAutoFix(out limitReason))
                    {
                        _lastServiceProblemSignature = sig;
                        _serviceCheckDoneOnce = true;
                        AppendLog(UiStrings.T("KaiAutoFixStart"), LogLevel.Info);
                        Forget(AutoFixBmwServicesAsync(), "AutoFixBmwServices");
                        return;
                    }
                    if (shouldBalloon && !string.IsNullOrEmpty(limitReason))
                    {
                        AppendLog(limitReason, LogLevel.Warning);
                        ShowBalloon(WinForms.ToolTipIcon.Error, TruncateBalloon(msg));
                        _lastServiceProblemSignature = sig;
                        _serviceCheckDoneOnce = true;
                        return;
                    }
                }

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
                try { ApplyBmwServiceLogObservation(false); }
                catch { }
                AppendLog(ex.Message, LogLevel.Error);
                ShowBalloon(WinForms.ToolTipIcon.Error, TruncateBalloon(ex.Message));
            }
            finally
            {
                BroadcastKaiSync();
            }
        }

        private static bool ServicesExistButUnhealthy(BmwServicesReport report)
        {
            if (report == null || report.Services == null || report.Services.Count == 0)
                return false;
            var anyExists = false;
            for (var i = 0; i < report.Services.Count; i++)
            {
                var s = report.Services[i];
                if (s == null) continue;
                if (!s.Exists) return false;
                anyExists = true;
            }
            return anyExists && !report.AllOk;
        }

        private bool TryBeginAutoFix(out string limitReason)
        {
            limitReason = null;
            var now = DateTime.UtcNow;
            if ((now - _lastAutoFixUtc).TotalMinutes < 3)
            {
                limitReason = UiStrings.T("KaiAutoFixLimited");
                return false;
            }
            while (_autoFixHourWindow.Count > 0
                && (now - _autoFixHourWindow.Peek()).TotalHours >= 1)
                _autoFixHourWindow.Dequeue();
            if (_autoFixHourWindow.Count >= 3)
            {
                limitReason = UiStrings.T("KaiAutoFixLimited");
                return false;
            }
            _autoFixInFlight = true;
            _lastAutoFixUtc = now;
            _autoFixHourWindow.Enqueue(now);
            return true;
        }

        private void ResetAutoFixCounters()
        {
            // Clear hourly budget after sustained OK; keep _lastAutoFixUtc so the
            // 3-minute gap still applies if services crash again immediately.
            _autoFixHourWindow.Clear();
        }

        private async System.Threading.Tasks.Task AutoFixBmwServicesAsync()
        {
            try
            {
                await KaiFixServicesCoreAsync(true);
            }
            finally
            {
                _autoFixInFlight = false;
            }
        }

        private void ScheduleStartupServiceRecheck()
        {
            if (_startupServiceRecheckTimer == null)
            {
                _startupServiceRecheckTimer = new DispatcherTimer();
                _startupServiceRecheckTimer.Tick += StartupServiceRecheckTimer_Tick;
            }
            _startupServiceRecheckTimer.Interval = TimeSpan.FromSeconds(12);
            _startupServiceRecheckTimer.Stop();
            _startupServiceRecheckTimer.Start();
        }

        private void StopStartupServiceRecheck()
        {
            try
            {
                if (_startupServiceRecheckTimer != null)
                    _startupServiceRecheckTimer.Stop();
            }
            catch { }
        }

        private void StartupServiceRecheckTimer_Tick(object sender, EventArgs e)
        {
            StopStartupServiceRecheck();
            if (_exitRequested) return;
            _startupServiceBalloonDeferred = true;
            try
            {
                // Second pass: allow forceBalloon to raise tray/log if still not OK.
                CheckBmwServices(true, false);
            }
            finally
            {
                _startupServiceBalloonDeferred = false;
            }
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

        private void IntervalBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            ApplyIntervalFromBox(true);
        }

    }
}
