using System;
using System.Collections.Generic;
using System.Diagnostics;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;
using Windows.UI.Xaml.Markup;
using UltraPad.Helpers;

namespace UltraPad.UltraPadUI
{
    public sealed partial class TextRuler : UserControl
    {
        private enum DragTarget { None, FirstIndent, HangingIndent, LeftIndent, RightIndent, Tab }

        private const float Undefined = -9999999f;

        private RichEditBox _editor;

        private double _pageWidth = 816;
        private double _leftMargin = 120;
        private double _rightMargin = 120;

        private double _pageX;
        private double _scale = 1;

        private float _first, _left, _right;
        private bool _indentsKnown;
        private readonly List<TabStop> _tabs = new List<TabStop>();
        private bool _tabsKnown;

        private DragTarget _drag = DragTarget.None;
        private TabStop _dragTab;
        private float _dragTabOriginal = Undefined;
        private bool _dragTabRemoving;
        private bool _dragMoved;
        private Point _dragStart;

        private struct TabStop
        {
            public float Position;
            public TabAlignment Align;
            public TabLeader Leader;
        }

        public RichEditBox TargetEditor
        {
            get { return _editor; }
            set
            {
                if (_editor != null)
                {
                    _editor.SelectionChanged -= Editor_SelectionChanged;
                    _editor.TextChanged -= Editor_TextChanged;
                    _editor.LayoutUpdated -= Editor_LayoutUpdated;
                }
                _editor = value;
                if (_editor != null)
                {
                    _editor.SelectionChanged += Editor_SelectionChanged;
                    _editor.TextChanged += Editor_TextChanged;
                    _editor.LayoutUpdated += Editor_LayoutUpdated;
                }
                RefreshFromSelection();
            }
        }

        public event Action<double?> GuideChanged;

        public event EventHandler ParagraphDialogRequested;

        public event EventHandler TabsDialogRequested;

        public TextRuler()
        {
            InitializeComponent();
            PageSettings.UnitChanged += unit => Redraw();
        }

        #region Public API

        public void SetPage(double pageWidth, double leftMargin, double rightMargin)
        {
            _pageWidth = Math.Max(1, pageWidth);
            _leftMargin = Math.Max(0, leftMargin);
            _rightMargin = Math.Max(0, rightMargin);
            UpdatePagePosition(true);
        }

