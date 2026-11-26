using System;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Text;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Input;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;
using UltraPad.Helpers;

namespace UltraPad.UltraPadUI
{
    public sealed class PictureAdorner
    {
        private const double HandleSize = 9;
        private const double MinSize = 8;

        private readonly RichEditBox _editor;
        private readonly Canvas _canvas;
        private readonly Rectangle _frame;
        private readonly Rectangle[] _handles = new Rectangle[8];

        private int _start = -1;

        private bool _infoValid;
        private int _infoStart = -1;
        private PictureHelper.PictureInfo _info;
        private bool _infoOk;
        private Rect _rect;
        private bool _visible;

        private int _dragHandle = -1;
        private Point _dragStart;
        private Rect _dragRect;

        private readonly DispatcherTimer _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        private string _dragRtf;
        private PictureHelper.PictureInfo _dragInfo;
        private double _dragWidth, _dragHeight;
        private double _pendingWidth, _pendingHeight;
        private int _appliedScaleX, _appliedScaleY;
        private bool _undoGroupOpen;

        private static readonly CoreCursorType[] Cursors =
        {
            CoreCursorType.SizeNorthwestSoutheast, CoreCursorType.SizeNorthSouth, CoreCursorType.SizeNortheastSouthwest, CoreCursorType.SizeWestEast,
            CoreCursorType.SizeNorthwestSoutheast, CoreCursorType.SizeNorthSouth, CoreCursorType.SizeNortheastSouthwest, CoreCursorType.SizeWestEast
        };

        public PictureAdorner(RichEditBox editor, Canvas canvas)
        {
            _editor = editor;
            _canvas = canvas;

            Color accent = Color.FromArgb(255, 0, 120, 212);
            try { accent = new UISettings().GetColorValue(UIColorType.Accent); } catch { }
            var accentBrush = new SolidColorBrush(accent);

            _frame = new Rectangle
            {
                Stroke = accentBrush,
                StrokeThickness = 1,
                Fill = new SolidColorBrush(Colors.Transparent),
                Visibility = Visibility.Collapsed
            };
            _frame.PointerPressed += Frame_PointerPressed;
            _frame.PointerMoved += Frame_PointerMoved;
            _frame.PointerReleased += Frame_PointerReleased;
            _frame.PointerCaptureLost += (s, e) => EndMove(false);
            _frame.DoubleTapped += Frame_DoubleTapped;
            _frame.RightTapped += Frame_RightTapped;
            _canvas.Children.Add(_frame);

            _dropCaret = new Rectangle
            {
                Width = 2,
                Fill = new SolidColorBrush(Colors.Black),
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed
            };
            _canvas.Children.Add(_dropCaret);

            for (int i = 0; i < 8; i++)
            {
                var handle = new Rectangle
                {
                    Width = HandleSize,
                    Height = HandleSize,
                    RadiusX = 2,
                    RadiusY = 2,
                    Fill = new SolidColorBrush(Colors.White),
                    Stroke = accentBrush,
                    StrokeThickness = 1,
                    Visibility = Visibility.Collapsed,
                    Tag = i
                };
                handle.PointerEntered += Handle_PointerEntered;
                handle.PointerExited += Handle_PointerExited;
                handle.PointerPressed += Handle_PointerPressed;
                handle.PointerMoved += Handle_PointerMoved;
                handle.PointerReleased += Handle_PointerReleased;
                handle.PointerCaptureLost += Handle_PointerCaptureLost;
                _handles[i] = handle;
                _canvas.Children.Add(handle);
            }

            _editor.SelectionChanged += (s, e) => OnSelectionChanged();
            _editor.TextChanged += (s, e) => { if (_dragHandle < 0) { _infoValid = false; ValidateObject(); } Update(); };
            _editor.PreviewKeyDown += Editor_PreviewKeyDown;
            PictureHelper.AdornerSelection = ed => ed == _editor ? SelectedObjectStart : -1;
            _editor.SizeChanged += (s, e) => Update();
            _editor.LayoutUpdated += (s, e) => { if (_visible) Update(); };
            _liveTimer.Tick += (s, e) => ApplyLive();

            _editor.Loaded += (s, e) => HookEditorScrolling();
            HookEditorScrolling();

            _editor.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(Editor_PointerReleased), true);
        }

