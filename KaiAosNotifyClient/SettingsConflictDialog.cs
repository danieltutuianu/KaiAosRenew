using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KaiAosNotifyClient
{
    internal enum SettingsConflictChoice
    {
        Cancel = 0,
        Reload = 1,
        Overwrite = 2
    }

    /// <summary>Conflict on Save: Reload server / Overwrite server / Cancel.</summary>
    internal sealed class SettingsConflictDialog : Window
    {
        public SettingsConflictChoice Choice { get; private set; }

        public SettingsConflictDialog()
        {
            Choice = SettingsConflictChoice.Cancel;
            Title = ClientUiStrings.T("AppTitle");
            Width = 460;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = Brushes.White;
            ShowInTaskbar = false;

            var root = new DockPanel { Margin = new Thickness(16) };

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };
            DockPanel.SetDock(buttons, Dock.Bottom);

            var reloadBtn = MakeButton(ClientUiStrings.T("ConflictReload"), false);
            reloadBtn.Click += delegate
            {
                Choice = SettingsConflictChoice.Reload;
                DialogResult = true;
                Close();
            };

            var overwriteBtn = MakeButton(ClientUiStrings.T("ConflictOverwrite"), false);
            overwriteBtn.Click += delegate
            {
                Choice = SettingsConflictChoice.Overwrite;
                DialogResult = true;
                Close();
            };

            var cancelBtn = MakeButton(ClientUiStrings.T("ConflictCancel"), true);
            cancelBtn.IsCancel = true;
            cancelBtn.Click += delegate
            {
                Choice = SettingsConflictChoice.Cancel;
                DialogResult = false;
                Close();
            };

            buttons.Children.Add(reloadBtn);
            buttons.Children.Add(overwriteBtn);
            buttons.Children.Add(cancelBtn);

            var msg = new TextBlock
            {
                Text = ClientUiStrings.T("ServerSettingsConflictPrompt"),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33))
            };

            root.Children.Add(buttons);
            root.Children.Add(msg);
            Content = root;
        }

        private static Button MakeButton(string content, bool isDefault)
        {
            return new Button
            {
                Content = content,
                MinWidth = 100,
                MinHeight = 28,
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(12, 4, 12, 4),
                IsDefault = isDefault
            };
        }

        public static SettingsConflictChoice Show(Window owner)
        {
            var dlg = new SettingsConflictDialog();
            if (owner != null)
                dlg.Owner = owner;
            dlg.ShowDialog();
            return dlg.Choice;
        }
    }
}
