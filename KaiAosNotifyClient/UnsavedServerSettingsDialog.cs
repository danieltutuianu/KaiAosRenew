using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KaiAosNotifyClient
{
    internal enum UnsavedSettingsChoice
    {
        Cancel = 0,
        Save = 1,
        Discard = 2
    }

    internal sealed class ServerSettingsDiffRow
    {
        public string Key { get; set; }
        public string Label { get; set; }
        public string OldDisplay { get; set; }
        public string NewDisplay { get; set; }

        public string LineText
        {
            get
            {
                return string.Format("{0}: {1} → {2}",
                    Label ?? Key ?? "",
                    OldDisplay ?? "",
                    NewDisplay ?? "");
            }
        }
    }

    /// <summary>Leave Server Settings with unsaved edits: Save / Discard / Cancel + change list.</summary>
    internal sealed class UnsavedServerSettingsDialog : Window
    {
        public UnsavedSettingsChoice Choice { get; private set; }

        public UnsavedServerSettingsDialog(IList<ServerSettingsDiffRow> rows)
        {
            Choice = UnsavedSettingsChoice.Cancel;
            Title = ClientUiStrings.T("ServerSettingsLeaveTitle");
            Width = 520;
            MinHeight = 220;
            MaxHeight = 560;
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

            var saveBtn = MakeButton(ClientUiStrings.T("ServerSettingsLeaveSave"), true, "IconSave");
            saveBtn.Click += delegate
            {
                Choice = UnsavedSettingsChoice.Save;
                DialogResult = true;
                Close();
            };

            var discardBtn = MakeButton(ClientUiStrings.T("ServerSettingsLeaveDiscard"), false, "IconClear");
            discardBtn.Click += delegate
            {
                Choice = UnsavedSettingsChoice.Discard;
                DialogResult = true;
                Close();
            };

            var cancelBtn = MakeButton(ClientUiStrings.T("ServerSettingsLeaveCancel"), false, "IconDismiss");
            cancelBtn.IsCancel = true;
            cancelBtn.Click += delegate
            {
                Choice = UnsavedSettingsChoice.Cancel;
                DialogResult = false;
                Close();
            };

            buttons.Children.Add(saveBtn);
            buttons.Children.Add(discardBtn);
            buttons.Children.Add(cancelBtn);

            var body = new StackPanel();
            body.Children.Add(new TextBlock
            {
                Text = ClientUiStrings.T("ServerSettingsLeavePrompt"),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 10),
                Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33))
            });

            var list = new ListBox
            {
                MaxHeight = 320,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                Padding = new Thickness(4),
                FontSize = 12,
                FontFamily = new FontFamily("Consolas, Courier New, monospace")
            };

            if (rows == null || rows.Count == 0)
            {
                list.Items.Add(ClientUiStrings.T("ServerSettingsDiffEmpty"));
            }
            else
            {
                for (var i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];
                    if (row == null) continue;
                    list.Items.Add(row.LineText);
                }
            }

            var scroll = new ScrollViewer
            {
                Content = list,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 340
            };
            body.Children.Add(scroll);

            root.Children.Add(buttons);
            root.Children.Add(body);
            Content = root;
        }

        private static Button MakeButton(string content, bool isDefault, string iconResourceKey)
        {
            var btn = new Button
            {
                Content = content,
                MinWidth = 100,
                MinHeight = 28,
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(12, 4, 16, 4),
                IsDefault = isDefault,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                VerticalContentAlignment = VerticalAlignment.Center,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            try
            {
                if (Application.Current != null)
                {
                    var style = Application.Current.TryFindResource("BeigeButton") as Style;
                    if (style != null)
                        btn.Style = style;
                    if (!string.IsNullOrEmpty(iconResourceKey))
                    {
                        var geo = Application.Current.TryFindResource(iconResourceKey);
                        if (geo != null)
                            btn.Tag = geo;
                    }
                }
            }
            catch { }
            return btn;
        }

        public static UnsavedSettingsChoice Show(Window owner, IList<ServerSettingsDiffRow> rows)
        {
            var dlg = new UnsavedServerSettingsDialog(rows);
            if (owner != null)
                dlg.Owner = owner;
            dlg.ShowDialog();
            return dlg.Choice;
        }
    }
}
