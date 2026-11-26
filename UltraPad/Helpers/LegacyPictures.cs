using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace UltraPad.Helpers
{
    // The classic Windows editor stores pasted pictures as metafiles (\wmetafile8 or \emfblip) that only wrap a bitmap.
    // The editor can't draw metafiles, so the bitmap is taken out of them and stored as PNG instead.
    public static class LegacyPictures
    {
        private const string Placeholder = @"{\b [Unsupported picture]}";

        public static async Task<string> ConvertAsync(string rtf)
        {
            if (string.IsNullOrEmpty(rtf) || (rtf.IndexOf(@"\wmetafile", StringComparison.Ordinal) < 0 &&
                                              rtf.IndexOf(@"\emfblip", StringComparison.Ordinal) < 0))
            {
                return rtf;
            }

            var output = new StringBuilder(rtf.Length);
            int i = 0;
            while (i < rtf.Length)
            {
                int pict = FindControlWord(rtf, "pict", i);
                if (pict < 0) break;

                int start = GroupStart(rtf, pict);
                int end = start < 0 ? -1 : GroupEnd(rtf, start);
                if (start < i || end < 0)
                {
                    output.Append(rtf, i, pict + 5 - i);
                    i = pict + 5;
                    continue;
                }

                output.Append(rtf, i, start - i);
                string block = rtf.Substring(start, end - start);
                bool metafile = block.IndexOf(@"\wmetafile", StringComparison.Ordinal) >= 0 ||
                                block.IndexOf(@"\emfblip", StringComparison.Ordinal) >= 0;

                // Word puts a metafile copy in \nonshppict next to the real picture; the editor skips that group anyway
                if (!metafile || InsideGroup(rtf, start, "nonshppict"))
                {
                    output.Append(block);
                }
                else
                {
                    string converted = null;
                    try
                    {
                        converted = await ConvertBlockAsync(block);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("LegacyPictures: " + ex.Message);
                    }
                    output.Append(converted ?? Placeholder);
                }
                i = end;
            }

            if (i < rtf.Length) output.Append(rtf, i, rtf.Length - i);
            return output.ToString();
        }

        private static int FindControlWord(string rtf, string word, int from)
        {
            string token = "\\" + word;
            int index = from;
            while ((index = rtf.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
            {
                int after = index + token.Length;
                bool escaped = index > 0 && rtf[index - 1] == '\\' && (index < 2 || rtf[index - 2] != '\\');
                if (!escaped && (after >= rtf.Length || !char.IsLetter(rtf[after]))) return index;
                index = after;
            }
            return -1;
        }

        // the '{' that opens the group a control word starts, e.g. "{\pict"
        private static int GroupStart(string rtf, int controlWord)
        {
            int j = controlWord - 1;
            while (j >= 0 && char.IsWhiteSpace(rtf[j])) j--;
            return j >= 0 && rtf[j] == '{' ? j : -1;
        }

        private static int GroupEnd(string rtf, int start)
        {
            int depth = 0;
            for (int j = start; j < rtf.Length; j++)
            {
                char c = rtf[j];
                if (c == '\\')
                {
                    j++;
                    continue;
                }
                if (c == '{') depth++;
                else if (c == '}' && --depth == 0) return j + 1;
            }
            return -1;
        }

        private static bool InsideGroup(string rtf, int position, string word)
        {
            int from = 0;
            while (true)
            {
                int found = FindControlWord(rtf, word, from);
                if (found < 0 || found > position) return false;
                int start = GroupStart(rtf, found);
                if (start >= 0)
                {
                    int end = GroupEnd(rtf, start);
                    if (end > position) return true;
                }
                from = found + word.Length + 1;
            }
        }

        private sealed class PictureInfo
        {
            public readonly Dictionary<string, int> Words = new Dictionary<string, int>();
            public byte[] Data;
            public bool Emf;
        }

        private static PictureInfo Parse(string block)
        {
            var info = new PictureInfo();
            var hex = new StringBuilder();
            int j = 1;
            while (j < block.Length - 1)
            {
                char c = block[j];
                if (c == '\\')
                {
                    int k = j + 1;
                    while (k < block.Length && char.IsLetter(block[k])) k++;
                    string name = block.Substring(j + 1, k - j - 1);
                    int numberStart = k;
                    if (k < block.Length && (block[k] == '-' || char.IsDigit(block[k])))
                    {
                        k++;
                        while (k < block.Length && char.IsDigit(block[k])) k++;
                    }
                    if (name.Length == 0)
                    {
                        j = k + 1;
                        continue;
                    }
                    if (name == "bin") return null;
                    if (int.TryParse(block.Substring(numberStart, k - numberStart), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                    {
                        info.Words[name] = value;
                    }
                    if (name == "emfblip") info.Emf = true;
                    if (k < block.Length && block[k] == ' ') k++;
                    j = k;
                }
                else if (c == '{')
                {
                    int end = GroupEnd(block, j);
                    if (end < 0) return null;
                    j = end;
                }
                else
                {
                    if (Uri.IsHexDigit(c)) hex.Append(c);
                    j++;
                }
            }

            if (hex.Length < 2) return null;
            var data = new byte[hex.Length / 2];
            for (int b = 0; b < data.Length; b++)
            {
                data[b] = (byte)((HexValue(hex[2 * b]) << 4) | HexValue(hex[2 * b + 1]));
            }
            info.Data = data;
            return info;
        }

        private static int HexValue(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            return c - 'A' + 10;
        }

        private static async Task<string> ConvertBlockAsync(string block)
        {
            PictureInfo info = Parse(block);
            if (info?.Data == null) return null;

            byte[] dib = info.Emf ? DibFromEmf(info.Data) : DibFromWmf(info.Data);
            if (dib == null) return null;

            byte[] bmp = BmpFromDib(dib);
            if (bmp == null) return null;

            var png = await PngFromBmpAsync(bmp);
            if (png == null) return null;

            int width = png.Item2, height = png.Item3;
            info.Words.TryGetValue("picwgoal", out int goalWidth);
            info.Words.TryGetValue("pichgoal", out int goalHeight);
            if (goalWidth <= 0 || goalHeight <= 0)
            {
                // metafile \picw and \pich are in hundredths of a millimetre
                if (info.Words.TryGetValue("picw", out int w) && info.Words.TryGetValue("pich", out int h) && w > 0 && h > 0 && !info.Emf)
                {
                    goalWidth = (int)Math.Round(w * 1440.0 / 2540.0);
                    goalHeight = (int)Math.Round(h * 1440.0 / 2540.0);
                }
                else
                {
                    goalWidth = width * 15;
                    goalHeight = height * 15;
                }
            }

            var rtf = new StringBuilder(png.Item1.Length * 2 + 200);
            rtf.Append(@"{\pict\pngblip");
            rtf.Append(@"\picw").Append(width).Append(@"\pich").Append(height);
            rtf.Append(@"\picwgoal").Append(goalWidth).Append(@"\pichgoal").Append(goalHeight);
            foreach (string word in new[] { "picscalex", "picscaley", "piccropl", "piccropr", "piccropt", "piccropb" })
            {
                if (info.Words.TryGetValue(word, out int value)) rtf.Append('\\').Append(word).Append(value);
            }
            rtf.Append("\r\n");

            const string digits = "0123456789abcdef";
            byte[] bytes = png.Item1;
            for (int b = 0; b < bytes.Length; b++)
            {
                rtf.Append(digits[bytes[b] >> 4]).Append(digits[bytes[b] & 15]);
                if (b % 64 == 63) rtf.Append("\r\n");
            }
            rtf.Append('}');
            return rtf.ToString();
        }

        private static ushort U16(byte[] d, int o) => (ushort)(d[o] | (d[o + 1] << 8));

        private static uint U32(byte[] d, int o) => (uint)(d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24));

        private static int DibLength(byte[] data, int offset, int available)
        {
            if (available < 12) return -1;
            uint headerSize = U32(data, offset);
            if (headerSize != 12 && headerSize != 40 && headerSize != 52 && headerSize != 56 && headerSize != 108 && headerSize != 124) return -1;
            return available;
        }

        // the largest bitmap drawn by one of the bitmap records of a Windows metafile
        private static byte[] DibFromWmf(byte[] data)
        {
            int pos = 0;
            if (data.Length >= 22 && U32(data, 0) == 0x9AC6CDD7) pos = 22;
            if (data.Length < pos + 18) return null;
            pos += U16(data, pos + 2) * 2;

            byte[] best = null;
            while (pos + 6 <= data.Length)
            {
                long size = U32(data, pos) * 2L;
                ushort function = U16(data, pos + 4);
                if (size < 6 || pos + size > data.Length) break;
                if (function == 0x0000) break;

                int dibOffset = -1;
                switch (function)
                {
                    case 0x0F43: dibOffset = 28; break; // META_STRETCHDIB
                    case 0x0B41: dibOffset = 26; break; // META_DIBSTRETCHBLT
                    case 0x0940: dibOffset = 22; break; // META_DIBBITBLT
                    case 0x0D33: dibOffset = 24; break; // META_SETDIBTODEV
                }

                if (dibOffset > 0 && size > dibOffset)
                {
                    int length = DibLength(data, pos + dibOffset, (int)size - dibOffset);
                    if (length > 0 && (best == null || length > best.Length))
                    {
                        best = new byte[length];
                        System.Buffer.BlockCopy(data, pos + dibOffset, best, 0, length);
                    }
                }
                pos += (int)size;
            }
            return best;
        }

        // the largest bitmap drawn by one of the bitmap records of an enhanced metafile
        private static byte[] DibFromEmf(byte[] data)
        {
            int pos = 0;
            byte[] best = null;
            while (pos + 8 <= data.Length)
            {
                uint type = U32(data, pos);
                long size = U32(data, pos + 4);
                if (size < 8 || pos + size > data.Length) break;

                int headerField = -1;
                switch (type)
                {
                    case 76: // EMR_BITBLT
                    case 77: // EMR_STRETCHBLT
                        headerField = 84;
                        break;
                    case 80: // EMR_SETDIBITSTODEVICE
                    case 81: // EMR_STRETCHDIBITS
                        headerField = 48;
                        break;
                }

                if (headerField > 0 && size >= headerField + 16)
                {
                    uint offHeader = U32(data, pos + headerField);
                    uint cbHeader = U32(data, pos + headerField + 4);
                    uint offBits = U32(data, pos + headerField + 8);
                    uint cbBits = U32(data, pos + headerField + 12);
                    if (cbHeader >= 12 && cbBits > 0 && offHeader + cbHeader <= size && offBits + cbBits <= size)
                    {
                        long length = (long)cbHeader + cbBits;
                        if (DibLength(data, pos + (int)offHeader, (int)cbHeader) > 0 && (best == null || length > best.Length))
                        {
                            best = new byte[length];
                            System.Buffer.BlockCopy(data, pos + (int)offHeader, best, 0, (int)cbHeader);
                            System.Buffer.BlockCopy(data, pos + (int)offBits, best, (int)cbHeader, (int)cbBits);
                        }
                    }
                }

                if (type == 14) break; // EMR_EOF
                pos += (int)size;
            }
            return best;
        }

        // a DIB from a metafile has no file header; adding one makes it a regular .bmp file
        private static byte[] BmpFromDib(byte[] dib)
        {
            uint headerSize = U32(dib, 0);
            int colors;
            int entrySize = headerSize == 12 ? 3 : 4;
            int extra = 0;
            if (headerSize == 12)
            {
                int bits = U16(dib, 10);
                colors = bits <= 8 ? 1 << bits : 0;
            }
            else
            {
                if (dib.Length < 40) return null;
                int bits = U16(dib, 14);
                uint compression = U32(dib, 16);
                uint used = U32(dib, 32);
                colors = used != 0 ? (int)Math.Min(used, 256u) : bits <= 8 ? 1 << bits : 0;
                if (headerSize == 40 && (compression == 3 || compression == 6)) extra = compression == 3 ? 12 : 16; // BI_BITFIELDS masks
            }

            long offset = 14 + headerSize + extra + (long)colors * entrySize;
            if (offset >= dib.Length + 14) return null;

            var bmp = new byte[dib.Length + 14];
            bmp[0] = (byte)'B';
            bmp[1] = (byte)'M';
            WriteU32(bmp, 2, (uint)bmp.Length);
            WriteU32(bmp, 10, (uint)offset);
            System.Buffer.BlockCopy(dib, 0, bmp, 14, dib.Length);
            return bmp;
        }

        private static void WriteU32(byte[] d, int o, uint v)
        {
            d[o] = (byte)v;
            d[o + 1] = (byte)(v >> 8);
            d[o + 2] = (byte)(v >> 16);
            d[o + 3] = (byte)(v >> 24);
        }

        private static async Task<Tuple<byte[], int, int>> PngFromBmpAsync(byte[] bmp)
        {
            using (var input = new InMemoryRandomAccessStream())
            {
                await input.WriteAsync(bmp.AsBuffer());
                input.Seek(0);

                BitmapDecoder decoder = await BitmapDecoder.CreateAsync(BitmapDecoder.BmpDecoderId, input);
                int width = (int)decoder.PixelWidth, height = (int)decoder.PixelHeight;
                if (width <= 0 || height <= 0) return null;

                PixelDataProvider pixels = await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, new BitmapTransform(),
                    ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
                byte[] data = pixels.DetachPixelData();

                // metafile bitmaps have no transparency; the unused byte is often 0
                for (int p = 3; p < data.Length; p += 4) data[p] = 255;

                using (var output = new InMemoryRandomAccessStream())
                {
                    BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
                    encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, (uint)width, (uint)height,
                                         decoder.DpiX > 0 ? decoder.DpiX : 96, decoder.DpiY > 0 ? decoder.DpiY : 96, data);
                    await encoder.FlushAsync();

                    var png = new byte[output.Size];
                    output.Seek(0);
                    using (var reader = new DataReader(output.GetInputStreamAt(0)))
                    {
                        await reader.LoadAsync((uint)output.Size);
                        reader.ReadBytes(png);
                    }
                    return Tuple.Create(png, width, height);
                }
            }
        }
    }
}
