using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace KaiAosRenew
{
    /// <summary>Shared WPF focus helpers (Server + Client shortcuts).</summary>
    internal static class WpfFocusHelpers
    {
        public static bool IsComboDropDownOpenInFocus()
        {
            var d = Keyboard.FocusedElement as DependencyObject;
            while (d != null)
            {
                var cb = d as ComboBox;
                if (cb != null && cb.IsDropDownOpen) return true;
                var parent = (d is Visual || d is System.Windows.Media.Media3D.Visual3D)
                    ? VisualTreeHelper.GetParent(d)
                    : LogicalTreeHelper.GetParent(d);
                d = parent;
            }
            return false;
        }

        /// <summary>Centers a window on the primary work area (DIP). Skips minimized windows.</summary>
        public static void CenterOnScreen(Window window)
        {
            if (window == null) return;
            if (window.WindowState == WindowState.Minimized) return;

            var w = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
            var h = window.ActualHeight > 0 ? window.ActualHeight : window.Height;
            if (double.IsNaN(w) || double.IsNaN(h) || w <= 0 || h <= 0) return;

            var work = SystemParameters.WorkArea;
            window.Left = work.Left + (work.Width - w) / 2;
            window.Top = work.Top + (work.Height - h) / 2;
        }
    }
}
