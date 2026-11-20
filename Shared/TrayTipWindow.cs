using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace KaiAosRenew
{
    public enum TrayTipLevel
    {
        Info,
        Warning,
        Error
    }

    /// <summary>
    /// In-app tray tip (Server + Client): visible even when Windows toasts are disabled.
    /// Optional action button; double-click body can navigate.
    /// </summary>
    internal sealed class TrayTipWindow : Window
    {
        private const int MaxPendingTips = 8;
        private static readonly Regex VinRegex = new Regex(
            @"\b([A-HJ-NPR-Z0-9]{17})\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Queue<PendingTip> PendingTips = new Queue<PendingTip>();
        private static TrayTipWindow _current;
        private static bool _suppressQueueDrain;

        /// <summary>Localized Copy VIN button label (set from ApplyLanguage).</summary>
        public static string CopyVinLabel = "Copy VIN";

        private readonly DispatcherTimer _closeTimer;
        private readonly bool _sticky;
        private readonly bool _hasAction;
        private readonly bool _hasBodyActivate;
        private readonly string _associatedVin;
        private Action _action;
        private Action _onBodyActivate;
        private int _remainingSeconds;
        private TextBlock _countdownText;
        private TextBlock _bodyText;

        private sealed class PendingTip
        {
            public string Title;
            public string Text;
            public TrayTipLevel Level;
            public string ActionLabel;
            public Action Action;
            public int DurationSeconds;
            public Action OnBodyActivate;
            public string AssociatedVin;
        }

        /// <summary>Sticky tip with a Copy action (VIN tip) — protected from non-VIN overlays.</summary>
        private bool IsStickyVinActionTip
        {
            get { return _sticky && _hasAction; }
        }

        /// <summary>VIN of the current sticky Copy tip, or null.</summary>
        public static string CurrentStickyVin
        {
            get
            {
                if (_current == null || !_current.IsStickyVinActionTip)
                    return null;
                return string.IsNullOrEmpty(_current._associatedVin) ? null : _current._associatedVin;
            }
        }

        private TrayTipWindow(
            string title,
            string text,
            TrayTipLevel level,
            string actionLabel,
            Action action,
            int durationSeconds,
            Action onBodyActivate,
            string associatedVin)
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
            _associatedVin = associatedVin ?? "";
            _sticky = durationSeconds < 0;
            _hasAction = !string.IsNullOrEmpty(actionLabel) && action != null;
            _hasBodyActivate = onBodyActivate != null;

            Color accent;
            switch (level)
            {
                case TrayTipLevel.Warning:
                    accent = Color.FromRgb(200, 140, 20);
                    break;
                case TrayTipLevel.Error:
                    accent = Color.FromRgb(180, 50, 40);
                    break;
                default:
                    accent = Color.FromRgb(30, 120, 200);
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
            if (_sticky)
            {
                var closeBtn = new Button
                {
                    Content = "\u2715",
                    Width = 26,
                    Height = 26,
                    Padding = new Thickness(0),
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Top,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Cursor = Cursors.Hand,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Foreground = new SolidColorBrush(bodyFg),
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    ToolTip = "Close"
                };
                closeBtn.Click += StickyCloseButton_Click;
                Grid.SetColumn(closeBtn, 1);
                titleRow.Children.Add(closeBtn);
            }
            else
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
            _bodyText = new TextBlock
            {
                Text = text ?? "",
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 0),
                Foreground = new SolidColorBrush(bodyFg),
                TextWrapping = TextWrapping.Wrap
            };
            textPanel.Children.Add(_bodyText);

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

            Grid.SetColumn(textPanel, 1);

            root.Children.Add(accentBar);
            root.Children.Add(textPanel);
            border.Child = root;
            Content = border;

            MouseLeftButtonUp += Balloon_MouseLeftButtonUp;
            MouseDoubleClick += Balloon_MouseDoubleClick;
            if (!_sticky)
            {
                MouseEnter += delegate { PauseCloseTimer(); };
                MouseLeave += delegate { ResumeCloseTimer(); };
            }

            if (_sticky)
            {
                _closeTimer = new DispatcherTimer();
            }
            else
            {
                var seconds = durationSeconds;
                if (seconds <= 0)
                    seconds = _hasAction ? 8 : 4;
                if (seconds < 3) seconds = 3;
                if (seconds > 60) seconds = 60;
                _remainingSeconds = seconds;
                UpdateCountdownText();
                _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _closeTimer.Tick += CloseTimer_Tick;
            }
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

        private void StickyCloseButton_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            Close();
        }

        private void Balloon_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_sticky) return;
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
            if (_sticky) return;
            if (e.OriginalSource is Button || IsDescendantOfButton(e.OriginalSource as DependencyObject))
                return;
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
            if (_sticky) return;
            if (_closeTimer != null && !_closeTimer.IsEnabled)
                _closeTimer.Start();
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            PositionNearTray();
            if (!_sticky && !_closeTimer.IsEnabled)
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
            if (!_suppressQueueDrain)
            {
                try
                {
                    Dispatcher.BeginInvoke(new Action(ShowNextPending), DispatcherPriority.Normal);
                }
                catch { }
            }
        }

        private void PositionNearTray()
        {
            var work = SystemParameters.WorkArea;
            Left = work.Right - Width - 12;
            Top = work.Bottom - ActualHeight - 12;
            if (Top < work.Top)
                Top = work.Top + 12;
        }

        public static TrayTipLevel ParseLevel(string level)
        {
            var lv = (level ?? "info").Trim().ToLowerInvariant();
            if (lv == "warning" || lv == "warn") return TrayTipLevel.Warning;
            if (lv == "error" || lv == "err") return TrayTipLevel.Error;
            return TrayTipLevel.Info;
        }

        /// <summary>Close any visible tip (e.g. when a later BMW upload succeeds).</summary>
        public static void CloseCurrent()
        {
            if (_current == null) return;
            try { _current.Close(); }
            catch { }
            _current = null;
        }

        /// <summary>Close the sticky VIN tip only if it matches <paramref name="vin"/>.</summary>
        public static bool CloseIfVin(string vin)
        {
            if (string.IsNullOrEmpty(vin) || _current == null || !_current.IsStickyVinActionTip)
                return false;
            if (!string.Equals(_current._associatedVin, vin, StringComparison.OrdinalIgnoreCase))
                return false;
            CloseCurrent();
            return true;
        }

        /// <summary>
        /// If the current tip is a sticky Copy tip for the same VIN, update its body text in place.
        /// </summary>
        public static bool TryUpdateStickyVinTip(string vin, string text)
        {
            if (string.IsNullOrEmpty(vin) || _current == null || !_current.IsStickyVinActionTip)
                return false;
            if (!string.Equals(_current._associatedVin, vin, StringComparison.OrdinalIgnoreCase))
                return false;
            if (_current._bodyText != null)
                _current._bodyText.Text = text ?? "";
            return true;
        }

        public static void ShowTip(string title, string text, TrayTipLevel level)
        {
            ShowTip(title, text, level, null, null, 0, null, null);
        }

        public static void ShowTip(
            string title,
            string text,
            TrayTipLevel level,
            string actionLabel,
            Action action)
        {
            ShowTip(title, text, level, actionLabel, action, 0, null, null);
        }

        /// <summary>String-level overload (client notify messages: info/warning/error).</summary>
        public static void ShowTip(
            string title,
            string text,
            string level,
            string actionLabel,
            Action action,
            int durationSeconds)
        {
            ShowTip(title, text, ParseLevel(level), actionLabel, action, durationSeconds, null, null);
        }

        public static void ShowTip(
            string title,
            string text,
            string level,
            string actionLabel,
            Action action,
            int durationSeconds,
            Action onBodyActivate)
        {
            ShowTip(title, text, ParseLevel(level), actionLabel, action, durationSeconds, onBodyActivate, null);
        }

        public static void ShowTip(
            string title,
            string text,
            string level,
            string actionLabel,
            Action action,
            int durationSeconds,
            Action onBodyActivate,
            string associatedVin)
        {
            ShowTip(title, text, ParseLevel(level), actionLabel, action, durationSeconds, onBodyActivate, associatedVin);
        }

        /// <summary>Sticky tip: stays until the user clicks Close (no auto-close).</summary>
        public static void ShowSticky(string title, string text, TrayTipLevel level)
        {
            ShowTip(title, text, level, null, null, -1, null, null);
        }

        public static void ShowSticky(string title, string text, string level)
        {
            ShowSticky(title, text, ParseLevel(level));
        }

        public static void ShowTip(
            string title,
            string text,
            TrayTipLevel level,
            string actionLabel,
            Action action,
            int durationSeconds,
            Action onBodyActivate)
        {
            ShowTip(title, text, level, actionLabel, action, durationSeconds, onBodyActivate, null);
        }

        public static void ShowTip(
            string title,
            string text,
            TrayTipLevel level,
            string actionLabel,
            Action action,
            int durationSeconds,
            Action onBodyActivate,
            string associatedVin)
        {
            // While a sticky VIN tip is up, non-explicit tips are queued (before auto-inject Copy).
            var hasExplicitVin = !string.IsNullOrEmpty(associatedVin);
            if (_current != null && _current.IsStickyVinActionTip && !hasExplicitVin)
            {
                EnqueuePending(title, text, level, actionLabel, action, durationSeconds, onBodyActivate, associatedVin);
                return;
            }

            EnsureCopyVinAction(ref actionLabel, ref action, ref associatedVin, text);

            if (_current != null)
            {
                _suppressQueueDrain = true;
                try
                {
                    try { _current.Close(); }
                    catch { }
                }
                finally
                {
                    _suppressQueueDrain = false;
                }
                _current = null;
            }

            PresentTip(title, text, level, actionLabel, action, durationSeconds, onBodyActivate, associatedVin);
        }

        private static void PresentTip(
            string title,
            string text,
            TrayTipLevel level,
            string actionLabel,
            Action action,
            int durationSeconds,
            Action onBodyActivate,
            string associatedVin)
        {
            var win = new TrayTipWindow(
                title, text, level, actionLabel, action, durationSeconds, onBodyActivate, associatedVin);
            _current = win;
            win.Show();
        }

        private static void EnqueuePending(
            string title,
            string text,
            TrayTipLevel level,
            string actionLabel,
            Action action,
            int durationSeconds,
            Action onBodyActivate,
            string associatedVin)
        {
            if (PendingTips.Count > 0)
            {
                var last = default(PendingTip);
                foreach (var tip in PendingTips)
                    last = tip;
                if (last != null
                    && last.Level == level
                    && string.Equals(last.Text ?? "", text ?? "", StringComparison.Ordinal))
                    return;
            }

            while (PendingTips.Count >= MaxPendingTips)
                PendingTips.Dequeue();

            PendingTips.Enqueue(new PendingTip
            {
                Title = title,
                Text = text,
                Level = level,
                ActionLabel = actionLabel,
                Action = action,
                DurationSeconds = durationSeconds,
                OnBodyActivate = onBodyActivate,
                AssociatedVin = associatedVin
            });
        }

        private static void ShowNextPending()
        {
            if (_current != null || PendingTips.Count == 0)
                return;

            var next = PendingTips.Dequeue();
            var actionLabel = next.ActionLabel;
            var action = next.Action;
            var associatedVin = next.AssociatedVin;
            EnsureCopyVinAction(ref actionLabel, ref action, ref associatedVin, next.Text);
            PresentTip(
                next.Title,
                next.Text,
                next.Level,
                actionLabel,
                action,
                next.DurationSeconds,
                next.OnBodyActivate,
                associatedVin);
        }

        /// <summary>
        /// If the tip has no action but a VIN is known (associated or in text), add Copy VIN.
        /// </summary>
        private static void EnsureCopyVinAction(
            ref string actionLabel,
            ref Action action,
            ref string associatedVin,
            string text)
        {
            if (!string.IsNullOrEmpty(actionLabel) && action != null)
                return;

            var vin = associatedVin;
            if (string.IsNullOrEmpty(vin))
                vin = TryExtractVin(text);
            if (string.IsNullOrEmpty(vin))
                return;

            associatedVin = vin;
            var copyVin = vin;
            actionLabel = string.IsNullOrEmpty(CopyVinLabel) ? "Copy VIN" : CopyVinLabel;
            action = delegate { TryCopyText(copyVin); };
        }

        private static string TryExtractVin(string text)
        {
            if (string.IsNullOrEmpty(text))
                return null;
            var m = VinRegex.Match(text);
            if (!m.Success || m.Groups.Count < 2)
                return null;
            return m.Groups[1].Value.ToUpperInvariant();
        }

        private static void TryCopyText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                Clipboard.SetDataObject(text, true);
                return;
            }
            catch { }
            try
            {
                System.Windows.Forms.Clipboard.SetText(text);
            }
            catch { }
        }
    }
}
