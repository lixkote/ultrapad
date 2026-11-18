using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Windows.Storage;
using Windows.UI.Xaml;

namespace WordPad.Helpers
{
    public enum MeasureUnit { Inches, Centimeters, Points, Picas }

    public enum WrapMode { NoWrap, WrapToWindow, WrapToRuler }

    public struct RulerGrid
    {
        public double LabelStep;
        public double MajorStep;
        public double MinorStep;
        public bool HalfTicks;
        public double UnitTwips;
    }

    public static class PageSettings
    {
        public const double TwipsPerInch = 1440.0;
        public const double TwipsPerCm = 1440.0 / 2.54;
        public const double TwipsPerPoint = 20.0;
        public const double TwipsPerPica = 240.0;
        public const double TwipsPerDip = 15.0;
        public const double DefaultTabTwips = 720;

        public const string KeyUnit = "unit";
        public const string KeyWrap = "textwrapping";
        public const string KeyPaper = "papersize";
        public const string KeyOrientation = "orientation";
        public const string KeyMarginLeft = "pageMarginLeftTwips";
        public const string KeyMarginRight = "pageMarginRightTwips";
        public const string KeyMarginTop = "pageMarginTopTwips";
        public const string KeyMarginBottom = "pageMarginBottomTwips";
        public const string KeyPrintPageNumbers = "isprintpagenumbers";

        public const double DefaultMarginLeftRight = 1800;
        public const double DefaultMarginTopBottom = 1440;

        private static ApplicationDataContainer Settings => ApplicationData.Current.LocalSettings;

        public static readonly IReadOnlyList<KeyValuePair<string, Size2>> PaperSizes = new List<KeyValuePair<string, Size2>>
        {
            new KeyValuePair<string, Size2>("A3", Size2.FromMm(297, 420)),
            new KeyValuePair<string, Size2>("A4", Size2.FromMm(210, 297)),
            new KeyValuePair<string, Size2>("A5", Size2.FromMm(148, 210)),
            new KeyValuePair<string, Size2>("B4 (JIS)", Size2.FromMm(257, 364)),
            new KeyValuePair<string, Size2>("B5 (JIS)", Size2.FromMm(182, 257)),
            new KeyValuePair<string, Size2>("Executive", Size2.FromInches(7.25, 10.5)),
            new KeyValuePair<string, Size2>("Legal", Size2.FromInches(8.5, 14)),
            new KeyValuePair<string, Size2>("Letter", Size2.FromInches(8.5, 11)),
            new KeyValuePair<string, Size2>("Tabloid", Size2.FromInches(11, 17)),
        };

        public struct Size2
        {
            public double Width;
            public double Height;
            public Size2(double w, double h) { Width = w; Height = h; }
            public static Size2 FromMm(double w, double h) => new Size2(w / 25.4 * TwipsPerInch, h / 25.4 * TwipsPerInch);
            public static Size2 FromInches(double w, double h) => new Size2(w * TwipsPerInch, h * TwipsPerInch);
        }

        #region Units

        public static MeasureUnit Unit
        {
            get
            {
                switch ((Settings.Values[KeyUnit] as string ?? "inches").ToLowerInvariant())
                {
                    case "cm":
                    case "centimeters":
                        return MeasureUnit.Centimeters;
                    case "points":
                        return MeasureUnit.Points;
                    case "picas":
                        return MeasureUnit.Picas;
                    default:
                        return MeasureUnit.Inches;
                }
            }
            set
            {
                Settings.Values[KeyUnit] = UnitTag(value);
                UnitChanged?.Invoke(value);
            }
        }

        public static event Action<MeasureUnit> UnitChanged;

        public static string UnitTag(MeasureUnit unit)
        {
            switch (unit)
            {
                case MeasureUnit.Centimeters: return "cm";
                case MeasureUnit.Points: return "points";
                case MeasureUnit.Picas: return "picas";
                default: return "inches";
            }
        }

        public static double TwipsPerUnit(MeasureUnit unit)
        {
            switch (unit)
            {
                case MeasureUnit.Centimeters: return TwipsPerCm;
                case MeasureUnit.Points: return TwipsPerPoint;
                case MeasureUnit.Picas: return TwipsPerPica;
                default: return TwipsPerInch;
            }
        }

        public static string UnitSuffix(MeasureUnit unit)
        {
            switch (unit)
            {
                case MeasureUnit.Centimeters: return " cm";
                case MeasureUnit.Points: return " pt";
                case MeasureUnit.Picas: return " pi";
                default: return "\"";
            }
        }

