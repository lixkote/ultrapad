using DocumentFormat.OpenXml.Drawing;
using Microsoft.UI.Xaml.Controls;
using UltraPad;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Reflection.Metadata;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Globalization;
using Windows.Storage;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using UltraPad.Helpers;

// Szablon elementu Kontrolka użytkownika jest udokumentowany na stronie https://go.microsoft.com/fwlink/?LinkId=234236

namespace UltraPad.UltraPadUI.Ribbon
{
    public sealed partial class ParagraphToolbar : UserControl
    {
        public RichEditBox Editor { get; set; }

        private void ListStylesGrid_Loaded(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            HighlightCurrentListStyle(sender as Windows.UI.Xaml.Controls.Panel, Editor);
        }

        // Like WordPad, the list style of the paragraph with the cursor is framed in the accent color
        // when the list styles open (here and in the editor's context menu). The frame is a separate
        // border over the button, so the button's own hover look can't replace it; on hover it only
        // gets a little lighter, like in WordPad.
        private const string SelectionFrameName = "ListStyleSelectionFrame";

        public static void HighlightCurrentListStyle(Windows.UI.Xaml.Controls.Panel grid, Windows.UI.Xaml.Controls.RichEditBox editor)
        {
            if (grid == null || editor == null) return;
            Windows.UI.Text.MarkerType current;
            try { current = editor.Document.Selection.ParagraphFormat.ListType; }
            catch { return; }

            // the accent shades Windows 11 uses for accent elements: lighter on dark, darker on light
            bool dark = grid.ActualTheme == Windows.UI.Xaml.ElementTheme.Dark;
            Windows.UI.Color accent = dark ? Windows.UI.Color.FromArgb(255, 96, 205, 255) : Windows.UI.Color.FromArgb(255, 0, 95, 184);
            string accentKey = dark ? "SystemAccentColorLight2" : "SystemAccentColorDark1";
            if (Windows.UI.Xaml.Application.Current.Resources.TryGetValue(accentKey, out object color) && color is Windows.UI.Color systemAccent)
            {
                accent = systemAccent;
            }

            Windows.UI.Xaml.Controls.Border frame = null;
            Windows.UI.Xaml.Controls.Button selected = null;
            foreach (Windows.UI.Xaml.UIElement child in grid.Children)
            {
                if (child is Windows.UI.Xaml.Controls.Border border && border.Name == SelectionFrameName) frame = border;
                if (!(child is Windows.UI.Xaml.Controls.Button button)) continue;

                // an earlier version framed the button itself
                button.ClearValue(Windows.UI.Xaml.Controls.Control.BorderBrushProperty);
                button.ClearValue(Windows.UI.Xaml.Controls.Control.BorderThicknessProperty);
                button.ClearValue(Windows.UI.Xaml.Controls.Control.PaddingProperty);

                Windows.UI.Text.MarkerType? type = ListTypeOfButton(button.Name);
                if (type != null && type.Value == current) selected = button;
            }

            if (frame == null)
            {
                frame = new Windows.UI.Xaml.Controls.Border
                {
                    Name = SelectionFrameName,
                    BorderThickness = new Windows.UI.Xaml.Thickness(2),
                    IsHitTestVisible = false
                };
                grid.Children.Add(frame);
            }

            if (selected == null)
            {
                frame.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                return;
            }

            Windows.UI.Xaml.Media.SolidColorBrush normalBrush = new Windows.UI.Xaml.Media.SolidColorBrush(accent);
            Windows.UI.Xaml.Media.SolidColorBrush hoverBrush = new Windows.UI.Xaml.Media.SolidColorBrush(Lighten(accent, 0.3));

            frame.Visibility = Windows.UI.Xaml.Visibility.Visible;
            frame.BorderBrush = normalBrush;
            frame.Width = selected.Width;
            frame.Height = selected.Height;
            frame.Margin = selected.Margin;
            frame.CornerRadius = selected.CornerRadius;
            frame.HorizontalAlignment = selected.HorizontalAlignment;
            frame.VerticalAlignment = selected.VerticalAlignment;
            Windows.UI.Xaml.Controls.Grid.SetRow(frame, Windows.UI.Xaml.Controls.Grid.GetRow(selected));
            Windows.UI.Xaml.Controls.Grid.SetColumn(frame, Windows.UI.Xaml.Controls.Grid.GetColumn(selected));

            // the hover handlers are added once per button and look up the frame's current button
            foreach (Windows.UI.Xaml.UIElement child in grid.Children)
            {
                if (!(child is Windows.UI.Xaml.Controls.Button button) || button.Tag as string == "framehooked") continue;
                button.Tag = "framehooked";
                button.PointerEntered += (s, e) => SetFrameHover(grid, button, true);
                button.PointerExited += (s, e) => SetFrameHover(grid, button, false);
                button.PointerCanceled += (s, e) => SetFrameHover(grid, button, false);
            }
            frame.Tag = new object[] { selected, normalBrush, hoverBrush };
        }

