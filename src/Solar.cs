using System;

namespace Dusk
{
    // Sunrise and sunset from latitude/longitude, computed locally (no internet).
    public static class Solar
    {
        private static readonly DateTime J2000 = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        // Returns false during polar day or night. Minutes are local minutes after midnight.
        public static bool SunTimes(DateTime localDate, double latitude, double longitude,
            out double sunriseMinute, out double sunsetMinute)
        {
            sunriseMinute = sunsetMinute = 0;
            DateTime localNoon = DateTime.SpecifyKind(localDate.Date.AddHours(12), DateTimeKind.Local);
            double n = Math.Round((localNoon.ToUniversalTime() - J2000).TotalDays + 0.0008);

            double jStar = n - longitude / 360.0;
            double m = Mod(357.5291 + 0.98560028 * jStar, 360);
            double c = 1.9148 * Sin(m) + 0.0200 * Sin(2 * m) + 0.0003 * Sin(3 * m);
            double lambda = Mod(m + c + 180 + 102.9372, 360);
            double transit = jStar + 0.0053 * Sin(m) - 0.0069 * Sin(2 * lambda);

            double sinDecl = Sin(lambda) * Sin(23.4397);
            double cosDecl = Math.Cos(Math.Asin(sinDecl));
            double cosHour = (Sin(-0.833) - Sin(latitude) * sinDecl) / (Cos(latitude) * cosDecl);
            if (cosHour < -1 || cosHour > 1) return false;

            double hourAngle = Math.Acos(cosHour) * 180 / Math.PI;
            sunriseMinute = ToLocalMinute(transit - hourAngle / 360, localDate);
            sunsetMinute = ToLocalMinute(transit + hourAngle / 360, localDate);
            return true;
        }

        private static double ToLocalMinute(double daysSinceJ2000, DateTime localDate)
        {
            DateTime local = J2000.AddDays(daysSinceJ2000).ToLocalTime();
            return (local - localDate.Date).TotalMinutes;
        }

        private static double Sin(double deg) { return Math.Sin(deg * Math.PI / 180); }
        private static double Cos(double deg) { return Math.Cos(deg * Math.PI / 180); }
        private static double Mod(double a, double b) { return ((a % b) + b) % b; }
    }
}
