using System;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.ApplicationModel.Resources;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using muxc = Microsoft.UI.Xaml.Controls;

namespace UltraPad.Helpers
{
    public static class PictureHelper
    {
        public const char ObjectChar = '￼';

        public struct PictureInfo
        {
            public int Start;
            public double GoalWidthTwips;
            public double GoalHeightTwips;
            public int ScaleX;
            public int ScaleY;
            public bool IsObject;

            public double WidthDip => GoalWidthTwips * ScaleX / 100.0 / 15.0;
            public double HeightDip => GoalHeightTwips * ScaleY / 100.0 / 15.0;
        }

        private static string L(string key, string fallback)
        {
            try
            {
                string value = ResourceLoader.GetForCurrentView().GetString(key);
                return string.IsNullOrEmpty(value) ? fallback : value;
            }
            catch
            {
                return fallback;
            }
        }

        public static Func<RichEditBox, int> AdornerSelection;

        public static int SelectedPictureStart(RichEditBox editor)
        {
            int start = AdornerSelection?.Invoke(editor) ?? -1;
            if (start >= 0) return start;
            return IsRealPictureSelection(editor) ? editor.Document.Selection.StartPosition : -1;
        }

        public static bool IsPictureSelected(RichEditBox editor) => SelectedPictureStart(editor) >= 0;

        private static bool IsRealPictureSelection(RichEditBox editor)
        {
            try
            {
                ITextSelection sel = editor.Document.Selection;
                if (sel.Length != 1) return false;
                sel.GetText(TextGetOptions.None, out string text);
                return text == ObjectChar.ToString();
            }
            catch
            {
                return false;
            }
        }

        private static int ReadWord(string rtf, string word, int fallback)
        {
            Match m = Regex.Match(rtf, @"\\" + word + @"(-?\d+)");
            return m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;
        }

        public static bool TryGetPicture(RichEditBox editor, int start, out PictureInfo info, out string rtf)
        {
            info = new PictureInfo { Start = start, ScaleX = 100, ScaleY = 100 };
            rtf = null;
            try
            {
                ITextRange range = editor.Document.GetRange(start, start + 1);
                range.GetText(TextGetOptions.FormatRtf, out rtf);
                if (string.IsNullOrEmpty(rtf) || rtf.IndexOf(@"\pict", StringComparison.Ordinal) < 0) return false;

                int objW = ReadWord(rtf, "objw", 0), objH = ReadWord(rtf, "objh", 0);
                if (rtf.IndexOf(@"\object", StringComparison.Ordinal) >= 0 && objW > 0 && objH > 0)
                {
                    info.IsObject = true;
                    info.GoalWidthTwips = objW;
                    info.GoalHeightTwips = objH;
                    info.ScaleX = ReadWord(rtf, "objscalex", 100);
                    info.ScaleY = ReadWord(rtf, "objscaley", 100);
                }
                else
                {
                    info.GoalWidthTwips = ReadWord(rtf, "picwgoal", 0);
                    info.GoalHeightTwips = ReadWord(rtf, "pichgoal", 0);
                    info.ScaleX = ReadWord(rtf, "picscalex", 100);
                    info.ScaleY = ReadWord(rtf, "picscaley", 100);
                }
                if (info.ScaleX <= 0) info.ScaleX = 100;
                if (info.ScaleY <= 0) info.ScaleY = 100;
                return info.GoalWidthTwips > 0 && info.GoalHeightTwips > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("PictureHelper.TryGetPicture: " + ex.Message);
                return false;
            }
        }

        private static string SetWord(string rtf, string word, int value)
        {
            string replacement = @"\" + word + value.ToString(CultureInfo.InvariantCulture);
            if (Regex.IsMatch(rtf, @"\\" + word + @"-?\d+"))
                return Regex.Replace(rtf, @"\\" + word + @"-?\d+", m => replacement);
            return Regex.Replace(rtf, @"\\pict(?![a-z])", m => @"\pict" + replacement);
        }

        private static string SetObjectWord(string rtf, string word, int value)
        {
            string replacement = @"\" + word + value.ToString(CultureInfo.InvariantCulture);
            if (Regex.IsMatch(rtf, @"\\" + word + @"-?\d+"))
                return Regex.Replace(rtf, @"\\" + word + @"-?\d+", m => replacement);
            return Regex.Replace(rtf, @"\\objh-?\d+", m => m.Value + replacement);
        }

        public static bool SetScale(RichEditBox editor, int start, int scaleX, int scaleY)
        {
            if (!TryGetPicture(editor, start, out PictureInfo info, out string rtf)) return false;
            editor.Document.BeginUndoGroup();
            try
            {
                return ApplyScale(editor, start, rtf, scaleX, scaleY);
            }
            finally
            {
                editor.Document.EndUndoGroup();
            }
        }