        private static void SetFrameHover(Windows.UI.Xaml.Controls.Panel grid, Windows.UI.Xaml.Controls.Button button, bool hover)
        {
            foreach (Windows.UI.Xaml.UIElement child in grid.Children)
            {
                if (!(child is Windows.UI.Xaml.Controls.Border frame) || frame.Name != SelectionFrameName) continue;
                if (!(frame.Tag is object[] state) || state.Length != 3 || !ReferenceEquals(state[0], button)) return;
                frame.BorderBrush = (Windows.UI.Xaml.Media.Brush)(hover ? state[2] : state[1]);
                return;
            }
        }

        private static Windows.UI.Color Lighten(Windows.UI.Color color, double amount)
        {
            byte Mix(byte c) => (byte)Math.Round(c + (255 - c) * amount);
            return Windows.UI.Color.FromArgb(color.A, Mix(color.R), Mix(color.G), Mix(color.B));
        }

        private static Windows.UI.Text.MarkerType? ListTypeOfButton(string name)
        {
            switch (name)
            {
                case "NoneNumeral": return Windows.UI.Text.MarkerType.None;
                case "DottedNumeral": return Windows.UI.Text.MarkerType.Bullet;
                case "NumberNumeral": return Windows.UI.Text.MarkerType.Arabic;
                case "LetterSmallNumeral": return Windows.UI.Text.MarkerType.LowercaseEnglishLetter;
                case "LetterBigNumeral": return Windows.UI.Text.MarkerType.UppercaseEnglishLetter;
                case "SmalliNumeral": return Windows.UI.Text.MarkerType.LowercaseRoman;
                case "BigINumeral": return Windows.UI.Text.MarkerType.UppercaseRoman;
                default: return null;
            }
        }
        public MainPage MainPagea { get; set; }  
        public ParagraphToolbar()
        {
            this.InitializeComponent();
        }
        private ApplicationDataContainer localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;

        private Throttle _selectionThrottle;

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (_selectionThrottle == null) _selectionThrottle = new Throttle(TimeSpan.FromMilliseconds(80), UpdateFromSelection);

            // Loaded fires again every time the collapsed toolbar's flyout opens
            Editor.SelectionChanged -= Editor_SelectionChangedThrottled;
            Editor.SelectionChanged += Editor_SelectionChangedThrottled;
        }

        private void Editor_SelectionChangedThrottled(object sender, RoutedEventArgs e) => _selectionThrottle?.Request();

        public object ConvertString2Float(string value, Type targetType, object parameter, string language)
        {
            // Convert a string to a float.
            if (value is string stringValue)
            {
                if (float.TryParse(stringValue, out float result))
                {
                    return result;
                }
            }

            return 0.0f; // Default value if the conversion fails.
        }

