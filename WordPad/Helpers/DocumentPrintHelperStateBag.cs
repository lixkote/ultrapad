
using Windows.System;
using Windows.UI.Xaml;
using Microsoft.Toolkit.Uwp;

namespace WordPad.Helpers
{
    internal class DocumentPrintHelperStateBag
    {
        private readonly DispatcherQueue _dispatcherQueue;

        internal DocumentPrintHelperStateBag(DispatcherQueue dispatcherQueue)
        {
            _dispatcherQueue = dispatcherQueue;
        }

        public HorizontalAlignment HorizontalAlignment { get; set; }

        public VerticalAlignment VerticalAlignment { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }

        public Thickness Margin { get; set; }

        public void Capture(FrameworkElement element)
        {
            HorizontalAlignment = element.HorizontalAlignment;
            VerticalAlignment = element.VerticalAlignment;
            Width = element.Width;
            Height = element.Height;
            Margin = element.Margin;
        }

        public void Restore(FrameworkElement element)
        {
            _dispatcherQueue.EnqueueAsync(() =>
            {
                element.HorizontalAlignment = HorizontalAlignment;
                element.VerticalAlignment = VerticalAlignment;
                element.Width = Width;
                element.Height = Height;
                element.Margin = Margin;
            });
        }
    }
}