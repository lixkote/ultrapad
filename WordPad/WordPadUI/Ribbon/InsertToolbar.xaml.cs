using System;
using WordPad.Helpers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage.Streams;
using Windows.Storage;
using Windows.System;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using Windows.Storage.Pickers;
using Windows.ApplicationModel.Core;
using Windows.Management.Deployment;

// Szablon elementu Kontrolka użytkownika jest udokumentowany na stronie https://go.microsoft.com/fwlink/?LinkId=234236

namespace WordPad.WordPadUI.Ribbon
{
    public sealed partial class InsertToolbar : UserControl
    {
        public RichEditBox Editor { get; set; }
        public InsertToolbar()
        {
            this.InitializeComponent();
        }

        private async void InsertDateAndTimeButton_Click(object sender, RoutedEventArgs e)
        {
            ContentDialog dialog = new ContentDialog();
            dialog.Title = WordPad.Helpers.Loc.Get("dlg_datetime_title", "Date and time");

            // Create a ListView for the user to select the date format
            ListView listView = new ListView();
            listView.SelectionMode = ListViewSelectionMode.Single;

            // Create a list of date formats to display in the ListView
            List<string> dateFormats = new List<string>();
            dateFormats.Add(DateTime.Now.ToString("dd.MM.yyyy"));
            dateFormats.Add(DateTime.Now.ToString("dd MMM yyyy"));
            dateFormats.Add(DateTime.Now.ToString("dddd, dd MMMM yyyy"));
            dateFormats.Add(DateTime.Now.ToString("dd MMMM yyyy"));
            dateFormats.Add(DateTime.Now.ToString("HH:mm:ss"));

            // Set the ItemsSource of the ListView to the list of date formats
            listView.ItemsSource = dateFormats;

            // Set the content of the ContentDialog to the ListView
            dialog.Content = listView;

            // Make the insert button colored
            dialog.DefaultButton = ContentDialogButton.Primary;

            // Add an "Insert" button to the ContentDialog
            dialog.PrimaryButtonText = WordPad.Helpers.Loc.Get("dlg_ok", "OK");
            dialog.PrimaryButtonClick += (s, args) =>
            {
                string selectedFormat = listView.SelectedItem as string;
                try
                {
                    string formattedDate = dateFormats[listView.SelectedIndex];
                    Editor.Document.Selection.Text = formattedDate;
                    Editor.Focus(FocusState.Keyboard);
                }
                catch
                {
                }
            };

            // Add a "Cancel" button to the ContentDialog
            dialog.SecondaryButtonText = WordPad.Helpers.Loc.Get("dlg_cancel", "Cancel");

            // Show the ContentDialog
            await dialog.ShowThemedAsync();
        }

        static async Task<AppListEntry> GetAppByPackageFamilyNameAsync(string packageFamilyName)
        {
            var pkgManager = new PackageManager();
            var pkg = pkgManager.FindPackagesForUser("", packageFamilyName).FirstOrDefault();

            if (pkg == null) return null;

            var apps = await pkg.GetAppListEntriesAsync();
            var firstApp = apps.FirstOrDefault();
            return firstApp;
        }

        private async void InsertObjectButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new WordPad.WordPadUI.ObjectInsertion();
            ContentDialogResult result = await dialog.ShowThemedAsync();
            if (result == ContentDialogResult.Primary || dialog.ConfirmedByDoubleClick)
            {
                await dialog.ExecuteAsync(Editor);
            }
        }

        private void PictureMenu_Opening(object sender, object e)
        {
            bool picture = Editor != null && WordPad.Helpers.PictureHelper.IsPictureSelected(Editor);
            MenuChangePicture.IsEnabled = picture;
            MenuResizePicture.IsEnabled = picture;
        }

        private async void MenuChangePicture_Click(object sender, RoutedEventArgs e)
        {
            if (Editor != null) await WordPad.Helpers.PictureHelper.ChangePictureAsync(Editor);
        }

        private async void MenuResizePicture_Click(object sender, RoutedEventArgs e)
        {
            if (Editor != null) await WordPad.Helpers.PictureHelper.ShowResizeDialogAsync(Editor);
        }

        private async void InsertImageButton_Click(object sender, RoutedEventArgs e)
        {
            FileOpenPicker open = new FileOpenPicker();
            open.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            foreach (string extension in WordPad.Helpers.ImageInsertHelper.SupportedExtensions) open.FileTypeFilter.Add(extension);

            StorageFile file = await open.PickSingleFileAsync();

            if (file != null)
            {
                if (!await WordPad.Helpers.ImageInsertHelper.InsertFileAsync(Editor, file, selectPicture: true))
                    await WordPad.Helpers.ImageInsertHelper.ShowLastErrorAsync();
            }
        }

        private async void InsertPaintImageButton_Click(object sender, RoutedEventArgs e)
        {
            await WordPad.Helpers.EmbeddedObjectManager.For(Editor).InsertNewPaintPictureAsync(".png");
        }
    }
}