        public static string FormatTwips(double twips, MeasureUnit unit, bool withSuffix = true)
        {
            double value = twips / TwipsPerUnit(unit);
            string format = unit == MeasureUnit.Points ? "0.#" : "0.##";
            string text = Math.Round(value, 2).ToString(format, CultureInfo.CurrentCulture);
            if (text == "-0") text = "0";
            return withSuffix ? text + UnitSuffix(unit) : text;
        }

        private static readonly Regex LengthRegex = new Regex(
            @"^\s*(?<num>[-+]?(\d+([.,]\d*)?|[.,]\d+))\s*(?<unit>""|''|in|inch|inches|cal|cale|cm|mm|pt|pkt|pi|pc)?\s*\.?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static bool TryParseToTwips(string text, MeasureUnit defaultUnit, out double twips)
        {
            twips = 0;
            if (text == null) return false;
            Match m = LengthRegex.Match(text);
            if (!m.Success) return false;

            string num = m.Groups["num"].Value.Replace(',', '.');
            if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) return false;

            string unit = m.Groups["unit"].Success ? m.Groups["unit"].Value.ToLowerInvariant() : "";
            double factor;
            switch (unit)
            {
                case "":
                    factor = TwipsPerUnit(defaultUnit);
                    break;
                case "\"":
                case "''":
                case "in":
                case "inch":
                case "inches":
                case "cal":
                case "cale":
                    factor = TwipsPerInch;
                    break;
                case "cm":
                    factor = TwipsPerCm;
                    break;
                case "mm":
                    factor = TwipsPerCm / 10.0;
                    break;
                case "pt":
                case "pkt":
                    factor = TwipsPerPoint;
                    break;
                default:
                    factor = TwipsPerPica;
                    break;
            }

            twips = value * factor;
            return !double.IsNaN(twips) && !double.IsInfinity(twips);
        }

        public static RulerGrid GetRulerGrid(MeasureUnit unit, double zoom)
        {
            int level = zoom >= 0.95 ? 0 : zoom >= 0.7 ? 1 : zoom >= 0.45 ? 2 : 3;
            double u = TwipsPerUnit(unit);
            var g = new RulerGrid { UnitTwips = u };
            switch (unit)
            {
                case MeasureUnit.Centimeters:
                    g.LabelStep = level <= 1 ? u : level == 2 ? 2 * u : 4 * u;
                    g.MajorStep = level <= 1 ? u : 2 * u;
                    g.MinorStep = level == 0 ? u / 4 : level == 1 ? u / 2 : u;
                    g.HalfTicks = level == 0;
                    break;
                case MeasureUnit.Points:
                    g.LabelStep = level <= 1 ? 36 * u : 72 * u;
                    g.MajorStep = g.LabelStep;
                    g.MinorStep = level == 0 ? 6 * u : 12 * u;
                    g.HalfTicks = level <= 1;
                    break;
                case MeasureUnit.Picas:
                    g.LabelStep = level <= 2 ? 6 * u : 12 * u;
                    g.MajorStep = g.LabelStep;
                    g.MinorStep = level == 0 ? u : 3 * u;
                    g.HalfTicks = level <= 1;
                    break;
                default:
                    g.LabelStep = level <= 2 ? u : 2 * u;
                    g.MajorStep = g.LabelStep;
                    g.MinorStep = level == 0 ? u / 8 : u / 4;
                    g.HalfTicks = level <= 1;
                    break;
            }
            return g;
        }

        public static double SnapTwips(MeasureUnit unit)
        {
            switch (unit)
            {
                case MeasureUnit.Centimeters: return TwipsPerCm / 4;
                case MeasureUnit.Points: return 100;
                case MeasureUnit.Picas: return 120;
                default: return 90;
            }
        }

        #endregion

        #region Word wrap

        public static WrapMode Wrap
        {
            get
            {
                switch (Settings.Values[KeyWrap] as string)
                {
                    case "nowrap": return WrapMode.NoWrap;
                    case "wrapwindow": return WrapMode.WrapToWindow;
                    default: return WrapMode.WrapToRuler;
                }
            }
            set
            {
                Settings.Values[KeyWrap] = WrapTag(value);
                WrapChanged?.Invoke(value);
            }
        }

        public static event Action<WrapMode> WrapChanged;

        public static string WrapTag(WrapMode mode)
        {
            switch (mode)
            {
                case WrapMode.NoWrap: return "nowrap";
                case WrapMode.WrapToWindow: return "wrapwindow";
                default: return "wrapruler";
            }
        }

        #endregion

        #region Paper and margins