        public static bool ApplyScale(RichEditBox editor, int start, string pictureRtf, int scaleX, int scaleY, bool select = true)
        {
            scaleX = Math.Max(1, Math.Min(5000, scaleX));
            scaleY = Math.Max(1, Math.Min(5000, scaleY));
            string newRtf = SetWord(SetWord(pictureRtf, "picscalex", scaleX), "picscaley", scaleY);
            if (newRtf.IndexOf(@"\object", StringComparison.Ordinal) >= 0)
                newRtf = SetObjectWord(SetObjectWord(newRtf, "objscalex", scaleX), "objscaley", scaleY);
            try
            {
                EmbeddedObjectManager objects = EmbeddedObjectManager.For(editor);
                object link = objects.Detach(start);
                ITextRange range = editor.Document.GetRange(start, start + 1);
                range.SetText(TextSetOptions.FormatRtf, newRtf);
                objects.Reattach(link, start);

                range.GetText(TextGetOptions.None, out string inserted);
                if (inserted != null && inserted.Length > 1 && inserted.EndsWith("\r"))
                {
                    editor.Document.GetRange(range.EndPosition - 1, range.EndPosition).Text = string.Empty;
                }

                if (select) editor.Document.Selection.SetRange(start, start + 1);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("PictureHelper.ApplyScale: " + ex.Message);
                return false;
            }
        }

        public static int ScaleFor(double dip, double goalTwips) => (int)Math.Round(dip * 15.0 * 100.0 / goalTwips);

        public static bool SetSize(RichEditBox editor, int start, double widthDip, double heightDip)
        {
            if (!TryGetPicture(editor, start, out PictureInfo info, out _)) return false;
            return SetScale(editor, start, ScaleFor(widthDip, info.GoalWidthTwips), ScaleFor(heightDip, info.GoalHeightTwips));
        }

        public static async Task ChangePictureAsync(RichEditBox editor)
        {
            int start = SelectedPictureStart(editor);
            if (start < 0) return;

            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
            foreach (string extension in ImageInsertHelper.SupportedExtensions) picker.FileTypeFilter.Add(extension);
            StorageFile file = await picker.PickSingleFileAsync();
            if (file == null) return;

            editor.Document.Selection.SetRange(start, start + 1);
            if (!await ImageInsertHelper.InsertFileAsync(editor, file, selectPicture: true))
                await ImageInsertHelper.ShowLastErrorAsync();
        }

        public static async Task ShowResizeDialogAsync(RichEditBox editor)
        {
            int start = SelectedPictureStart(editor);
            if (start < 0) return;
            if (!TryGetPicture(editor, start, out PictureInfo info, out _)) return;

            double ratio = (double)info.ScaleY / info.ScaleX;
            bool updating = false;

            var horizontal = new muxc.NumberBox
            {
                Header = L("resize_horizontal", "Horizontal (%):"),
                Minimum = 1,
                Maximum = 5000,
                Value = info.ScaleX,
                SmallChange = 1,
                LargeChange = 10,
                SpinButtonPlacementMode = muxc.NumberBoxSpinButtonPlacementMode.Compact,
                Width = 200,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            var vertical = new muxc.NumberBox
            {
                Header = L("resize_vertical", "Vertical (%):"),
                Minimum = 1,
                Maximum = 5000,
                Value = info.ScaleY,
                SmallChange = 1,
                LargeChange = 10,
                SpinButtonPlacementMode = muxc.NumberBoxSpinButtonPlacementMode.Compact,
                Width = 200,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            var lockRatio = new CheckBox { Content = L("resize_lock", "Lock aspect ratio"), IsChecked = true };

            horizontal.ValueChanged += (s, e) =>
            {
                if (updating || double.IsNaN(e.NewValue) || lockRatio.IsChecked != true) return;
                updating = true;
                vertical.Value = Math.Max(1, Math.Round(e.NewValue * ratio));
                updating = false;
            };
            vertical.ValueChanged += (s, e) =>
            {
                if (updating || double.IsNaN(e.NewValue) || lockRatio.IsChecked != true) return;
                updating = true;
                horizontal.Value = Math.Max(1, Math.Round(e.NewValue / ratio));
                updating = false;
            };
            lockRatio.Checked += (s, e) =>
            {
                if (!double.IsNaN(horizontal.Value) && horizontal.Value > 0 && !double.IsNaN(vertical.Value))
                    ratio = vertical.Value / horizontal.Value;
            };

            var scaleGroup = new StackPanel { Spacing = 12 };
            scaleGroup.Children.Add(new TextBlock { Text = L("resize_scale", "Scale"), FontWeight = Windows.UI.Text.FontWeights.SemiBold });
            scaleGroup.Children.Add(horizontal);
            scaleGroup.Children.Add(vertical);
            scaleGroup.Children.Add(lockRatio);

            var dialog = new ContentDialog
            {
                Title = L("resize_title", "Resize"),
                Content = scaleGroup,
                PrimaryButtonText = L("dlg_ok", "OK"),
                SecondaryButtonText = L("resize_cancel", "Cancel"),
                DefaultButton = ContentDialogButton.Primary
            };
            if (Application.Current.Resources.TryGetValue("DefaultContentDialogStyle", out object style) && style is Style s1)
                dialog.Style = s1;

            if (await dialog.ShowThemedAsync() != ContentDialogResult.Primary) return;
            if (double.IsNaN(horizontal.Value) || double.IsNaN(vertical.Value)) return;

            SetScale(editor, start, (int)Math.Round(horizontal.Value), (int)Math.Round(vertical.Value));
            editor.Focus(FocusState.Programmatic);
        }
    }
}
