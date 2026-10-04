using System;
using System.Drawing;

namespace Dusk.Ui
{
    // The flyout above the tray icon: current status, the colour for this part of the day, and quick "disable" options.
    public sealed class TrayFlyout : Surface
    {
        private readonly Controller ctl;

        public TrayFlyout(Controller controller) : base(336)
        {
            ctl = controller;
        }

        protected override bool AnchorBottom { get { return true; } }

        protected override Native.POINT Place(Native.RECT work, int width, int height, bool firstShow)
        {
            return new Native.POINT { X = work.Right - width - Px(12), Y = work.Bottom - height - Px(10) };
        }

        protected override void OnDeactivated()
        {
            if (!IsVisible) return;
            Hide(true);
            ctl.FlyoutClosed();
        }

        protected override void OnHidden()
        {
            ctl.WindowHidden(this);
        }

        protected override float PaintContent(Canvas c)
        {
            Palette p = c.P;
            DayTimes t = ctl.Times;
            double hour = ctl.NowHour;
            bool off = ctl.IsOff;
            Period period = ctl.Schedule.PeriodAt(t, hour);
            int kNow = (int)(Math.Round(ctl.Schedule.KelvinAt(t, hour) / 10) * 10);
            Color tint = Palette.Kelvin(off ? ColorTemp.Neutral : ctl.IsDarkroom ? ColorTemp.Min : kNow);
            const float x = 16, w = 304;
            float y = 16;

            // Status
            Font status = Fonts.Sans(14, 600), time = Fonts.Sans(13);
            float lh = c.Normal(status);
            c.Circle(x + 5, y + lh / 2, 8, Palette.Alpha(tint, 0.45));
            c.Circle(x + 5, y + lh / 2, 5, tint);
            c.CircleBorder(x + 5, y + lh / 2, 5, p.Line, 1); // keeps a white daytime dot visible
            string title = off ? "Dusk is off" : ctl.IsDarkroom ? "Darkroom" : Schedule.NameOf(period) + " · " + kNow + " K";
            c.Text(title, status, p.Ink, x + 20, c.Baseline(status, y, lh));
            c.Text(Schedule.FormatHour(hour), time, p.Muted, x + w, c.Baseline(status, y, lh), Align.Right);
            y += lh + 4;

            Font small = Fonts.Sans(12);
            string warning = ctl.Warning;
            string note = warning ?? (off ? ctl.OffNote
                : ctl.IsDarkroom ? "Red only, inverted · Alt+End to leave"
                : ctl.Schedule.SolarLine(t, hour));
            y += c.Paragraph(note, small, warning != null ? p.Danger : p.Muted, x, y, w, c.Normal(small));

            // Colour for this part of the day
            y += 12;
            Tone tone = Schedule.ToneOf(period);
            int kelvin = ctl.Settings.GetTemp(tone);
            Widgets.Slider(c, "fly-slider", x, y, w, 26, 9, 3, 20, kelvin, p.Knob, false, k => ctl.SetTemp(tone, k));
            y += 26;
            Font tiny = Fonts.Sans(11);
            float tinyLh = c.Normal(tiny);
            c.Text(Schedule.NameOf(tone) + " color", tiny, p.Muted, x, c.Baseline(tiny, y, tinyLh));
            c.Text(Widgets.KelvinLabel(kelvin), tiny, p.Muted, x + w, c.Baseline(tiny, y, tinyLh), Align.Right);
            y += tinyLh;

            // The warmest Dusk goes, past the end of the slider
            y = Option(c, "darkroom", "Darkroom", "Red only, inverted", ctl.IsDarkroom, y + 12, ctl.ToggleDarkroom);

            // Disable
            y += 16;
            c.Label("Disable", 11, p.Muted, x, y, tinyLh, 0.08f);
            y += tinyLh + 8;
            string kind = ctl.DisabledKind;
            y = Option(c, "dis-hour", "For an hour", "Until " + Schedule.FormatHour(hour + 1), kind == "hour", y, () => ctl.DisableFor("hour"));
            y = Option(c, "dis-sunrise", "Until sunrise", Schedule.FormatHour(t.Sunrise), kind == "sunrise", y + 6, () => ctl.DisableFor("sunrise"));
            string app = ctl.LastAppName != null ? ctl.LastAppName + " · while in front" : "While it is in front";
            y = Option(c, "dis-app", "For this app", app, ctl.AppOff, y + 6, () => ctl.DisableFor("app"));

            // Footer
            y += 14;
            c.Line(0, y + 0.5f, 336, y + 0.5f, p.Line2, 1);
            y += 1 + 12;
            Font open = Fonts.Sans(13, 500), quit = Fonts.Sans(13);
            float footLh = c.Normal(open);
            float openW = c.Text("Open Dusk", open, p.Ink, x, c.Baseline(open, y, footLh));
            c.Click("open", new RectangleF(x - 4, y - 4, openW + 8, footLh + 8), ctl.OpenMain);
            float quitW = c.Text("Quit", quit, c.IsHover("quit") ? p.Ink : p.Muted, x + w, c.Baseline(quit, y, footLh), Align.Right);
            c.Click("quit", new RectangleF(x + w - quitW - 4, y - 4, quitW + 8, footLh + 8), ctl.Quit);
            y += footLh + 16;
            return y;
        }

        private float Option(Canvas c, string id, string label, string note, bool on, float y, Action click)
        {
            Palette p = c.P;
            var r = new RectangleF(16, y, 304, 36);
            Color fg = on ? p.BtnFg : p.Ink;
            if (on) c.Fill(r, p.Btn, 8);
            else if (c.IsHover(id)) c.Fill(r, p.Surface2, 8);
            c.Border(r, on ? p.Btn : p.Line, 1, 8);
            Font f = Fonts.Sans(13), n = Fonts.Sans(12);
            c.Text(label, f, fg, r.X + 12, c.Baseline(f, y, 36));
            c.Text(note, n, Palette.Alpha(fg, 0.7), r.Right - 12, c.Baseline(f, y, 36), Align.Right);
            c.Click(id, r, click);
            return y + 36;
        }
    }
}
