using System;
using System.Runtime.InteropServices;
using Dusk.Ui;

namespace Dusk
{
    // Owns the state both windows show, applies colours to the screen, and runs the tray icon,
    // hotkeys and system notifications through a hidden message window.
    public sealed class Controller
    {
        private const uint WM_TRAY = Native.WM_APP + 1;
        private const int TimerTick = 1, TimerAnimate = 2, TimerPlay = 3, TimerSave = 4, TimerTrim = 5;
        private const int HotkeyCooler = 1, HotkeyWarmer = 2, HotkeyOff = 3, HotkeyOpen = 4;
        private const double PlaySpeed = 2; // hours of the day per second
        private static readonly uint[] HotkeyKeys = { 0, 0x21, 0x22, 0x23, 0x24 }; // PgUp, PgDn, End, Home

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        private readonly bool preview;
        private readonly bool[] hotkeys = new bool[5];
        private Native.WndProcDelegate procDelegate;
        private Native.WinEventDelegate foregroundDelegate;
        private IntPtr hwnd, foregroundHook, iconOn, iconOff;
        private uint taskbarCreated;
        private bool trayAdded, trayShowsOff, quitting;
        private string trayTip;
        private GammaEngine engine;
        private double applied = ColorTemp.Neutral, target = ColorTemp.Neutral;
        private DateTime lastVerify, lastConflictCheck, lastPlayStep, flyoutClosedAt;
        private string disabledKind;
        private DateTime disabledUntil;
        private string lastUiState;

        public readonly Settings Settings;
        public readonly Schedule Schedule;
        public readonly MainWindow Main;
        public readonly TrayFlyout Flyout;

        public double? FixedHour;
        public double? PreviewHour { get; private set; }
        public bool Playing { get; private set; }
        public Tone? Selected { get; private set; }
        public string LastAppExe { get; private set; }
        public string LastAppName { get; private set; }
        public bool FluxRunning { get; private set; }
        public bool NightLightOn { get; private set; }

        public Controller(bool preview, bool snapshot)
        {
            this.preview = preview;
            Settings = snapshot ? Settings.CreateDefault() : Settings.Load();
            Settings.DoNotSave = preview;
            Schedule = new Schedule(Settings);
            Main = new MainWindow(this);
            Flyout = new TrayFlyout(this);
            Main.Palette = Flyout.Palette = Palette;
        }

        // ---- What the windows show ----

        public Palette Palette { get { return Palette.For(Settings.Theme); } }
        public double NowHour { get { return FixedHour.HasValue ? FixedHour.Value : DateTime.Now.TimeOfDay.TotalHours; } }
        public double DisplayHour { get { return PreviewHour.HasValue ? PreviewHour.Value : NowHour; } }
        public DayTimes Times { get { return Schedule.Times(DateTime.Today); } }
        public Period DisplayPeriod { get { return Schedule.PeriodAt(Times, DisplayHour); } }
        public Tone SelectedTone { get { return Selected.HasValue ? Selected.Value : Schedule.ToneOf(DisplayPeriod); } }
        public int DisplayKelvin { get { return (int)(Math.Round(Schedule.KelvinAt(Times, DisplayHour) / 10) * 10); } }
        public bool TimedOff { get { return disabledKind != null && DateTime.Now < disabledUntil; } }
        public bool AppOff { get { return LastAppExe != null && Settings.ExcludedApps.Contains(LastAppExe); } }
        public bool IsOff { get { return TimedOff || AppOff; } }
        public bool IsDarkroom { get { return Settings.Darkroom && !IsOff; } }
        public string DisabledKind { get { return TimedOff ? disabledKind : null; } }

        public string OffNote
        {
            get
            {
                if (TimedOff)
                    return disabledKind == "sunrise"
                        ? "Back on at sunrise, " + Schedule.FormatHour(disabledUntil.TimeOfDay.TotalHours)
                        : "Back on at " + Schedule.FormatHour(disabledUntil.TimeOfDay.TotalHours);
                return AppOff ? "Off while " + LastAppName + " is in front" : "";
            }
        }

        public bool RangeBlocked { get { return engine != null && engine.RangeBlocked && !FluxRunning && !NightLightOn; } }

