using System;
using Dusk.Ui;

namespace Dusk
{
    public static class Tests
    {
        private static int failures;

        public static int Main(string[] args)
        {
            if (Array.IndexOf(args, "--online") >= 0)
            {
                // Optional: checks the real place lookup (sends "London" to Open-Meteo).
                var done = new System.Threading.ManualResetEvent(false);
                PlaceResult found = null;
                Geocoder.LookUp("London", r => { found = r; done.Set(); });
                bool finished = done.WaitOne(15000);
                Console.WriteLine(finished && found != null
                    ? "Online lookup: " + found.Name + " at " + Geocoder.FormatCoordinates(found.Latitude, found.Longitude)
                    : "Online lookup failed");
                return finished && found != null ? 0 : 1;
            }

            Console.WriteLine("Colour multipliers (R G B %):");
            foreach (int k in new[] { 800, 1200, 1900, 3400, 6500, 9300 })
            {
                double[] m = ColorTemp.Multipliers(k);
                Console.WriteLine(string.Format("  {0,5}K  {1,5:0.0} {2,5:0.0} {3,5:0.0}", k, m[0] * 100, m[1] * 100, m[2] * 100));
            }

            Console.WriteLine("Colour:");
            ColorTests();
            Console.WriteLine("Schedule:");
            ScheduleTests();
            Console.WriteLine("Slider scale:");
            SliderTests();
            Console.WriteLine("Sun:");
            SolarTests();
            Console.WriteLine("Settings:");
            SettingsTests();
            Console.WriteLine("Place:");
            PlaceTests();
            Console.WriteLine("Displays:");
            Console.WriteLine(GammaEngine.Diagnose());

            Console.WriteLine(failures == 0 ? "All tests passed" : failures + " test(s) failed");
            return failures == 0 ? 0 : 1;
        }

        private static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
            if (!ok) failures++;
        }

        private static void Near(double actual, double expected, string what)
        {
            Check(Math.Abs(actual - expected) < 1, what + " (" + Math.Round(actual) + "K)");
        }

        private static void ColorTests()
        {
            double[] w = ColorTemp.Multipliers(6500);
            Check(Math.Abs(w[0] - 1) < 1e-9 && Math.Abs(w[1] - 1) < 1e-9 && Math.Abs(w[2] - 1) < 1e-9, "6500K is exactly white");
            double prev = -1;
            bool rising = true;
            for (int k = ColorTemp.Min; k <= 6500; k += 100)
            {
                double g = ColorTemp.Multipliers(k)[1];
                if (g < prev - 1e-9) rising = false;
                prev = g;
            }
            Check(rising, "green rises steadily from the warmest setting to 6500K");
            Check(ColorTemp.Multipliers(9300)[2] == 1 && ColorTemp.Multipliers(9300)[0] < 0.9, "9300K is blue-white");

            // Past 800K a screen has no redder colour, so Dusk keeps going by dimming the red.
            double[] deepest = ColorTemp.Multipliers(ColorTemp.Min), deepRed = ColorTemp.Multipliers(ColorTemp.DeepRed);
            Check(ColorTemp.Min == 600 && deepRed[0] == 1 && deepest[0] < deepRed[0] && deepest[0] > 0.3,
                "600K is dimmer than 800K (red " + deepest[0].ToString("0.00") + " vs 1.00)");
            Check(deepest[1] == 0 && deepest[2] == 0 && ColorTemp.Multipliers(700)[1] == 0 && deepRed[1] > 0, "green is gone by 700K");
            bool redRising = true;
            double prevRed = -1;
            for (int k = ColorTemp.Min; k <= 1000; k += 50)
            {
                double r = ColorTemp.Multipliers(k)[0];
                if (r < prevRed - 1e-9) redRising = false;
                prevRed = r;
            }
            Check(redRising, "red brightens steadily from 600K to 1000K");
            Check(ColorTemp.DarkroomRed > 0 && ColorTemp.DarkroomRed < deepest[0], "darkroom is darker still");

            // Darkroom inverts the screen: white reads as dim red, and nothing but red is left.
            var darkroom = new[] { ColorTemp.DarkroomRed, 0.0, 0.0 };
            ushort[] inverted = GammaEngine.BuildRamp(darkroom, true);
            Check(inverted[0] > 22000 && inverted[0] < 23500 && inverted[255] == 0,
                "inverted ramp turns white into dim red (" + inverted[0] + " of 65535)");
            Check(inverted[256] == 0 && inverted[511] == 0 && inverted[512] == 0 && inverted[767] == 0, "inverted ramp leaves no green or blue");
            ushort[] plain = GammaEngine.BuildRamp(new[] { 1.0, 1.0, 1.0 }, false);
            Check(plain[0] == 0 && plain[255] == 65535, "a normal ramp passes white through");
            float[] matrix = GammaEngine.BuildMatrix(darkroom, true);
            Check(Math.Abs(matrix[0] + ColorTemp.DarkroomRed) < 1e-6 && Math.Abs(matrix[20] - ColorTemp.DarkroomRed) < 1e-6
                && matrix[6] == 0 && matrix[12] == 0, "inverted colour matrix for HDR screens");
        }

