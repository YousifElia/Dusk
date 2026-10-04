using System;
using System.Drawing;

namespace Dusk.Ui
{
    // Colours from the Dusk design, light and dark.
    public sealed class Palette
    {
        public bool IsDark;
        public Color Bg, Surface, Surface2, Ink, Muted, Line, Line2, Knob, Btn, BtnFg, Area, Border, Danger;

        // Fixed colours the design uses on top of coloured surfaces, the same in both themes.
        public static readonly Color OnColor = Hex("#1B1917");
        public static readonly Color BandText = Hex("#2A2118");
        public static readonly Color TagText = Hex("#FFF7EE");

        public static readonly Palette Light = new Palette
        {
            Bg = Hex("#F6F3ED"),
            Surface = Hex("#FFFFFF"),
            Surface2 = Alpha(Hex("#1B1917"), 0.06),
            Ink = Hex("#1B1917"),
            Muted = Hex("#6F6A62"),
            Line = Alpha(Hex("#1B1917"), 0.16),
            Line2 = Alpha(Hex("#1B1917"), 0.09),
            Knob = Hex("#FFFFFF"),
            Btn = Hex("#1B1917"),
            BtnFg = Hex("#F6F3ED"),
            Area = Hex("#E6E1D8"),
            Border = Hex("#D3D0CB"),
            Danger = Hex("#B3261E")
        };

        public static readonly Palette Dark = new Palette
        {
            IsDark = true,
            Bg = Hex("#0E0F14"),
            Surface = Hex("#16171E"),
            Surface2 = Alpha(Color.White, 0.07),
            Ink = Hex("#F2F1EC"),
            Muted = Hex("#9394A0"),
            Line = Alpha(Color.White, 0.14),
            Line2 = Alpha(Color.White, 0.08),
            Knob = Hex("#F2F1EC"),
            Btn = Hex("#F2F1EC"),
            BtnFg = Hex("#0E0F14"),
            Area = Hex("#1A1B23"),
            Border = Hex("#303135"),
            Danger = Hex("#F2B8B5")
        };

        public static Palette For(string theme)
        {
            return theme == "dark" ? Dark : Light;
        }

        public static Color Hex(string s)
        {
            int v = Convert.ToInt32(s.TrimStart('#'), 16);
            return Color.FromArgb((v >> 16) & 255, (v >> 8) & 255, v & 255);
        }

        public static Color Alpha(Color c, double opacity)
        {
            return Color.FromArgb((int)Math.Round(Math.Max(0, Math.Min(1, opacity)) * 255), c.R, c.G, c.B);
        }

        public static Color Multiply(Color a, Color b)
        {
            return Color.FromArgb(a.R * b.R / 255, a.G * b.G / 255, a.B * b.B / 255);
        }

        // How a temperature looks in the UI (the design's colour curve). The screen itself uses ColorTemp.
        public static Color Kelvin(double kelvin)
        {
            double t = kelvin / 100, r, g, b;
            if (t <= 66)
            {
                r = 255;
                g = 99.4708025861 * Math.Log(t) - 161.1195681661;
                b = t <= 19 ? 0 : 138.5177312231 * Math.Log(t - 10) - 305.0447927307;
            }
            else
            {
                r = 329.698727446 * Math.Pow(t - 60, -0.1332047592);
                g = 288.1221695283 * Math.Pow(t - 60, -0.0755148492);
                b = 255;
            }
            return Color.FromArgb(Byte(r), Byte(g), Byte(b));
        }

        private static int Byte(double v)
        {
            return (int)Math.Round(Math.Max(0, Math.Min(255, v)));
        }

        // Slider position: 800K–6500K takes the first 80% of the track, where the useful warm range is;
        // 6500K–9300K (cooler than normal) takes the rest.
        public const double NeutralPosition = 0.8;

        public static double KelvinToPosition(double k)
        {
            k = Math.Max(ColorTemp.Min, Math.Min(ColorTemp.Max, k));
            if (k <= ColorTemp.Neutral) return NeutralPosition * (k - ColorTemp.Min) / (ColorTemp.Neutral - ColorTemp.Min);
            return NeutralPosition + (1 - NeutralPosition) * (k - ColorTemp.Neutral) / (ColorTemp.Max - ColorTemp.Neutral);
        }

        public static int PositionToKelvin(double p)
        {
            p = Math.Max(0, Math.Min(1, p));
            double k = p <= NeutralPosition
                ? ColorTemp.Min + p / NeutralPosition * (ColorTemp.Neutral - ColorTemp.Min)
                : ColorTemp.Neutral + (p - NeutralPosition) / (1 - NeutralPosition) * (ColorTemp.Max - ColorTemp.Neutral);
            int snapped = (int)(Math.Round(k / 100) * 100);
            if (Math.Abs(snapped - ColorTemp.Neutral) <= 100) snapped = ColorTemp.Neutral; // easy to land on "no change"
            return snapped;
        }

        // Chart height for a temperature: 6500K reaches the top line; cooler values use the headroom above it.
        public static double KelvinToChart(double k)
        {
            if (k <= ColorTemp.Neutral) return Math.Max(0, (k - ColorTemp.Min) / (ColorTemp.Neutral - ColorTemp.Min));
            return 1 + 0.1 * Math.Min(1, (k - ColorTemp.Neutral) / (ColorTemp.Max - ColorTemp.Neutral));
        }
    }
}