        private void LoadSettings()
        {
            string no = "no";
            var settings = ApplicationData.Current.LocalSettings;
            if (settings.Values.TryGetValue("is10ptenabled", out object value))
            {
                string yesorno = value.ToString();
                if (yesorno != null)
                {
                    if (yesorno != no)
                    {
                        _10pt.IsChecked = true;
                    }
                }
            }

            string linespacingval = (string)Windows.Storage.ApplicationData.Current.LocalSettings.Values["linespacing"];
            Debug.WriteLine("Detected Spacing: " + linespacingval);

            float lineSpacingValue = 1.0f; // Default value

            switch (linespacingval)
            {
                case "1.0":
                    lineSpacingValue = 1.0f;
                    spacingradio1.IsChecked = true;
                    break;
                case "1.15":
                    lineSpacingValue = 1.15f;
                    spacingradio2.IsChecked = true;
                    break;
                case "1.5":
                    lineSpacingValue = 1.5f;
                    spacingradio3.IsChecked = true;
                    break;
                case "2":
                    lineSpacingValue = 2.0f;
                    spacingradio4.IsChecked = true;
                    break;
                default:
                    break;
            }
            float halal = (float)ConvertString2Float(linespacingval, typeof(float), null, null);

            // Get the current document from the RichEditBox
            ITextDocument document = Editor.Document;

            // Get the current selection from the document
            ITextSelection selection = document.Selection;

            // Get the paragraph format of the selection
            ITextParagraphFormat paragraphFormat = selection.ParagraphFormat;

            // Set the line spacing rule to multiple
            paragraphFormat.SetLineSpacing(LineSpacingRule.Multiple, halal);
        }

        public string GetText(RichEditBox RichEditor)
        {
            RichEditor.Document.GetText(TextGetOptions.FormatRtf, out string Text);
            ITextRange Range = RichEditor.Document.GetRange(0, Text.Length);
            Range.GetText(TextGetOptions.FormatRtf, out string Value);
            return Value;
        }

        private void SetParagraphIndents(float leftIndent, float rightIndent, float firstLineIndent, bool applyToSelectionOnly = true)
        {
            // Get the ITextDocument interface for the RichEditBox's document
            ITextDocument document = Editor.Document;

            // Get the current selection's start and end positions
            int start = document.Selection.StartPosition;
            int end = document.Selection.EndPosition;

            // If applyToSelectionOnly is true, check if there's any selected text in the RichEditBox
            if (applyToSelectionOnly && start == end)
            {
                //return;
            }

            // Get the ITextRange interface for the selection or the entire document
            ITextRange textRange;
            if (applyToSelectionOnly)
            {
                textRange = document.Selection;
            }
            else
            {
                textRange = document.GetRange(0, GetText(Editor).Length);
            }

            // Get the ITextParagraphFormat interface for the text range
            ITextParagraphFormat paragraphFormat = textRange.ParagraphFormat;

            // Set the left and right indents for the current selection's paragraph(s)
            try
            {
                if (document.Selection.Length != 0)
                {
                    paragraphFormat.SetIndents(firstLineIndent, leftIndent, rightIndent);
                }
                else
                {
                    document.GetRange(document.Selection.StartPosition, document.Selection.EndPosition + 1);
                    paragraphFormat.SetIndents(firstLineIndent, leftIndent, rightIndent);
                }
            }
            catch
            {
            }

            // Apply the new paragraph format to the current selection or the entire document
            textRange.ParagraphFormat = paragraphFormat;

            // LeftIndent.Text = leftIndent.ToString();

            // RightIndent.Text = rightIndent.ToString();
        }
        public async void ShowParagraphDialog()
        {
            await ShowParagraphDialogAsync();
        }

        public async Task ShowParagraphDialogAsync()
        {
            if (Editor == null) return;
            var paragraphDialog = new ParagraphDialog(Editor);
            ContentDialogResult result = await paragraphDialog.ShowThemedAsync();

            if (result == ContentDialogResult.Secondary || (result == ContentDialogResult.Primary && paragraphDialog.TabsRequested))
            {
                paragraphDialog.Apply();
            }

            if (result == ContentDialogResult.Primary && paragraphDialog.TabsRequested)
            {
                await ShowTabsDialogAsync();
            }
            else
            {
                Editor.Focus(FocusState.Programmatic);
            }
        }

        public async Task ShowTabsDialogAsync()
        {
            if (Editor == null) return;
            var tabsDialog = new TabsDialog(Editor);
            if (await tabsDialog.ShowThemedAsync() == ContentDialogResult.Secondary)
            {
                tabsDialog.Apply();
            }
            Editor.Focus(FocusState.Programmatic);
        }

        private void NoneNumeral_Click(object sender, RoutedEventArgs e)
        {
            Editor.Document.Selection.ParagraphFormat.ListType = MarkerType.None;
            TextBulletingButton.IsChecked = false;
            TextBulletingButton.Flyout.Hide();
            Editor.Focus(FocusState.Keyboard);
        }

