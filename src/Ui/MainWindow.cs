using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Dusk.Ui
{
    public enum MainScreen { FirstRun, Main, Colors }

    // The Dusk window: a custom title bar over three screens (first run, main, colours).
    public sealed partial class MainWindow : Surface
    {
        public const float WindowWidth = 880;
        private const float TitleHeight = 32;
        private static readonly Color CloseHover = Palette.Hex("#C42B1C");

        private readonly Controller ctl;
        private MainScreen screen = MainScreen.Main;

        public MainWindow(Controller controller) : base(WindowWidth)
        {
            ctl = controller;
        }

        public MainScreen Screen { get { return screen; } }

        public void SetScreen(MainScreen next)
        {
            if (next == MainScreen.FirstRun && screen != MainScreen.FirstRun) ResetPlaceInput();
            screen = next;
            AutoHide = next != MainScreen.FirstRun; // setup waits for you
            if (next == MainScreen.FirstRun && IsVisible) StartCaret();
            Invalidate();
        }

        protected override float PaintContent(Canvas c)
        {
            PaintGlow(c);
            float bottom;
            switch (screen)
            {
                case MainScreen.FirstRun: bottom = PaintFirstRun(c, TitleHeight); break;
                case MainScreen.Colors: bottom = PaintColors(c, TitleHeight); break;
                default: bottom = PaintMain(c, TitleHeight); break;
            }
            PaintTitleBar(c);
            return bottom;
        }

        // The current screen colour, glowing softly up from the bottom edge.
        private void PaintGlow(Canvas c)
        {
            // The design: an ellipse from 15% to 85% wide, 260px tall, 150px below the bottom edge, blur(70px), 50% opacity.
            Color tint = Palette.Kelvin(ctl.IsOff ? ColorTemp.Neutral : ctl.IsDarkroom ? ColorTemp.Min : ctl.DisplayKelvin);
            var ellipse = new RectangleF(WindowWidth * 0.15f, DesignHeight + 150 - 260, WindowWidth * 0.7f, 260);
            c.BlurredEllipse(ellipse, 70, Palette.Alpha(tint, 0.5));
        }

        private void PaintTitleBar(Canvas c)
        {
            Palette p = c.P;
            float buttonsLeft = WindowWidth - 46 * 3;
            c.Area("caption", new RectangleF(0, 0, buttonsLeft, TitleHeight)).Caption = true;

            Widgets.Icon(c, 12, 8, 16);
            Font f = Fonts.Sans(12);
            c.Text("Dusk", f, p.Ink, 36, c.Baseline(f, 0, TitleHeight));

            // Minimize (hides to the tray)
            var min = new RectangleF(buttonsLeft, 0, 46, TitleHeight);
            if (c.IsHover("min")) c.Fill(min, p.Surface2, 0);
            c.Line(min.X + 18, 16.5f, min.X + 28, 16.5f, p.Ink, 1);
            c.Click("min", min, () => Hide(true));

            // Maximize (the window has a fixed size, so this stays dimmed)
            var max = new RectangleF(buttonsLeft + 46, 0, 46, TitleHeight);
            using (var pen = new Pen(Palette.Alpha(p.Ink, 0.35), 1))
            using (GraphicsPath box = Canvas.Round(new RectangleF(max.X + 18.5f, 11.5f, 9, 9), 1.5f))
                c.G.DrawPath(pen, box);

            // Close (hides to the tray; quit from the tray)
            var close = new RectangleF(buttonsLeft + 92, 0, 46, TitleHeight);
            bool hot = c.IsHover("close");
            if (hot) c.Fill(close, CloseHover, 0);
            Color glyph = hot ? Color.White : p.Ink;
            c.Line(close.X + 18.5f, 11.5f, close.X + 27.5f, 20.5f, glyph, 1);
            c.Line(close.X + 27.5f, 11.5f, close.X + 18.5f, 20.5f, glyph, 1);
            c.Click("close", close, () => Hide(true));
        }

        protected override Native.POINT Place(Native.RECT work, int width, int height, bool firstShow)
        {
            return new Native.POINT
            {
                X = work.Left + (work.Right - work.Left - width) / 2,
                Y = Math.Max(work.Top, work.Top + (work.Bottom - work.Top - height) / 2)
            };
        }

        protected override bool KeepOpen()
        {
            return ctl.Playing || lookingUp;
        }

        protected override void OnShown()
        {
            if (screen == MainScreen.FirstRun) StartCaret();
        }

        protected override void OnHidden()
        {
            Native.KillTimer(Handle, (IntPtr)TimerCustom);
            inputFocused = false;
            if (screen == MainScreen.Colors) screen = MainScreen.Main;
            ctl.WindowHidden(this);
        }

        protected override bool OnKey(int vk)
        {
            if (vk == 0x1B && screen == MainScreen.Colors)
            {
                SetScreen(MainScreen.Main);
                return true;
            }
            if (inputFocused && selectAll && (vk == 0x25 || vk == 0x27 || vk == 0x23 || vk == 0x24)) // arrows, Home, End
            {
                selectAll = false;
                Invalidate();
                return true;
            }
            return false;
        }

        // ---- First run ----

        private string placeText = "", committedPlace = "", placeError;
        private bool inputFocused, caretOn, lookingUp, selectAll;
        private int lookupId;

        private void ResetPlaceInput()
        {
            Settings s = ctl.Settings;
            placeText = s.Place.Length > 0 ? s.Place
                : s.HasLocation ? s.Latitude.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ", "
                    + s.Longitude.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                : "";
            committedPlace = s.HasLocation ? placeText : "";
            placeError = null;
            lookingUp = false;
        }

        private void StartCaret()
        {
            caretOn = true;
            Native.SetTimer(Handle, (IntPtr)TimerCustom, 530, IntPtr.Zero);
        }

        protected override void OnCustomTimer()
        {
            caretOn = !caretOn;
            if (inputFocused) Invalidate();
        }

        protected override void OnMouseDown(Hit hit)
        {
            bool focus = screen == MainScreen.FirstRun && hit != null && hit.Id == "place";
            if (inputFocused && !focus && placeText.Trim() != committedPlace) SubmitPlace(false);
            if (focus && !inputFocused) selectAll = placeText.Length > 0; // typing replaces what's there
            inputFocused = focus;
            caretOn = true;
            if (App.Tracing) App.Trace("MainWindow focus " + focus + " screen " + screen);
        }

        protected override void OnChar(char ch)
        {
            if (screen != MainScreen.FirstRun || !inputFocused) return;
            if (ch == (char)0x01)
            {
                selectAll = placeText.Length > 0; // Ctrl+A
                Invalidate();
                return;
            }
            if (ch == '\r')
            {
                selectAll = false;
                SubmitPlace(false);
                return;
            }
            if (ch == '\b')
            {
                if (selectAll) placeText = "";
                else if (placeText.Length > 0) placeText = placeText.Substring(0, placeText.Length - 1);
            }
            else if (ch == (char)0x7F)
            {
                placeText = ""; // Ctrl+Backspace
            }
            else if (ch == (char)0x16)
            {
                string pasted = Clipboard().Replace("\r", " ").Replace("\n", " ");
                placeText = selectAll ? pasted : placeText + pasted;
            }
            else if (ch >= ' ')
            {
                placeText = selectAll ? ch.ToString() : placeText + ch;
            }
            else
            {
                return;
            }
            selectAll = false;
            if (placeText.Length > 80) placeText = placeText.Substring(0, 80);
            placeError = null;
            caretOn = true;
            if (App.Tracing) App.Trace("MainWindow text now '" + placeText + "'");
            Invalidate();
        }

        private string Clipboard()
        {
            if (!Native.OpenClipboard(Handle)) return "";
            try
            {
                IntPtr data = Native.GetClipboardData(Native.CF_UNICODETEXT);
                if (data == IntPtr.Zero) return "";
                IntPtr text = Native.GlobalLock(data);
                try
                {
                    return text == IntPtr.Zero ? "" : Marshal.PtrToStringUni(text) ?? "";
                }
                finally
                {
                    Native.GlobalUnlock(data);
                }
            }
            finally
            {
                Native.CloseClipboard();
            }
        }

        private void SubmitPlace(bool startAfter)
        {
            string text = placeText.Trim();
            if (text.Length == 0 || (text == committedPlace && ctl.Settings.HasLocation))
            {
                if (!startAfter) return;
                if (ctl.Settings.HasLocation && text.Length > 0) FinishSetup();
                else placeError = "Type your city, or coordinates like 42.2, -83.2";
                Invalidate();
                return;
            }

            double lat, lon;
            if (Geocoder.TryParseCoordinates(text, out lat, out lon))
            {
                ctl.SetPlace(new PlaceResult { Name = "", Latitude = lat, Longitude = lon });
                committedPlace = text;
                placeError = null;
                if (startAfter) FinishSetup();
                Invalidate();
                return;
            }

            int id = ++lookupId;
            lookingUp = true;
            placeError = null;
            Invalidate();
            Geocoder.LookUp(text, result => App.Post(() =>
            {
                if (id != lookupId) return;
                lookingUp = false;
                if (result == null)
                {
                    placeError = "Couldn't find that place. Check the spelling, or type coordinates like 42.2, -83.2";
                }
                else
                {
                    ctl.SetPlace(result);
                    placeText = committedPlace = result.Name;
                    if (startAfter) FinishSetup();
                }
                Invalidate();
            }));
        }

        private void FinishSetup()
        {
            ctl.FinishSetup();
            inputFocused = false;
            Native.KillTimer(Handle, (IntPtr)TimerCustom);
            SetScreen(MainScreen.Main);
            Touch();
        }

        private float PaintFirstRun(Canvas c, float y)
        {
            Palette p = c.P;
            DayTimes t = ctl.Times;
            const float x = 160, w = 560;
            y += 26;

            Widgets.Icon(c, x, y, 36);
            c.Label("Welcome to Dusk", 12, p.Muted, x + 46, y, 36, 0.08f);
            y += 36 + 16;

            y += c.Paragraph("Two details, then Dusk runs on its own.", Fonts.Serif(40), p.Ink, x, y, w, 42);
            y += 14;
            y += c.Paragraph("Dusk warms your screen after sunset and again before bed, then brings it back by morning. "
                + "It needs to know where the sun is and when your day starts.", Fonts.Sans(14), p.Muted, x, y, w, 21);

            // Where are you?
            y += 26;
            Font label = Fonts.Sans(14, 500);
            float labelLh = c.Normal(label);
            c.Text("Where are you?", label, p.Ink, x, c.Baseline(label, y, labelLh));
            y += labelLh + 8;

            var box = new RectangleF(x, y, w, 44);
            c.Fill(box, p.Surface, 8);
            c.Border(box, inputFocused ? p.Muted : p.Line, 1, 8);
            Font input = Fonts.Sans(15);
            float inputBase = c.Baseline(input, y, 44);
            float textW = 0;
            if (placeText.Length > 0)
            {
                if (inputFocused && selectAll)
                    c.Fill(new RectangleF(x + 12, y + 11, c.Measure(placeText, input) + 4, 22), Palette.Alpha(p.Ink, 0.14), 3);
                textW = c.Text(placeText, input, p.Ink, x + 14, inputBase);
            }
            else if (!inputFocused)
            {
                c.Text("City, or coordinates like 42.2, -83.2", input, p.Muted, x + 14, inputBase);
            }
            if (inputFocused && caretOn && !selectAll) c.Line(x + 14 + textW + 1, y + 12, x + 14 + textW + 1, y + 32, p.Ink, 1);
            c.Area("place", box).Cursor = Native.IDC_IBEAM;
            y += 44 + 8;

            Font meta = Fonts.Sans(13);
            float metaLh = c.Normal(meta);
            if (placeError != null)
            {
                y += c.Paragraph(placeError, meta, p.Danger, x, y, w, metaLh);
            }
            else
            {
                float mx = x, mb = c.Baseline(meta, y, metaLh);
                string where = lookingUp ? "Looking up…"
                    : ctl.Settings.HasLocation ? Geocoder.FormatCoordinates(ctl.Settings.Latitude, ctl.Settings.Longitude)
                    : "Press Enter to look it up";
                mx += c.Text(where, meta, p.Muted, mx, mb) + 18;
                if (ctl.Settings.HasLocation && !lookingUp)
                {
                    mx += c.Text("Sunrise " + Schedule.FormatHour(t.Sunrise), meta, p.Muted, mx, mb) + 18;
                    c.Text("Sunset " + Schedule.FormatHour(t.Sunset), meta, p.Muted, mx, mb);
                }
                y += metaLh;
            }

            // When do you usually wake up?
            y += 24;
            Font serif = Fonts.Serif(34);
            string wake = Schedule.FormatHour(t.Wake);
            float timeW = Math.Max(128, c.Measure(wake, serif));
            float groupW = 32 + 8 + timeW + 8 + 32;
            float leftW = w - groupW - 20;
            float titleLh = c.Normal(label), noteLh = c.Normal(meta);
            string note = "Bedtime colors begin at " + Schedule.FormatHour(t.Bed) + ", "
                + ctl.Settings.SleepHours.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " hours before you wake.";
            int noteLines = c.Wrap(note, meta, leftW).Count;
            float leftH = titleLh + 4 + noteLines * noteLh;
            float rowH = Math.Max(leftH, 44);
            float leftTop = y + (rowH - leftH) / 2;
            c.Text("When do you usually wake up?", label, p.Ink, x, c.Baseline(label, leftTop, titleLh));
            c.Paragraph(note, meta, p.Muted, x, leftTop + titleLh + 4, leftW, noteLh);

            float gx = x + w - groupW, cy = y + rowH / 2;
            Widgets.StepButton(c, "fr-wake-dec", new RectangleF(gx, cy - 16, 32, 32), false, 8, () => ctl.SetWake(ctl.Settings.Wake - 0.25));
            c.Text(wake, serif, p.Ink, gx + 40 + timeW / 2, c.Baseline(serif, cy - 22, 44), Align.Center);
            Widgets.StepButton(c, "fr-wake-inc", new RectangleF(gx + 48 + timeW, cy - 16, 32, 32), true, 8, () => ctl.SetWake(ctl.Settings.Wake + 0.25));
            y += rowH;

            // The day's colours
            y += 24;
            var bar = new RectangleF(x, y, w, 14);
            using (LinearGradientBrush brush = Widgets.DayGradient(ctl, bar)) c.Fill(bar, brush, 7);
            c.Border(bar, p.Line2, 1, 7);
            y += 14 + 6;
            y += Widgets.HourLabels(c, x, y, w);

            // Start Dusk
            y += 26;
            var start = new RectangleF(x, y, w, 46);
            c.Shadow(start, 10, 8, 20, -10, Color.FromArgb(178, 232, 90, 43));
            Color top = c.IsHover("start") ? Palette.Hex("#FAC286") : Palette.Hex("#F8B672");
            using (LinearGradientBrush brush = Canvas.Gradient(start, new[] { top, Palette.Hex("#E85A2B") }, new[] { 0f, 1f }, true))
                c.Fill(start, brush, 10);
            Font startFont = Fonts.Sans(15, 600);
            c.Text(lookingUp ? "Looking up…" : "Start Dusk", startFont, Palette.OnColor, x + w / 2, c.Baseline(startFont, y, 46), Align.Center);
            c.Click("start", start, () => SubmitPlace(true));
            y += 46 + 40;
            return y;
        }
    }
}
