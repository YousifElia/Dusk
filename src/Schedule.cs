using System;

namespace Dusk
{
    public enum Period { Night, Morning, Day, Evening }

    // The three colours you choose. Morning (after waking, before sunrise) reuses the sunset colour.
    public enum Tone { Day, Sunset, Bed }

    public struct DayTimes
    {
        public double Wake, Sunrise, Sunset, Bed; // hours of the day, 0–24, for display
        public double B1, B2, B3, B4;             // wake, sunrise, sunset, bedtime in order, unwrapped from wake
        public bool SunKnown;
    }

    public sealed class Preset
    {
        public string Name, Description;
        public int Day, Sunset, Bed;

        public Preset(string name, string description, int day, int sunset, int bed)
        {
            Name = name;
            Description = description;
            Day = day;
            Sunset = sunset;
            Bed = bed;
        }
    }

    // The day as the design describes it: bedtime colours until you wake, sunset colours until sunrise,
    // daytime colours until sunset, sunset colours until bedtime (a set time before you wake).
    // Each change blends over 45 minutes centred on its time.
    public class Schedule
    {
        public const double BlendHours = 0.75;
        public const string CustomName = "Custom colors";

        public static readonly Preset[] Presets =
        {
            new Preset("Recommended colors", "Warm evenings, very warm bedtime.", 6500, 3400, 1900),
            new Preset("Working late", "A gentler evening shift for late nights.", 6500, 4700, 3400),
            new Preset("Far from the equator", "Softer daytime for long summer light.", 5500, 3400, 1900),
            new Preset("Classic", "Same warmth for sunset and bedtime.", 6500, 3400, 3400),
            new Preset("Cave painting", "Deep amber, as warm as it gets.", 5000, 2300, 1200),
            new Preset("Color fidelity", "A small shift; colors stay accurate.", 6500, 5000, 3400)
        };

        private readonly Settings settings;
        private DateTime sunDate = DateTime.MinValue;
        private double sunLat = double.NaN, sunLon = double.NaN, sunRise, sunSet;
        private bool sunKnown;

        public Schedule(Settings settings)
        {
            this.settings = settings;
        }

        public Settings Settings { get { return settings; } }

        public DayTimes Times(DateTime date)
        {
            double rise = 7, set = 19;
            bool known = false;
            if (settings.HasLocation && SunTimes(date.Date, out rise, out set)) known = true;

            var t = new DayTimes { Wake = settings.Wake, Sunrise = rise, SunKnown = known };
            t.B1 = settings.Wake;
            t.B2 = Math.Max(rise, t.B1);
            t.B3 = Math.Min(Math.Max(set, t.B2 + 0.5), t.B1 + 19);
            t.B4 = Math.Min(Math.Max(t.B1 + 24 - settings.SleepHours, t.B3 + 0.6), t.B1 + 20);
            t.Sunset = Mod24(set);
            t.Bed = Mod24(t.B4);
            return t;
        }

        private bool SunTimes(DateTime date, out double rise, out double set)
        {
            if (date != sunDate || settings.Latitude != sunLat || settings.Longitude != sunLon)
            {
                double r, s;
                sunKnown = Solar.SunTimes(date, settings.Latitude, settings.Longitude, out r, out s);
                sunRise = Mod24(r / 60);
                sunSet = Mod24(s / 60);
                if (sunSet <= sunRise) sunSet += 24;
                sunDate = date;
                sunLat = settings.Latitude;
                sunLon = settings.Longitude;
            }
            rise = sunKnown ? sunRise : 7;
            set = sunKnown ? sunSet : 19;
            return sunKnown;
        }

        // Maps an hour of the day onto the span that starts 3 hours before wake, where the four changes sit in order.
        private static double Unwrap(DayTimes t, double hour)
        {
            if (hour < t.B1 - 3) return hour + 24;
            if (hour >= t.B1 + 21) return hour - 24;
            return hour;
        }

        public double KelvinAt(DayTimes t, double hour)
        {
            double u = Unwrap(t, hour);
            double day = settings.DayK, sun = settings.SunsetK, bed = settings.BedK;
            return bed
                + (sun - bed) * Step(u, t.B1)
                + (day - sun) * Step(u, t.B2)
                + (sun - day) * Step(u, t.B3)
                + (bed - sun) * Step(u, t.B4);
        }

        private static double Step(double hour, double at)
        {
            double x = (hour - at + BlendHours / 2) / BlendHours;
            x = Math.Max(0, Math.Min(1, x));
            return x * x * (3 - 2 * x);
        }

        public Period PeriodAt(DayTimes t, double hour)
        {
            double u = Unwrap(t, hour);
            if (u < t.B1 || u >= t.B4) return Period.Night;
            if (u < t.B2) return Period.Morning;
            if (u < t.B3) return Period.Day;
            return Period.Evening;
        }

        public static Tone ToneOf(Period p)
        {
            switch (p)
            {
                case Period.Day: return Tone.Day;
                case Period.Night: return Tone.Bed;
                default: return Tone.Sunset;
            }
        }

        public static string NameOf(Period p)
        {
            switch (p)
            {
                case Period.Morning: return "Morning";
                case Period.Day: return "Daytime";
                case Period.Evening: return "Sunset";
                default: return "Bedtime";
            }
        }

        public static string NameOf(Tone t)
        {
            switch (t)
            {
                case Tone.Day: return "Daytime";
                case Tone.Sunset: return "Sunset";
                default: return "Bedtime";
            }
        }

        public string PresetName()
        {
            foreach (Preset p in Presets)
                if (p.Day == settings.DayK && p.Sunset == settings.SunsetK && p.Bed == settings.BedK) return p.Name;
            return CustomName;
        }

        public string SolarLine(DayTimes t, double hour)
        {
            if (hour >= t.Sunrise && hour < t.Sunset) return "Daylight · sunset in " + FormatDuration(t.Sunset - hour);
            if (hour >= t.Sunset) return "Sunset was " + FormatDuration(hour - t.Sunset) + " ago · wake in " + FormatDuration(Mod24(t.Wake - hour));
            if (hour < t.Wake) return "Night · wake in " + FormatDuration(t.Wake - hour);
            return "Before sunrise · sunrise in " + FormatDuration(t.Sunrise - hour);
        }

        public static double Mod24(double h)
        {
            return ((h % 24) + 24) % 24;
        }

        public static string FormatHour(double h)
        {
            h = Mod24(h);
            int hh = (int)Math.Floor(h);
            int m = (int)Math.Round((h - hh) * 60);
            if (m == 60)
            {
                m = 0;
                hh = (hh + 1) % 24;
            }
            return (hh % 12 == 0 ? 12 : hh % 12) + ":" + m.ToString("00") + (hh >= 12 ? " PM" : " AM");
        }

        public static string FormatDuration(double h)
        {
            h = Math.Max(0, h);
            int hours = (int)Math.Floor(h);
            int minutes = (int)Math.Round((h - hours) * 60);
            if (minutes == 60)
            {
                hours++;
                minutes = 0;
            }
            if (hours == 0) return minutes + " m";
            return minutes == 0 ? hours + " h" : hours + " h " + minutes + " m";
        }
    }
}
