using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace KaiAosRenew
{
    /// <summary>
    /// In-app tray balloon: always visible even when Windows toast notifications are disabled.
    /// Optional action button (e.g. Copy VIN). Double-click body can navigate (e.g. Keys tab).
    /// </summary>
    internal sealed class TrayBalloonWindow : Window
    {
        private static TrayBalloonWindow _current;
        private readonly DispatcherTimer _closeTimer;
        private readonly bool _hasAction;
        private readonly bool _hasBodyActivate;
        private Action _action;
        private Action _onBodyActivate;

        private TrayBalloonWindow(
            string title,
            string text,
            WinForms.ToolTipIcon icon,
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
            _hasAction = !string.IsNullOrEmpty(actionLabel) && action != null;
            _hasBodyActivate = onBodyActivate != null;

            Color accent;
            string iconGlyph;
            switch (icon)
            {
                case WinForms.ToolTipIcon.Warning:
                    accent = Color.FromRgb(200, 140, 20);
                    iconGlyph = "!";
                    break;
                case WinForms.ToolTipIcon.Error:
                    accent = Color.FromRgb(180, 50, 40);
                    iconGlyph = "x";
                    break;
                default:
                    accent = Color.FromRgb(30, 120, 200);
                    iconGlyph = "i";
                    break;
            }

            var night = ThemeManager.IsNight;
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
                Padding = new Thickness(0),
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
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var accentBar = new Border
            {
                Background = new SolidColorBrush(accent),
                CornerRadius = new CornerRadius(4, 0, 0, 4)
            };
            Grid.SetColumn(accentBar, 0);

            var iconCircle = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(accent),
                Margin = new Thickness(12, 14, 8, 14),
                VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock
                {
                    Text = iconGlyph,
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Grid.SetColumn(iconCircle, 1);

            var textPanel = new StackPanel { Margin = new Thickness(4, 12, 14, 12) };
            textPanel.Children.Add(new TextBlock
            {
                Text = title ?? "KAI AOS Renew",
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Foreground = new SolidColorBrush(titleFg),
                TextWrapping = TextWrapping.Wrap
            });
            textPanel.Children.Add(new TextBlock
            {
                Text = text ?? "",
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 0),
                Foreground = new SolidColorBrush(bodyFg),
                TextWrapping = TextWrapping.Wrap
            });

            if (_hasAction)
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
                btn.Click += ActionButton_Click;
                textPanel.Children.Add(btn);
            }

            Grid.SetColumn(textPanel, 2);

            root.Children.Add(accentBar);
            root.Children.Add(iconCircle);
            root.Children.Add(textPanel);
            border.Child = root;
            Content = border;

            MouseLeftButtonUp += Balloon_MouseLeftButtonUp;
            MouseDoubleClick += Balloon_MouseDoubleClick;
            if (_hasAction || _hasBodyActivate)
            {
                MouseEnter += delegate { PauseCloseTimer(); };
                MouseLeave += delegate { ResumeCloseTimer(); };
            }

            var seconds = durationSeconds;
            if (seconds <= 0)
                seconds = _hasAction ? 8 : 4;
            if (seconds < 3) seconds = 3;
            if (seconds > 60) seconds = 60;
            _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            _closeTimer.Tick += delegate
            {
                _closeTimer.Stop();
                Close();
            };
        }

        private void ActionButton_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            try
            {
                if (_action != null)
                    _action();
            }
            catch { }
            Close();
        }

        private void Balloon_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is Button || IsDescendantOfButton(e.OriginalSource as DependencyObject))
                return;
            e.Handled = true;
            try
            {
                if (_onBodyActivate != null)
                    _onBodyActivate();
            }
            catch { }
            Close();
        }

        private void Balloon_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is Button || IsDescendantOfButton(e.OriginalSource as DependencyObject))
                return;
            // When double-click can navigate, ignore single click so the second click can land.
            if (_hasBodyActivate)
                return;
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

        private void PauseCloseTimer()
        {
            if (_closeTimer != null && _closeTimer.IsEnabled)
                _closeTimer.Stop();
        }

        private void ResumeCloseTimer()
        {
            if (_closeTimer != null && !_closeTimer.IsEnabled)
                _closeTimer.Start();
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            PositionNearTray();
            if (!_closeTimer.IsEnabled)
                _closeTimer.Start();
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

        private void PositionNearTray()
        {
            var work = SystemParameters.WorkArea;
            Left = work.Right - Width - 12;
            Top = work.Bottom - ActualHeight - 12;
            if (Top < work.Top)
                Top = work.Top + 12;
        }

        public static void ShowTip(string title, string text, WinForms.ToolTipIcon icon)
        {
            ShowTip(title, text, icon, null, null, 0, null);
        }

        public static void ShowTip(
            string title,
            string text,
            WinForms.ToolTipIcon icon,
            string actionLabel,
            Action action)
        {
            ShowTip(title, text, icon, actionLabel, action, 0, null);
        }

        public static void ShowTip(
            string title,
            string text,
            WinForms.ToolTipIcon icon,
            string actionLabel,
            Action action,
            int durationSeconds,
            Action onBodyActivate)
        {
            if (_current != null)
            {
                try { _current.Close(); }
                catch { }
                _current = null;
            }
            var win = new TrayBalloonWindow(
                title, text, icon, actionLabel, action, durationSeconds, onBodyActivate);
            _current = win;
            win.Show();
        }
    }
}
