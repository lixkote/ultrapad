using System;
using System.Collections.Generic;
using System.Linq;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using WordPad.Helpers;

namespace WordPad.WordPadUI
{
    public sealed partial class TabsDialog : ContentDialog
    {
        private const double MaxTabTwips = 22 * PageSettings.TwipsPerInch;
        private const int MaxTabs = 32;

        private readonly RichEditBox _editor;
        private readonly MeasureUnit _unit;
        private readonly List<double> _tabs = new List<double>();
        private bool _changed;

        public TabsDialog() : this(null) { }

        public TabsDialog(RichEditBox editor)
        {
            InitializeComponent();
            _editor = editor;
            _unit = PageSettings.Unit;

            if (_editor != null)
            {
                List<float> tabs = ParagraphHelper.GetTabs(_editor);
                if (tabs != null) _tabs.AddRange(tabs.Select(pt => PageSettings.PointsToTwips(pt)));
            }
            RefreshList();
            UpdateButtons();
        }

        private void RefreshList()
        {
            _tabs.Sort();
            TabsPostitionsListView.Items.Clear();
            foreach (double tw in _tabs)
                TabsPostitionsListView.Items.Add(PageSettings.FormatTwips(tw, _unit));
        }

        private bool TryReadEntry(out double twips)
        {
            return PageSettings.TryParseToTwips(EnteringBox.Text, _unit, out twips) && twips >= 0 && twips <= MaxTabTwips;
        }

        private void UpdateButtons()
        {
            bool hasText = !string.IsNullOrWhiteSpace(EnteringBox.Text);
            bool valid = TryReadEntry(out _);

            if (!hasText || valid)
                EnteringBox.ClearValue(Control.BorderBrushProperty);
            else
                EnteringBox.BorderBrush = new SolidColorBrush(Windows.UI.Colors.Red);

            SetButton.IsEnabled = valid && _tabs.Count < MaxTabs;
            ClearButton.IsEnabled = TabsPostitionsListView.SelectedIndex >= 0 || (valid && FindTab(EnteringBox.Text) >= 0);
            ClearAllButton.IsEnabled = _tabs.Count > 0;
        }

        private int FindTab(string text)
        {
            if (!PageSettings.TryParseToTwips(text, _unit, out double twips)) return -1;
            return _tabs.FindIndex(t => Math.Abs(t - twips) < 1);
        }

        private void EnteringBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateButtons();

        private void EnteringBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter && SetButton.IsEnabled)
            {
                SetButton_Click(sender, null);
                e.Handled = true;
            }
        }

        private void TabsPostitionsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int i = TabsPostitionsListView.SelectedIndex;
            if (i >= 0 && i < _tabs.Count) EnteringBox.Text = PageSettings.FormatTwips(_tabs[i], _unit);
            UpdateButtons();
        }

        private void SetButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryReadEntry(out double twips)) return;
            if (!_tabs.Any(t => Math.Abs(t - twips) < 1))
            {
                _tabs.Add(twips);
                _changed = true;
            }
            RefreshList();
            EnteringBox.Text = string.Empty;
            EnteringBox.Focus(FocusState.Programmatic);
            UpdateButtons();
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            int index = TabsPostitionsListView.SelectedIndex;
            if (index < 0) index = FindTab(EnteringBox.Text);
            if (index >= 0 && index < _tabs.Count)
            {
                _tabs.RemoveAt(index);
                _changed = true;
            }
            RefreshList();
            EnteringBox.Text = string.Empty;
            UpdateButtons();
        }

        private void ClearAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (_tabs.Count > 0) _changed = true;
            _tabs.Clear();
            RefreshList();
            EnteringBox.Text = string.Empty;
            UpdateButtons();
        }

        private void TabsDialog1_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            if (TryReadEntry(out double twips) && !_tabs.Any(t => Math.Abs(t - twips) < 1) && _tabs.Count < MaxTabs)
            {
                _tabs.Add(twips);
                _changed = true;
            }
        }

        public void Apply()
        {
            if (_editor == null || !_changed) return;
            ParagraphHelper.SetTabs(_editor, _tabs.Select(tw => (float)PageSettings.TwipsToPoints(tw)));
        }
    }
}
