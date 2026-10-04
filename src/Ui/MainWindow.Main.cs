using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Dusk.Ui
{
    // The main screen: time, view switcher, the day view (Horizon, Dial or Strata), the colour slider,
    // wake/sunrise/sunset/bedtime, and the footer.
    public sealed partial class MainWindow
    {
        private const float X = 48, W = 784, ViewHeight = 320;
        private static readonly string[] ViewKeys = { "horizon", "dial", "strata" };
        private static readonly string[] ViewNames = { "Horizon", "Dial", "Strata" };
        private static readonly Color DocLine1 = Palette.Hex("#CFCBC3"), DocLine2 = Palette.Hex("#E2DED6");

        private float PaintMain(Canvas c, float y)
        {
            Palette p = c.P;
            DayTimes t = ctl.Times;
            double hour = ctl.DisplayHour;
            Tone sel = ctl.SelectedTone;
            y += 12;

            PaintHeader(c, y, hour);
            y += 56 + 6;
            y += PaintStatusRow(c, y, t, hour);

            y += 16;
            switch (ctl.Settings.View)
            {
                case "dial": PaintDial(c, y, t, hour, sel); break;
                case "strata": PaintStrata(c, y, t, hour, sel); break;
                default: PaintHorizon(c, y, t, hour); break;
            }
            y += ViewHeight;

            // Colour for the selected part of the day
            y += 18;
            Font label = Fonts.Sans(13), value = Fonts.Sans(13, 500), tiny = Fonts.Sans(11);
            float lh = c.Normal(label), tinyLh = c.Normal(tiny);
            int kelvin = ctl.Settings.GetTemp(sel);
            float baseline = c.Baseline(label, y, lh);
            c.Text(Schedule.NameOf(sel) + " color", label, p.Muted, X, baseline);
            c.Text(kelvin == ColorTemp.Neutral ? "6500 K · no change" : Widgets.KelvinLabel(kelvin), value, p.Ink, X + W, baseline, Align.Right);
            y += lh + 2;
            Widgets.Slider(c, "main-slider", X, y, W, 32, 12, 4, 24, kelvin, p.Knob, true, k => ctl.SetTemp(sel, k));
            y += 32;
            float tinyBase = c.Baseline(tiny, y, tinyLh);
            c.Text("600 K · warmest", tiny, p.Muted, X, tinyBase);
            c.Text("9300 K · coolest", tiny, p.Muted, X + W, tinyBase, Align.Right);
            y += tinyLh;

            y += 20;
            y += PaintStats(c, y, t);
            y += 14;
            y += PaintFooter(c, y);
            return y + 24;
        }

        private void PaintHeader(Canvas c, float y, double hour)
        {
            Palette p = c.P;
            Font big = Fonts.Serif(56);
            c.Tracked(Schedule.FormatHour(hour), big, p.Ink, X, c.Baseline(big, y, 56), -0.84f, Align.Left);

            // Light/dark toggle
            var theme = new RectangleF(X + W - 36, y + 8, 36, 36);
            c.Fill(theme, c.IsHover("theme") ? Palette.Alpha(p.Ink, 0.1) : p.Surface2, 9);
            c.Border(theme, p.Line2, 1, 9);
            float ix = theme.X + 18, iy = theme.Y + 18;
            using (var ink = new SolidBrush(p.Ink)) c.G.FillPie(ink, ix - 7, iy - 7, 14, 14, 90, 180);
            c.CircleBorder(ix, iy, 7, p.Ink, 1.5f);
            c.Click("theme", theme, ctl.ToggleTheme).Cursor = Native.IDC_HAND;

            // Horizon · Dial · Strata
            Font f = Fonts.Sans(13, 500);
            var widths = new float[ViewKeys.Length];
            float segW = 8;
            for (int i = 0; i < ViewKeys.Length; i++)
            {
                widths[i] = c.Measure(ViewNames[i], f) + 24;
                segW += widths[i];
            }
            var seg = new RectangleF(theme.X - 8 - segW, y + 8, segW, 36);
            c.Fill(seg, p.Surface2, 9);
            c.Border(seg, p.Line2, 1, 9);
            float bx = seg.X + 4;
            for (int i = 0; i < ViewKeys.Length; i++)
            {
                string key = ViewKeys[i];
                string id = "view-" + key;
                bool active = ctl.Settings.View == key;
                var b = new RectangleF(bx, seg.Y + 4, widths[i], 28);
                if (active) c.Fill(b, p.Btn, 6);
                else if (c.IsHover(id)) c.Fill(b, p.Surface2, 6);
                c.Text(ViewNames[i], f, active ? p.BtnFg : p.Ink, b.X + b.Width / 2, c.Baseline(f, b.Y, 28), Align.Center);
                c.Click(id, b, () => ctl.SetView(key));
                bx += widths[i];
            }
        }

        // Solar line on the left; screen preview, current temperature and "Play the day" on the right.
        private float PaintStatusRow(Canvas c, float y, DayTimes t, double hour)
        {
            Palette p = c.P;
            bool off = ctl.IsOff;
            int kNow = ctl.DisplayKelvin;
            Color tint = Palette.Kelvin(off ? ColorTemp.Neutral : ctl.IsDarkroom ? ColorTemp.Min : kNow);

            Font kFont = Fonts.Sans(16, 600), perFont = Fonts.Sans(12), playFont = Fonts.Sans(13), solar = Fonts.Sans(14);
            float kLh = c.Normal(kFont), perLh = c.Normal(perFont);
            float rowH = Math.Max(kLh + perLh, 36), mid = y + rowH / 2;
            string kLabel = off ? "Off" : ctl.IsDarkroom ? "Darkroom" : kNow + " K";
            string perLine = off ? ctl.OffNote : ctl.IsDarkroom ? "Red only, inverted" : Schedule.NameOf(ctl.DisplayPeriod) + " colors";

            string play = ctl.Playing ? "Pause" : ctl.PreviewHour.HasValue ? "Back to now" : "Play the day";
            float playW = c.Measure(play, playFont) + 30;
            var playRect = new RectangleF(X + W - playW, mid - 16, playW, 32);
            if (c.IsHover("play")) c.Fill(playRect, p.Surface2, 16);
            c.Border(playRect, p.Line, 1, 16);
            c.Text(play, playFont, p.Ink, playRect.X + playW / 2, c.Baseline(playFont, playRect.Y, 32), Align.Center);
            c.Click("play", playRect, () =>
            {
                if (!ctl.Playing && ctl.PreviewHour.HasValue) ctl.BackToNow();
                else ctl.TogglePlay();
            });

            float blockW = Math.Max(c.Measure(kLabel, kFont), c.Measure(perLine, perFont));
            float bx = playRect.X - 14 - blockW, blockTop = mid - (kLh + perLh) / 2;
            c.Text(kLabel, kFont, p.Ink, bx, c.Baseline(kFont, blockTop, kLh));
            c.Text(perLine, perFont, p.Muted, bx, c.Baseline(perFont, blockTop + kLh, perLh));

            // A little page, tinted the way the screen is right now
            var doc = new RectangleF(bx - 14 - 56, mid - 18, 56, 36);
            c.Fill(doc, tint, 4);
            c.Fill(new RectangleF(doc.X + 6, doc.Y + 7, 44, 4), Palette.Multiply(DocLine1, tint), 2);
            c.Fill(new RectangleF(doc.X + 6, doc.Y + 15, 32, 3), Palette.Multiply(DocLine2, tint), 1.5f);
            c.Fill(new RectangleF(doc.X + 6, doc.Y + 22, 26, 3), Palette.Multiply(DocLine2, tint), 1.5f);
            c.Border(doc, p.Line, 1, 4);

            float solarLh = c.Normal(solar);
            c.Text(ctl.Schedule.SolarLine(t, hour), solar, p.Muted, X, c.Baseline(solar, mid - solarLh / 2, solarLh));
            return rowH;
        }

        // ---- Horizon: sky strip with the sun's path over the day's screen colour ----

        private void PaintHorizon(Canvas c, float top, DayTimes t, double hour)
        {
            Palette p = c.P;
            float labelLh = c.Normal(Fonts.Sans(11));
            float sy = top + (ViewHeight - (64 + 150 + 14 + labelLh)) / 2;

            var sky = new RectangleF(X, sy, W, 64);
            using (GraphicsPath skyPath = Canvas.Round(sky, 8, 8, 0, 0))
            using (LinearGradientBrush brush = SkyGradient(sky, t))
            {
                c.G.FillPath(brush, skyPath);
                c.G.SetClip(skyPath);
                PaintSunPath(c, sky, t, hour);
                c.G.ResetClip();
            }

            var area = new RectangleF(X, sy + 64, W, 150);
            using (GraphicsPath areaBg = Canvas.Round(area, 0, 0, 8, 8))
            using (var bg = new SolidBrush(p.Area))
                c.G.FillPath(bg, areaBg);

            const int samples = 192;
            var curve = new PointF[samples + 1];
            for (int i = 0; i <= samples; i++)
            {
                double h = i * 24.0 / samples;
                double k = ctl.Schedule.KelvinAt(t, h);
                curve[i] = new PointF(X + (float)(h / 24) * W, area.Bottom - 8 - (float)Palette.KelvinToChart(k) * (150 - 22));
            }
            using (var fill = new GraphicsPath())
            {
                fill.AddLines(curve);
                fill.AddLine(area.Right, curve[samples].Y, area.Right, area.Bottom - 8);
                fill.AddArc(area.Right - 16, area.Bottom - 16, 16, 16, 0, 90);
                fill.AddLine(area.Right - 8, area.Bottom, area.X + 8, area.Bottom);
                fill.AddArc(area.X, area.Bottom - 16, 16, 16, 90, 90);
                fill.CloseFigure();
                using (LinearGradientBrush brush = Widgets.DayGradient(ctl, area)) c.G.FillPath(brush, fill);
            }
            using (var pen = new Pen(Palette.Alpha(Palette.OnColor, 0.6), 1.5f) { LineJoin = LineJoin.Round })
                c.G.DrawLines(pen, curve);
            c.Label("Screen color over the day", 11, Palette.Alpha(Palette.OnColor, 0.72), X + 12, area.Bottom - 8 - labelLh, labelLh, 0.06f);

            // Now line and handle
            float nx = X + (float)(hour / 24) * W;
            c.Fill(new RectangleF(nx - 1, sy, 2, area.Bottom - sy), p.Ink, 0);
            c.Circle(nx, area.Bottom - 2, 10, p.Bg);
            c.Circle(nx, area.Bottom - 2, 7, p.Ink);

            c.Drag("horizon", new RectangleF(X, sy, W, area.Bottom - sy), Native.IDC_SIZEWE,
                pt => ctl.SetPreviewHour((pt.X - X) / W * 24), null);
            Widgets.HourLabels(c, X, area.Bottom + 14, W);
        }

        // Night, dawn, day and dusk colours, placed around today's actual sunrise and sunset.
        private static LinearGradientBrush SkyGradient(RectangleF r, DayTimes t)
        {
            double rise = t.Sunrise, set = t.Sunset < t.Sunrise ? t.Sunset + 24 : t.Sunset;
            double[] hours = { 0, rise - 1.77, rise - 0.57, rise, rise + 1.11, (rise + set) / 2, set - 1.09, set, set + 0.84, set + 2.27, 24 };
            string[] hex = { "#1B1F3D", "#232A55", "#5B5C8F", "#F1B37D", "#C8DDF2", "#DDEBF7", "#C7DAEF", "#F0A363", "#5A4B7A", "#232A55", "#1B1F3D" };
            var colors = new Color[hex.Length];
            var positions = new float[hex.Length];
            float last = 0;
            for (int i = 0; i < hex.Length; i++)
            {
                colors[i] = Palette.Hex(hex[i]);
                float pos = (float)(Math.Max(0, Math.Min(24, hours[i])) / 24);
                if (i > 0) pos = Math.Max(pos, last + 0.001f);
                positions[i] = last = Math.Min(pos, 1);
            }
            positions[0] = 0;
            positions[hex.Length - 1] = 1;
            return Canvas.Gradient(r, colors, positions, false);
        }

        private void PaintSunPath(Canvas c, RectangleF sky, DayTimes t, double hour)
        {
            double rise = t.Sunrise, set = t.Sunset < t.Sunrise ? t.Sunset + 24 : t.Sunset;
            double dayLen = set - rise, nightLen = 24 - dayLen;
            Func<double, float> px = h => sky.X + (float)(Schedule.Mod24(h) / 24) * sky.Width;

            var sun = new PointF[61];
            for (int i = 0; i <= 60; i++)
                sun[i] = new PointF(px(rise + i / 60.0 * dayLen), sky.Y + 46 - (float)Math.Sin(Math.PI * i / 60) * 36);
            using (var pen = new Pen(Color.FromArgb(153, 255, 255, 255), 1) { DashPattern = new[] { 2f, 4f } })
                c.G.DrawLines(pen, sun);

            using (var pen = new Pen(Color.FromArgb(71, 255, 255, 255), 1) { DashPattern = new[] { 2f, 4f } })
            {
                PointF prev = PointF.Empty;
                for (int i = 0; i <= 60; i++)
                {
                    double h = set + i / 60.0 * nightLen;
                    var pt = new PointF(px(h), sky.Y + 46 + (float)Math.Sin(Math.PI * i / 60) * 12);
                    if (i > 0 && Math.Abs(pt.X - prev.X) < sky.Width / 2) c.G.DrawLine(pen, prev, pt); // skip the wrap at midnight
                    prev = pt;
                }
            }
            c.Line(sky.X, sky.Y + 46, sky.Right, sky.Y + 46, Color.FromArgb(89, 255, 255, 255), 1);

            double hh = hour < rise ? hour + 24 : hour;
            bool isDay = hh >= rise && hh < set;
            double elevation = isDay
                ? Math.Sin(Math.PI * (hh - rise) / dayLen)
                : -Math.Sin(Math.PI * Schedule.Mod24(hour - set) / nightLen);
            float sx = sky.X + (float)(hour / 24) * sky.Width;
            float syy = sky.Y + 46 - (float)(isDay ? elevation * 36 : elevation * 12);
            Color fill = Palette.Hex(isDay ? "#FFE9A8" : "#E6E4F3");
            c.Glow(new RectangleF(sx - 16, syy - 16, 32, 32), Palette.Alpha(fill, 0.8), 0.4f, 0.4f);
            c.Circle(sx, syy, 7, fill);
        }

        // ---- Dial: the day as a ring ----

        private void PaintDial(Canvas c, float top, DayTimes t, double hour, Tone sel)
        {
            Palette p = c.P;
            float cx = X + 22 + 138, cy = top + 22 + 138;
            Color tint = Palette.Kelvin(ctl.IsOff ? ColorTemp.Neutral : ctl.DisplayKelvin);

            const int segments = 240;
            for (int i = 0; i < segments; i++)
            {
                float start = -90 + i * 360f / segments - 0.4f, sweep = 360f / segments + 0.8f;
                using (var path = new GraphicsPath())
                using (var brush = new SolidBrush(Palette.Kelvin(ctl.Schedule.KelvinAt(t, (i + 0.5) * 24.0 / segments))))
                {
                    path.AddArc(cx - 138, cy - 138, 276, 276, start, sweep);
                    path.AddArc(cx - 112, cy - 112, 224, 224, start + sweep, -sweep);
                    path.CloseFigure();
                    c.G.FillPath(brush, path);
                }
            }

            // Inner glow in the current colour
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(cx - 112, cy - 112, 224, 224);
                using (var brush = new PathGradientBrush(path))
                {
                    brush.CenterColor = Palette.Alpha(tint, 0);
                    brush.SurroundColors = new[] { Palette.Alpha(tint, 0.45) };
                    brush.FocusScales = new PointF(0.5f, 0.5f);
                    c.G.FillPath(brush, path);
                }
            }

            for (int i = 0; i < 24; i++)
            {
                bool major = i % 6 == 0;
                double a = i * 15 * Math.PI / 180;
                float len = major ? 9 : 5, sin = (float)Math.Sin(a), cos = (float)Math.Cos(a);
                c.Line(cx + 108 * sin, cy - 108 * cos, cx + (108 - len) * sin, cy - (108 - len) * cos, Palette.Alpha(p.Ink, major ? 0.7 : 0.3), 1);
            }

            foreach (double h in new[] { t.Wake, t.Bed, t.Sunrise, t.Sunset })
            {
                PointF m = OnRing(cx, cy, 127, h);
                c.Circle(m.X, m.Y, 4.5f, p.Ink);
                c.Circle(m.X, m.Y, 3, p.Bg);
            }
            PointF now = OnRing(cx, cy, 127, hour);
            c.Glow(new RectangleF(now.X - 30, now.Y - 30, 60, 60), Palette.Alpha(tint, 0.9), 0.3f, 0.3f);
            c.Circle(now.X, now.Y, 14, p.Bg);
            c.Circle(now.X, now.Y, 10, p.Knob);

            // Centre: time, temperature, period
            Font small = Fonts.Sans(12), num = Fonts.Serif(62), unit = Fonts.Sans(18), per = Fonts.Sans(13);
            float smallLh = c.Normal(small), perLh = c.Normal(per);
            float blockTop = cy - (smallLh + 4 + 62 + 8 + perLh) / 2;
            c.Tracked(Schedule.FormatHour(hour), small, p.Muted, cx, c.Baseline(small, blockTop, smallLh), 0.48f, Align.Center);
            string kText = ctl.IsOff ? "—" : ctl.DisplayKelvin.ToString();
            float numW = c.MeasureTracked(kText, num, -1.24f), unitW = c.Measure("K", unit);
            float rowX = cx - (numW + 5 + unitW) / 2, numBase = c.Baseline(num, blockTop + smallLh + 4, 62);
            c.Tracked(kText, num, p.Ink, rowX, numBase, -1.24f, Align.Left);
            c.Text("K", unit, p.Muted, rowX + numW + 5, numBase);
            string perLine = ctl.IsOff ? ctl.OffNote : Schedule.NameOf(ctl.DisplayPeriod) + " colors";
            c.Text(perLine, per, p.Muted, cx, c.Baseline(per, blockTop + smallLh + 4 + 62 + 8, perLh), Align.Center);

            Font hourFont = Fonts.Sans(11);
            float hLh = c.Normal(hourFont);
            c.Text("12 AM", hourFont, p.Muted, cx, c.Baseline(hourFont, cy - 138 - 20, hLh), Align.Center);
            c.Text("12 PM", hourFont, p.Muted, cx, c.Baseline(hourFont, cy + 138 + 20 - hLh, hLh), Align.Center);
            c.Text("6 AM", hourFont, p.Muted, cx + 138 + 40, c.Baseline(hourFont, cy - hLh / 2, hLh), Align.Right);
            c.Text("6 PM", hourFont, p.Muted, cx - 138 - 38, c.Baseline(hourFont, cy - hLh / 2, hLh));

            c.Drag("dial", new RectangleF(cx - 138, cy - 138, 276, 276), Native.IDC_HAND, pt =>
            {
                double angle = Math.Atan2(pt.Y - cy, pt.X - cx) * 180 / Math.PI + 90;
                ctl.SetPreviewHour(Schedule.Mod24(angle / 360 * 24));
            }, null);

            // Periods
            const float rx = X + 320 + 40, rw = W - 360;
            Font eyebrow = Fonts.Sans(11), name = Fonts.Sans(14, 500), range = Fonts.Sans(12), kFont = Fonts.Sans(14);
            float eLh = c.Normal(eyebrow), nLh = c.Normal(name), rLh = c.Normal(range), hintLh = c.Normal(range);
            float itemH = 10 + nLh + 1 + rLh + 10 + 2;
            float blockH = eLh + 10 + itemH * 3 + 12 + 12 + hintLh;
            float y = top + (ViewHeight - blockH) / 2;
            c.Label("Periods · click one to adjust it", 11, p.Muted, rx, y, eLh, 0.08f);
            y += eLh + 10;

            string[] ranges =
            {
                Schedule.FormatHour(t.Sunrise) + " – " + Schedule.FormatHour(t.Sunset),
                Schedule.FormatHour(t.Sunset) + " – " + Schedule.FormatHour(t.Bed)
                    + (t.B2 > t.B1 ? " · " + Schedule.FormatHour(t.Wake) + " – " + Schedule.FormatHour(t.Sunrise) : ""),
                Schedule.FormatHour(t.Bed) + " – " + Schedule.FormatHour(t.Wake)
            };
            Tone[] tones = { Tone.Day, Tone.Sunset, Tone.Bed };
            for (int i = 0; i < 3; i++)
            {
                Tone tone = tones[i];
                string id = "period-" + i;
                var r = new RectangleF(rx, y, rw, itemH);
                bool on = sel == tone;
                if (on || c.IsHover(id)) c.Fill(r, p.Surface2, 8);
                c.Border(r, on ? p.Ink : p.Line2, 1, 8);
                int k = ctl.Settings.GetTemp(tone);
                c.Circle(r.X + 13 + 11, r.Y + itemH / 2, 11, Palette.Kelvin(k));
                c.CircleBorder(r.X + 13 + 11, r.Y + itemH / 2, 11, Palette.Alpha(Palette.OnColor, 0.15), 1);
                float tx = r.X + 13 + 22 + 12;
                c.Text(Schedule.NameOf(tone), name, p.Ink, tx, c.Baseline(name, r.Y + 11, nLh));
                c.Text(ranges[i], range, p.Muted, tx, c.Baseline(range, r.Y + 11 + nLh + 1, rLh));
                c.Text(Widgets.KelvinLabel(k), kFont, p.Ink, r.Right - 13, c.Baseline(kFont, r.Y, itemH), Align.Right);
                c.Click(id, r, () => ctl.SelectTone(tone));
                y += itemH + 6;
            }
            y += 12 - 6;
            c.Text("Drag anywhere on the dial to preview a time.", range, p.Muted, rx, c.Baseline(range, y, hintLh));
        }

        private static PointF OnRing(float cx, float cy, float radius, double hour)
        {
            double a = hour / 24 * 2 * Math.PI;
            return new PointF(cx + radius * (float)Math.Sin(a), cy - radius * (float)Math.Cos(a));
        }

        // ---- Strata: the day stacked from the moment you wake ----

        private void PaintStrata(Canvas c, float top, DayTimes t, double hour, Tone sel)
        {
            Palette p = c.P;
            const float bandsX = X + 56, bandsW = 352 - 56;
            float nowY = (float)(Schedule.Mod24(hour - t.Wake) / 24) * ViewHeight;

            // Hour rail
            c.Fill(new RectangleF(X + 22, top, 2, ViewHeight), p.Line, 0);
            Font tick = Fonts.Sans(10);
            float tickLh = c.Normal(tick);
            for (int i = 0; i < 7; i++)
            {
                float ty = top + (i + 1) * 3 / 24f * ViewHeight;
                c.Fill(new RectangleF(X, ty - tickLh / 2 - 2, 46, tickLh + 4), p.Bg, 0);
                c.Text(Schedule.FormatHour(t.Wake + (i + 1) * 3), tick, p.Muted, X + 23, c.Baseline(tick, ty - tickLh / 2, tickLh), Align.Center);
            }
            c.Circle(X + 23, top + nowY, 10, p.Bg);
            c.Circle(X + 23, top + nowY, 7, p.Ink);
            c.Drag("rail", new RectangleF(X, top, 46, ViewHeight), Native.IDC_SIZENS,
                pt => ctl.SetPreviewHour(Schedule.Mod24(t.Wake + Math.Max(0, Math.Min(1, (pt.Y - top) / ViewHeight)) * 24)), null);

            // Bands
            var box = new RectangleF(bandsX, top, bandsW, ViewHeight);
            c.Shadow(box, 10, 12, 30, -14, Color.FromArgb(115, 0, 0, 0));
            string[] names = { "Morning", "Daytime", "Sunset", "Bedtime" };
            double[] from = { t.B1, t.B2, t.B3, t.B4 };
            double[] to = { t.B2, t.B3, t.B4, t.B1 + 24 };
            Tone[] tones = { Tone.Sunset, Tone.Day, Tone.Sunset, Tone.Bed };
            Font name = Fonts.Sans(13, 600), kFont = Fonts.Sans(12), range = Fonts.Sans(11);
            float nameLh = c.Normal(name), rangeLh = c.Normal(range);

            using (GraphicsPath clip = Canvas.Round(box, 10))
            {
                c.G.SetClip(clip);
                float by = top;
                for (int i = 0; i < 4; i++)
                {
                    float bh = (float)((to[i] - from[i]) / 24) * ViewHeight;
                    if (bh < 0.5f) continue;
                    var band = new RectangleF(bandsX, by, bandsW, bh);
                    int k = ctl.Settings.GetTemp(tones[i]);
                    c.Fill(band, Palette.Kelvin(k), 0);

                    Region previous = c.G.Clip;
                    c.G.IntersectClip(band);
                    if (bh >= 40)
                    {
                        float nb = c.Baseline(name, by + 10, nameLh);
                        c.Text(names[i], name, Palette.BandText, bandsX + 14, nb);
                        c.Text(Widgets.KelvinLabel(k), kFont, Palette.BandText, bandsX + bandsW - 14, nb, Align.Right);
                    }
                    c.Text(Schedule.FormatHour(from[i]) + " – " + Schedule.FormatHour(to[i]), range, Palette.Alpha(Palette.BandText, 0.7),
                        bandsX + 14, c.Baseline(range, by + 10 + nameLh + 2, rangeLh));
                    c.G.Clip = previous;

                    if (tones[i] == sel) c.Border(band, Palette.OnColor, 2, 0);
                    else c.Line(bandsX, by + bh - 0.5f, bandsX + bandsW, by + bh - 0.5f, Palette.Alpha(Palette.OnColor, 0.15), 1);
                    Tone tone = tones[i];
                    c.Click("band-" + i, band, () => ctl.SelectTone(tone));
                    by += bh;
                }

                // Now line with its time tag
                c.Fill(new RectangleF(bandsX, top + nowY - 1, bandsW, 2), Palette.OnColor, 0);
                Font tagFont = Fonts.Sans(11, 600);
                string label = Schedule.FormatHour(hour);
                float tagW = c.Measure(label, tagFont) + 14, tagH = c.Normal(tagFont) + 8;
                var tag = new RectangleF(bandsX + bandsW - 8 - tagW, top + nowY - 24, tagW, tagH);
                c.Fill(tag, Palette.OnColor, 4);
                c.Text(label, tagFont, Palette.TagText, tag.X + 7, c.Baseline(tagFont, tag.Y, tagH));
                c.G.ResetClip();
            }

            // Selected part of the day
            const float rx = X + 352 + 40, rw = W - 392;
            Font eyebrow = Fonts.Sans(11), body = Fonts.Sans(14), hint = Fonts.Sans(12);
            float eLh = c.Normal(eyebrow), hintLh = c.Normal(hint);
            string selRange = sel == Tone.Day
                ? "From sunrise at " + Schedule.FormatHour(t.Sunrise) + " to sunset at " + Schedule.FormatHour(t.Sunset) + "."
                : sel == Tone.Sunset
                    ? "From sunset at " + Schedule.FormatHour(t.Sunset) + " until bedtime at " + Schedule.FormatHour(t.Bed)
                        + (t.B2 > t.B1 ? ", and again from " + Schedule.FormatHour(t.Wake) + " until sunrise." : ".")
                    : "From bedtime at " + Schedule.FormatHour(t.Bed) + " until you wake at " + Schedule.FormatHour(t.Wake) + ".";
            float bodyH = c.Wrap(selRange, body, rw).Count * 20.3f;
            float blockH = eLh + 10 + 44 + 10 + bodyH + 20 + hintLh;
            float y = top + (ViewHeight - blockH) / 2;
            c.Label("Your day, from the moment you wake", 11, p.Muted, rx, y, eLh, 0.08f);
            y += eLh + 10;
            Font serif = Fonts.Serif(44);
            c.Tracked(Schedule.NameOf(sel), serif, p.Ink, rx, c.Baseline(serif, y, 44), -0.44f, Align.Left);
            y += 44 + 10;
            y += c.Paragraph(selRange, body, p.Muted, rx, y, rw, 20.3f);
            y += 20;
            c.Text("Drag the hour rail to preview a time · click a band to adjust it.", hint, p.Muted, rx, c.Baseline(hint, y, hintLh));
        }

        // ---- Wake · Sunrise · Sunset · Bedtime ----

        private float PaintStats(Canvas c, float y, DayTimes t)
        {
            Palette p = c.P;
            Font label = Fonts.Sans(11), value = Fonts.Sans(18, 500);
            float lLh = c.Normal(label), vLh = c.Normal(value);
            float cellH = 12 + lLh + 2 + vLh + 12;
            c.Fill(new RectangleF(X, y, W, cellH + 2), p.Line2, 10);

            string[] labels = { "Wake", "Sunrise", "Sunset", "Bedtime" };
            string[] values = { Schedule.FormatHour(t.Wake), Schedule.FormatHour(t.Sunrise), Schedule.FormatHour(t.Sunset), Schedule.FormatHour(t.Bed) };
            float cellW = (W - 2 - 3) / 4f;
            for (int i = 0; i < 4; i++)
            {
                var cell = new RectangleF(X + 1 + i * (cellW + 1), y + 1, cellW, cellH);
                using (var bg = new SolidBrush(p.Bg))
                using (GraphicsPath path = Canvas.Round(cell, i == 0 ? 9 : 0, i == 3 ? 9 : 0, i == 3 ? 9 : 0, i == 0 ? 9 : 0))
                    c.G.FillPath(bg, path);
                c.Label(labels[i], 11, p.Muted, cell.X + 16, cell.Y + 12, lLh, 0.06f);
                c.Text(values[i], value, p.Ink, cell.X + 16, c.Baseline(value, cell.Y + 12 + lLh + 2, vLh));
            }

            float right = X + 1 + cellW - 16, mid = y + 1 + cellH / 2;
            Widgets.StepButton(c, "wake-inc", new RectangleF(right - 28, mid - 14, 28, 28), true, 6, () => ctl.SetWake(ctl.Settings.Wake + 0.25));
            Widgets.StepButton(c, "wake-dec", new RectangleF(right - 60, mid - 14, 28, 28), false, 6, () => ctl.SetWake(ctl.Settings.Wake - 0.25));
            return cellH + 2;
        }

        private float PaintFooter(Canvas c, float y)
        {
            Palette p = c.P;
            Font f = Fonts.Sans(12), bold = Fonts.Sans(12, 500);
            float rowH = c.Normal(f) + 8, baseline = c.Baseline(f, y, rowH);

            // Colors: <preset> ›
            string preset = ctl.Schedule.PresetName();
            float arrowW = c.Measure(" ›", f), presetW = c.Measure(preset, bold), prefixW = c.Measure("Colors: ", f);
            float cx = X + W - arrowW - presetW - prefixW;
            Color muted = c.IsHover("colors") ? p.Ink : p.Muted;
            c.Text("Colors: ", f, muted, cx, baseline);
            c.Text(preset, bold, p.Ink, cx + prefixW, baseline);
            c.Text(" ›", f, muted, cx + prefixW + presetW, baseline);
            c.Click("colors", new RectangleF(cx - 4, y, prefixW + presetW + arrowW + 8, rowH), () => SetScreen(MainScreen.Colors));

            // Place, or a warning when something is getting in Dusk's way
            string warning = ctl.Warning;
            float maxW = cx - X - 24;
            if (warning != null)
            {
                c.Text(Fit(c, warning, f, maxW), f, p.Danger, X, baseline);
                if (ctl.RangeBlocked) c.Click("unlock", new RectangleF(X, y, maxW, rowH), ctl.UnlockRange);
            }
            else
            {
                Settings s = ctl.Settings;
                string where = s.HasLocation ? Geocoder.FormatCoordinates(s.Latitude, s.Longitude) : "Set your location";
                if (s.Place.Length > 0) where = s.Place + " · " + where;
                float ww = c.Text(Fit(c, where, f, maxW), f, c.IsHover("place-link") ? p.Ink : p.Muted, X, baseline);
                c.Click("place-link", new RectangleF(X - 4, y, ww + 8, rowH), () => SetScreen(MainScreen.FirstRun));
            }
            return rowH;
        }

        private static string Fit(Canvas c, string s, Font f, float width)
        {
            if (c.Measure(s, f) <= width) return s;
            while (s.Length > 1 && c.Measure(s + "…", f) > width) s = s.Substring(0, s.Length - 1);
            return s.TrimEnd() + "…";
        }
    }
}
