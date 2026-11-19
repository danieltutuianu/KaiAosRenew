using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace KaiAosRenew
{
    // Keyboard shortcuts, search-clear buttons and theme-aware status brushes.
    public partial class MainWindow : Window
    {
        private enum ShortcutTab { None, Keys, Activity, Register }

        private static readonly SolidColorBrush StatusWarnFallbackBrush = FrozenBrush(0xB2, 0x6A, 0x00);
        private static readonly SolidColorBrush StatusOkFallbackBrush = FrozenBrush(0x2E, 0x7D, 0x32);
        private static readonly SolidColorBrush StatusErrorFallbackBrush = FrozenBrush(0xC6, 0x28, 0x28);
        private static readonly SolidColorBrush StatusStoppedFallbackBrush = FrozenBrush(0x8C, 0x8C, 0x8C);
        private static readonly SolidColorBrush TextMutedFallbackBrush = FrozenBrush(0x88, 0x88, 0x88);

        private static SolidColorBrush FrozenBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        // Theme resource first (follows Day / Night); built-in colour only if the key is missing.
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

        private Brush StatusOk() { return ThemeBrush("StatusOkBrush", StatusOkFallbackBrush); }
        private Brush StatusWarn() { return ThemeBrush("StatusWarnBrush", StatusWarnFallbackBrush); }
        private Brush StatusErr() { return ThemeBrush("StatusErrorBrush", StatusErrorFallbackBrush); }
        private Brush StatusStopped() { return ThemeBrush("StatusStoppedBrush", StatusStoppedFallbackBrush); }
        private Brush TextMuted() { return ThemeBrush("TextMutedBrush", TextMutedFallbackBrush); }

        // Re-apply brushes that were assigned from code after a Day / Night switch.
        private void RefreshThemeDependentBrushes()
        {
            try
            {
                UpdateAosLoginStatus();
                RefreshAuthCooldownBanner(); // also refreshes the auth-guards footer
                UpdateAosDirtyStatus();
            }
            catch { }
        }

        private void ApplyShortcutStrings()
        {
            var clearTip = UiStrings.T("SearchClearTip");
            if (KeysSearchClearBtn != null) KeysSearchClearBtn.ToolTip = clearTip;
            if (ActivitySearchClearBtn != null) ActivitySearchClearBtn.ToolTip = clearTip;
            if (RegisterSearchClearBtn != null) RegisterSearchClearBtn.ToolTip = clearTip;
            if (RegisterEmptyHint != null) RegisterEmptyHint.Text = UiStrings.T("RegisterEmptyHint");
            // Row copy buttons localise their own automation names in UpdateVin/KmCopyIconVisibility.
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
            RefreshLogView();
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

        // Ctrl+F = focus search, F5 = refresh active tab, Esc = clear search / close About.
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
            }
            catch { }
        }
    }
}
