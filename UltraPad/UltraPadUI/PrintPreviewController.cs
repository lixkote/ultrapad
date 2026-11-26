using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using UltraPad.Helpers;

namespace UltraPad.UltraPadUI
{
    public sealed class PrintPreviewController
    {
        private const double Gap = 12;
        private static readonly double[] ZoomSteps = { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0, 3.0, 4.0 };

        private readonly Grid _host;
        private readonly ScrollViewer _scroller;
        private readonly StackPanel _pagesPanel;

        private List<string> _pages = new List<string>();
        private PageGeometry _geometry;
        private Style _editorStyle;
        private bool _pageNumbers;
        private int _version;

        public int PageCount => _pages.Count;
        public int CurrentPage { get; private set; }
        public bool TwoPages { get; private set; }
        public double Zoom { get; private set; }
        public bool IsBusy { get; private set; }

        public IReadOnlyList<string> Pages => _pages;
        public PageGeometry Geometry => _geometry;

        public bool CanZoomIn => !IsBusy && NextZoom(true) > 0;
        public bool CanZoomOut => !IsBusy && Zoom > 0;
        public bool CanShowOnePage => !IsBusy && PageCount > 0;
        public bool CanShowTwoPages => !IsBusy && PageCount > 0;
        public bool CanGoBack => !IsBusy && CurrentPage > 0;
        public bool CanGoForward => !IsBusy && CurrentPage + PagesShown < PageCount;

        private int PagesShown => TwoPages ? 2 : 1;

        public event Action StateChanged;

        public PrintPreviewController(Grid host, ScrollViewer scroller, StackPanel pagesPanel)
        {
            _host = host;
            _scroller = scroller;
            _pagesPanel = pagesPanel;
            _scroller.SizeChanged += (s, e) => Layout();
            _host.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(Host_PointerWheelChanged), true);
            _pagesPanel.Tapped += (s, e) => { ToggleActualSize(); e.Handled = true; };
        }

        public async Task ShowAsync(RichEditBox source, Func<string, string> rtfFilter)
        {
            int version = ++_version;
            IsBusy = true;
            _pagesPanel.Children.Clear();
            StateChanged?.Invoke();

            _geometry = PageGeometry.FromPageSettings();
            _editorStyle = source.Style;
            _pageNumbers = PageSettings.PrintPageNumbers;
            List<string> pages = await DocumentPaginator.PaginateAsync(source, _host, _geometry, rtfFilter);
            if (version != _version) return;

            _pages = pages;
            CurrentPage = 0;
            TwoPages = false;
            Zoom = 0;
            IsBusy = false;
            Render();
        }

        public void Clear()
        {
            _version++;
            IsBusy = false;
            _pages = new List<string>();
            _pagesPanel.Children.Clear();
        }

        public bool IsActualSize => Zoom > 0 && Math.Abs(Zoom - 1.0) < 0.001;

        public void ShowActualSize()
        {
            if (IsBusy || PageCount == 0 || IsActualSize) return;
            Zoom = 1.0;
            Layout();
            StateChanged?.Invoke();
        }

        public void ToggleActualSize()
        {
            if (IsBusy || PageCount == 0) return;
            Zoom = IsActualSize ? 0 : 1.0;
            Layout();
            StateChanged?.Invoke();
        }

        public void ZoomIn()
        {
            double next = NextZoom(true);
            if (next > 0) { Zoom = next; Layout(); StateChanged?.Invoke(); }
        }

        public void ZoomOut()
        {
            if (Zoom <= 0) return;
            double next = NextZoom(false);
            Zoom = next;
            Layout();
            StateChanged?.Invoke();
        }

        public void ShowOnePage()
        {
            if (!TwoPages && Zoom <= 0) return;
            TwoPages = false;
            Zoom = 0;
            Render();
        }

        public void ShowTwoPages()
        {
            if ((TwoPages && Zoom <= 0) || PageCount == 0) return;
            TwoPages = true;
            Zoom = 0;
            if (CurrentPage % 2 == 1) CurrentPage--;
            if (CurrentPage + 1 >= PageCount) CurrentPage = Math.Max(0, PageCount - 2);
            Render();
        }

        public void GoBack()
        {
            if (!CanGoBack) return;
            CurrentPage = Math.Max(0, CurrentPage - PagesShown);
            Render();
        }