        public string Warning
        {
            get
            {
                if (FluxRunning) return "f.lux is also running and will fight Dusk over your screen colors. Quit f.lux to use Dusk.";
                if (NightLightOn) return "Windows Night light is on and tints your screen on top of Dusk.";
                if (engine != null && engine.RangeBlocked) return "Windows is limiting how warm your screen can go.";
                return null;
            }
        }

        // ---- Startup and shutdown ----

        public void Start(bool showMain)
        {
            CreateMessageWindow();
            App.SetPostTarget(hwnd);
            engine = new GammaEngine(!preview);
            target = ComputeTarget();
            Native.SetTimer(hwnd, (IntPtr)TimerAnimate, 16, IntPtr.Zero); // fade in from normal colours

            foregroundDelegate = OnForegroundChanged;
            foregroundHook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero, foregroundDelegate, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
            OnForegroundChanged(IntPtr.Zero, 0, Native.GetForegroundWindow(), 0, 0, 0, 0);
            Native.WTSRegisterSessionNotification(hwnd, 0);
            taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");

            CheckConflicts();
            RegisterHotkeys();
            AddTray();
            Native.SetTimer(hwnd, (IntPtr)TimerTick, 1000, IntPtr.Zero);
            if (showMain) OpenMain();
            else TrimSoon();
        }

        public void Quit()
        {
            if (quitting) return;
            quitting = true;
            Settings.Save();
            Main.Hide(false);
            Flyout.Hide(false);
            for (int id = HotkeyCooler; id <= HotkeyOpen; id++)
                if (hotkeys[id]) Native.UnregisterHotKey(hwnd, id);
            if (foregroundHook != IntPtr.Zero) Native.UnhookWinEvent(foregroundHook);
            Native.WTSUnRegisterSessionNotification(hwnd);
            RemoveTray();
            engine.Reset();
            Native.PostQuitMessage(0);
        }

        public void EmergencyReset()
        {
            try
            {
                if (engine != null) engine.Reset();
            }
            catch (Exception)
            {
            }
        }

        // ---- Actions from the windows ----

        public void OpenMain()
        {
            Flyout.Hide(false);
            Main.SetScreen(Settings.SetupDone ? MainScreen.Main : MainScreen.FirstRun);
            Main.Show();
        }

        public void ToggleFlyout()
        {
            if (Flyout.IsVisible) Flyout.Hide(true);
            else if ((DateTime.Now - flyoutClosedAt).TotalMilliseconds > 400) Flyout.Show(); // this click just closed it
        }

        public void FlyoutClosed()
        {
            flyoutClosedAt = DateTime.Now;
        }

        public void WindowHidden(Surface window)
        {
            if (window == Main)
            {
                StopPlay();
                PreviewHour = null;
                Selected = null;
                Changed();
            }
            TrimSoon();
        }

        public void SetPreviewHour(double hour)
        {
            StopPlay();
            PreviewHour = Math.Max(0, Math.Min(23.99, hour));
            Selected = null;
            Changed();
        }

        public void TogglePlay()
        {
            if (Playing)
            {
                StopPlay();
                Changed();
                return;
            }
            Playing = true;
            Selected = null;
            if (!PreviewHour.HasValue) PreviewHour = NowHour;
            lastPlayStep = DateTime.Now;
            if (hwnd != IntPtr.Zero) Native.SetTimer(hwnd, (IntPtr)TimerPlay, 33, IntPtr.Zero);
            Changed();
        }

        public void BackToNow()
        {
            StopPlay();
            PreviewHour = null;
            Changed();
        }

        private void StopPlay()
        {
            if (!Playing) return;
            Playing = false;
            if (hwnd != IntPtr.Zero) Native.KillTimer(hwnd, (IntPtr)TimerPlay);
        }

        public void SelectTone(Tone tone)
        {
            Selected = tone;
            Changed();
        }

        public void SetTemp(Tone tone, int kelvin)
        {
            if (Settings.GetTemp(tone) == kelvin && disabledKind == null) return;
            Settings.SetTemp(tone, kelvin);
            disabledKind = null;
            SaveSoon();
            Changed();
        }

        public void ApplyPreset(int dayK, int sunsetK, int bedK)
        {
            Settings.DayK = dayK;
            Settings.SunsetK = sunsetK;
            Settings.BedK = bedK;
            SaveSoon();
            Changed();
        }

