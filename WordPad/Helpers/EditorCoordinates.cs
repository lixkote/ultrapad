using System;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace WordPad.Helpers
{
    public static class EditorCoordinates
    {
        private static WeakReference<RichEditBox> _cachedEditor;
        private static WeakReference<FrameworkElement> _cachedViewport;

        public static FrameworkElement GetViewport(RichEditBox editor)
        {
            if (_cachedEditor != null && _cachedEditor.TryGetTarget(out RichEditBox e) && e == editor &&
                _cachedViewport != null && _cachedViewport.TryGetTarget(out FrameworkElement v) &&
                VisualTreeHelper.GetParent(v) != null)
                return v;

            FrameworkElement found = null;
            ScrollViewer content = Find<ScrollViewer>(editor, "ContentElement") ?? Find<ScrollViewer>(editor, null);
            if (content != null) found = Find<ScrollContentPresenter>(content, null);

            if (found != null)
            {
                _cachedEditor = new WeakReference<RichEditBox>(editor);
                _cachedViewport = new WeakReference<FrameworkElement>(found);
            }
            return found;
        }

        public static ScrollViewer GetScrollViewer(RichEditBox editor)
        {
            return Find<ScrollViewer>(editor, "ContentElement") ?? Find<ScrollViewer>(editor, null);
        }

        private static T Find<T>(DependencyObject root, string name) where T : FrameworkElement
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is T t && (name == null || t.Name == name)) return t;
                T deeper = Find<T>(child, name);
                if (deeper != null) return deeper;
            }
            return null;
        }

        private static Point FallbackOffset(RichEditBox editor)
        {
            return new Point(editor.BorderThickness.Left + editor.Padding.Left, editor.BorderThickness.Top + editor.Padding.Top);
        }

        public static Point ToClient(RichEditBox editor, UIElement from, Point point)
        {
            try
            {
                FrameworkElement viewport = GetViewport(editor);
                if (viewport != null) return from.TransformToVisual(viewport).TransformPoint(point);

                Point p = from.TransformToVisual(editor).TransformPoint(point);
                Point o = FallbackOffset(editor);
                return new Point(p.X - o.X, p.Y - o.Y);
            }
            catch
            {
                return point;
            }
        }

        public static Rect FromClient(RichEditBox editor, UIElement to, Rect rect)
        {
            try
            {
                FrameworkElement viewport = GetViewport(editor);
                if (viewport != null) return viewport.TransformToVisual(to).TransformBounds(rect);

                Point o = FallbackOffset(editor);
                return editor.TransformToVisual(to).TransformBounds(new Rect(rect.X + o.X, rect.Y + o.Y, rect.Width, rect.Height));
            }
            catch
            {
                return rect;
            }
        }
    }
}
