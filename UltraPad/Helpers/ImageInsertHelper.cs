using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Display;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace UltraPad.Helpers
{
    public static class ImageInsertHelper
    {
        private static readonly string[] ImageExtensions =
            { ".png", ".jpg", ".jpeg", ".jfif", ".bmp", ".dib", ".gif", ".tif", ".tiff", ".ico", ".heic", ".webp" };

        public static string[] SupportedExtensions => ImageExtensions;

        public static bool ClipboardHasPictureOnly(DataPackageView content)
        {
            if (content == null) return false;
            if (content.Contains(StandardDataFormats.Rtf)) return false;
            if (content.Contains("PNG") || content.Contains(StandardDataFormats.Bitmap)) return true;
            return false;
        }

        public static bool ClipboardHasImageFiles(DataPackageView content)
        {
            return content != null && content.Contains(StandardDataFormats.StorageItems) &&
                   !content.Contains(StandardDataFormats.Rtf) && !content.Contains(StandardDataFormats.Text);
        }

        public static async Task<bool> PasteFromClipboardAsync(RichEditBox editor)
        {
            try
            {
                DataPackageView content = Clipboard.GetContent();

                if (ClipboardHasPictureOnly(content))
                {
                    IRandomAccessStream stream = null;
                    string type = ".bmp";

                    if (content.Contains("PNG"))
                    {
                        try { stream = await content.GetDataAsync("PNG") as IRandomAccessStream; type = ".png"; }
                        catch { stream = null; }
                    }

                    if (stream == null && content.Contains(StandardDataFormats.Bitmap))
                    {
                        type = ".bmp";
                        RandomAccessStreamReference reference = await content.GetBitmapAsync();
                        stream = await reference.OpenReadAsync();
                    }

                    if (stream == null) return false;
                    bool ok = await InsertAsync(editor, stream, type, selectPicture: false);
                    if (!ok) await ShowLastErrorAsync();
                    return ok;
                }

                if (ClipboardHasImageFiles(content))
                {
                    var items = await content.GetStorageItemsAsync();
                    bool any = false;
                    foreach (var file in items.OfType<StorageFile>())
                    {
                        if (!ImageExtensions.Contains(file.FileType.ToLowerInvariant())) continue;
                        any |= await InsertFileAsync(editor, file, selectPicture: false);
                    }
                    return any;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("ImageInsertHelper.PasteFromClipboardAsync: " + ex.Message);
            }
            return false;
        }

        public static string LastError { get; private set; }

        public static async Task<bool> InsertFileAsync(RichEditBox editor, StorageFile file, bool selectPicture = true)
        {
            if (file == null) return false;
            IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read);
            return await InsertAsync(editor, stream, file.FileType, selectPicture);
        }

        public static async Task<bool> InsertAsync(RichEditBox editor, IRandomAccessStream source, string fileType, bool selectPicture)
        {
            LastError = null;
            uint pixelWidth, pixelHeight;
            BitmapDecoder decoder;

            try
            {
                // The editor keeps the stream it is given and only reads it again when the document is
                // saved. A clipboard stream stops working as soon as something else is copied, so a pasted
                // picture was lost on save unless it was the last one. Keep a private copy instead.
                source = await CopyToMemoryAsync(source);
            }
            catch (Exception ex)
            {
                LastError = "The picture could not be read: " + ex.Message;
                return false;
            }

            try
            {
                source.Seek(0);
                decoder = await BitmapDecoder.CreateAsync(source);
                pixelWidth = decoder.OrientedPixelWidth;
                pixelHeight = decoder.OrientedPixelHeight;
                if (pixelWidth == 0 || pixelHeight == 0)
                {
                    LastError = "The picture is empty.";
                    return false;
                }
            }
            catch (Exception ex)
            {
                LastError = "The picture could not be read: " + ex.Message;
                return false;
            }

            double scale = 1.0;
            try { scale = DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel; } catch { }
            if (scale <= 0) scale = 1.0;

            double width = pixelWidth / scale;
            double height = pixelHeight / scale;

            double maxWidth = GetTextAreaWidth(editor);
            bool downscaled = false;
            if (maxWidth > 16 && width > maxWidth)
            {
                height = height * maxWidth / width;
                width = maxWidth;
                downscaled = true;
            }
            int w = Math.Max(1, (int)Math.Round(width));
            int h = Math.Max(1, (int)Math.Round(height));

            // With display scaling (150% etc.) the size in DIPs is rounded, so a 328 px screenshot was
            // drawn 328.5 px wide and smoothed over half pixels: text in it looked thicker and paler
            // than the original. When the picture isn't shrunk, pick a DIP size that is a whole number
            // of screen pixels and pad the picture with a few transparent pixels to exactly that size,
            // so it is drawn pixel for pixel.
            if (!downscaled && scale > 1.001)
            {
                int alignedW = AlignedDipSize(pixelWidth, scale, out uint paddedW);
                int alignedH = AlignedDipSize(pixelHeight, scale, out uint paddedH);
                if (alignedW > 0 && alignedH > 0)
                {
                    IRandomAccessStream padded = null;
                    if (paddedW != pixelWidth || paddedH != pixelHeight)
                    {
                        padded = await PadPictureAsync(decoder, pixelWidth, pixelHeight, paddedW, paddedH);
                    }
                    if (padded != null)
                    {
                        source = padded;
                        fileType = ".png";
                        w = alignedW;
                        h = alignedH;
                    }
                    else if (paddedW == pixelWidth && paddedH == pixelHeight)
                    {
                        w = alignedW;
                        h = alignedH;
                    }
                }
            }

            string type = (fileType ?? "").ToLowerInvariant();
            bool direct = type == ".png" || type == ".jpg" || type == ".jpeg" || type == ".jfif";

            ITextSelection selection = editor.Document.Selection;
            int start = selection.StartPosition;
            int end = selection.EndPosition;

            Exception firstError = null;
            if (direct)
            {
                try
                {
                    source.Seek(0);
                    selection.InsertImage(w, h, 0, VerticalCharacterAlignment.Baseline, "img", source);
                    return Finish(editor, start, selectPicture);
                }
                catch (Exception ex)
                {
                    firstError = ex;
                    editor.Document.Selection.SetRange(start, end);
                }
            }

            try
            {
                SoftwareBitmap bitmap = await decoder.GetSoftwareBitmapAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied,
                    new BitmapTransform(),
                    ExifOrientationMode.RespectExifOrientation,
                    ColorManagementMode.DoNotColorManage);

                var png = new InMemoryRandomAccessStream();
                BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, png);
                encoder.SetSoftwareBitmap(bitmap);
                await encoder.FlushAsync();
                png.Seek(0);

                editor.Document.Selection.InsertImage(w, h, 0, VerticalCharacterAlignment.Baseline, "img", png);
                return Finish(editor, start, selectPicture);
            }
            catch (Exception ex)
            {
                LastError = (firstError != null ? firstError.Message + "\n" : "") + ex.Message;
                Debug.WriteLine("ImageInsertHelper.InsertAsync: " + LastError);
                return false;
            }
        }

        // the smallest DIP size that covers the picture and is a whole number of screen pixels;
        // returns 0 when that would need more than a few pixels of padding
        private static int AlignedDipSize(uint pixels, double scale, out uint paddedPixels)
        {
            paddedPixels = pixels;
            int dip = Math.Max(1, (int)Math.Ceiling(pixels / scale - 0.0001));
            for (int i = 0; i < 64; i++, dip++)
            {
                double physical = dip * scale;
                double whole = Math.Round(physical);
                if (Math.Abs(physical - whole) > 0.01 || whole < pixels) continue;
                if (whole - pixels > 16) return 0;
                paddedPixels = (uint)whole;
                return dip;
            }
            return 0;
        }

        private static async Task<IRandomAccessStream> PadPictureAsync(BitmapDecoder decoder, uint width, uint height, uint newWidth, uint newHeight)
        {
            try
            {
                PixelDataProvider data = await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied,
                    new BitmapTransform(),
                    ExifOrientationMode.RespectExifOrientation,
                    ColorManagementMode.DoNotColorManage);
                byte[] pixels = data.DetachPixelData();
                if (pixels.Length < (long)width * height * 4) return null;

                byte[] result = new byte[(long)newWidth * newHeight * 4];
                int rowBytes = (int)width * 4;
                for (int y = 0; y < height; y++)
                {
                    System.Array.Copy(pixels, (long)y * rowBytes, result, (long)y * newWidth * 4, rowBytes);
                }

                var stream = new InMemoryRandomAccessStream();
                BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, newWidth, newHeight, 96, 96, result);
                await encoder.FlushAsync();
                stream.Seek(0);
                return stream;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("ImageInsertHelper.PadPictureAsync: " + ex.Message);
                return null;
            }
        }

        private static async Task<IRandomAccessStream> CopyToMemoryAsync(IRandomAccessStream source)
        {
            var copy = new InMemoryRandomAccessStream();
            source.Seek(0);
            using (IInputStream input = source.GetInputStreamAt(0))
            using (IOutputStream output = copy.GetOutputStreamAt(0))
            {
                await RandomAccessStream.CopyAsync(input, output);
                await output.FlushAsync();
            }
            source.Dispose();
            copy.Seek(0);
            return copy;
        }

        private static bool Finish(RichEditBox editor, int start, bool selectPicture)
        {
            editor.Focus(FocusState.Programmatic);

            if (selectPicture)
                editor.Document.Selection.SetRange(start, start + 1);
            else
                editor.Document.Selection.SetRange(start + 1, start + 1);
            return true;
        }

        public static async Task ShowLastErrorAsync()
        {
            try
            {
                await new ContentDialog
                {
                    Title = "UltraPad",
                    Content = Loc.Get("dlg_picturefailed", "The picture could not be inserted.") + (string.IsNullOrEmpty(LastError) ? "" : "\n\n" + LastError),
                    CloseButtonText = Loc.Get("dlg_ok", "OK")
                }.ShowThemedAsync();
            }
            catch
            {
            }
        }

        public static double GetTextAreaWidth(RichEditBox editor)
        {
            double width = editor.ActualWidth;
            if (double.IsNaN(width) || width <= 0) width = editor.Width;
            if (double.IsNaN(width) || width <= 0) return 0;
            width -= editor.Padding.Left + editor.Padding.Right + editor.BorderThickness.Left + editor.BorderThickness.Right;
            return Math.Max(0, width - 20);
        }
    }
}