        public void SetWake(double hours)
        {
            Settings.Wake = Math.Max(Settings.MinWake, Math.Min(Settings.MaxWake, hours));
            Settings.Normalize();
            SaveSoon();
            Changed();
        }

        public void SetView(string view)
        {
            Settings.View = view;
            SaveSoon();
            Changed();
        }

        public void ToggleTheme()
        {
            Settings.Theme = Settings.Theme == "dark" ? "light" : "dark";
            Main.SetPalette(Palette);
            Flyout.SetPalette(Palette);
            SaveSoon();
        }

        public void SetPlace(PlaceResult place)
        {
            Settings.Place = place.Name ?? "";
            Settings.HasLocation = true;
            Settings.Latitude = place.Latitude;
            Settings.Longitude = place.Longitude;
            Settings.Normalize();
            SaveSoon();
            Changed();
        }

        public void FinishSetup()
        {
            Settings.SetupDone = true;
            Settings.Save();
            if (!preview)
            {
                try
                {
                    SystemInfo.SetStartWithWindows(true);
                }
                catch (Exception ex)
                {
                    App.Log(ex);
                }
            }
            Changed();
        }

        // The warmest Dusk goes: red only, inverted and dim. Alt+End leaves it, as in f.lux.
        public void ToggleDarkroom()
        {
            Settings.Darkroom = !Settings.Darkroom;
            if (Settings.Darkroom) disabledKind = null; // darkroom takes over from a timed pause
            SaveSoon();
            Changed();
        }

        // kind: "hour", "sunrise" or "app". Choosing the active option again turns Dusk back on.
        public void DisableFor(string kind)
        {
            if (kind == "app")
            {
                if (LastAppExe == null) return;
                if (!Settings.ExcludedApps.Remove(LastAppExe)) Settings.ExcludedApps.Add(LastAppExe);
                SaveSoon();
            }
            else if (DisabledKind == kind)
            {
                disabledKind = null;
            }
            else
            {
                disabledKind = kind;
                disabledUntil = kind == "hour" ? DateTime.Now.AddHours(1) : NextSunrise();
            }
            Changed();
        }

        private DateTime NextSunrise()
        {
            DateTime now = DateTime.Now;
            DateTime today = now.Date.AddHours(Schedule.Times(now.Date).Sunrise);
            return today > now ? today : now.Date.AddDays(1).AddHours(Schedule.Times(now.Date.AddDays(1)).Sunrise);
        }

        public void UnlockRange()
        {
            SystemInfo.RequestGammaRangeUnlock();
            Changed();
        }

        // Something the windows show has changed: update the screen target, the tray and the windows.
        public void Changed()
        {
            if (quitting) return;
            target = ComputeTarget();
            if (hwnd != IntPtr.Zero && Math.Abs(1e6 / applied - 1e6 / target) > 0.05)
                Native.SetTimer(hwnd, (IntPtr)TimerAnimate, 16, IntPtr.Zero);
            UpdateTray();
            Main.Invalidate();
            Flyout.Invalidate();
        }

        // ---- Screen colour ----

        private double ComputeTarget()
        {
            if (engine != null) engine.Darkroom = IsDarkroom;
            if (IsOff) return ColorTemp.Neutral;
            double hour = PreviewHour.HasValue && Main.IsVisible ? PreviewHour.Value : NowHour;
            return Schedule.KelvinAt(Times, hour);
        }

        private void Animate()
        {
            double diff = 1e6 / target - 1e6 / applied;
            if (Math.Abs(diff) < 0.5)
            {
                applied = target;
                Native.KillTimer(hwnd, (IntPtr)TimerAnimate);
            }
            else
            {
                applied = 1e6 / (1e6 / applied + diff * 0.14);
            }
            engine.Apply(applied);
        }