        private static Schedule NewSchedule(double wake)
        {
            var s = new Settings { Wake = wake, SleepHours = 9, DayK = 6500, SunsetK = 3400, BedK = 1900 };
            return new Schedule(s); // no location: sunrise 7:00 AM, sunset 7:00 PM
        }

        private static void ScheduleTests()
        {
            Schedule sch = NewSchedule(6);
            DayTimes t = sch.Times(new DateTime(2026, 9, 14));
            Check(t.Wake == 6 && t.Sunrise == 7 && t.Sunset == 19 && t.Bed == 21, "wake 6, sunrise 7, sunset 7 PM, bedtime 9 hours before wake");
            Near(sch.KelvinAt(t, 12), 6500, "noon is daytime");
            Near(sch.KelvinAt(t, 3), 1900, "3 AM is bedtime");
            Near(sch.KelvinAt(t, 1), 1900, "1 AM, past midnight, is still bedtime");
            Near(sch.KelvinAt(t, 6), 2650, "halfway into the morning change at wake time");
            Near(sch.KelvinAt(t, 6.5), 3400, "morning uses the sunset colour");
            Near(sch.KelvinAt(t, 7), 4950, "halfway into daytime at sunrise");
            Near(sch.KelvinAt(t, 19), 4950, "halfway into sunset colours at sunset");
            Near(sch.KelvinAt(t, 20), 3400, "evening is sunset colour");
            Near(sch.KelvinAt(t, 21), 2650, "halfway into bedtime colours at bedtime");
            Check(sch.PeriodAt(t, 3) == Period.Night && sch.PeriodAt(t, 6.5) == Period.Morning && sch.PeriodAt(t, 12) == Period.Day
                && sch.PeriodAt(t, 20) == Period.Evening && sch.PeriodAt(t, 22) == Period.Night, "periods across the day");

            Schedule late = NewSchedule(8); // wakes after sunrise: no morning period
            DayTimes lt = late.Times(new DateTime(2026, 9, 14));
            Near(late.KelvinAt(lt, 7.5), 1900, "before a late wake it's still bedtime, even after sunrise");
            Near(late.KelvinAt(lt, 8), 4200, "a late wake goes straight from bedtime to daytime");
            Check(late.PeriodAt(lt, 7.9) == Period.Night && late.PeriodAt(lt, 9) == Period.Day, "late wake skips morning");

            Schedule noon = NewSchedule(12); // latest allowed wake: bedtime crosses midnight
            DayTimes nt = noon.Times(new DateTime(2026, 9, 14));
            Check(nt.Bed == 3, "a noon wake gives a 3 AM bedtime (" + nt.Bed + ")");
            Near(noon.KelvinAt(nt, 1), 3400, "1 AM is still evening for a noon wake");
            Near(noon.KelvinAt(nt, 5), 1900, "5 AM is bedtime for a noon wake");

            Check(Schedule.FormatHour(21.37) == "9:22 PM" && Schedule.FormatHour(0) == "12:00 AM" && Schedule.FormatHour(5.75) == "5:45 AM",
                "time labels (" + Schedule.FormatHour(21.37) + ")");
            Check(Schedule.FormatDuration(3.0833) == "3 h 5 m" && Schedule.FormatDuration(0.5) == "30 m" && Schedule.FormatDuration(2) == "2 h",
                "duration labels (" + Schedule.FormatDuration(3.0833) + ")");
            Check(sch.PresetName() == "Recommended colors", "default colours are the recommended preset");
            sch.Settings.BedK = 1500;
            Check(sch.PresetName() == Schedule.CustomName, "changed colours are custom");
        }

