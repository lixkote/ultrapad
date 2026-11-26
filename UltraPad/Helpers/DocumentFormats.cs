using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using UltraPad.Helpers.Formats;

namespace UltraPad.Helpers
{
    public enum DocumentFileFormat { Rtf, Docx, Odt, Text, TextDos, TextUnicode, TextUtf8 }

    public static class DocumentFormats
    {
        static DocumentFormats()
        {
            try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); } catch { }
        }

        public static bool IsText(DocumentFileFormat f) => f == DocumentFileFormat.Text || f == DocumentFileFormat.TextDos || f == DocumentFileFormat.TextUnicode || f == DocumentFileFormat.TextUtf8;

        public static string Extension(DocumentFileFormat f)
        {
            switch (f)
            {
                case DocumentFileFormat.Rtf: return ".rtf";
                case DocumentFileFormat.Docx: return ".docx";
                case DocumentFileFormat.Odt: return ".odt";
                default: return ".txt";
            }
        }

        public static DocumentFileFormat FromExtension(string extension)
        {
            switch ((extension ?? "").ToLowerInvariant())
            {
                case ".docx": return DocumentFileFormat.Docx;
                case ".odt": return DocumentFileFormat.Odt;
                case ".txt": case ".text": case ".log": case ".ini": case ".csv": return DocumentFileFormat.Text;
                default: return DocumentFileFormat.Rtf;
            }
        }

        public static string DisplayName(DocumentFileFormat f)
        {
            switch (f)
            {
                case DocumentFileFormat.Rtf: return Loc.Get("fmt_rtf", "Rich Text Format (RTF)");
                case DocumentFileFormat.Docx: return Loc.Get("fmt_docx", "Office Open XML Document");
                case DocumentFileFormat.Odt: return Loc.Get("fmt_odt", "OpenDocument Text");
                case DocumentFileFormat.TextDos: return Loc.Get("fmt_txtdos", "Text Document - MS-DOS Format");
                case DocumentFileFormat.TextUnicode: return Loc.Get("fmt_txtunicode", "Unicode Text Document");
                case DocumentFileFormat.TextUtf8: return Loc.Get("fmt_txtutf8", "Text Document (UTF-8)");
                default: return Loc.Get("fmt_txt", "Text Document");
            }
        }

        public static Encoding AnsiEncoding()
        {
            try { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage); } catch { }
            try { return Encoding.GetEncoding(1252); } catch { }
            return Encoding.UTF8;
        }

        public static Encoding OemEncoding()
        {
            try { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); } catch { }
            try { return Encoding.GetEncoding(437); } catch { }
            return Encoding.UTF8;
        }

        #region Opening

        public static async Task<string> LoadAsRtfAsync(StorageFile file, DocumentFileFormat format)
        {
            byte[] bytes = await ReadAllBytesAsync(file);
            DocModel doc;
            using (var ms = new MemoryStream(bytes))
            {
                doc = format == DocumentFileFormat.Odt ? OdtConverter.Read(ms) : DocxConverter.Read(ms);
            }
            await ConvertPicturesForEditorAsync(doc);
            return RtfWriter.Write(doc);
        }

        public static async Task<KeyValuePair<string, DocumentFileFormat>> LoadTextAsync(StorageFile file)
        {
            byte[] b = await ReadAllBytesAsync(file);
            if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE)
                return new KeyValuePair<string, DocumentFileFormat>(Encoding.Unicode.GetString(b, 2, b.Length - 2), DocumentFileFormat.TextUnicode);
            if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF)
                return new KeyValuePair<string, DocumentFileFormat>(Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2), DocumentFileFormat.TextUnicode);
            if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF)
                return new KeyValuePair<string, DocumentFileFormat>(Encoding.UTF8.GetString(b, 3, b.Length - 3), DocumentFileFormat.TextUtf8);

            bool ascii = b.All(x => x < 0x80);
            if (!ascii)
            {
                try
                {
                    string utf8 = new UTF8Encoding(false, true).GetString(b);
                    return new KeyValuePair<string, DocumentFileFormat>(utf8, DocumentFileFormat.TextUtf8);
                }
                catch (DecoderFallbackException) { }
            }
            return new KeyValuePair<string, DocumentFileFormat>(AnsiEncoding().GetString(b), DocumentFileFormat.Text);
        }

        private static async Task<byte[]> ReadAllBytesAsync(StorageFile file)
        {
            IBuffer buffer = await FileIO.ReadBufferAsync(file);
            return buffer.ToArray();
        }

        private static async Task ConvertPicturesForEditorAsync(DocModel doc)
        {
            foreach (Paragraph p in doc.Paragraphs)
            {
                for (int i = 0; i < p.Inlines.Count; i++)
                {
                    if (!(p.Inlines[i] is ImageRun img)) continue;
                    if (img.Format == "png" || img.Format == "jpeg" || img.Format == "emf" || img.Format == "wmf") continue;
                    byte[] png = await ToPngAsync(img.Data);
                    if (png != null)
                    {
                        img.Data = png;
                        img.Format = "png";
                    }
                    else
                    {
                        p.Inlines.RemoveAt(i);
                        i--;
                    }
                }
            }
        }

        private static async Task<byte[]> ToPngAsync(byte[] data)
        {
            try
            {
                using (var input = new InMemoryRandomAccessStream())
                {
                    await input.WriteAsync(data.AsBuffer());
                    input.Seek(0);
                    BitmapDecoder decoder = await BitmapDecoder.CreateAsync(input);
                    SoftwareBitmap bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                    using (var output = new InMemoryRandomAccessStream())
                    {
                        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
                        encoder.SetSoftwareBitmap(bitmap);
                        await encoder.FlushAsync();
                        var bytes = new byte[output.Size];
                        output.Seek(0);
                        await output.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, InputStreamOptions.None);
                        return bytes;
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Saving

        public static byte[] Serialize(DocumentFileFormat format, string rtf, string text)
        {
            switch (format)
            {
                case DocumentFileFormat.Docx:
                case DocumentFileFormat.Odt:
                    {
                        DocModel doc = RtfReader.Read(rtf);
                        ApplyPageSetup(doc);
                        using (var ms = new MemoryStream())
                        {
                            if (format == DocumentFileFormat.Docx) DocxConverter.Write(doc, ms);
                            else OdtConverter.Write(doc, ms);
                            return ms.ToArray();
                        }
                    }

                case DocumentFileFormat.Text:
                    return AnsiEncoding().GetBytes(TextForFile(text));
                case DocumentFileFormat.TextDos:
                    return OemEncoding().GetBytes(TextForFile(text));
                case DocumentFileFormat.TextUnicode:
                    return Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(TextForFile(text))).ToArray();
                case DocumentFileFormat.TextUtf8:
                    return new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(TextForFile(text))).ToArray();

                default:
                    return Encoding.UTF8.GetBytes(rtf);
            }
        }

        private static string TextForFile(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (text.EndsWith("\r")) text = text.Substring(0, text.Length - 1);
            return text.Replace("\r\n", "\r").Replace("\n", "\r").Replace("\v", "\r").Replace("\r", "\r\n");
        }

        private static void ApplyPageSetup(DocModel doc)
        {
            try
            {
                PageSettings.Size2 size = PageSettings.GetPageSizeTwips();
                Windows.UI.Xaml.Thickness m = PageSettings.GetMarginsTwips();
                doc.PageWidthTw = (int)Math.Round(size.Width);
                doc.PageHeightTw = (int)Math.Round(size.Height);
                doc.MarginLeftTw = (int)Math.Round(m.Left);
                doc.MarginTopTw = (int)Math.Round(m.Top);
                doc.MarginRightTw = (int)Math.Round(m.Right);
                doc.MarginBottomTw = (int)Math.Round(m.Bottom);
            }
            catch { }
        }

        #endregion
    }
}