        private ScrollViewer _editorScroller;

        private void HookEditorScrolling()
        {
            ScrollViewer scroller = EditorCoordinates.GetScrollViewer(_editor);
            if (scroller == null || scroller == _editorScroller) return;
            if (_editorScroller != null)
            {
                _editorScroller.ViewChanging -= EditorScroller_ViewChanging;
                _editorScroller.ViewChanged -= EditorScroller_ViewChanged;
            }
            _editorScroller = scroller;
            _editorScroller.ViewChanging += EditorScroller_ViewChanging;
            _editorScroller.ViewChanged += EditorScroller_ViewChanged;
        }

        private void EditorScroller_ViewChanging(object sender, ScrollViewerViewChangingEventArgs e) => TrackScrolling();

        private void EditorScroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e) => TrackScrolling();

        private DateTime _trackUntil;
        private bool _tracking;

        public void TrackScrolling()
        {
            _trackUntil = DateTime.UtcNow.AddMilliseconds(300);
            ForceUpdate();
            if (_tracking) return;
            _tracking = true;
            CompositionTarget.Rendering += CompositionTarget_Rendering;
        }

        private void CompositionTarget_Rendering(object sender, object e)
        {
            if (DateTime.UtcNow > _trackUntil)
            {
                CompositionTarget.Rendering -= CompositionTarget_Rendering;
                _tracking = false;
            }
            if (_visible || _objSelected) Update();
        }

        public void ForceUpdate()
        {
            if (!_visible && !_objSelected) return;
            _rect = Rect.Empty;
            Update();
        }

        private void Editor_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_dragHandle >= 0) return;
            try
            {
                Point client = EditorCoordinates.ToClient(_editor, _editor, e.GetCurrentPoint(_editor).Position);
                ITextSelection sel = _editor.Document.Selection;
                if (sel.Length != 0) return;

                ITextRange hit = _editor.Document.GetRangeFromPoint(client, PointOptions.ClientCoordinates);
                int p = hit.StartPosition;
                foreach (int start in new[] { p, p - 1 })
                {
                    if (start < 0) continue;
                    ITextRange obj = _editor.Document.GetRange(start, start + 1);
                    obj.GetText(TextGetOptions.None, out string text);
                    if (text != "\uFFFC") continue;
                    obj.GetRect(PointOptions.ClientCoordinates, out Rect r, out _);
                    if (r.Width < 1 || client.X < r.Left - 1 || client.X > r.Right + 1 || client.Y < r.Top - 1 || client.Y > r.Bottom + 1) continue;

                    sel.SetRange(start, start + 1);
                    _infoValid = false;
                    _ = _editor.Dispatcher.RunAsync(CoreDispatcherPriority.Low, ForceUpdate);
                    return;
                }
            }
            catch { }
        }

        #region Selection

        private ITextRange _objRange;
        private bool _objSelected;
        private int _expectedCaret = -1;
        private bool _materialized;
        private int _materializeId;

        public int SelectedObjectStart => _objSelected && _objRange != null ? _objRange.StartPosition : -1;

        private static bool IsObjectRange(ITextRange r)
        {
            if (r == null || r.Length != 1) return false;
            r.GetText(TextGetOptions.None, out string text);
            return text == PictureHelper.ObjectChar.ToString();
        }

        private void OnSelectionChanged()
        {
            if (_dragHandle >= 0) { Update(); return; }
            try
            {
                ITextSelection sel = _editor.Document.Selection;
                int start = sel.StartPosition, end = sel.EndPosition;

                if (_objSelected)
                {
                    int objStart = _objRange.StartPosition;
                    if ((start == end && start == _expectedCaret) ||
                        (_materialized && start == objStart && end == objStart + 1))
                    {
                        Update();
                        return;
                    }
                }
                _materialized = false;

                if (end - start == 1 && IsObjectRange(_editor.Document.GetRange(start, end)))
                {
                    SelectObject(start);
                    return;
                }

                if (_objSelected) Deselect(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("PictureAdorner selection: " + ex.Message);
            }
            _infoValid = false;
            Update();
        }

        private void SelectObject(int start)
        {
            if (!IsObjectRange(_editor.Document.GetRange(start, start + 1))) return;
            bool same = _objSelected && _objRange.StartPosition == start;
            _objRange = _editor.Document.GetRange(start, start + 1);
            _objSelected = true;
            _materialized = false;
            if (!same) _infoValid = false;
            CollapseTo(start);
            try { _editor.Document.CaretType = CaretType.Null; } catch { }
            Update();
        }

        public void Deselect(bool moveCaret, int caret = -1)
        {
            if (!_objSelected) return;
            _objSelected = false;
            _materialized = false;
            _expectedCaret = -1;
            try { _editor.Document.CaretType = CaretType.Normal; } catch { }
            if (moveCaret && caret >= 0) _editor.Document.Selection.SetRange(caret, caret);
            Hide();
        }

        private void CollapseTo(int position)
        {
            _expectedCaret = position;
            ITextSelection sel = _editor.Document.Selection;
            if (sel.StartPosition != position || sel.EndPosition != position) sel.SetRange(position, position);
        }

        private void ValidateObject()
        {
            if (!_objSelected) return;
            if (!IsObjectRange(_objRange)) Deselect(false);
        }

        public bool Materialize(bool restoreAfter)
        {
            if (!_objSelected || !IsObjectRange(_objRange)) return false;
            int start = _objRange.StartPosition;
            int id = ++_materializeId;
            _materialized = true;
            _editor.Document.Selection.SetRange(start, start + 1);
            if (restoreAfter)
                _ = _editor.Dispatcher.RunIdleAsync(a => { if (id == _materializeId) RestoreAfterCommand(start); });
            return true;
        }

        public void EndCommand()
        {
            if (_objSelected && _materialized && _objRange != null) RestoreAfterCommand(_objRange.StartPosition);
        }

        private void RestoreAfterCommand(int start)
        {
            if (!_materialized || !_objSelected) return;
            ITextSelection sel = _editor.Document.Selection;
            if (sel.StartPosition == start && sel.EndPosition == start + 1 && IsObjectRange(_objRange))
            {
                _materialized = false;
                CollapseTo(start);
            }
        }

        private void Editor_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (!_objSelected || _dragHandle >= 0) return;
            CoreWindow window = Window.Current.CoreWindow;
            bool Down(Windows.System.VirtualKey k) => (window.GetKeyState(k) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
            bool shift = Down(Windows.System.VirtualKey.Shift), ctrl = Down(Windows.System.VirtualKey.Control), alt = Down(Windows.System.VirtualKey.Menu);

            if (!IsObjectRange(_objRange)) { Deselect(false); return; }
            int start = _objRange.StartPosition;

            switch (e.Key)
            {
                case Windows.System.VirtualKey.Shift:
                case Windows.System.VirtualKey.Control:
                case Windows.System.VirtualKey.Menu:
                case Windows.System.VirtualKey.LeftShift:
                case Windows.System.VirtualKey.RightShift:
                case Windows.System.VirtualKey.LeftControl:
                case Windows.System.VirtualKey.RightControl:
                case Windows.System.VirtualKey.LeftMenu:
                case Windows.System.VirtualKey.RightMenu:
                case Windows.System.VirtualKey.LeftWindows:
                case Windows.System.VirtualKey.RightWindows:
                case Windows.System.VirtualKey.CapitalLock:
                    return;

                case Windows.System.VirtualKey.Delete:
                case Windows.System.VirtualKey.Back:
                    if (ctrl || alt) break;
                    Deselect(false);
                    _editor.Document.GetRange(start, start + 1).SetText(TextSetOptions.None, "");
                    _editor.Document.Selection.SetRange(start, start);
                    e.Handled = true;
                    return;

                case Windows.System.VirtualKey.Escape:
                    Deselect(true, start);
                    e.Handled = true;
                    return;

                case Windows.System.VirtualKey.Left:
                case Windows.System.VirtualKey.Right:
                    if (shift) break;
                    Deselect(true, e.Key == Windows.System.VirtualKey.Left ? start : start + 1);
                    e.Handled = true;
                    return;

                case Windows.System.VirtualKey.Up:
                case Windows.System.VirtualKey.Down:
                case Windows.System.VirtualKey.Home:
                case Windows.System.VirtualKey.End:
                case Windows.System.VirtualKey.PageUp:
                case Windows.System.VirtualKey.PageDown:
                    if (shift) break;
                    Deselect(true, start);
                    return;
            }

            Materialize(true);
        }

        #endregion

        #region Moving

        private readonly Rectangle _dropCaret;
        private bool _movePressed, _moving;
        private Point _movePressPoint;
        private int _dropPosition = -1;

        private void Frame_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            PointerPoint point = e.GetCurrentPoint(null);
            if (!point.Properties.IsLeftButtonPressed || !_objSelected) return;
            _movePressed = true;
            _moving = false;
            _movePressPoint = point.Position;
            _frame.CapturePointer(e.Pointer);
            _editor.Focus(FocusState.Pointer);
            e.Handled = true;
        }

        private void Frame_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_movePressed) return;
            Point p = e.GetCurrentPoint(null).Position;
            if (!_moving && Math.Abs(p.X - _movePressPoint.X) < 4 && Math.Abs(p.Y - _movePressPoint.Y) < 4) return;
            _moving = true;

            _dropPosition = -1;
            _dropCaret.Visibility = Visibility.Collapsed;
            try
            {
                Point client = EditorCoordinates.ToClient(_editor, _editor, e.GetCurrentPoint(_editor).Position);
                int pos = _editor.Document.GetRangeFromPoint(client, PointOptions.ClientCoordinates).StartPosition;
                int start = _objRange.StartPosition;
                if (pos != start && pos != start + 1)
                {
                    _editor.Document.GetRange(pos, pos).GetRect(PointOptions.ClientCoordinates, out Rect r, out _);
                    Rect c = EditorCoordinates.FromClient(_editor, _canvas, r);
                    _dropCaret.Height = Math.Max(8, c.Height);
                    Canvas.SetLeft(_dropCaret, Math.Round(c.X - 1));
                    Canvas.SetTop(_dropCaret, Math.Round(c.Y));
                    _dropCaret.Fill = new SolidColorBrush(_editor.ActualTheme == ElementTheme.Dark ? Colors.White : Colors.Black);
                    _dropCaret.Visibility = Visibility.Visible;
                    _dropPosition = pos;
                }
            }
            catch { }
            e.Handled = true;
        }

        private void Frame_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!_movePressed) return;
            EndMove(true);
            _frame.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
        }

        private void EndMove(bool drop)
        {
            if (!_movePressed) return;
            bool moving = _moving;
            int target = _dropPosition;
            _movePressed = false;
            _moving = false;
            _dropPosition = -1;
            _dropCaret.Visibility = Visibility.Collapsed;
            if (drop && moving && target >= 0) MovePicture(target);
        }

        private void MovePicture(int target)
        {
            if (!_objSelected || !IsObjectRange(_objRange)) return;
            int start = _objRange.StartPosition;
            ITextRange source = _editor.Document.GetRange(start, start + 1);
            source.GetText(TextGetOptions.FormatRtf, out string rtf);
            if (string.IsNullOrEmpty(rtf)) return;

            _editor.Document.BeginUndoGroup();
            try
            {
                Deselect(false);
                EmbeddedObjectManager objects = EmbeddedObjectManager.For(_editor);
                object link = objects.Detach(start);
                source.SetText(TextSetOptions.None, string.Empty);
                int at = target > start ? target - 1 : target;
                ITextRange insert = _editor.Document.GetRange(at, at);
                insert.SetText(TextSetOptions.FormatRtf, rtf);

                insert.GetText(TextGetOptions.None, out string inserted);
                int offset = inserted?.IndexOf(PictureHelper.ObjectChar) ?? -1;
                if (offset < 0) return;
                int pictureAt = insert.StartPosition + offset;
                int tail = insert.EndPosition - (pictureAt + 1);
                if (tail > 0) _editor.Document.GetRange(pictureAt + 1, pictureAt + 1 + tail).Text = string.Empty;
                if (offset > 0) { _editor.Document.GetRange(insert.StartPosition, pictureAt).Text = string.Empty; pictureAt = insert.StartPosition; }

                objects.Reattach(link, pictureAt);
                _infoValid = false;
                SelectObject(pictureAt);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("PictureAdorner.MovePicture: " + ex.Message);
            }
            finally
            {
                _editor.Document.EndUndoGroup();
            }
        }

        private async void Frame_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            e.Handled = true;
            await EmbeddedObjectManager.For(_editor).TryActivateSelectedObjectAsync();
        }

        private void Frame_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            if (_editor.ContextFlyout is FlyoutBase flyout)
            {
                e.Handled = true;
                flyout.ShowAt(_editor, new FlyoutShowOptions { Position = e.GetPosition(_editor) });
            }
        }

        #endregion

        public void Update()
        {
            bool dragging = _dragHandle >= 0;
            if (dragging) return;
            if (!_objSelected)
            {
                Hide();
                return;
            }
            if (!IsObjectRange(_objRange))
            {
                Deselect(false);
                return;
            }

            try
            {
                int start = dragging ? _start : _objRange.StartPosition;
                ITextRange range = _editor.Document.GetRange(start, start + 1);
                range.GetRect(PointOptions.ClientCoordinates, out Rect line, out int hit);
                if (line.Width < 1 || line.Height < 1)
                {
                    if (!dragging) Hide();
                    return;
                }

                if (!dragging && (!_infoValid || _infoStart != start))
                {
                    _infoOk = PictureHelper.TryGetPicture(_editor, start, out _info, out _);
                    _infoStart = start;
                    _infoValid = true;
                }

                Rect rect = PictureRect(range, line);
                rect = EditorCoordinates.FromClient(_editor, _canvas, rect);

                _start = start;
                if (_visible && Math.Abs(rect.X - _rect.X) < 0.1 && Math.Abs(rect.Y - _rect.Y) < 0.1 &&
                    Math.Abs(rect.Width - _rect.Width) < 0.1 && Math.Abs(rect.Height - _rect.Height) < 0.1) return;

                _rect = rect;
                Place(rect);
                Show();
            }
            catch
            {
                if (!dragging) Hide();
            }
        }

        private Rect PictureRect(ITextRange range, Rect line)
        {
            double width = line.Width;
            double height = double.NaN;
            if (_infoOk && _info.WidthDip > 0 && _info.HeightDip > 0)
            {
                double ratio = width / _info.WidthDip;
                if (ratio < 0.5 || ratio > 2 || Math.Abs(ratio - 1) < 0.1) ratio = 1;
                width = _info.WidthDip * ratio;
                height = _info.HeightDip * ratio;
            }

            const PointOptions start = PointOptions.ClientCoordinates | PointOptions.Start;
            double cellBottom = double.NaN, baseline = double.NaN;
            try { range.GetPoint(HorizontalCharacterAlignment.Left, VerticalCharacterAlignment.Bottom, start, out Point p); cellBottom = p.Y; } catch { }
            try { range.GetPoint(HorizontalCharacterAlignment.Left, VerticalCharacterAlignment.Baseline, start, out Point p); baseline = p.Y; } catch { }

            bool InLine(double y) => !double.IsNaN(y) && y >= line.Top - 0.5 && y <= line.Bottom + 0.5;

            if (_infoOk && _info.IsObject && InLine(baseline) && baseline - line.Top > 4)
                return new Rect(line.X, line.Top, Math.Max(1, width), baseline - line.Top);

            if (double.IsNaN(height))
            {
                double bottom = InLine(baseline) ? baseline : line.Bottom;
                return new Rect(line.X, line.Top, Math.Max(1, width), Math.Max(1, bottom - line.Top));
            }

            if (InLine(baseline) && baseline - line.Top - height > 2)
                return new Rect(line.X, baseline - height, Math.Max(1, width), height);
            return new Rect(line.X, line.Top, Math.Max(1, width), height);
        }

        public void Hide()
        {
            if (!_visible && _frame.Visibility == Visibility.Collapsed) return;
            _visible = false;
            _frame.Visibility = Visibility.Collapsed;
            foreach (Rectangle h in _handles) h.Visibility = Visibility.Collapsed;
        }

        private void Show()
        {
            _visible = true;
            _frame.Visibility = Visibility.Visible;
            foreach (Rectangle h in _handles) h.Visibility = Visibility.Visible;
        }

        private void Place(Rect r)
        {
            Rect clip;
            try { clip = _editor.TransformToVisual(_canvas).TransformBounds(new Rect(0, 0, Math.Max(0, _editor.ActualWidth), Math.Max(0, _editor.ActualHeight))); }
            catch { clip = new Rect(0, 0, Math.Max(0, _editor.ActualWidth), Math.Max(0, _editor.ActualHeight)); }
            _canvas.Clip = new RectangleGeometry { Rect = clip };

            Canvas.SetLeft(_frame, r.X);
            Canvas.SetTop(_frame, r.Y);
            _frame.Width = Math.Max(1, r.Width);
            _frame.Height = Math.Max(1, r.Height);

            double l = r.X, t = r.Y, cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, rr = r.X + r.Width, b = r.Y + r.Height;
            Point[] points = { new Point(l, t), new Point(cx, t), new Point(rr, t), new Point(rr, cy), new Point(rr, b), new Point(cx, b), new Point(l, b), new Point(l, cy) };
            for (int i = 0; i < 8; i++)
            {
                Canvas.SetLeft(_handles[i], Math.Round(points[i].X - HandleSize / 2));
                Canvas.SetTop(_handles[i], Math.Round(points[i].Y - HandleSize / 2));
            }
        }

        private static int IndexOf(object sender) => (int)((FrameworkElement)sender).Tag;

        private void Handle_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            Window.Current.CoreWindow.PointerCursor = new CoreCursor(Cursors[IndexOf(sender)], 0);
        }

        private void Handle_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (_dragHandle < 0) Window.Current.CoreWindow.PointerCursor = new CoreCursor(CoreCursorType.Arrow, 0);
        }

        public bool IsDragging => _dragHandle >= 0;

        private void Handle_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!e.GetCurrentPoint(_canvas).Properties.IsLeftButtonPressed || _start < 0) return;
            _dragHandle = IndexOf(sender);
            _dragStart = e.GetCurrentPoint(null).Position;
            _dragRect = _rect;

            double zoom = CanvasScale();
            _dragWidth = _rect.Width / zoom;
            _dragHeight = _rect.Height / zoom;
            _pendingWidth = _dragWidth;
            _pendingHeight = _dragHeight;

            _dragRtf = null;
            if (PictureHelper.TryGetPicture(_editor, _start, out _dragInfo, out string rtf))
            {
                _dragRtf = rtf;
                _appliedScaleX = _dragInfo.ScaleX;
                _appliedScaleY = _dragInfo.ScaleY;
                _info = _dragInfo;
                _infoOk = true;
                _infoStart = _start;
                _infoValid = true;
            }

            ((UIElement)sender).CapturePointer(e.Pointer);
            _liveTimer.Interval = TimeSpan.FromMilliseconds(40);
            _liveTimer.Start();
            e.Handled = true;
        }

        private double WindowScale()
        {
            try
            {
                Rect r = _canvas.TransformToVisual(null).TransformBounds(new Rect(0, 0, 100, 100));
                return r.Width > 1 ? r.Width / 100.0 : 1;
            }
            catch
            {
                return 1;
            }
        }

        private double CanvasScale()
        {
            try
            {
                Rect r = _editor.TransformToVisual(_canvas).TransformBounds(new Rect(0, 0, 100, 100));
                return r.Width > 1 ? r.Width / 100.0 : 1;
            }
            catch
            {
                return 1;
            }
        }

        private void Handle_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_dragHandle < 0) return;
            Point p = e.GetCurrentPoint(null).Position;
            double zoom = CanvasScale();
            double toWindow = WindowScale();
            double dx = (p.X - _dragStart.X) / toWindow / zoom, dy = (p.Y - _dragStart.Y) / toWindow / zoom;

            int hx = (_dragHandle == 2 || _dragHandle == 3 || _dragHandle == 4) ? 1 : (_dragHandle == 0 || _dragHandle == 6 || _dragHandle == 7) ? -1 : 0;
            int hy = (_dragHandle == 4 || _dragHandle == 5 || _dragHandle == 6) ? 1 : (_dragHandle == 0 || _dragHandle == 1 || _dragHandle == 2) ? -1 : 0;

            double w = Math.Max(MinSize, _dragWidth + hx * dx);
            double h = Math.Max(MinSize, _dragHeight + hy * dy);

            bool corner = _dragHandle % 2 == 0;
            bool shift = (Window.Current.CoreWindow.GetKeyState(Windows.System.VirtualKey.Shift) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
            if (corner && !shift && _dragWidth > 0 && _dragHeight > 0)
            {
                double ratio = _dragHeight / _dragWidth;
                if (w * ratio > h) h = w * ratio; else w = h / ratio;
            }

            _pendingWidth = w;
            _pendingHeight = h;

            if (_dragRtf == null)
            {
                double x = hx < 0 ? _dragRect.Right - w * zoom : _dragRect.X;
                double y = hy < 0 ? _dragRect.Bottom - h * zoom : _dragRect.Y;
                _rect = new Rect(x, y, w * zoom, h * zoom);
            }
            else
            {
                _rect = new Rect(_dragRect.X, _dragRect.Y, w * zoom, h * zoom);
            }
            Place(_rect);
            e.Handled = true;
        }

        private void ApplyLive()
        {
            if (_dragHandle < 0 || _dragRtf == null || _dragInfo.GoalWidthTwips <= 0 || _dragInfo.GoalHeightTwips <= 0) return;

            double kx = _dragInfo.ScaleX / _dragWidth, ky = _dragInfo.ScaleY / _dragHeight;
            int sx = Math.Max(1, (int)Math.Round(_pendingWidth * kx));
            int sy = Math.Max(1, (int)Math.Round(_pendingHeight * ky));
            if (sx == _appliedScaleX && sy == _appliedScaleY) return;

            if (!_undoGroupOpen)
            {
                _editor.Document.BeginUndoGroup();
                _undoGroupOpen = true;
            }

            var watch = System.Diagnostics.Stopwatch.StartNew();
            bool applied = PictureHelper.ApplyScale(_editor, _start, _dragRtf, sx, sy, select: false);
            _liveTimer.Interval = TimeSpan.FromMilliseconds(Math.Min(400, Math.Max(40, watch.ElapsedMilliseconds * 3)));
            if (applied)
            {
                if (_objSelected) _objRange = _editor.Document.GetRange(_start, _start + 1);
                _appliedScaleX = sx;
                _appliedScaleY = sy;
                _info.ScaleX = sx;
                _info.ScaleY = sy;
                _infoOk = true;
                _infoStart = _start;
                _infoValid = true;
            }
        }

        private void Handle_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_dragHandle < 0) return;
            ((UIElement)sender).ReleasePointerCapture(e.Pointer);
            FinishDrag();
            e.Handled = true;
        }

        private void Handle_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (_dragHandle >= 0) FinishDrag();
        }

        private void FinishDrag()
        {
            _liveTimer.Stop();
            int start = _start;

            if (_dragRtf != null)
            {
                ApplyLive();
                if (_undoGroupOpen)
                {
                    _editor.Document.EndUndoGroup();
                    _undoGroupOpen = false;
                }
                _dragHandle = -1;
            }
            else
            {
                bool changed = Math.Abs(_pendingWidth - _dragWidth) > 0.5 || Math.Abs(_pendingHeight - _dragHeight) > 0.5;
                _dragHandle = -1;
                if (changed && start >= 0) PictureHelper.SetSize(_editor, start, _pendingWidth, _pendingHeight);
            }

            _dragRtf = null;
            Window.Current.CoreWindow.PointerCursor = new CoreCursor(CoreCursorType.Arrow, 0);
            _editor.Focus(FocusState.Programmatic);

            _visible = false;
            _infoValid = false;
            _ = _editor.Dispatcher.RunAsync(CoreDispatcherPriority.Low, Update);
        }
    }
}