        public void UpdatePagePosition(bool forceRedraw = false)
        {
            double pageX = _pageX, scale = _scale;
            try
            {
                if (_editor != null && _editor.ActualWidth > 0 && RulerCanvas.ActualWidth > 0)
                {
                    GeneralTransform t = _editor.TransformToVisual(RulerCanvas);
                    Point p0 = t.TransformPoint(new Point(0, 0));
                    Point p1 = t.TransformPoint(new Point(_editor.ActualWidth, 0));
                    pageX = p0.X;
                    scale = (p1.X - p0.X) / _editor.ActualWidth;
                    if (scale <= 0 || double.IsNaN(scale)) scale = 1;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("TextRuler.UpdatePagePosition: " + ex.Message);
            }

            if (forceRedraw || Math.Abs(pageX - _pageX) > 0.25 || Math.Abs(scale - _scale) > 0.0005)
            {
                _pageX = pageX;
                _scale = scale;
                Redraw();
            }
        }

        public void RefreshFromSelection()
        {
            if (_drag != DragTarget.None) return;

            _indentsKnown = false;
            _tabsKnown = false;
            _tabs.Clear();

            if (_editor != null)
            {
                try
                {
                    ITextParagraphFormat f = _editor.Document.Selection.ParagraphFormat;
                    float first = f.FirstLineIndent, left = f.LeftIndent, right = f.RightIndent;
                    _indentsKnown = IsKnown(first) && IsKnown(left) && IsKnown(right);
                    _first = IsKnown(first) ? first : 0;
                    _left = IsKnown(left) ? left : 0;
                    _right = IsKnown(right) ? right : 0;

                    int count = f.TabCount;
                    if (count >= 0 && count < 64)
                    {
                        _tabsKnown = true;
                        for (int i = 0; i < count; i++)
                        {
                            f.GetTab(i, out float pos, out TabAlignment align, out TabLeader leader);
                            if (IsKnown(pos)) _tabs.Add(new TabStop { Position = pos, Align = align, Leader = leader });
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("TextRuler.RefreshFromSelection: " + ex.Message);
                }
            }

            RedrawMarkers();
        }

        #endregion

        #region Geometry helpers

        private static bool IsKnown(float v) => !float.IsNaN(v) && v > -1000000f && v < 1000000f;

        private double TextLeftX => _pageX + _leftMargin * _scale;
        private double TextRightX => _pageX + (_pageWidth - _rightMargin) * _scale;
        private double TextWidthPoints => Math.Max(0, PageSettings.DipToPoints(_pageWidth - _leftMargin - _rightMargin));

        private double XFromPoints(double points) => TextLeftX + PageSettings.PointsToDip(points) * _scale;

        private double PointsFromX(double x) => PageSettings.DipToPoints((x - TextLeftX) / _scale);

        private double FirstX => XFromPoints(_left + _first);
        private double LeftX => XFromPoints(_left);
        private double RightX => TextRightX - PageSettings.PointsToDip(_right) * _scale;

        #endregion

        #region Drawing

        private void Redraw()
        {
            double width = RulerCanvas.ActualWidth;
            double height = RulerCanvas.ActualHeight;
            if (width <= 0) return;

            RulerCanvas.Clip = new RectangleGeometry { Rect = new Rect(0, 0, width, Math.Max(0, height)) };

            double pageW = _pageWidth * _scale;
            Canvas.SetLeft(PageBar, _pageX);
            PageBar.Width = Math.Max(0, pageW);

            Canvas.SetLeft(TextArea, TextLeftX);
            TextArea.Width = Math.Max(0, TextRightX - TextLeftX);

            DrawTicks();
            RedrawMarkers();
        }

        private void DrawTicks()
        {
            TickLayer.Children.Clear();

            MeasureUnit unit = PageSettings.Unit;
            RulerGrid grid = PageSettings.GetRulerGrid(unit, _scale);
            double dipPerTwip = _scale / PageSettings.TwipsPerDip;
            if (grid.MinorStep <= 0 || dipPerTwip <= 0) return;

            double origin = TextLeftX;
            double pageLeft = _pageX + 3;
            double pageRight = _pageX + _pageWidth * _scale - 3;
            double visibleLeft = -20, visibleRight = RulerCanvas.ActualWidth + 20;

            var tickStyle = (Style)Resources["RulerTickStyle"];
            var labelStyle = (Style)Resources["RulerLabelStyle"];

            foreach (int dir in new[] { 1, -1 })
            {
                for (int k = 1; k < 4000; k++)
                {
                    double twips = k * grid.MinorStep;
                    double x = origin + dir * twips * dipPerTwip;
                    if (x > pageRight || x < pageLeft) break;
                    if (x < visibleLeft || x > visibleRight) continue;

                    bool isLabel = IsMultiple(twips, grid.LabelStep);
                    bool isMajor = IsMultiple(twips, grid.MajorStep);
                    bool isHalf = grid.HalfTicks && IsMultiple(twips, grid.MajorStep / 2);

                    if (isLabel)
                    {
                        int number = (int)Math.Round(twips / grid.UnitTwips);
                        var label = new TextBlock { Style = labelStyle, Text = number.ToString() };
                        Canvas.SetLeft(label, x - 15);
                        Canvas.SetTop(label, 12);
                        TickLayer.Children.Add(label);
                        continue;
                    }

                    double h = isMajor ? 7 : isHalf ? 5 : 3;
                    var tick = new Rectangle { Style = tickStyle, Width = 1, Height = h, RadiusX = 0.5, RadiusY = 0.5 };
                    Canvas.SetLeft(tick, Math.Round(x));
                    Canvas.SetTop(tick, Math.Round(19 - h / 2));
                    TickLayer.Children.Add(tick);
                }
            }
        }

        private static bool IsMultiple(double value, double step)
        {
            if (step <= 0) return false;
            double r = value / step;
            return Math.Abs(r - Math.Round(r)) < 1e-6;
        }

        private void RedrawMarkers()
        {
            if (RulerCanvas.ActualWidth <= 0) return;

            double opacity = _indentsKnown || _drag != DragTarget.None ? 1.0 : 0.45;
            PlaceMarker(FirstIndentMarker, FirstX, opacity);
            PlaceMarker(HangingIndentMarker, LeftX, opacity);
            PlaceMarker(LeftIndentMarker, LeftX, opacity);
            PlaceMarker(RightIndentMarker, RightX, opacity);

            DrawTabs();
        }

        private static void PlaceMarker(FrameworkElement marker, double x, double opacity)
        {
            Canvas.SetLeft(marker, Math.Round(x) - marker.Width / 2);
            marker.Opacity = opacity;
        }

        private void DrawTabs()
        {
            TabLayer.Children.Clear();
            var markStyle = (Style)Resources["RulerTabMarkStyle"];
            var defaultStyle = (Style)Resources["RulerDefaultTabStyle"];

            float last = 0;
            if (_tabsKnown)
            {
                foreach (TabStop tab in _tabs)
                {
                    if (_drag == DragTarget.Tab && _dragTabRemoving && tab.Position == _dragTab.Position) continue;
                    DrawTab(tab, markStyle);
                    last = Math.Max(last, tab.Position);
                }
            }

            float defaultTab = 36f;
            try
            {
                if (_editor != null && _editor.Document.DefaultTabStop > 1) defaultTab = _editor.Document.DefaultTabStop;
            }
            catch { }

            double maxPoints = TextWidthPoints;
            int guard = 0;
            for (double pos = (Math.Floor(last / defaultTab) + 1) * defaultTab; pos < maxPoints && guard++ < 500; pos += defaultTab)
            {
                var mark = new Rectangle { Style = defaultStyle };
                Canvas.SetLeft(mark, Math.Round(XFromPoints(pos)) - 1);
                Canvas.SetTop(mark, 28);
                TabLayer.Children.Add(mark);
            }
        }

        private void DrawTab(TabStop tab, Style style)
        {
            double x = Math.Round(XFromPoints(tab.Position)) + 0.5;
            const double top = 20.5, bottom = 27.5, arm = 5;
            string data;
            switch (tab.Align)
            {
                case TabAlignment.Right:
                    data = $"M {F(x)},{F(top)} L {F(x)},{F(bottom)} L {F(x - arm)},{F(bottom)}";
                    break;
                case TabAlignment.Center:
                    data = $"M {F(x)},{F(top)} L {F(x)},{F(bottom)} M {F(x - arm)},{F(bottom)} L {F(x + arm)},{F(bottom)}";
                    break;
                case TabAlignment.Decimal:
                    data = $"M {F(x)},{F(top)} L {F(x)},{F(bottom)} M {F(x - arm)},{F(bottom)} L {F(x + arm)},{F(bottom)} M {F(x + 3)},{F(top + 2)} L {F(x + 3)},{F(top + 2)}";
                    break;
                default:
                    data = $"M {F(x)},{F(top)} L {F(x)},{F(bottom)} L {F(x + arm)},{F(bottom)}";
                    break;
            }

            var mark = new Path
            {
                Style = (Style)Resources["RulerTabPathStyle"],
                Data = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), data)
            };
            TabLayer.Children.Add(mark);
        }

        private static string F(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        #endregion

        #region Editor events

        private Throttle _refreshThrottle;

        private void RequestRefresh()
        {
            if (_refreshThrottle == null) _refreshThrottle = new Throttle(TimeSpan.FromMilliseconds(80), RefreshFromSelection);
            _refreshThrottle.Request();
        }

        private void Editor_SelectionChanged(object sender, RoutedEventArgs e) => RequestRefresh();

        private void Editor_TextChanged(object sender, RoutedEventArgs e) => RequestRefresh();

        private void Editor_LayoutUpdated(object sender, object e) => UpdatePagePosition();

        private void RulerCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => UpdatePagePosition(true);

        #endregion

        #region Mouse

        private static bool Near(double a, double b, double tolerance) => Math.Abs(a - b) <= tolerance;

        private void RulerCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_editor == null) return;
            var point = e.GetCurrentPoint(RulerCanvas);
            if (!point.Properties.IsLeftButtonPressed) return;

            Point p = point.Position;
            DragTarget target = HitTest(p, out TabStop hitTab);

            if (target == DragTarget.None)
            {
                if (p.X >= TextLeftX && p.X <= TextRightX && p.Y >= 6 && p.Y <= 32 && _tabsKnown && _tabs.Count < 32)
                {
                    float pos = (float)Snap(PointsFromX(p.X), e);
                    pos = (float)Math.Max(0, Math.Min(TextWidthPoints, pos));
                    hitTab = new TabStop { Position = pos, Align = TabAlignment.Left, Leader = TabLeader.Spaces };
                    _tabs.Add(hitTab);
                    target = DragTarget.Tab;
                    _dragTabOriginal = Undefined;
                    _dragMoved = true;
                }
                else
                {
                    return;
                }
            }
            else
            {
                _dragTabOriginal = target == DragTarget.Tab ? hitTab.Position : Undefined;
                _dragMoved = false;
            }

            _drag = target;
            _dragTab = hitTab;
            _dragTabRemoving = false;
            _dragStart = p;
            RulerCanvas.CapturePointer(e.Pointer);
            e.Handled = true;

            RedrawMarkers();
            GuideChanged?.Invoke(CurrentDragX());
        }

