
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Graphics.Printing;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Printing;
using Microsoft.Toolkit.Uwp;
using Microsoft.Toolkit.Uwp.Helpers;

namespace WordPad.Helpers
{
    public class DocumentPrintHelper : IDisposable
    {
        public event Action OnPrintSucceeded;

        public event Action<PrintTask> PrintTaskCreated;

        public event Action OnPrintFailed;

        public event Action OnPrintCanceled;

        public event Action<List<FrameworkElement>> OnPreviewPagesCreated;

        public double ApplicationContentMarginLeft { get; set; } = 0.03;

        public double ApplicationContentMarginTop { get; set; } = 0.03;

        private PrintDocument _printDocument;

        private IPrintDocumentSource _printDocumentSource;

        private List<FrameworkElement> _printPreviewPages;

        private Canvas _printCanvas;

        private Panel _canvasContainer;
        private string _printTaskName;
        private Dictionary<FrameworkElement, DocumentPrintHelperStateBag> _stateBags = new Dictionary<FrameworkElement, DocumentPrintHelperStateBag>();
        private bool _directPrint = false;

        private List<FrameworkElement> _elementsToPrint;

        private PrintHelperOptions _printHelperOptions;

        private PrintHelperOptions _defaultPrintHelperOptions;

        public DispatcherQueue DispatcherQueue { get; set; }

        public bool PrintFullPage { get; set; }

        public DocumentPrintHelper(Panel canvasContainer, PrintHelperOptions defaultPrintHelperOptions = null, DispatcherQueue dispatcherQueue = null)
        {
            DispatcherQueue = dispatcherQueue ?? DispatcherQueue.GetForCurrentThread();

            if (canvasContainer == null)
            {
                throw new ArgumentNullException();
            }

            _printPreviewPages = new List<FrameworkElement>();
            _printCanvas = new Canvas();
            _printCanvas.Opacity = 0;

            _canvasContainer = canvasContainer;
            _canvasContainer.RequestedTheme = ElementTheme.Light;

            _elementsToPrint = new List<FrameworkElement>();

            _defaultPrintHelperOptions = defaultPrintHelperOptions ?? new PrintHelperOptions();

            RegisterForPrinting();
        }

        public void AddFrameworkElementToPrint(FrameworkElement element)
        {
            if (element.Parent != null)
            {
                throw new ArgumentException("Printable elements cannot have a parent.");
            }

            _elementsToPrint.Add(element);
        }

        public void RemoveFrameworkElementToPrint(FrameworkElement element)
        {
            _elementsToPrint.Remove(element);
        }

        public void ClearListOfPrintableFrameworkElements()
        {
            _elementsToPrint.Clear();
        }

        public async Task ShowPrintUIAsync(string printTaskName, bool directPrint = false)
        {
            this._directPrint = directPrint;

            PrintManager printMan = PrintManager.GetForCurrentView();
            printMan.PrintTaskRequested += PrintTaskRequested;

            _printTaskName = printTaskName;
            await PrintManager.ShowPrintUIAsync();
        }

        public Task ShowPrintUIAsync(string printTaskName, PrintHelperOptions printHelperOptions, bool directPrint = false)
        {
            _printHelperOptions = printHelperOptions;

            return ShowPrintUIAsync(printTaskName, directPrint);
        }

        public void Dispose()
        {
            if (_printDocument == null)
            {
                return;
            }

            _printCanvas = null;
            DispatcherQueue.EnqueueAsync(() =>
            {
                _printDocument.Paginate -= CreatePrintPreviewPages;
                _printDocument.GetPreviewPage -= GetPrintPreviewPage;
                _printDocument.AddPages -= AddPrintPages;
            });
        }

        private void RegisterForPrinting()
        {
            _printDocument = new PrintDocument();
            _printDocumentSource = _printDocument.DocumentSource;
            _printDocument.Paginate += CreatePrintPreviewPages;
            _printDocument.GetPreviewPage += GetPrintPreviewPage;
            _printDocument.AddPages += AddPrintPages;
        }

        private async Task DetachCanvas()
        {
            if (!_directPrint)
            {
                await DispatcherQueue.EnqueueAsync(() =>
                {
                    _canvasContainer.Children.Remove(_printCanvas);
                    _printCanvas.Children.Clear();
                });
            }

            _stateBags.Clear();

            await ClearPageCache();

            PrintManager printMan = PrintManager.GetForCurrentView();
            printMan.PrintTaskRequested -= PrintTaskRequested;
        }

