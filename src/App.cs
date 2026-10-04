using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using Dusk.Ui;

// Opts into .NET Framework 4.8 runtime defaults (for example TLS 1.2 for the place lookup).
[assembly: TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

namespace Dusk
{
    public static class App
    {
        private const string MutexName = "Local\\Dusk.SingleInstance";
        public const string ControllerClass = "DuskController";
        public const uint WM_SHOW_MAIN = Native.WM_APP + 2, WM_POSTED = Native.WM_APP + 5;

        private static readonly Queue<Action> posted = new Queue<Action>();
        private static IntPtr postTarget;

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int pid);

        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                if (!Native.SetProcessDpiAwarenessContext(Native.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)) Native.SetProcessDPIAware();
            }
            catch (EntryPointNotFoundException)
            {
                Native.SetProcessDPIAware();
            }

            string snapshot = Arg(args, "--snapshot");
            if (snapshot != null) return Snapshot(args, snapshot);

            bool createdNew;
            var mutex = new Mutex(true, MutexName, out createdNew);
            if (!createdNew)
            {
                // Already running: ask that copy to open its window.
                IntPtr existing = Native.FindWindow(ControllerClass, null);
                if (existing != IntPtr.Zero)
                {
                    AllowSetForegroundWindow(-1);
                    Native.PostMessage(existing, WM_SHOW_MAIN, IntPtr.Zero, IntPtr.Zero);
                }
                return 0;
            }

            Fonts.Load();
            Tracing = Flag(args, "--trace");
            string placeAt = Arg(args, "--place-at");
            if (placeAt != null)
            {
                string[] xy = placeAt.Split(',');
                Surface.PlacementPoint = new Native.POINT { X = int.Parse(xy[0], CultureInfo.InvariantCulture), Y = int.Parse(xy[1], CultureInfo.InvariantCulture) };
            }
            var controller = new Controller(Flag(args, "--preview"), false);
            if (Flag(args, "--skip-setup")) controller.Settings.SetupDone = true; // for testing the main window without saving setup
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Log(e.ExceptionObject as Exception);
                controller.EmergencyReset();
            };
            controller.Start(!Flag(args, "--startup") || !controller.Settings.SetupDone);

            Native.MSG msg;
            while (Native.GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
            {
                Native.TranslateMessage(ref msg);
                Native.DispatchMessage(ref msg);
            }
            GC.KeepAlive(mutex);
            return 0;
        }

        // Renders a screen to a PNG for design review: --snapshot out.png [--screen horizon|dial|strata|colors|firstrun|tray]
        // [--theme light|dark] [--hour 21.37] [--scale 1.25]. Never touches the screen, settings or registry.
        private static int Snapshot(string[] args, string path)
        {
            Fonts.Load();
            var controller = new Controller(true, true);
            string theme = Arg(args, "--theme");
            if (theme != null) controller.Settings.Theme = theme;
            if (Flag(args, "--darkroom")) controller.Settings.Darkroom = true;
            string hour = Arg(args, "--hour");
            controller.FixedHour = hour != null ? double.Parse(hour, CultureInfo.InvariantCulture) : 21.37;
            string scaleText = Arg(args, "--scale");
            float scale = scaleText != null ? float.Parse(scaleText, CultureInfo.InvariantCulture) : Native.GetDpiForSystem() / 96f;

            string screen = Arg(args, "--screen") ?? "horizon";
            if (screen == "tray")
            {
                controller.Flyout.SetPalette(controller.Palette);
                controller.Flyout.SaveSnapshot(path, scale);
                return 0;
            }
            controller.Settings.SetupDone = screen != "firstrun";
            if (screen == "horizon" || screen == "dial" || screen == "strata") controller.Settings.View = screen;
            controller.Main.SetPalette(controller.Palette);
            controller.Main.SetScreen(screen == "firstrun" ? MainScreen.FirstRun : screen == "colors" ? MainScreen.Colors : MainScreen.Main);
            controller.Main.SaveSnapshot(path, scale);
            return 0;
        }

        // Runs an action on the UI thread (from the place lookup's worker thread, for example).
        public static void Post(Action action)
        {
            lock (posted) posted.Enqueue(action);
            Native.PostMessage(postTarget, WM_POSTED, IntPtr.Zero, IntPtr.Zero);
        }

        internal static void SetPostTarget(IntPtr hwnd)
        {
            postTarget = hwnd;
        }

        internal static void DrainPosted()
        {
            while (true)
            {
                Action action;
                lock (posted)
                {
                    if (posted.Count == 0) return;
                    action = posted.Dequeue();
                }
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Log(ex);
                }
            }
        }

        // Diagnostic event log, only with --trace (written to %TEMP%\dusk-trace.log).
        public static bool Tracing;

        public static void Trace(string message)
        {
            if (!Tracing) return;
            try
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "dusk-trace.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + Environment.NewLine);
            }
            catch (Exception)
            {
            }
        }

        public static void Log(Exception ex)
        {
            if (ex == null) return;
            try
            {
                string file = Path.Combine(Settings.FolderPath, "error.log");
                Directory.CreateDirectory(Settings.FolderPath);
                if (File.Exists(file) && new FileInfo(file).Length > 1024 * 1024) return;
                File.AppendAllText(file, DateTime.Now.ToString("s") + "  " + ex + Environment.NewLine + Environment.NewLine);
            }
            catch (Exception)
            {
            }
        }

        private static string Arg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        private static bool Flag(string[] args, string name)
        {
            return Array.IndexOf(args, name) >= 0;
        }
    }
}
