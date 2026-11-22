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

namespace WordPad.Helpers
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
            if (maxWidth > 16 && width > maxWidth)
            {
                height = height * maxWidth / width;
                width = maxWidth;
            }
            int w = Math.Max(1, (int)Math.Round(width));
            int h = Math.Max(1, (int)Math.Round(height));

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
