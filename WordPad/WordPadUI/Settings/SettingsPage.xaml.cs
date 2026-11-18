using DocumentFormat.OpenXml.Drawing;
using RectifyPad;
using System;
using Windows.Storage;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace WordPad.WordPadUI.Settings
{
    public sealed partial class SettingsPage : Page
    {
        private ApplicationDataContainer localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
        public SettingsPage()
        {
            this.InitializeComponent();
            WordWrapIcon.FontFamily = (FontFamily)Application.Current.Resources["CustomIconFont"];
        }

        protected override void OnNavigatedTo(Windows.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            Window.Current.SetTitleBar(AppTitleBar);

            // Initialize theme radio buttons
            InitializeThemeRadioButtons();

            // Initialize unit radio buttons
            InitializeUnitRadioButtons();

            // Initialize text wrapping radio buttons
            InitializeWrapRadioButtons();

            if (localSettings.Values["IsDarkThemeEditor"] != null)
            {
                EditorDarkModeToggle.IsOn = (bool)Windows.Storage.ApplicationData.Current.LocalSettings.Values["IsDarkThemeEditor"];
            }
            if (localSettings.Values["isSpellCheckEnabled"] != null)
            {
                SpellCheckToggle.IsOn = (bool)Windows.Storage.ApplicationData.Current.LocalSettings.Values["isSpellCheckEnabled"];
            }
            if (localSettings.Values["isTextPredictEnabled"] != null)
            {
                AutocorrectToggle.IsOn = (bool)Windows.Storage.ApplicationData.Current.LocalSettings.Values["isTextPredictEnabled"];
            }
        }

        private void InitializeThemeRadioButtons()
        {
            string selectedTheme = (string)localSettings.Values["theme"];
            if (!string.IsNullOrEmpty(selectedTheme))
            {
                // Find the RadioButton with a matching Tag
                foreach (var item in radiocontainer.Children)
                {
                    if (item is RadioButton radioButton && radioButton.Tag is string tag && tag == selectedTheme)
                    {
                        radioButton.IsChecked = true;
                        ApplyTheme(selectedTheme);
                        break; // Exit the loop once a match is found
                    }
                }
            }
        }

        private void InitializeWrapRadioButtons()
        {
            string selectedWrap = (string)localSettings.Values["textwrapping"];
            if (!string.IsNullOrEmpty(selectedWrap))
            {
                // Find the RadioButton with a matching Tag
                foreach (var item in wrapradiocontainer.Items)
                {
                    if (item is RadioButton radioButton && radioButton.Tag is string tag && tag == selectedWrap)
                    {
                        radioButton.IsChecked = true;
                        break; // Exit the loop once a match is found
                    }
                }
            }
        }

        private void InitializeUnitRadioButtons()
        {
            string selectedUnit = (string)localSettings.Values["unit"];
            if (!string.IsNullOrEmpty(selectedUnit))
            {
                // Find the RadioButton with a matching Tag
                foreach (var item in unitradiocontainer.Items)
                {
                    if (item is RadioButton radioButton && radioButton.Tag is string tag && tag == selectedUnit)
                    {
                        radioButton.IsChecked = true;
                        break; // Exit the loop once a match is found
                    }
                }
            }
        }

        private RadioButton FindRadioButtonByTag(string tag)
        {
            foreach (var item in radiocontainer.Children)
            {
                if (item is RadioButton rb && rb.Tag.ToString() == tag)
                {
                    return rb;
                }
            }
            return null;
        }

        private void ApplyTheme(string selectedTheme)
        {
            App.ApplyAppTheme(selectedTheme);
        }

        private void RadioButton_Checked(object sender, RoutedEventArgs e)
        {
            string selectedTheme = ((RadioButton)sender)?.Tag?.ToString();
            ApplyTheme(selectedTheme);

            // Save the selected theme in app data
            localSettings.Values["theme"] = selectedTheme;
        }

        private void UnitRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            string selectedUnit = ((RadioButton)sender)?.Tag?.ToString();

            // Save the selected unit in app data
            localSettings.Values["unit"] = selectedUnit;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(MainPage));
        }

        private async void feedback_Click(object sender, RoutedEventArgs e)
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/lixkote/UltraPad/issues"));
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(MainPage));
        }

        private void WrapRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            string selectedWrap = ((RadioButton)sender)?.Tag?.ToString();

            // Save the selected unit in app data
            localSettings.Values["textwrapping"] = selectedWrap;
        }

        private void SpellCheckToggle_Toggled(object sender, RoutedEventArgs e)
        {
            bool isSpellCheck = SpellCheckToggle.IsOn;

            // Save the setting
            localSettings.Values["isSpellCheckEnabled"] = isSpellCheck;

            // Apply the theme using ThemeManager
            SettingsPageManager.SetSpellCheck(isSpellCheck);
        }

        private void EditorDarkModeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            bool isDarkTheme = EditorDarkModeToggle.IsOn;

            // Save the setting
            localSettings.Values["IsDarkThemeEditor"] = isDarkTheme;

            // Apply the theme using ThemeManager
            SettingsPageManager.SetTheme(isDarkTheme);
        }

        private void AutocorrectToggle_Toggled(object sender, RoutedEventArgs e)
        {
            bool isPredict = AutocorrectToggle.IsOn;

            // Save the setting
            localSettings.Values["isTextPredictEnabled"] = isPredict;

            // Apply the theme using ThemeManager
            SettingsPageManager.SetPredict(isPredict);
        }
        private void AdvFeatureToggle_Toggled(object sender, RoutedEventArgs e)
        {
        }

        private async void donatebutton_Click(object sender, RoutedEventArgs e)
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri("https://ko-fi.com/lixkote"));
        }
    }
}
