using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace Dusk.Ui
{
    // The Dusk mark: a setting sun cut by horizon stripes. Grey when Dusk is off.
    public static class IconArt
    {
        public static Bitmap Render(int size, bool off)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float s = size;
                var circle = new RectangleF(s * 0.04f, s * 0.04f, s * 0.92f, s * 0.92f);
                Color[] colors = off
                    ? new[] { Palette.Hex("#C9C6BF"), Palette.Hex("#A9A6A0"), Palette.Hex("#8A877F") }
                    : new[] { Palette.Hex("#F8B672"), Palette.Hex("#F07A3A"), Palette.Hex("#E0482A") };
                using (LinearGradientBrush brush = Canvas.Gradient(new RectangleF(0, 0, s, s), colors, new[] { 0f, 0.55f, 1f }, true))
                    g.FillEllipse(brush, circle);

                // Cut the horizon stripes out, pixel-aligned so they stay crisp at small sizes.
                g.SmoothingMode = SmoothingMode.None;
                g.CompositingMode = CompositingMode.SourceCopy;
                using (var clear = new SolidBrush(Color.Transparent))
                {
                    if (size >= 24)
                    {
                        g.FillRectangle(clear, 0, (float)Math.Round(s * 0.60), s, Math.Max(1, (float)Math.Round(s * 0.055)));
                        g.FillRectangle(clear, 0, (float)Math.Round(s * 0.73), s, Math.Max(1, (float)Math.Round(s * 0.065)));
                    }
                    else
                    {
                        g.FillRectangle(clear, 0, (float)Math.Round(s * 0.64), s, Math.Max(1, (float)Math.Round(s * 0.09)));
                    }
                }
            }
            return bmp;
        }

        public static IntPtr Hicon(int size, bool off)
        {
            using (Bitmap b = Render(size, off)) return b.GetHicon();
        }

        public static byte[] Png(int size)
        {
            using (Bitmap b = Render(size, false))
            using (var ms = new MemoryStream())
            {
                b.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }
    }
}
