using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using UltraPad.Helpers;

namespace UltraPad.UltraPadUI
{
    public sealed partial class Pageprop : ContentDialog
    {
        private const double PreviewSize = 170;

        private readonly MeasureUnit _unit;
        private bool _loading = true;

        public Pageprop()
        {
            InitializeComponent();
            _unit = PageSettings.Unit;
            LoadUI();
            _loading = false;
            UpdatePreview();
        }

        private void LoadUI()
        {
            foreach (var paper in PageSettings.PaperSizes)
                PaperTypeCombo.Items.Add(paper.Key);

            string current = PageSettings.PaperName;
            for (int i = 0; i < PageSettings.PaperSizes.Count; i++)
                if (PageSettings.PaperSizes[i].Key == current) PaperTypeCombo.SelectedIndex = i;

            string source = PageSettings.PaperSource;
            foreach (string id in PageSettings.PaperSourceIds)
            {
                PaperSourceCombo.Items.Add(new ComboBoxItem { Content = PaperSourceName(id), Tag = id });
                if (id == source) PaperSourceCombo.SelectedIndex = PaperSourceCombo.Items.Count - 1;
            }
            if (PaperSourceCombo.SelectedIndex < 0) PaperSourceCombo.SelectedIndex = 0;

            if (PageSettings.IsLandscape) orientationlandscape.IsChecked = true;
            else orientationportait.IsChecked = true;

            Thickness m = PageSettings.GetMarginsTwips();
            LeftMarginTextBox.Text = PageSettings.FormatTwips(m.Left, _unit, false);
            RightMarginTextBox.Text = PageSettings.FormatTwips(m.Right, _unit, false);
            TopMarginTextBox.Text = PageSettings.FormatTwips(m.Top, _unit, false);
            BottomMarginTextBox.Text = PageSettings.FormatTwips(m.Bottom, _unit, false);

            printpagenumbers.IsChecked = PageSettings.PrintPageNumbers;

            marginsnameinch.Visibility = _unit == MeasureUnit.Inches ? Visibility.Visible : Visibility.Collapsed;
            marginsnamecm.Visibility = _unit == MeasureUnit.Centimeters ? Visibility.Visible : Visibility.Collapsed;
            marginsnamept.Visibility = _unit == MeasureUnit.Points ? Visibility.Visible : Visibility.Collapsed;
            marginsnamepicas.Visibility = _unit == MeasureUnit.Picas ? Visibility.Visible : Visibility.Collapsed;
        }

        public static string PaperSourceName(string id)
        {
            string fallback;
            switch (id)
            {
                case "manual": fallback = "Manual feed"; break;
                case "tray1": fallback = "Tray 1"; break;
                case "tray2": fallback = "Tray 2"; break;
                case "tray3": fallback = "Tray 3"; break;
                case "envelope": fallback = "Envelope feed"; break;
                default: fallback = "Automatically select"; break;
            }
            try
            {
                string text = Windows.ApplicationModel.Resources.ResourceLoader.GetForCurrentView().GetString("papersource_" + id);
                return string.IsNullOrEmpty(text) ? fallback : text;
            }
            catch
            {
                return fallback;
            }
        }

        private string SelectedPaper =>
            PaperTypeCombo.SelectedItem as string ?? PageSettings.PaperName;

        private bool Landscape => orientationlandscape.IsChecked == true;

        private PageSettings.Size2 SelectedPageSize()
        {
            PageSettings.Size2 s = PageSettings.GetPaperSize(SelectedPaper);
            return Landscape ? new PageSettings.Size2(s.Height, s.Width) : s;
        }

        private bool TryReadMargins(out Thickness margins, bool mark)
        {
            margins = new Thickness();
            PageSettings.Size2 page = SelectedPageSize();
            bool ok = true;

            double l = ReadBox(LeftMarginTextBox, ref ok, mark);
            double r = ReadBox(RightMarginTextBox, ref ok, mark);
            double t = ReadBox(TopMarginTextBox, ref ok, mark);
            double b = ReadBox(BottomMarginTextBox, ref ok, mark);
            if (!ok) return false;

            const double minText = PageSettings.TwipsPerInch;
            if (l + r > page.Width - minText)
            {
                ok = false;
                if (mark) { MarkInvalid(LeftMarginTextBox); MarkInvalid(RightMarginTextBox); }
            }
            if (t + b > page.Height - minText)
            {
                ok = false;
                if (mark) { MarkInvalid(TopMarginTextBox); MarkInvalid(BottomMarginTextBox); }
            }

            margins = new Thickness(l, t, r, b);
            return ok;
        }

        private double ReadBox(TextBox box, ref bool ok, bool mark)
        {
            if (PageSettings.TryParseToTwips(box.Text, _unit, out double twips) && twips >= 0)
            {
                if (mark) box.ClearValue(Control.BorderBrushProperty);
                return twips;
            }
            ok = false;
            if (mark) MarkInvalid(box);
            return 0;
        }

        private static void MarkInvalid(TextBox box)
        {
            box.BorderBrush = new SolidColorBrush(Windows.UI.Colors.Red);
        }

        private void UpdatePreview()
        {
            if (_loading) return;

            PageSettings.Size2 page = SelectedPageSize();
            double scale = (PreviewSize - 16) / Math.Max(page.Width, page.Height);
            double w = page.Width * scale, h = page.Height * scale;
            double x = (PreviewSize - w) / 2 - 2, y = (PreviewSize - h) / 2 - 2;

            Paper.Width = w;
            Paper.Height = h;
            Canvas.SetLeft(Paper, x);
            Canvas.SetTop(Paper, y);
            PaperShadow.Width = w;
            PaperShadow.Height = h;
            Canvas.SetLeft(PaperShadow, x + 5);
            Canvas.SetTop(PaperShadow, y + 5);

            if (!TryReadMargins(out Thickness m, false))
            {
                return;
            }

            double ml = m.Left * scale, mr = m.Right * scale, mt = m.Top * scale, mb = m.Bottom * scale;
            exampletextgrid.Width = Math.Max(1, w - ml - mr);
            exampletextgrid.Height = Math.Max(1, h - mt - mb);
            Canvas.SetLeft(exampletextgrid, x + ml);
            Canvas.SetTop(exampletextgrid, y + mt);
        }

        private void PaperTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdatePreview();

        private void Orientation_Checked(object sender, RoutedEventArgs e) => UpdatePreview();

        private void Margin_TextChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

        private void Pageprop_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            if (!TryReadMargins(out _, true))
            {
                args.Cancel = true;
            }
        }

        public void Save()
        {
            PageSettings.PaperName = SelectedPaper;
            PageSettings.IsLandscape = Landscape;
            if (TryReadMargins(out Thickness m, false)) PageSettings.SetMarginsTwips(m);
            PageSettings.PrintPageNumbers = printpagenumbers.IsChecked == true;
            PageSettings.PaperSource = (PaperSourceCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "auto";
        }
    }
}
