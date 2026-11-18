using DocumentFormat.OpenXml.Bibliography;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Foundation.Metadata;
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
    public sealed partial class FontToolbar : UserControl
    {
        private bool textboxwasalreadyloaded = false;
        private bool updateFontFormat = true;
        ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
        public RichEditBox Editor { get; set; }

        public List<double> FontSizes { get; } = new List<double>()
            {
                8,
                9,
                10,
                11,
                12,
                14,
                16,
                18,
                20,
                24,
                28,
                36,
                48,
                72
            };

        private void UpdateFontSize()
        {
            fontSizeComboBox.SelectedItem = Editor.FontSize;
        }

        private void CancelColor_Click(object sender, RoutedEventArgs e)
        {
            // Cancel flyout
            colorPickerButton.Flyout.Hide();
        }

        private void ConfirmColor_Click(object sender, RoutedEventArgs e)
        {
            // Confirm color picker choice and apply color to text
            Windows.UI.Color color = myColorPicker.Color;
            Editor.Document.Selection.CharacterFormat.ForegroundColor = color;
            FontColorMarker.SetValue(ForegroundProperty, new SolidColorBrush(color));

            // Hide flyout
            colorPickerButton.Flyout.Hide();
            FontTextColorDropDownButton.Flyout.Hide();
            Editor.Focus(FocusState.Programmatic);
        }

        public FontToolbar()
        {
            this.InitializeComponent();
            BuildPalettes();
            BackTextColorMarkerMain.FontFamily = (FontFamily)Application.Current.Resources["CustomIconFont"];
            BackTextColorMarker.FontFamily = (FontFamily)Application.Current.Resources["CustomIconFont"]; 
            FontColorMarkerMain.FontFamily = (FontFamily)Application.Current.Resources["CustomIconFont"];
            FontColorMarker.FontFamily = (FontFamily)Application.Current.Resources["CustomIconFont"];
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            Editor.SelectionChanged += Editor_SelectionChanged;
        }

        private void FontSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox comboBox)
            {
                if (comboBox.SelectedItem is double selectedValue)
                {
                    Editor.Document.Selection.CharacterFormat.Size = (float)selectedValue;
                    Editor.Focus(FocusState.Programmatic);
                }
            }
        }

        private void FontSizeCombo_TextSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args)
        {
            bool isDouble = double.TryParse(sender.Text, out double newValue);

            // Check if the user selected a predefined font size from the ComboBox.
            if (sender.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag is double predefinedSize)
            {
                newValue = predefinedSize;
            }

            // Set the selected item if:
            // - The value successfully parsed to double AND
            // - The value is in the list of sizes OR is a custom value between 8 and 100
            if (isDouble && (FontSizes.Contains(newValue) || (newValue < 300 && newValue > 0)))
            {
                // Update the SelectedItem to the new value. 
                sender.SelectedItem = newValue;
                Editor.Document.Selection.CharacterFormat.Size = (float)newValue;
            }
            else
            {
                // If the item is invalid, reject it and revert the text. 
                sender.Text = sender.SelectedValue?.ToString();

                var dialog = new ContentDialog
                {
                    Content = Loc.Format("dlg_fontsize_range", "The font size must be a number between {0} and {1}.", 0, 300),
                    CloseButtonText = Loc.Get("dlg_close", "Close"),
                    DefaultButton = ContentDialogButton.Close
                };
                var task = dialog.ShowThemedAsync();
            }

            // Mark the event as handled so the framework doesn’t update the selected item automatically. 
            args.Handled = true;
        }

        private void fontSizeComboBox_Loaded(object sender, RoutedEventArgs e)
        {
            {
                fontSizeComboBox.SelectedIndex = 3;

                if ((ApiInformation.IsApiContractPresent("Windows.Foundation.UniversalApiContract", 7)))
                {
                    fontSizeComboBox.TextSubmitted += FontSizeCombo_TextSubmitted;
                }
            }
        }

        private void FontsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Editor.Document.Selection == null || !updateFontFormat)
                return;
            try
            {
                // Check if SelectedValue is null before attempting to access it
                var selectedValue = FontsComboBox.SelectedValue;
                if (selectedValue != null)
                {
                    string choosenvalue = selectedValue.ToString();
                    Editor.Document.Selection.CharacterFormat.Name = choosenvalue;
                }
                else
                {
                    // Handle the case where SelectedValue is null (fallback to a default font)
                    Editor.Document.Selection.CharacterFormat.Name = "Calibri";
                }

                // Ensure the editor gets focus
                Editor.Focus(FocusState.Programmatic);
            }
            catch
            {
                // In case of any other unexpected errors, fallback to the default font
                Editor.Document.Selection.CharacterFormat.Name = "Calibri";
                Editor.Focus(FocusState.Programmatic);
            }
        }

        private void FontTextColorDropDownButton_Click(Microsoft.UI.Xaml.Controls.SplitButton sender, Microsoft.UI.Xaml.Controls.SplitButtonClickEventArgs args)
        {
            var color = ((SolidColorBrush)FontColorMarker.Foreground).Color;
            if (color.A == 0) color = Windows.UI.Colors.Black;
            if (Editor.ActualTheme == ElementTheme.Dark && color.R == 0 && color.G == 0 && color.B == 0) color = Windows.UI.Colors.White;
            Editor.Document.Selection.CharacterFormat.ForegroundColor = color;
            Editor.Focus(FocusState.Keyboard);
        }

        private void FontBackgroundColorDropDownButton_Click(Microsoft.UI.Xaml.Controls.SplitButton sender, Microsoft.UI.Xaml.Controls.SplitButtonClickEventArgs args)
        {
            var color = ((SolidColorBrush)BackTextColorMarker.Foreground).Color;
            Editor.Document.Selection.CharacterFormat.BackgroundColor = color;
            Editor.Focus(FocusState.Keyboard);
        }

        private void FontSizesComboBox_TextSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args)
        {
            bool isDouble = double.TryParse(sender.Text, out double newValue);

            // Check if the user selected a predefined font size from the ComboBox.
            if (sender.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag is double predefinedSize)
            {
                newValue = predefinedSize;
            }

            // Set the selected item if:
            // - The value successfully parsed to double AND
            // - The value is in the list of sizes OR is a custom value between 8 and 100
            if (isDouble && (FontSizes.Contains(newValue) || (newValue < 200 && newValue > 8)))
            {
                // Update the SelectedItem to the new value. 
                sender.SelectedItem = newValue;
                Editor.Document.Selection.CharacterFormat.Size = (float)newValue;
            }
            else
            {
                // If the item is invalid, reject it and revert the text. 
                sender.Text = sender.SelectedValue?.ToString();

                var dialog = new ContentDialog
                {
                    Content = Loc.Format("dlg_fontsize_range", "The font size must be a number between {0} and {1}.", 8, 200),
                    CloseButtonText = Loc.Get("dlg_close", "Close"),
                    DefaultButton = ContentDialogButton.Close
                };
                var task = dialog.ShowThemedAsync();
            }

            // Mark the event as handled so the framework doesn’t update the selected item automatically. 
            args.Handled = true;
            Editor.Focus(FocusState.Programmatic);
        }

        private void TextBoldButton_Click(object sender, RoutedEventArgs e)
        {
            Editor.FormatSelected(RichEditHelpers.FormattingMode.Bold);
            Editor.Focus(FocusState.Programmatic);
        }

        private void TextItalicButton_Click(object sender, RoutedEventArgs e)
        {
            Editor.FormatSelected(RichEditHelpers.FormattingMode.Italic);
            Editor.Focus(FocusState.Programmatic);
        }

        private void TextUnderlineButton_Click(object sender, RoutedEventArgs e)
        {
            Editor.FormatSelected(RichEditHelpers.FormattingMode.Underline);
            Editor.Focus(FocusState.Programmatic);
        }

        private void TextStrikethroughButton_Click(object sender, RoutedEventArgs e)
        {
            Editor.FormatSelected(RichEditHelpers.FormattingMode.Strikethrough);
            Editor.Focus(FocusState.Programmatic);
        }

        private void TextSubscriptButton_Click(object sender, RoutedEventArgs e)
        {
            Editor.FormatSelected(RichEditHelpers.FormattingMode.Subscript);
            Editor.Focus(FocusState.Programmatic);
        }

        private void TextSuperscriptButton_Click(object sender, RoutedEventArgs e)
        {
            Editor.FormatSelected(RichEditHelpers.FormattingMode.Superscript);
            Editor.Focus(FocusState.Programmatic);
        }

        private void FontEnlargeButton_Click(object sender, RoutedEventArgs e)
        {
            // Get a reference to the RichEditBox control
            RichEditBox richEditBox = Editor;

            // Increase the font size of the currently selected text by 2 points
            richEditBox.Document.Selection.CharacterFormat.Size += 2;
            // Get the index of the currently selected item
            fontSizeComboBox.SelectedItem = richEditBox.Document.Selection.CharacterFormat.Size;
        }

        private void FontDislargeButton_Click(object sender, RoutedEventArgs e)
        {
            // Get a reference to the RichEditBox control
            RichEditBox richEditBox = Editor;

            // Decrease the font size of the currently selected text by 2 points
            richEditBox.Document.Selection.CharacterFormat.Size -= 2;
            fontSizeComboBox.SelectedItem = richEditBox.Document.Selection.CharacterFormat.Size;
        }

        private void FontsComboBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (textboxwasalreadyloaded == false)
            {
                if (localSettings.Values["FontFamily"] is string fontSetting)
                {
                    FontsComboBox.SelectedItem = fontSetting;
                    Editor.FontFamily = new FontFamily(fontSetting);
                }
                else
                {
                    FontsComboBox.SelectedItem = "Calibri";
                    Editor.FontFamily = new FontFamily("Calibri");
                }
                double points = 11;
                if (localSettings.Values["fontsize"] is string sizeSetting &&
                    double.TryParse(sizeSetting, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed) && parsed > 0)
                    points = parsed;
                Editor.FontSize = points * 96.0 / 72.0;
                textboxwasalreadyloaded = true;
            }
        }

        private void Editor_SelectionChanged(object sender, RoutedEventArgs args)
        {
            TextBoldButton.IsChecked = Editor.Document.Selection.CharacterFormat.Bold == FormatEffect.On;

            TextItalicButton.IsChecked = Editor.Document.Selection.CharacterFormat.Italic == FormatEffect.On;

            TextUnderlineButton.IsChecked = Editor.Document.Selection.CharacterFormat.Underline != UnderlineType.None &&
                                        Editor.Document.Selection.CharacterFormat.Underline != UnderlineType.Undefined;

            TextStrikethroughButton.IsChecked = Editor.Document.Selection.CharacterFormat.Strikethrough == FormatEffect.On;

            TextSubscriptButton.IsChecked = Editor.Document.Selection.CharacterFormat.Subscript == FormatEffect.On;

            TextSuperscriptButton.IsChecked = Editor.Document.Selection.CharacterFormat.Superscript == FormatEffect.On;

            if (Editor.Document.Selection.CharacterFormat.Size > 0)
                // Font size is negative when selection contains multiple font sizes
                fontSizeComboBox.Text = Editor.Document.Selection.CharacterFormat.Size.ToString();

            // Prevent accidental font changes when selection contains multiple styles
            updateFontFormat = false;
            ShowFontName(Editor.Document.Selection.CharacterFormat.Name);
            updateFontFormat = true;
        }

        private void ShowFontName(string name)
        {
            name = name?.Trim() ?? "";
            string match = null;
            if (FontsComboBox.ItemsSource is System.Collections.Generic.IEnumerable<string> fonts)
                match = System.Linq.Enumerable.FirstOrDefault(fonts, f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                FontsComboBox.SelectedItem = match;
            }
            else
            {
                FontsComboBox.SelectedIndex = -1;
                FontsComboBox.Text = name;
            }
        }

        private static readonly string[][] TextColors =
        {
            new[] { "FFFFFF", "White" }, new[] { "A5A5A5", "Light gray" }, new[] { "7F7F7F", "Gray" }, new[] { "666666", "Dark gray" }, new[] { "333333", "Charcoal" }, new[] { "000000", "Black" },
            new[] { "FF0000", "Red" }, new[] { "FFC000", "Orange" }, new[] { "FFFF00", "Yellow" }, new[] { "00B050", "Green" }, new[] { "004DBB", "Blue" }, new[] { "9B00D3", "Purple" },
            new[] { "C0504D", "Dark red" }, new[] { "F79646", "Light orange" }, new[] { "9BBB59", "Olive green" }, new[] { "4BACC6", "Aqua" }, new[] { "4F81BD", "Cornflower blue" }, new[] { "8064A2", "Lavender" },
            new[] { "D16349", "Brick" }, new[] { "D19049", "Ochre" }, new[] { "CCB400", "Gold" }, new[] { "8FB08C", "Sage" }, new[] { "646B86", "Slate" }, new[] { "9E7C7C", "Rose brown" },
            new[] { "DD8484", "Rose" }, new[] { "F3A447", "Tangerine" }, new[] { "DFCE04", "Lemon" }, new[] { "A5B592", "Moss" }, new[] { "809EC2", "Steel blue" }, new[] { "9C85C0", "Lilac" },
        };

        private static readonly string[][] HighlightColors =
        {
            new[] { "FFFF00", "Yellow" }, new[] { "00FF00", "Bright green" }, new[] { "00FFFF", "Turquoise" }, new[] { "FF00FF", "Pink" }, new[] { "0000FF", "Blue" },
            new[] { "FF0000", "Red" }, new[] { "000080", "Dark blue" }, new[] { "008080", "Teal" }, new[] { "008000", "Green" }, new[] { "800080", "Violet" },
            new[] { "800000", "Dark red" }, new[] { "808000", "Dark yellow" }, new[] { "808080", "Gray 50%" }, new[] { "C0C0C0", "Gray 25%" }, new[] { "000000", "Black" },
        };

        private bool _palettesBuilt;

        private void BuildPalettes()
        {
            if (_palettesBuilt) return;
            _palettesBuilt = true;
            foreach (string[] c in TextColors) colorGrid.Children.Add(CreateSwatch(c[0], c[1], TextSwatch_Click));
            foreach (string[] c in HighlightColors) colorGrid2.Children.Add(CreateSwatch(c[0], c[1], HighlightSwatch_Click));

            FontTextColorDropDownButton.Flyout.Opening += (s, e) => MarkSwatch(colorGrid, Editor.Document.Selection.CharacterFormat.ForegroundColor, false);
            FontBackgroundColorDropDownButton.Flyout.Opening += (s, e) => MarkSwatch(colorGrid2, Editor.Document.Selection.CharacterFormat.BackgroundColor, true);
        }

        private Button CreateSwatch(string hex, string name, RoutedEventHandler click)
        {
            Windows.UI.Color color = ParseHex(hex);
            var ring = new Windows.UI.Xaml.Shapes.Ellipse
            {
                Width = 32, Height = 32, Opacity = 0,
                Style = (Style)Application.Current.Resources["PaintSwatchRingStyle"]
            };
            var swatch = new Windows.UI.Xaml.Shapes.Ellipse
            {
                Width = 24, Height = 24,
                Fill = new SolidColorBrush(color),
                Style = (Style)Application.Current.Resources["PaintSwatchEllipseStyle"]
            };
            var grid = new Grid { Width = 32, Height = 32 };
            grid.Children.Add(ring);
            grid.Children.Add(swatch);

            var button = new Button
            {
                Style = (Style)Application.Current.Resources["PaintSwatchButtonStyle"],
                Width = 34, Height = 34, Padding = new Thickness(0), Margin = new Thickness(1),
                CornerRadius = new CornerRadius(17),
                Content = grid, Tag = color
            };
            string label = Loc.Get("colorname_" + hex.ToLowerInvariant(), name);
            ToolTipService.SetToolTip(button, label);
            Windows.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
            button.Click += click;
            return button;
        }

        private static Windows.UI.Color ParseHex(string hex)
        {
            byte r = Convert.ToByte(hex.Substring(0, 2), 16), g = Convert.ToByte(hex.Substring(2, 2), 16), b = Convert.ToByte(hex.Substring(4, 2), 16);
            return Windows.UI.Color.FromArgb(255, r, g, b);
        }

        private void MarkSwatch(Panel grid, Windows.UI.Color current, bool highlight)
        {
            if (!highlight && Editor.ActualTheme == ElementTheme.Dark && current.R == 255 && current.G == 255 && current.B == 255)
                current = Windows.UI.Colors.Black;
            foreach (Button b in grid.Children.OfType<Button>())
            {
                bool selected = current.A == 255 && b.Tag is Windows.UI.Color c && c.R == current.R && c.G == current.G && c.B == current.B;
                if (b.Content is Grid g && g.Children.Count > 0) g.Children[0].Opacity = selected ? 1 : 0;
            }
        }

        private void TextSwatch_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.Tag is Windows.UI.Color color)) return;
            Windows.UI.Color applied = Editor.ActualTheme == ElementTheme.Dark && color.R == 0 && color.G == 0 && color.B == 0 ? Windows.UI.Colors.White : color;
            Editor.Document.Selection.CharacterFormat.ForegroundColor = applied;
            FontColorMarker.Foreground = new SolidColorBrush(color);
            FontTextColorDropDownButton.Flyout.Hide();
            Editor.Focus(FocusState.Keyboard);
        }

        private void HighlightSwatch_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.Tag is Windows.UI.Color color)) return;
            Editor.Document.Selection.CharacterFormat.BackgroundColor = color;
            BackTextColorMarker.Foreground = new SolidColorBrush(color);
            FontBackgroundColorDropDownButton.Flyout.Hide();
            Editor.Focus(FocusState.Keyboard);
        }

        private void Autobutton_Click(object sender, RoutedEventArgs e)
        {
            Windows.UI.Color color = Editor.ActualTheme == ElementTheme.Dark ? Windows.UI.Colors.White : Windows.UI.Colors.Black;
            Editor.Document.Selection.CharacterFormat.ForegroundColor = color;
            FontColorMarker.Foreground = new SolidColorBrush(Windows.UI.Colors.Black);
            FontTextColorDropDownButton.Flyout.Hide();
            Editor.Focus(FocusState.Programmatic);
        }

        private void NoColorButton_Click(object sender, RoutedEventArgs e)
        {
            Editor.Document.Selection.CharacterFormat.BackgroundColor = Windows.UI.Colors.Transparent;
            BackTextColorMarker.Foreground = new SolidColorBrush(Windows.UI.Colors.Transparent);
            FontBackgroundColorDropDownButton.Flyout.Hide();
            Editor.Focus(FocusState.Programmatic);
        }
    }
}
