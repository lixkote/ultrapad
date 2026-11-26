using System;
using System.Threading.Tasks;
using Windows.ApplicationModel.Resources;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using UltraPad.Helpers;

namespace UltraPad.UltraPadUI
{
    public sealed partial class ObjectInsertion : ContentDialog
    {
        private sealed class ObjectType
        {
            public InsertObjectKind Kind;
            public string Name;
            public override string ToString() => Name;
        }

        private StorageFile _file;

        public InsertObjectKind SelectedKind { get; private set; } = InsertObjectKind.PaintPicture;
        public bool CreateFromFile => CreateFromFileRadio.IsChecked == true;
        public bool DisplayAsIcon => DisplayAsIconCheck.IsChecked == true;
        public StorageFile SelectedFile => _file;

        public ObjectInsertion()
        {
            InitializeComponent();

            AddType(InsertObjectKind.BitmapImage, L("objtype_bitmap", "Bitmap Image"));
            AddType(InsertObjectKind.Package, L("objtype_package", "Package"));
            AddType(InsertObjectKind.PaintPicture, L("objtype_paint", "Paint Picture"));
            AddType(InsertObjectKind.Table, L("objtype_table", "Table"));
            ObjectTypeList.SelectedIndex = 2;

            UpdateUi();
        }

        private void AddType(InsertObjectKind kind, string name)
        {
            ObjectTypeList.Items.Add(new ObjectType { Kind = kind, Name = name });
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

        private void UpdateUi()
        {
            if (ObjectTypePanel == null || FilePanel == null || CreateFromFileRadio == null || DisplayAsIconCheck == null || ResultText == null || ResultIcon == null) return;
            bool fromFile = CreateFromFile;
            ObjectTypePanel.Visibility = fromFile ? Visibility.Collapsed : Visibility.Visible;
            FilePanel.Visibility = fromFile ? Visibility.Visible : Visibility.Collapsed;

            DisplayAsIconCheck.IsEnabled = fromFile;
            if (!fromFile) DisplayAsIconCheck.IsChecked = SelectedKind == InsertObjectKind.Package;

            string typeName = (ObjectTypeList.SelectedItem as ObjectType)?.Name ?? "";
            string text;
            string glyph;

            if (!fromFile)
            {
                text = string.Format(L("objresult_new", "Inserts a new {0} object into your document."), typeName);
                switch (SelectedKind)
                {
                    case InsertObjectKind.PaintPicture:
                    case InsertObjectKind.BitmapImage:
                        text += " " + L("objresult_paint", "The picture opens in Paint. Save it there to update the document, double-click it to edit it again.");
                        glyph = "";
                        break;
                    case InsertObjectKind.Package:
                        text += " " + L("objresult_package", "You will choose the file to package. It is displayed as an icon, double-click it to open the file.");
                        glyph = "";
                        break;
                    default:
                        glyph = "";
                        break;
                }
            }
            else if (DisplayAsIcon)
            {
                text = L("objresult_fileicon", "Inserts a picture of the file's icon into your document. Double-click it to open the file.");
                glyph = "";
            }
            else
            {
                text = L("objresult_file", "Inserts the contents of the file into your document. Pictures can be edited in Paint by double-clicking them; other files are inserted as an icon.");
                glyph = "";
            }

            ResultText.Text = text;
            ResultIcon.Glyph = glyph;
            IsPrimaryButtonEnabled = !fromFile || _file != null;
        }

        private void Mode_Checked(object sender, RoutedEventArgs e) => UpdateUi();

        private void DisplayAsIcon_Changed(object sender, RoutedEventArgs e) => UpdateUi();

        private void ObjectTypeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ObjectTypeList.SelectedItem is ObjectType type) SelectedKind = type.Kind;
            UpdateUi();
        }

        private void ObjectTypeList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (CreateFromFile || ObjectTypeList.SelectedItem == null) return;
            _okByDoubleClick = true;
            Hide();
        }

        private bool _okByDoubleClick;

        public bool ConfirmedByDoubleClick => _okByDoubleClick;

        private async void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeFilter.Add("*");
            StorageFile file = await picker.PickSingleFileAsync();
            if (file == null) return;
            _file = file;
            FilePathBox.Text = string.IsNullOrEmpty(file.Path) ? file.Name : file.Path;
            UpdateUi();
        }

        private void ObjectInsertion_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            if (CreateFromFile && _file == null) args.Cancel = true;
        }

        public async Task ExecuteAsync(RichEditBox editor)
        {
            EmbeddedObjectManager objects = EmbeddedObjectManager.For(editor);

            if (CreateFromFile)
            {
                await objects.InsertFromFileAsync(_file, DisplayAsIcon);
                return;
            }

            switch (SelectedKind)
            {
                case InsertObjectKind.PaintPicture:
                    await objects.InsertNewPaintPictureAsync(".png");
                    break;
                case InsertObjectKind.BitmapImage:
                    await objects.InsertNewPaintPictureAsync(".bmp");
                    break;
                case InsertObjectKind.Table:
                    objects.InsertTable();
                    break;
                case InsertObjectKind.Package:
                    var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
                    picker.FileTypeFilter.Add("*");
                    StorageFile file = await picker.PickSingleFileAsync();
                    if (file != null) await objects.InsertPackageAsync(file);
                    break;
            }
        }
    }
}
