using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics.Display;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace UltraPad.Helpers
{
    public enum InsertObjectKind { PaintPicture, BitmapImage, Table, Package }

    public sealed class EmbeddedObjectManager
    {
        private const string PaintPackageFamilyName = "Microsoft.Paint_8wekyb3d8bbwe";
        private const char ObjectChar = '￼';

        private static readonly ConditionalWeakTable<RichEditBox, EmbeddedObjectManager> Managers =
            new ConditionalWeakTable<RichEditBox, EmbeddedObjectManager>();

        private sealed class TrackedObject
        {
            public ITextRange Range;
            public StorageFile File;
            public DateTimeOffset Modified;
            public bool EditInPaint;
        }

        private readonly RichEditBox _editor;
        private readonly List<TrackedObject> _objects = new List<TrackedObject>();
        private bool _refreshing;

        private EmbeddedObjectManager(RichEditBox editor)
        {
            _editor = editor;
        }

        public static EmbeddedObjectManager For(RichEditBox editor) => Managers.GetValue(editor, e => new EmbeddedObjectManager(e));

        public static readonly string[] TextExtensions = { ".txt", ".rtf" };

        #region Create new

        public async Task<bool> InsertNewPaintPictureAsync(string extension = ".png")
        {
            try
            {
                StorageFolder folder = await GetWorkFolderAsync();
                StorageFile file = await folder.CreateFileAsync(extension == ".bmp" ? "Bitmap.bmp" : "Picture.png", CreationCollisionOption.GenerateUniqueName);

                const uint width = 480, height = 320;
                using (IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite))
                {
                    Guid encoderId = extension == ".bmp" ? BitmapEncoder.BmpEncoderId : BitmapEncoder.PngEncoderId;
                    BitmapEncoder encoder = await BitmapEncoder.CreateAsync(encoderId, stream);
                    byte[] pixels = Enumerable.Repeat((byte)255, (int)(width * height * 4)).ToArray();
                    encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, width, height, 96, 96, pixels);
                    await encoder.FlushAsync();
                }

                bool inserted = await InsertTrackedPictureAsync(file, editInPaint: true);

                await OpenInPaintAsync(file);
                if (!inserted) await ImageInsertHelper.ShowLastErrorAsync();
                return inserted;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("InsertNewPaintPictureAsync: " + ex.Message);
                return false;
            }
        }

        public void InsertTable(int columns = 3, int rows = 3)
        {
            double textWidthDip = ImageInsertHelper.GetTextAreaWidth(_editor);
            int totalTwips = textWidthDip > 50 ? (int)(textWidthDip * 15) : 9000;
            int cell = Math.Max(500, totalTwips / columns);

            var rtf = new StringBuilder(@"{\rtf1\ansi ");
            for (int r = 0; r < rows; r++)
            {
                rtf.Append(@"\trowd\trgaph108");
                for (int c = 1; c <= columns; c++)
                {
                    rtf.Append(@"\clbrdrt\brdrs\brdrw10\clbrdrl\brdrs\brdrw10\clbrdrb\brdrs\brdrw10\clbrdrr\brdrs\brdrw10\cellx").Append(c * cell);
                }
                for (int c = 0; c < columns; c++) rtf.Append(@"\pard\intbl\cell");
                rtf.Append(@"\row");
            }
            rtf.Append(@"\pard\par}");
            _editor.Document.Selection.SetText(TextSetOptions.FormatRtf, rtf.ToString());
            _editor.Document.Selection.Collapse(true);
            _editor.Focus(FocusState.Programmatic);
        }

        #endregion

        #region Create from file

        public async Task<bool> InsertFromFileAsync(StorageFile file, bool displayAsIcon)
        {
            if (file == null) return false;
            string ext = file.FileType.ToLowerInvariant();
            try
            {
                if (!displayAsIcon && ImageInsertHelper.SupportedExtensions.Contains(ext))
                {
                    StorageFolder folder = await GetWorkFolderAsync();
                    StorageFile copy = await file.CopyAsync(folder, file.Name, NameCollisionOption.GenerateUniqueName);
                    bool ok = await InsertTrackedPictureAsync(copy, editInPaint: true);
                    if (!ok) await ImageInsertHelper.ShowLastErrorAsync();
                    return ok;
                }

                if (!displayAsIcon && ext == ".rtf")
                {
                    string rtf = await FileIO.ReadTextAsync(file);
                    _editor.Document.Selection.SetText(TextSetOptions.FormatRtf, rtf);
                    _editor.Document.Selection.Collapse(false);
                    return true;
                }

                if (!displayAsIcon && ext == ".txt")
                {
                    string text = await FileIO.ReadTextAsync(file);
                    _editor.Document.Selection.SetText(TextSetOptions.None, text);
                    _editor.Document.Selection.Collapse(false);
                    return true;
                }

                return await InsertPackageAsync(file);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("InsertFromFileAsync: " + ex.Message);
                return false;
            }
        }

        public async Task<bool> InsertPackageAsync(StorageFile file)
        {
            try
            {
                double scale = 1.0;
                try { scale = DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel; } catch { }
                float dpi = (float)(96 * scale);

                CanvasDevice device = CanvasDevice.GetSharedDevice();
                CanvasBitmap icon = null;
                using (StorageItemThumbnail thumbnail = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, (uint)Math.Round(48 * scale), ThumbnailOptions.UseCurrentScale))
                {
                    if (thumbnail != null) icon = await CanvasBitmap.LoadAsync(device, thumbnail, dpi);
                }

                const float width = 120f, iconSize = 48f;
                var format = new CanvasTextFormat
                {
                    FontFamily = "Segoe UI",
                    FontSize = 12,
                    HorizontalAlignment = CanvasHorizontalAlignment.Center,
                    WordWrapping = CanvasWordWrapping.EmergencyBreak
                };
                using (var layout = new CanvasTextLayout(device, file.Name, format, width, 60))
                {
                    float height = iconSize + 6 + (float)Math.Min(60, layout.LayoutBounds.Height) + 4;
                    using (var target = new CanvasRenderTarget(device, width, height, dpi))
                    {
                        using (CanvasDrawingSession ds = target.CreateDrawingSession())
                        {
                            ds.Clear(Colors.Transparent);
                            if (icon != null) ds.DrawImage(icon, new Rect((width - iconSize) / 2, 2, iconSize, iconSize));
                            ds.DrawTextLayout(layout, 0, iconSize + 6, Colors.Black);
                        }

                        var stream = new InMemoryRandomAccessStream();
                        await target.SaveAsync(stream, CanvasBitmapFileFormat.Png);
                        stream.Seek(0);

                        int start = _editor.Document.Selection.StartPosition;
                        if (!await ImageInsertHelper.InsertAsync(_editor, stream, ".png", selectPicture: true)) return false;
                        Track(start, file, editInPaint: false, modified: DateTimeOffset.MinValue);
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("InsertPackageAsync: " + ex.Message);
                return false;
            }
        }

        #endregion

        #region Activation

        public async Task<bool> TryActivateSelectedObjectAsync()
        {
            int start = PictureHelper.SelectedPictureStart(_editor);
            if (start < 0) return false;

            TrackedObject obj = _objects.FirstOrDefault(o => o.Range.StartPosition == start && o.Range.Length == 1);
            if (obj == null) return false;

            if (obj.EditInPaint)
            {
                await OpenInPaintAsync(obj.File);
            }
            else
            {
                await Launcher.LaunchFileAsync(obj.File);
            }
            return true;
        }

        public async Task RefreshChangedObjectsAsync()
        {
            if (_refreshing) return;
            _refreshing = true;
            try
            {
                foreach (TrackedObject obj in _objects.Where(o => o.EditInPaint).ToList())
                {
                    BasicProperties props;
                    try { props = await obj.File.GetBasicPropertiesAsync(); }
                    catch { _objects.Remove(obj); continue; }

                    if (props.DateModified <= obj.Modified.AddSeconds(1)) continue;

                    if (obj.Range.Length != 1)
                    {
                        _objects.Remove(obj);
                        continue;
                    }
                    obj.Range.GetText(TextGetOptions.None, out string text);
                    if (text != ObjectChar.ToString())
                    {
                        _objects.Remove(obj);
                        continue;
                    }

                    ITextSelection sel = _editor.Document.Selection;
                    int selStart = sel.StartPosition, selEnd = sel.EndPosition;
                    int start = obj.Range.StartPosition;

                    sel.SetRange(start, start + 1);
                    if (await ImageInsertHelper.InsertFileAsync(_editor, obj.File, selectPicture: false))
                    {
                        obj.Range = _editor.Document.GetRange(start, start + 1);
                    }
                    obj.Modified = props.DateModified;
                    _editor.Document.Selection.SetRange(selStart, selEnd);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("RefreshChangedObjectsAsync: " + ex.Message);
            }
            finally
            {
                _refreshing = false;
            }
        }

        public void Clear() => _objects.Clear();

        public object Detach(int start)
        {
            TrackedObject obj = _objects.FirstOrDefault(o => o.Range.StartPosition == start && o.Range.Length == 1);
            if (obj != null) _objects.Remove(obj);
            return obj;
        }

        public void Reattach(object link, int start)
        {
            if (!(link is TrackedObject obj)) return;
            obj.Range = _editor.Document.GetRange(start, start + 1);
            _objects.Add(obj);
        }

        #endregion

        #region Helpers

        private static async Task<StorageFolder> GetWorkFolderAsync()
        {
            return await ApplicationData.Current.TemporaryFolder.CreateFolderAsync("Objects", CreationCollisionOption.OpenIfExists);
        }

        private async Task<bool> InsertTrackedPictureAsync(StorageFile file, bool editInPaint)
        {
            int start = _editor.Document.Selection.StartPosition;
            if (!await ImageInsertHelper.InsertFileAsync(_editor, file, selectPicture: true)) return false;
            BasicProperties props = await file.GetBasicPropertiesAsync();
            Track(start, file, editInPaint, props.DateModified);
            return true;
        }

        private void Track(int start, StorageFile file, bool editInPaint, DateTimeOffset modified)
        {
            _objects.Add(new TrackedObject
            {
                Range = _editor.Document.GetRange(start, start + 1),
                File = file,
                EditInPaint = editInPaint,
                Modified = modified
            });
        }

        private static async Task OpenInPaintAsync(StorageFile file)
        {
            try
            {
                if (await Launcher.LaunchFileAsync(file, new LauncherOptions { TargetApplicationPackageFamilyName = PaintPackageFamilyName })) return;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("OpenInPaintAsync (Paint): " + ex.Message);
            }

            try
            {
                if (await Launcher.LaunchFileAsync(file)) return;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("OpenInPaintAsync (default): " + ex.Message);
            }

            await Launcher.LaunchFileAsync(file, new LauncherOptions { DisplayApplicationPicker = true });
        }

        #endregion
    }
}
