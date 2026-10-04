using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;

namespace Dusk
{
    public sealed class PlaceResult
    {
        public string Name;
        public double Latitude, Longitude;
    }

    // Turns what you type in "Where are you?" into coordinates.
    // Coordinates are read directly; place names are looked up online with Open-Meteo's free geocoding service.
    public static class Geocoder
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly Regex Coordinates = new Regex(
            @"^\s*([+-]?\d+(?:\.\d+)?)\s*°?\s*([NnSs])?\s*[,;\s]\s*([+-]?\d+(?:\.\d+)?)\s*°?\s*([EeWw])?\s*$");

        public static bool TryParseCoordinates(string text, out double latitude, out double longitude)
        {
            latitude = longitude = 0;
            Match m = Coordinates.Match(text ?? "");
            if (!m.Success) return false;
            latitude = double.Parse(m.Groups[1].Value, Inv);
            longitude = double.Parse(m.Groups[3].Value, Inv);
            if (m.Groups[2].Success && char.ToUpperInvariant(m.Groups[2].Value[0]) == 'S') latitude = -Math.Abs(latitude);
            if (m.Groups[4].Success && char.ToUpperInvariant(m.Groups[4].Value[0]) == 'W') longitude = -Math.Abs(longitude);
            return Math.Abs(latitude) <= 90 && Math.Abs(longitude) <= 180;
        }

        public static string FormatCoordinates(double latitude, double longitude)
        {
            return string.Format(Inv, "{0:0.0}°{1}, {2:0.0}°{3}",
                Math.Abs(latitude), latitude >= 0 ? "N" : "S", Math.Abs(longitude), longitude >= 0 ? "E" : "W");
        }

        // Runs on a worker thread; calls done with null if nothing was found or the lookup failed.
        public static void LookUp(string query, Action<PlaceResult> done)
        {
            ThreadPool.QueueUserWorkItem(state =>
            {
                PlaceResult result = null;
                try
                {
                    ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
                    var request = (HttpWebRequest)WebRequest.Create(
                        "https://geocoding-api.open-meteo.com/v1/search?count=1&language=en&format=json&name=" + Uri.EscapeDataString(query.Trim()));
                    request.Timeout = 8000;
                    request.UserAgent = "Dusk";
                    string json;
                    using (WebResponse response = request.GetResponse())
                    using (var reader = new StreamReader(response.GetResponseStream()))
                        json = reader.ReadToEnd();
                    result = Parse(json);
                }
                catch (Exception)
                {
                    result = null;
                }
                done(result);
            });
        }

        public static PlaceResult Parse(string json)
        {
            int start = json.IndexOf("\"results\"", StringComparison.Ordinal);
            if (start < 0) return null;
            int open = json.IndexOf('{', start);
            int close = open < 0 ? -1 : json.IndexOf('}', open);
            if (close < 0) return null;
            string first = json.Substring(open, close - open);

            string name = Field(first, "name"), region = Field(first, "admin1"), country = Field(first, "country");
            Match lat = Regex.Match(first, "\"latitude\"\\s*:\\s*(-?[\\d.]+)");
            Match lon = Regex.Match(first, "\"longitude\"\\s*:\\s*(-?[\\d.]+)");
            if (name == null || !lat.Success || !lon.Success) return null;

            string extra = region != null && region != name ? region : country;
            return new PlaceResult
            {
                Name = extra != null ? name + ", " + extra : name,
                Latitude = double.Parse(lat.Groups[1].Value, Inv),
                Longitude = double.Parse(lon.Groups[1].Value, Inv)
            };
        }

        private static string Field(string json, string key)
        {
            Match m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            return m.Success ? Regex.Unescape(m.Groups[1].Value) : null;
        }
    }
}
