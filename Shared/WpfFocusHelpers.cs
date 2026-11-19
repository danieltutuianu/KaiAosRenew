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
    }
}