        private void PrintTaskRequested(PrintManager sender, PrintTaskRequestedEventArgs e)
        {
            PrintTask printTask = null;
            printTask = e.Request.CreatePrintTask(_printTaskName, sourceRequested =>
            {
                ApplyPrintSettings(printTask);
                PrintTaskCreated?.Invoke(printTask);

                printTask.Completed += async (s, args) =>
                {
                    await DispatcherQueue.EnqueueAsync(
                        async () =>
                        {
                            foreach (var element in _stateBags.Keys)
                            {
                                _stateBags[element].Restore(element);
                            }

                            _stateBags.Clear();
                            _canvasContainer.RequestedTheme = ElementTheme.Default;
                            await DetachCanvas();

                            switch (args.Completion)
                            {
                                case PrintTaskCompletion.Failed:
                                    OnPrintFailed?.Invoke();
                                    break;

                                case PrintTaskCompletion.Canceled:
                                    OnPrintCanceled?.Invoke();
                                    break;

                                case PrintTaskCompletion.Submitted:
                                    OnPrintSucceeded?.Invoke();
                                    break;
                            }
                        }, DispatcherQueuePriority.Normal);
                };

                sourceRequested.SetSource(_printDocumentSource);
            });
        }

        private void ApplyPrintSettings(PrintTask printTask)
        {
            _printHelperOptions = _printHelperOptions ?? _defaultPrintHelperOptions;

            IEnumerable<string> displayedOptionsToAdd = _printHelperOptions.DisplayedOptions;

            if (!_printHelperOptions.ExtendDisplayedOptions)
            {
                printTask.Options.DisplayedOptions.Clear();
            }

            foreach (var displayedOption in displayedOptionsToAdd)
            {
                if (!printTask.Options.DisplayedOptions.Contains(displayedOption))
                {
                    printTask.Options.DisplayedOptions.Add(displayedOption);
                }
            }

            if (printTask.Options.Binding != PrintBinding.NotAvailable)
            {
                printTask.Options.Binding = _printHelperOptions.Binding == PrintBinding.Default ? printTask.Options.Binding : _printHelperOptions.Binding;
            }

            if (printTask.Options.Bordering != PrintBordering.NotAvailable)
            {
                printTask.Options.Bordering = _printHelperOptions.Bordering == PrintBordering.Default ? printTask.Options.Bordering : _printHelperOptions.Bordering;
            }

            if (printTask.Options.MediaType != PrintMediaType.NotAvailable)
            {
                printTask.Options.MediaType = _printHelperOptions.MediaType == PrintMediaType.Default ? printTask.Options.MediaType : _printHelperOptions.MediaType;
            }

            if (printTask.Options.MediaSize != PrintMediaSize.NotAvailable)
            {
                printTask.Options.MediaSize = _printHelperOptions.MediaSize == PrintMediaSize.Default ? printTask.Options.MediaSize : _printHelperOptions.MediaSize;
            }

            if (printTask.Options.HolePunch != PrintHolePunch.NotAvailable)
            {
                printTask.Options.HolePunch = _printHelperOptions.HolePunch == PrintHolePunch.Default ? printTask.Options.HolePunch : _printHelperOptions.HolePunch;
            }

            if (printTask.Options.Duplex != PrintDuplex.NotAvailable)
            {
                printTask.Options.Duplex = _printHelperOptions.Duplex == PrintDuplex.Default ? printTask.Options.Duplex : _printHelperOptions.Duplex;
            }

            if (printTask.Options.ColorMode != PrintColorMode.NotAvailable)
            {
                printTask.Options.ColorMode = _printHelperOptions.ColorMode == PrintColorMode.Default ? printTask.Options.ColorMode : _printHelperOptions.ColorMode;
            }

            if (printTask.Options.Collation != PrintCollation.NotAvailable)
            {
                printTask.Options.Collation = _printHelperOptions.Collation == PrintCollation.Default ? printTask.Options.Collation : _printHelperOptions.Collation;
            }

            if (printTask.Options.PrintQuality != PrintQuality.NotAvailable)
            {
                printTask.Options.PrintQuality = _printHelperOptions.PrintQuality == PrintQuality.Default ? printTask.Options.PrintQuality : _printHelperOptions.PrintQuality;
            }

            if (printTask.Options.Staple != PrintStaple.NotAvailable)
            {
                printTask.Options.Staple = _printHelperOptions.Staple == PrintStaple.Default ? printTask.Options.Staple : _printHelperOptions.Staple;
            }

            if (printTask.Options.Orientation != PrintOrientation.NotAvailable)
            {
                printTask.Options.Orientation = _printHelperOptions.Orientation == PrintOrientation.Default ? printTask.Options.Orientation : _printHelperOptions.Orientation;
            }

            _printHelperOptions = null;
        }