        private DragTarget HitTest(Point p, out TabStop hitTab)
        {
            hitTab = default(TabStop);

            if (p.Y >= 32 && Near(p.X, LeftX, 7)) return DragTarget.LeftIndent;
            if (p.Y <= 12 && Near(p.X, FirstX, 7)) return DragTarget.FirstIndent;
            if (p.Y >= 19 && p.Y < 32 && Near(p.X, LeftX, 7)) return DragTarget.HangingIndent;
            if (p.Y >= 19 && Near(p.X, RightX, 7)) return DragTarget.RightIndent;

            if (_tabsKnown && p.Y >= 12 && p.Y <= 32)
            {
                double best = 6;
                bool found = false;
                foreach (TabStop tab in _tabs)
                {
                    double d = Math.Abs(XFromPoints(tab.Position) - p.X);
                    if (d <= best) { best = d; hitTab = tab; found = true; }
                }
                if (found) return DragTarget.Tab;
            }
            return DragTarget.None;
        }

        private double Snap(double points, PointerRoutedEventArgs e)
        {
            bool alt = (Window.Current.CoreWindow.GetKeyState(VirtualKey.Menu) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
            if (alt) return points;
            double step = PageSettings.TwipsToPoints(PageSettings.SnapTwips(PageSettings.Unit));
            return Math.Round(points / step) * step;
        }

        private void RulerCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_drag == DragTarget.None) return;
            Point p = e.GetCurrentPoint(RulerCanvas).Position;
            if (!_dragMoved && Math.Abs(p.X - _dragStart.X) < 2 && Math.Abs(p.Y - _dragStart.Y) < 2) return;
            _dragMoved = true;

