using DocumentFormat.OpenXml.Drawing;
using Microsoft.UI.Xaml.Controls;
using RectifyPad;
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
using WordPad.Helpers;

// Szablon elementu Kontrolka użytkownika jest udokumentowany na stronie https://go.microsoft.com/fwlink/?LinkId=234236

namespace WordPad.WordPadUI.Ribbon
{
    public sealed partial class ParagraphToolbar : UserControl
    {
        public RichEditBox Editor { get; set; }
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