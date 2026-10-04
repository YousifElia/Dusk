using System;
using System.Collections.Generic;
using System.IO;
using Dusk.Ui;

namespace Dusk
{
    // Writes a multi-size .ico (PNG frames) for the exe.
    public static class IconGen
    {
        public static int Main(string[] args)
        {
            string path = args.Length > 0 ? args[0] : "dusk.ico";
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };
            var frames = new List<byte[]>();
            foreach (int size in sizes) frames.Add(IconArt.Png(size));

            using (var w = new BinaryWriter(File.Create(path)))
            {
                w.Write((ushort)0);
                w.Write((ushort)1);
                w.Write((ushort)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)0);
                    w.Write((byte)0);
                    w.Write((ushort)1);
                    w.Write((ushort)32);
                    w.Write(frames[i].Length);
                    w.Write(offset);
                    offset += frames[i].Length;
                }
                foreach (byte[] f in frames) w.Write(f);
            }
            Console.WriteLine("Wrote " + path);
            return 0;
        }
    }
}
