using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KaiAosRenew;

namespace KaiAosNotifyClient
{
    public partial class MainWindow : Window
    {
        private enum ShortcutTab { None, Keys, Activity, Register }

        private void ApplyShortcutStrings()
        {
            var clearTip = ClientUiStrings.T("SearchClearTip");
            if (KeysSearchClearBtn != null) KeysSearchClearBtn.ToolTip = clearTip;
            if (ActivitySearchClearBtn != null) ActivitySearchClearBtn.ToolTip = clearTip;
            if (RegisterSearchClearBtn != null) RegisterSearchClearBtn.ToolTip = clearTip;
            if (RegisterEmptyHint != null) RegisterEmptyHint.Text = ClientUiStrings.T("RegisterEmptyHint");
            if (FavHostBtn != null) FavHostBtn.ToolTip = ClientUiStrings.T("FavoriteHost");
            if (SrvDownloadDiagBundleBtn != null)
            {
                SrvDownloadDiagBundleBtn.Content = ClientUiStrings.T("DiagBundle");
                SrvDownloadDiagBundleBtn.ToolTip = ClientUiStrings.T("DiagBundleTip");
            }
        }

        private static void SetSearchClearVisibility(Button clearBtn, TextBox box)
        {
            if (clearBtn == null) return;
            var has = box != null && !string.IsNullOrEmpty(box.Text);
            clearBtn.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ActivitySearchClear_Click(object sender, RoutedEventArgs e)
        {
            if (_activitySearchDebounceTimer != null)
                _activitySearchDebounceTimer.Stop();
            _activitySearchText = "";
            if (ActivitySearchBox != null)
                ActivitySearchBox.Text = "";
            SetSearchClearVisibility(ActivitySearchClearBtn, ActivitySearchBox);
            RefreshActivityView();
            if (ActivitySearchBox != null)
                ActivitySearchBox.Focus();
        }

        private void RegisterSearchClear_Click(object sender, RoutedEventArgs e)
        {
            if (_registerSearchDebounceTimer != null)
                _registerSearchDebounceTimer.Stop();
            _registerSearchText = "";
            if (RegisterSearchBox != null)
                RegisterSearchBox.Text = "";
            SetSearchClearVisibility(RegisterSearchClearBtn, RegisterSearchBox);
            ApplyRegisterFilter();
            if (RegisterSearchBox != null)
                RegisterSearchBox.Focus();
        }

        private ShortcutTab GetActiveShortcutTab()
        {
            if (_aboutVisible || MainTabs == null) return ShortcutTab.None;
            var sel = MainTabs.SelectedItem;
            if (sel == null) return ShortcutTab.None;
            if (ReferenceEquals(sel, TabKeys)) return ShortcutTab.Keys;
            if (ReferenceEquals(sel, TabActivity)) return ShortcutTab.Activity;
            if (ReferenceEquals(sel, TabRegister)) return ShortcutTab.Register;
            return ShortcutTab.None;
        }

        private TextBox GetActiveSearchBox()
        {
            switch (GetActiveShortcutTab())
            {
                case ShortcutTab.Keys: return KeysSearchBox;
                case ShortcutTab.Activity: return ActivitySearchBox;
                case ShortcutTab.Register: return RegisterSearchBox;
            }
            return null;
        }

        private bool FocusActiveTabSearch()
        {
            var box = GetActiveSearchBox();
            if (box == null || !box.IsVisible || !box.IsEnabled) return false;
            box.Focus();
            Keyboard.Focus(box);
            box.SelectAll();
            return true;
        }

        private bool RefreshActiveTab()
        {
            switch (GetActiveShortcutTab())
            {
                case ShortcutTab.Keys:
                    KeysRefresh_Click(this, new RoutedEventArgs());
                    return true;
                case ShortcutTab.Activity:
                    ActivityRefresh_Click(this, new RoutedEventArgs());
                    return true;
                case ShortcutTab.Register:
                    RegisterRefresh_Click(this, new RoutedEventArgs());
                    return true;
            }
            return false;
        }

        private bool EscapeAction()
        {
            if (WpfFocusHelpers.IsComboDropDownOpenInFocus()) return false;
            if (_aboutVisible)
            {
                HideAboutPanel();
                return true;
            }
            switch (GetActiveShortcutTab())
            {
                case ShortcutTab.Keys:
                    if (KeysSearchBox != null && !string.IsNullOrEmpty(KeysSearchBox.Text))
                    {
                        KeysSearchClear_Click(this, new RoutedEventArgs());
                        return true;
                    }
                    break;
                case ShortcutTab.Activity:
                    if (ActivitySearchBox != null && !string.IsNullOrEmpty(ActivitySearchBox.Text))
                    {
                        ActivitySearchClear_Click(this, new RoutedEventArgs());
                        return true;
                    }
                    break;
                case ShortcutTab.Register:
                    if (RegisterSearchBox != null && !string.IsNullOrEmpty(RegisterSearchBox.Text))
                    {
                        RegisterSearchClear_Click(this, new RoutedEventArgs());
                        return true;
                    }
                    break;
            }
            return false;
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Handled) return;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var mods = Keyboard.Modifiers;
            try
            {
                if (key == Key.F && mods == ModifierKeys.Control)
                {
                    if (FocusActiveTabSearch()) e.Handled = true;
                }
                else if (key == Key.F5 && mods == ModifierKeys.None)
                {
                    if (RefreshActiveTab()) e.Handled = true;
                }
                else if (key == Key.Escape && mods == ModifierKeys.None)
                {
                    if (EscapeAction()) e.Handled = true;
                }
                else if (key == Key.Enter && mods == ModifierKeys.None)
                {
                    // Enter on host/port while disconnected → Connect.
                    if (_client != null && !_client.IsConnected
                        && (ReferenceEquals(Keyboard.FocusedElement, HostCombo)
                            || ReferenceEquals(Keyboard.FocusedElement, PortBox)
                            || IsFocusInside(HostCombo)
                            || IsFocusInside(PortBox)))
                    {
                        ConnectBtn_Click(this, new RoutedEventArgs());
                        e.Handled = true;
                    }
                }
            }
            catch { }
        }

        private static bool IsFocusInside(DependencyObject root)
        {
            if (root == null) return false;
            var d = Keyboard.FocusedElement as DependencyObject;
            while (d != null)
            {
                if (ReferenceEquals(d, root)) return true;
                var parent = (d is Visual || d is System.Windows.Media.Media3D.Visual3D)
                    ? VisualTreeHelper.GetParent(d)
                    : LogicalTreeHelper.GetParent(d);
                d = parent;
            }
            return false;
        }
    }
}
