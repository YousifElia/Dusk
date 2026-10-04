using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace Dusk.Ui
{
    // A borderless, custom-drawn window: paints through Canvas into a back buffer, handles the mouse,
    // scales for DPI, fades in and out, and hides itself after 10 seconds without interaction.
    // Hovering counts as interaction once the mouse has actually moved over the window.
    public abstract class Surface
    {
        private const string ClassName = "DuskSurface";
        private const uint WM_EXITSIZEMOVE = 0x232;
        private const int TimerAutoHide = 1, TimerFade = 2;
        protected const int TimerCustom = 3;
        public const double HideAfterSeconds = 10;

        private static Native.WndProcDelegate procDelegate;
        private static readonly Dictionary<IntPtr, Surface> windows = new Dictionary<IntPtr, Surface>();
        private static Surface creating;

        private readonly List<Hit> hits = new List<Hit>();
        private Bitmap buffer;
        private bool dirty = true;
        private string hoverId, pressedId;
        private Hit dragHit;
        private int cursorId = Native.IDC_ARROW;
        private bool trackingLeave, hoverArmed, userMoved;
        private DateTime lastTouch;
        private Native.POINT lastCursor;
        private int alpha = 255, fadeDirection;

        protected float Scale = 1;
        protected readonly float DesignWidth;
        protected float DesignHeight = 400;

        public IntPtr Handle { get; private set; }
        public bool IsVisible { get; private set; }
        public Palette Palette = Palette.Light;
        public bool AutoHide = true;

        protected Surface(float designWidth)
        {
            DesignWidth = designWidth;
        }

        // Paints everything and returns the content height in design pixels.
        protected abstract float PaintContent(Canvas c);

        // Where to put the window on a monitor's work area (physical pixels).
        protected abstract Native.POINT Place(Native.RECT work, int width, int height, bool firstShow);

        protected virtual bool OnKey(int vk) { return false; }
        protected virtual void OnChar(char ch) { }
        protected virtual void OnDeactivated() { }
        protected virtual void OnShown() { }
        protected virtual void OnHidden() { }
        protected virtual void OnCustomTimer() { }
        protected virtual void OnMouseDown(Hit hit) { }
        protected virtual bool KeepOpen() { return false; }
        protected virtual bool AnchorBottom { get { return false; } }

        // ---- Showing and hiding ----

        public void Show()
        {
            EnsureHandle();
            bool appearing = !IsVisible;
            IsVisible = true;
            DesignHeight = MeasureHeight();

            Native.RECT r;
            Native.GetWindowRect(Handle, out r);
            bool place = appearing && !(userMoved && fadeDirection == 0 && OnAnyMonitor(r));
            for (int attempt = 0; attempt < 2; attempt++)
            {
                float scaleBefore = Scale;
                int w = Px(DesignWidth), h = Px(DesignHeight);
                if (place)
                {
                    Native.POINT at = Place(WorkAreaAtCursor(), w, h, !userMoved);
                    r.Left = at.X;
                    r.Top = at.Y;
                }
                Native.SetWindowPos(Handle, Native.HWND_TOPMOST, r.Left, r.Top, w, h, Native.SWP_NOACTIVATE);
                // Landing on a monitor with a different scale resizes the window (WM_DPICHANGED); place it again at the new size.
                if (Scale == scaleBefore) break;
                Native.GetWindowRect(Handle, out r);
            }

            if (appearing)
            {
                if (fadeDirection == 0)
                {
                    SetLayered(true);
                    alpha = 0;
                    Native.SetLayeredWindowAttributes(Handle, 0, 0, Native.LWA_ALPHA);
                    Native.ShowWindow(Handle, Native.SW_SHOW);
                }
                fadeDirection = 1;
                Native.SetTimer(Handle, (IntPtr)TimerFade, 16, IntPtr.Zero);
            }
            Native.SetForegroundWindow(Handle);
            Touch();
            Native.GetCursorPos(out lastCursor);
            hoverArmed = false;
            Native.SetTimer(Handle, (IntPtr)TimerAutoHide, 200, IntPtr.Zero);
            Invalidate();
            if (appearing) OnShown();
        }

        public void Hide(bool animate)
        {
            if (!IsVisible) return;
            IsVisible = false;
            Native.KillTimer(Handle, (IntPtr)TimerAutoHide);
            EndDrag();
            if (!animate)
            {
                FinishHide();
                return;
            }
            SetLayered(true);
            Native.SetLayeredWindowAttributes(Handle, 0, (byte)alpha, Native.LWA_ALPHA);
            fadeDirection = -1;
            Native.SetTimer(Handle, (IntPtr)TimerFade, 16, IntPtr.Zero);
        }

        private void FinishHide()
        {
            Native.KillTimer(Handle, (IntPtr)TimerFade);
            fadeDirection = 0;
            Native.ShowWindow(Handle, Native.SW_HIDE);
            SetLayered(false);
            alpha = 255;
            hoverId = pressedId = null;
            if (buffer != null)
            {
                buffer.Dispose();
                buffer = null;
            }
            OnHidden();
        }

        private void FadeStep()
        {
            alpha = Math.Max(0, Math.Min(255, alpha + fadeDirection * 36));
            Native.SetLayeredWindowAttributes(Handle, 0, (byte)alpha, Native.LWA_ALPHA);
            if (fadeDirection > 0 && alpha >= 255)
            {
                Native.KillTimer(Handle, (IntPtr)TimerFade);
                fadeDirection = 0;
                SetLayered(false);
                Invalidate();
            }
            else if (fadeDirection < 0 && alpha <= 0)
            {
                FinishHide();
            }
        }

        private void SetLayered(bool on)
        {
            int ex = Native.GetWindowLong(Handle, Native.GWL_EXSTYLE);
            int next = on ? ex | Native.WS_EX_LAYERED : ex & ~Native.WS_EX_LAYERED;
            if (next != ex) Native.SetWindowLong(Handle, Native.GWL_EXSTYLE, next);
        }

        public void Touch()
        {
            lastTouch = DateTime.Now;
        }

        private void CheckAutoHide()
        {
            if (!IsVisible) return;
            if (!AutoHide || KeepOpen() || dragHit != null || Hovered())
            {
                Touch();
                return;
            }
            if ((DateTime.Now - lastTouch).TotalSeconds >= HideAfterSeconds) Hide(true);
        }

        private bool Hovered()
        {
            Native.POINT p;
            if (!Native.GetCursorPos(out p)) return false;
            bool moved = p.X != lastCursor.X || p.Y != lastCursor.Y;
            lastCursor = p;
            Native.RECT r;
            bool inside = Native.GetWindowRect(Handle, out r) && p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom;
            if (!inside) hoverArmed = false;
            else if (moved) hoverArmed = true;
            return inside && hoverArmed;
        }

        // ---- Painting ----

        public void Invalidate()
        {
            dirty = true;
            if (Handle != IntPtr.Zero && IsVisible) Native.InvalidateRect(Handle, IntPtr.Zero, false);
        }

        protected int Px(float design)
        {
            return (int)Math.Ceiling(design * Scale);
        }

        private static void Setup(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            // Blend in sRGB like a browser does; GDI+'s gamma-corrected mode bands faint colours on dark backgrounds.
            g.CompositingQuality = CompositingQuality.AssumeLinear;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        }

        private float MeasureHeight()
        {
            using (var bmp = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                Setup(g);
                return PaintContent(new Canvas(g, Palette, new List<Hit>(), null, null));
            }
        }

        private void Render()
        {
            int w = Px(DesignWidth), h = Px(DesignHeight);
            if (buffer == null || buffer.Width != w || buffer.Height != h)
            {
                if (buffer != null) buffer.Dispose();
                buffer = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            }
            float used;
            using (Graphics g = Graphics.FromImage(buffer))
            {
                Setup(g);
                g.Clear(Palette.Bg);
                g.ScaleTransform(Scale, Scale);
                hits.Clear();
                used = PaintContent(new Canvas(g, Palette, hits, hoverId, pressedId));
            }
            dirty = false;
            if (Math.Abs(used - DesignHeight) > 0.5f) Resize(used);
        }

        private void Resize(float height)
        {
            DesignHeight = height;
            dirty = true;
            if (Handle == IntPtr.Zero) return;
            Native.RECT r;
            Native.GetWindowRect(Handle, out r);
            int h = Px(height);
            int top = AnchorBottom ? r.Bottom - h : r.Top;
            // Growing (first run → main screen) mustn't push the window past the taskbar.
            Native.RECT work = WorkAreaAt(new Native.POINT { X = (r.Left + r.Right) / 2, Y = (r.Top + r.Bottom) / 2 });
            if (top + h > work.Bottom) top = work.Bottom - h;
            if (top < work.Top) top = work.Top;
            Native.SetWindowPos(Handle, IntPtr.Zero, r.Left, top, Px(DesignWidth), h, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            Native.InvalidateRect(Handle, IntPtr.Zero, false);
        }

        public void SaveSnapshot(string path, float scale)
        {
            Scale = scale;
            DesignHeight = MeasureHeight();
            Render();
            if (dirty) Render();
            buffer.Save(path, ImageFormat.Png);
        }

        private void Paint()
        {
            Native.PAINTSTRUCT ps;
            IntPtr hdc = Native.BeginPaint(Handle, out ps);
            try
            {
                if (dirty || buffer == null) Render();
                if (buffer == null) return;
                using (Graphics g = Graphics.FromHdc(hdc))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.DrawImage(buffer, new Rectangle(0, 0, buffer.Width, buffer.Height), 0, 0, buffer.Width, buffer.Height, GraphicsUnit.Pixel);
                }
            }
            finally
            {
                Native.EndPaint(Handle, ref ps);
            }
        }

        // ---- Mouse ----

        private Hit HitAt(PointF p)
        {
            for (int i = hits.Count - 1; i >= 0; i--)
                if (hits[i].Rect.Contains(p)) return hits[i];
            return null;
        }

        private PointF ToDesign(IntPtr lParam)
        {
            return new PointF(LowWord(lParam) / Scale, HighWord(lParam) / Scale);
        }

        private void EndDrag()
        {
            if (dragHit == null) return;
            Hit d = dragHit;
            dragHit = null;
            Native.ReleaseCapture();
            if (d.DragEnd != null) d.DragEnd();
        }

        private void MouseMove(PointF p)
        {
            if (!trackingLeave)
            {
                var tme = new Native.TRACKMOUSEEVENT { cbSize = Marshal.SizeOf(typeof(Native.TRACKMOUSEEVENT)), dwFlags = Native.TME_LEAVE, hwndTrack = Handle };
                trackingLeave = Native.TrackMouseEvent(ref tme);
            }
            if (dragHit != null)
            {
                Touch();
                dragHit.Drag(p);
                return;
            }
            Hit h = HitAt(p);
            int cursor = h == null || h.Caption ? Native.IDC_ARROW : h.Cursor;
            if (cursor != cursorId)
            {
                cursorId = cursor;
                Native.SetCursor(Native.LoadCursor(IntPtr.Zero, cursor));
            }
            string id = h == null ? null : h.Id;
            if (id != hoverId)
            {
                hoverId = id;
                Invalidate();
            }
        }

        private void MouseDown(PointF p)
        {
            Touch();
            Hit h = HitAt(p);
            pressedId = h == null ? null : h.Id;
            OnMouseDown(h);
            if (h != null)
            {
                Native.SetCapture(Handle);
                if (h.Drag != null)
                {
                    dragHit = h;
                    h.Drag(p);
                }
            }
            Invalidate();
        }

        private void MouseUp(PointF p)
        {
            Touch();
            if (dragHit != null)
            {
                EndDrag();
            }
            else
            {
                string pressed = pressedId;
                pressedId = null;
                Native.ReleaseCapture();
                Hit h = HitAt(p);
                if (h != null && h.Id == pressed && h.Click != null) h.Click();
            }
            pressedId = null;
            Invalidate();
        }

        // ---- Window plumbing ----

        private void EnsureHandle()
        {
            if (Handle != IntPtr.Zero) return;
            IntPtr instance = Native.GetModuleHandle(null);
            if (procDelegate == null)
            {
                procDelegate = StaticWndProc;
                var wc = new Native.WNDCLASSEX
                {
                    cbSize = Marshal.SizeOf(typeof(Native.WNDCLASSEX)),
                    style = Native.CS_DROPSHADOW,
                    lpfnWndProc = procDelegate,
                    hInstance = instance,
                    hCursor = Native.LoadCursor(IntPtr.Zero, Native.IDC_ARROW),
                    lpszClassName = ClassName
                };
                Native.RegisterClassEx(ref wc);
            }
            creating = this;
            Handle = Native.CreateWindowEx(Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST, ClassName, "Dusk", Native.WS_POPUP,
                0, 0, 100, 100, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
            creating = null;
            if (Handle == IntPtr.Zero)
            {
                App.Log(new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Couldn't create a Dusk window"));
                return;
            }
            windows[Handle] = this;

            int round = Native.DWMWCP_ROUND;
            Native.DwmSetWindowAttribute(Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, 4);
            ApplyBorder();
            try
            {
                uint dpi = Native.GetDpiForWindow(Handle);
                if (dpi > 0) Scale = dpi / 96f;
            }
            catch (EntryPointNotFoundException)
            {
            }
        }

        public void SetPalette(Palette palette)
        {
            Palette = palette;
            if (Handle != IntPtr.Zero) ApplyBorder();
            Invalidate();
        }

        private void ApplyBorder()
        {
            int colorRef = Palette.Border.R | (Palette.Border.G << 8) | (Palette.Border.B << 16);
            Native.DwmSetWindowAttribute(Handle, Native.DWMWA_BORDER_COLOR, ref colorRef, 4);
        }

        // For testing: open windows on the monitor containing this screen point instead of the cursor's.
        public static Native.POINT? PlacementPoint;

        private static Native.RECT WorkAreaAtCursor()
        {
            Native.POINT p;
            if (PlacementPoint.HasValue) p = PlacementPoint.Value;
            else Native.GetCursorPos(out p);
            return WorkAreaAt(p);
        }

        private static Native.RECT WorkAreaAt(Native.POINT p)
        {
            var info = new Native.MONITORINFO { cbSize = Marshal.SizeOf(typeof(Native.MONITORINFO)) };
            Native.GetMonitorInfo(Native.MonitorFromPoint(p, Native.MONITOR_DEFAULTTONEAREST), ref info);
            return info.rcWork;
        }

        private static bool OnAnyMonitor(Native.RECT r)
        {
            var center = new Native.POINT { X = (r.Left + r.Right) / 2, Y = r.Top + 16 };
            var info = new Native.MONITORINFO { cbSize = Marshal.SizeOf(typeof(Native.MONITORINFO)) };
            if (!Native.GetMonitorInfo(Native.MonitorFromPoint(center, Native.MONITOR_DEFAULTTONEAREST), ref info)) return false;
            return center.X >= info.rcWork.Left && center.X < info.rcWork.Right && center.Y >= info.rcWork.Top && center.Y < info.rcWork.Bottom;
        }

        private static int LowWord(IntPtr v) { return (short)(v.ToInt32() & 0xFFFF); }
        private static int HighWord(IntPtr v) { return (short)((v.ToInt32() >> 16) & 0xFFFF); }

        private static IntPtr StaticWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            Surface s;
            if (!windows.TryGetValue(hwnd, out s)) s = creating;
            if (s == null) return Native.DefWindowProc(hwnd, msg, wParam, lParam);
            if (s.Handle == IntPtr.Zero) s.Handle = hwnd;
            try
            {
                return s.WndProc(hwnd, msg, wParam, lParam);
            }
            catch (Exception ex)
            {
                App.Log(ex);
                return Native.DefWindowProc(hwnd, msg, wParam, lParam);
            }
        }

        private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            switch (msg)
            {
                case Native.WM_PAINT:
                    if (App.Tracing) App.Trace(GetType().Name + " paint dirty " + dirty + " visible " + IsVisible);
                    Paint();
                    return IntPtr.Zero;
                case Native.WM_ERASEBKGND:
                    return (IntPtr)1;
                case Native.WM_NCHITTEST:
                {
                    var pt = new Native.POINT { X = LowWord(lParam), Y = HighWord(lParam) };
                    Native.ScreenToClient(hwnd, ref pt);
                    Hit h = HitAt(new PointF(pt.X / Scale, pt.Y / Scale));
                    return (IntPtr)(h != null && h.Caption ? Native.HTCAPTION : Native.HTCLIENT);
                }
                case Native.WM_SETCURSOR:
                    if (LowWord(lParam) == Native.HTCLIENT)
                    {
                        Native.SetCursor(Native.LoadCursor(IntPtr.Zero, cursorId));
                        return (IntPtr)1;
                    }
                    break;
                case WM_EXITSIZEMOVE:
                    userMoved = true;
                    Touch();
                    break;
                case Native.WM_MOUSEMOVE:
                    MouseMove(ToDesign(lParam));
                    return IntPtr.Zero;
                case Native.WM_MOUSELEAVE:
                    trackingLeave = false;
                    if (hoverId != null && dragHit == null)
                    {
                        hoverId = null;
                        Invalidate();
                    }
                    return IntPtr.Zero;
                case Native.WM_LBUTTONDOWN:
                {
                    PointF p = ToDesign(lParam);
                    if (App.Tracing)
                    {
                        Hit h = HitAt(p);
                        App.Trace(GetType().Name + " down at " + p + " scale " + Scale + " hits " + hits.Count + " hit " + (h == null ? "none" : h.Id));
                    }
                    MouseDown(p);
                    return IntPtr.Zero;
                }
                case Native.WM_LBUTTONUP:
                    if (App.Tracing) App.Trace(GetType().Name + " up at " + ToDesign(lParam) + " pressed " + pressedId);
                    MouseUp(ToDesign(lParam));
                    return IntPtr.Zero;
                case Native.WM_CAPTURECHANGED:
                    if (dragHit != null && lParam != hwnd)
                    {
                        Hit d = dragHit;
                        dragHit = null;
                        if (d.DragEnd != null) d.DragEnd();
                    }
                    return IntPtr.Zero;
                case Native.WM_MOUSEWHEEL:
                {
                    Touch();
                    var pt = new Native.POINT { X = LowWord(lParam), Y = HighWord(lParam) };
                    Native.ScreenToClient(hwnd, ref pt);
                    Hit h = HitAt(new PointF(pt.X / Scale, pt.Y / Scale));
                    if (h != null && h.Wheel != null) h.Wheel(HighWord(wParam));
                    return IntPtr.Zero;
                }
                case Native.WM_KEYDOWN:
                    Touch();
                    if (!OnKey(wParam.ToInt32()) && wParam.ToInt32() == 0x1B) Hide(true);
                    return IntPtr.Zero;
                case Native.WM_CHAR:
                    if (App.Tracing) App.Trace(GetType().Name + " char " + wParam.ToInt32());
                    Touch();
                    OnChar((char)wParam.ToInt32());
                    return IntPtr.Zero;
                case Native.WM_ACTIVATE:
                    if (LowWord(wParam) == 0) OnDeactivated();
                    break;
                case Native.WM_TIMER:
                    switch (wParam.ToInt32())
                    {
                        case TimerAutoHide: CheckAutoHide(); break;
                        case TimerFade: FadeStep(); break;
                        case TimerCustom: OnCustomTimer(); break;
                    }
                    return IntPtr.Zero;
                case Native.WM_DPICHANGED:
                {
                    Scale = HighWord(wParam) / 96f;
                    var suggested = (Native.RECT)Marshal.PtrToStructure(lParam, typeof(Native.RECT));
                    Native.SetWindowPos(hwnd, IntPtr.Zero, suggested.Left, suggested.Top, Px(DesignWidth), Px(DesignHeight),
                        Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                    Invalidate();
                    return IntPtr.Zero;
                }
                case Native.WM_CLOSE:
                    Hide(true);
                    return IntPtr.Zero;
            }
            return Native.DefWindowProc(hwnd, msg, wParam, lParam);
        }
    }
}
