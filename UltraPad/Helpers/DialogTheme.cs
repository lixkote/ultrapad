using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace UltraPad.Helpers
{
    public static class DialogTheme
    {
        public static IAsyncOperation<ContentDialogResult> ShowThemedAsync(this ContentDialog dialog)
        {
            Apply(dialog);
            return dialog.ShowAsync();
        }

        public static void Apply(FrameworkElement element)
        {
            if (element != null && Window.Current?.Content is FrameworkElement root)
                element.RequestedTheme = root.ActualTheme;
        }
    }
}
