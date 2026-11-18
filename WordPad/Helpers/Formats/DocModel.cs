using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace WordPad.Helpers.Formats
{
    public enum TextAlign { Left, Center, Right, Justify }

    public enum ListKind { None, Bullet, Decimal, LowerLetter, UpperLetter, LowerRoman, UpperRoman }

    public enum UnderlineKind { None, Single, Double, Dotted, Dash, Wave, Thick, Words }

    public enum VerticalPosition { Baseline, Superscript, Subscript }

    public enum TabAlign { Left, Center, Right, Decimal }

    public enum TabLeader { None, Dots, Hyphens, Underline }

    public enum LineRule { Auto, AtLeast, Exact }

    public sealed class TabStop
    {
        public int PositionTw;
        public TabAlign Align;
        public TabLeader Leader;

        public TabStop Clone() => (TabStop)MemberwiseClone();
    }

    public sealed class CharProps
    {
        public const string DefaultFont = "Calibri";

        public string Font = DefaultFont;
        public int HalfPoints = 22;
        public bool Bold, Italic, Strike, Hidden;
        public UnderlineKind Underline;
        public VerticalPosition Position;
        public int? Color;
        public int? Highlight;

        public CharProps Clone() => (CharProps)MemberwiseClone();

        public bool SameAs(CharProps o)
        {
            return o != null && Font == o.Font && HalfPoints == o.HalfPoints && Bold == o.Bold && Italic == o.Italic &&
                   Strike == o.Strike && Hidden == o.Hidden && Underline == o.Underline && Position == o.Position &&
                   Color == o.Color && Highlight == o.Highlight;
        }

        public string Key()
        {
            return string.Join("|", Font, HalfPoints, Bold, Italic, Strike, Underline, Position, Color, Highlight);
        }
    }

    public sealed class ParaProps
    {
        public TextAlign Align;
        public int LeftTw, RightTw, FirstLineTw, BeforeTw, AfterTw;
        public int Line = 240;
        public LineRule Rule = LineRule.Auto;
        public List<TabStop> Tabs = new List<TabStop>();
        public ListKind List;
        public int ListLevel;
        public int ListStart = 1;
        public bool PageBreakBefore;

        public ParaProps Clone()
        {
            var p = (ParaProps)MemberwiseClone();
            p.Tabs = new List<TabStop>();
            foreach (TabStop t in Tabs) p.Tabs.Add(t.Clone());
            return p;
        }

        public string Key()
        {
            var sb = new StringBuilder();
            sb.Append(Align).Append('|').Append(LeftTw).Append('|').Append(RightTw).Append('|').Append(FirstLineTw).Append('|')
              .Append(BeforeTw).Append('|').Append(AfterTw).Append('|').Append(Line).Append('|').Append(Rule).Append('|')
              .Append(PageBreakBefore);
            foreach (TabStop t in Tabs) sb.Append('|').Append(t.PositionTw).Append(t.Align).Append(t.Leader);
            return sb.ToString();
        }
    }

    public abstract class Inline { }

    public sealed class TextRun : Inline
    {
        public string Text;
        public CharProps Props;
        public TextRun(string text, CharProps props) { Text = text; Props = props; }
    }

    public sealed class TabRun : Inline
    {
        public CharProps Props;
        public TabRun(CharProps props) { Props = props; }
    }

    public sealed class LineBreakRun : Inline { }

    public sealed class PageBreakRun : Inline { }

    public sealed class ImageRun : Inline
    {
        public byte[] Data;
        public string Format;
        public int WidthTw, HeightTw;

        public string Extension => Format == "jpeg" ? "jpg" : Format;

        public string MimeType
        {
            get
            {
                switch (Format)
                {
                    case "jpeg": return "image/jpeg";
                    case "gif": return "image/gif";
                    case "bmp": return "image/bmp";
                    case "tiff": return "image/tiff";
                    case "emf": return "image/x-emf";
                    case "wmf": return "image/x-wmf";
                    default: return "image/png";
                }
            }
        }

        public static string Sniff(byte[] d)
        {
            if (d == null || d.Length < 8) return null;
            if (d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47) return "png";
            if (d[0] == 0xFF && d[1] == 0xD8) return "jpeg";
            if (d[0] == 0x47 && d[1] == 0x49 && d[2] == 0x46) return "gif";
            if (d[0] == 0x42 && d[1] == 0x4D) return "bmp";
            if ((d[0] == 0x49 && d[1] == 0x49 && d[2] == 0x2A) || (d[0] == 0x4D && d[1] == 0x4D && d[3] == 0x2A)) return "tiff";
            if (d[0] == 0x01 && d[1] == 0 && d[2] == 0 && d[3] == 0 && d.Length > 44 && d[40] == 0x20 && d[41] == 0x45 && d[42] == 0x4D && d[43] == 0x46) return "emf";
            if ((d[0] == 0xD7 && d[1] == 0xCD && d[2] == 0xC6 && d[3] == 0x9A) || (d[0] == 0x01 && d[1] == 0x00 && d[2] == 0x09 && d[3] == 0x00)) return "wmf";
            return null;
        }

        public static void PixelSize(byte[] d, out int w, out int h)
        {
            w = h = 0;
            try
            {
                string f = Sniff(d);
                if (f == "png" && d.Length > 24)
                {
                    w = (d[16] << 24) | (d[17] << 16) | (d[18] << 8) | d[19];
                    h = (d[20] << 24) | (d[21] << 16) | (d[22] << 8) | d[23];
                }
                else if (f == "gif" && d.Length > 10)
                {
                    w = d[6] | (d[7] << 8);
                    h = d[8] | (d[9] << 8);
                }
                else if (f == "bmp" && d.Length > 26)
                {
                    w = BitConverter.ToInt32(d, 18);
                    h = Math.Abs(BitConverter.ToInt32(d, 22));
                }
                else if (f == "jpeg")
                {
                    int i = 2;
                    while (i + 9 < d.Length)
                    {
                        if (d[i] != 0xFF) { i++; continue; }
                        byte marker = d[i + 1];
                        int len = (d[i + 2] << 8) | d[i + 3];
                        if (marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
                        {
                            h = (d[i + 5] << 8) | d[i + 6];
                            w = (d[i + 7] << 8) | d[i + 8];
                            return;
                        }
                        i += 2 + len;
                    }
                }
            }
            catch { w = h = 0; }
        }
    }

    public sealed class Paragraph
    {
        public ParaProps Props = new ParaProps();
        public CharProps MarkProps = new CharProps();
        public List<Inline> Inlines = new List<Inline>();

        public bool IsEmpty => Inlines.Count == 0;
    }

    public sealed class DocModel
    {
        public List<Paragraph> Paragraphs = new List<Paragraph>();

        public int PageWidthTw, PageHeightTw;
        public int MarginLeftTw = -1, MarginRightTw = -1, MarginTopTw = -1, MarginBottomTw = -1;
        public int DefaultTabTw = 720;

        public bool HasPage => PageWidthTw > 0 && PageHeightTw > 0;

        public void Normalize()
        {
            foreach (Paragraph p in Paragraphs)
            {
                var merged = new List<Inline>();
                foreach (Inline i in p.Inlines)
                {
                    if (i is TextRun t && t.Text.Length == 0) continue;
                    if (i is TextRun tr && merged.Count > 0 && merged[merged.Count - 1] is TextRun last && last.Props.SameAs(tr.Props))
                    {
                        merged[merged.Count - 1] = new TextRun(last.Text + tr.Text, last.Props);
                        continue;
                    }
                    merged.Add(i);
                }
                p.Inlines = merged;
            }
        }
    }

    public static class ListNumbering
    {
        public sealed class Info
        {
            public int Number;
            public int RunStart;
            public bool Continues;
            public bool NewRun;
        }

        public static Info[] Compute(DocModel doc)
        {
            var result = new Info[doc.Paragraphs.Count];
            var kinds = new Dictionary<int, ListKind>();
            var numbers = new Dictionary<int, int>();
            int previousLevel = -1;
            ListKind previousKind = ListKind.None;
            int runStart = 1;

            for (int i = 0; i < doc.Paragraphs.Count; i++)
            {
                ParaProps pp = doc.Paragraphs[i].Props;
                var info = new Info();
                result[i] = info;
                if (pp.List == ListKind.None)
                {
                    kinds.Clear();
                    numbers.Clear();
                    previousLevel = -1;
                    previousKind = ListKind.None;
                    continue;
                }

                int level = pp.ListLevel;
                foreach (int deeper in kinds.Keys.Where(k => k > level).ToList()) { kinds.Remove(deeper); numbers.Remove(deeper); }

                if (kinds.TryGetValue(level, out ListKind kind) && kind == pp.List)
                {
                    info.Number = numbers[level] + 1;
                    info.Continues = previousLevel != level || previousKind != pp.List;
                }
                else
                {
                    info.Number = Math.Max(1, pp.ListStart);
                }
                kinds[level] = pp.List;
                numbers[level] = info.Number;

                info.NewRun = previousLevel != level || previousKind != pp.List;
                if (info.NewRun) runStart = info.Number;
                info.RunStart = runStart;
                previousLevel = level;
                previousKind = pp.List;
            }
            return result;
        }
    }

    public static class Units
    {
        public const int EmuPerTwip = 635;

        public static bool TryParseLength(string value, out double twips)
        {
            twips = 0;
            if (string.IsNullOrWhiteSpace(value)) return false;
            value = value.Trim();
            int i = 0;
            while (i < value.Length && (char.IsDigit(value[i]) || value[i] == '.' || value[i] == '-' || value[i] == '+' || value[i] == 'e' && i > 0 && i + 1 < value.Length && (char.IsDigit(value[i + 1]) || value[i + 1] == '-'))) i++;
            if (!double.TryParse(value.Substring(0, i), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double n)) return false;
            string unit = value.Substring(i).Trim().ToLowerInvariant();
            switch (unit)
            {
                case "in": twips = n * 1440; return true;
                case "cm": twips = n * 1440 / 2.54; return true;
                case "mm": twips = n * 144 / 2.54; return true;
                case "pt": twips = n * 20; return true;
                case "pc": twips = n * 240; return true;
                case "px": twips = n * 15; return true;
                case "": twips = n; return true;
                default: return false;
            }
        }

        public static string Inches(int twips) =>
            (twips / 1440.0).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + "in";

        public static string Points(double halfPoints) =>
            (halfPoints / 2.0).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "pt";

        public static string Hex(int rgb) => (rgb & 0xFFFFFF).ToString("X6");

        public static bool TryParseHexColor(string value, out int rgb)
        {
            rgb = 0;
            if (string.IsNullOrEmpty(value)) return false;
            value = value.TrimStart('#');
            if (value.Length != 6) return false;
            return int.TryParse(value, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out rgb);
        }

        public static string NumberText(ListKind kind, int n)
        {
            switch (kind)
            {
                case ListKind.Decimal: return n.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case ListKind.LowerLetter: return Letters(n).ToLowerInvariant();
                case ListKind.UpperLetter: return Letters(n);
                case ListKind.LowerRoman: return Roman(n).ToLowerInvariant();
                case ListKind.UpperRoman: return Roman(n);
                default: return "•";
            }
        }

        private static string Letters(int n)
        {
            var sb = new StringBuilder();
            while (n > 0) { n--; sb.Insert(0, (char)('A' + n % 26)); n /= 26; }
            return sb.ToString();
        }

        private static string Roman(int n)
        {
            if (n <= 0 || n > 3999) return n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string[] r = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
            int[] v = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
            var sb = new StringBuilder();
            for (int i = 0; i < v.Length; i++) while (n >= v[i]) { sb.Append(r[i]); n -= v[i]; }
            return sb.ToString();
        }
    }
}
