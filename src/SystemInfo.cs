using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace Dusk
{
    public static class SystemInfo
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValue = "Dusk";
        private const string IcmKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM";
        private const string NightLightKey = @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\" +
            @"default$windows.data.bluelightreduction.bluelightreductionstate\windows.data.bluelightreduction.bluelightreductionstate";

        public static bool FluxRunning()
        {
            Process[] found = Process.GetProcessesByName("flux");
            foreach (Process p in found) p.Dispose();
            return found.Length > 0;
        }

        // Best effort: Windows keeps Night light state in an undocumented blob.
        public static bool NightLightOn()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(NightLightKey))
                {
                    var data = key == null ? null : key.GetValue("Data") as byte[];
                    return data != null && data.Length > 18 && data[18] == 0x15;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool GammaRangeUnlocked()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(IcmKey))
                {
                    object v = key == null ? null : key.GetValue("GdiIcmGammaRange");
                    return v != null && Convert.ToInt64(v) >= 256;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Asks for admin approval, then lets Windows accept the extreme gamma ramps 800K needs.
        // Takes effect after signing out and back in.
        public static bool RequestGammaRangeUnlock()
        {
            try
            {
                var psi = new ProcessStartInfo("reg.exe",
                    "add \"HKLM\\" + IcmKey + "\" /v GdiIcmGammaRange /t REG_DWORD /d 256 /f")
                {
                    Verb = "runas",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit();
                    return p.ExitCode == 0;
                }
            }
            catch (Exception)
            {
                return false; // the user declined the admin prompt
            }
        }

        public static bool AppsUseLightTheme()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = key == null ? null : key.GetValue("AppsUseLightTheme");
                    return v == null || Convert.ToInt32(v) != 0;
                }
            }
            catch (Exception)
            {
                return true;
            }
        }

        public static bool StartsWithWindows()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                return key != null && key.GetValue(RunValue) != null;
        }

        public static void SetStartWithWindows(bool on)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) key.SetValue(RunValue, "\"" + Assembly.GetEntryAssembly().Location + "\" --startup");
                else key.DeleteValue(RunValue, false);
            }
        }

        // f.lux stores the wake time as minutes after midnight.
        public static bool TryFluxWake(out double hours)
        {
            hours = 0;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Michael Herf\flux\Preferences"))
                {
                    object v = key == null ? null : key.GetValue("waketime");
                    if (v == null) return false;
                    hours = Convert.ToInt64(v) / 60.0;
                    return hours >= Settings.MinWake && hours <= Settings.MaxWake;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        // The app behind a window: its exe name (for matching) and a friendly name (for showing).
        public static bool AppForWindow(IntPtr hwnd, out string exe, out string display)
        {
            exe = display = null;
            uint pid;
            Native.GetWindowThreadProcessId(hwnd, out pid);
            if (pid == 0 || pid == Native.GetCurrentProcessId()) return false;

            var cls = new System.Text.StringBuilder(128);
            Native.GetClassName(hwnd, cls, cls.Capacity);
            switch (cls.ToString())
            {
                case "Shell_TrayWnd":
                case "Shell_SecondaryTrayWnd":
                case "NotifyIconOverflowWindow":
                case "TopLevelWindowForOverflowXamlIsland":
                case "Progman":
                case "WorkerW":
                    return false; // the taskbar and desktop aren't apps you'd turn Dusk off for
            }

            IntPtr process = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (process == IntPtr.Zero) return false;
            try
            {
                var path = new System.Text.StringBuilder(1024);
                uint size = (uint)path.Capacity;
                if (!Native.QueryFullProcessImageName(process, 0, path, ref size)) return false;
                exe = Path.GetFileNameWithoutExtension(path.ToString()).ToLowerInvariant();
                try
                {
                    string description = FileVersionInfo.GetVersionInfo(path.ToString()).FileDescription;
                    display = string.IsNullOrEmpty(description) ? null : description.Trim();
                }
                catch (Exception)
                {
                }
                if (string.IsNullOrEmpty(display) || display.Length > 28)
                    display = exe.Length > 0 ? char.ToUpperInvariant(exe[0]) + exe.Substring(1) : exe;
                return exe.Length > 0;
            }
            finally
            {
                Native.CloseHandle(process);
            }
        }

        // f.lux stores latitude/longitude as hundredths of a degree in signed 32-bit DWORDs.
        public static bool TryFluxLocation(out double latitude, out double longitude)
        {
            latitude = longitude = 0;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Michael Herf\flux\Preferences"))
                {
                    if (key == null) return false;
                    object lat = key.GetValue("Latitude"), lon = key.GetValue("Longitude");
                    if (lat == null || lon == null) return false;
                    latitude = unchecked((int)Convert.ToInt64(lat)) / 100.0;
                    longitude = unchecked((int)(uint)Convert.ToInt64(lon)) / 100.0;
                    return Math.Abs(latitude) <= 90 && Math.Abs(longitude) <= 180;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
