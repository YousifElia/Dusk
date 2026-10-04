using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Dusk.Ui
{
    public enum Align { Left, Center, Right }

    // A clickable or draggable area recorded while painting. Coordinates are in design pixels.
    public sealed class Hit
    {
        public string Id;
        public RectangleF Rect;
        public int Cursor = Native.IDC_HAND;
        public bool Caption;
        public Action Click;
        public Action<PointF> Drag;
        public Action DragEnd;
        public Action<int> Wheel;
    }

    // Drawing helpers over GDI+, in the design's CSS pixel units (the window scales them for DPI).
    public sealed class Canvas
    {
        private static readonly StringFormat Format = CreateFormat();
        private static readonly Blend SoftFalloff = CreateFalloff();

        // Position 0 is the glow's outer edge and 1 its centre; a smoothstep curve keeps both ends gentle.
        private static Blend CreateFalloff()
        {
            const int steps = 12;
            var blend = new Blend(steps + 1);
            for (int i = 0; i <= steps; i++)
            {
                float p = (float)i / steps;
                blend.Positions[i] = p;
                blend.Factors[i] = p * p * (3 - 2 * p);
            }
            return blend;
        }

        public readonly Graphics G;
        public readonly Palette P;
        private readonly List<Hit> hits;
        private readonly string hover, pressed;

        public Canvas(Graphics g, Palette palette, List<Hit> hits, string hover, string pressed)
        {
            G = g;
            P = palette;
            this.hits = hits;
            this.hover = hover;
            this.pressed = pressed;
        }

        private static StringFormat CreateFormat()
        {
            var f = (StringFormat)StringFormat.GenericTypographic.Clone();
            f.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap | StringFormatFlags.NoClip;
            return f;
        }

        // ---- Interaction ----

        public bool IsHover(string id) { return id != null && id == hover; }
        public bool IsPressed(string id) { return id != null && id == pressed; }

        public Hit Area(string id, RectangleF r)
        {
            var h = new Hit { Id = id, Rect = r };
            hits.Add(h);
            return h;
        }

        public Hit Click(string id, RectangleF r, Action action)
        {
            Hit h = Area(id, r);
            h.Click = action;
            return h;
        }

        public Hit Drag(string id, RectangleF r, int cursor, Action<PointF> drag, Action end)
        {
            Hit h = Area(id, r);
            h.Cursor = cursor;
            h.Drag = drag;
            h.DragEnd = end;
            return h;
        }

        // ---- Shapes ----

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            return Round(r, radius, radius, radius, radius);
        }

        public static GraphicsPath Round(RectangleF r, float tl, float tr, float br, float bl)
        {
            var p = new GraphicsPath();
            float max = Math.Min(r.Width, r.Height) / 2;
            tl = Math.Min(tl, max); tr = Math.Min(tr, max); br = Math.Min(br, max); bl = Math.Min(bl, max);
            if (tl > 0) p.AddArc(r.X, r.Y, tl * 2, tl * 2, 180, 90); else p.AddLine(r.X, r.Y, r.X, r.Y);
            if (tr > 0) p.AddArc(r.Right - tr * 2, r.Y, tr * 2, tr * 2, 270, 90); else p.AddLine(r.Right, r.Y, r.Right, r.Y);
            if (br > 0) p.AddArc(r.Right - br * 2, r.Bottom - br * 2, br * 2, br * 2, 0, 90); else p.AddLine(r.Right, r.Bottom, r.Right, r.Bottom);
            if (bl > 0) p.AddArc(r.X, r.Bottom - bl * 2, bl * 2, bl * 2, 90, 90); else p.AddLine(r.X, r.Bottom, r.X, r.Bottom);
            p.CloseFigure();
            return p;
        }

        public void Fill(RectangleF r, Color c, float radius)
        {
            if (c.A == 0) return;
            using (var b = new SolidBrush(c)) Fill(r, b, radius);
        }

        public void Fill(RectangleF r, Brush b, float radius)
        {
            if (radius <= 0)
            {
                G.FillRectangle(b, r);
                return;
            }
            using (GraphicsPath p = Round(r, radius)) G.FillPath(b, p);
        }

        // A CSS-style border: drawn inside the box.
        public void Border(RectangleF r, Color c, float width, float radius)
        {
            if (c.A == 0) return;
            var inner = new RectangleF(r.X + width / 2, r.Y + width / 2, r.Width - width, r.Height - width);
            using (var pen = new Pen(c, width))
            using (GraphicsPath p = Round(inner, Math.Max(0, radius - width / 2)))
                G.DrawPath(pen, p);
        }

        public void Circle(float cx, float cy, float r, Color c)
        {
            using (var b = new SolidBrush(c)) G.FillEllipse(b, cx - r, cy - r, r * 2, r * 2);
        }

        public void CircleBorder(float cx, float cy, float r, Color c, float width)
        {
            using (var pen = new Pen(c, width)) G.DrawEllipse(pen, cx - r + width / 2, cy - r + width / 2, (r - width / 2) * 2, (r - width / 2) * 2);
        }

        public void Line(float x1, float y1, float x2, float y2, Color c, float width)
        {
            using (var pen = new Pen(c, width)) G.DrawLine(pen, x1, y1, x2, y2);
        }

        // Approximates a CSS box-shadow by stacking faint rounded rectangles that grow across the blur.
        public void Shadow(RectangleF r, float radius, float dy, float blur, float spread, Color c)
        {
            const int steps = 10;
            double layer = 1 - Math.Pow(1 - c.A / 255.0, 1.0 / steps);
            using (var brush = new SolidBrush(Color.FromArgb((int)Math.Round(layer * 255), c.R, c.G, c.B)))
            {
                for (int i = 0; i < steps; i++)
                {
                    float grow = spread + blur * ((i + 0.5f) / steps - 0.5f);
                    var box = new RectangleF(r.X - grow, r.Y + dy - grow, r.Width + grow * 2, r.Height + grow * 2);
                    if (box.Width <= 0 || box.Height <= 0) continue;
                    Fill(box, brush, Math.Max(0, radius + grow));
                }
            }
        }

        // A soft elliptical glow, strongest in the middle, fading to nothing at the edge.
        public void Glow(RectangleF ellipse, Color c, float coreX, float coreY)
        {
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(ellipse);
                using (var brush = new PathGradientBrush(path))
                {
                    brush.CenterColor = c;
                    brush.SurroundColors = new[] { Color.FromArgb(0, c.R, c.G, c.B) };
                    brush.FocusScales = new PointF(coreX, coreY);
                    brush.Blend = SoftFalloff; // eases out like a blur instead of ending in a visible edge
                    G.FillPath(brush, path);
                }
            }
        }

        // A CSS-style blurred ellipse (filter: blur): drawn tiny, Gaussian-blurred, then scaled up smoothly.
        // sigma is the blur's standard deviation in design pixels, as in CSS.
        private static Bitmap blurCache;
        private static string blurCacheKey;

        public void BlurredEllipse(RectangleF ellipse, float sigma, Color color)
        {
            const float cell = 4; // design pixels per mask pixel: fine enough that scaling up stays smooth
            float pad = sigma * 3;
            int w = (int)Math.Ceiling((ellipse.Width + pad * 2) / cell);
            int h = (int)Math.Ceiling((ellipse.Height + pad * 2) / cell);
            var dest = new Rectangle((int)Math.Round(ellipse.X - pad), (int)Math.Round(ellipse.Y - pad), (int)(w * cell), (int)(h * cell));

            string key = w + "x" + h + "|" + ellipse.Width + "x" + ellipse.Height + "|" + sigma + "|" + color.ToArgb();
            if (key != blurCacheKey)
            {
                if (blurCache != null) blurCache.Dispose();
                blurCache = BlurredMask(w, h, pad / cell, ellipse.Width / cell, ellipse.Height / cell, sigma / cell, color);
                blurCacheKey = key;
            }

            using (var attributes = new System.Drawing.Imaging.ImageAttributes())
            {
                InterpolationMode oldInterpolation = G.InterpolationMode;
                PixelOffsetMode oldOffset = G.PixelOffsetMode;
                G.InterpolationMode = InterpolationMode.Bilinear;
                G.PixelOffsetMode = PixelOffsetMode.Half;
                attributes.SetWrapMode(WrapMode.TileFlipXY); // no dark fringe at the bitmap's edges
                G.DrawImage(blurCache, dest, 0, 0, w, h, GraphicsUnit.Pixel, attributes);
                G.InterpolationMode = oldInterpolation;
                G.PixelOffsetMode = oldOffset;
            }
        }

        private static Bitmap BlurredMask(int w, int h, float offset, float ellipseW, float ellipseH, float sigma, Color color)
        {
            var alpha = new float[w * h];
            float cx = offset + ellipseW / 2, cy = offset + ellipseH / 2, rx = ellipseW / 2, ry = ellipseH / 2;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    // 2x2 supersampling is plenty: the blur that follows is many pixels wide
                    int inside = 0;
                    for (int sy = 0; sy < 2; sy++)
                    {
                        for (int sx = 0; sx < 2; sx++)
                        {
                            float dx = (x + (sx + 0.5f) / 2 - cx) / rx, dy = (y + (sy + 0.5f) / 2 - cy) / ry;
                            if (dx * dx + dy * dy <= 1) inside++;
                        }
                    }
                    alpha[y * w + x] = inside / 4f;
                }
            }

            // Three box blurs approximate a Gaussian: each box of width n adds (n² − 1) / 12 to the variance.
            int radius = Math.Max(1, (int)Math.Round((Math.Sqrt(4 * sigma * sigma + 1) - 1) / 2));
            var scratch = new float[Math.Max(w, h)];
            for (int pass = 0; pass < 3; pass++)
            {
                for (int y = 0; y < h; y++) BoxBlur(alpha, y * w, 1, w, radius, scratch);
                for (int x = 0; x < w; x++) BoxBlur(alpha, x, w, h, radius, scratch);
            }

            var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var rect = new Rectangle(0, 0, w, h);
            System.Drawing.Imaging.BitmapData data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                var pixels = new byte[w * h * 4];
                for (int i = 0; i < w * h; i++)
                {
                    pixels[i * 4] = color.B;
                    pixels[i * 4 + 1] = color.G;
                    pixels[i * 4 + 2] = color.R;
                    pixels[i * 4 + 3] = (byte)Math.Round(Math.Min(1, alpha[i]) * color.A);
                }
                System.Runtime.InteropServices.Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
            }
            finally
            {
                bmp.UnlockBits(data);
            }
            return bmp;
        }

        // Blurs one row or column in place (start index, step between samples, count), treating outside as empty.
        private static void BoxBlur(float[] values, int start, int step, int count, int radius, float[] scratch)
        {
            float sum = 0;
            int width = radius * 2 + 1;
            for (int i = -radius - 1; i < count; i++)
            {
                int add = i + radius, remove = i - radius - 1;
                if (add >= 0 && add < count) sum += values[start + add * step];
                if (remove >= 0 && remove < count) sum -= values[start + remove * step];
                if (i >= 0) scratch[i] = sum / width;
            }
            for (int i = 0; i < count; i++) values[start + i * step] = scratch[i];
        }

        public static LinearGradientBrush Gradient(RectangleF r, Color[] colors, float[] positions, bool vertical)
        {
            var bounds = new RectangleF(r.X - 0.5f, r.Y - 0.5f, r.Width + 1, r.Height + 1);
            var brush = new LinearGradientBrush(bounds, colors[0], colors[colors.Length - 1],
                vertical ? LinearGradientMode.Vertical : LinearGradientMode.Horizontal);
            brush.InterpolationColors = new ColorBlend { Colors = colors, Positions = positions };
            brush.WrapMode = WrapMode.TileFlipX;
            return brush;
        }

        // ---- Text ----

        // Where the baseline sits for text centred in a CSS line box.
        public float Baseline(Font f, float top, float lineHeight)
        {
            float asc = Fonts.Ascent(f), desc = Fonts.Descent(f);
            return top + (lineHeight - (asc + desc)) / 2 + asc;
        }

        public float Normal(Font f)
        {
            return Fonts.LineSpacing(f);
        }

        public float Measure(string s, Font f)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            return G.MeasureString(s, f, PointF.Empty, Format).Width;
        }

        public float Text(string s, Font f, Color c, float x, float baseline)
        {
            return Text(s, f, c, x, baseline, Align.Left);
        }

        public float Text(string s, Font f, Color c, float x, float baseline, Align align)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            float w = Measure(s, f);
            if (align == Align.Center) x -= w / 2;
            else if (align == Align.Right) x -= w;
            using (var b = new SolidBrush(c)) G.DrawString(s, f, b, x, baseline - Fonts.Ascent(f), Format);
            return w;
        }

        // Letter-spaced text (the design's uppercase labels, and the tight serif time).
        public float MeasureTracked(string s, Font f, float tracking)
        {
            float w = 0;
            foreach (char ch in s) w += Measure(ch.ToString(), f) + tracking;
            return s.Length > 0 ? w - tracking : 0;
        }

        public float Tracked(string s, Font f, Color c, float x, float baseline, float tracking, Align align)
        {
            float w = MeasureTracked(s, f, tracking);
            if (align == Align.Center) x -= w / 2;
            else if (align == Align.Right) x -= w;
            using (var b = new SolidBrush(c))
            {
                foreach (char ch in s)
                {
                    string one = ch.ToString();
                    G.DrawString(one, f, b, x, baseline - Fonts.Ascent(f), Format);
                    x += Measure(one, f) + tracking;
                }
            }
            return w;
        }

        public float Label(string s, float size, Color c, float x, float top, float lineHeight, float trackingEm)
        {
            Font f = Fonts.Sans(size, 400);
            return Tracked(s.ToUpperInvariant(), f, c, x, Baseline(f, top, lineHeight), trackingEm * size, Align.Left);
        }

        public List<string> Wrap(string s, Font f, float width)
        {
            var lines = new List<string>();
            string line = "";
            foreach (string word in s.Split(' '))
            {
                string attempt = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && Measure(attempt, f) > width)
                {
                    lines.Add(line);
                    line = word;
                }
                else
                {
                    line = attempt;
                }
            }
            if (line.Length > 0) lines.Add(line);
            // Avoid a lone last word, as CSS text-wrap: pretty does.
            if (lines.Count >= 2 && lines[lines.Count - 1].IndexOf(' ') < 0)
            {
                string prev = lines[lines.Count - 2];
                int cut = prev.LastIndexOf(' ');
                if (cut > 0)
                {
                    string moved = prev.Substring(cut + 1) + " " + lines[lines.Count - 1];
                    if (Measure(moved, f) <= width)
                    {
                        lines[lines.Count - 2] = prev.Substring(0, cut);
                        lines[lines.Count - 1] = moved;
                    }
                }
            }
            return lines;
        }

        // Wrapped text; returns the height used.
        public float Paragraph(string s, Font f, Color c, float x, float top, float width, float lineHeight)
        {
            List<string> lines = Wrap(s, f, width);
            for (int i = 0; i < lines.Count; i++)
                Text(lines[i], f, c, x, Baseline(f, top + i * lineHeight, lineHeight));
            return lines.Count * lineHeight;
        }
    }
}
