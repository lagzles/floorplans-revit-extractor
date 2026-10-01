using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace FloorplansRevitExtractor.Core.Png
{
    /// <summary>
    /// Reads the plan image Revit exports, without an image library: 8-bit,
    /// non-interlaced PNG in gray, gray+alpha, RGB, RGBA or palette, all five
    /// row filters, chunk CRCs and the zlib checksum verified. Always returns
    /// RGB, alpha composited over white paper, so every sample's image is
    /// the dataset's PNG RGB 8-bit whatever Revit wrote.
    /// </summary>
    public static class PngDecoder
    {
        private const byte White = 255;

        /// <summary>
        /// Twice the spec's largest image (8,000 px a side): a header above this
        /// is refused before inflating, so a tiny file cannot claim gigabytes.
        /// </summary>
        public const long MaxPixels = 128_000_000;

        private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        public static RgbImage DecodeRgb(byte[] png)
        {
            Header header = default;
            byte[]? palette = null;
            byte[]? transparency = null;
            using var idat = new MemoryStream();

            RequireSignature(png);
            int offset = Signature.Length;
            bool ended = false;
            while (!ended)
            {
                (string type, byte[] data, int next) = ReadChunk(png, offset);
                switch (type)
                {
                    case "IHDR":
                        header = Header.Parse(data);
                        break;
                    case "PLTE":
                        palette = data;
                        break;
                    case "tRNS":
                        transparency = data;
                        break;
                    case "IDAT":
                        idat.Write(data, 0, data.Length);
                        break;
                    case "IEND":
                        ended = true;
                        break;
                }

                offset = next;
            }

            if (header.Width == 0)
            {
                throw new InvalidDataException("PNG has no IHDR chunk.");
            }

            byte[] pixels = Unfilter(Inflate(idat.ToArray(), header), header);
            return new RgbImage(header.Width, header.Height, ToRgb(pixels, header, palette, transparency));
        }

        private static void RequireSignature(byte[] png)
        {
            if (png.Length < Signature.Length)
            {
                throw new InvalidDataException("Not a PNG: too short.");
            }

            for (int i = 0; i < Signature.Length; i++)
            {
                if (png[i] != Signature[i])
                {
                    throw new InvalidDataException("Not a PNG: bad signature.");
                }
            }
        }

        private static (string Type, byte[] Data, int Next) ReadChunk(byte[] png, int offset)
        {
            if (offset + 12 > png.Length)
            {
                throw new InvalidDataException("PNG ends before its IEND chunk.");
            }

            int length = ReadInt32(png, offset);
            if (length < 0 || offset + 12L + length > png.Length)
            {
                throw new InvalidDataException($"PNG chunk at byte {offset} runs past the end of the file.");
            }

            string type = Encoding.ASCII.GetString(png, offset + 4, 4);
            var data = new byte[length];
            Buffer.BlockCopy(png, offset + 8, data, 0, length);
            uint stored = (uint)ReadInt32(png, offset + 8 + length);
            if (Checksums.Crc32(png, offset + 4, 4, data) != stored)
            {
                throw new InvalidDataException($"PNG chunk {type} at byte {offset} fails its CRC check.");
            }

            return (type, data, offset + 12 + length);
        }

        /// <summary>Inflates into one buffer of exactly the size the header implies; any other size is an error.</summary>
        private static byte[] Inflate(byte[] zlib, Header header)
        {
            if (zlib.Length < 6 || (zlib[0] & 0x0F) != 8 || (((zlib[0] << 8) | zlib[1]) % 31) != 0)
            {
                throw new InvalidDataException("PNG image data is not a zlib stream.");
            }

            var raw = new byte[header.Height * (((long)header.Width * header.BytesPerPixel) + 1)];
            int filled = 0;
            using (var deflate = new DeflateStream(new MemoryStream(zlib, 2, zlib.Length - 6), CompressionMode.Decompress))
            {
                int read;
                while (filled < raw.Length && (read = deflate.Read(raw, filled, raw.Length - filled)) > 0)
                {
                    filled += read;
                }

                if (filled < raw.Length || deflate.ReadByte() >= 0)
                {
                    throw new InvalidDataException(
                        $"PNG image data does not inflate to the {raw.Length} bytes a {header.Width}x{header.Height} image needs.");
                }
            }

            if ((uint)ReadInt32(zlib, zlib.Length - 4) != Checksums.Adler32(raw))
            {
                throw new InvalidDataException("PNG image data fails its Adler-32 check.");
            }

            return raw;
        }

        private static byte[] Unfilter(byte[] raw, Header header)
        {
            int bpp = header.BytesPerPixel;
            int stride = header.Width * bpp;
            if (raw.Length != (long)header.Height * (stride + 1))
            {
                throw new InvalidDataException($"PNG image data has {raw.Length} bytes; {header.Width}x{header.Height} needs {(long)header.Height * (stride + 1)}.");
            }

            var pixels = new byte[header.Height * stride];
            for (int y = 0; y < header.Height; y++)
            {
                byte filter = raw[y * (stride + 1)];
                int src = (y * (stride + 1)) + 1;
                int dst = y * stride;
                int up = dst - stride;
                for (int x = 0; x < stride; x++)
                {
                    int a = x >= bpp ? pixels[dst + x - bpp] : 0;
                    int b = y > 0 ? pixels[up + x] : 0;
                    int c = x >= bpp && y > 0 ? pixels[up + x - bpp] : 0;
                    pixels[dst + x] = (byte)(raw[src + x] + Predict(filter, a, b, c, y));
                }
            }

            return pixels;
        }

        private static int Predict(byte filter, int a, int b, int c, int row)
        {
            switch (filter)
            {
                case 0:
                    return 0;
                case 1:
                    return a;
                case 2:
                    return b;
                case 3:
                    return (a + b) / 2;
                case 4:
                    int p = a + b - c;
                    int pa = Math.Abs(p - a);
                    int pb = Math.Abs(p - b);
                    int pc = Math.Abs(p - c);
                    return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
                default:
                    throw new InvalidDataException($"PNG row {row} has unknown filter type {filter}.");
            }
        }

        private static byte[] ToRgb(byte[] pixels, Header header, byte[]? palette, byte[]? transparency)
        {
            if (header.ColorType == 2 && transparency == null)
            {
                return pixels; // already RGB: no full-image copy
            }

            int count = header.Width * header.Height;
            var rgb = new byte[count * 3];
            for (int i = 0; i < count; i++)
            {
                int o = i * 3;
                switch (header.ColorType)
                {
                    case 0:
                        rgb[o] = rgb[o + 1] = rgb[o + 2] = IsKeyColor(transparency, pixels[i]) ? White : pixels[i];
                        break;
                    case 2:
                        bool key = IsKeyColor(transparency, pixels[i * 3], pixels[(i * 3) + 1], pixels[(i * 3) + 2]);
                        rgb[o] = key ? White : pixels[i * 3];
                        rgb[o + 1] = key ? White : pixels[(i * 3) + 1];
                        rgb[o + 2] = key ? White : pixels[(i * 3) + 2];
                        break;
                    case 3:
                        int entry = pixels[i] * 3;
                        if (palette == null || entry + 2 >= palette.Length)
                        {
                            throw new InvalidDataException($"PNG palette index {pixels[i]} has no PLTE entry.");
                        }

                        byte paletteAlpha = transparency != null && pixels[i] < transparency.Length ? transparency[pixels[i]] : (byte)255;
                        rgb[o] = OverWhite(palette[entry], paletteAlpha);
                        rgb[o + 1] = OverWhite(palette[entry + 1], paletteAlpha);
                        rgb[o + 2] = OverWhite(palette[entry + 2], paletteAlpha);
                        break;
                    case 4:
                        byte g = OverWhite(pixels[i * 2], pixels[(i * 2) + 1]);
                        rgb[o] = rgb[o + 1] = rgb[o + 2] = g;
                        break;
                    case 6:
                        byte alpha = pixels[(i * 4) + 3];
                        rgb[o] = OverWhite(pixels[i * 4], alpha);
                        rgb[o + 1] = OverWhite(pixels[(i * 4) + 1], alpha);
                        rgb[o + 2] = OverWhite(pixels[(i * 4) + 2], alpha);
                        break;
                }
            }

            return rgb;
        }

        /// <summary>
        /// tRNS on gray or true color names one transparent value, as 16-bit
        /// samples; at bit depth 8 only the low byte is used.
        /// </summary>
        private static bool IsKeyColor(byte[]? transparency, params byte[] samples)
        {
            if (transparency == null || transparency.Length != samples.Length * 2)
            {
                return false;
            }

            for (int s = 0; s < samples.Length; s++)
            {
                if (transparency[(s * 2) + 1] != samples[s])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>A transparent export pixel is paper: composite over white.</summary>
        private static byte OverWhite(byte value, byte alpha)
        {
            double a = alpha / 255.0;
            return (byte)Math.Floor((value * a) + (White * (1 - a)) + 0.5);
        }

        private static int ReadInt32(byte[] b, int i) => (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3];

        private readonly struct Header
        {
            private Header(int width, int height, byte colorType)
            {
                Width = width;
                Height = height;
                ColorType = colorType;
            }

            internal int Width { get; }

            internal int Height { get; }

            internal byte ColorType { get; }

            internal int BytesPerPixel => ColorType switch
            {
                0 => 1,
                2 => 3,
                3 => 1,
                4 => 2,
                _ => 4,
            };

            internal static Header Parse(byte[] data)
            {
                if (data.Length != 13)
                {
                    throw new InvalidDataException($"PNG IHDR has {data.Length} bytes; it must have 13.");
                }

                int width = ReadInt32(data, 0);
                int height = ReadInt32(data, 4);
                byte bitDepth = data[8];
                byte colorType = data[9];
                if (width <= 0 || height <= 0)
                {
                    throw new InvalidDataException($"PNG size {width}x{height} is invalid.");
                }

                if (bitDepth != 8)
                {
                    throw new InvalidDataException($"Only 8-bit PNG images are read; this one is {bitDepth}-bit.");
                }

                if (colorType != 0 && colorType != 2 && colorType != 3 && colorType != 4 && colorType != 6)
                {
                    throw new InvalidDataException($"PNG color type {colorType} is invalid.");
                }

                if (data[10] != 0 || data[11] != 0)
                {
                    throw new InvalidDataException("PNG compression and filter methods must be 0.");
                }

                if (data[12] != 0)
                {
                    throw new InvalidDataException("Interlaced PNG images are not read.");
                }

                if ((long)width * height > MaxPixels)
                {
                    throw new InvalidDataException(
                        $"PNG declares {width}x{height} = {(long)width * height} pixels; at most {MaxPixels} pixels are read.");
                }

                return new Header(width, height, colorType);
            }
        }
    }
}
