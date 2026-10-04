using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Dusk.Ui
{
    // The Colors screen: presets on the left, one slider per part of the day on the right.
    public sealed partial class MainWindow
    {
        private float PaintColors(Canvas c, float y)
        {
            Palette p = c.P;
            DayTimes t = ctl.Times;
            y += 18;

            // ‹ Back   Colors                        Using <preset>
            Font serif = Fonts.Serif(38), back = Fonts.Sans(14), small = Fonts.Sans(13), smallBold = Fonts.Sans(13, 500);
            float baseline = c.Baseline(serif, y, 38);
            float backW = c.Text("‹ Back", back, c.IsHover("back") ? p.Ink : p.Muted, X, baseline);
            c.Click("back", new RectangleF(X - 4, y, backW + 8, 38), () => SetScreen(MainScreen.Main));
            c.Text("Colors", serif, p.Ink, X + backW + 16, baseline);
            string current = ctl.Schedule.PresetName();
            float currentW = c.Text(current, smallBold, p.Ink, X + W, baseline, Align.Right);
            c.Text("Using ", small, p.Muted, X + W - currentW, baseline, Align.Right);
            y += 38 + 20;

            // Presets
            Font name = Fonts.Sans(14, 500), desc = Fonts.Sans(12);
            float nameLh = c.Normal(name), descLh = c.Normal(desc);
            float itemH = 1 + 9 + nameLh + 1 + descLh + 9 + 1;
            float ly = y;
            Settings s = ctl.Settings;
            for (int i = 0; i <= Schedule.Presets.Length; i++)
            {
                bool custom = i == Schedule.Presets.Length;
                string presetName = custom ? Schedule.CustomName : Schedule.Presets[i].Name;
                string description = custom ? "Your own three temperatures." : Schedule.Presets[i].Description;
                int day = custom ? s.DayK : Schedule.Presets[i].Day;
                int sunset = custom ? s.SunsetK : Schedule.Presets[i].Sunset;
                int bed = custom ? s.BedK : Schedule.Presets[i].Bed;
                string id = "preset-" + i;
                var r = new RectangleF(X, ly, 300, itemH);
                bool on = presetName == current;
                if (on) c.Fill(r, p.Surface2, 8);
                else if (c.IsHover(id)) c.Fill(r, Palette.Alpha(p.Surface2, p.Surface2.A / 255.0 * 0.6), 8);
                c.Border(r, on ? p.Ink : p.Line2, 1, 8);

                var strip = new RectangleF(r.X + 13, ly + (itemH - 24) / 2, 42, 24);
                using (GraphicsPath clip = Canvas.Round(strip, 4))
                {
                    c.G.SetClip(clip);
                    c.Fill(new RectangleF(strip.X, strip.Y, 14, 24), Palette.Kelvin(day), 0);
                    c.Fill(new RectangleF(strip.X + 14, strip.Y, 14, 24), Palette.Kelvin(sunset), 0);
                    c.Fill(new RectangleF(strip.X + 28, strip.Y, 14, 24), Palette.Kelvin(bed), 0);
                    c.G.ResetClip();
                }
                c.Border(new RectangleF(strip.X - 1, strip.Y - 1, 44, 26), Palette.Alpha(Palette.OnColor, 0.12), 1, 4);

                float tx = strip.Right + 1 + 12;
                c.Text(presetName, name, p.Ink, tx, c.Baseline(name, ly + 10, nameLh));
                c.Text(description, desc, p.Muted, tx, c.Baseline(desc, ly + 10 + nameLh + 1, descLh));
                if (!custom)
                {
                    int d = day, su = sunset, b = bed;
                    c.Click(id, r, () => ctl.ApplyPreset(d, su, b));
                }
                ly += itemH + 6;
            }
            float leftBottom = ly - 6;

            // One slider per colour
            const float rx = X + 340, rw = W - 340;
            float ry = y;
            Font kFont = Fonts.Sans(13), when = Fonts.Sans(12);
            float whenLh = c.Normal(when);
            string morning = t.B2 > t.B1 ? ", and again before sunrise" : "";
            string[] whens =
            {
                "From sunrise, " + Schedule.FormatHour(t.Sunrise) + ", to sunset, " + Schedule.FormatHour(t.Sunset),
                Schedule.FormatHour(t.Sunset) + " to " + Schedule.FormatHour(t.Bed) + morning,
                Schedule.FormatHour(t.Bed) + " until you wake at " + Schedule.FormatHour(t.Wake)
            };
            Tone[] tones = { Tone.Day, Tone.Sunset, Tone.Bed };
            for (int i = 0; i < 3; i++)
            {
                Tone tone = tones[i];
                int k = s.GetTemp(tone);
                float rowBase = c.Baseline(name, ry, nameLh);
                c.Text(Schedule.NameOf(tone), name, p.Ink, rx, rowBase);
                c.Text(k == ColorTemp.Neutral ? "6500 K · no change" : Widgets.KelvinLabel(k), kFont, p.Ink, rx + rw, rowBase, Align.Right);
                ry += nameLh + 2;
                Widgets.Slider(c, "row-" + i, rx, ry, rw, 28, 10, 3, 22, k, Palette.Kelvin(k), false, v => ctl.SetTemp(tone, v));
                ry += 28;
                c.Text(whens[i], when, p.Muted, rx, c.Baseline(when, ry, whenLh));
                ry += whenLh + 20;
            }

            // Over the day
            Font eyebrow = Fonts.Sans(11);
            float eLh = c.Normal(eyebrow);
            c.Label("Over the day", 11, p.Muted, rx, ry, eLh, 0.06f);
            ry += eLh + 8;
            var bar = new RectangleF(rx, ry, rw, 18);
            using (LinearGradientBrush brush = Widgets.DayGradient(ctl, bar)) c.Fill(bar, brush, 9);
            c.Border(bar, p.Line2, 1, 9);
            float nx = rx + (float)(ctl.DisplayHour / 24) * rw;
            c.Fill(new RectangleF(nx - 1, ry - 5, 2, 28), p.Ink, 0);
            ry += 18 + 6;
            ry += Widgets.HourLabels(c, rx, ry, rw);

            return Math.Max(leftBottom, ry) + 30;
        }
    }
}
