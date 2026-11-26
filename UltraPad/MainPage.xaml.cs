using DocumentFormat.OpenXml.Bibliography;
using DocumentFormat.OpenXml.Drawing;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Toolkit.Uwp.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using Windows.ApplicationModel.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.Email;
using Windows.ApplicationModel.Resources;
using Windows.ApplicationModel.Resources.Core;
using Windows.Foundation;
using Windows.Graphics.Display;
using Windows.Graphics.Printing;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;
using Windows.Storage.Provider;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Core.Preview;
using Windows.UI.Text;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using UltraPad.Helpers;
using UltraPad.UltraPadUI;
using UltraPad.UltraPadUI.Settings;
using Application = Windows.UI.Xaml.Application;
using CheckBox = Windows.UI.Xaml.Controls.CheckBox;
using Color = Windows.UI.Color;
using EditingToolbar = UltraPad.UltraPadUI.Ribbon.EditingToolbar;
using Point = Windows.Foundation.Point;
using Run = DocumentFormat.OpenXml.Wordprocessing.Run;
using Text = DocumentFormat.OpenXml.Wordprocessing.Text;
using UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding;

// UltraPad made by Lixkote
// Main page c# source code

namespace UltraPad
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private bool _isDirty = false;
        private string _cleanRtf;
        private string _lastSavedText = "";
        private string _currentFilePath = null;

        private bool _initialized = false;
        public StorageFile RichEditFile;
        private string appTitleStr => "UltraPad";
        private string _untitledTitle;

        ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;

        private bool updateFontFormat = true;
        public string ZoomString => ZoomSlider.Value.ToString() + "%";

        private string fileNameWithPath = "";

        private double baseRulerWidth;

        string originalDocText = "";

        UnitManager unitConverter = new UnitManager();
        SettingsManagerMain settingsManager = new SettingsManagerMain();
        private DocumentFileFormat _currentFormat = DocumentFileFormat.Rtf;
        private double _pageHeightDip = 1056;
        private bool _caretScrollQueued;
        private bool _caretScrollWithoutFocus;

        private int _suppressDirty;
        private DateTime _ignoreTextChangedUntil = DateTime.MinValue;

        private bool _isEditorDark;
        private bool _spellCheckEnabled;
        private bool _autocorrectEnabled;

        private bool _toolbarAutoHide;
        private bool _toolbarOverlayOpen;

        private ScrollViewer _editorScrollViewer;
        private bool _wheelSelecting;
        private int _wheelAnchor = -1;
        private Point _lastPointerWindowPos;
        private ulong _lastWheelTimestamp;

        private readonly List<MenuFlyoutItemBase> _spellMenuItems = new List<MenuFlyoutItemBase>();
        private Point _lastRightClickPoint;
        private DateTime _lastRightClickTime = DateTime.MinValue;

        private bool _inPaste;
        private PictureAdorner _pictureAdorner;

        private static readonly Color Black = Color.FromArgb(255, 0, 0, 0);
        private static readonly Color White = Color.FromArgb(255, 255, 255, 255);

        private const string KeyShowRuler = "showRuler";
        private const string KeyShowStatusBar = "showStatusBar";
        private const string KeyToolbarAutoHide = "toolbarAutoHide";
        private const int MaxRecentFiles = 9;

        public List<string> Fonts
        {
            get
            {
                return CanvasTextFormat.GetSystemFontFamilies().OrderBy(f => f).ToList();
            }
        }

        private static readonly double[] ZoomLevels = { 3, 2, 1.5, 1.25, 1, 0.75, 0.5, 0.25 };
        private bool _updatingZoomBox;
        private bool _fitToWindowActive;
        private bool _applyingFit;

        public List<double> FontSizes { get; } = new List<double>()
        {
                8,
                9,
                10,
                11,
                12,
                14,
                16,
                18,
                20,
                24,
                28,
                36,
                48,
                72
        };

        private void SetLineSpacing(double lineSpacingValue, LineSpacingRule lineSpacingRule = LineSpacingRule.Exactly)
        {
            // Get the document from the RichEditBox
            var document = Editor.Document;

            // Select the entire document (or specify a different range if needed)
            document.Selection.Expand(TextRangeUnit.Paragraph);

            // Modify the line spacing using ITextParagraphFormat
            var paragraphFormat = document.Selection.ParagraphFormat;

            // Use SetLineSpacing to set both the rule and the spacing value
            paragraphFormat.SetLineSpacing(lineSpacingRule, (float)lineSpacingValue);
        }

        public MainPage()
        {
            /////
            /// Startup Procedure
            /////

            // Enable navigation cache
            this.NavigationCacheMode = Windows.UI.Xaml.Navigation.NavigationCacheMode.Enabled;

            // Run the startup functions
            InitializeComponent();
            _untitledTitle = AppTitle.Text;
            AppTitle.RegisterPropertyChangedCallback(TextBlock.TextProperty, (s, dp) => UpdateWindowTitle());
            UpdateWindowTitle();
            settingsManager.InitializeDefaults();
            LoadThemeFromSettings();
            ConnectRibbonToolbars();
            LoadViewSettings();
            ApplyEditorSettings();
            ApplyPageLayout();
            UpdateViewMenuChecks();
            _ = PopulateRecentsAsync();
            InitializeZoomBox();

            // ParagraphMenuIcon.FontFamily = (Windows.UI.Xaml.Media.FontFamily)Application.Current.Resources["CustomIconFont"];

            SystemNavigationManagerPreview.GetForCurrentView().CloseRequested += OnCloseRequest;
            ribbongrid.DataContext = this;

            SettingsPageManager.ThemeChanged += ChangeEditorContainerTheme;
            SettingsPageManager.SpellChanged += EnableEditorSpellCheck;
            SettingsPageManager.PredictChanged += EnableEditorAutocorrect;
            PageSettings.UnitChanged += unit => UpdateViewMenuChecks();
            PageSettings.WrapChanged += mode => { ApplyPageLayout(); UpdateViewMenuChecks(); };

            DataTransferManager dataTransferManager = DataTransferManager.GetForCurrentView();
            dataTransferManager.DataRequested += DataTransferManager_DataRequested;

            Editor.TextChanging += Editor_TextChanging;
            Editor.Paste += Editor_Paste;
            Editor.AddHandler(UIElement.CharacterReceivedEvent, new TypedEventHandler<UIElement, CharacterReceivedRoutedEventArgs>(Editor_CharacterReceived), true);
            Editor.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Editor_PointerPressedAll), true);
            Editor.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(Editor_PointerMovedAll), true);
            Editor.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(Editor_PointerReleasedAll), true);
            Editor.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(Editor_PointerReleasedAll), true);
            Editor.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(Editor_PointerWheelChangedAll), true);
            EditorContextMenu.Opening += EditorContextMenu_Opening;

            _pictureAdorner = new PictureAdorner(Editor, PictureAdornerCanvas);
            Editor.BringIntoViewRequested += (snd, args) => { if (_pictureAdorner?.IsDragging ?? false) args.Handled = true; };

            Editor.AddHandler(UIElement.DoubleTappedEvent, new DoubleTappedEventHandler(Editor_DoubleTappedAll), true);
            Window.Current.Activated += Window_Activated;

            this.Loaded += (s, e) => ApplyToolbarMode();
            RichTextScrollView.Loaded += (s, e) => ApplyAppThemeToScrollBars();
            EditorContainer.SizeChanged += (s, e) => UpdateEditorMinHeight();
            RichTextScrollView.RegisterPropertyChangedCallback(Windows.UI.Xaml.Controls.ScrollViewer.ComputedHorizontalScrollBarVisibilityProperty, (s, dp) => UpdateEditorMinHeight());
            this.ActualThemeChanged += (s, e) => ApplyAppThemeToScrollBars();
            EditorContextMenu.Closed += EditorContextMenu_Closed;
            EditorContextMenu.Opened += (s, e) =>
            {
                ApplyThemeToOpenPopups();
                RefreshParagraphMenuIcon();
            };

            EditorContentHost.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(ContentArea_PointerPressed), true);
            RulerBorder.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(ContentArea_PointerPressed), true);

            TextRuler.GuideChanged += TextRuler_GuideChanged;
            TextRuler.ParagraphDialogRequested += (s, e) => pararibbontoolbar.ShowParagraphDialog();
            TextRuler.TabsDialogRequested += async (s, e) => await pararibbontoolbar.ShowTabsDialogAsync();

            var toolbarAccelerator = new KeyboardAccelerator { Key = VirtualKey.F1, Modifiers = VirtualKeyModifiers.Control };
            toolbarAccelerator.Invoked += (s, a) => { SetToolbarAutoHide(!_toolbarAutoHide); a.Handled = true; };
            this.KeyboardAccelerators.Add(toolbarAccelerator);
            // like in the classic editor, the shortcuts only work while Find and Replace are available (the document isn't empty)
            AddAccelerator(VirtualKey.F, VirtualKeyModifiers.Control, () => editribbontoolbar.ShowFind(EditorContentHost), () => editribbontoolbar.CanSearch);
            AddAccelerator(VirtualKey.H, VirtualKeyModifiers.Control, () => editribbontoolbar.ShowReplace(EditorContentHost), () => editribbontoolbar.CanSearch);
            AddAccelerator(VirtualKey.F3, VirtualKeyModifiers.None, () => FindAgain(false), () => editribbontoolbar.CanSearch);
            AddAccelerator(VirtualKey.F3, VirtualKeyModifiers.Shift, () => FindAgain(true), () => editribbontoolbar.CanSearch);

            // otherwise the page shows its shortcuts as a tooltip that can get stuck on screen
            this.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;

            BeginProgrammaticChange();
            SetLineSpacing(1, LineSpacingRule.Multiple); // Single spacing
            EndProgrammaticChange();
            MarkClean();
        }

        private void AddAccelerator(VirtualKey key, VirtualKeyModifiers modifiers, Action action, Func<bool> canRun)
        {
            var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
            accelerator.Invoked += (s, a) =>
            {
                if (_printPreviewOpen || !canRun()) return;
                a.Handled = true;
                action();
            };
            this.KeyboardAccelerators.Add(accelerator);
        }

        private async void FindAgain(bool backward)
        {
            if (!EditingToolbar.HasSearchText)
            {
                editribbontoolbar.ShowFind(EditorContentHost);
                return;
            }

            EditingToolbar.SearchResult result = EditingToolbar.FindNext(Editor, backward);
            if (result != EditingToolbar.SearchResult.NotFound)
            {
                QueueCaretIntoView(true);
                return;
            }

            try
            {
                await new ContentDialog
                {
                    Title = appTitleStr,
                    Content = EditingToolbar.Describe(result),
                    CloseButtonText = Loc.Get("dlg_ok", "OK"),
                    DefaultButton = ContentDialogButton.Close
                }.ShowThemedAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("FindAgain: " + ex.Message);
            }
        }

        // taskbar previews and Alt+Tab show "<title> - UltraPad"
        private void UpdateWindowTitle()
        {
            try
            {
                string title = AppTitle.Text ?? "";
                string suffix = " - " + appTitleStr;
                if (title.EndsWith(suffix, StringComparison.Ordinal)) title = title.Substring(0, title.Length - suffix.Length);
                ApplicationView.GetForCurrentView().Title = title == appTitleStr ? "" : title;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("UpdateWindowTitle: " + ex.Message);
            }
        }

        #region Dirty tracking

        private void MarkClean()
        {
            _isDirty = false;
            _cleanRtf = SnapshotRtf();

            // startup and loading still apply default formatting for a moment afterwards (toolbar
            // settings, line spacing, layout), which looks like an edit; if the user hasn't touched
            // anything by the time that's over, the result still counts as unchanged
            _userTouched = false;
            if (_settleTimer == null)
            {
                _settleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
                _settleTimer.Tick += (s, e) =>
                {
                    _settleTimer.Stop();
                    if (_userTouched) return;
                    _isDirty = false;
                    _cleanRtf = SnapshotRtf();
                };
                this.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((s, e) => _userTouched = true), true);
                this.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((s, e) => _userTouched = true), true);
            }
            _settleTimer.Stop();
            _settleTimer.Start();
        }

        private DispatcherTimer _settleTimer;
        private bool _userTouched;

        private bool IsDocumentEmpty()
        {
            try
            {
                Editor.Document.GetText(TextGetOptions.None, out string text);
                return string.IsNullOrEmpty(text) || text == "\r";
            }
            catch
            {
                return false;
            }
        }

        // big documents (tens of MB, many pictures) take seconds to serialize to RTF, which froze
        // the window (and the mouse wheel) every time a snapshot was taken; for those only the
        // dirty flag is used, like the classic editor does
        private const long LargeDocumentBytes = 8L * 1024 * 1024;
        private bool _largeDocument;

        private const ulong LoadingProgressBytes = 2UL * 1024 * 1024;
        private bool _loadingShown;

        // loading progress in the status bar for big files, like the classic editor shows while opening
        private void ShowLoading(int percent)
        {
            percent = Math.Max(0, Math.Min(100, percent));
            if (!_loadingShown)
            {
                _loadingShown = true;
                LoadingPanel.Visibility = Visibility.Visible;
                ApplyStatusBarVisibility();
            }
            LoadingProgress.IsIndeterminate = false;
            LoadingProgress.Value = percent;
            LoadingText.Text = Loc.Format("status_loading", "Loading document... {0}%", percent);
        }

        // putting the text into the editor is one call that can't report progress, so the bar
        // switches to the moving style and the UI gets a moment to draw that before it starts
        private async Task ShowLoadingFinalStepAsync()
        {
            ShowLoading(75);
            LoadingProgress.IsIndeterminate = true;
            LoadingText.Text = Loc.Get("status_loading_layout", "Preparing document...");
            await Task.Delay(30);
        }

        private void HideLoading()
        {
            if (!_loadingShown) return;
            _loadingShown = false;
            LoadingProgress.IsIndeterminate = false;
            LoadingPanel.Visibility = Visibility.Collapsed;
            ApplyStatusBarVisibility();
        }

        private string SnapshotRtf()
        {
            if (_largeDocument) return null;
            var watch = Stopwatch.StartNew();
            string rtf = CurrentRtf();
            if (watch.ElapsedMilliseconds > 250 || (rtf != null && rtf.Length > LargeDocumentBytes))
            {
                _largeDocument = true;
                return null;
            }
            return rtf;
        }

        private string CurrentRtf()
        {
            try
            {
                Editor.Document.GetText(TextGetOptions.FormatRtf, out string rtf);
                return rtf;
            }
            catch
            {
                return null;
            }
        }

        // late TextChanged events (images, layout) can set the flag without a real edit,
        // so the document is compared with what was loaded or saved before asking
        private bool HasUnsavedChanges()
        {
            if (!_isDirty) return false;

            // an untitled document with nothing in it has nothing worth saving, like in the classic editor
            if (_currentFile == null && IsDocumentEmpty())
            {
                _isDirty = false;
                return false;
            }

            if (_cleanRtf != null && !_largeDocument && CurrentRtf() == _cleanRtf)
            {
                _isDirty = false;
                return false;
            }
            return true;
        }

        private void BeginProgrammaticChange()
        {
            _suppressDirty++;
        }

        private void EndProgrammaticChange()
        {
            if (_suppressDirty > 0) _suppressDirty--;
            _ignoreTextChangedUntil = DateTime.UtcNow.AddMilliseconds(400);
        }

        private void Editor_TextChanging(RichEditBox sender, RichEditBoxTextChangingEventArgs args)
        {
            // late events from a programmatic change (recoloring for the editor color, loading) are
            // not edits, same as in Editor_TextChanged
            if (_suppressDirty == 0 && DateTime.UtcNow > _ignoreTextChangedUntil) _isDirty = true;
        }

        private void Editor_TextChanged(object sender, RoutedEventArgs e)
        {
            if (_suppressDirty == 0 && DateTime.UtcNow > _ignoreTextChangedUntil) _isDirty = true;
        }

        #endregion

        #region Editor settings

        private bool GetBoolSetting(string key, bool fallback)
        {
            object value = localSettings.Values[key];
            return value is bool b ? b : fallback;
        }

        private void ApplyEditorSettings()
        {
            SetEditorDark(GetBoolSetting("IsDarkThemeEditor", false), recolor: true);
            EnableEditorSpellCheck(GetBoolSetting("isSpellCheckEnabled", false));
            EnableEditorAutocorrect(GetBoolSetting("isTextPredictEnabled", false));
        }

        public void ChangeEditorContainerTheme(bool isDarkThemeEditor)
        {
            SetEditorDark(isDarkThemeEditor, recolor: true);
        }

        private void SetEditorDark(bool dark, bool recolor)
        {
            bool changed = _isEditorDark != dark;
            _isEditorDark = dark;
            EditorContainer.RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
            PrintSubItem.IsEnabled = !dark;

            if (recolor && changed)
            {
                if (dark) RecolorRange(null, Black, White);
                else RecolorRange(null, White, Black);
            }
        }

        public void EnableEditorSpellCheck(bool isSpellCheckEnabled)
        {
            _spellCheckEnabled = isSpellCheckEnabled;
            Editor.IsSpellCheckEnabled = isSpellCheckEnabled;
        }

        public void EnableEditorAutocorrect(bool isTextPredictEnabled)
        {
            _autocorrectEnabled = isTextPredictEnabled;
            Editor.IsTextPredictionEnabled = isTextPredictEnabled;
        }

        private void RecolorRange(ITextRange target, Color from, Color to)
        {
            bool wasDirty = _isDirty;
            BeginProgrammaticChange();
            try
            {
                ITextDocument doc = Editor.Document;
                int start = target?.StartPosition ?? 0;
                int end = target?.EndPosition ?? doc.GetRange(0, TextConstants.MaxUnitCount).EndPosition;
                if (end <= start) return;

                ITextRange run = doc.GetRange(start, start);
                int guard = 0;
                while (run.EndPosition < end && guard++ < 200000)
                {
                    int before = run.EndPosition;
                    run.MoveEnd(TextRangeUnit.CharacterFormat, 1);
                    if (run.EndPosition > end) run.EndPosition = end;
                    if (run.EndPosition <= before) break;

                    Color c = run.CharacterFormat.ForegroundColor;
                    // text without its own color ("automatic") reports a transparent color; it is
                    // drawn black on the page, so it counts as black here
                    bool automatic = c.A == 0;
                    bool matches = automatic
                        ? (from.R == 0 && from.G == 0 && from.B == 0)
                        : (c.R == from.R && c.G == from.G && c.B == from.B);
                    if (matches)
                    {
                        run.CharacterFormat.ForegroundColor = to;
                    }
                    run.Collapse(false);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("RecolorRange: " + ex.Message);
            }
            finally
            {
                EndProgrammaticChange();
                _isDirty = wasDirty;

                // the editor color is a display setting, so an unchanged document stays unchanged
                if (!wasDirty) _cleanRtf = SnapshotRtf();
            }
        }

        private static string RecolorRtfColorTable(string rtf, bool blackToWhite)
        {
            if (string.IsNullOrEmpty(rtf)) return rtf;
            int start = rtf.IndexOf(@"{\colortbl", StringComparison.Ordinal);
            if (start < 0) return rtf;

            int depth = 0, end = start;
            for (; end < rtf.Length; end++)
            {
                char ch = rtf[end];
                if (ch == '\\') { end++; continue; }
                if (ch == '{') depth++;
                else if (ch == '}')
                {
                    depth--;
                    if (depth == 0) break;
                }
            }
            if (end >= rtf.Length) return rtf;

            string table = rtf.Substring(start, end - start + 1);
            string newTable = blackToWhite
                ? Regex.Replace(table, @"\\red0\\green0\\blue0(?![0-9])", @"\red255\green255\blue255")
                : Regex.Replace(table, @"\\red255\\green255\\blue255(?![0-9])", @"\red0\green0\blue0");

            return rtf.Substring(0, start) + newTable + rtf.Substring(end + 1);
        }

        private string GetRtfForExport()
        {
            Editor.Document.GetText(TextGetOptions.FormatRtf, out string rtf);
            return _isEditorDark ? RecolorRtfColorTable(rtf, blackToWhite: false) : rtf;
        }

        #endregion

        #region View settings

        private void LoadViewSettings()
        {
            RulerToggle.IsChecked = GetBoolSetting(KeyShowRuler, true);
            StatusbarToggle.IsChecked = GetBoolSetting(KeyShowStatusBar, true);
            _toolbarAutoHide = GetBoolSetting(KeyToolbarAutoHide, false);
            ApplyRulerVisibility();
            ApplyStatusBarVisibility();
            ApplyToolbarMode();
        }

        private void RulerToggle_Click(object sender, RoutedEventArgs e)
        {
            localSettings.Values[KeyShowRuler] = RulerToggle.IsChecked;
            ApplyRulerVisibility();
        }

        private void StatusbarToggle_Click(object sender, RoutedEventArgs e)
        {
            localSettings.Values[KeyShowStatusBar] = StatusbarToggle.IsChecked;
            ApplyStatusBarVisibility();
        }

        private void ApplyRulerVisibility()
        {
            bool show = RulerToggle.IsChecked && PrintPreviewRibbon.Visibility != Visibility.Visible;
            RulerBorder.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

            double banner = FormatWarningBar.IsOpen
                ? FormatWarningBar.ActualHeight + FormatWarningBar.Margin.Top + FormatWarningBar.Margin.Bottom
                : 0;
            RulerBorder.Margin = new Thickness(0, banner, 0, 0);
            EditorContentHost.Margin = new Thickness(0, banner + (show ? 40 : 0), 0, 0);
            RulerGuideLine.Y1 = banner + (show ? 40 : 0);
        }

        private void ShowFormatWarning(bool show)
        {
            FormatWarningBar.IsOpen = show;
            ApplyRulerVisibility();
        }

        private void FormatWarningBar_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyRulerVisibility();

        private void FormatWarningBar_Closed(Microsoft.UI.Xaml.Controls.InfoBar sender, Microsoft.UI.Xaml.Controls.InfoBarClosedEventArgs args) => ApplyRulerVisibility();

        private void ApplyStatusBarVisibility()
        {
            StatusBarBorder.Visibility = (_loadingShown || StatusbarToggle.IsChecked) && PrintPreviewRibbon.Visibility != Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateViewMenuChecks()
        {
            WrapMode wrap = PageSettings.Wrap;
            WrapNoneMenuItem.IsChecked = wrap == WrapMode.NoWrap;
            WrapWindowMenuItem.IsChecked = wrap == WrapMode.WrapToWindow;
            WrapRulerMenuItem.IsChecked = wrap == WrapMode.WrapToRuler;

            MeasureUnit unit = PageSettings.Unit;
            UnitInchesMenuItem.IsChecked = unit == MeasureUnit.Inches;
            UnitCmMenuItem.IsChecked = unit == MeasureUnit.Centimeters;
            UnitPointsMenuItem.IsChecked = unit == MeasureUnit.Points;
            UnitPicasMenuItem.IsChecked = unit == MeasureUnit.Picas;
        }

        private void WrapMenuItem_Click(object sender, RoutedEventArgs e)
        {
            switch ((sender as FrameworkElement)?.Tag as string)
            {
                case "nowrap": PageSettings.Wrap = WrapMode.NoWrap; break;
                case "wrapwindow": PageSettings.Wrap = WrapMode.WrapToWindow; break;
                default: PageSettings.Wrap = WrapMode.WrapToRuler; break;
            }
        }

        private void UnitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            switch ((sender as FrameworkElement)?.Tag as string)
            {
                case "cm": PageSettings.Unit = MeasureUnit.Centimeters; break;
                case "points": PageSettings.Unit = MeasureUnit.Points; break;
                case "picas": PageSettings.Unit = MeasureUnit.Picas; break;
                default: PageSettings.Unit = MeasureUnit.Inches; break;
            }
        }

        private void Zoom100_Click(object sender, RoutedEventArgs e)
        {
            ZoomSlider.Value = 1;
        }

        private void ApplyPageLayout()
        {
            PageSettings.Size2 size = PageSettings.GetPageSizeTwips();
            Thickness m = PageSettings.GetMarginsTwips();

            double pageWidth = PageSettings.TwipsToDip(size.Width);
            double pageHeight = PageSettings.TwipsToDip(size.Height);
            double left = PageSettings.TwipsToDip(m.Left);
            double right = PageSettings.TwipsToDip(m.Right);
            double top = PageSettings.TwipsToDip(m.Top);
            double bottom = PageSettings.TwipsToDip(m.Bottom);

            WrapMode wrap = PageSettings.Wrap;
            double width = pageWidth;

            if (wrap == WrapMode.WrapToWindow)
            {
                double zoom = RichTextScrollView.ZoomFactor > 0 ? RichTextScrollView.ZoomFactor : 1;
                double viewport = RichTextScrollView.ViewportWidth > 0 ? RichTextScrollView.ViewportWidth : RichTextScrollView.ActualWidth;
                left = right = 16;
                if (viewport > 0) width = Math.Max(240, viewport / zoom - 8);
            }

            Editor.TextWrapping = wrap == WrapMode.NoWrap ? TextWrapping.NoWrap : TextWrapping.Wrap;

            // the editor's 1 px border sits inside its width, so without this the text area came out
            // narrower than page width minus margins (classic editor: A4 with 3.18 cm margins = 14.65 cm)
            Thickness border = Editor.BorderThickness;

            // with display scaling, start the text area on a whole screen pixel so pictures in it
            // aren't drawn between pixels (which blurs them)
            double pixelScale = 1.0;
            try { pixelScale = Windows.Graphics.Display.DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel; } catch { }
            if (pixelScale > 1.001)
            {
                left = Math.Round(left * pixelScale) / pixelScale;
                top = Math.Round(top * pixelScale) / pixelScale;
            }

            Editor.Padding = new Thickness(
                Math.Max(0, left - border.Left), Math.Max(0, top - border.Top),
                Math.Max(0, right - border.Right), Math.Max(0, bottom - border.Bottom));
            _pageHeightDip = pageHeight;
            Editor.Width = width;
            Editor.Height = double.NaN;
            EditorMandatoryPrintingGrid.Width = width;
            EditorMandatoryPrintingGrid.Height = double.NaN;
            UpdateEditorMinHeight();

            TextRuler.SetPage(width, left, right);
        }

        private void RichTextScrollView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_fitToWindowActive) ApplyFitToWindow();
            if (PageSettings.Wrap == WrapMode.WrapToWindow) ApplyPageLayout();
        }

        // Like the classic editor, the page reaches down to the bottom of the window instead of being
        // a whole sheet tall, so an empty or short document can't be scrolled and has no scroll bar;
        // both show up once the text goes past the bottom of the window.
        private double _minHeightZoom = -1;

        private void UpdateEditorMinHeight() => UpdateEditorMinHeight(RichTextScrollView.ZoomFactor);

        // Zooming out needs a taller page (in unzoomed units) to still reach the bottom of the window.
        // Setting it before the zoom animation starts keeps the page from coming up short and then
        // jumping during the animation; zooming in shrinks it only once the animation has finished.
        private void PrepareEditorMinHeightForZoom(double targetZoom)
        {
            if (targetZoom <= 0 || Math.Abs(targetZoom - RichTextScrollView.ZoomFactor) < 0.001) return;
            SuppressScrollBarDuringZoom();
            if (targetZoom < RichTextScrollView.ZoomFactor) UpdateEditorMinHeight(targetZoom);
        }

        // While a zoom animates, the page is briefly taller or shorter than the window (its height is
        // only fitted once the zoom settles), which made the scroll bar flash in and out. A document
        // that fits the window keeps the bar hidden until the zoom is done; a longer one has the bar
        // the whole time anyway.
        private bool _zoomBarSuppressed;

        private void SuppressScrollBarDuringZoom()
        {
            if (_zoomBarSuppressed) return;
            if (RichTextScrollView.ComputedVerticalScrollBarVisibility == Windows.UI.Xaml.Visibility.Visible) return;
            if (Editor.ActualHeight > Editor.MinHeight + 0.5) return;
            _zoomBarSuppressed = true;
            RichTextScrollView.VerticalScrollBarVisibility = Windows.UI.Xaml.Controls.ScrollBarVisibility.Hidden;
        }

        private void EndScrollBarSuppression()
        {
            if (!_zoomBarSuppressed) return;
            // after the layout pass that applies the new page height
            _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () =>
            {
                _zoomBarSuppressed = false;
                RichTextScrollView.VerticalScrollBarVisibility = Windows.UI.Xaml.Controls.ScrollBarVisibility.Auto;
            });
        }

        private void GrowEditorMinHeightForZoom(double zoom)
        {
            if (zoom <= 0) return;
            double available = EditorContainer.ActualHeight;
            if (available <= 0) return;
            if (RichTextScrollView.ComputedHorizontalScrollBarVisibility == Visibility.Visible) available -= 16;
            double min = Math.Max(48, Math.Floor(available / zoom) - 1);
            if (min <= Editor.MinHeight + 0.5) return;
            Editor.MinHeight = min;
            EditorMandatoryPrintingGrid.MinHeight = min;
        }

        private void UpdateEditorMinHeight(double zoom)
        {
            if (zoom <= 0) zoom = 1;
            double available = EditorContainer.ActualHeight;
            if (available <= 0) return;
            if (RichTextScrollView.ComputedHorizontalScrollBarVisibility == Visibility.Visible) available -= 16;

            _minHeightZoom = zoom;
            double min = Math.Max(48, Math.Floor(available / zoom) - 1);
            if (Math.Abs(Editor.MinHeight - min) < 0.5) return;
            Editor.MinHeight = min;
            EditorMandatoryPrintingGrid.MinHeight = min;
        }

        private void RichTextScrollView_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            // only once a zoom has settled; changing the page size during the animation made it jump
            if (e.IsIntermediate)
            {
                // zooming with Ctrl + wheel or a pinch goes straight through the scroll viewer
                if (Math.Abs(RichTextScrollView.ZoomFactor - _minHeightZoom) > 0.001)
                {
                    SuppressScrollBarDuringZoom();
                    // while zooming out the page grows along, so it keeps reaching the bottom of the
                    // window instead of showing as a small block in the middle; it only shrinks once
                    // the zoom has settled (shrinking during the animation made it jump)
                    GrowEditorMinHeightForZoom(RichTextScrollView.ZoomFactor);
                }
            }
            else
            {
                if (Math.Abs(RichTextScrollView.ZoomFactor - _minHeightZoom) > 0.001) UpdateEditorMinHeight();
                EndScrollBarSuppression();
            }
            TextRuler.UpdatePagePosition();
            if (_wheelSelecting) UpdateWheelSelection();
        }

        private void TextRuler_GuideChanged(double? x)
        {
            if (x == null)
            {
                RulerGuideLine.Visibility = Visibility.Collapsed;
                return;
            }
            try
            {
                Point p = TextRuler.TransformToVisual(RulerGuideCanvas).TransformPoint(new Point(x.Value, 0));
                RulerGuideLine.X1 = RulerGuideLine.X2 = Math.Round(p.X) + 0.5;
                RulerGuideLine.Y2 = Math.Max(RulerGuideCanvas.ActualHeight, 100);
                RulerGuideLine.Visibility = Visibility.Visible;
            }
            catch
            {
                RulerGuideLine.Visibility = Visibility.Collapsed;
            }
        }

        #endregion

        #region Toolbar auto hide

        private Storyboard _ribbonStoryboard;

        private void SetToolbarAutoHide(bool autoHide)
        {
            bool wasAutoHide = _toolbarAutoHide;
            _toolbarAutoHide = autoHide;
            localSettings.Values[KeyToolbarAutoHide] = autoHide;
            bool preview = PrintPreviewRibbon.Visibility == Visibility.Visible;

            if (autoHide && !wasAutoHide && !preview && RibbonHost.Visibility == Visibility.Visible)
            {
                _toolbarOverlayOpen = true;
                UpdateToolbarMenuChecks();
                ShowToolbarButton.Visibility = Visibility.Visible;
                ShowToolbarSeparator.Visibility = Visibility.Visible;
                HideToolbarOverlay();
                return;
            }

            bool reveal = !autoHide && wasAutoHide && !_toolbarOverlayOpen && !preview;
            _toolbarOverlayOpen = false;
            ApplyToolbarMode();
            if (reveal) PlayToolbarEntrance();
        }

        private void UpdateToolbarMenuChecks()
        {
            AlwaysShowToolbarItem.IsChecked = !_toolbarAutoHide;
            AutoHideToolbarItem.IsChecked = _toolbarAutoHide;
            AlwaysShowToolbarViewToggle.IsChecked = !_toolbarAutoHide;
            AutoHideToolbarViewToggle.IsChecked = _toolbarAutoHide;
        }

        private void ApplyToolbarMode()
        {
            _ribbonStoryboard?.Stop();

            UpdateToolbarMenuChecks();

            Visibility buttonVisibility = _toolbarAutoHide ? Visibility.Visible : Visibility.Collapsed;
            ShowToolbarButton.Visibility = buttonVisibility;
            ShowToolbarSeparator.Visibility = buttonVisibility;
            UpdateShowToolbarButton();

            ribbongrid.Visibility = Visibility.Visible;
            ribbongrid.Opacity = 1;
            RibbonSlide.Y = 0;

            if (PrintPreviewRibbon.Visibility == Visibility.Visible)
            {
                RibbonHost.Visibility = Visibility.Collapsed;
                return;
            }

            if (!_toolbarAutoHide)
            {
                Grid.SetRow(RibbonHost, 3);
                VisualStateManager.GoToState(this, "ToolbarDocked", false);
                RibbonHost.Visibility = Visibility.Visible;
            }
            else
            {
                Grid.SetRow(RibbonHost, 4);
                VisualStateManager.GoToState(this, "ToolbarFloating", false);
                RibbonHost.Visibility = _toolbarOverlayOpen ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private Windows.UI.Xaml.Controls.ToolTip _showToolbarToolTip;

        private void UpdateShowToolbarButton()
        {
            bool open = _toolbarAutoHide && _toolbarOverlayOpen;
            ShowToolbarIcon.Glyph = open ? "\uED1A" : "\uE890";
            ShowToolbarText.Opacity = open ? 0 : 1;
            HideToolbarText.Opacity = open ? 1 : 0;
            AutomationProperties.SetName(ShowToolbarButton, open ? HideToolbarText.Text : ShowToolbarText.Text);
            if (_showToolbarToolTip == null)
            {
                _showToolbarToolTip = new Windows.UI.Xaml.Controls.ToolTip();
                ToolTipService.SetToolTip(ShowToolbarButton, _showToolbarToolTip);
            }
            // the button moves or hides right after this, so a tooltip left open would stay behind
            _showToolbarToolTip.IsOpen = false;
            _showToolbarToolTip.Content = (open ? HideToolbarText.Text : ShowToolbarText.Text) + " (Ctrl+F1)";
        }

        private static readonly TimeSpan ToolbarAnimationDuration = TimeSpan.FromMilliseconds(167);

        private Storyboard CreateRibbonStoryboard(double fromY, double toY, double fromOpacity, double toOpacity)
        {
            var sb = new Storyboard();
            var ease = new CircleEase { EasingMode = EasingMode.EaseInOut };

            var slide = new DoubleAnimation { From = fromY, To = toY, Duration = ToolbarAnimationDuration, EasingFunction = ease };
            Storyboard.SetTarget(slide, RibbonSlide);
            Storyboard.SetTargetProperty(slide, "Y");

            var fade = new DoubleAnimation { From = fromOpacity, To = toOpacity, Duration = ToolbarAnimationDuration, EasingFunction = ease };
            Storyboard.SetTarget(fade, ribbongrid);
            Storyboard.SetTargetProperty(fade, "Opacity");

            sb.Children.Add(slide);
            sb.Children.Add(fade);
            return sb;
        }

        private double ToolbarSlideDistance()
        {
            if (ribbongrid.ActualHeight <= 0) RibbonHost.UpdateLayout();
            return ribbongrid.ActualHeight > 0 ? ribbongrid.ActualHeight : 110;
        }

        private void PlayToolbarEntrance()
        {
            _ribbonStoryboard?.Stop();
            double distance = ToolbarSlideDistance();
            RibbonSlide.Y = -distance;
            ribbongrid.Opacity = 0;
            _ribbonStoryboard = CreateRibbonStoryboard(-distance, 0, 0, 1);
            _ribbonStoryboard.Completed += (s, e) => { RibbonSlide.Y = 0; ribbongrid.Opacity = 1; };
            _ribbonStoryboard.Begin();
        }

        private void ShowToolbarOverlay()
        {
            _toolbarOverlayOpen = true;
            ApplyToolbarMode();
            PlayToolbarEntrance();
        }

        private void HideToolbarOverlay()
        {
            if (!_toolbarOverlayOpen) return;
            _toolbarOverlayOpen = false;
            UpdateShowToolbarButton();

            double distance = ToolbarSlideDistance();
            _ribbonStoryboard?.Stop();
            var sb = CreateRibbonStoryboard(0, -distance, 1, 0);
            sb.Completed += (s, e) =>
            {
                if (!_toolbarOverlayOpen) ApplyToolbarMode();
            };
            _ribbonStoryboard = sb;
            sb.Begin();
        }

        private void RibbonHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RibbonHost.Clip = new RectangleGeometry { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height + 24) };
        }

        private void ShowToolbarButton_Click(object sender, RoutedEventArgs e)
        {
            if (_toolbarOverlayOpen) HideToolbarOverlay();
            else ShowToolbarOverlay();
        }

        private void AlwaysShowToolbarItem_Click(object sender, RoutedEventArgs e)
        {
            SetToolbarAutoHide(false);
        }

        private void AutoHideToolbarItem_Click(object sender, RoutedEventArgs e)
        {
            SetToolbarAutoHide(true);
        }

        private void AutoHideToolbarViewToggle_Click(object sender, RoutedEventArgs e)
        {
            SetToolbarAutoHide(AutoHideToolbarViewToggle.IsChecked);
        }

        private void ContentArea_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_toolbarAutoHide && _toolbarOverlayOpen) HideToolbarOverlay();
        }

        #endregion

        #region Zoom

        private static string ZoomText(double zoom) =>
            Math.Round(zoom * 100).ToString(System.Globalization.CultureInfo.CurrentCulture) + "%";

        private void InitializeZoomBox()
        {
            _updatingZoomBox = true;
            ZoomComboBox.Items.Clear();
            foreach (double level in ZoomLevels) ZoomComboBox.Items.Add(ZoomText(level));
            _updatingZoomBox = false;
            UpdateZoomBox();
        }

        private void UpdateZoomBox()
        {
            if (ZoomComboBox == null || ZoomSlider == null) return;
            _updatingZoomBox = true;
            try
            {
                string text = ZoomText(ZoomSlider.Value);
                int index = ZoomComboBox.Items.IndexOf(text);
                ZoomComboBox.SelectedIndex = index;
                ZoomComboBox.Text = text;
            }
            finally
            {
                _updatingZoomBox = false;
            }
        }

        private static bool TryParseZoom(string text, out double zoom)
        {
            zoom = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text.Trim().TrimEnd('%').Trim().Replace(',', '.');
            if (!double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double percent)) return false;
            zoom = percent / 100.0;
            return zoom > 0;
        }

        private void ZoomComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingZoomBox) return;
            if (ZoomComboBox.SelectedItem is string text && TryParseZoom(text, out double zoom))
            {
                ZoomSlider.Value = Math.Max(ZoomSlider.Minimum, Math.Min(ZoomSlider.Maximum, zoom));
            }
        }

        private void ZoomComboBox_TextSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args)
        {
            if (TryParseZoom(args.Text, out double zoom))
            {
                ZoomSlider.Value = Math.Max(ZoomSlider.Minimum, Math.Min(ZoomSlider.Maximum, zoom));
            }
            args.Handled = true;
            UpdateZoomBox();
        }

        private void SetFitToWindowActive(bool active)
        {
            _fitToWindowActive = active;
            FitIconCircle.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        }

        private void FitToWindow_Click(object sender, RoutedEventArgs e)
        {
            ApplyFitToWindow();
            SetFitToWindowActive(true);
        }

        private void ApplyFitToWindow()
        {
            double viewW = RichTextScrollView.ViewportWidth > 0 ? RichTextScrollView.ViewportWidth : RichTextScrollView.ActualWidth;
            double viewH = RichTextScrollView.ViewportHeight > 0 ? RichTextScrollView.ViewportHeight : RichTextScrollView.ActualHeight;
            double pageW = EditorMandatoryPrintingGrid.ActualWidth > 0 ? EditorMandatoryPrintingGrid.ActualWidth : EditorMandatoryPrintingGrid.Width;
            double pageH = _pageHeightDip;
            if (viewW <= 0 || viewH <= 0 || pageW <= 0 || pageH <= 0) return;

            double zoom = Math.Min((viewW - 24) / pageW, (viewH - 24) / pageH);
            zoom = Math.Max(ZoomSlider.Minimum, Math.Min(ZoomSlider.Maximum, zoom));
            _applyingFit = true;
            try
            {
                ZoomSlider.Value = Math.Round(zoom, 2);
                PrepareEditorMinHeightForZoom(ZoomSlider.Value);
                RichTextScrollView.ChangeView(0, 0, (float)ZoomSlider.Value);
            }
            finally
            {
                _applyingFit = false;
            }
        }

        private void FullScreen_Click(object sender, RoutedEventArgs e)
        {
            ApplicationView view = ApplicationView.GetForCurrentView();
            if (view.IsFullScreenMode) view.ExitFullScreenMode();
            else view.TryEnterFullScreenMode();
        }

        #endregion

        #region Mouse wheel

        private static T FindDescendant<T>(DependencyObject root, string name) where T : FrameworkElement
        {
            if (root == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is T match && (name == null || match.Name == name)) return match;
                T found = FindDescendant<T>(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private void HookEditorScrollViewer()
        {
            if (_editorScrollViewer != null) return;
            _editorScrollViewer = FindDescendant<ScrollViewer>(Editor, "ContentElement");
            if (_editorScrollViewer == null) return;

            _editorScrollViewer.ViewChanged += EditorScrollViewer_ViewChanged;

            var presenter = FindDescendant<ScrollContentPresenter>(_editorScrollViewer, null);
            presenter?.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(Editor_PointerWheelChangedAll), true);
        }

        private void EditorScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            _pictureAdorner?.TrackScrolling();
            if (_wheelSelecting) UpdateWheelSelection();
        }

        private void Editor_PointerPressedAll(object sender, PointerRoutedEventArgs e)
        {
            HookEditorScrollViewer();
            var point = e.GetCurrentPoint(Editor);
            _wheelSelecting = false;
            _wheelAnchor = -1;
            _lastPointerWindowPos = e.GetCurrentPoint(null).Position;

            if (point.Properties.IsRightButtonPressed)
            {
                _lastRightClickPoint = point.Position;
                _lastRightClickTime = DateTime.UtcNow;
            }

            _selectingWithMouse = point.PointerDevice.PointerDeviceType == Windows.Devices.Input.PointerDeviceType.Mouse && point.Properties.IsLeftButtonPressed;
            if (_selectingWithMouse) _selectionBar?.Hide();
            SetLinkHover(null, false);
        }

        private void Editor_PointerMovedAll(object sender, PointerRoutedEventArgs e)
        {
            _lastPointerWindowPos = e.GetCurrentPoint(null).Position;
            UpdateLinkHover(e);
        }

        private void Editor_PointerReleasedAll(object sender, PointerRoutedEventArgs e)
        {
            _wheelSelecting = false;
            _wheelAnchor = -1;

            if (_selectingWithMouse)
            {
                _selectingWithMouse = false;
                ShowSelectionBar(e.GetCurrentPoint(Editor).Position);
            }
        }

        #region Selection toolbar

        // The editor's own selection flyout uses "dismiss on pointer move away", which closes it as soon
        // as the mouse moves towards it, and it takes the light theme of the page. This one stays open
        // until the user clicks elsewhere or types, and follows the app theme.
        private Microsoft.UI.Xaml.Controls.TextCommandBarFlyout _selectionBar;
        private bool _selectingWithMouse;

        // The editor sits in a container with its own (light or dark) theme, and menus opened from it
        // take that theme. Menus are part of the app's interface, so they follow the app theme instead.
        private void ApplyAppThemeToEditorMenus()
        {
            Windows.UI.Xaml.Style style = new Windows.UI.Xaml.Style(typeof(Windows.UI.Xaml.Controls.MenuFlyoutPresenter));
            if (Windows.UI.Xaml.Application.Current.Resources.TryGetValue("DefaultMenuFlyoutPresenterStyle", out object basedOn) && basedOn is Windows.UI.Xaml.Style baseStyle)
            {
                style.BasedOn = baseStyle;
            }
            style.Setters.Add(new Windows.UI.Xaml.Setter(Windows.UI.Xaml.FrameworkElement.RequestedThemeProperty, this.ActualTheme));
            EditorContextMenu.MenuFlyoutPresenterStyle = style;
        }

        private void SetupSelectionBar()
        {
            ApplyAppThemeToEditorMenus();
            this.ActualThemeChanged += (s, e) => ApplyAppThemeToEditorMenus();

            Editor.SelectionFlyout = null;

            // keeps found text visible while the find box has the focus
            if (Editor.SelectionHighlightColor is SolidColorBrush highlight)
            {
                Editor.SelectionHighlightColorWhenNotFocused = new SolidColorBrush(highlight.Color) { Opacity = 0.6 };
            }
            _selectionBar = new Microsoft.UI.Xaml.Controls.TextCommandBarFlyout();
            _selectionBar.Opened += (s, e) => ApplyThemeToOpenPopups();
        }

        private void ShowSelectionBar(Point position)
        {
            if (_selectionBar == null || _printPreviewOpen) return;

            // wait for the editor to finish the selection (double clicks select the word after the release)
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
            {
                try
                {
                    if (_selectingWithMouse || Editor.Document.Selection.Length == 0) return;
                    if (_pictureAdorner != null && _pictureAdorner.SelectedObjectStart >= 0) return;

                    _selectionBar.ShowAt(Editor, new Windows.UI.Xaml.Controls.Primitives.FlyoutShowOptions
                    {
                        Position = position,
                        Placement = Windows.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.TopEdgeAlignedLeft,
                        ShowMode = Windows.UI.Xaml.Controls.Primitives.FlyoutShowMode.Transient
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("ShowSelectionBar: " + ex.Message);
                }
            });
        }

        private void HideSelectionBarIfCollapsed()
        {
            if (_selectionBar != null && _selectionBar.IsOpen && Editor.Document.Selection.Length == 0) _selectionBar.Hide();
        }

        private static void ApplyThemeToOpenPopups()
        {
            if (!(Window.Current?.Content is FrameworkElement root)) return;
            foreach (Windows.UI.Xaml.Controls.Primitives.Popup popup in VisualTreeHelper.GetOpenPopups(Window.Current))
            {
                // menus and toolbars follow the app theme; the editor color only applies to the page.
                // Flyouts copy the theme of the element they open from (the page), so this is set
                // again after they open
                if (popup.Child is FrameworkElement child && child.RequestedTheme != root.ActualTheme)
                {
                    child.RequestedTheme = root.ActualTheme;
                }
            }
        }

        private static bool IsCommandBarFlyout(DependencyObject element, int depth = 0)
        {
            if (element is Microsoft.UI.Xaml.Controls.Primitives.CommandBarFlyoutCommandBar) return true;
            if (depth > 3) return false;
            int count = VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < count; i++)
            {
                if (IsCommandBarFlyout(VisualTreeHelper.GetChild(element, i), depth + 1)) return true;
            }
            return false;
        }

        #endregion

        #region Links

        private Windows.UI.Xaml.Controls.ToolTip _linkToolTip;
        private string _hoveredLink;
        private bool _handCursor;

        private void UpdateLinkHover(PointerRoutedEventArgs e)
        {
            Windows.UI.Input.PointerPoint point = e.GetCurrentPoint(Editor);
            if (point.PointerDevice.PointerDeviceType != Windows.Devices.Input.PointerDeviceType.Mouse || point.Properties.IsLeftButtonPressed ||
                point.Properties.IsRightButtonPressed || _printPreviewOpen || (_pictureAdorner?.IsDragging ?? false))
            {
                SetLinkHover(null, false);
                return;
            }

            string link = null;
            try
            {
                Point client = EditorCoordinates.ToClient(Editor, Editor, point.Position);
                int position = Editor.Document.GetRangeFromPoint(client, PointOptions.ClientCoordinates).StartPosition;

                // the nearest insertion point can be on either side of the character under the pointer
                link = LinkAt(position, client) ?? (position > 0 ? LinkAt(position - 1, client) : null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("UpdateLinkHover: " + ex.Message);
            }

            bool ctrl = Window.Current.CoreWindow.GetKeyState(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
            SetLinkHover(link, ctrl);
        }

        private string LinkAt(int position, Point client)
        {
            ITextRange character = Editor.Document.GetRange(position, position + 1);
            if (character.EndPosition <= character.StartPosition) return null;
            character.GetRect(PointOptions.ClientCoordinates, out Rect rect, out _);
            if (rect.Width <= 0 || client.X < rect.Left - 1 || client.X > rect.Right + 1 || client.Y < rect.Top || client.Y > rect.Bottom) return null;

            string link = character.Link;
            if (string.IsNullOrEmpty(link)) return null;
            return link.Trim().Trim('"');
        }

        private void SetLinkHover(string link, bool ctrl)
        {
            if (link != _hoveredLink)
            {
                _hoveredLink = link;
                if (_linkToolTip == null) _linkToolTip = new Windows.UI.Xaml.Controls.ToolTip();
                _linkToolTip.IsOpen = false;

                if (link != null)
                {
                    string hint = Loc.Get("link_hint", "Ctrl+click to follow link");
                    _linkToolTip.Content = string.IsNullOrEmpty(link) ? hint : link + "\n" + hint;
                    ToolTipService.SetToolTip(Editor, _linkToolTip);
                }
                else
                {
                    ToolTipService.SetToolTip(Editor, null);
                }
            }

            bool hand = link != null && ctrl;
            if (hand)
            {
                Window.Current.CoreWindow.PointerCursor = new CoreCursor(CoreCursorType.Hand, 0);
                _handCursor = true;
            }
            else if (_handCursor)
            {
                Window.Current.CoreWindow.PointerCursor = new CoreCursor(CoreCursorType.IBeam, 0);
                _handCursor = false;
            }
        }

        #endregion

        private void Editor_PointerWheelChangedAll(object sender, PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(Editor);
            if (!point.Properties.IsLeftButtonPressed || point.Properties.IsHorizontalMouseWheel) return;

            e.Handled = true;
            if (point.Timestamp == _lastWheelTimestamp) return;
            _lastWheelTimestamp = point.Timestamp;

            HookEditorScrollViewer();
            _lastPointerWindowPos = e.GetCurrentPoint(null).Position;

            if (!_wheelSelecting)
            {
                ITextSelection sel = Editor.Document.Selection;
                bool startActive = (sel.Options & SelectionOptions.StartActive) == SelectionOptions.StartActive;
                _wheelAnchor = startActive ? sel.EndPosition : sel.StartPosition;
                _wheelSelecting = true;
            }

            int delta = point.Properties.MouseWheelDelta;
            if (delta == 0) return;
            double distance = delta / 120.0 * 3 * 19;

            ScrollViewer inner = _editorScrollViewer;
            bool innerCanScroll = inner != null && inner.ScrollableHeight > 0 &&
                                  (delta > 0 ? inner.VerticalOffset > 0.5 : inner.VerticalOffset < inner.ScrollableHeight - 0.5);

            if (innerCanScroll)
            {
                double target = Math.Max(0, Math.Min(inner.ScrollableHeight, inner.VerticalOffset - distance));
                inner.ChangeView(null, target, null, true);
            }
            else
            {
                ScrollViewer outer = RichTextScrollView;
                double target = Math.Max(0, Math.Min(outer.ScrollableHeight, outer.VerticalOffset - distance * outer.ZoomFactor));
                outer.ChangeView(null, target, null, true);
            }

            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Low, UpdateWheelSelection);
        }

        private void UpdateWheelSelection()
        {
            if (!_wheelSelecting || _wheelAnchor < 0) return;
            try
            {
                Point p = Window.Current.Content.TransformToVisual(Editor).TransformPoint(_lastPointerWindowPos);
                double x = Math.Max(1, Math.Min(Editor.ActualWidth - 2, p.X));
                double y = Math.Max(1, Math.Min(Editor.ActualHeight - 2, p.Y));
                Point client = UltraPad.Helpers.EditorCoordinates.ToClient(Editor, Editor, new Point(x, y));
                ITextRange r = Editor.Document.GetRangeFromPoint(client, PointOptions.ClientCoordinates);
                int active = r.StartPosition;
                ITextSelection sel = Editor.Document.Selection;
                if (active >= _wheelAnchor)
                {
                    sel.SetRange(_wheelAnchor, active);
                    sel.Options &= ~SelectionOptions.StartActive;
                }
                else
                {
                    sel.SetRange(active, _wheelAnchor);
                    sel.Options |= SelectionOptions.StartActive;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("UpdateWheelSelection: " + ex.Message);
            }
        }

        #endregion

        #region Autocorrect

        private static bool IsWordChar(char c) => char.IsLetter(c) || c == '\'' || c == '’';

        private void Editor_CharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs args)
        {
            if (!_autocorrectEnabled || !_spellCheckEnabled) return;
            char c = args.Character;
            if (c == ' ' || c == '\r' || c == '\n' || c == '\t' || c == '.' || c == ',' || c == ';' || c == ':' || c == '!' || c == '?' || c == ')' || c == '"')
            {
                _ = Dispatcher.RunAsync(CoreDispatcherPriority.Low, TryAutoCorrectBeforeCaret);
            }
        }

        private void TryAutoCorrectBeforeCaret()
        {
            try
            {
                ITextSelection sel = Editor.Document.Selection;
                if (sel.Length != 0) return;
                int caret = sel.StartPosition;
                if (caret < 3) return;

                int from = Math.Max(0, caret - 64);
                Editor.Document.GetRange(from, caret).GetText(TextGetOptions.None, out string text);
                if (string.IsNullOrEmpty(text) || text.Length != caret - from) return;

                int end = text.Length - 1;
                if (IsWordChar(text[end])) return;
                int start = end;
                while (start > 0 && IsWordChar(text[start - 1])) start--;
                if (end - start < 2) return;

                if (start > 0 && !char.IsWhiteSpace(text[start - 1]) && "([{\"„“".IndexOf(text[start - 1]) < 0) return;

                string word = text.Substring(start, end - start).Trim('\'', '’');
                if (word.Length < 2 || word.Any(char.IsDigit)) return;
                int wordStart = from + text.IndexOf(word, start, StringComparison.Ordinal);

                string fix = SpellCheckHelper.GetAutoCorrection(word);
                if (string.IsNullOrEmpty(fix) || fix == word) return;

                if (word.Length > 1 && word.All(ch => !char.IsLetter(ch) || char.IsUpper(ch))) fix = fix.ToUpper();
                else if (char.IsUpper(word[0]) && char.IsLower(fix[0])) fix = char.ToUpper(fix[0]) + fix.Substring(1);

                ITextRange wordRange = Editor.Document.GetRange(wordStart, wordStart + word.Length);
                wordRange.GetText(TextGetOptions.None, out string check);
                if (check != word) return;

                Editor.Document.BeginUndoGroup();
                try { wordRange.Text = fix; }
                finally { Editor.Document.EndUndoGroup(); }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("TryAutoCorrectBeforeCaret: " + ex.Message);
            }
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

        private void EditorContextMenu_Opening(object sender, object e)
        {
            RemoveSpellMenuItems();
            if (!_spellCheckEnabled) return;

            try
            {
                int position;
                if ((DateTime.UtcNow - _lastRightClickTime).TotalSeconds < 2)
                    position = Editor.Document.GetRangeFromPoint(UltraPad.Helpers.EditorCoordinates.ToClient(Editor, Editor, _lastRightClickPoint), PointOptions.ClientCoordinates).StartPosition;
                else
                    position = Editor.Document.Selection.StartPosition;

                ITextRange range = Editor.Document.GetRange(position, position);
                range.Expand(TextRangeUnit.Word);
                range.GetText(TextGetOptions.None, out string raw);
                if (string.IsNullOrEmpty(raw)) return;

                int s = 0, t = raw.Length;
                while (s < t && !IsWordChar(raw[s])) s++;
                while (t > s && !IsWordChar(raw[t - 1])) t--;
                string word = raw.Substring(s, t - s).Trim('\'', '’');
                if (word.Length < 2 || word.Any(char.IsDigit)) return;
                int wordStart = range.StartPosition + raw.IndexOf(word, s, StringComparison.Ordinal);

                if (!SpellCheckHelper.IsMisspelled(word, 5, out List<string> suggestions)) return;

                int index = 0;
                foreach (string suggestion in suggestions)
                {
                    var item = new MenuFlyoutItem { Text = suggestion, FontWeight = Windows.UI.Text.FontWeights.SemiBold };
                    string replacement = suggestion;
                    item.Click += (a, b) => ReplaceWord(wordStart, word, replacement);
                    InsertSpellMenuItem(index++, item);
                }
                if (suggestions.Count == 0)
                {
                    InsertSpellMenuItem(index++, new MenuFlyoutItem { Text = L("spellNoSuggestions", "(No spelling suggestions)"), IsEnabled = false });
                }

                var add = new MenuFlyoutItem { Text = L("spellAddToDictionary", "Add to dictionary"), Icon = new FontIcon { Glyph = "" } };
                add.Click += (a, b) =>
                {
                    SpellCheckHelper.AddToDictionary(word);
                    Editor.IsSpellCheckEnabled = false;
                    Editor.IsSpellCheckEnabled = _spellCheckEnabled;
                };
                InsertSpellMenuItem(index++, add);
                InsertSpellMenuItem(index, new MenuFlyoutSeparator());
            }
            catch (Exception ex)
            {
                Debug.WriteLine("EditorContextMenu_Opening: " + ex.Message);
            }
        }

        private void InsertSpellMenuItem(int index, MenuFlyoutItemBase item)
        {
            EditorContextMenu.Items.Insert(index, item);
            _spellMenuItems.Add(item);
        }

        private void RemoveSpellMenuItems()
        {
            foreach (MenuFlyoutItemBase item in _spellMenuItems) EditorContextMenu.Items.Remove(item);
            _spellMenuItems.Clear();
        }

        private void EditorContextMenu_Closed(object sender, object e)
        {
        }

        private void ReplaceWord(int start, string word, string replacement)
        {
            ITextRange range = Editor.Document.GetRange(start, start + word.Length);
            range.GetText(TextGetOptions.None, out string current);
            if (current != word) return;
            range.Text = replacement;
            Editor.Document.Selection.SetRange(start + replacement.Length, start + replacement.Length);
            Editor.Focus(FocusState.Programmatic);
        }

        #endregion

        #region Objects

        private void Editor_DoubleTappedAll(object sender, DoubleTappedRoutedEventArgs e)
        {
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Low, async () =>
            {
                await EmbeddedObjectManager.For(Editor).TryActivateSelectedObjectAsync();
            });
        }

        private async void Window_Activated(object sender, WindowActivatedEventArgs e)
        {
            if (e.WindowActivationState == CoreWindowActivationState.Deactivated) return;
            await EmbeddedObjectManager.For(Editor).RefreshChangedObjectsAsync();
        }

        #endregion

        #region Clipboard

        private async Task PasteAsync()
        {
            if (_inPaste) return;
            _inPaste = true;
            _pictureAdorner?.Materialize(false);
            try
            {
                if (await ImageInsertHelper.PasteFromClipboardAsync(Editor)) return;

                int start = Editor.Document.Selection.StartPosition;
                Editor.Document.Selection.Paste(0);

                if (_isEditorDark)
                {
                    int end = Editor.Document.Selection.EndPosition;
                    if (end > start) RecolorRange(Editor.Document.GetRange(start, end), Black, White);
                    _isDirty = true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("PasteAsync: " + ex.Message);
            }
            finally
            {
                _inPaste = false;
                _pictureAdorner?.EndCommand();
            }
        }

        // Shift+Insert (and clipboard managers like CopyQ that send it) goes through the editor's
        // own paste, unlike Ctrl+V which comes from the menu shortcut. Changing the document from
        // inside that event (picture handling, the paste itself) crashed the editor, so the paste
        // runs right after the editor has finished handling the key
        private void Editor_Paste(object sender, Windows.UI.Xaml.Controls.TextControlPasteEventArgs e)
        {
            if (_inPaste) return;
            e.Handled = true;
            _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, async () =>
            {
                try { await PasteAsync(); }
                catch (Exception ex) { Debug.WriteLine("Editor_Paste: " + ex.Message); }
            });
        }

        private void CopySelection(bool cut)
        {
            _pictureAdorner?.Materialize(false);
            if (cut) Editor.Document.Selection.Cut();
            else Editor.Document.Selection.Copy();
            _pictureAdorner?.EndCommand();
            if (_isEditorDark) FixClipboardColorsAfterCopy();
        }

        private async void FixClipboardColorsAfterCopy()
        {
            try
            {
                DataPackageView content = Clipboard.GetContent();
                if (!content.Contains(StandardDataFormats.Rtf)) return;

                string rtf = await content.GetRtfAsync();
                string fixedRtf = RecolorRtfColorTable(rtf, blackToWhite: false);
                if (fixedRtf == rtf) return;

                var package = new DataPackage();
                package.SetRtf(fixedRtf);
                if (content.Contains(StandardDataFormats.Text)) package.SetText(await content.GetTextAsync());
                if (content.Contains(StandardDataFormats.Bitmap)) package.SetBitmap(await content.GetBitmapAsync());
                Clipboard.SetContent(package);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("FixClipboardColorsAfterCopy: " + ex.Message);
            }
        }

        #endregion

        #region Recent files

        private async Task PopulateRecentsAsync()
        {
            var mru = StorageApplicationPermissions.MostRecentlyUsedList;
            var items = new List<KeyValuePair<string, string>>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (AccessListEntry entry in mru.Entries.ToList())
            {
                string path = entry.Metadata;
                if (string.IsNullOrEmpty(path))
                {
                    try
                    {
                        StorageFile f = await mru.GetFileAsync(entry.Token, AccessCacheOptions.SuppressAccessTimeUpdate);
                        path = f.Path;
                    }
                    catch
                    {
                        mru.Remove(entry.Token);
                        continue;
                    }
                }

                if (!seen.Add(path))
                {
                    mru.Remove(entry.Token);
                    continue;
                }
                items.Add(new KeyValuePair<string, string>(entry.Token, path));
            }

            RecentItemsSubItem.Items.Clear();
            foreach (var item in items.Take(MaxRecentFiles))
            {
                var menuItem = new MenuFlyoutItem
                {
                    Text = System.IO.Path.GetFileName(item.Value),
                    Tag = item.Key
                };
                ToolTipService.SetToolTip(menuItem, item.Value);
                menuItem.Click += RecentItem_Click;
                RecentItemsSubItem.Items.Add(menuItem);
            }

            if (RecentItemsSubItem.Items.Count == 0)
            {
                RecentItemsSubItem.Items.Add(new MenuFlyoutItem { Text = L("norecent", "No recent documents"), IsEnabled = false });
            }
        }

        private async void RecentItem_Click(object sender, RoutedEventArgs e)
        {
            string token = (sender as FrameworkElement)?.Tag as string;
            if (string.IsNullOrEmpty(token)) return;

            if (HasUnsavedChanges())
            {
                var proceed = await ShowUnsavedDialog();
                if (proceed != true) return;
            }

            var mru = StorageApplicationPermissions.MostRecentlyUsedList;
            StorageFile file = null;
            try
            {
                file = await mru.GetFileAsync(token);
            }
            catch
            {
                file = null;
            }

            if (file == null)
            {
                mru.Remove(token);
                await PopulateRecentsAsync();
                await new ContentDialog
                {
                    Title = appTitleStr,
                    Content = L("recentmissing", "The file could not be found. It may have been moved or deleted."),
                    CloseButtonText = L("dlg_ok", "OK")
                }.ShowThemedAsync();
                return;
            }

            await OpenFileAsync(file, interactive: true);
        }

        private void AddToRecent(StorageFile file)
        {
            try
            {
                var mru = StorageApplicationPermissions.MostRecentlyUsedList;
                foreach (AccessListEntry entry in mru.Entries.ToList())
                {
                    if (string.Equals(entry.Metadata, file.Path, StringComparison.OrdinalIgnoreCase))
                        mru.Remove(entry.Token);
                }
                mru.Add(file, file.Path);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("AddToRecent: " + ex.Message);
            }
            _ = PopulateRecentsAsync();
        }

        #endregion

        // This page stays cached while Settings is open, outside the window's visual tree. A theme
        // changed meanwhile only reached part of it, so back here some of the ribbon kept the old
        // colors (until a popup happened to redraw it). Switching the page to the other theme and
        // back to inheriting makes every theme resource on it update.
        private void RefreshThemeResources()
        {
            try
            {
                Windows.UI.Xaml.ElementTheme current = this.ActualTheme;
                this.RequestedTheme = current == Windows.UI.Xaml.ElementTheme.Dark
                    ? Windows.UI.Xaml.ElementTheme.Light
                    : Windows.UI.Xaml.ElementTheme.Dark;
                this.RequestedTheme = Windows.UI.Xaml.ElementTheme.Default;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("RefreshThemeResources: " + ex.Message);
            }
        }

        private void LoadThemeFromSettings()
        {
            var values = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
            string value = values["theme"] as string ?? values["themeSetting"] as string ?? "System";
            App.ApplyAppTheme(value);
            Window.Current.SetTitleBar(AppTitleBar);
        }

        private void ConnectRibbonToolbars()
        {
            editribbontoolbar.Editor = Editor;
            editribbontoolbar.RevealSelection = () => QueueCaretIntoView(true);
            insertribbontoolbar.Editor = Editor;
            pararibbontoolbar.Editor = Editor;
            fontribbontoolbar.Editor = Editor;

            //collapsed variants also need to be 'connected'
            editribbontoolbarcol.Editor = Editor;
            editribbontoolbarcol.RevealSelection = () => QueueCaretIntoView(true);
            insertribbontoolbarcol.Editor = Editor;
            pararibbontoolbarcol.Editor = Editor;
            pararibbontoolbarcol.MainPagea = this;
            fontribbontoolbarcol.Editor = Editor;
            TextRuler.TargetEditor = Editor;
        }

        private MarkerType _type = MarkerType.Bullet;

        private void MyListButton_IsCheckedChanged(Microsoft.UI.Xaml.Controls.ToggleSplitButton sender, Microsoft.UI.Xaml.Controls.ToggleSplitButtonIsCheckedChangedEventArgs args)
        {
            if (sender.IsChecked)
            {
                //add bulleted list
                Editor.Document.Selection.ParagraphFormat.ListType = _type;
            }
            else
            {
                //remove bulleted list
                Editor.Document.Selection.ParagraphFormat.ListType = MarkerType.None;
            }
        }

        protected async override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            Window.Current.SetTitleBar(AppTitleBar);

            if (e.Parameter is StorageFile file)
            {
                await OpenFileAsync(file, interactive: false);
            }
            else if (_initialized)
            {
                LoadThemeFromSettings();
                RefreshThemeResources();
                ApplyEditorSettings();
                ApplyPageLayout();
                UpdateViewMenuChecks();
                TextRuler.UpdatePagePosition(true);
            }

            _initialized = true;
        }

        public async Task OpenFromActivationAsync(StorageFile file)
        {
            if (HasUnsavedChanges())
            {
                var proceed = await ShowUnsavedDialog();
                if (proceed != true) return;
            }
            await OpenFileAsync(file, interactive: false);
        }

        private async Task<bool> OpenFileAsync(StorageFile file, bool interactive)
        {
            if (file == null) return false;
            string ext = file.FileType.ToLowerInvariant();

            DocumentFileFormat format = DocumentFileFormat.Rtf;

            ulong fileSize = 0;
            try { fileSize = (await file.GetBasicPropertiesAsync()).Size; } catch { }
            _largeDocument = fileSize > LargeDocumentBytes;
            bool showProgress = fileSize > LoadingProgressBytes;
            if (showProgress) ShowLoading(0);

            BeginProgrammaticChange();
            try
            {
                if (ext == ".rtf")
                {
                    string rtf;

                    using (var stream = await file.OpenStreamForReadAsync())
                    using (var reader = new StreamReader(stream))
                    {
                        if (showProgress)
                        {
                            // read in pieces so the status bar can show how far it got
                            long length = Math.Max(1, stream.Length);
                            var builder = new StringBuilder((int)Math.Min(int.MaxValue, length));
                            char[] buffer = new char[1024 * 1024];
                            int read;
                            while ((read = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                builder.Append(buffer, 0, read);
                                ShowLoading((int)(Math.Min(1.0, stream.Position / (double)length) * 60));
                            }
                            rtf = builder.ToString();
                        }
                        else
                        {
                            rtf = await reader.ReadToEndAsync();
                        }
                    }

                    if (showProgress) ShowLoading(65);
                    rtf = await LegacyPictures.ConvertAsync(rtf);

                    if (_isEditorDark) rtf = RecolorRtfColorTable(rtf, blackToWhite: true);

                    if (showProgress) await ShowLoadingFinalStepAsync();
                    Editor.Document.SetText(TextSetOptions.FormatRtf, rtf);
                }
                else if (ext == ".docx" || ext == ".odt")
                {
                    format = ext == ".docx" ? DocumentFileFormat.Docx : DocumentFileFormat.Odt;
                    if (showProgress) ShowLoading(10);
                    string loadedRtf = await DocumentFormats.LoadAsRtfAsync(file, format);
                    if (showProgress) ShowLoading(60);
                    string rtf = await LegacyPictures.ConvertAsync(loadedRtf);
                    if (_isEditorDark) rtf = RecolorRtfColorTable(rtf, blackToWhite: true);
                    if (showProgress) await ShowLoadingFinalStepAsync();
                    Editor.Document.SetText(TextSetOptions.FormatRtf, rtf);
                }
                else
                {
                    KeyValuePair<string, DocumentFileFormat> loaded = await DocumentFormats.LoadTextAsync(file);
                    format = loaded.Value;
                    Editor.Document.SetText(TextSetOptions.None, loaded.Key);
                }

                EmbeddedObjectManager.For(Editor).Clear();

                // text without an explicit color (and all plain text) is drawn black; the color table
                // change above only covers explicitly black text, so recolor the runs like the setting does
                if (_isEditorDark) RecolorRange(null, Black, White);

                try { Editor.TextDocument.ClearUndoRedoHistory(); } catch { }
                Editor.Document.Selection.SetRange(0, 0);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("OpenFileAsync: " + ex.Message);
                await new ContentDialog
                {
                    Title = appTitleStr,
                    Content = Loc.Format("dlg_openfailed", "File {0} couldn't be opened.", file.Name),
                    CloseButtonText = Loc.Get("dlg_ok", "OK")
                }.ShowThemedAsync();
                return false;
            }
            finally
            {
                EndProgrammaticChange();
                if (showProgress) HideLoading();
            }

            _currentFile = file;
            _currentFormat = format;
            ShowFormatWarning(format == DocumentFileFormat.Docx || format == DocumentFileFormat.Odt);
            _currentFilePath = file.Path;
            fileNameWithPath = file.Path;
            AppTitle.Text = file.Name + " - " + appTitleStr;

            AddToRecent(file);
            StorageApplicationPermissions.FutureAccessList.AddOrReplace("CurrentlyOpenFile", file);

            Editor.Document.GetText(TextGetOptions.None, out _lastSavedText);
            MarkClean();
            TextRuler.RefreshFromSelection();
            Editor.Focus(FocusState.Programmatic);

            // formatting the toolbars apply right after loading can bring black text back, so the
            // dark editor recolors once more when that is done (still not counted as a change)
            if (_isEditorDark)
            {
                _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () =>
                {
                    if (_isEditorDark) RecolorRange(null, Black, White);
                });
            }
            return true;
        }

        private async void Open_Click(object sender, RoutedEventArgs e)
        {
            if (HasUnsavedChanges())
            {
                var proceed = await ShowUnsavedDialog();
                if (proceed != true) return;
            }

            FileOpenPicker open = new FileOpenPicker();
            open.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            open.FileTypeFilter.Add(".rtf");
            open.FileTypeFilter.Add(".docx");
            open.FileTypeFilter.Add(".odt");
            open.FileTypeFilter.Add(".txt");

            StorageFile file = await open.PickSingleFileAsync();
            if (file == null) return;

            await OpenFileAsync(file, interactive: true);
        }

        private void SubscriptButton_Click(object sender, RoutedEventArgs e)
        {
            Editor.FormatSelected(RichEditHelpers.FormattingMode.Subscript);
        }

        private void SuperScriptButton_Click(object sender, RoutedEventArgs e)
        {
            Editor.FormatSelected(RichEditHelpers.FormattingMode.Superscript);
        }
        private void StrikethroughButton_Click(object sender, RoutedEventArgs e)
        {
            Editor.FormatSelected(RichEditHelpers.FormattingMode.Strikethrough);
        }

        private void NoneNumeral_Click(object sender, RoutedEventArgs e)
        {
            Editor.Document.Selection.ParagraphFormat.ListType = MarkerType.None;
            Editor.Focus(FocusState.Keyboard);
        }

        private void DottedNumeral_Click(object sender, RoutedEventArgs e)
        {
            Editor.Document.Selection.ParagraphFormat.ListType = MarkerType.Bullet;
            Editor.Focus(FocusState.Keyboard);
        }

        private void NumberNumeral_Click(object sender, RoutedEventArgs e)
        {
            Editor.Document.Selection.ParagraphFormat.ListType = MarkerType.Arabic;
            Editor.Focus(FocusState.Keyboard);
        }

        private void LetterSmallNumeral_Click(object sender, RoutedEventArgs e)
        {
            Editor.Document.Selection.ParagraphFormat.ListType = MarkerType.LowercaseEnglishLetter;
            Editor.Focus(FocusState.Keyboard);
        }

        private void LetterBigNumeral_Click(object sender, RoutedEventArgs e)
        {
            Editor.Document.Selection.ParagraphFormat.ListType = MarkerType.UppercaseEnglishLetter;
            Editor.Focus(FocusState.Keyboard);
        }

        private void SmalliNumeral_Click(object sender, RoutedEventArgs e)
        {
            Editor.Document.Selection.ParagraphFormat.ListType = MarkerType.LowercaseRoman;
            Editor.Focus(FocusState.Keyboard);
        }

        private void BigINumeral_Click(object sender, RoutedEventArgs e)
        {
            Editor.Document.Selection.ParagraphFormat.ListType = MarkerType.UppercaseRoman;
            Editor.Focus(FocusState.Keyboard);
        }

        private void AlignRightButton_Click(object sender, RoutedEventArgs e)
        {
            Editor.AlignSelectedTo(RichEditHelpers.AlignMode.Right);
            editor_SelectionChanged(sender, e);
        }

        private void AlignCenterButton_Click(object sender, RoutedEventArgs e)
        {
            Editor.AlignSelectedTo(RichEditHelpers.AlignMode.Center);
            editor_SelectionChanged(sender, e);
        }

        private void AlignLeftButton_Click(object sender, RoutedEventArgs e)
        {
            Editor.AlignSelectedTo(RichEditHelpers.AlignMode.Left);
            editor_SelectionChanged(sender, e);
        }

        private void Undo_Click(object sender, RoutedEventArgs e)
        {
            Editor.Document.Undo();
        }

        private void Redo_Click(object sender, RoutedEventArgs e)
        {
            Editor.Document.Redo();
        }
        private void Cut_Click(object sender, RoutedEventArgs e)
        {
            CopySelection(cut: true);
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            CopySelection(cut: false);
        }

        private async void Paste_Click(Microsoft.UI.Xaml.Controls.SplitButton sender, Microsoft.UI.Xaml.Controls.SplitButtonClickEventArgs args)
        {
            await PasteAsync();
        }

        private void ZoomSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (Zoom100MenuItem != null) Zoom100MenuItem.IsEnabled = Math.Abs(e.NewValue - 1) > 0.001;
            if (!_applyingFit && _fitToWindowActive && FitIconCircle != null) SetFitToWindowActive(false);
            UpdateZoomBox();
            AnimateZoomSecond(e.OldValue, e.NewValue);
        }

        private void EditorContentHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            /*

            The status bar is slightly tinted by the mica backdrop

            Clipping the editor is needed, as the editor has a
            shadow. Without the clip, the shadow would be visible
            on the status bar

            */

            RectangleGeometry rectangle = new RectangleGeometry();
            rectangle.Rect = new Rect(0, 0, EditorContentHost.ActualWidth, EditorContentHost.ActualHeight);
            EditorContentHost.Clip = rectangle;
        }

        private void ToggleButton_Checked(object sender, RoutedEventArgs e)
        {
        }
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            if (Editor != null)
            {
                Editor.Focus(FocusState.Programmatic);

                // Get the position of the last character in the RichEditBox
                int lastPosition = Editor.Document.Selection.EndPosition;

                // Set the selection range to the entire document
                Editor.Document.Selection.SetRange(0, lastPosition);
            }
        }

        private void ToggleButton_Checked_1(object sender, RoutedEventArgs e)
        {
            object value = Editor.Document.Selection.CharacterFormat.Bold = FormatEffect.Toggle;
        }

        private void ToggleButton_Unchecked_1(object sender, RoutedEventArgs e)
        {
            object value = Editor.Document.Selection.CharacterFormat.Bold = FormatEffect.Toggle;
        }

        private async Task<bool?> ShowUnsavedDialog()
        {
            string fileName = AppTitle.Text.Replace(" - " + appTitleStr, "");

            var dialog = new ContentDialog
            {
                Title = Loc.Get("dlg_unsaved_title", "Do you want to save your work?"),
                Content = Loc.Format("dlg_unsaved_content", "There are unsaved changes in \u201C{0}\u201D.", fileName),
                PrimaryButtonText = Loc.Get("dlg_save", "Save"),
                SecondaryButtonText = Loc.Get("dlg_dontsave", "Don't save"),
                CloseButtonText = Loc.Get("dlg_cancel", "Cancel"),
                DefaultButton = ContentDialogButton.Primary
            };

            var result = await dialog.ShowThemedAsync();

            if (result == ContentDialogResult.Secondary)
                return true;

            if (result == ContentDialogResult.Primary)
            {
                return await SaveFileAsync(false, "DefaultFull");
            }

            return false;
        }

        private void ToggleButton_Checked_2(object sender, RoutedEventArgs e)
        {
            object value = Editor.Document.Selection.CharacterFormat.Italic = FormatEffect.Toggle;
        }

        private void ToggleButton_Checked_3(object sender, RoutedEventArgs e)
        {
            object value = Editor.Document.Selection.CharacterFormat.Strikethrough = FormatEffect.Toggle;
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            Editor.ChangeFontSize((float)2);
        }

        private async void SaveAsButton_Click(object sender, RoutedEventArgs e)
        {
            await SaveFileAsync(true, "DefaultFull");
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            await SaveFileAsync(false, "DefaultFull");
        }

        private async void AddImageButton_Click(Microsoft.UI.Xaml.Controls.SplitButton sender, Microsoft.UI.Xaml.Controls.SplitButtonClickEventArgs args)
        {
            // Open an image file.
            FileOpenPicker open = new FileOpenPicker();
            open.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            foreach (string extension in ImageInsertHelper.SupportedExtensions) open.FileTypeFilter.Add(extension);

            StorageFile file = await open.PickSingleFileAsync();

            if (file != null)
            {
                if (!await ImageInsertHelper.InsertFileAsync(Editor, file, selectPicture: true))
                    await ImageInsertHelper.ShowLastErrorAsync();
            }
        }

        private StorageFile _currentFile = null;

        private async Task<bool> SaveFileAsync(bool forceSaveAs, string selectedName)
        {
            StorageFile file = _currentFile;
            DocumentFileFormat format = _currentFormat;

            if (forceSaveAs || file == null)
            {
                var choices = new List<DocumentFileFormat>();
                switch (selectedName)
                {
                    case "RTF": choices.Add(DocumentFileFormat.Rtf); break;
                    case "DOCX": choices.Add(DocumentFileFormat.Docx); break;
                    case "ODT": choices.Add(DocumentFileFormat.Odt); break;
                    case "TXT": choices.Add(DocumentFileFormat.Text); break;
                    case "TXTDOS": choices.Add(DocumentFileFormat.TextDos); break;
                    case "TXTUNICODE": choices.Add(DocumentFileFormat.TextUnicode); break;
                    case "ALL":
                        choices.AddRange(new[] { DocumentFileFormat.Rtf, DocumentFileFormat.Docx, DocumentFileFormat.Odt,
                            DocumentFileFormat.Text, DocumentFileFormat.TextDos, DocumentFileFormat.TextUnicode });
                        if (choices.Remove(_currentFormat)) choices.Insert(0, _currentFormat);
                        break;
                    default:
                        choices.AddRange(new[] { DocumentFileFormat.Rtf, DocumentFileFormat.Docx, DocumentFileFormat.Odt, DocumentFileFormat.Text });
                        DocumentFileFormat current = DocumentFormats.IsText(_currentFormat) ? DocumentFileFormat.Text : _currentFormat;
                        choices.Remove(current);
                        choices.Insert(0, current);
                        break;
                }

                FileSavePicker savePicker = new FileSavePicker
                {
                    SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                    SuggestedFileName = file != null ? System.IO.Path.GetFileNameWithoutExtension(file.Name) : L("newdocumentname", "Document")
                };
                foreach (DocumentFileFormat f in choices)
                {
                    string ext = DocumentFormats.Extension(f);
                    savePicker.FileTypeChoices.Add(DocumentFormats.DisplayName(f) + " (*" + ext + ")", new List<string> { ext });
                }

                StorageFile picked = await savePicker.PickSaveFileAsync();
                if (picked == null) return false;

                DocumentFileFormat newFormat = choices.Count == 1 ? choices[0] : DocumentFormats.FromExtension(picked.FileType);
                if (selectedName == "ALL" && DocumentFormats.IsText(newFormat))
                {
                    DocumentFileFormat? encoding = await AskTextFormatAsync(DocumentFormats.IsText(_currentFormat) ? _currentFormat : DocumentFileFormat.Text);
                    if (encoding == null) return false;
                    newFormat = encoding.Value;
                }
                if (choices.Count > 1 && newFormat == DocumentFileFormat.Text && DocumentFormats.IsText(_currentFormat)) newFormat = _currentFormat;

                if (DocumentFormats.IsText(newFormat) && !DocumentFormats.IsText(_currentFormat))
                {
                    var warning = new ContentDialog
                    {
                        Title = appTitleStr,
                        Content = Loc.Get("dlg_textwarning", "You are about to save the document in a Text-Only format, which will remove all formatting. Are you sure you want to do this?"),
                        PrimaryButtonText = Loc.Get("dlg_yes", "Yes"),
                        CloseButtonText = Loc.Get("dlg_no", "No"),
                        DefaultButton = ContentDialogButton.Primary
                    };
                    if (await warning.ShowThemedAsync() != ContentDialogResult.Primary) return false;
                }

                file = picked;
                format = newFormat;
            }

            try
            {
                CachedFileManager.DeferUpdates(file);

                if (format == DocumentFileFormat.Rtf && !_isEditorDark)
                {
                    using (var stream = await file.OpenAsync(FileAccessMode.ReadWrite))
                    {
                        stream.Size = 0;
                        stream.Seek(0);
                        Editor.Document.SaveToStream(TextGetOptions.FormatRtf, stream);
                    }
                }
                else
                {
                    string rtf = format == DocumentFileFormat.Rtf || !DocumentFormats.IsText(format) ? GetRtfForExport() : null;
                    Editor.Document.GetText(TextGetOptions.None, out string text);
                    byte[] bytes = DocumentFormats.Serialize(format, rtf, text);
                    await FileIO.WriteBytesAsync(file, bytes);
                }

                var status = await CachedFileManager.CompleteUpdatesAsync(file);

                if (status != FileUpdateStatus.Complete && status != FileUpdateStatus.CompleteAndRenamed)
                {
                    await new ContentDialog
                    {
                        Title = Loc.Get("dlg_savefailed_title", "Save failed"),
                        Content = Loc.Format("dlg_savefailed_content", "File {0} couldn't be saved.", file.Name),
                        CloseButtonText = Loc.Get("dlg_ok", "OK")
                    }.ShowThemedAsync();
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("SaveFileAsync: " + ex.Message);
                await new ContentDialog
                {
                    Title = Loc.Get("dlg_savefailed_title", "Save failed"),
                    Content = Loc.Format("dlg_savefailed_content", "File {0} couldn't be saved.", file.Name) + "\n" + ex.Message,
                    CloseButtonText = Loc.Get("dlg_ok", "OK")
                }.ShowThemedAsync();
                return false;
            }

            _currentFile = file;
            _currentFormat = format;
            _currentFilePath = file.Path;
            fileNameWithPath = file.Path;
            MarkClean();

            Editor.Document.GetText(TextGetOptions.None, out _lastSavedText);

            AppTitle.Text = file.Name + " - " + appTitleStr;

            AddToRecent(file);
            return true;
        }

        private void CancelColor_Click(object sender, RoutedEventArgs e)
        {
        }

        private void fontbackgroundcolorsplitbutton_Click(Microsoft.UI.Xaml.Controls.SplitButton sender, Microsoft.UI.Xaml.Controls.SplitButtonClickEventArgs args)
        {
            // If you see this, remind me to look into the splitbutton color applying logic
        }

        private void fontcolorsplitbutton_Click(Microsoft.UI.Xaml.Controls.SplitButton sender, Microsoft.UI.Xaml.Controls.SplitButtonClickEventArgs args)
        {
            // If you see this, remind me to look into the splitbutton color applying logic
        }

        private void RadioButton_Click(object sender, RoutedEventArgs e)
        {
        }

        private void ItalicButton_Click(object sender, RoutedEventArgs e)
        {
            Editor.FormatSelected(RichEditHelpers.FormattingMode.Italic);
        }

        private void BoldButton_Click(object sender, RoutedEventArgs e)
        {
            Editor.FormatSelected(RichEditHelpers.FormattingMode.Bold);
        }

        private void UnderlineButton_Click(object sender, RoutedEventArgs e)
        {
            Editor.FormatSelected(RichEditHelpers.FormattingMode.Underline);
        }

        private void AlignAdjustedButton_Click(object sender, RoutedEventArgs e)
        {
        }

        private void ParagraphButton_Click(object sender, RoutedEventArgs e)
        {
            pararibbontoolbar.ShowParagraphDialog();
        }

        private void editor_SelectionChanged(object sender, RoutedEventArgs e)
        {
            QueueCaretIntoView();
            HideSelectionBarIfCollapsed();

            if (Editor.Document.Selection.CharacterFormat.Size > 0)
            {
                //font size is negative when selection contains multiple font sizes
                //FontSizeBox. = Editor.Document.Selection.CharacterFormat.Size;
            }
            //prevent accidental font changes when selection contains multiple styles
            updateFontFormat = false;
            updateFontFormat = true;
        }

        private void Button_Click_3Async(object sender, RoutedEventArgs e)
        {
        }

        private void DecreaseFontSize_Click(object sender, RoutedEventArgs e)
        {
            // Get a reference to the RichEditBox control
            RichEditBox richEditBox = Editor;

            // Decrease the font size of the currently selected text by 2 points
            richEditBox.Document.Selection.CharacterFormat.Size -= 2;
        }

        private void IncreaseFontSize_Click(object sender, RoutedEventArgs e)
        {
            // Get a reference to the RichEditBox control
            RichEditBox richEditBox = Editor;

            // Increase the font size of the currently selected text by 2 points
            richEditBox.Document.Selection.CharacterFormat.Size += 2;
        }

        private async void Button_Click_4Async(object sender, RoutedEventArgs e)
        {
            ContentDialog dialog = new ContentDialog();
            dialog.Title = Loc.Get("dlg_datetime_title", "Date and time");

            // Create a ListView for the user to select the date format
            ListView listView = new ListView();
            listView.SelectionMode = ListViewSelectionMode.Single;

            // Create a list of date formats to display in the ListView
            List<string> dateFormats = new List<string>();
            dateFormats.Add(DateTime.Now.ToString("dd.M.yyyy"));
            dateFormats.Add(DateTime.Now.ToString("M.dd.yyyy"));
            dateFormats.Add(DateTime.Now.ToString("dd MMM yyyy"));
            dateFormats.Add(DateTime.Now.ToString("dddd, dd MMMM yyyy"));
            dateFormats.Add(DateTime.Now.ToString("dd MMMM yyyy"));
            dateFormats.Add(DateTime.Now.ToString("hh:mm:ss tt"));
            dateFormats.Add(DateTime.Now.ToString("HH:mm:ss"));
            dateFormats.Add(DateTime.Now.ToString("dddd, dd MMMM yyyy, HH:mm:ss"));
            dateFormats.Add(DateTime.Now.ToString("dd MMMM yyyy, HH:mm:ss"));
            dateFormats.Add(DateTime.Now.ToString("MMM dd, yyyy"));

            // Set the ItemsSource of the ListView to the list of date formats
            listView.ItemsSource = dateFormats;

            // Set the content of the ContentDialog to the ListView
            dialog.Content = listView;

            // Make the insert button colored
            dialog.DefaultButton = ContentDialogButton.Primary;

            // Add an "Insert" button to the ContentDialog
            dialog.PrimaryButtonText = Loc.Get("dlg_ok", "OK");
            dialog.PrimaryButtonClick += (s, args) =>
            {
                if (listView.SelectedIndex < 0) return;
                string formattedDate = dateFormats[listView.SelectedIndex];
                Editor.Document.Selection.Text = formattedDate;
            };

            // Add a "Cancel" button to the ContentDialog
            dialog.SecondaryButtonText = Loc.Get("dlg_cancel", "Cancel");

            // Show the ContentDialog
            await dialog.ShowThemedAsync();
        }

        private DocumentPrintHelper _printHelper;
        private DataTemplate customPrintTemplate;
        private async void MenuFlyoutItem_Click(object sender, RoutedEventArgs e)
        {
            await PrintDocumentAsync();
        }

        private async Task PrintDocumentAsync()
        {
            try
            {
                PageGeometry geometry;
                IReadOnlyList<string> pages;
                if (_printPreviewOpen && _printPreview != null && !_printPreview.IsBusy && _printPreview.PageCount > 0)
                {
                    geometry = _printPreview.Geometry;
                    pages = _printPreview.Pages;
                }
                else
                {
                    geometry = PageGeometry.FromPageSettings();
                    Windows.UI.Xaml.Controls.Panel measureHost = _printPreviewOpen ? (Windows.UI.Xaml.Controls.Panel)PrintPreviewHost : EditorMandatoryPrintingGrid;
                    pages = await DocumentPaginator.PaginateAsync(Editor, measureHost, geometry, PrintRtfFilter);
                }

                _printHelper?.Dispose();
                _printHelper = new DocumentPrintHelper(PrintCanvasHost)
                {
                    ApplicationContentMarginLeft = 0,
                    ApplicationContentMarginTop = 0,
                    PrintFullPage = true
                };
                _printHelper.PrintTaskCreated += PresetPaperSource;

                bool numbers = PageSettings.PrintPageNumbers;
                for (int i = 0; i < pages.Count; i++)
                {
                    FrameworkElement page = DocumentPaginator.CreatePage(pages[i], geometry, Editor.Style, i + 1, numbers);
                    _printHelper.AddFrameworkElementToPrint(new Windows.UI.Xaml.Controls.Viewbox
                    {
                        Width = geometry.PageWidth,
                        Height = geometry.PageHeight,
                        Stretch = Windows.UI.Xaml.Media.Stretch.Uniform,
                        Child = page
                    });
                }

                var printHelperOptions = new PrintHelperOptions(true);
                printHelperOptions.Orientation = PageSettings.IsLandscape ? PrintOrientation.Landscape : PrintOrientation.Portrait;
                printHelperOptions.MediaSize = PageSettings.PrintMediaSizeFor(PageSettings.PaperName);
                printHelperOptions.AddDisplayOption(StandardPrintTaskOptions.MediaSize);
                printHelperOptions.AddDisplayOption(StandardPrintTaskOptions.InputBin);

                string title = AppTitle.Text.Replace(" - " + appTitleStr, "");
                await _printHelper.ShowPrintUIAsync(title, printHelperOptions, false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("PrintDocumentAsync: " + ex.Message);
            }
        }

        private string PrintRtfFilter(string rtf) => _isEditorDark ? RecolorRtfColorTable(rtf, blackToWhite: false) : rtf;

        private static void PresetPaperSource(PrintTask task)
        {
            string source = PageSettings.PaperSource;
            if (source == "auto") return;
            try
            {
                var details = Windows.Graphics.Printing.OptionDetails.PrintTaskOptionDetails.GetFromPrintTaskOptions(task.Options);
                if (!details.Options.TryGetValue(StandardPrintTaskOptions.InputBin, out var option)) return;
                if (!(option is Windows.Graphics.Printing.OptionDetails.IPrintItemListOptionDetails list)) return;

                string[] keywords;
                switch (source)
                {
                    case "manual": keywords = new[] { "manual" }; break;
                    case "tray1": keywords = new[] { "tray1", "tray 1", "1" }; break;
                    case "tray2": keywords = new[] { "tray2", "tray 2", "2" }; break;
                    case "tray3": keywords = new[] { "tray3", "tray 3", "3" }; break;
                    case "envelope": keywords = new[] { "envelope" }; break;
                    default: return;
                }

                foreach (object item in list.Items)
                {
                    string text = (item as Windows.Graphics.Printing.OptionDetails.IPrintCustomOptionDetails)?.DisplayName ?? item?.ToString() ?? "";
                    string id = item?.ToString() ?? "";
                    string haystack = (text + " " + id).ToLowerInvariant();
                    if (keywords.Any(k => haystack.Contains(k)))
                    {
                        list.TrySetValue(item);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("PresetPaperSource: " + ex.Message);
            }
        }
        #region Print preview

        private PrintPreviewController _printPreview;
        private bool _printPreviewOpen;

        private async void pintpreview_Click(object sender, RoutedEventArgs e)
        {
            await ShowPrintPreviewAsync();
        }

        private async Task ShowPrintPreviewAsync()
        {
            if (_printPreviewOpen) return;
            _printPreviewOpen = true;

            if (_printPreview == null)
            {
                _printPreview = new PrintPreviewController(PrintPreviewHost, PrintPreviewScroller, PrintPreviewPages);
                _printPreview.StateChanged += UpdatePrintPreviewButtons;
                PrintPreviewScroller.AddHandler(KeyDownEvent, new KeyEventHandler(PrintPreviewHost_KeyDown), true);
            }

            if (_toolbarOverlayOpen) HideToolbarOverlay();
            _pictureAdorner?.Hide();
            PictureAdornerCanvas.Visibility = Visibility.Collapsed;
            Editor.IsEnabled = false;

            PrintPreviewRibbon.Visibility = Visibility.Visible;
            PrintPreviewHost.Visibility = Visibility.Visible;
            SetDocumentViewHidden(true);
            ApplyToolbarMode();
            ApplyRulerVisibility();
            ApplyStatusBarVisibility();
            ZoomStack.Visibility = Visibility.Collapsed;

            PrintPreviewProgress.IsActive = true;
            UpdatePrintPreviewButtons();
            PrintPreviewScroller.Focus(FocusState.Programmatic);
            try
            {
                await _printPreview.ShowAsync(Editor, PrintRtfFilter);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("ShowPrintPreviewAsync: " + ex.Message);
            }
            finally
            {
                PrintPreviewProgress.IsActive = false;
                UpdatePrintPreviewButtons();
            }
        }

        private void SetDocumentViewHidden(bool hidden)
        {
            foreach (UIElement e in new UIElement[] { EditorContentHost, FormatWarningBar, DocTreeBorder, RulerGuideCanvas, RulerBorder })
            {
                if (e == null) continue;
                e.Opacity = hidden ? 0 : 1;
                e.IsHitTestVisible = !hidden;
            }
            if (!hidden) RulerGuideCanvas.IsHitTestVisible = false;
        }

        private void ClosePrintPreview()
        {
            if (!_printPreviewOpen) return;
            _printPreviewOpen = false;
            _printPreview?.Clear();

            PrintPreviewProgress.IsActive = false;
            PrintPreviewHost.Visibility = Visibility.Collapsed;
            PrintPreviewRibbon.Visibility = Visibility.Collapsed;
            SetDocumentViewHidden(false);
            ZoomStack.Visibility = Visibility.Visible;
            Editor.IsEnabled = true;
            PictureAdornerCanvas.Visibility = Visibility.Visible;

            ApplyToolbarMode();
            ApplyRulerVisibility();
            ApplyStatusBarVisibility();
            Editor.Focus(FocusState.Programmatic);
            _pictureAdorner?.Update();
        }

        private void closeprintpreviewclick(object sender, RoutedEventArgs e)
        {
            ClosePrintPreview();
        }

        private void UpdatePrintPreviewButtons()
        {
            PrintPreviewController p = _printPreview;
            bool ready = p != null && !p.IsBusy && p.PageCount > 0;
            SetPreviewButtonEnabled(PrintPreviewPrintButton, ready);
            SetPreviewButtonEnabled(PrintPreviewZoom100Button, ready);
            SetPreviewButtonEnabled(PrintPreviewOnePageButton, ready && p.CanShowOnePage);
            SetPreviewButtonEnabled(PrintPreviewTwoPagesButton, ready && p.CanShowTwoPages);
            SetPreviewButtonEnabled(PrintPreviewPrevButton, ready && p.CanGoBack);
            SetPreviewButtonEnabled(PrintPreviewNextButton, ready && p.CanGoForward);
            SetPreviewButtonEnabled(closeprintpreview, true);

            SetPreviewButtonSelected(PrintPreviewOnePageButton, ready && !p.TwoPages);
            SetPreviewButtonSelected(PrintPreviewTwoPagesButton, ready && p.TwoPages);

            string pageText = "";
            if (ready)
            {
                int first = p.CurrentPage + 1;
                int last = Math.Min(p.PageCount, first + (p.TwoPages ? 1 : 0));
                pageText = last > first
                    ? Loc.Format("pp_pages", "Pages {0}-{1}", first, last)
                    : Loc.Format("pp_page", "Page {0}", first);
            }
            PrintPreviewPageText.Text = pageText;
        }

        private static void SetPreviewButtonEnabled(Windows.UI.Xaml.Controls.Button button, bool enabled)
        {
            button.IsEnabled = enabled;
            if (!(button.Content is Windows.UI.Xaml.Controls.Viewbox box) || !(box.Child is Windows.UI.Xaml.Controls.Canvas canvas)) return;
            box.Opacity = enabled ? 1.0 : 0.4;

            var paths = canvas.Children.OfType<Windows.UI.Xaml.Shapes.Path>().ToList();
            Windows.UI.Xaml.Shapes.Path plain = paths.FirstOrDefault(x => !"accent".Equals(x.Tag));
            foreach (Windows.UI.Xaml.Shapes.Path path in paths.Where(x => "accent".Equals(x.Tag)))
            {
                if (path.Tag is string && path.Resources.Count == 0)
                    path.Resources["AccentStroke"] = path.Stroke;
                path.Stroke = enabled || plain == null ? (Windows.UI.Xaml.Media.Brush)path.Resources["AccentStroke"] : (plain.Stroke ?? plain.Fill);
            }
        }

        private void SetPreviewButtonSelected(Windows.UI.Xaml.Controls.Button button, bool selected)
        {
            if (selected)
            {
                button.Background = PreviewSelectedBrushes.Background;
                button.BorderBrush = PreviewSelectedBrushes.BorderBrush;
            }
            else
            {
                button.ClearValue(Windows.UI.Xaml.Controls.Control.BackgroundProperty);
                button.ClearValue(Windows.UI.Xaml.Controls.Control.BorderBrushProperty);
            }
        }

        private void PrintPreviewZoom100_Click(object sender, RoutedEventArgs e) => _printPreview?.ShowActualSize();
        private void PrintPreviewOnePage_Click(object sender, RoutedEventArgs e) => _printPreview?.ShowOnePage();
        private void PrintPreviewTwoPages_Click(object sender, RoutedEventArgs e) => _printPreview?.ShowTwoPages();
        private void PrintPreviewPrev_Click(object sender, RoutedEventArgs e) => _printPreview?.GoBack();
        private void PrintPreviewNext_Click(object sender, RoutedEventArgs e) => _printPreview?.GoForward();

        private void PrintPreviewHost_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Escape)
            {
                ClosePrintPreview();
                e.Handled = true;
                return;
            }
            if (_printPreview != null && _printPreview.HandleKey(e.Key)) e.Handled = true;
        }

        #endregion

        bool isTextChanged = false;
        private readonly bool isCopy;

        private async void OnCloseRequest(object sender, SystemNavigationCloseRequestedPreviewEventArgs e)
        {
            if (HasUnsavedChanges())
            {
                e.Handled = true;

                var result = await ShowUnsavedDialog();
                if (result == true)
                {
                    await ApplicationView.GetForCurrentView().TryConsolidateAsync();
                }
            }
        }

        private async void SaveAsRTF_Click(object sender, RoutedEventArgs e)
        {
            await SaveFileAsync(true, "RTF");
        }

        private async void SaveAsDOCX_Click(object sender, RoutedEventArgs e)
        {
            await SaveFileAsync(true, "DOCX");
        }

        private async void SaveAsODT_Click(object sender, RoutedEventArgs e)
        {
            await SaveFileAsync(true, "ODT");
        }

        private async Task<DocumentFileFormat?> AskTextFormatAsync(DocumentFileFormat preselected)
        {
            var formats = new[] { DocumentFileFormat.Text, DocumentFileFormat.TextDos, DocumentFileFormat.TextUnicode };
            var list = new Microsoft.UI.Xaml.Controls.RadioButtons();
            foreach (DocumentFileFormat f in formats) list.Items.Add(DocumentFormats.DisplayName(f));
            list.SelectedIndex = Math.Max(0, Array.IndexOf(formats, preselected));

            var dialog = new ContentDialog
            {
                Title = Loc.Get("dlg_textformat_title", "Save as type"),
                Content = list,
                PrimaryButtonText = Loc.Get("dlg_ok", "OK"),
                CloseButtonText = Loc.Get("dlg_cancel", "Cancel"),
                DefaultButton = ContentDialogButton.Primary
            };
            if (await dialog.ShowThemedAsync() != ContentDialogResult.Primary) return null;
            return formats[Math.Max(0, list.SelectedIndex)];
        }

        private async void SaveAsFormat_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement item && item.Tag is string type) await SaveFileAsync(true, type);
        }

        private async void SaveAsTXT_Click(object sender, RoutedEventArgs e)
        {
            await SaveFileAsync(true, "TXT");
        }

        private void DisableDocTree()
        {
            DocTreeBorder.Visibility = Visibility.Collapsed;
            Grid.SetColumn(EditorContentHost,0);
            Grid.SetColumn(RulerBorder, 0);
        }

        private void EnableDocTree()
        {
            DocTreeBorder.Visibility = Visibility.Visible;
            Grid.SetColumn(EditorContentHost, 1);
            Grid.SetColumn(RulerBorder, 1);
        }

        private async void NewDoc_Click(object sender, RoutedEventArgs e)
        {
            if (HasUnsavedChanges())
            {
                var proceed = await ShowUnsavedDialog();
                if (proceed != true)
                    return;
            }

            _largeDocument = false;
            BeginProgrammaticChange();
            try
            {
                Editor.Document.SetText(TextSetOptions.None, string.Empty);
                EmbeddedObjectManager.For(Editor).Clear();
                try { Editor.TextDocument.ClearUndoRedoHistory(); } catch { }
            }
            finally
            {
                EndProgrammaticChange();
            }

            _currentFile = null;
            _currentFormat = DocumentFileFormat.Rtf;
            ShowFormatWarning(false);
            _currentFilePath = null;
            fileNameWithPath = "";
            AppTitle.Text = _untitledTitle;
            MarkClean();
            TextRuler.RefreshFromSelection();
        }

        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            DataTransferManager.ShowShareUI();
        }

        private void AlignJustifyButton_Click(object sender, RoutedEventArgs e)
        {
            Editor.AlignSelectedTo(RichEditHelpers.AlignMode.Justify);
            editor_SelectionChanged(sender, e);
        }

        private void DecreaseZoomButton_Click(object sender, RoutedEventArgs e)
        {
            ZoomSlider.Value = Math.Max(ZoomSlider.Value - 0.1, ZoomSlider.Minimum);
        }

        private void IncreaseZoomButton_Click(object sender, RoutedEventArgs e)
        {
            ZoomSlider.Value = Math.Min(ZoomSlider.Value + 0.1, ZoomSlider.Maximum);
        }

        private void AnimateZoomSecond(double fromValue, double toValue)
        {
            if (RichTextScrollView == null) return;
            PrepareEditorMinHeightForZoom(ZoomSlider.Value);
            RichTextScrollView.ChangeView(null, null, (float)ZoomSlider.Value);
            if (PageSettings.Wrap == WrapMode.WrapToWindow) ApplyPageLayout();
        }

        private void MenuCut_Click(object sender, RoutedEventArgs e)
        {
            CopySelection(cut: true);
        }

        private void MenuCopy_Click(object sender, RoutedEventArgs e)
        {
            CopySelection(cut: false);
        }

        private async void MenuPaste_Click(object sender, RoutedEventArgs e)
        {
            await PasteAsync();
        }

        // A one-color bitmap icon takes the text color once, when it is created, and keeps it when the
        // theme changes (it stayed white in the light theme), so it is created again each time the
        // menu has opened with its final theme.
        private void RefreshParagraphMenuIcon()
        {
            try
            {
                // its color is set here too: the menu's hover look was worked out for the theme it
                // first opened with, which made the icon black on the dark menu on hover
                bool dark = (Window.Current?.Content as FrameworkElement)?.ActualTheme == Windows.UI.Xaml.ElementTheme.Dark;
                MenuParagraph.Icon = new Windows.UI.Xaml.Controls.BitmapIcon
                {
                    UriSource = new Uri("ms-appx:///Assets/MenuParagraph.png"),
                    ShowAsMonochrome = true,
                    Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(dark
                        ? Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)
                        : Windows.UI.Color.FromArgb(0xE4, 0x00, 0x00, 0x00))
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine("RefreshParagraphMenuIcon: " + ex.Message);
            }
        }

        private void ContextListStylesGrid_Loaded(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            UltraPad.UltraPadUI.Ribbon.ParagraphToolbar.HighlightCurrentListStyle(sender as Windows.UI.Xaml.Controls.Panel, Editor);
        }

        private void MenuParagraph_Click(object sender, RoutedEventArgs e)
        {
            pararibbontoolbar.ShowParagraphDialog();
        }

        public string GetText(RichEditBox RichEditor)
        {
            RichEditor.Document.GetText(TextGetOptions.FormatRtf, out string Text);
            ITextRange Range = RichEditor.Document.GetRange(0, Text.Length);
            Range.GetText(TextGetOptions.FormatRtf, out string Value);
            return Value;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            baseRulerWidth = TextRuler.ActualWidth;
            MarkClean();
        }

        // the page scroll bars (Paint-like template in MainPage.xaml) sit inside the editor area,
        // which takes the editor color; they follow the app theme instead, like in Paint
        private readonly System.Collections.Generic.List<Windows.UI.Xaml.Controls.Primitives.ScrollBar> _pageScrollBars = new System.Collections.Generic.List<Windows.UI.Xaml.Controls.Primitives.ScrollBar>();

        private void ApplyAppThemeToScrollBars()
        {
            if (_pageScrollBars.Count == 0) CollectOwnScrollBars(RichTextScrollView, _pageScrollBars);
            foreach (Windows.UI.Xaml.Controls.Primitives.ScrollBar bar in _pageScrollBars)
            {
                bar.RequestedTheme = this.ActualTheme;
            }
        }

        // only the page scroll viewer's own bars, not the hidden ones inside the editor
        private static void CollectOwnScrollBars(Windows.UI.Xaml.DependencyObject parent, System.Collections.Generic.List<Windows.UI.Xaml.Controls.Primitives.ScrollBar> result)
        {
            int count = Windows.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                Windows.UI.Xaml.DependencyObject child = Windows.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is Windows.UI.Xaml.Controls.Primitives.ScrollBar bar) { result.Add(bar); continue; }
                if (child is Windows.UI.Xaml.Controls.ScrollViewer) continue;
                CollectOwnScrollBars(child, result);
            }
        }

        private void MenuFlyoutItem_Click_1(object sender, RoutedEventArgs e)
        {
        }

        private void PageSetup_Click(object sender, RoutedEventArgs e)
        {
            openpageprop();
        }

        private async void opennotimplement()
        {
            // Create an instance of the ParagraphDialog
            NoImplement noimple = new NoImplement();

            // Show the dialog and wait for the user's input
            ContentDialogResult result = await noimple.ShowThemedAsync();
        }

        private async void openpageprop()
        {
            Pageprop pageprop = new Pageprop();
            ContentDialogResult result = await pageprop.ShowThemedAsync();

            if (result == ContentDialogResult.Primary)
            {
                pageprop.Save();
                ApplyPageLayout();
            }
        }

        private void PageSetupMenuItem_Click(object sender, RoutedEventArgs e)
        {
            openpageprop();
        }

        private void Editor_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            TextRuler.UpdatePagePosition();
        }

        private void QueueCaretIntoView(bool withoutFocus = false)
        {
            if (withoutFocus) _caretScrollWithoutFocus = true;
            if (_caretScrollQueued || _printPreviewOpen || (_pictureAdorner?.IsDragging ?? false)) return;
            _caretScrollQueued = true;
            _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () =>
            {
                _caretScrollQueued = false;
                bool withoutFocusNow = _caretScrollWithoutFocus;
                _caretScrollWithoutFocus = false;
                try
                {
                    if (!withoutFocusNow && FocusManager.GetFocusedElement() != Editor) return;
                    ITextSelection sel = Editor.Document.Selection;
                    ITextRange caret = Editor.Document.GetRange(sel.EndPosition, sel.EndPosition);
                    caret.GetRect(PointOptions.ClientCoordinates, out Rect r, out _);
                    if (r.Height <= 0) return;
                    Rect v = EditorCoordinates.FromClient(Editor, RichTextScrollView, r);
                    double viewH = RichTextScrollView.ViewportHeight;
                    const double pad = 24;
                    double offset = RichTextScrollView.VerticalOffset;
                    if (v.Bottom > viewH) offset += v.Bottom - viewH + pad;
                    else if (v.Top < 0) offset += v.Top - pad;
                    else return;
                    RichTextScrollView.ChangeView(null, Math.Max(0, offset), null, true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("QueueCaretIntoView: " + ex.Message);
                }
            });
        }

        private void SelectWord(RichEditBox box, bool forward)
        {
            var selection = box.Document.Selection;

            if (forward)
            {
                selection.MoveEnd(TextRangeUnit.Word, 1);
            }
            else
            {
                selection.MoveStart(TextRangeUnit.Word, -1);
            }
        }

        private async void PrintPreviewPrintButton_Click(object sender, RoutedEventArgs e)
        {
            await PrintDocumentAsync();
        }

        private async void Button_Click_2(object sender, RoutedEventArgs e)
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/Lixkote/UltraPad"));
        }

        private void ShareButton_Click(object sender, RoutedEventArgs e)
        {
            DataTransferManager.ShowShareUI();
        }
        async void DataTransferManager_DataRequested(DataTransferManager sender, DataRequestedEventArgs args)
        {
            DataRequest request = args.Request;
            var deferral = request.GetDeferral();
            try
            {
                request.Data.Properties.Title = AppTitle.Text.Replace(" - " + appTitleStr, "");

                DocumentFileFormat format = _currentFormat;
                string ext = DocumentFormats.Extension(format);
                string baseName = _currentFile != null
                    ? System.IO.Path.GetFileNameWithoutExtension(_currentFile.Name)
                    : L("newdocumentname", "Document");
                string rtf = format == DocumentFileFormat.Rtf || !DocumentFormats.IsText(format) ? GetRtfForExport() : null;
                Editor.Document.GetText(TextGetOptions.None, out string text);
                byte[] bytes = DocumentFormats.Serialize(format, rtf, text);

                var storageFolder = Windows.Storage.ApplicationData.Current.TemporaryFolder;
                var rtfFile = await storageFolder.CreateFileAsync(baseName + ext, Windows.Storage.CreationCollisionOption.ReplaceExisting);
                await Windows.Storage.FileIO.WriteBytesAsync(rtfFile, bytes);

                // Attach the file to the DataRequest.
                request.Data.SetStorageItems(new List<Windows.Storage.IStorageItem> { rtfFile });
            }
            finally
            {
                deferral.Complete();
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            this.Frame.Navigate(typeof(SettingsPage));
        }

        private async void QuickPrint_Click(object sender, RoutedEventArgs e)
        {
            await PrintDocumentAsync();
        }

        private void CloseMenuFlyoutItem_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Exit();
        }

        private void Editor_Loaded(object sender, RoutedEventArgs e)
        {
            if (_selectionBar == null) SetupSelectionBar();
            var format = Editor.Document.GetDefaultParagraphFormat();
            format.ListStart = 1;
            Editor.Document.SetDefaultParagraphFormat(format);
            HookEditorScrollViewer();
            TextRuler.UpdatePagePosition(true);
        }

        private int FindWordStart(RichEditBox richEditBox, int position)
        {
            string text;
            richEditBox.Document.GetText(Windows.UI.Text.TextGetOptions.None, out text);
            int start = position;
            while (start > 0 && !char.IsWhiteSpace(text[start - 1]) && !IsPunctuation(text[start - 1]))
            {
                start--;
            }
            return start;
        }

        private int FindWordEnd(RichEditBox richEditBox, int position)
        {
            string text;
            richEditBox.Document.GetText(Windows.UI.Text.TextGetOptions.None, out text);
            int end = position;
            while (end < text.Length && !char.IsWhiteSpace(text[end]) && !IsPunctuation(text[end]))
            {
                end++;
            }
            return end;
        }

        private void ShadowRect_Loaded(object sender, RoutedEventArgs e)
        {
            shadow.Receivers.Add(DocTree);
        }

        private void ShadowRectR_Loaded(object sender, RoutedEventArgs e)
        {
            shadow.Receivers.Add(TextRuler);
        }

        private bool IsPunctuation(char c)
        {
            return char.IsPunctuation(c);
        }

        private void Editor_KeyDown_1(object sender, KeyRoutedEventArgs e)
        {
            RichEditBox richEditBox = sender as RichEditBox;
            bool ctrl = Window.Current.CoreWindow.GetKeyState(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
            bool shift = Window.Current.CoreWindow.GetKeyState(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);

            if (e.Key == VirtualKey.Tab)
            {
                if (richEditBox != null)
                {
                    richEditBox.Document.Selection.TypeText("\t");
                    e.Handled = true;
                }
            }
            else if (e.Key == VirtualKey.A && (Window.Current.CoreWindow.GetKeyState(VirtualKey.Control) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down)
            {
                if (richEditBox != null)
                {
                    if (Editor != null)
                    {
                        string text;
                        Editor.Document.GetText(TextGetOptions.None, out text);
                        Editor.Focus(FocusState.Programmatic);
                        int lastPosition = Editor.Document.Selection.EndPosition;

                        Editor.Document.Selection.SetRange(0, text.Length);
                    }
                    e.Handled = true;
                }
            }
            else if (e.Key == VirtualKey.Back && (Window.Current.CoreWindow.GetKeyState(VirtualKey.Control) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down)
            {
                var selection = Editor.Document.Selection;
                var startPos = selection.StartPosition;
                int wordStart = FindWordStart(Editor, startPos);
                selection.SetRange(wordStart, startPos);
                selection.Delete(Windows.UI.Text.TextRangeUnit.Character, 1);
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.Delete && (Window.Current.CoreWindow.GetKeyState(VirtualKey.Control) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down)
            {
                var selection = Editor.Document.Selection;
                var endPos = selection.EndPosition;
                int wordEnd = FindWordEnd(Editor, endPos);
                selection.SetRange(endPos, wordEnd);
                selection.Delete(Windows.UI.Text.TextRangeUnit.Character, 1);

                e.Handled = true;
            }

            if (ctrl && shift)
            {
                if (e.Key == VirtualKey.Left)
                {
                    SelectWord(richEditBox, false);
                    e.Handled = true;
                }
                else if (e.Key == VirtualKey.Right)
                {
                    SelectWord(richEditBox, true);
                    e.Handled = true;
                }
            }
        }

        private void DocsTreeToggle_Click(object sender, RoutedEventArgs e)
        {
            if (DocsTreeToggle.IsChecked)
            {
                EnableDocTree();
            }
            else
            {
                DisableDocTree();
            }
        }
    }
}
