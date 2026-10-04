using System;
using System.Collections.Generic;

namespace Dusk
{
    // Converts a colour temperature to per-channel multipliers for the screen.
    // Blackbody spectrum (Planck) -> CIE 1931 XYZ -> linear sRGB, normalised so 6500K is white,
    // then sRGB-encoded because gamma ramps work on encoded values.
    public static class ColorTemp
    {
        public const int Min = 600;
        public const int Max = 9300;
        public const int Neutral = 6500;

        // Below this the true blackbody colour is outside the sRGB gamut and green collapses to zero,
        // so 700-1000K continues the curve smoothly instead.
        private const int PhysicalFloor = 1000;

        // Under this there is no redder colour left on a screen, so the curve keeps going by dimming the red.
        public const int DeepRed = 800;

        // Darkroom: red only, inverted and dim, for the darkest readable screen Dusk can produce.
        public const double DarkroomRed = 0.35;

        private static readonly Dictionary<int, double[]> cache = new Dictionary<int, double[]>();
        private static double[] white;

        public static double[] Multipliers(double kelvin)
        {
            int k = Clamp(kelvin, Min, Max);
            double[] m;
            lock (cache)
            {
                if (cache.TryGetValue(k, out m)) return m;
                m = Compute(k);
                cache[k] = m;
            }
            return m;
        }

        public static int Clamp(double kelvin, int lo, int hi)
        {
            if (kelvin < lo) return lo;
            if (kelvin > hi) return hi;
            return (int)Math.Round(kelvin);
        }

        // Blend two temperatures evenly in mired space (1e6 / K), which looks linear to the eye.
        public static double Mix(double fromK, double toK, double t)
        {
            if (t <= 0) return fromK;
            if (t >= 1) return toK;
            double a = 1e6 / fromK, b = 1e6 / toK;
            return 1e6 / (a + (b - a) * t);
        }

        private static double[] Compute(int k)
        {
            if (k < PhysicalFloor)
            {
                double[] floor = Multipliers(PhysicalFloor);
                double t = Math.Max(0, (k - 700) / 300.0); // green is gone by 700K
                double red = k >= DeepRed ? 1 : 1 - 0.55 * (DeepRed - k) / (double)(DeepRed - Min);
                return new[] { red, floor[1] * Math.Pow(t, 1.5), 0.0 };
            }
            if (white == null) white = LinearSrgb(Neutral);
            double[] c = LinearSrgb(k);
            double max = 0;
            for (int i = 0; i < 3; i++)
            {
                c[i] = Math.Max(0, c[i] / white[i]);
                max = Math.Max(max, c[i]);
            }
            for (int i = 0; i < 3; i++) c[i] = Math.Pow(c[i] / max, 1 / 2.2);
            return c;
        }

        private static double[] LinearSrgb(double kelvin)
        {
            double x = 0, y = 0, z = 0;
            for (int nm = 380; nm <= 780; nm++)
            {
                double m = nm * 1e-9;
                double b = 1 / (Math.Pow(m, 5) * (Math.Exp(1.4388e-2 / (m * kelvin)) - 1));
                x += b * CieX(nm);
                y += b * CieY(nm);
                z += b * CieZ(nm);
            }
            return new[]
            {
                3.2406 * x - 1.5372 * y - 0.4986 * z,
                -0.9689 * x + 1.8758 * y + 0.0415 * z,
                0.0557 * x - 0.2040 * y + 1.0570 * z
            };
        }

        // Wyman, Sloan and Shirley (2013) multi-lobe fit of the CIE 1931 2-degree observer.
        private static double G(double l, double mu, double s1, double s2)
        {
            double s = l < mu ? s1 : s2;
            double d = (l - mu) / s;
            return Math.Exp(-0.5 * d * d);
        }

        private static double CieX(double l) { return 1.056 * G(l, 599.8, 37.9, 31.0) + 0.362 * G(l, 442.0, 16.0, 26.7) - 0.065 * G(l, 501.1, 20.4, 26.2); }
        private static double CieY(double l) { return 0.821 * G(l, 568.8, 46.9, 40.5) + 0.286 * G(l, 530.9, 16.3, 31.1); }
        private static double CieZ(double l) { return 1.217 * G(l, 437.0, 11.8, 36.0) + 0.681 * G(l, 459.0, 26.0, 13.8); }
    }
}
