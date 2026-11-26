using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace UltraPad.Helpers.Formats
{
    public static class RtfWriter
    {
        public static string Write(DocModel doc)
        {
            var fonts = new List<string>();
            var colors = new List<int>();

            int FontIndex(string name)
            {
                if (string.IsNullOrEmpty(name)) name = CharProps.DefaultFont;
                int i = fonts.IndexOf(name);
                if (i < 0) { fonts.Add(name); i = fonts.Count - 1; }
                return i;
            }

            int ColorIndex(int rgb)
            {
                int i = colors.IndexOf(rgb);
                if (i < 0) { colors.Add(rgb); i = colors.Count - 1; }
                return i + 1;
            }

            FontIndex(CharProps.DefaultFont);
            int symbolFont = FontIndex("Symbol");

            var body = new StringBuilder();
            ListNumbering.Info[] numbering = ListNumbering.Compute(doc);

            for (int pi = 0; pi < doc.Paragraphs.Count; pi++)
            {
                Paragraph p = doc.Paragraphs[pi];
                ParaProps pp = p.Props;
                body.Append("\\pard");
                if (pp.PageBreakBefore) body.Append("\\pagebb");

                if (pp.List != ListKind.None)
                {
                    int number = numbering[pi].Number;
                    if (pp.List == ListKind.Bullet)
                    {
                        body.Append("{\\pntext\\f").Append(symbolFont).Append("\\'B7\\tab}{\\*\\pn\\pnlvlblt\\pnf").Append(symbolFont)
                            .Append("\\pnindent0{\\pntxtb\\'B7}}");
                    }
                    else
                    {
                        string kind;
                        switch (pp.List)
                        {
                            case ListKind.LowerLetter: kind = "\\pnlcltr"; break;
                            case ListKind.UpperLetter: kind = "\\pnucltr"; break;
                            case ListKind.LowerRoman: kind = "\\pnlcrm"; break;
                            case ListKind.UpperRoman: kind = "\\pnucrm"; break;
                            default: kind = "\\pndec"; break;
                        }
                        body.Append("{\\pntext\\f0 ").Append(Units.NumberText(pp.List, number)).Append(".\\tab}");
                        body.Append("{\\*\\pn\\pnlvlbody\\pnf0\\pnindent0\\pnstart").Append(numbering[pi].RunStart).Append(kind).Append("{\\pntxta.}}");
                    }
                }

                switch (pp.Align)
                {
                    case TextAlign.Center: body.Append("\\qc"); break;
                    case TextAlign.Right: body.Append("\\qr"); break;
                    case TextAlign.Justify: body.Append("\\qj"); break;
                }
                if (pp.FirstLineTw != 0) body.Append("\\fi").Append(pp.FirstLineTw);
                if (pp.LeftTw != 0) body.Append("\\li").Append(pp.LeftTw);
                if (pp.RightTw != 0) body.Append("\\ri").Append(pp.RightTw);
                if (pp.BeforeTw != 0) body.Append("\\sb").Append(pp.BeforeTw);
                if (pp.AfterTw != 0) body.Append("\\sa").Append(pp.AfterTw);
                if (pp.Rule == LineRule.Auto)
                {
                    if (pp.Line != 240 && pp.Line > 0) body.Append("\\sl").Append(pp.Line).Append("\\slmult1");
                }
                else if (pp.Line > 0)
                {
                    body.Append("\\sl").Append(pp.Rule == LineRule.Exact ? -pp.Line : pp.Line).Append("\\slmult0");
                }
                foreach (TabStop t in pp.Tabs)
                {
                    switch (t.Leader)
                    {
                        case TabLeader.Dots: body.Append("\\tldot"); break;
                        case TabLeader.Hyphens: body.Append("\\tlhyph"); break;
                        case TabLeader.Underline: body.Append("\\tlul"); break;
                    }
                    switch (t.Align)
                    {
                        case TabAlign.Center: body.Append("\\tqc"); break;
                        case TabAlign.Right: body.Append("\\tqr"); break;
                        case TabAlign.Decimal: body.Append("\\tqdec"); break;
                    }
                    body.Append("\\tx").Append(t.PositionTw);
                }

                CharProps current = null;
                if (p.Inlines.Count == 0)
                {
                    AppendChar(body, p.MarkProps, FontIndex, ColorIndex);
                }

                foreach (Inline inline in p.Inlines)
                {
                    if (inline is TextRun tr)
                    {
                        if (!tr.Props.SameAs(current)) { AppendChar(body, tr.Props, FontIndex, ColorIndex); current = tr.Props; }
                        AppendText(body, tr.Text);
                    }
                    else if (inline is TabRun tab)
                    {
                        if (!tab.Props.SameAs(current)) { AppendChar(body, tab.Props, FontIndex, ColorIndex); current = tab.Props; }
                        body.Append("\\tab ");
                    }
                    else if (inline is LineBreakRun)
                    {
                        body.Append("\\line ");
                    }
                    else if (inline is PageBreakRun)
                    {
                        body.Append("\\page ");
                    }
                    else if (inline is ImageRun img)
                    {
                        body.Append("{\\nosupersub\\up0\\dn0");
                        AppendPicture(body, img);
                        body.Append("}");
                    }
                }

                if (pi < doc.Paragraphs.Count - 1) body.Append("\\par").Append("\r\n");
            }

            var rtf = new StringBuilder();
            rtf.Append("{\\rtf1\\ansi\\ansicpg1252\\deff0\\nouicompat\\deftab").Append(doc.DefaultTabTw > 0 ? doc.DefaultTabTw : 720);
            rtf.Append("{\\fonttbl");
            for (int i = 0; i < fonts.Count; i++)
            {
                rtf.Append("{\\f").Append(i).Append(fonts[i] == "Symbol" ? "\\fnil\\fcharset2 " : "\\fnil\\fcharset0 ");
                AppendText(rtf, fonts[i]);
                rtf.Append(";}");
            }
            rtf.Append("}\r\n{\\colortbl ;");
            foreach (int c in colors)
            {
                rtf.Append("\\red").Append((c >> 16) & 0xFF).Append("\\green").Append((c >> 8) & 0xFF).Append("\\blue").Append(c & 0xFF).Append(';');
            }
            rtf.Append("}\r\n\\viewkind4\\uc1 ");
            rtf.Append(body);
            rtf.Append("}\r\n");
            return rtf.ToString();
        }

        private static void AppendChar(StringBuilder sb, CharProps c, Func<string, int> font, Func<int, int> color)
        {
            sb.Append("\\plain\\f").Append(font(c.Font)).Append("\\fs").Append(c.HalfPoints);
            if (c.Bold) sb.Append("\\b");
            if (c.Italic) sb.Append("\\i");
            if (c.Strike) sb.Append("\\strike");
            if (c.Hidden) sb.Append("\\v");
            switch (c.Underline)
            {
                case UnderlineKind.Single: sb.Append("\\ul"); break;
                case UnderlineKind.Double: sb.Append("\\uldb"); break;
                case UnderlineKind.Dotted: sb.Append("\\uld"); break;
                case UnderlineKind.Dash: sb.Append("\\uldash"); break;
                case UnderlineKind.Wave: sb.Append("\\ulwave"); break;
                case UnderlineKind.Thick: sb.Append("\\ulth"); break;
                case UnderlineKind.Words: sb.Append("\\ulw"); break;
            }
            if (c.Position == VerticalPosition.Superscript) sb.Append("\\super");
            else if (c.Position == VerticalPosition.Subscript) sb.Append("\\sub");
            if (c.Color.HasValue) sb.Append("\\cf").Append(color(c.Color.Value));
            if (c.Highlight.HasValue) sb.Append("\\highlight").Append(color(c.Highlight.Value));
            sb.Append(' ');
        }

        public static void AppendText(StringBuilder sb, string text)
        {
            foreach (char ch in text)
            {
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '{': sb.Append("\\{"); break;
                    case '}': sb.Append("\\}"); break;
                    case '\t': sb.Append("\\tab "); break;
                    case '\n': sb.Append("\\line "); break;
                    case '\r': break;
                    case '\u00A0': sb.Append("\\~"); break;
                    default:
                        if (ch < 0x20) break;
                        if (ch < 0x80) sb.Append(ch);
                        else sb.Append("\\u").Append(((int)(short)ch).ToString(CultureInfo.InvariantCulture)).Append('?');
                        break;
                }
            }
        }

        private static void AppendPicture(StringBuilder sb, ImageRun img)
        {
            string blip;
            switch (img.Format)
            {
                case "png": blip = "\\pngblip"; break;
                case "jpeg": blip = "\\jpegblip"; break;
                case "emf": blip = "\\emfblip"; break;
                case "wmf": blip = "\\wmetafile8"; break;
                default: return;
            }

            byte[] data = img.Data;
            if (img.Format == "wmf" && data.Length > 22 && data[0] == 0xD7 && data[1] == 0xCD && data[2] == 0xC6 && data[3] == 0x9A)
            {
                var raw = new byte[data.Length - 22];
                Array.Copy(data, 22, raw, 0, raw.Length);
                data = raw;
            }

            ImageRun.PixelSize(img.Data, out int pw, out int ph);
            int wTw = img.WidthTw > 0 ? img.WidthTw : pw * 15;
            int hTw = img.HeightTw > 0 ? img.HeightTw : ph * 15;
            if (pw <= 0) pw = Math.Max(1, wTw / 15);
            if (ph <= 0) ph = Math.Max(1, hTw / 15);

            sb.Append("{\\pict").Append(blip)
              .Append("\\picw").Append(pw).Append("\\pich").Append(ph)
              .Append("\\picwgoal").Append(Math.Max(1, wTw)).Append("\\pichgoal").Append(Math.Max(1, hTw))
              .Append("\r\n");
            const string hex = "0123456789abcdef";
            for (int i = 0; i < data.Length; i++)
            {
                byte b = data[i];
                sb.Append(hex[b >> 4]).Append(hex[b & 15]);
                if (i % 64 == 63) sb.Append("\r\n");
            }
            sb.Append("}");
        }
    }
}