        private async void CreatePrintPreviewPages(object sender, PaginateEventArgs e)
        {
            PrintTaskOptions printingOptions = e.PrintTaskOptions;

            PrintPageDescription pageDescription = printingOptions.GetPageDescription(0);

            if (_directPrint)
            {
                _canvasContainer.RequestedTheme = ElementTheme.Light;
                foreach (FrameworkElement element in this._canvasContainer.Children)
                {
                    _printPreviewPages.Add(element);
                }
            }
            else
            {
                if (!_canvasContainer.Children.Contains(_printCanvas))
                {
                    _canvasContainer.Children.Add(_printCanvas);
                }

                _canvasContainer.RequestedTheme = ElementTheme.Light;

                await ClearPageCache();

                _printCanvas.Children.Clear();

                var printPageTasks = new List<Task>();
                foreach (var element in _elementsToPrint)
                {
                    printPageTasks.Add(AddOnePrintPreviewPage(element, pageDescription));
                }

                await Task.WhenAll(printPageTasks);
            }

            OnPreviewPagesCreated?.Invoke(_printPreviewPages);

            PrintDocument printDoc = (PrintDocument)sender;

            _printCanvas.UpdateLayout();
            printDoc.SetPreviewPageCount(_printPreviewPages.Count, PreviewPageCountType.Intermediate);
            printDoc.SetPreviewPage(_printPreviewPages.Count, _printPreviewPages[_printPreviewPages.Count - 1]);
        }

        private void GetPrintPreviewPage(object sender, GetPreviewPageEventArgs e)
        {
            PrintDocument printDoc = (PrintDocument)sender;
            if (_printPreviewPages.Count() > 0)
            {
                printDoc.SetPreviewPage(e.PageNumber, _printPreviewPages[e.PageNumber - 1]);
            }
        }

        private void AddPrintPages(object sender, AddPagesEventArgs e)
        {
            for (int i = 0; i < _printPreviewPages.Count; i++)
            {
                _printDocument.AddPage(_printPreviewPages[i]);
            }

            PrintDocument printDoc = (PrintDocument)sender;

            printDoc.AddPagesComplete();
        }

        private Task AddOnePrintPreviewPage(FrameworkElement element, PrintPageDescription printPageDescription)
        {
            var page = new Page();

            if (!_stateBags.ContainsKey(element))
            {
                var stateBag = new DocumentPrintHelperStateBag(DispatcherQueue);
                stateBag.Capture(element);
                _stateBags.Add(element, stateBag);
            }

            page.Width = printPageDescription.PageSize.Width;
            page.Height = printPageDescription.PageSize.Height;

            double marginWidth = Math.Max(printPageDescription.PageSize.Width - printPageDescription.ImageableRect.Width, printPageDescription.PageSize.Width * ApplicationContentMarginLeft * 2);
            double marginHeight = Math.Max(printPageDescription.PageSize.Height - printPageDescription.ImageableRect.Height, printPageDescription.PageSize.Height * ApplicationContentMarginTop * 2);

            element.VerticalAlignment = VerticalAlignment.Top;
            element.HorizontalAlignment = HorizontalAlignment.Left;

            if (PrintFullPage)
            {
                double scale = Math.Min(page.Width / Math.Max(1, element.Width), page.Height / Math.Max(1, element.Height));
                if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0) scale = 1;
                element.Width = element.Width * scale;
                element.Height = element.Height * scale;
                element.Margin = new Thickness((page.Width - element.Width) / 2, (page.Height - element.Height) / 2, 0, 0);
                page.Content = element;

                return DispatcherQueue.EnqueueAsync(
                    () =>
                    {
                        _printCanvas.Children.Add(page);
                        _printCanvas.UpdateLayout();
                        _printCanvas.InvalidateMeasure();
                        _printPreviewPages.Add(page);
                    }, DispatcherQueuePriority.High);
            }

            if (element.Width > element.Height)
            {
                var newWidth = page.Width - marginWidth;

                element.Height = element.Height * (newWidth / element.Width);
                element.Width = newWidth;
            }
            else
            {
                var newHeight = page.Height - marginHeight;

                element.Width = element.Width * (newHeight / element.Height);
                element.Height = newHeight;
            }

            element.Margin = new Thickness(marginWidth / 2, marginHeight / 2, marginWidth / 2, marginHeight / 2);
            page.Content = element;

            return DispatcherQueue.EnqueueAsync(
                () =>
                {
                    _printCanvas.Children.Add(page);
                    _printCanvas.UpdateLayout();
                    _printCanvas.InvalidateMeasure();

                    _printPreviewPages.Add(page);
                }, DispatcherQueuePriority.High);
        }

        private Task ClearPageCache()
        {
            return DispatcherQueue.EnqueueAsync(() =>
            {
                if (!_directPrint)
                {
                    foreach (Page page in _printPreviewPages)
                    {
                        page.Content = null;
                    }
                }

                _printPreviewPages.Clear();
            });
        }
    }
}