        private void Tick()
        {
            DateTime now = DateTime.Now;
            if (disabledKind != null && now >= disabledUntil) disabledKind = null;
            target = ComputeTarget();
            if (Math.Abs(1e6 / applied - 1e6 / target) > 0.05)
            {
                Native.SetTimer(hwnd, (IntPtr)TimerAnimate, 16, IntPtr.Zero);
            }
            else if ((now - lastVerify).TotalSeconds >= 5)
            {
                // Games, drivers and other apps sometimes replace our colours.
                lastVerify = now;
                if (!engine.Verify()) engine.Apply(applied);
            }
            if ((now - lastConflictCheck).TotalSeconds >= 10) CheckConflicts();

            // Repaint once a minute (the clock) or when something visible changed.
            string state = now.ToString("HHmm") + IsOff + Warning + DisplayKelvin;
            if (state != lastUiState)
            {
                lastUiState = state;
                UpdateTray();
                Main.Invalidate();
                Flyout.Invalidate();
            }
        }

        private void PlayStep()
        {
            DateTime now = DateTime.Now;
            double dt = Math.Min(0.1, (now - lastPlayStep).TotalSeconds);
            lastPlayStep = now;
            PreviewHour = Schedule.Mod24(DisplayHour + dt * PlaySpeed);
            Changed();
        }

        private void Reapply()
        {
            if (quitting) return;
            engine.RefreshDisplays();
            engine.Apply(applied);
            lastVerify = DateTime.MinValue;
        }

        private void CheckConflicts()
        {
            lastConflictCheck = DateTime.Now;
            FluxRunning = SystemInfo.FluxRunning();
            NightLightOn = SystemInfo.NightLightOn();
            RegisterHotkeys(); // retries any another app (often f.lux) was holding
        }

        private void SaveSoon()
        {
            if (hwnd == IntPtr.Zero) return;
            Native.SetTimer(hwnd, (IntPtr)TimerSave, 1000, IntPtr.Zero);
        }

        // Hands unused memory back to Windows once the windows are closed.
        private void TrimSoon()
        {
            if (hwnd == IntPtr.Zero) return;
            Native.SetTimer(hwnd, (IntPtr)TimerTrim, 1500, IntPtr.Zero);
        }

        private static void TrimMemory()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Native.SetProcessWorkingSetSize(Native.GetCurrentProcess(), (IntPtr)(-1), (IntPtr)(-1));
        }

        private void OnForegroundChanged(IntPtr hook, uint evt, IntPtr window, int idObject, int idChild, uint thread, uint time)
        {
            string exe, name;
            if (window == IntPtr.Zero || !SystemInfo.AppForWindow(window, out exe, out name)) return;
            if (exe == LastAppExe) return;
            bool wasOff = AppOff;
            LastAppExe = exe;
            LastAppName = name;
            if (AppOff != wasOff || Flyout.IsVisible) Changed();
        }

        // ---- Tray icon ----

        private void AddTray()
        {
            if (trayAdded || hwnd == IntPtr.Zero) return;
            int size = Math.Max(16, GetSystemMetrics(49)); // SM_CXSMICON
            if (iconOn == IntPtr.Zero) iconOn = IconArt.Hicon(size, false);
            if (iconOff == IntPtr.Zero) iconOff = IconArt.Hicon(size, true);
            trayShowsOff = IsOff;
            trayTip = TrayTip();
            var data = TrayData(Native.NIF_MESSAGE | Native.NIF_ICON | Native.NIF_TIP);
            trayAdded = Native.Shell_NotifyIcon(Native.NIM_ADD, ref data);
        }

        private void UpdateTray()
        {
            if (!trayAdded) return;
            string tip = TrayTip();
            if (tip == trayTip && trayShowsOff == IsOff) return;
            trayTip = tip;
            trayShowsOff = IsOff;
            var data = TrayData(Native.NIF_ICON | Native.NIF_TIP);
            Native.Shell_NotifyIcon(Native.NIM_MODIFY, ref data);
        }

        private void RemoveTray()
        {
            if (!trayAdded) return;
            var data = TrayData(0);
            Native.Shell_NotifyIcon(Native.NIM_DELETE, ref data);
            trayAdded = false;
        }

