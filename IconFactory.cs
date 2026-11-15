using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

namespace KaiAosRenew
{
    internal static class IconFactory
    {
        private const string KaiGuiIco =
            @"C:\Program Files (x86)\BMW\ISPI\KAI\KAIGui\KaiGui.ico";

        public static Icon GetAppIcon()
        {
            try
            {
                if (File.Exists(KaiGuiIco))
                    return new Icon(KaiGuiIco);
            }
            catch { }
            return SystemIcons.Application;
        }

        public static Icon GetErrorTrayIcon()
        {
            return GetBadgedTrayIcon(Color.FromArgb(220, 200, 30, 30), SystemIcons.Error);
        }

        /// <summary>App icon with an amber badge — update available (no balloon).</summary>
        public static Icon GetOutdatedTrayIcon(Icon baseIcon = null)
        {
            return GetBadgedTrayIcon(Color.FromArgb(230, 210, 140, 20), SystemIcons.Warning, baseIcon);
        }

        private static Icon GetBadgedTrayIcon(Color badge, Icon fallback, Icon baseIcon = null)
        {
            Icon owned = null;
            try
            {
                var src = baseIcon;
                if (src == null)
                {
                    owned = GetAppIcon();
                    src = owned;
                }
                using (var baseBmp = src.ToBitmap())
                {
                    var size = 16;
                    var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(baseBmp, 0, 0, size, size);
                        using (var brush = new SolidBrush(badge))
                        using (var border = new Pen(Color.White, 1f))
                        {
                            g.FillEllipse(brush, size - 8, size - 8, 7, 7);
                            g.DrawEllipse(border, size - 8, size - 8, 7, 7);
                        }
                    }
                    var hIcon = bmp.GetHicon();
                    var icon = Icon.FromHandle(hIcon);
                    var clone = (Icon)icon.Clone();
                    NativeIconHelper.DestroyIcon(hIcon);
                    icon.Dispose();
                    bmp.Dispose();
                    return clone;
                }
            }
            catch
            {
                return fallback;
            }
            finally
            {
                if (owned != null)
                {
                    try { owned.Dispose(); }
                    catch { }
                }
            }
        }

        private static class NativeIconHelper
        {
            [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
            public static extern bool DestroyIcon(IntPtr handle);
        }

        public static Bitmap SaveIcon(int size)
        {
            var bmp = NewCanvas(size);
            using (var g = Graphics.FromImage(bmp))
            {
                Setup(g);
                // Floppy body (Office-like blue)
                using (var body = new SolidBrush(Color.FromArgb(0, 120, 215)))
                using (var metal = new SolidBrush(Color.FromArgb(200, 200, 205)))
                using (var label = new SolidBrush(Color.FromArgb(245, 245, 245)))
                using (var pen = new Pen(Color.FromArgb(40, 40, 40)))
                {
                    g.FillRectangle(body, 2, 1, 12, 14);
                    g.DrawRectangle(pen, 2, 1, 12, 14);
                    // metal shutter
                    g.FillRectangle(metal, 4, 1, 8, 4);
                    g.DrawRectangle(pen, 4, 1, 8, 4);
                    // label area
                    g.FillRectangle(label, 4, 7, 8, 6);
                    g.DrawRectangle(pen, 4, 7, 8, 6);
                    g.DrawLine(pen, 5, 9, 11, 9);
                    g.DrawLine(pen, 5, 11, 10, 11);
                }
            }
            return bmp;
        }

        public static Bitmap HourglassIcon(int size)
        {
            var bmp = NewCanvas(size);
            using (var g = Graphics.FromImage(bmp))
            {
                Setup(g);
                DrawHourglass(g, false);
            }
            return bmp;
        }

        public static Bitmap HourglassStoppedIcon(int size)
        {
            var bmp = NewCanvas(size);
            using (var g = Graphics.FromImage(bmp))
            {
                Setup(g);
                DrawHourglass(g, true);
            }
            return bmp;
        }

        private static void DrawHourglass(Graphics g, bool crossed)
        {
            using (var frame = new Pen(Color.FromArgb(60, 60, 60), 1.5f))
            using (var sand = new SolidBrush(Color.FromArgb(218, 165, 32)))
            using (var glass = new SolidBrush(Color.FromArgb(180, 220, 240, 255)))
            using (var slash = new Pen(Color.FromArgb(200, 30, 30), 2f))
            {
                var top = new Point[] {
                    new Point(3, 2), new Point(13, 2), new Point(9, 8), new Point(7, 8)
                };
                var bottom = new Point[] {
                    new Point(7, 8), new Point(9, 8), new Point(13, 14), new Point(3, 14)
                };
                g.FillPolygon(glass, top);
                g.FillPolygon(glass, bottom);
                g.DrawPolygon(frame, top);
                g.DrawPolygon(frame, bottom);
                // sand
                g.FillPolygon(sand, new Point[] {
                    new Point(5, 3), new Point(11, 3), new Point(8, 7)
                });
                g.FillRectangle(sand, 6, 12, 4, 2);
                g.DrawLine(frame, 3, 2, 13, 2);
                g.DrawLine(frame, 3, 14, 13, 14);
                if (crossed)
                {
                    g.DrawLine(slash, 2, 2, 14, 14);
                    g.DrawLine(slash, 14, 2, 2, 14);
                }
            }
        }

        private static Bitmap NewCanvas(int size)
        {
            return new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        }

        private static void Setup(Graphics g)
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }
    }
}
