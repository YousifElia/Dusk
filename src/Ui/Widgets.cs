using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Dusk.Ui
{
    // Pieces both windows draw: the colour slider, −/+ buttons, the day's colour strip and hour labels.
    public static class Widgets
    {
        private static readonly string[] Hours = { "12 AM", "6 AM", "12 PM", "6 PM", "12 AM" };
        private static Bitmap icon;

        public static void Icon(Canvas c, float x, float y, float size)
        {
            if (icon == null) icon = IconArt.Render(64, false);
            InterpolationMode old = c.G.InterpolationMode;
            c.G.InterpolationMode = InterpolationMode.HighQualityBicubic;
            c.G.DrawImage(icon, new RectangleF(x, y, size, size));
            c.G.InterpolationMode = old;
        }

        // The screen colour across the day, as a left-to-right gradient.
        public static LinearGradientBrush DayGradient(Controller ctl, RectangleF r)
        {
            const int steps = 48;
            DayTimes t = ctl.Times;
            var colors = new Color[steps + 1];
            var positions = new float[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                colors[i] = Palette.Kelvin(ctl.Schedule.KelvinAt(t, i * 24.0 / steps));
                positions[i] = (float)i / steps;
            }
            return Canvas.Gradient(r, colors, positions, false);
        }

        private static LinearGradientBrush TrackGradient(RectangleF r)
        {
            const int steps = 24;
            var colors = new Color[steps + 1];
            var positions = new float[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                double p = (double)i / steps;
                double k = p <= Palette.NeutralPosition
                    ? ColorTemp.Min + p / Palette.NeutralPosition * (ColorTemp.Neutral - ColorTemp.Min)
                    : ColorTemp.Neutral + (p - Palette.NeutralPosition) / (1 - Palette.NeutralPosition) * (ColorTemp.Max - ColorTemp.Neutral);
                colors[i] = Palette.Kelvin(k);
                positions[i] = (float)p;
            }
            return Canvas.Gradient(r, colors, positions, false);
        }

        // A temperature slider: gradient track, knob with an ink ring, small tick at 6500K ("no change").
        public static void Slider(Canvas c, string id, float x, float top, float width, float height,
            float trackTop, float knobTop, float knobSize, int kelvin, Color knobFill, bool shadow, Action<int> set)
        {
            var track = new RectangleF(x, top + trackTop, width, 8);
            using (LinearGradientBrush brush = TrackGradient(track)) c.Fill(track, brush, 4);
            c.Border(track, c.P.Line2, 1, 4);
            float tickX = x + (float)Palette.NeutralPosition * width;
            c.Line(tickX, track.Bottom + 2, tickX, track.Bottom + 5, c.P.Line, 1);

            float cx = x + (float)Palette.KelvinToPosition(kelvin) * width;
            var knob = new RectangleF(cx - knobSize / 2, top + knobTop, knobSize, knobSize);
            if (shadow) c.Shadow(knob, knobSize / 2, 2, 6, 0, Color.FromArgb(64, 0, 0, 0));
            c.Circle(cx, knob.Y + knobSize / 2, knobSize / 2, knobFill);
            c.CircleBorder(cx, knob.Y + knobSize / 2, knobSize / 2, c.P.Ink, 2);

            Hit hit = c.Drag(id, new RectangleF(x - knobSize / 2, top, width + knobSize, height), Native.IDC_HAND,
                p => set(Palette.PositionToKelvin((p.X - x) / width)), null);
            hit.Wheel = delta => set(ColorTemp.Clamp(kelvin + (delta > 0 ? 100 : -100), ColorTemp.Min, ColorTemp.Max));
        }

        public static void StepButton(Canvas c, string id, RectangleF r, bool plus, float radius, Action click)
        {
            if (c.IsHover(id)) c.Fill(r, c.P.Surface2, radius);
            c.Border(r, c.P.Line, 1, radius);
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, arm = r.Width * 0.16f;
            c.Line(cx - arm, cy, cx + arm, cy, c.P.Ink, 1.4f);
            if (plus) c.Line(cx, cy - arm, cx, cy + arm, c.P.Ink, 1.4f);
            c.Click(id, r, click);
        }

        // "12 AM · 6 AM · 12 PM · 6 PM · 12 AM" spread edge to edge, like CSS space-between. Returns the height.
        public static float HourLabels(Canvas c, float x, float top, float width)
        {
            Font f = Fonts.Sans(11);
            float lh = c.Normal(f), total = 0;
            var widths = new float[Hours.Length];
            for (int i = 0; i < Hours.Length; i++)
            {
                widths[i] = c.Measure(Hours[i], f);
                total += widths[i];
            }
            float gap = (width - total) / (Hours.Length - 1), cursor = x, baseline = c.Baseline(f, top, lh);
            for (int i = 0; i < Hours.Length; i++)
            {
                c.Text(Hours[i], f, c.P.Muted, cursor, baseline);
                cursor += widths[i] + gap;
            }
            return lh;
        }

        public static string KelvinLabel(int kelvin)
        {
            return kelvin + " K";
        }
    }
}
