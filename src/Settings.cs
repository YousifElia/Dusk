using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Dusk
{
    // Saved as simple key=value lines in %APPDATA%\Dusk\settings.ini.
    public class Settings
    {
        public const double MinWake = 3, MaxWake = 12;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public bool SetupDone;
        public string Place = "";
        public bool HasLocation;
        public double Latitude, Longitude;
        public double Wake = 7;
        public double SleepHours = 9;
        public int DayK = 6500, SunsetK = 3400, BedK = 1900;
        public string View = "horizon";
        public string Theme = "light";
        public bool Darkroom;
        public List<string> ExcludedApps = new List<string>();

        // Preview and snapshot runs never write the settings file.
        public bool DoNotSave;

        public static string FolderPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dusk"); }
        }

        public static string FilePath
        {
            get { return Path.Combine(FolderPath, "settings.ini"); }
        }

        // First run: start from f.lux's location and wake time if they're on this PC.
        public static Settings CreateDefault()
        {
            var s = new Settings();
            double lat, lon, wake;
            if (SystemInfo.TryFluxLocation(out lat, out lon))
            {
                s.HasLocation = true;
                s.Latitude = lat;
                s.Longitude = lon;
            }
            if (SystemInfo.TryFluxWake(out wake)) s.Wake = wake;
            s.Normalize();
            return s;
        }

        public static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath)) return Parse(File.ReadAllText(FilePath));
            }
            catch (Exception)
            {
                // Unreadable settings fall back to first-run defaults rather than blocking startup.
            }
            return CreateDefault();
        }

        public static Settings Parse(string text)
        {
            var s = new Settings();
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                int eq = line.IndexOf('=');
                if (eq <= 0 || line.StartsWith("#")) continue;
                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string v = line.Substring(eq + 1).Trim();
                switch (key)
                {
                    case "setup": s.SetupDone = v == "1"; break;
                    case "place": s.Place = v; break;
                    case "haslocation": s.HasLocation = v == "1"; break;
                    case "latitude": s.Latitude = Num(v, 0); break;
                    case "longitude": s.Longitude = Num(v, 0); break;
                    case "wake": s.Wake = Num(v, 7); break;
                    case "sleephours": s.SleepHours = Num(v, 9); break;
                    case "day": s.DayK = (int)Num(v, 6500); break;
                    case "sunset": s.SunsetK = (int)Num(v, 3400); break;
                    case "bed": s.BedK = (int)Num(v, 1900); break;
                    case "view": s.View = v; break;
                    case "theme": s.Theme = v; break;
                    case "darkroom": s.Darkroom = v == "1"; break;
                    case "excludedapps":
                        foreach (string app in v.Split('|'))
                            if (app.Trim().Length > 0) s.ExcludedApps.Add(app.Trim().ToLowerInvariant());
                        break;
                }
            }
            s.Normalize();
            return s;
        }

        public string Serialize()
        {
            var sb = new StringBuilder();
            sb.Append("# Dusk settings\r\n");
            Line(sb, "setup", SetupDone ? "1" : "0");
            Line(sb, "place", Place.Replace("\r", " ").Replace("\n", " "));
            Line(sb, "hasLocation", HasLocation ? "1" : "0");
            Line(sb, "latitude", Latitude.ToString("0.####", Inv));
            Line(sb, "longitude", Longitude.ToString("0.####", Inv));
            Line(sb, "wake", Wake.ToString("0.##", Inv));
            Line(sb, "sleepHours", SleepHours.ToString("0.##", Inv));
            Line(sb, "day", DayK.ToString(Inv));
            Line(sb, "sunset", SunsetK.ToString(Inv));
            Line(sb, "bed", BedK.ToString(Inv));
            Line(sb, "view", View);
            Line(sb, "theme", Theme);
            Line(sb, "darkroom", Darkroom ? "1" : "0");
            Line(sb, "excludedApps", string.Join("|", ExcludedApps.ToArray()));
            return sb.ToString();
        }

        public void Save()
        {
            if (DoNotSave) return;
            try
            {
                Directory.CreateDirectory(FolderPath);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, Serialize());
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch (Exception)
            {
                // Saving is best-effort; Dusk keeps running with the settings in memory.
            }
        }

        public void Normalize()
        {
            if (Place == null) Place = "";
            if (ExcludedApps == null) ExcludedApps = new List<string>();
            Latitude = Math.Max(-90, Math.Min(90, Latitude));
            Longitude = Math.Max(-180, Math.Min(180, Longitude));
            Wake = Math.Max(MinWake, Math.Min(MaxWake, Math.Round(Wake * 4) / 4));
            SleepHours = Math.Max(4, Math.Min(12, SleepHours));
            DayK = ColorTemp.Clamp(DayK, ColorTemp.Min, ColorTemp.Max);
            SunsetK = ColorTemp.Clamp(SunsetK, ColorTemp.Min, ColorTemp.Max);
            BedK = ColorTemp.Clamp(BedK, ColorTemp.Min, ColorTemp.Max);
            if (View != "horizon" && View != "dial" && View != "strata") View = "horizon";
            if (Theme != "light" && Theme != "dark") Theme = "light";
        }

        public int GetTemp(Tone t)
        {
            return t == Tone.Day ? DayK : t == Tone.Sunset ? SunsetK : BedK;
        }

        public void SetTemp(Tone t, int kelvin)
        {
            kelvin = ColorTemp.Clamp(kelvin, ColorTemp.Min, ColorTemp.Max);
            if (t == Tone.Day) DayK = kelvin;
            else if (t == Tone.Sunset) SunsetK = kelvin;
            else BedK = kelvin;
        }

        private static void Line(StringBuilder sb, string key, string value)
        {
            sb.Append(key).Append('=').Append(value).Append("\r\n");
        }

        private static double Num(string v, double fallback)
        {
            double d;
            return double.TryParse(v, NumberStyles.Float, Inv, out d) && !double.IsNaN(d) && !double.IsInfinity(d) ? d : fallback;
        }
    }
}
