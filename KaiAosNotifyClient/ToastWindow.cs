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
        private Action _action;

        private ToastWindow(string title, string text, string level, string actionLabel, Action action, int durationSeconds)
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
            _sticky = durationSeconds < 0;
            var hasAction = !string.IsNullOrEmpty(actionLabel) && action != null;

            Color accent;
            string iconGlyph;
            var lv = (level ?? "info").ToLowerInvariant();
            if (lv == "warning")
            {
                accent = Color.FromRgb(200, 140, 20);
                iconGlyph = "!";
            }
            else if (lv == "error")
            {
                accent = Color.FromRgb(180, 50, 40);
                iconGlyph = "x";
            }
            else
            {
                accent = Color.FromRgb(30, 120, 200);
                iconGlyph = "i";
            }

            var panelBg = Color.FromRgb(250, 248, 244);
            var panelBorder = Color.FromRgb(170, 160, 145);
            var titleFg = Color.FromRgb(40, 40, 40);
            var bodyFg = Color.FromRgb(70, 70, 70);
            var btnBg = Color.FromRgb(235, 230, 220);
            var btnBorder = Color.FromRgb(160, 150, 135);

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
                Text = title ?? "KAI - AOS Renew",
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
                if (!_sticky)
                {
                    MouseEnter += delegate { if (_closeTimer != null && _closeTimer.IsEnabled) _closeTimer.Stop(); };
                    MouseLeave += delegate { if (_closeTimer != null && !_closeTimer.IsEnabled) _closeTimer.Start(); };
                }
            }

            Grid.SetColumn(textPanel, 2);
            root.Children.Add(accentBar);
            root.Children.Add(iconCircle);
            root.Children.Add(textPanel);
            border.Child = root;
            Content = border;

            if (!_sticky)
            {
                MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
                {
                    if (e.OriginalSource is Button) return;
                    Close();
                };
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
                _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
                _closeTimer.Tick += delegate
                {
                    _closeTimer.Stop();
                    Close();
                };
            }
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
            if (ReferenceEquals(_current, this))
                _current = null;
            base.OnClosed(e);
        }

        /// <summary>Sticky tip: stays until the user clicks OK (no auto-close).</summary>
        public static void ShowSticky(string title, string text, string level)
        {
            ShowTip(title, text, level, "OK", () => { }, -1);
        }

        public static void ShowTip(string title, string text, string level, string actionLabel, Action action, int seconds)
        {
            if (_current != null)
            {
                try { _current.Close(); }
                catch { }
                _current = null;
            }
            var win = new ToastWindow(title, text, level, actionLabel, action, seconds);
            _current = win;
            win.Show();
        }
    }
}