        private static void SliderTests()
        {
            bool roundTrip = true;
            foreach (int k in new[] { 800, 1200, 1900, 3400, 6500, 7500, 9300 })
                if (Palette.PositionToKelvin(Palette.KelvinToPosition(k)) != k) roundTrip = false;
            Check(roundTrip, "slider positions round-trip to the same temperature");
            Check(Math.Abs(Palette.KelvinToPosition(6500) - 0.8) < 1e-9, "6500K sits at 80% of the track");
            Check(Palette.PositionToKelvin(0.805) == 6500, "positions near 6500K snap to no change");
        }

        private static void SolarTests()
        {
            double rise, set;
            bool ok = Solar.SunTimes(new DateTime(2026, 9, 13), 42.33, -83.05, out rise, out set);
            Check(ok, "sun times found for Detroit");
            if (TimeZoneInfo.Local.Id == "Eastern Standard Time")
            {
                Check(Math.Abs(rise - (7 * 60 + 14)) <= 6, "sunrise is about 7:14 AM");
                Check(Math.Abs(set - (19 * 60 + 48)) <= 6, "sunset is about 7:48 PM");
            }
            double r2, s2;
            Check(!Solar.SunTimes(new DateTime(2026, 6, 21), 80, 0, out r2, out s2), "no sunset in arctic summer");
        }

        private static void SettingsTests()
        {
            var s = new Settings
            {
                SetupDone = true,
                Place = "Ann Arbor, Michigan",
                HasLocation = true,
                Latitude = 42.2776,
                Longitude = -83.7409,
                Wake = 5.75,
                BedK = 1200,
                View = "strata",
                Theme = "dark",
                Darkroom = true
            };
            s.ExcludedApps.Add("vlc");
            Settings back = Settings.Parse(s.Serialize());
            Check(back.SetupDone && back.Place == s.Place && back.HasLocation && Math.Abs(back.Latitude - 42.2776) < 1e-9
                && back.Wake == 5.75 && back.BedK == 1200 && back.View == "strata" && back.Theme == "dark" && back.Darkroom
                && back.ExcludedApps.Count == 1 && back.ExcludedApps[0] == "vlc", "settings survive a save and load");

            Settings broken = Settings.Parse("wake=banana\nday=99999\nview=nope\ntheme=\n");
            Check(broken.Wake == 7 && broken.DayK == ColorTemp.Max && broken.View == "horizon" && broken.Theme == "light", "bad settings are repaired");
        }

        private static void PlaceTests()
        {
            double lat, lon;
            Check(Geocoder.TryParseCoordinates("42.2, -83.2", out lat, out lon) && lat == 42.2 && lon == -83.2, "decimal coordinates");
            Check(Geocoder.TryParseCoordinates("42.2°N 83.2°W", out lat, out lon) && lat == 42.2 && lon == -83.2, "coordinates with N/W");
            Check(!Geocoder.TryParseCoordinates("Ann Arbor", out lat, out lon), "a city name isn't coordinates");
            Check(!Geocoder.TryParseCoordinates("95, 10", out lat, out lon), "out-of-range latitude is rejected");
            Check(Geocoder.FormatCoordinates(42.2, -83.19) == "42.2°N, 83.2°W", "coordinates label");

            PlaceResult r = Geocoder.Parse("{\"results\":[{\"id\":1,\"name\":\"Ann Arbor\",\"latitude\":42.27756,\"longitude\":-83.74088,"
                + "\"country\":\"United States\",\"admin1\":\"Michigan\",\"postcodes\":[\"48104\"]}],\"generationtime_ms\":0.5}");
            Check(r != null && r.Name == "Ann Arbor, Michigan" && Math.Abs(r.Latitude - 42.27756) < 1e-9 && Math.Abs(r.Longitude + 83.74088) < 1e-9,
                "lookup result is read");
            Check(Geocoder.Parse("{\"generationtime_ms\":0.3}") == null, "no results means no place");
        }
    }
}