        private void DottedNumeral_Click(object sender, RoutedEventArgs e)
        {
            Editor.Document.Selection.ParagraphFormat.ListType = MarkerType.Bullet;
            TextBulletingButton.IsChecked = true;
            TextBulletingButton.Flyout.Hide();
            Editor.Focus(FocusState.Keyboard);
        }

        private void BulletButton_Click(Microsoft.UI.Xaml.Controls.SplitButton sender, Microsoft.UI.Xaml.Controls.SplitButtonClickEventArgs e)
        {
            if (Editor.Document.Selection.ParagraphFormat.ListType != MarkerType.None)
            {
                Editor.Document.Selection.ParagraphFormat.ListType = MarkerType.None;
                TextBulletingButton.IsChecked = false;
            }
            else
            {
                Editor.Document.Selection.ParagraphFormat.ListType = MarkerType.Bullet;
                TextBulletingButton.IsChecked = true;
            }
            TextBulletingButton.Flyout.Hide();
            Editor.Focus(FocusState.Keyboard);
        }

        private void NumberNumeral_Click(object sender, RoutedEventArgs e)
        {
            Editor.Document.Selection.ParagraphFormat.ListType = MarkerType.Arabic;
            TextBulletingButton.IsChecked = true;
            TextBulletingButton.Flyout.Hide();
            Editor.Focus(FocusState.Keyboard);
        }

        private void LetterSmallNumeral_Click(object sender, RoutedEventArgs e)
        {
            Editor.Document.Selection.ParagraphFormat.ListType = MarkerType.LowercaseEnglishLetter;
            TextBulletingButton.IsChecked = true;
            TextBulletingButton.Flyout.Hide();
            Editor.Focus(FocusState.Keyboard);
        }

        private void LetterBigNumeral_Click(object sender, RoutedEventArgs e)
        {
            Editor.Document.Selection.ParagraphFormat.ListType = MarkerType.UppercaseEnglishLetter;
            TextBulletingButton.IsChecked = true;
            TextBulletingButton.Flyout.Hide();
            Editor.Focus(FocusState.Keyboard);
        }

        private void SmalliNumeral_Click(object sender, RoutedEventArgs e)
        {
            Editor.Document.Selection.ParagraphFormat.ListType = MarkerType.LowercaseRoman;
            TextBulletingButton.IsChecked = true;
            TextBulletingButton.Flyout.Hide();
            Editor.Focus(FocusState.Keyboard);
        }

        private void BigINumeral_Click(object sender, RoutedEventArgs e)
        {
            Editor.Document.Selection.ParagraphFormat.ListType = MarkerType.UppercaseRoman;
            TextBulletingButton.IsChecked = true;
            TextBulletingButton.Flyout.Hide();
            Editor.Focus(FocusState.Keyboard);
        }

        private void AlignJustifyButton_Click(object sender, RoutedEventArgs e)
        {
            Editor.AlignSelectedTo(RichEditHelpers.AlignMode.Justify);
            editor_SelectionChanged(sender, e);
            Editor.Focus(FocusState.Keyboard);
        }

        private void editor_SelectionChanged(object sender, RoutedEventArgs e) => UpdateFromSelection();

        private void UpdateFromSelection()
        {
            if (Editor == null) return;
            ITextParagraphFormat format = Editor.Document.Selection.ParagraphFormat;
            ParagraphAlignment alignment = format.Alignment;
            AlignLeftButton.IsChecked = alignment == ParagraphAlignment.Left;
            AlignCenterButton.IsChecked = alignment == ParagraphAlignment.Center;
            AlignRightButton.IsChecked = alignment == ParagraphAlignment.Right;
            AlignJustifyButton.IsChecked = alignment == ParagraphAlignment.Justify;
            TextBulletingButton.IsChecked = format.ListType != MarkerType.None;
        }

        private async void ParagraphButton_Click(object sender, RoutedEventArgs e)
        {
            ShowParagraphDialog();
        }