        private Native.NOTIFYICONDATA TrayData(uint flags)
        {
            return new Native.NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf(typeof(Native.NOTIFYICONDATA)),
                hWnd = hwnd,
                uID = 1,
                uFlags = flags,
                uCallbackMessage = WM_TRAY,
                hIcon = trayShowsOff ? iconOff : iconOn,
                szTip = trayTip ?? "Dusk"
            };
        }

        private string TrayTip()
        {
            if (IsOff) return "Dusk is off";
            if (IsDarkroom) return "Dusk · Darkroom";
            return "Dusk · " + Schedule.NameOf(Schedule.PeriodAt(Times, NowHour)) + " · " + (int)(Math.Round(target / 10) * 10) + " K";
        }

        // ---- Hotkeys ----

        private void RegisterHotkeys()
        {
            if (hwnd == IntPtr.Zero || preview) return;
            for (int id = HotkeyCooler; id <= HotkeyOpen; id++)
            {
                if (hotkeys[id]) continue;
                uint mods = Native.MOD_ALT | (id >= HotkeyOff ? Native.MOD_NOREPEAT : 0);
                hotkeys[id] = Native.RegisterHotKey(hwnd, id, mods, HotkeyKeys[id]);
            }
        }

        private void AdjustCurrent(int delta)
        {
            Tone tone = Schedule.ToneOf(Schedule.PeriodAt(Times, NowHour));
            SetTemp(tone, Settings.GetTemp(tone) + delta);
        }

        // ---- Message window ----

        private void CreateMessageWindow()
        {
            procDelegate = WndProc;
            IntPtr instance = Native.GetModuleHandle(null);
            var wc = new Native.WNDCLASSEX
            {
                cbSize = Marshal.SizeOf(typeof(Native.WNDCLASSEX)),
                lpfnWndProc = procDelegate,
                hInstance = instance,
                lpszClassName = App.ControllerClass
            };
            Native.RegisterClassEx(ref wc);
            // A hidden top-level window (not message-only) so it receives display and power broadcasts.
            hwnd = Native.CreateWindowEx(Native.WS_EX_TOOLWINDOW, App.ControllerClass, "Dusk", Native.WS_POPUP,
                0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
            if (hwnd == IntPtr.Zero)
                App.Log(new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Couldn't create Dusk's message window"));
        }

        private IntPtr WndProc(IntPtr h, uint msg, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (taskbarCreated != 0 && msg == taskbarCreated)
                {
                    trayAdded = false; // Explorer restarted
                    AddTray();
                    return IntPtr.Zero;
                }
                switch (msg)
                {
                    case WM_TRAY:
                    {
                        int mouse = lParam.ToInt32() & 0xFFFF;
                        if (mouse == Native.WM_LBUTTONUP || mouse == Native.WM_RBUTTONUP) ToggleFlyout();
                        return IntPtr.Zero;
                    }
                    case Native.WM_HOTKEY:
                        switch (wParam.ToInt32())
                        {
                            case HotkeyCooler: AdjustCurrent(100); break;
                            case HotkeyWarmer: AdjustCurrent(-100); break;
                            case HotkeyOff:
                                if (IsDarkroom) ToggleDarkroom();
                                else DisableFor("hour");
                                break;
                            case HotkeyOpen: OpenMain(); break;
                        }
                        return IntPtr.Zero;
                    case Native.WM_TIMER:
                        switch (wParam.ToInt32())
                        {
                            case TimerTick: Tick(); break;
                            case TimerAnimate: Animate(); break;
                            case TimerPlay: PlayStep(); break;
                            case TimerSave:
                                Native.KillTimer(h, (IntPtr)TimerSave);
                                Settings.Save();
                                break;
                            case TimerTrim:
                                Native.KillTimer(h, (IntPtr)TimerTrim);
                                if (!Main.IsVisible && !Flyout.IsVisible) TrimMemory();
                                break;
                        }
                        return IntPtr.Zero;
                    case Native.WM_DISPLAYCHANGE:
                        Reapply();
                        break;
                    case Native.WM_POWERBROADCAST:
                        if (wParam.ToInt32() == Native.PBT_APMRESUMEAUTOMATIC || wParam.ToInt32() == Native.PBT_APMRESUMESUSPEND) Reapply();
                        break;
                    case Native.WM_WTSSESSION_CHANGE:
                        if (wParam.ToInt32() == Native.WTS_SESSION_UNLOCK) Reapply();
                        break;
                    case App.WM_SHOW_MAIN:
                        OpenMain();
                        return IntPtr.Zero;
                    case App.WM_POSTED:
                        App.DrainPosted();
                        return IntPtr.Zero;
                }
            }
            catch (Exception ex)
            {
                App.Log(ex);
            }
            return Native.DefWindowProc(h, msg, wParam, lParam);
        }
    }
}