        public static string PaperName
        {
            get
            {
                string name = Settings.Values[KeyPaper] as string;
                foreach (var p in PaperSizes)
                    if (string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)) return p.Key;
                return DefaultPaperName();
            }
            set { Settings.Values[KeyPaper] = value; }
        }

        private static string DefaultPaperName()
        {
            try
            {
                string region = new Windows.Globalization.GeographicRegion().CodeTwoLetter;
                if (region == "US" || region == "CA" || region == "MX" || region == "PH") return "Letter";
            }
            catch { }
            return "A4";
        }

        public static bool IsLandscape
        {
            get { return string.Equals(Settings.Values[KeyOrientation] as string, "Landscape", StringComparison.OrdinalIgnoreCase); }
            set { Settings.Values[KeyOrientation] = value ? "Landscape" : "Portrait"; }
        }

        public const string KeyPaperSource = "papersource";

        public static readonly string[] PaperSourceIds = { "auto", "manual", "tray1", "tray2", "tray3", "envelope" };

        public static string PaperSource
        {
            get
            {
                string v = Settings.Values[KeyPaperSource] as string;
                if (string.IsNullOrEmpty(v) || Array.IndexOf(PaperSourceIds, v.ToLowerInvariant()) < 0) return "auto";
                return v.ToLowerInvariant();
            }
            set { Settings.Values[KeyPaperSource] = value; }
        }

        public static Windows.Graphics.Printing.PrintMediaSize PrintMediaSizeFor(string paperName)
        {
            switch (paperName)
            {
                case "A3": return Windows.Graphics.Printing.PrintMediaSize.IsoA3;
                case "A4": return Windows.Graphics.Printing.PrintMediaSize.IsoA4;
                case "A5": return Windows.Graphics.Printing.PrintMediaSize.IsoA5;
                case "B4 (JIS)": return Windows.Graphics.Printing.PrintMediaSize.JisB4;
                case "B5 (JIS)": return Windows.Graphics.Printing.PrintMediaSize.JisB5;
                case "Executive": return Windows.Graphics.Printing.PrintMediaSize.NorthAmericaExecutive;
                case "Legal": return Windows.Graphics.Printing.PrintMediaSize.NorthAmericaLegal;
                case "Letter": return Windows.Graphics.Printing.PrintMediaSize.NorthAmericaLetter;
                case "Tabloid": return Windows.Graphics.Printing.PrintMediaSize.NorthAmericaTabloid;
                default: return Windows.Graphics.Printing.PrintMediaSize.Default;
            }
        }

        public static bool PrintPageNumbers
        {
            get { return (Settings.Values[KeyPrintPageNumbers] as string) != "no"; }
            set { Settings.Values[KeyPrintPageNumbers] = value ? "yes" : "no"; }
        }

        public static Size2 GetPaperSize(string name)
        {
            foreach (var p in PaperSizes)
                if (string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)) return p.Value;
            return Size2.FromMm(210, 297);
        }

        public static Size2 GetPageSizeTwips()
        {
            Size2 s = GetPaperSize(PaperName);
            return IsLandscape ? new Size2(s.Height, s.Width) : s;
        }

        public static Thickness GetMarginsTwips()
        {
            return new Thickness(
                ReadDouble(KeyMarginLeft, DefaultMarginLeftRight),
                ReadDouble(KeyMarginTop, DefaultMarginTopBottom),
                ReadDouble(KeyMarginRight, DefaultMarginLeftRight),
                ReadDouble(KeyMarginBottom, DefaultMarginTopBottom));
        }

        public static void SetMarginsTwips(Thickness margins)
        {
            Settings.Values[KeyMarginLeft] = margins.Left;
            Settings.Values[KeyMarginTop] = margins.Top;
            Settings.Values[KeyMarginRight] = margins.Right;
            Settings.Values[KeyMarginBottom] = margins.Bottom;
        }

        private static double ReadDouble(string key, double fallback)
        {
            object v = Settings.Values[key];
            if (v is double d && d >= 0 && !double.IsNaN(d)) return d;
            return fallback;
        }

        #endregion

        public static double TwipsToDip(double twips) => twips / TwipsPerDip;
        public static double DipToTwips(double dip) => dip * TwipsPerDip;
        public static double PointsToDip(double pt) => pt * 96.0 / 72.0;
        public static double DipToPoints(double dip) => dip * 72.0 / 96.0;
        public static double TwipsToPoints(double twips) => twips / TwipsPerPoint;
        public static double PointsToTwips(double pt) => pt * TwipsPerPoint;
    }
}
