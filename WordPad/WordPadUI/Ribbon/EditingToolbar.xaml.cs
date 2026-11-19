using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.System;
using WordPad.Helpers;

// Szablon elementu Kontrolka użytkownika jest udokumentowany na stronie https://go.microsoft.com/fwlink/?LinkId=234236

namespace WordPad.WordPadUI.Ribbon
{
    public sealed partial class EditingToolbar : UserControl
    {
        private RichEditBox _editor;

        public RichEditBox Editor
        {
            get => _editor;
            set
            {
                if (_editor != null) _editor.TextChanged -= Editor_TextChanged;
                _editor = value;
                if (_editor != null) _editor.TextChanged += Editor_TextChanged;
                UpdateEnabledState();
            }
        }

        public EditingToolbar()
        {
            this.InitializeComponent();
            Loaded += (s, e) => UpdateEnabledState();
        }

        private void Editor_TextChanged(object sender, RoutedEventArgs e) => UpdateEnabledState();

        public void UpdateEnabledState()
        {
            bool hasContent = false;
            try
            {
                if (_editor != null)
                {
                    int end = _editor.Document.GetRange(0, TextConstants.MaxUnitCount).EndPosition;
                    if (end > 1) hasContent = true;
                    else if (end == 1)
                    {
                        _editor.Document.GetRange(0, 1).GetText(TextGetOptions.None, out string first);
                        hasContent = first != "\r" && first.Length > 0;
                    }
                }
            }
            catch { hasContent = true; }

            SetEnabled(FindButton, FindIcon, hasContent);
            SetEnabled(ReplaceButton, ReplaceIcon, hasContent);
            SetEnabled(SelectAllButton, SelectAllIcon, hasContent);
        }

        private static void SetEnabled(Control button, UIElement icon, bool enabled)
        {
            if (button.IsEnabled == enabled) return;
            button.IsEnabled = enabled;
            if (icon is Image) icon.Opacity = enabled ? 1.0 : 0.36;
        }

        #region Find and replace

        private static string s_findText = "";
        private static string s_replaceText = "";
        private static bool s_wholeWord;
        private static bool s_matchCase;

        public Action RevealSelection { get; set; }

        public static bool HasSearchText => !string.IsNullOrEmpty(s_findText);

        public void ShowFind(FrameworkElement fallback) => ShowSearchFlyout(FindFlyout, FindButton, fallback);

        public void ShowReplace(FrameworkElement fallback) => ShowSearchFlyout(ReplaceFlyout, ReplaceButton, fallback);

        public bool IsShownOnScreen => IsInVisibleTree(FindButton);

        // Find and Replace are greyed out for an empty document
        public bool CanSearch
        {
            get
            {
                UpdateEnabledState();
                return FindButton.IsEnabled;
            }
        }