            double pos = Snap(PointsFromX(p.X), e);
            double textWidth = TextWidthPoints;
            const double minGap = 18;

            switch (_drag)
            {
                case DragTarget.FirstIndent:
                    {
                        double abs = Clamp(pos, 0, textWidth - _right - minGap);
                        _first = (float)(abs - _left);
                        break;
                    }
                case DragTarget.HangingIndent:
                    {
                        double firstAbs = _left + _first;
                        double newLeft = Clamp(pos, 0, textWidth - _right - minGap);
                        _left = (float)newLeft;
                        _first = (float)(firstAbs - newLeft);
                        break;
                    }
                case DragTarget.LeftIndent:
                    {
                        double newLeft = Clamp(pos, Math.Max(0, -_first), textWidth - _right - minGap - Math.Max(0, _first));
                        _left = (float)newLeft;
                        break;
                    }
                case DragTarget.RightIndent:
                    {
                        double maxRight = textWidth - Math.Max(_left, _left + _first) - minGap;
                        _right = (float)Clamp(textWidth - pos, 0, Math.Max(0, maxRight));
                        break;
                    }
                case DragTarget.Tab:
                    {
                        _dragTabRemoving = p.Y < -6 || p.Y > RulerCanvas.ActualHeight + 6;
                        float newPos = (float)Clamp(pos, 0, textWidth);
                        int index = _tabs.FindIndex(t => t.Position == _dragTab.Position);
                        _dragTab.Position = newPos;
                        if (index >= 0) _tabs[index] = _dragTab;
                        break;
                    }
            }

