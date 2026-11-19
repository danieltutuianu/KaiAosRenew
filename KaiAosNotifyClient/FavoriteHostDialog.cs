using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace KaiAosNotifyClient
{
    internal enum FavoriteHostChoice
    {
        Cancel = 0,
        Save = 1,
        Remove = 2
    }

    /// <summary>Save / rename / remove a favorite host (alias is optional).</summary>
    internal sealed class FavoriteHostDialog : Window
    {
        private readonly TextBox _aliasBox;

        public FavoriteHostChoice Choice { get; private set; }
        public string Alias { get; private set; }

        public FavoriteHostDialog(string host, string alias, bool alreadyFavorite)
        {
            Choice = FavoriteHostChoice.Cancel;
            Alias = alias ?? "";
            Title = ClientUiStrings.T("FavHostTitle");
            Width = 420;
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

            var saveBtn = MakeButton(ClientUiStrings.T("FavHostSave"), true);
            saveBtn.Click += delegate
            {
                Choice = FavoriteHostChoice.Save;
                Alias = _aliasBox != null ? (_aliasBox.Text ?? "").Trim() : "";
                DialogResult = true;
                Close();
            };
            buttons.Children.Add(saveBtn);

            if (alreadyFavorite)
            {
                var removeBtn = MakeButton(ClientUiStrings.T("FavHostRemove"), false);
                removeBtn.Click += delegate
                {
                    Choice = FavoriteHostChoice.Remove;
                    DialogResult = true;
                    Close();
                };
                buttons.Children.Add(removeBtn);
            }

            var cancelBtn = MakeButton(ClientUiStrings.T("ConflictCancel"), false);
            cancelBtn.IsCancel = true;
            cancelBtn.Click += delegate
            {
                Choice = FavoriteHostChoice.Cancel;
                DialogResult = false;
                Close();
            };
            buttons.Children.Add(cancelBtn);

            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = string.Format(ClientUiStrings.T("FavHostAliasPrompt"), host),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 8),
                Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33))
            });
            _aliasBox = new TextBox
            {
                Text = alias ?? "",
                MaxLength = ClientSettings.MaxFavoriteAliasLength,
                MinHeight = 26,
                Padding = new Thickness(4, 3, 4, 3)
            };
            panel.Children.Add(_aliasBox);

            root.Children.Add(buttons);
            root.Children.Add(panel);
            Content = root;

            Loaded += delegate
            {
                _aliasBox.Focus();
                _aliasBox.SelectAll();
            };
        }

        private static Button MakeButton(string content, bool isDefault)
        {
            return new Button
            {
                Content = content,
                MinWidth = 90,
                MinHeight = 28,
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(12, 4, 12, 4),
                IsDefault = isDefault
            };
        }

        public static FavoriteHostChoice Show(Window owner, string host, string alias, bool alreadyFavorite, out string newAlias)
        {
            var dlg = new FavoriteHostDialog(host, alias, alreadyFavorite);
            if (owner != null)
                dlg.Owner = owner;
            dlg.ShowDialog();
            newAlias = dlg.Alias;
            return dlg.Choice;
        }
    }
}