        private void ShowSearchFlyout(Flyout flyout, FrameworkElement button, FrameworkElement fallback)
        {
            if (_editor == null) return;
            try
            {
                if (IsInVisibleTree(button))
                {
                    flyout.ShowAt(button);
                }
                else if (fallback != null)
                {
                    flyout.ShowAt(fallback, new FlyoutShowOptions
                    {
                        Placement = FlyoutPlacementMode.BottomEdgeAlignedRight,
                        Position = new Point(Math.Max(0, fallback.ActualWidth - 16), 8)
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ShowSearchFlyout: " + ex.Message);
            }
        }

        private static bool IsInVisibleTree(UIElement element)
        {
            DependencyObject current = element;
            while (current != null)
            {
                if (current is UIElement ui && (ui.Visibility != Visibility.Visible || ui.Opacity <= 0)) return false;
                if (current == Window.Current?.Content) return true;
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        // selected text becomes the search text, like in WordPad
        private string InitialSearchText()
        {
            try
            {
                ITextSelection sel = _editor?.Document.Selection;
                if (sel != null && sel.Length > 0 && sel.Length < 256)
                {
                    sel.GetText(TextGetOptions.None, out string text);
                    if (!string.IsNullOrEmpty(text) && text.IndexOfAny(new[] { '\r', '\n', '\v', '\uFFFC' }) < 0) return text;
                }
            }
            catch { }
            return s_findText;
        }

        private void FindFlyout_Opening(object sender, object e)
        {
            findBox.Text = InitialSearchText();
            FindWholeWord.IsChecked = s_wholeWord;
            FindMatchCase.IsChecked = s_matchCase;
            SetStatus(FindStatus, null);
        }

        private void FindFlyout_Opened(object sender, object e)
        {
            findBox.Focus(FocusState.Programmatic);
            findBox.SelectAll();
        }

        private void ReplaceFlyout_Opening(object sender, object e)
        {
            replaceFindBox.Text = InitialSearchText();
            replaceBox.Text = s_replaceText;
            ReplaceWholeWord.IsChecked = s_wholeWord;
            ReplaceMatchCase.IsChecked = s_matchCase;
            SetStatus(ReplaceStatus, null);
        }

        private void ReplaceFlyout_Opened(object sender, object e)
        {
            replaceFindBox.Focus(FocusState.Programmatic);
            replaceFindBox.SelectAll();
        }

        private void ReadFindFlyout()
        {
            s_findText = findBox.Text;
            s_wholeWord = FindWholeWord.IsChecked == true;
            s_matchCase = FindMatchCase.IsChecked == true;
        }

        private void ReadReplaceFlyout()
        {
            s_findText = replaceFindBox.Text;
            s_replaceText = replaceBox.Text;
            s_wholeWord = ReplaceWholeWord.IsChecked == true;
            s_matchCase = ReplaceMatchCase.IsChecked == true;
        }

        private static void SetStatus(TextBlock status, string text)
        {
            status.Text = text ?? "";
            status.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        private static FindOptions CurrentOptions()
        {
            FindOptions options = FindOptions.None;
            if (s_wholeWord) options |= FindOptions.Word;
            if (s_matchCase) options |= FindOptions.Case;
            return options;
        }

        public enum SearchResult
        {
            Found,
            FoundAfterWrap,
            NotFound,
        }

        // searches from the selection and continues from the other end of the document when needed
        public static SearchResult FindNext(RichEditBox editor, bool backward = false)
        {
            if (editor == null || string.IsNullOrEmpty(s_findText)) return SearchResult.NotFound;

            ITextDocument doc = editor.Document;
            ITextSelection sel = doc.Selection;
            FindOptions options = CurrentOptions();
            int count = backward ? -TextConstants.MaxUnitCount : TextConstants.MaxUnitCount;
            int from = backward ? sel.StartPosition : sel.EndPosition;

            ITextRange range = doc.GetRange(from, from);
            SearchResult result = SearchResult.Found;
            if (range.FindText(s_findText, count, options) <= 0)
            {
                int end = doc.GetRange(0, TextConstants.MaxUnitCount).EndPosition;
                range = backward ? doc.GetRange(end, end) : doc.GetRange(0, 0);
                if (range.FindText(s_findText, count, options) <= 0) return SearchResult.NotFound;
                result = SearchResult.FoundAfterWrap;
            }

            sel.SetRange(range.StartPosition, range.EndPosition);
            return result;
        }

        public static string Describe(SearchResult result)
        {
            switch (result)
            {
                case SearchResult.NotFound:
                    return Loc.Format("find_notfound", "Cannot find \"{0}\".", s_findText);
                case SearchResult.FoundAfterWrap:
                    return Loc.Get("find_wrapped", "Reached the end of the document and continued from the beginning.");
                default:
                    return null;
            }
        }

        private void SearchAndReport(TextBlock status)
        {
            SearchResult result = FindNext(_editor);
            SetStatus(status, Describe(result));
            if (result != SearchResult.NotFound) RevealSelection?.Invoke();
        }

        private void FindNext_Click(object sender, RoutedEventArgs e)
        {
            ReadFindFlyout();
            SearchAndReport(FindStatus);
        }

        private void FindBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != VirtualKey.Enter) return;
            e.Handled = true;
            ReadFindFlyout();
            SearchAndReport(FindStatus);
        }

        private void ReplaceFindNext_Click(object sender, RoutedEventArgs e)
        {
            ReadReplaceFlyout();
            SearchAndReport(ReplaceStatus);
        }

        private void ReplaceFindBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != VirtualKey.Enter) return;
            e.Handled = true;
            ReadReplaceFlyout();
            SearchAndReport(ReplaceStatus);
        }

        private void ReplaceBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != VirtualKey.Enter) return;
            e.Handled = true;
            ReplaceSelected_Click(sender, e);
        }

        private bool SelectionMatches()
        {
            ITextSelection sel = _editor.Document.Selection;
            if (sel.Length == 0) return false;
            sel.GetText(TextGetOptions.None, out string selected);
            if (!string.Equals(selected, s_findText, s_matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)) return false;
            if (!s_wholeWord) return true;

            // the same search from the start of the selection has to land on the selection itself
            ITextRange check = _editor.Document.GetRange(sel.StartPosition, sel.StartPosition);
            return check.FindText(s_findText, sel.Length, CurrentOptions()) > 0 &&
                   check.StartPosition == sel.StartPosition && check.EndPosition == sel.EndPosition;
        }

        private void ReplaceSelected_Click(object sender, RoutedEventArgs e)
        {
            ReadReplaceFlyout();
            if (_editor == null || string.IsNullOrEmpty(s_findText)) return;

            if (SelectionMatches())
            {
                ITextSelection sel = _editor.Document.Selection;
                sel.SetText(TextSetOptions.None, s_replaceText);
                sel.Collapse(false);
            }
            SearchAndReport(ReplaceStatus);
        }

        private void ReplaceAll_Click(object sender, RoutedEventArgs e)
        {
            ReadReplaceFlyout();
            if (_editor == null || string.IsNullOrEmpty(s_findText)) return;

            ITextDocument doc = _editor.Document;
            FindOptions options = CurrentOptions();
            int replaced = 0;
            doc.BeginUndoGroup();
            doc.BatchDisplayUpdates();
            try
            {
                ITextRange range = doc.GetRange(0, 0);
                while (range.FindText(s_findText, TextConstants.MaxUnitCount, options) > 0)
                {
                    range.SetText(TextSetOptions.None, s_replaceText);
                    range.Collapse(false);
                    replaced++;
                }
            }
            finally
            {
                doc.ApplyDisplayUpdates();
                doc.EndUndoGroup();
            }

            SetStatus(ReplaceStatus, replaced == 0
                ? Loc.Format("find_notfound", "Cannot find \"{0}\".", s_findText)
                : Loc.Format("find_replaced", "{0} replacements made.", replaced));
        }

        #endregion

        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (Editor != null)
            {
                string text;
                Editor.Document.GetText(TextGetOptions.None, out text);
                Editor.Focus(FocusState.Programmatic);
                int lastPosition = Editor.Document.Selection.EndPosition;

                Editor.Document.Selection.SetRange(0, text.Length);
            }
        }
    }
}
