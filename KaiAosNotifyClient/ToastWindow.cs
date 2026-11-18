using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace KaiAosNotifyClient
{
    /// <summary>Toast tip styled like KaiAosRenew TrayBalloonWindow (Copy VIN support).</summary>
    internal sealed class ToastWindow : Window
    {
        private static ToastWindow _current;
        private readonly DispatcherTimer _closeTimer;
        private readonly bool _sticky;
        private readonly bool _hasBodyActivate;
        private Action _action;
        private Action _onBodyActivate;
        private int _remainingSeconds;
        private TextBlock _countdownText;

        private ToastWindow(
            string title,
            string text,
            string level,
            string actionLabel,
            Action action,
            int durationSeconds,
            Action onBodyActivate)
        {
            Title = title;
            Width = 360;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            ShowActivated = false;
            FontFamily = new FontFamily("Segoe UI");

            _action = action;
            _onBodyActivate = onBodyActivate;
            _hasBodyActivate = onBodyActivate != null;
            _sticky = durationSeconds < 0;
            var hasAction = !string.IsNullOrEmpty(actionLabel) && action != null;

            Color accent;
            var lv = (level ?? "info").ToLowerInvariant();
            if (lv == "warning")
            {
                accent = Color.FromRgb(200, 140, 20);
            }
            else if (lv == "error")
            {
                accent = Color.FromRgb(180, 50, 40);
            }
            else
            {
                accent = Color.FromRgb(30, 120, 200);
            }

            var night = ClientThemeManager.IsNight;
            var panelBg = night ? Color.FromRgb(45, 45, 45) : Color.FromRgb(250, 248, 244);
            var panelBorder = night ? Color.FromRgb(90, 90, 90) : Color.FromRgb(170, 160, 145);
            var titleFg = night ? Color.FromRgb(232, 232, 232) : Color.FromRgb(40, 40, 40);
            var bodyFg = night ? Color.FromRgb(180, 180, 180) : Color.FromRgb(70, 70, 70);
            var btnBg = night ? Color.FromRgb(60, 60, 60) : Color.FromRgb(235, 230, 220);
            var btnBorder = night ? Color.FromRgb(110, 110, 110) : Color.FromRgb(160, 150, 135);

            var border = new Border
            {
                Background = new SolidColorBrush(panelBg),
                BorderBrush = new SolidColorBrush(panelBorder),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 12,
                    ShadowDepth = 2,
                    Opacity = 0.35,
                    Color = Colors.Black
                }
            };

            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var accentBar = new Border
            {
                Background = new SolidColorBrush(accent),
                CornerRadius = new CornerRadius(4, 0, 0, 4)
            };
            Grid.SetColumn(accentBar, 0);

            var textPanel = new StackPanel { Margin = new Thickness(12, 12, 14, 12) };
            var titleRow = new Grid();
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var titleBlock = new TextBlock
            {
                Text = title ?? "KAI - AOS Renew",
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Foreground = new SolidColorBrush(titleFg),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(titleBlock, 0);
            titleRow.Children.Add(titleBlock);
            if (!_sticky)
            {
                _countdownText = new TextBlock
                {
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var countdownBadge = new Border
                {
                    MinWidth = 28,
                    Height = 26,
                    CornerRadius = new CornerRadius(13),
                    Background = new SolidColorBrush(accent),
                    Padding = new Thickness(6, 0, 6, 0),
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Top,
                    Child = _countdownText
                };
                Grid.SetColumn(countdownBadge, 1);
                titleRow.Children.Add(countdownBadge);
            }
            textPanel.Children.Add(titleRow);
            textPanel.Children.Add(new TextBlock
            {
                Text = text ?? "",
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 0),
                Foreground = new SolidColorBrush(bodyFg),
                TextWrapping = TextWrapping.Wrap
            });

            if (hasAction)
            {
                var btn = new Button
                {
                    Content = actionLabel,
                    Margin = new Thickness(0, 10, 0, 0),
                    Padding = new Thickness(12, 4, 12, 4),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Cursor = Cursors.Hand,
                    Background = new SolidColorBrush(btnBg),
                    BorderBrush = new SolidColorBrush(btnBorder),
                    BorderThickness = new Thickness(1),
                    Foreground = new SolidColorBrush(titleFg),
                    FontSize = 12
                };
                btn.Click += delegate
                {
                    try { if (_action != null) _action(); }
                    catch { }
                    Close();
                };
                textPanel.Children.Add(btn);
            }

            if (!_sticky)
            {
                MouseEnter += delegate { if (_closeTimer != null && _closeTimer.IsEnabled) _closeTimer.Stop(); };
                MouseLeave += delegate { if (_closeTimer != null && !_closeTimer.IsEnabled) _closeTimer.Start(); };
            }

            Grid.SetColumn(textPanel, 1);
            root.Children.Add(accentBar);
            root.Children.Add(textPanel);
            border.Child = root;
            Content = border;

            if (!_sticky)
            {
                MouseLeftButtonUp += Toast_MouseLeftButtonUp;
                MouseDoubleClick += Toast_MouseDoubleClick;
            }

            if (_sticky)
            {
                _closeTimer = new DispatcherTimer();
            }
            else
            {
                var seconds = durationSeconds;
                if (seconds <= 0) seconds = hasAction ? 8 : 4;
                if (seconds < 3) seconds = 3;
                if (seconds > 60) seconds = 60;
                _remainingSeconds = seconds;
                UpdateCountdownText();
                _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _closeTimer.Tick += CloseTimer_Tick;
            }
        }

        private void Toast_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_sticky) return;
            if (e.OriginalSource is Button || IsDescendantOfButton(e.OriginalSource as DependencyObject))
                return;
            // When double-click can navigate, ignore single click so the second click can land.
            if (_hasBodyActivate)
                return;
            Close();
        }

        private void Toast_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_sticky) return;
            if (e.OriginalSource is Button || IsDescendantOfButton(e.OriginalSource as DependencyObject))
                return;
            if (!_hasBodyActivate) return;
            e.Handled = true;
            try
            {
                if (_onBodyActivate != null)
                    _onBodyActivate();
            }
            catch { }
            Close();
        }

        private static bool IsDescendantOfButton(DependencyObject node)
        {
            while (node != null)
            {
                if (node is Button) return true;
                node = VisualTreeHelper.GetParent(node);
            }
            return false;
        }

        private void CloseTimer_Tick(object sender, EventArgs e)
        {
            if (_sticky) return;
            _remainingSeconds--;
            if (_remainingSeconds <= 0)
            {
                _closeTimer.Stop();
                Close();
                return;
            }
            UpdateCountdownText();
        }

        private void UpdateCountdownText()
        {
            if (_countdownText == null) return;
            _countdownText.Text = _remainingSeconds + "s";
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            var work = SystemParameters.WorkArea;
            Left = work.Right - Width - 12;
            Top = work.Bottom - ActualHeight - 12;
            if (Top < work.Top) Top = work.Top + 12;
            if (!_sticky && !_closeTimer.IsEnabled) _closeTimer.Start();
        }

        protected override void OnClosed(EventArgs e)
        {
            _closeTimer.Stop();
            _action = null;
            _onBodyActivate = null;
            if (ReferenceEquals(_current, this))
                _current = null;
            base.OnClosed(e);
        }

        /// <summary>Sticky tip: stays until the user clicks OK (no auto-close).</summary>
        public static void ShowSticky(string title, string text, string level)
        {
            ShowTip(title, text, level, "OK", () => { }, -1, null);
        }

        public static void ShowTip(string title, string text, string level, string actionLabel, Action action, int seconds)
        {
            ShowTip(title, text, level, actionLabel, action, seconds, null);
        }

        public static void ShowTip(
            string title,
            string text,
            string level,
            string actionLabel,
            Action action,
            int seconds,
            Action onBodyActivate)
        {
            if (_current != null)
            {
                try { _current.Close(); }
                catch { }
                _current = null;
            }
            var win = new ToastWindow(title, text, level, actionLabel, action, seconds, onBodyActivate);
            _current = win;
            win.Show();
        }
    }
}
