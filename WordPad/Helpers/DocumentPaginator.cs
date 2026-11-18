using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace WordPad.Helpers
{
    public sealed class PageGeometry
    {
        public double PageWidth { get; set; }
        public double PageHeight { get; set; }
        public Thickness Margins { get; set; }
        public TextWrapping Wrap { get; set; } = TextWrapping.Wrap;

        public double TextWidth => Math.Max(16, PageWidth - Margins.Left - Margins.Right);
        public double TextHeight => Math.Max(16, PageHeight - Margins.Top - Margins.Bottom);

        public static PageGeometry FromPageSettings()
        {
            PageSettings.Size2 size = PageSettings.GetPageSizeTwips();
            Thickness m = PageSettings.GetMarginsTwips();
            return new PageGeometry
            {
                PageWidth = PageSettings.TwipsToDip(size.Width),
                PageHeight = PageSettings.TwipsToDip(size.Height),
                Margins = new Thickness(PageSettings.TwipsToDip(m.Left), PageSettings.TwipsToDip(m.Top),
                                        PageSettings.TwipsToDip(m.Right), PageSettings.TwipsToDip(m.Bottom)),
                Wrap = PageSettings.Wrap == WrapMode.NoWrap ? TextWrapping.NoWrap : TextWrapping.Wrap
            };
        }
    }

    public static class DocumentPaginator
    {
        public const int MaxPages = 999;

        public static async Task<List<string>> PaginateAsync(RichEditBox source, Panel measureHost, PageGeometry page, Func<string, string> rtfFilter = null)
        {
            var pages = new List<string>();
            RichEditBox box = CreatePageEditor(page, source.Style);
            box.Opacity = 0;
            box.IsHitTestVisible = false;
            box.HorizontalAlignment = HorizontalAlignment.Left;
            box.VerticalAlignment = VerticalAlignment.Top;
            if (measureHost is Grid grid)
            {
                Grid.SetRowSpan(box, Math.Max(1, grid.RowDefinitions.Count));
                Grid.SetColumnSpan(box, Math.Max(1, grid.ColumnDefinitions.Count));
            }
            measureHost.Children.Add(box);

            try
            {
                await WaitLoadedAsync(box);

                int docEnd = source.Document.GetRange(0, TextConstants.MaxUnitCount).EndPosition;
                int pos = 0;
                while (pos < docEnd && pages.Count < MaxPages)
                {
                    source.Document.GetRange(pos, docEnd).GetText(TextGetOptions.FormatRtf, out string rest);
                    box.Document.SetText(TextSetOptions.FormatRtf, rest);
                    box.UpdateLayout();
                    await box.Dispatcher.RunAsync(CoreDispatcherPriority.Low, () => { });

                    int remaining = docEnd - pos;
                    int brk = FindPageBreak(box, page);

                    if (brk <= 0 || brk >= remaining - 1)
                    {
                        if (brk <= 0 && remaining > 1)
                        {
                            brk = FirstLineEnd(box);
                            if (brk > 0 && brk < remaining - 1)
                            {
                                pages.Add(Filter(GetRtf(source, pos, pos + brk), rtfFilter));
                                pos += brk;
                                continue;
                            }
                        }
                        pages.Add(Filter(rest, rtfFilter));
                        break;
                    }

                    pages.Add(Filter(GetRtf(source, pos, pos + brk), rtfFilter));
                    pos += brk;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("DocumentPaginator: " + ex.Message);
                if (pages.Count == 0)
                {
                    source.Document.GetText(TextGetOptions.FormatRtf, out string all);
                    pages.Add(Filter(all, rtfFilter));
                }
            }
            finally
            {
                measureHost.Children.Remove(box);
            }

            if (pages.Count == 0) pages.Add(string.Empty);
            return pages;
        }

        private static string Filter(string rtf, Func<string, string> filter) => filter == null ? rtf : filter(rtf);

        private static string GetRtf(RichEditBox source, int start, int end)
        {
            source.Document.GetRange(start, end).GetText(TextGetOptions.FormatRtf, out string rtf);
            return rtf;
        }

        private static int FindPageBreak(RichEditBox box, PageGeometry page)
        {
            double w = page.TextWidth, h = page.TextHeight;
            ITextRange r = box.Document.GetRangeFromPoint(new Point(Math.Max(1, w - 2), Math.Max(1, h - 1)), PointOptions.ClientCoordinates);
            r.Expand(TextRangeUnit.Line);
            r.GetRect(PointOptions.ClientCoordinates, out Rect line, out _);

            if (line.Bottom > h + 0.5) return r.StartPosition;
            return r.EndPosition;
        }

        private static int FirstLineEnd(RichEditBox box)
        {
            ITextRange r = box.Document.GetRange(0, 0);
            r.Expand(TextRangeUnit.Line);
            return r.EndPosition;
        }

        private static Task WaitLoadedAsync(FrameworkElement element)
        {
            var tcs = new TaskCompletionSource<bool>();
            RoutedEventHandler handler = null;
            handler = (s, e) => { element.Loaded -= handler; tcs.TrySetResult(true); };
            element.Loaded += handler;
            _ = Task.Delay(500).ContinueWith(_ => tcs.TrySetResult(false));
            return tcs.Task;
        }

        public static RichEditBox CreatePageEditor(PageGeometry page, Style style)
        {
            var box = new RichEditBox
            {
                Width = page.PageWidth,
                Height = page.PageHeight,
                Padding = page.Margins,
                BorderThickness = new Thickness(0),
                TextWrapping = page.Wrap,
                IsSpellCheckEnabled = false,
                IsTextPredictionEnabled = false,
                IsTabStop = false,
                RequestedTheme = ElementTheme.Light,
                Background = new SolidColorBrush(Colors.White),
                Foreground = new SolidColorBrush(Colors.Black),
                MinWidth = 0,
                MinHeight = 0
            };
            if (style != null) box.Style = style;
            box.Padding = page.Margins;
            box.BorderThickness = new Thickness(0);
            ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Hidden);
            ScrollViewer.SetHorizontalScrollBarVisibility(box, ScrollBarVisibility.Hidden);
            return box;
        }

        public static FrameworkElement CreatePage(string rtf, PageGeometry page, Style style, int pageNumber, bool showPageNumber)
        {
            var paper = new Grid
            {
                Width = page.PageWidth,
                Height = page.PageHeight,
                Background = new SolidColorBrush(Colors.White),
                RequestedTheme = ElementTheme.Light,
                IsHitTestVisible = false
            };

            RichEditBox box = CreatePageEditor(page, style);
            box.IsReadOnly = false;
            try { box.Document.SetText(TextSetOptions.FormatRtf, rtf ?? string.Empty); } catch { }
            box.IsReadOnly = true;
            box.Loaded += (s, e) =>
            {
                try
                {
                    box.IsReadOnly = false;
                    box.Document.SetText(TextSetOptions.FormatRtf, rtf ?? string.Empty);
                    box.IsReadOnly = true;
                }
                catch { }
            };
            paper.Children.Add(box);

            if (showPageNumber)
            {
                paper.Children.Add(new TextBlock
                {
                    Text = pageNumber.ToString(),
                    Foreground = new SolidColorBrush(Colors.Black),
                    FontSize = 14.667,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 0, Math.Max(8, page.Margins.Bottom / 2 - 10))
                });
            }

            return paper;
        }
    }
}
