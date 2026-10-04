using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media.Imaging;

namespace DuskTools
{
    // Builds an animated GIF out of PNG frames. A development tool for the README's demos;
    // it is not part of Dusk.exe. Windows' own GIF encoder writes one frame at a time, so each
    // frame is encoded separately and the pieces are reassembled into one looping GIF89a.
    public static class MakeGif
    {
        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("usage: MakeGif out.gif delayCentiseconds frame1.png frame2.png ...");
                return 1;
            }
            string output = args[0];
            int delay = int.Parse(args[1]);

            using (FileStream file = File.Create(output))
            {
                bool first = true;
                for (int i = 2; i < args.Length; i++)
                {
                    Frame frame = Parse(EncodeOneFrame(args[i]));
                    if (first)
                    {
                        WriteHeader(file, frame);
                        first = false;
                    }
                    WriteFrame(file, frame, delay);
                }
                file.WriteByte(0x3B); // trailer
            }
            Console.WriteLine("Wrote " + output + " (" + (args.Length - 2) + " frames, " + new FileInfo(output).Length / 1024 + " KB)");
            return 0;
        }

        private sealed class Frame
        {
            public int Width, Height;
            public byte[] Palette; // 3 bytes per colour
            public byte LzwMinimumCodeSize;
            public byte[] Data;    // LZW sub-blocks, including the terminating zero
        }

        private static byte[] EncodeOneFrame(string pngPath)
        {
            BitmapSource image;
            using (FileStream fs = File.OpenRead(pngPath))
                image = new PngBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            var encoder = new GifBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var ms = new MemoryStream())
            {
                encoder.Save(ms);
                return ms.ToArray();
            }
        }

        private static Frame Parse(byte[] gif)
        {
            var frame = new Frame();
            int i = 6; // skip "GIF89a"
            frame.Width = gif[i] | (gif[i + 1] << 8);
            frame.Height = gif[i + 2] | (gif[i + 3] << 8);
            int packed = gif[i + 4];
            i += 7;

            byte[] globalPalette = null;
            if ((packed & 0x80) != 0)
            {
                int size = 3 * (1 << ((packed & 7) + 1));
                globalPalette = new byte[size];
                Array.Copy(gif, i, globalPalette, 0, size);
                i += size;
            }

            while (i < gif.Length)
            {
                byte block = gif[i++];
                if (block == 0x21) // extension: skip it
                {
                    i++; // label
                    while (gif[i] != 0) i += gif[i] + 1;
                    i++;
                }
                else if (block == 0x2C) // image descriptor
                {
                    i += 8; // position and size, already known from the screen descriptor
                    int imagePacked = gif[i++];
                    if ((imagePacked & 0x80) != 0)
                    {
                        int size = 3 * (1 << ((imagePacked & 7) + 1));
                        frame.Palette = new byte[size];
                        Array.Copy(gif, i, frame.Palette, 0, size);
                        i += size;
                    }
                    else
                    {
                        frame.Palette = globalPalette;
                    }
                    frame.LzwMinimumCodeSize = gif[i++];
                    int start = i;
                    while (gif[i] != 0) i += gif[i] + 1;
                    i++; // the terminating zero
                    frame.Data = new byte[i - start];
                    Array.Copy(gif, start, frame.Data, 0, frame.Data.Length);
                    return frame;
                }
                else
                {
                    break;
                }
            }
            throw new InvalidDataException("No image data found in the encoded frame.");
        }

        private static void WriteHeader(Stream output, Frame frame)
        {
            foreach (char c in "GIF89a") output.WriteByte((byte)c);
            WriteShort(output, frame.Width);
            WriteShort(output, frame.Height);
            output.WriteByte(0x70); // no global palette; each frame carries its own
            output.WriteByte(0);
            output.WriteByte(0);

            // Netscape extension: loop forever
            output.WriteByte(0x21);
            output.WriteByte(0xFF);
            output.WriteByte(11);
            foreach (char c in "NETSCAPE2.0") output.WriteByte((byte)c);
            output.WriteByte(3);
            output.WriteByte(1);
            WriteShort(output, 0);
            output.WriteByte(0);
        }

        private static void WriteFrame(Stream output, Frame frame, int delayCentiseconds)
        {
            // Graphic control extension: how long this frame stays on screen
            output.WriteByte(0x21);
            output.WriteByte(0xF9);
            output.WriteByte(4);
            output.WriteByte(0x04); // leave the frame in place, no transparency
            WriteShort(output, delayCentiseconds);
            output.WriteByte(0);
            output.WriteByte(0);

            int colours = frame.Palette.Length / 3, bits = 0;
            while ((1 << (bits + 1)) < colours) bits++;

            output.WriteByte(0x2C);
            WriteShort(output, 0);
            WriteShort(output, 0);
            WriteShort(output, frame.Width);
            WriteShort(output, frame.Height);
            output.WriteByte((byte)(0x80 | bits)); // this frame has its own palette
            output.Write(frame.Palette, 0, frame.Palette.Length);
            output.WriteByte(frame.LzwMinimumCodeSize);
            output.Write(frame.Data, 0, frame.Data.Length);
        }

        private static void WriteShort(Stream output, int value)
        {
            output.WriteByte((byte)(value & 0xFF));
            output.WriteByte((byte)((value >> 8) & 0xFF));
        }
    }
}
