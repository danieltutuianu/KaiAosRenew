using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using KaiAosRenew;

namespace KaiAosNotifyClient
{
    /// <summary>Result dialog for Check browser password: OK + open password manager (+ optional Fill & Save).</summary>
    internal sealed class BrowserPassResultWindow : Window
    {
        private static readonly Brush AccentBrush = CreateBrush(0xC6, 0x28, 0x28);
        private static readonly Brush OkBrush = CreateBrush(0x2E, 0x7D, 0x32);

        public bool FillAndSaveRequested { get; private set; }
        public bool OpenedPasswordManager { get; private set; }

        public struct ShowResult
        {
            public bool FillAndSaveRequested;
            public bool OpenedPasswordManager;
        }

        private static Brush CreateBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        public BrowserPassResultWindow(
            string browserName,
            string header,
            string coloredSite,
            string statusText,
            bool statusIsOk,
            string footer,
            bool offerFillSave)
        {
            Title = "KAI - AOS Renew - Client";
            Width = 520;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = Brushes.White;

            var root = new DockPanel { Margin = new Thickness(16) };

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };
            DockPanel.SetDock(buttons, Dock.Bottom);

            string openLabel;
            try { openLabel = string.Format(ClientUiStrings.T("OpenBrowserPasswords"), browserName ?? ""); }
            catch { openLabel = "Open " + browserName + " passwords"; }

            var openBtn = new Button
            {
                Content = openLabel,
                MinWidth = 150,
                MinHeight = 28,
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(10, 4, 10, 4),
                IsDefault = !offerFillSave
            };
            openBtn.Click += delegate
            {
                string err;
                if (!BrowserPasswordReader.TryOpenPasswordManager(browserName, out err))
                {
                    MessageBox.Show(this,
                        string.Format(ClientUiStrings.T("OpenBrowserPasswordsFail"), err ?? "unknown"),
                        "KAI - AOS Renew - Client", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                OpenedPasswordManager = true;
                DialogResult = true;
                Close();
            };

            if (offerFillSave)
            {
                var fillBtn = new Button
                {
                    Content = ClientUiStrings.T("BrowserPassFillSave"),
                    MinWidth = 120,
                    MinHeight = 28,
                    Margin = new Thickness(0, 0, 8, 0),
                    Padding = new Thickness(10, 4, 10, 4)
                };
                fillBtn.Click += delegate
                {
                    FillAndSaveRequested = true;
                    DialogResult = true;
                    Close();
                };
                buttons.Children.Add(fillBtn);
            }

            var okBtn = new Button
            {
                Content = ClientUiStrings.T("Ok"),
                MinWidth = 80,
                MinHeight = 28,
                Padding = new Thickness(10, 4, 10, 4),
                IsDefault = offerFillSave,
                IsCancel = true
            };
            okBtn.Click += delegate
            {
                DialogResult = true;
                Close();
            };

            buttons.Children.Add(openBtn);
            buttons.Children.Add(okBtn);
            root.Children.Add(buttons);

            var text = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = Brushes.Black
            };
            AppendPlain(text, header);
            if (!string.IsNullOrEmpty(coloredSite))
            {
                text.Inlines.Add(new LineBreak());
                text.Inlines.Add(new LineBreak());
                AppendColored(text, coloredSite, AccentBrush);
            }
            if (!string.IsNullOrEmpty(statusText))
            {
                text.Inlines.Add(new LineBreak());
                text.Inlines.Add(new LineBreak());
                AppendColored(text, statusText, statusIsOk ? OkBrush : AccentBrush);
            }
            if (!string.IsNullOrEmpty(footer))
            {
                text.Inlines.Add(new LineBreak());
                text.Inlines.Add(new LineBreak());
                AppendPlain(text, footer);
            }
            root.Children.Add(text);

            Content = root;
        }

        private static void AppendPlain(TextBlock tb, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            AppendWithBreaks(tb, text, Brushes.Black, FontWeights.Normal);
        }

        private static void AppendColored(TextBlock tb, string text, Brush brush)
        {
            if (string.IsNullOrEmpty(text)) return;
            AppendWithBreaks(tb, text, brush, FontWeights.SemiBold);
        }

        private static void AppendWithBreaks(TextBlock tb, string text, Brush brush, FontWeight weight)
        {
            var parts = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                    tb.Inlines.Add(new LineBreak());
                tb.Inlines.Add(new Run(parts[i]) { Foreground = brush, FontWeight = weight });
            }
        }

        public static ShowResult ShowFor(
            Window owner,
            string browserName,
            string header,
            string coloredSite,
            string statusText,
            bool statusIsOk,
            string footer,
            bool offerFillSave)
        {
            var w = new BrowserPassResultWindow(browserName, header, coloredSite, statusText, statusIsOk, footer, offerFillSave);
            if (owner != null)
                w.Owner = owner;
            w.ShowDialog();
            return new ShowResult
            {
                FillAndSaveRequested = w.FillAndSaveRequested,
                OpenedPasswordManager = w.OpenedPasswordManager
            };
        }
    }
}
