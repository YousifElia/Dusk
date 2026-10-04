using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Dusk.Ui
{
    // Instrument Serif and Hanken Grotesk, embedded in Dusk.exe (SIL Open Font License).
    public static class Fonts
    {
        private static readonly PrivateFontCollection collection = new PrivateFontCollection();
        private static readonly Dictionary<string, Font> cache = new Dictionary<string, Font>();
        private static FontFamily regular, medium, semibold, serif;

        public static void Load()
        {
            if (regular != null) return;
            Assembly asm = typeof(Fonts).Assembly;
            foreach (string name in asm.GetManifestResourceNames())
            {
                if (!name.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)) continue;
                using (Stream s = asm.GetManifestResourceStream(name))
                {
                    var data = new byte[s.Length];
                    s.Read(data, 0, data.Length);
                    // GDI+ keeps using this memory for the life of the process, so it is never freed.
                    IntPtr mem = Marshal.AllocCoTaskMem(data.Length);
                    Marshal.Copy(data, 0, mem, data.Length);
                    collection.AddMemoryFont(mem, data.Length);
                }
            }
            foreach (FontFamily f in collection.Families)
            {
                if (f.Name == "Hanken Grotesk") regular = f;
                else if (f.Name == "Hanken Grotesk Medium") medium = f;
                else if (f.Name == "Hanken Grotesk SemiBold") semibold = f;
                else if (f.Name == "Instrument Serif") serif = f;
            }
            if (regular == null) regular = new FontFamily("Segoe UI");
            if (medium == null) medium = regular;
            if (semibold == null) semibold = regular;
            if (serif == null) serif = new FontFamily("Georgia");
        }

        public static Font Sans(float px)
        {
            return Get(regular, px);
        }

        public static Font Sans(float px, int weight)
        {
            return Get(weight >= 600 ? semibold : weight >= 500 ? medium : regular, px);
        }

        public static Font Serif(float px)
        {
            return Get(serif, px);
        }

        private static Font Get(FontFamily family, float px)
        {
            Load();
            string key = family.Name + "|" + px;
            Font f;
            if (!cache.TryGetValue(key, out f))
            {
                f = new Font(family, px, FontStyle.Regular, GraphicsUnit.Pixel);
                cache[key] = f;
            }
            return f;
        }

        // Pixels above the baseline and below it, from the font's own metrics.
        public static float Ascent(Font f)
        {
            FontFamily fam = f.FontFamily;
            return f.Size * fam.GetCellAscent(FontStyle.Regular) / fam.GetEmHeight(FontStyle.Regular);
        }

        public static float Descent(Font f)
        {
            FontFamily fam = f.FontFamily;
            return f.Size * fam.GetCellDescent(FontStyle.Regular) / fam.GetEmHeight(FontStyle.Regular);
        }

        // CSS "line-height: normal" for this font.
        public static float LineSpacing(Font f)
        {
            FontFamily fam = f.FontFamily;
            return f.Size * fam.GetLineSpacing(FontStyle.Regular) / fam.GetEmHeight(FontStyle.Regular);
        }
    }
}