        private void AlignRightButton_Click(object sender, RoutedEventArgs e)
        {
            Editor.AlignSelectedTo(RichEditHelpers.AlignMode.Right);
            editor_SelectionChanged(sender, e);
            Editor.Focus(FocusState.Keyboard);
        }

        private void AlignLeftButton_Click(object sender, RoutedEventArgs e)
        {
            Editor.AlignSelectedTo(RichEditHelpers.AlignMode.Left);
            editor_SelectionChanged(sender, e);
            Editor.Focus(FocusState.Keyboard);
        }

        private void AlignCenterButton_Click(object sender, RoutedEventArgs e)
        {
            Editor.AlignSelectedTo(RichEditHelpers.AlignMode.Center);
            editor_SelectionChanged(sender, e);
            Editor.Focus(FocusState.Keyboard);
        }

        private void IndentationIncreaseRight_Click(object sender, RoutedEventArgs e)
        {
            Editor.Document.Selection.ParagraphFormat.SetIndents(Editor.Document.Selection.ParagraphFormat.FirstLineIndent,
                                                                 Editor.Document.Selection.ParagraphFormat.LeftIndent + 20,
                                                                 Editor.Document.Selection.ParagraphFormat.RightIndent);
            Editor.Focus(FocusState.Keyboard);
        }

        private void IndentationIncreaseLeft_Click(object sender, RoutedEventArgs e)
        {
            Editor.Document.Selection.ParagraphFormat.SetIndents(Editor.Document.Selection.ParagraphFormat.FirstLineIndent,
                                                                 Math.Max(Editor.Document.Selection.ParagraphFormat.LeftIndent - 20, 0),
                                                                 Editor.Document.Selection.ParagraphFormat.RightIndent);
            Editor.Focus(FocusState.Keyboard);
        }

        private void RadioMenuFlyoutItem_Click(object sender, RoutedEventArgs e)
        {
            Editor.Document.Selection.ParagraphFormat.SetLineSpacing(LineSpacingRule.Multiple, 1);
        }

        private void RadioMenuFlyoutItem_Click_1(object sender, RoutedEventArgs e)
        {
            Editor.Document.Selection.ParagraphFormat.SetLineSpacing(LineSpacingRule.Multiple, (float)1.15);
        }

        private void RadioMenuFlyoutItem_Click_2(object sender, RoutedEventArgs e)
        {
            Editor.Document.Selection.ParagraphFormat.SetLineSpacing(LineSpacingRule.Multiple, (float)1.5);
        }

        private void RadioMenuFlyoutItem_Click_3(object sender, RoutedEventArgs e)
        {
            Editor.Document.Selection.ParagraphFormat.SetLineSpacing(LineSpacingRule.Multiple, 2);
        }

        private void ParagraphSettingButton_Loaded(object sender, RoutedEventArgs e)
        {
        }

        private void SpacingFlyout_Opening(object sender, object e)
        {
            if (Editor == null) return;
            ITextParagraphFormat f = Editor.Document.Selection.ParagraphFormat;

            float spacing = f.LineSpacing;
            if (f.LineSpacingRule == LineSpacingRule.Single) spacing = 1.0f;
            else if (f.LineSpacingRule == LineSpacingRule.OneAndHalf) spacing = 1.5f;
            else if (f.LineSpacingRule == LineSpacingRule.Double) spacing = 2.0f;
            else if (f.LineSpacingRule != LineSpacingRule.Multiple) spacing = -1;

            spacingradio1.IsChecked = Math.Abs(spacing - 1.0f) < 0.01f;
            spacingradio2.IsChecked = Math.Abs(spacing - 1.15f) < 0.01f;
            spacingradio3.IsChecked = Math.Abs(spacing - 1.5f) < 0.01f;
            spacingradio4.IsChecked = Math.Abs(spacing - 2.0f) < 0.01f;

            _10pt.IsChecked = f.SpaceAfter >= 9.5f && f.SpaceAfter < 1000000f;
        }

        private void AddSpace_Click(object sender, RoutedEventArgs e)
        {
            if (Editor == null) return;
            float after = _10pt.IsChecked ? 10f : 0f;
            ParagraphHelper.ForEachSelectedParagraph(Editor, f => f.SpaceAfter = after);
            Editor.Focus(FocusState.Keyboard);
        }
    }
}