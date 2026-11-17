using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace KaiAosRenew
{
    /// <summary>Pick day + clipboard/file destination for Activity/Register log export.</summary>
    internal sealed class SaveLogDialog : Window
    {
        private readonly DatePicker _dayPicker;
        private readonly RadioButton _clipRadio;
        private readonly RadioButton _fileRadio;

        public DateTime SelectedDay { get; private set; }
        public bool ToClipboard { get; private set; }
        public bool Confirmed { get; private set; }

        public SaveLogDialog(string title, DateTime defaultDay)
        {
            Title = title ?? "Save log";
            Width = 360;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;

            var root = new StackPanel { Margin = new Thickness(16) };
            root.Children.Add(new TextBlock
            {
                Text = UiStrings.T("SaveLogDay"),
                Margin = new Thickness(0, 0, 0, 6)
            });
            _dayPicker = new DatePicker
            {
                SelectedDate = defaultDay.Date,
                Margin = new Thickness(0, 0, 0, 12)
            };
            root.Children.Add(_dayPicker);

            _clipRadio = new RadioButton
            {
                Content = UiStrings.T("SaveLogClipboard"),
                IsChecked = true,
                Margin = new Thickness(0, 0, 0, 6)
            };
            _fileRadio = new RadioButton
            {
                Content = UiStrings.T("SaveLogFile"),
                Margin = new Thickness(0, 0, 0, 14)
            };
            root.Children.Add(_clipRadio);
            root.Children.Add(_fileRadio);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var ok = new Button
            {
                Content = UiStrings.T("SaveLogOk"),
                MinWidth = 80,
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true
            };
            ok.Click += Ok_Click;
            var cancel = new Button
            {
                Content = UiStrings.T("SaveLogCancel"),
                MinWidth = 80,
                IsCancel = true
            };
            cancel.Click += delegate { Confirmed = false; Close(); };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            Content = root;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            SelectedDay = (_dayPicker.SelectedDate ?? DateTime.Today).Date;
            ToClipboard = _clipRadio.IsChecked == true;
            Confirmed = true;
            DialogResult = true;
            Close();
        }

        public static string FormatDayFileName(string prefix, DateTime day)
        {
            return string.Format("{0}-{1:dd.MM.yyyy}.log", prefix, day);
        }

        public static string PromptSavePath(Window owner, string fileName)
        {
            var dlg = new SaveFileDialog
            {
                FileName = fileName,
                Filter = "Log files (*.log)|*.log|All files (*.*)|*.*",
                DefaultExt = ".log",
                AddExtension = true,
                OverwritePrompt = true
            };
            var ok = owner != null ? dlg.ShowDialog(owner) : dlg.ShowDialog();
            return ok == true ? dlg.FileName : null;
        }
    }
}
