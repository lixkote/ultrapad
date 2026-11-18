using System;
using System.Globalization;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using WordPad.Helpers;

namespace WordPad.WordPadUI
{
    public sealed partial class ParagraphDialog : ContentDialog
    {
        private static readonly float[] LineSpacings = { 1.0f, 1.15f, 1.5f, 2.0f };

        private readonly RichEditBox _editor;
        private readonly MeasureUnit _unit;

        private string _leftText, _rightText, _firstText;
        private int _spacingIndex, _alignIndex;
        private bool? _addSpace;

        public bool TabsRequested { get; private set; }

        public ParagraphDialog() : this(null) { }

        public ParagraphDialog(RichEditBox editor)
        {
            InitializeComponent();
            _editor = editor;
            _unit = PageSettings.Unit;

            foreach (float s in LineSpacings)
                LineSpacingCombo.Items.Add(s.ToString("0.00", CultureInfo.CurrentCulture));

            if (_editor != null) Load();
        }

        private void Load()
        {
            ITextParagraphFormat f = _editor.Document.Selection.ParagraphFormat;

            LeftIndentBox.Text = _leftText = FormatPoints(f.LeftIndent);
            RightIndentBox.Text = _rightText = FormatPoints(f.RightIndent);
            OneLineBox.Text = _firstText = FormatPoints(f.FirstLineIndent);

            _spacingIndex = -1;
            float spacing = f.LineSpacing;
            switch (f.LineSpacingRule)
            {
                case LineSpacingRule.Single: spacing = 1.0f; break;
                case LineSpacingRule.OneAndHalf: spacing = 1.5f; break;
                case LineSpacingRule.Double: spacing = 2.0f; break;
                case LineSpacingRule.Multiple: break;
                default: spacing = -1; break;
            }
            for (int i = 0; i < LineSpacings.Length; i++)
                if (Math.Abs(LineSpacings[i] - spacing) < 0.01f) _spacingIndex = i;
            LineSpacingCombo.SelectedIndex = _spacingIndex;

            float after = f.SpaceAfter;
            _addSpace = ParagraphHelper.IsKnown(after) ? (bool?)(after >= 9.5f) : null;
            AddSpaceCheckBox.IsThreeState = _addSpace == null;
            AddSpaceCheckBox.IsChecked = _addSpace;

            switch (f.Alignment)
            {
                case ParagraphAlignment.Left: _alignIndex = 0; break;
                case ParagraphAlignment.Right: _alignIndex = 1; break;
                case ParagraphAlignment.Center: _alignIndex = 2; break;
                case ParagraphAlignment.Justify: _alignIndex = 3; break;
                default: _alignIndex = -1; break;
            }
            AlignCombo.SelectedIndex = _alignIndex;
        }

        private string FormatPoints(float points)
        {
            return ParagraphHelper.IsKnown(points) ? PageSettings.FormatTwips(PageSettings.PointsToTwips(points), _unit) : "";
        }

        private bool Validate()
        {
            bool ok = true;
            foreach (TextBox box in new[] { LeftIndentBox, RightIndentBox, OneLineBox })
            {
                bool valid = string.IsNullOrWhiteSpace(box.Text) ||
                             (PageSettings.TryParseToTwips(box.Text, _unit, out double tw) && Math.Abs(tw) <= 22 * PageSettings.TwipsPerInch);
                if (valid)
                {
                    box.ClearValue(Control.BorderBrushProperty);
                }
                else
                {
                    box.BorderBrush = new SolidColorBrush(Windows.UI.Colors.Red);
                    if (ok) box.Focus(FocusState.Programmatic);
                    ok = false;
                }
            }
            return ok;
        }

        private float ReadBox(TextBox box, string original)
        {
            if (box.Text == original) return ParagraphHelper.Undefined;
            if (string.IsNullOrWhiteSpace(box.Text)) return ParagraphHelper.Undefined;
            PageSettings.TryParseToTwips(box.Text, _unit, out double twips);
            return (float)PageSettings.TwipsToPoints(twips);
        }

        public void Apply()
        {
            if (_editor == null) return;

            float left = ReadBox(LeftIndentBox, _leftText);
            float right = ReadBox(RightIndentBox, _rightText);
            float first = ReadBox(OneLineBox, _firstText);
            int spacingIndex = LineSpacingCombo.SelectedIndex;
            int alignIndex = AlignCombo.SelectedIndex;
            bool? addSpace = AddSpaceCheckBox.IsChecked;

            if (left != ParagraphHelper.Undefined || right != ParagraphHelper.Undefined || first != ParagraphHelper.Undefined)
                ParagraphHelper.SetIndents(_editor, left, right, first);

            ParagraphHelper.ForEachSelectedParagraph(_editor, f =>
            {
                if (spacingIndex >= 0 && spacingIndex != _spacingIndex)
                    f.SetLineSpacing(LineSpacingRule.Multiple, LineSpacings[spacingIndex]);

                if (addSpace.HasValue && addSpace != _addSpace)
                    f.SpaceAfter = addSpace.Value ? 10f : 0f;

                if (alignIndex >= 0 && alignIndex != _alignIndex)
                {
                    switch (alignIndex)
                    {
                        case 0: f.Alignment = ParagraphAlignment.Left; break;
                        case 1: f.Alignment = ParagraphAlignment.Right; break;
                        case 2: f.Alignment = ParagraphAlignment.Center; break;
                        case 3: f.Alignment = ParagraphAlignment.Justify; break;
                    }
                }
            });
        }

        private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            if (!Validate()) { args.Cancel = true; return; }
            TabsRequested = true;
        }

        private void ContentDialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            if (!Validate()) args.Cancel = true;
        }
    }
}
