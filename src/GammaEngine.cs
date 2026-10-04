using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Dusk
{
    // Applies a colour temperature to every monitor.
    // Normal path: per-monitor gamma ramps. HDR / colour-managed displays ignore gamma ramps,
    // so those (and Windows refusing an extreme ramp) switch to the full-screen colour filter.
    public class GammaEngine : IDisposable
    {
        private static readonly double[] Identity = { 1, 1, 1 };

        private readonly bool enabled;
        private double[] expected = Identity;
        private bool expectedInverted;
        private bool magReady;
        private bool filterActive;
        private bool advancedColor;
        private bool gammaIsIdentity;

        public bool UsingFilter { get { return filterActive; } }
        public bool RangeBlocked { get; private set; }

        // Darkroom: the screen shows red only, inverted and dimmed, whatever the schedule says.
        public bool Darkroom { get; set; }

        // enabled = false leaves the screen untouched (preview mode).
        public GammaEngine(bool enabled)
        {
            this.enabled = enabled;
            RefreshDisplays();
        }

        public void RefreshDisplays()
        {
            advancedColor = DetectAdvancedColor();
            gammaIsIdentity = false; // a display change can reset ramps; make sure they're re-sent
        }

        public void Apply(double kelvin)
        {
            bool inverted = Darkroom;
            double[] m = inverted ? new[] { ColorTemp.DarkroomRed, 0.0, 0.0 } : ColorTemp.Multipliers(kelvin);
            expected = m;
            expectedInverted = inverted;
            if (!enabled) return;

            if (advancedColor)
            {
                ResetGammaOnce();
                ApplyFilter(m, inverted, true);
                return;
            }

            if (SetAllGamma(m, inverted))
            {
                gammaIsIdentity = false;
                RangeBlocked = false;
                if (filterActive) ApplyFilter(Identity, false, false);
                return;
            }

            // Windows refused the ramp (the gamma range limit is on): use the filter instead.
            RangeBlocked = true;
            gammaIsIdentity = false;
            ResetGammaOnce();
            ApplyFilter(m, inverted, true);
        }

        // In filter mode the ramps only need clearing once, not on every animation frame.
        private void ResetGammaOnce()
        {
            if (gammaIsIdentity) return;
            SetAllGamma(Identity, false);
            gammaIsIdentity = true;
        }

        // Returns false if something (a game, a driver, another app) replaced our gamma ramp.
        public bool Verify()
        {
            if (!enabled) return true;
            if (advancedColor || filterActive) return false; // re-send the filter in case something cleared it
            var ramp = new ushort[768];
            foreach (string name in Displays())
            {
                IntPtr hdc = Native.CreateDC(name, null, null, IntPtr.Zero);
                if (hdc == IntPtr.Zero) continue;
                try
                {
                    if (!Native.GetDeviceGammaRamp(hdc, ramp)) continue;
                    for (int c = 0; c < 3; c++)
                    {
                        int want = RampValue(expectedInverted ? 0 : 255, expected[c]);
                        if (Math.Abs(ramp[c * 256 + 255] - want) > 600) return false;
                    }
                }
                finally
                {
                    Native.DeleteDC(hdc);
                }
            }
            return true;
        }

        public void Reset()
        {
            if (!enabled) return;
            expected = Identity;
            expectedInverted = false;
            SetAllGamma(Identity, false);
            gammaIsIdentity = true;
            if (magReady)
            {
                ApplyFilter(Identity, false, false);
                Native.MagUninitialize();
                magReady = false;
            }
        }

        public void Dispose()
        {
            Reset();
        }

        // For troubleshooting: checks gamma access by writing each display's current ramp back unchanged.
        public static string Diagnose()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("  HDR or automatic color management on: " + DetectAdvancedColor());
            var ramp = new ushort[768];
            foreach (string name in Displays())
            {
                IntPtr hdc = Native.CreateDC(name, null, null, IntPtr.Zero);
                if (hdc == IntPtr.Zero)
                {
                    sb.AppendLine("  " + name + ": could not open");
                    continue;
                }
                try
                {
                    bool read = Native.GetDeviceGammaRamp(hdc, ramp);
                    bool write = read && Native.SetDeviceGammaRamp(hdc, ramp);
                    sb.AppendLine("  " + name + ": read " + read + ", write " + write +
                        ", current white point R/G/B " + ramp[255] + "/" + ramp[511] + "/" + ramp[767]);
                }
                finally
                {
                    Native.DeleteDC(hdc);
                }
            }
            sb.Append("  Extended gamma range allowed: " + SystemInfo.GammaRangeUnlocked());
            return sb.ToString();
        }

        private static IEnumerable<string> Displays()
        {
            var dd = new Native.DISPLAY_DEVICE();
            for (uint i = 0; ; i++)
            {
                dd.cb = Marshal.SizeOf(dd);
                if (!Native.EnumDisplayDevices(null, i, ref dd, 0)) yield break;
                if ((dd.StateFlags & Native.DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0) yield return dd.DeviceName;
            }
        }

        private static int RampValue(int i, double multiplier)
        {
            return (int)Math.Min(65535, Math.Round(i * 257 * multiplier));
        }

        // The 3x256 ramp Windows wants. invert flips it so white becomes black and the screen reads as
        // light text on a dark ground, which is what Darkroom does.
        public static ushort[] BuildRamp(double[] m, bool invert)
        {
            var ramp = new ushort[768];
            for (int c = 0; c < 3; c++)
                for (int i = 0; i < 256; i++)
                    ramp[c * 256 + i] = (ushort)RampValue(invert ? 255 - i : i, m[c]);
            return ramp;
        }

        // Row-major 5x5 matrix applied to [R G B A 1] by the full-screen filter; the last row is added
        // to each channel, so a negative scale plus that offset inverts the channel.
        public static float[] BuildMatrix(double[] m, bool invert)
        {
            var t = new float[25];
            t[0] = (float)(invert ? -m[0] : m[0]);
            t[6] = (float)(invert ? -m[1] : m[1]);
            t[12] = (float)(invert ? -m[2] : m[2]);
            t[18] = 1;
            t[24] = 1;
            if (invert)
            {
                t[20] = (float)m[0];
                t[21] = (float)m[1];
                t[22] = (float)m[2];
            }
            return t;
        }

        private static bool SetAllGamma(double[] m, bool invert)
        {
            ushort[] ramp = BuildRamp(m, invert);
            int applied = 0;
            bool ok = true;
            foreach (string name in Displays())
            {
                IntPtr hdc = Native.CreateDC(name, null, null, IntPtr.Zero);
                if (hdc == IntPtr.Zero) continue;
                try
                {
                    ok &= Native.SetDeviceGammaRamp(hdc, ramp);
                    applied++;
                }
                finally
                {
                    Native.DeleteDC(hdc);
                }
            }
            return ok && applied > 0;
        }

        private void ApplyFilter(double[] m, bool invert, bool active)
        {
            if (!magReady) magReady = Native.MagInitialize();
            if (!magReady) return;
            var effect = new Native.MAGCOLOREFFECT { transform = BuildMatrix(m, invert) };
            filterActive = Native.MagSetFullscreenColorEffect(ref effect) && active;
        }

        private static bool DetectAdvancedColor()
        {
            try
            {
                uint numPaths, numModes;
                if (Native.GetDisplayConfigBufferSizes(Native.QDC_ONLY_ACTIVE_PATHS, out numPaths, out numModes) != 0) return false;
                var paths = new Native.DISPLAYCONFIG_PATH_INFO[numPaths];
                var modes = new Native.DISPLAYCONFIG_MODE_INFO[numModes];
                if (Native.QueryDisplayConfig(Native.QDC_ONLY_ACTIVE_PATHS, ref numPaths, paths, ref numModes, modes, IntPtr.Zero) != 0) return false;

                for (int i = 0; i < numPaths; i++)
                {
                    var info = new Native.DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
                    {
                        type = Native.DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO,
                        size = (uint)Marshal.SizeOf(typeof(Native.DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO)),
                        adapterLow = paths[i].targetAdapterLow,
                        adapterHigh = paths[i].targetAdapterHigh,
                        id = paths[i].targetId
                    };
                    // bit 1 = advancedColorEnabled (HDR or automatic colour management)
                    if (Native.DisplayConfigGetDeviceInfo(ref info) == 0 && (info.value & 0x2) != 0) return true;
                }
            }
            catch (Exception)
            {
                // Older Windows without these APIs: assume plain SDR.
            }
            return false;
        }
    }
}