        public void GoForward()
        {
            if (!CanGoForward) return;
            CurrentPage = Math.Min(PageCount - 1, CurrentPage + PagesShown);
            Render();
        }

        public void GoTo(int page)
        {
            page = Math.Max(0, Math.Min(PageCount - 1, page));
            if (TwoPages && page % 2 == 1) page--;
            if (page == CurrentPage) return;
            CurrentPage = page;
            Render();
        }

        public bool HandleKey(VirtualKey key)
        {
            switch (key)
            {
                case VirtualKey.PageDown:
                case VirtualKey.Down when Zoom <= 0:
                case VirtualKey.Right when Zoom <= 0:
                    GoForward(); return true;
                case VirtualKey.PageUp:
                case VirtualKey.Up when Zoom <= 0:
                case VirtualKey.Left when Zoom <= 0:
                    GoBack(); return true;
                case VirtualKey.Home:
                    GoTo(0); return true;
                case VirtualKey.End:
                    GoTo(PageCount - 1); return true;
                case VirtualKey.Add:
                    ZoomIn(); return true;
                case VirtualKey.Subtract:
                    ZoomOut(); return true;
            }
            return false;
        }

        private void Host_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            if (IsBusy || PageCount == 0) return;
            var point = e.GetCurrentPoint(_host);
            int delta = point.Properties.MouseWheelDelta;
            bool ctrl = (e.KeyModifiers & VirtualKeyModifiers.Control) == VirtualKeyModifiers.Control;

            if (ctrl)
            {
                if (delta > 0) ZoomIn(); else ZoomOut();
                e.Handled = true;
                return;
            }

            if (Zoom <= 0)
            {
                if (delta < 0) GoForward(); else GoBack();
                e.Handled = true;
            }
        }

        private double FitScale()
        {
            if (_geometry == null) return 1;
            double w = Math.Max(1, _scroller.ActualWidth - 2 * Gap);
            double h = Math.Max(1, _scroller.ActualHeight - 2 * Gap);
            int n = PagesShown;
            double sx = (w - (n - 1) * Gap) / (n * _geometry.PageWidth);
            double sy = h / _geometry.PageHeight;
            return Math.Max(0.05, Math.Min(sx, sy));
        }

        private double NextZoom(bool larger)
        {
            double fit = FitScale();
            double current = Zoom > 0 ? Zoom : fit;
            if (larger)
            {
                foreach (double step in ZoomSteps)
                    if (step > current + 0.01 && step > fit + 0.01) return step;
                return 0;
            }

            for (int i = ZoomSteps.Length - 1; i >= 0; i--)
                if (ZoomSteps[i] < current - 0.01 && ZoomSteps[i] > fit + 0.01) return ZoomSteps[i];
            return 0;
        }

        private void Render()
        {
            _pagesPanel.Children.Clear();
            if (_geometry != null)
            {
                for (int i = CurrentPage; i < Math.Min(PageCount, CurrentPage + PagesShown); i++)
                {
                    FrameworkElement page = DocumentPaginator.CreatePage(_pages[i], _geometry, _editorStyle, i + 1, _pageNumbers);
                    var frame = new Border
                    {
                        BorderThickness = new Thickness(1),
                        Child = new Viewbox { Stretch = Stretch.Uniform, Child = page }
                    };
                    frame.SetValue(Border.BorderBrushProperty, Application.Current.Resources.TryGetValue("CardStrokeColorDefaultBrush", out object brush) ? brush : null);
                    _pagesPanel.Children.Add(frame);
                }

                if (TwoPages && CurrentPage + 1 >= PageCount)
                    _pagesPanel.Children.Add(new Border { BorderThickness = new Thickness(1) });
            }
            Layout();
            StateChanged?.Invoke();
        }

        private void Layout()
        {
            if (_geometry == null) return;
            double scale = Zoom > 0 ? Zoom : FitScale();
            foreach (Border frame in _pagesPanel.Children.OfType<Border>())
            {
                frame.Width = Math.Round(_geometry.PageWidth * scale) + 2;
                frame.Height = Math.Round(_geometry.PageHeight * scale) + 2;
            }
            _pagesPanel.Spacing = Gap;
            _pagesPanel.Margin = new Thickness(Gap - 1);
            _scroller.HorizontalScrollBarVisibility = Zoom > 0 ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
            _scroller.VerticalScrollBarVisibility = Zoom > 0 ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
            StateChanged?.Invoke();
        }
    }
}