            RedrawMarkers();
            GuideChanged?.Invoke(_dragTabRemoving ? (double?)null : CurrentDragX());
        }

        private static double Clamp(double v, double min, double max) => max < min ? min : Math.Max(min, Math.Min(max, v));

        private double? CurrentDragX()
        {
            switch (_drag)
            {
                case DragTarget.FirstIndent: return FirstX;
                case DragTarget.HangingIndent:
                case DragTarget.LeftIndent: return LeftX;
                case DragTarget.RightIndent: return RightX;
                case DragTarget.Tab: return XFromPoints(_dragTab.Position);
                default: return null;
            }
        }

        private void RulerCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_drag == DragTarget.None) return;
            FinishDrag(apply: true);
            RulerCanvas.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
        }

        private void RulerCanvas_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (_drag != DragTarget.None) FinishDrag(apply: true);
        }

        private void FinishDrag(bool apply)
        {
            if (_drag == DragTarget.None) return;
            DragTarget target = _drag;
            bool moved = _dragMoved;
            _drag = DragTarget.None;
            GuideChanged?.Invoke(null);

            if (apply && moved && _editor != null)
            {
                try
                {
                    ApplyDrag(target);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("TextRuler.ApplyDrag: " + ex.Message);
                }
                _editor.Focus(FocusState.Programmatic);
            }

            _dragTabRemoving = false;
            _dragTabOriginal = Undefined;
            RefreshFromSelection();
        }

        private void ApplyDrag(DragTarget target)
        {
            float newFirstAbs = _left + _first;
            float newLeft = _left;
            float newRight = _right;
            TabStop tab = _dragTab;
            float original = _dragTabOriginal;
            bool removing = _dragTabRemoving;

            ParagraphHelper.ForEachSelectedParagraph(_editor, f =>
            {
                float first = f.FirstLineIndent, left = f.LeftIndent, right = f.RightIndent;
                if (!IsKnown(first)) first = 0;
                if (!IsKnown(left)) left = 0;
                if (!IsKnown(right)) right = 0;

                switch (target)
                {
                    case DragTarget.FirstIndent:
                        f.SetIndents(newFirstAbs - left, left, right);
                        break;
                    case DragTarget.HangingIndent:
                        f.SetIndents((left + first) - newLeft, newLeft, right);
                        break;
                    case DragTarget.LeftIndent:
                        f.SetIndents(first, newLeft, right);
                        break;
                    case DragTarget.RightIndent:
                        f.SetIndents(first, left, newRight);
                        break;
                    case DragTarget.Tab:
                        if (IsKnown(original)) f.DeleteTab(original);
                        if (!removing) f.AddTab(tab.Position, tab.Align, tab.Leader);
                        break;
                }
            });
        }

        private void RulerCanvas_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            Point p = e.GetPosition(RulerCanvas);
            DragTarget target = HitTest(p, out TabStop _);
            if (target == DragTarget.Tab)
            {
                TabsDialogRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
            else if (target != DragTarget.None)
            {
                ParagraphDialogRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
        }

        #endregion
    }
}
