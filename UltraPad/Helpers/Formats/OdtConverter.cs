using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace UltraPad.Helpers.Formats
{
    public static class OdtConverter
    {
        private static readonly XNamespace Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
        private static readonly XNamespace Style = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
        private static readonly XNamespace Text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
        private static readonly XNamespace Table = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
        private static readonly XNamespace Draw = "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0";
        private static readonly XNamespace Fo = "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0";
        private static readonly XNamespace XLink = "http://www.w3.org/1999/xlink";
        private static readonly XNamespace Svg = "urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0";
        private static readonly XNamespace Meta = "urn:oasis:names:tc:opendocument:xmlns:meta:1.0";
        private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
        private static readonly XNamespace Manifest = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";

        #region Reading

        private sealed class StyleDef
        {
            public string Name, Family, Parent, ListStyle;
            public XElement ParagraphProps, TextProps;
        }

        private sealed class Reader
        {
            public ZipArchive Zip;
            public Dictionary<string, StyleDef> Styles = new Dictionary<string, StyleDef>();
            public Dictionary<string, XElement> ListStyles = new Dictionary<string, XElement>();
            public Dictionary<string, string> FontFaces = new Dictionary<string, string>();
            public Dictionary<string, XElement> PageLayouts = new Dictionary<string, XElement>();
            public StyleDef DefaultParagraph, DefaultText;
            public DocModel Doc = new DocModel();
            public Dictionary<string, int> ListCounters = new Dictionary<string, int>();
        }

        public static DocModel Read(Stream stream)
        {
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
            {
                var r = new Reader { Zip = zip };
                XDocument styles = LoadXml(zip, "styles.xml");
                XDocument content = LoadXml(zip, "content.xml") ?? throw new InvalidDataException("No content.xml");

                if (styles != null) ReadStyleSheet(r, styles.Root, automatic: false);
                ReadStyleSheet(r, content.Root, automatic: true);

                if (styles != null)
                {
                    XElement master = styles.Root.Element(Office + "master-styles")?.Element(Style + "master-page");
                    string layoutName = (string)master?.Attribute(Style + "page-layout-name");
                    if (layoutName != null && r.PageLayouts.TryGetValue(layoutName, out XElement layout)) ReadPageLayout(r.Doc, layout);
                }

                XElement text = content.Root.Element(Office + "body")?.Element(Office + "text");
                if (text != null) ReadBlocks(r, text, null, 0);
                if (r.Doc.Paragraphs.Count == 0) r.Doc.Paragraphs.Add(new Paragraph());
                r.Doc.Normalize();
                return r.Doc;
            }
        }

        private static XDocument LoadXml(ZipArchive zip, string path)
        {
            ZipArchiveEntry entry = zip.GetEntry(path);
            if (entry == null) return null;
            using (Stream s = entry.Open()) return XDocument.Load(s);
        }

        private static byte[] LoadBytes(ZipArchive zip, string path)
        {
            if (path == null) return null;
            path = Uri.UnescapeDataString(path.TrimStart('.', '/'));
            ZipArchiveEntry entry = zip.GetEntry(path) ?? zip.Entries.FirstOrDefault(e => string.Equals(e.FullName, path, StringComparison.OrdinalIgnoreCase));
            if (entry == null) return null;
            using (Stream s = entry.Open())
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        private static void ReadStyleSheet(Reader r, XElement root, bool automatic)
        {
            foreach (XElement face in root.Elements(Office + "font-face-decls").Elements(Style + "font-face"))
            {
                string family = ((string)face.Attribute(Svg + "font-family") ?? (string)face.Attribute(Style + "name") ?? "").Trim('\'', '"', ' ');
                r.FontFaces[(string)face.Attribute(Style + "name") ?? family] = family;
            }

            var containers = new List<XElement>();
            XElement named = root.Element(Office + "styles");
            if (named != null) containers.Add(named);
            XElement auto = root.Element(Office + "automatic-styles");
            if (auto != null) containers.Add(auto);

            foreach (XElement container in containers)
            {
                foreach (XElement e in container.Elements())
                {
                    if (e.Name == Style + "default-style")
                    {
                        var def = MakeStyle(e);
                        if (def.Family == "paragraph") r.DefaultParagraph = def;
                        else if (def.Family == "text") r.DefaultText = def;
                    }
                    else if (e.Name == Style + "style")
                    {
                        StyleDef def = MakeStyle(e);
                        r.Styles[def.Family + "/" + def.Name] = def;
                    }
                    else if (e.Name == Text + "list-style")
                    {
                        r.ListStyles[(string)e.Attribute(Style + "name")] = e;
                    }
                    else if (e.Name == Style + "page-layout")
                    {
                        r.PageLayouts[(string)e.Attribute(Style + "name")] = e;
                    }
                }
            }
        }

        private static StyleDef MakeStyle(XElement e)
        {
            return new StyleDef
            {
                Name = (string)e.Attribute(Style + "name"),
                Family = (string)e.Attribute(Style + "family"),
                Parent = (string)e.Attribute(Style + "parent-style-name"),
                ListStyle = (string)e.Attribute(Style + "list-style-name"),
                ParagraphProps = e.Element(Style + "paragraph-properties"),
                TextProps = e.Element(Style + "text-properties")
            };
        }

        private static void ReadPageLayout(DocModel doc, XElement layout)
        {
            XElement p = layout.Element(Style + "page-layout-properties");
            if (p == null) return;
            if (Units.TryParseLength((string)p.Attribute(Fo + "page-width"), out double w) &&
                Units.TryParseLength((string)p.Attribute(Fo + "page-height"), out double h))
            {
                doc.PageWidthTw = (int)Math.Round(w);
                doc.PageHeightTw = (int)Math.Round(h);
            }
            if (Units.TryParseLength((string)p.Attribute(Fo + "margin-left"), out double ml)) doc.MarginLeftTw = (int)Math.Round(ml);
            if (Units.TryParseLength((string)p.Attribute(Fo + "margin-right"), out double mr)) doc.MarginRightTw = (int)Math.Round(mr);
            if (Units.TryParseLength((string)p.Attribute(Fo + "margin-top"), out double mt)) doc.MarginTopTw = (int)Math.Round(mt);
            if (Units.TryParseLength((string)p.Attribute(Fo + "margin-bottom"), out double mb)) doc.MarginBottomTw = (int)Math.Round(mb);
        }

        private static List<StyleDef> Chain(Reader r, string family, string name)
        {
            var chain = new List<StyleDef>();
            var seen = new HashSet<string>();
            while (name != null && seen.Add(name) && r.Styles.TryGetValue(family + "/" + name, out StyleDef s))
            {
                chain.Insert(0, s);
                name = s.Parent;
            }
            return chain;
        }

        private static void ReadBlocks(Reader r, XElement container, string listStyle, int listDepth)
        {
            foreach (XElement e in container.Elements())
            {
                XName n = e.Name;
                if (n == Text + "p" || n == Text + "h")
                {
                    ReadParagraph(r, e, listStyle, listDepth, null);
                }
                else if (n == Text + "list")
                {
                    string style = (string)e.Attribute(Text + "style-name") ?? listStyle;
                    foreach (XElement item in e.Elements())
                    {
                        if (item.Name == Text + "list-item" || item.Name == Text + "list-header")
                        {
                            bool header = item.Name == Text + "list-header";
                            bool first = true;
                            int? startValue = (int?)item.Attribute(Text + "start-value");
                            foreach (XElement child in item.Elements())
                            {
                                if (child.Name == Text + "p" || child.Name == Text + "h")
                                {
                                    ReadParagraph(r, child, header || !first ? null : style, listDepth + 1, startValue, continuation: !first || header);
                                    first = false;
                                }
                                else if (child.Name == Text + "list")
                                {
                                    var wrapper = new XElement("wrap", child);
                                    ReadBlocks(r, wrapper, style, listDepth + 1);
                                    first = false;
                                }
                            }
                        }
                    }
                }
                else if (n == Text + "section" || n == Text + "index-body" || n == Text + "table-of-content" ||
                         n == Text + "alphabetical-index" || n == Text + "illustration-index" || n == Text + "user-index")
                {
                    ReadBlocks(r, n == Text + "section" || n == Text + "index-body" ? e : (e.Element(Text + "index-body") ?? e), listStyle, listDepth);
                }
                else if (n == Table + "table")
                {
                    foreach (XElement row in e.Descendants(Table + "table-row"))
                        foreach (XElement cell in row.Elements(Table + "table-cell"))
                            ReadBlocks(r, cell, listStyle, listDepth);
                }
                else if (n == Draw + "frame")
                {
                    var para = new Paragraph();
                    r.Doc.Paragraphs.Add(para);
                    ReadFrame(r, para, e);
                }
            }
        }

        private static void ReadParagraph(Reader r, XElement p, string listStyle, int listDepth, int? startValue, bool continuation = false)
        {
            string styleName = (string)p.Attribute(Text + "style-name");
            List<StyleDef> chain = Chain(r, "paragraph", styleName);

            var para = new Paragraph();
            ParaProps pp = para.Props;
            pp.AfterTw = 0;
            var c = new CharProps();

            if (r.DefaultParagraph != null)
            {
                ApplyParagraph(pp, r.DefaultParagraph.ParagraphProps);
                ApplyText(r, c, r.DefaultParagraph.TextProps);
            }
            foreach (StyleDef s in chain)
            {
                ApplyParagraph(pp, s.ParagraphProps);
                ApplyText(r, c, s.TextProps);
            }

            string listName = listStyle;
            if (listName == null && !continuation && listDepth == 0)
            {
                foreach (StyleDef s in chain) if (s.ListStyle != null) listName = s.ListStyle;
            }
            if (listName != null && r.ListStyles.TryGetValue(listName, out XElement list))
            {
                int level = Math.Max(1, listDepth);
                XElement levelStyle = list.Elements().FirstOrDefault(x => (int?)x.Attribute(Text + "level") == level)
                                      ?? list.Elements().LastOrDefault();
                if (levelStyle != null)
                {
                    ListKind kind = ListKind.None;
                    int start = 1;
                    if (levelStyle.Name == Text + "list-level-style-bullet") kind = ListKind.Bullet;
                    else if (levelStyle.Name == Text + "list-level-style-number")
                    {
                        switch ((string)levelStyle.Attribute(Style + "num-format"))
                        {
                            case "a": kind = ListKind.LowerLetter; break;
                            case "A": kind = ListKind.UpperLetter; break;
                            case "i": kind = ListKind.LowerRoman; break;
                            case "I": kind = ListKind.UpperRoman; break;
                            case "": case null: kind = ListKind.None; break;
                            default: kind = ListKind.Decimal; break;
                        }
                        start = (int?)levelStyle.Attribute(Text + "start-value") ?? 1;
                    }
                    else if (levelStyle.Name == Text + "list-level-style-image") kind = ListKind.Bullet;

                    if (kind != ListKind.None)
                    {
                        pp.List = kind;
                        pp.ListLevel = level - 1;
                        pp.ListStart = startValue ?? start;

                        XElement props = levelStyle.Element(Style + "list-level-properties");
                        XElement align = props?.Element(Style + "list-level-label-alignment");
                        bool ownIndent = chain.Any(s => s.ParagraphProps?.Attribute(Fo + "margin-left") != null);
                        if (!ownIndent)
                        {
                            if (align != null)
                            {
                                if (Units.TryParseLength((string)align.Attribute(Fo + "margin-left"), out double ml)) pp.LeftTw = (int)Math.Round(ml);
                                if (Units.TryParseLength((string)align.Attribute(Fo + "text-indent"), out double ti)) pp.FirstLineTw = (int)Math.Round(ti);
                            }
                            else if (props != null)
                            {
                                Units.TryParseLength((string)props.Attribute(Text + "space-before"), out double sb);
                                Units.TryParseLength((string)props.Attribute(Text + "min-label-width"), out double mw);
                                pp.LeftTw = (int)Math.Round(sb + mw);
                                pp.FirstLineTw = -(int)Math.Round(mw);
                            }
                        }
                    }
                }
            }

            foreach (TabStop t in pp.Tabs) t.PositionTw += pp.LeftTw;

            para.MarkProps = c.Clone();
            r.Doc.Paragraphs.Add(para);

            var state = new InlineState { AtStart = true };
            ReadInlines(r, para, p, c, state);
        }

        private sealed class InlineState
        {
            public bool AtStart;
            public bool LastWasSpace;
        }

        private static void ApplyParagraph(ParaProps pp, XElement props)
        {
            if (props == null) return;

            switch ((string)props.Attribute(Fo + "text-align"))
            {
                case "center": pp.Align = TextAlign.Center; break;
                case "end": case "right": pp.Align = TextAlign.Right; break;
                case "justify": pp.Align = TextAlign.Justify; break;
                case "start": case "left": pp.Align = TextAlign.Left; break;
            }

            if (Units.TryParseLength((string)props.Attribute(Fo + "margin-left"), out double ml)) pp.LeftTw = (int)Math.Round(ml);
            if (Units.TryParseLength((string)props.Attribute(Fo + "margin-right"), out double mr)) pp.RightTw = (int)Math.Round(mr);
            if (Units.TryParseLength((string)props.Attribute(Fo + "text-indent"), out double ti)) pp.FirstLineTw = (int)Math.Round(ti);
            if (Units.TryParseLength((string)props.Attribute(Fo + "margin-top"), out double mt)) pp.BeforeTw = Math.Max(0, (int)Math.Round(mt));
            if (Units.TryParseLength((string)props.Attribute(Fo + "margin-bottom"), out double mb)) pp.AfterTw = Math.Max(0, (int)Math.Round(mb));

            string lh = (string)props.Attribute(Fo + "line-height");
            if (lh != null)
            {
                if (lh.EndsWith("%") && double.TryParse(lh.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out double pct))
                {
                    pp.Line = (int)Math.Round(240 * pct / 100);
                    pp.Rule = LineRule.Auto;
                }
                else if (lh == "normal") { pp.Line = 240; pp.Rule = LineRule.Auto; }
                else if (Units.TryParseLength(lh, out double exact)) { pp.Line = (int)Math.Round(exact); pp.Rule = LineRule.Exact; }
            }
            if (Units.TryParseLength((string)props.Attribute(Style + "line-height-at-least"), out double least))
            {
                pp.Line = (int)Math.Round(least);
                pp.Rule = LineRule.AtLeast;
            }

            if ((string)props.Attribute(Fo + "break-before") == "page") pp.PageBreakBefore = true;

            XElement tabs = props.Element(Style + "tab-stops");
            if (tabs != null)
            {
                pp.Tabs.Clear();
                foreach (XElement t in tabs.Elements(Style + "tab-stop"))
                {
                    if (!Units.TryParseLength((string)t.Attribute(Style + "position"), out double pos)) continue;
                    var stop = new TabStop { PositionTw = (int)Math.Round(pos) };
                    switch ((string)t.Attribute(Style + "type"))
                    {
                        case "center": stop.Align = TabAlign.Center; break;
                        case "right": stop.Align = TabAlign.Right; break;
                        case "char": stop.Align = TabAlign.Decimal; break;
                    }
                    string leader = (string)t.Attribute(Style + "leader-text");
                    string leaderStyle = (string)t.Attribute(Style + "leader-style");
                    if (leader == "." || leaderStyle == "dotted") stop.Leader = TabLeader.Dots;
                    else if (leader == "-" || leaderStyle == "dash") stop.Leader = TabLeader.Hyphens;
                    else if (leader == "_" || leaderStyle == "solid") stop.Leader = TabLeader.Underline;
                    pp.Tabs.Add(stop);
                }
            }
        }

        private static void ApplyText(Reader r, CharProps c, XElement props)
        {
            if (props == null) return;

            string fontName = (string)props.Attribute(Style + "font-name");
            string family = (string)props.Attribute(Fo + "font-family");
            if (fontName != null) c.Font = r.FontFaces.TryGetValue(fontName, out string f) ? f : fontName;
            else if (family != null) c.Font = family.Trim('\'', '"', ' ');

            string size = (string)props.Attribute(Fo + "font-size");
            if (size != null)
            {
                if (size.EndsWith("%") && double.TryParse(size.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out double pct))
                    c.HalfPoints = Math.Max(1, (int)Math.Round(c.HalfPoints * pct / 100));
                else if (Units.TryParseLength(size, out double tw))
                    c.HalfPoints = Math.Max(1, (int)Math.Round(tw / 10));
            }

            string weight = (string)props.Attribute(Fo + "font-weight");
            if (weight != null) c.Bold = weight == "bold" || (int.TryParse(weight, out int wv) && wv >= 600);
            string italic = (string)props.Attribute(Fo + "font-style");
            if (italic != null) c.Italic = italic == "italic" || italic == "oblique";

            string ul = (string)props.Attribute(Style + "text-underline-style");
            if (ul != null)
            {
                if (ul == "none") c.Underline = UnderlineKind.None;
                else
                {
                    string type = (string)props.Attribute(Style + "text-underline-type");
                    string width = (string)props.Attribute(Style + "text-underline-width");
                    string mode = (string)props.Attribute(Style + "text-underline-mode");
                    if (type == "double") c.Underline = UnderlineKind.Double;
                    else if (ul == "dotted") c.Underline = UnderlineKind.Dotted;
                    else if (ul == "dash" || ul == "long-dash" || ul == "dot-dash" || ul == "dot-dot-dash") c.Underline = UnderlineKind.Dash;
                    else if (ul == "wave") c.Underline = UnderlineKind.Wave;
                    else if (width == "bold" || width == "thick") c.Underline = UnderlineKind.Thick;
                    else if (mode == "skip-white-space") c.Underline = UnderlineKind.Words;
                    else c.Underline = UnderlineKind.Single;
                }
            }

            string strike = (string)props.Attribute(Style + "text-line-through-style");
            if (strike != null) c.Strike = strike != "none";

            string position = (string)props.Attribute(Style + "text-position");
            if (position != null)
            {
                string first = position.Split(' ')[0];
                if (first == "super" || (first.EndsWith("%") && !first.StartsWith("-") && first != "0%")) c.Position = VerticalPosition.Superscript;
                else if (first == "sub" || first.StartsWith("-")) c.Position = VerticalPosition.Subscript;
                else c.Position = VerticalPosition.Baseline;
            }

            string color = (string)props.Attribute(Fo + "color");
            if (color != null && Units.TryParseHexColor(color, out int rgb)) c.Color = rgb;
            if ((string)props.Attribute(Style + "use-window-font-color") == "true") c.Color = null;

            string bg = (string)props.Attribute(Fo + "background-color");
            if (bg != null)
            {
                if (bg == "transparent") c.Highlight = null;
                else if (Units.TryParseHexColor(bg, out int hl)) c.Highlight = hl;
            }

            string display = (string)props.Attribute(Text + "display");
            if (display != null) c.Hidden = display == "none";
        }

        private static void ReadInlines(Reader r, Paragraph para, XElement container, CharProps c, InlineState state)
        {
            foreach (XNode node in container.Nodes())
            {
                if (node is XText t)
                {
                    AddText(para, CollapseSpaces(t.Value, state), c);
                    continue;
                }
                if (!(node is XElement e)) continue;
                XName n = e.Name;

                if (n == Text + "span")
                {
                    CharProps sc = c.Clone();
                    foreach (StyleDef s in Chain(r, "text", (string)e.Attribute(Text + "style-name"))) ApplyText(r, sc, s.TextProps);
                    if (!sc.Hidden) ReadInlines(r, para, e, sc, state);
                }
                else if (n == Text + "s")
                {
                    int count = (int?)e.Attribute(Text + "c") ?? 1;
                    AddText(para, new string(' ', Math.Max(1, count)), c);
                    state.LastWasSpace = true;
                    state.AtStart = false;
                }
                else if (n == Text + "tab")
                {
                    para.Inlines.Add(new TabRun(c.Clone()));
                    state.LastWasSpace = false;
                    state.AtStart = false;
                }
                else if (n == Text + "line-break")
                {
                    para.Inlines.Add(new LineBreakRun());
                    state.LastWasSpace = false;
                    state.AtStart = true;
                }
                else if (n == Draw + "frame")
                {
                    ReadFrame(r, para, e);
                    state.AtStart = false;
                }
                else if (n == Draw + "a")
                {
                    foreach (XElement frame in e.Elements(Draw + "frame")) ReadFrame(r, para, frame);
                }
                else if (n == Text + "note" || n == Office + "annotation" || n == Office + "annotation-end" ||
                         n == Text + "bookmark" || n == Text + "bookmark-start" || n == Text + "bookmark-end" ||
                         n == Text + "reference-mark" || n == Text + "reference-mark-start" || n == Text + "reference-mark-end" ||
                         n == Text + "soft-page-break" || n == Text + "change" || n == Text + "change-start" || n == Text + "change-end" ||
                         n == Text + "toc-mark" || n == Text + "alphabetical-index-mark" || n == Draw + "custom-shape" || n == Draw + "g")
                {
                }
                else
                {
                    ReadInlines(r, para, e, c, state);
                }
            }
        }

        private static string CollapseSpaces(string text, InlineState state)
        {
            var sb = new StringBuilder(text.Length);
            foreach (char ch in text)
            {
                bool space = ch == ' ' || ch == '\t' || ch == '\n' || ch == '\r';
                if (space)
                {
                    if (state.LastWasSpace || state.AtStart) continue;
                    sb.Append(' ');
                    state.LastWasSpace = true;
                }
                else
                {
                    sb.Append(ch);
                    state.LastWasSpace = false;
                    state.AtStart = false;
                }
            }
            return sb.ToString();
        }

        private static void AddText(Paragraph para, string text, CharProps c)
        {
            if (string.IsNullOrEmpty(text)) return;
            para.Inlines.Add(new TextRun(text, c.Clone()));
        }

        private static void ReadFrame(Reader r, Paragraph para, XElement frame)
        {
            XElement image = frame.Element(Draw + "image");
            if (image == null) return;
            byte[] data = LoadBytes(r.Zip, (string)image.Attribute(XLink + "href"));
            if (data == null)
            {
                string b64 = (string)image.Element(Office + "binary-data");
                if (b64 != null) try { data = Convert.FromBase64String(b64); } catch { }
            }
            if (data == null) return;

            string format = ImageRun.Sniff(data);
            if (format == null)
            {
                foreach (XElement alt in frame.Elements(Draw + "image").Skip(1))
                {
                    byte[] d = LoadBytes(r.Zip, (string)alt.Attribute(XLink + "href"));
                    if (d != null && ImageRun.Sniff(d) != null) { data = d; format = ImageRun.Sniff(d); break; }
                }
            }
            if (format == null) return;

            Units.TryParseLength((string)frame.Attribute(Svg + "width"), out double w);
            Units.TryParseLength((string)frame.Attribute(Svg + "height"), out double h);
            para.Inlines.Add(new ImageRun { Data = data, Format = format, WidthTw = (int)Math.Round(w), HeightTw = (int)Math.Round(h) });
        }

        #endregion

        #region Writing

        public static void Write(DocModel doc, Stream stream)
        {
            var paragraphStyles = new Dictionary<string, string>();
            var textStyles = new Dictionary<string, string>();
            var listStyles = new Dictionary<string, string>();
            var fonts = new List<string> { CharProps.DefaultFont };
            var autoStyles = new XElement(Office + "automatic-styles");
            var pictures = new List<KeyValuePair<string, ImageRun>>();

            string ParagraphStyle(ParaProps pp, CharProps mark)
            {
                string key = pp.Key() + "#" + mark.Key();
                if (paragraphStyles.TryGetValue(key, out string name)) return name;
                name = "P" + (paragraphStyles.Count + 1);
                paragraphStyles[key] = name;

                var props = new XElement(Style + "paragraph-properties",
                    new XAttribute(Fo + "margin-left", Units.Inches(pp.LeftTw)),
                    new XAttribute(Fo + "margin-right", Units.Inches(pp.RightTw)),
                    new XAttribute(Fo + "text-indent", Units.Inches(pp.FirstLineTw)),
                    new XAttribute(Fo + "margin-top", Units.Inches(pp.BeforeTw)),
                    new XAttribute(Fo + "margin-bottom", Units.Inches(pp.AfterTw)),
                    new XAttribute(Fo + "text-align", pp.Align == TextAlign.Center ? "center" : pp.Align == TextAlign.Right ? "end" : pp.Align == TextAlign.Justify ? "justify" : "start"),
                    new XAttribute(Style + "auto-text-indent", "false"));
                if (pp.Rule == LineRule.Auto)
                    props.Add(new XAttribute(Fo + "line-height", (pp.Line * 100.0 / 240).ToString("0.##", CultureInfo.InvariantCulture) + "%"));
                else if (pp.Rule == LineRule.Exact)
                    props.Add(new XAttribute(Fo + "line-height", Units.Inches(pp.Line)));
                else
                    props.Add(new XAttribute(Style + "line-height-at-least", Units.Inches(pp.Line)));
                if (pp.PageBreakBefore) props.Add(new XAttribute(Fo + "break-before", "page"));
                if (pp.Tabs.Count > 0)
                {
                    var tabs = new XElement(Style + "tab-stops");
                    foreach (TabStop t in pp.Tabs)
                    {
                        var stop = new XElement(Style + "tab-stop", new XAttribute(Style + "position", Units.Inches(t.PositionTw - pp.LeftTw)));
                        if (t.Align != TabAlign.Left)
                            stop.Add(new XAttribute(Style + "type", t.Align == TabAlign.Center ? "center" : t.Align == TabAlign.Right ? "right" : "char"));
                        if (t.Align == TabAlign.Decimal) stop.Add(new XAttribute(Style + "char", CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator));
                        if (t.Leader != TabLeader.None)
                        {
                            stop.Add(new XAttribute(Style + "leader-style", t.Leader == TabLeader.Dots ? "dotted" : t.Leader == TabLeader.Hyphens ? "dash" : "solid"));
                            stop.Add(new XAttribute(Style + "leader-text", t.Leader == TabLeader.Dots ? "." : t.Leader == TabLeader.Hyphens ? "-" : "_"));
                        }
                        tabs.Add(stop);
                    }
                    props.Add(tabs);
                }

                autoStyles.Add(new XElement(Style + "style",
                    new XAttribute(Style + "name", name), new XAttribute(Style + "family", "paragraph"),
                    new XAttribute(Style + "parent-style-name", "Standard"),
                    props, TextProperties(mark, fonts)));
                return name;
            }

            string TextStyle(CharProps c)
            {
                string key = c.Key();
                if (textStyles.TryGetValue(key, out string name)) return name;
                name = "T" + (textStyles.Count + 1);
                textStyles[key] = name;
                autoStyles.Add(new XElement(Style + "style",
                    new XAttribute(Style + "name", name), new XAttribute(Style + "family", "text"),
                    TextProperties(c, fonts)));
                return name;
            }

            string ListStyle(ParaProps pp)
            {
                string key = pp.List + "/" + pp.ListStart + "/" + pp.LeftTw + "/" + pp.FirstLineTw;
                if (listStyles.TryGetValue(key, out string name)) return name;
                name = "L" + (listStyles.Count + 1);
                listStyles[key] = name;
                var list = new XElement(Text + "list-style", new XAttribute(Style + "name", name));
                for (int level = 1; level <= 10; level++)
                {
                    XElement levelStyle;
                    if (pp.List == ListKind.Bullet)
                    {
                        levelStyle = new XElement(Text + "list-level-style-bullet",
                            new XAttribute(Text + "level", level), new XAttribute(Text + "bullet-char", "•"));
                    }
                    else
                    {
                        string fmt = pp.List == ListKind.LowerLetter ? "a" : pp.List == ListKind.UpperLetter ? "A" :
                                     pp.List == ListKind.LowerRoman ? "i" : pp.List == ListKind.UpperRoman ? "I" : "1";
                        levelStyle = new XElement(Text + "list-level-style-number",
                            new XAttribute(Text + "level", level), new XAttribute(Style + "num-suffix", "."),
                            new XAttribute(Style + "num-format", fmt), new XAttribute(Text + "start-value", Math.Max(1, pp.ListStart)));
                    }
                    int left = pp.LeftTw + (level - 1) * 360;
                    levelStyle.Add(new XElement(Style + "list-level-properties",
                        new XAttribute(Text + "list-level-position-and-space-mode", "label-alignment"),
                        new XElement(Style + "list-level-label-alignment",
                            new XAttribute(Text + "label-followed-by", "listtab"),
                            new XAttribute(Text + "list-tab-stop-position", Units.Inches(left)),
                            new XAttribute(Fo + "text-indent", Units.Inches(pp.FirstLineTw)),
                            new XAttribute(Fo + "margin-left", Units.Inches(left)))));
                    list.Add(levelStyle);
                }
                autoStyles.Add(list);
                return name;
            }

            for (int i = 0; i < doc.Paragraphs.Count - 1; i++)
            {
                if (doc.Paragraphs[i].Inlines.Any(x => x is PageBreakRun))
                {
                    ParaProps next = doc.Paragraphs[i + 1].Props.Clone();
                    next.PageBreakBefore = true;
                    doc.Paragraphs[i + 1].Props = next;
                }
            }

            var officeText = new XElement(Office + "text");
            ListNumbering.Info[] numbering = ListNumbering.Compute(doc);
            var open = new List<Tuple<int, XElement, ListKind>>();
            var lastItems = new Dictionary<XElement, XElement>();

            for (int pi = 0; pi < doc.Paragraphs.Count; pi++)
            {
                Paragraph p = doc.Paragraphs[pi];
                var xp = new XElement(Text + "p", new XAttribute(Text + "style-name", ParagraphStyle(p.Props, p.MarkProps)));

                foreach (Inline inline in p.Inlines)
                {
                    if (inline is TextRun tr)
                    {
                        var span = new XElement(Text + "span", new XAttribute(Text + "style-name", TextStyle(tr.Props)));
                        AppendOdfText(span, tr.Text);
                        xp.Add(span);
                    }
                    else if (inline is TabRun tab) xp.Add(new XElement(Text + "span", new XAttribute(Text + "style-name", TextStyle(tab.Props)), new XElement(Text + "tab")));
                    else if (inline is LineBreakRun) xp.Add(new XElement(Text + "line-break"));
                    else if (inline is PageBreakRun)
                    {
                    }
                    else if (inline is ImageRun img)
                    {
                        string path = "Pictures/image" + (pictures.Count + 1) + "." + img.Extension;
                        pictures.Add(new KeyValuePair<string, ImageRun>(path, img));
                        ImageRun.PixelSize(img.Data, out int pw, out int ph);
                        int w = img.WidthTw > 0 ? img.WidthTw : Math.Max(1, pw) * 15;
                        int h = img.HeightTw > 0 ? img.HeightTw : Math.Max(1, ph) * 15;
                        xp.Add(new XElement(Draw + "frame",
                            new XAttribute(Draw + "style-name", "fr1"), new XAttribute(Draw + "name", "Image" + pictures.Count),
                            new XAttribute(Text + "anchor-type", "as-char"),
                            new XAttribute(Svg + "width", Units.Inches(w)), new XAttribute(Svg + "height", Units.Inches(h)),
                            new XAttribute(Draw + "z-index", pictures.Count - 1),
                            new XElement(Draw + "image",
                                new XAttribute(XLink + "href", path), new XAttribute(XLink + "type", "simple"),
                                new XAttribute(XLink + "show", "embed"), new XAttribute(XLink + "actuate", "onLoad"))));
                    }
                }

                if (p.Props.List != ListKind.None)
                {
                    int level = Math.Max(0, p.Props.ListLevel);
                    ListNumbering.Info info = numbering[pi];
                    while (open.Count > 0 && open[open.Count - 1].Item1 > level) open.RemoveAt(open.Count - 1);
                    if (open.Count > 0 && open[open.Count - 1].Item1 == level && open[open.Count - 1].Item3 != p.Props.List)
                        open.RemoveAt(open.Count - 1);

                    XElement list;
                    if (open.Count > 0 && open[open.Count - 1].Item1 == level)
                    {
                        list = open[open.Count - 1].Item2;
                    }
                    else
                    {
                        list = new XElement(Text + "list", new XAttribute(Text + "style-name", ListStyle(p.Props)));
                        if (info.Continues) list.Add(new XAttribute(Text + "continue-numbering", "true"));
                        if (open.Count > 0 && lastItems.TryGetValue(open[open.Count - 1].Item2, out XElement parentItem)) parentItem.Add(list);
                        else { open.Clear(); officeText.Add(list); }
                        open.Add(Tuple.Create(level, list, p.Props.List));
                    }
                    var item = new XElement(Text + "list-item", xp);
                    if (info.NewRun && !info.Continues && info.Number != Math.Max(1, p.Props.ListStart))
                        item.Add(new XAttribute(Text + "start-value", info.Number));
                    list.Add(item);
                    lastItems[list] = item;
                }
                else
                {
                    open.Clear();
                    officeText.Add(xp);
                }
            }

            autoStyles.Add(new XElement(Style + "style", new XAttribute(Style + "name", "fr1"), new XAttribute(Style + "family", "graphic"),
                new XAttribute(Style + "parent-style-name", "Graphics"),
                new XElement(Style + "graphic-properties",
                    new XAttribute(Style + "vertical-pos", "top"), new XAttribute(Style + "vertical-rel", "baseline"),
                    new XAttribute(Style + "mirror", "none"), new XAttribute(Fo + "clip", "rect(0in, 0in, 0in, 0in)"))));

            var content = new XDocument(new XDeclaration("1.0", "UTF-8", null),
                new XElement(Office + "document-content", Namespaces(), new XAttribute(Office + "version", "1.3"),
                    FontFaces(fonts),
                    autoStyles,
                    new XElement(Office + "body", officeText)));

            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                ZipArchiveEntry mime = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
                using (Stream s = mime.Open())
                {
                    byte[] b = Encoding.ASCII.GetBytes("application/vnd.oasis.opendocument.text");
                    s.Write(b, 0, b.Length);
                }

                Save(zip, "content.xml", content);
                Save(zip, "styles.xml", StylesXml(doc, fonts));
                Save(zip, "meta.xml", new XDocument(new XDeclaration("1.0", "UTF-8", null),
                    new XElement(Office + "document-meta", Namespaces(), new XAttribute(Office + "version", "1.3"),
                        new XElement(Office + "meta",
                            new XElement(Meta + "generator", "UltraPad"),
                            new XElement(Meta + "creation-date", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)),
                            new XElement(Dc + "date", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture))))));

                foreach (KeyValuePair<string, ImageRun> pic in pictures)
                {
                    ZipArchiveEntry entry = zip.CreateEntry(pic.Key, CompressionLevel.NoCompression);
                    using (Stream s = entry.Open()) s.Write(pic.Value.Data, 0, pic.Value.Data.Length);
                }

                var manifest = new XElement(Manifest + "manifest", new XAttribute(XNamespace.Xmlns + "manifest", Manifest), new XAttribute(Manifest + "version", "1.3"),
                    FileEntry("/", "application/vnd.oasis.opendocument.text", true),
                    FileEntry("content.xml", "text/xml"),
                    FileEntry("styles.xml", "text/xml"),
                    FileEntry("meta.xml", "text/xml"));
                foreach (KeyValuePair<string, ImageRun> pic in pictures) manifest.Add(FileEntry(pic.Key, pic.Value.MimeType));
                Save(zip, "META-INF/manifest.xml", new XDocument(new XDeclaration("1.0", "UTF-8", null), manifest));
            }
        }

        private static XElement FileEntry(string path, string type, bool root = false)
        {
            var e = new XElement(Manifest + "file-entry", new XAttribute(Manifest + "full-path", path), new XAttribute(Manifest + "media-type", type));
            if (root) e.Add(new XAttribute(Manifest + "version", "1.3"));
            return e;
        }

        private static object[] Namespaces()
        {
            return new object[]
            {
                new XAttribute(XNamespace.Xmlns + "office", Office), new XAttribute(XNamespace.Xmlns + "style", Style),
                new XAttribute(XNamespace.Xmlns + "text", Text), new XAttribute(XNamespace.Xmlns + "table", Table),
                new XAttribute(XNamespace.Xmlns + "draw", Draw), new XAttribute(XNamespace.Xmlns + "fo", Fo),
                new XAttribute(XNamespace.Xmlns + "xlink", XLink), new XAttribute(XNamespace.Xmlns + "svg", Svg),
                new XAttribute(XNamespace.Xmlns + "meta", Meta), new XAttribute(XNamespace.Xmlns + "dc", Dc)
            };
        }

        private static XElement FontFaces(List<string> fonts)
        {
            var decls = new XElement(Office + "font-face-decls");
            foreach (string f in fonts)
            {
                decls.Add(new XElement(Style + "font-face", new XAttribute(Style + "name", f),
                    new XAttribute(Svg + "font-family", f.Contains(" ") ? "'" + f + "'" : f)));
            }
            return decls;
        }

        private static XElement TextProperties(CharProps c, List<string> fonts)
        {
            string font = string.IsNullOrEmpty(c.Font) ? CharProps.DefaultFont : c.Font;
            if (!fonts.Contains(font)) fonts.Add(font);
            string size = Units.Points(c.HalfPoints);
            var props = new XElement(Style + "text-properties",
                new XAttribute(Style + "font-name", font), new XAttribute(Style + "font-name-asian", font), new XAttribute(Style + "font-name-complex", font),
                new XAttribute(Fo + "font-size", size), new XAttribute(Style + "font-size-asian", size), new XAttribute(Style + "font-size-complex", size),
                new XAttribute(Fo + "font-weight", c.Bold ? "bold" : "normal"), new XAttribute(Style + "font-weight-asian", c.Bold ? "bold" : "normal"),
                new XAttribute(Style + "font-weight-complex", c.Bold ? "bold" : "normal"),
                new XAttribute(Fo + "font-style", c.Italic ? "italic" : "normal"), new XAttribute(Style + "font-style-asian", c.Italic ? "italic" : "normal"),
                new XAttribute(Style + "font-style-complex", c.Italic ? "italic" : "normal"),
                new XAttribute(Style + "text-line-through-style", c.Strike ? "solid" : "none"));
            if (c.Strike) props.Add(new XAttribute(Style + "text-line-through-type", "single"));

            switch (c.Underline)
            {
                case UnderlineKind.None: props.Add(new XAttribute(Style + "text-underline-style", "none")); break;
                case UnderlineKind.Double: props.Add(new XAttribute(Style + "text-underline-style", "solid"), new XAttribute(Style + "text-underline-type", "double")); break;
                case UnderlineKind.Dotted: props.Add(new XAttribute(Style + "text-underline-style", "dotted")); break;
                case UnderlineKind.Dash: props.Add(new XAttribute(Style + "text-underline-style", "dash")); break;
                case UnderlineKind.Wave: props.Add(new XAttribute(Style + "text-underline-style", "wave")); break;
                case UnderlineKind.Thick: props.Add(new XAttribute(Style + "text-underline-style", "solid"), new XAttribute(Style + "text-underline-width", "bold")); break;
                case UnderlineKind.Words: props.Add(new XAttribute(Style + "text-underline-style", "solid"), new XAttribute(Style + "text-underline-mode", "skip-white-space")); break;
                default: props.Add(new XAttribute(Style + "text-underline-style", "solid")); break;
            }
            if (c.Underline != UnderlineKind.None)
            {
                props.Add(new XAttribute(Style + "text-underline-color", "font-color"));
                if (c.Underline != UnderlineKind.Thick) props.Add(new XAttribute(Style + "text-underline-width", "auto"));
            }

            props.Add(new XAttribute(Style + "text-position",
                c.Position == VerticalPosition.Superscript ? "super 58%" : c.Position == VerticalPosition.Subscript ? "sub 58%" : "0% 100%"));
            if (c.Color.HasValue) props.Add(new XAttribute(Fo + "color", "#" + Units.Hex(c.Color.Value).ToLowerInvariant()));
            else props.Add(new XAttribute(Style + "use-window-font-color", "true"));
            props.Add(new XAttribute(Fo + "background-color", c.Highlight.HasValue ? "#" + Units.Hex(c.Highlight.Value).ToLowerInvariant() : "transparent"));
            if (c.Hidden) props.Add(new XAttribute(Text + "display", "none"));
            return props;
        }

        private static void AppendOdfText(XElement target, string text)
        {
            var sb = new StringBuilder();
            int i = 0;
            void Flush() { if (sb.Length > 0) { target.Add(new XText(sb.ToString())); sb.Clear(); } }
            while (i < text.Length)
            {
                char ch = text[i];
                if (ch == ' ')
                {
                    int count = 0;
                    while (i < text.Length && text[i] == ' ') { count++; i++; }
                    bool atStart = sb.Length == 0 && !target.Nodes().Any();
                    if (!atStart) { sb.Append(' '); count--; }
                    if (count > 0)
                    {
                        Flush();
                        var s = new XElement(Text + "s");
                        if (count > 1) s.Add(new XAttribute(Text + "c", count));
                        target.Add(s);
                    }
                    continue;
                }
                if (ch == '\t') { Flush(); target.Add(new XElement(Text + "tab")); }
                else if (ch == '\n' || ch == '\v') { Flush(); target.Add(new XElement(Text + "line-break")); }
                else if (ch == '\r') { }
                else if (ch == '\t' || ch == '\n' || (ch >= 0x20 && ch <= 0xD7FF) || (ch >= 0xE000 && ch <= 0xFFFD) || char.IsSurrogate(ch)) sb.Append(ch);
                i++;
            }
            Flush();
        }

        private static XDocument StylesXml(DocModel doc, List<string> fonts)
        {
            int width = doc.HasPage ? doc.PageWidthTw : 12240, height = doc.HasPage ? doc.PageHeightTw : 15840;
            int ml = doc.MarginLeftTw >= 0 ? doc.MarginLeftTw : 1800, mr = doc.MarginRightTw >= 0 ? doc.MarginRightTw : 1800;
            int mt = doc.MarginTopTw >= 0 ? doc.MarginTopTw : 1440, mb = doc.MarginBottomTw >= 0 ? doc.MarginBottomTw : 1440;

            var defaultText = new XElement(Style + "text-properties",
                new XAttribute(Style + "font-name", CharProps.DefaultFont), new XAttribute(Fo + "font-size", "11pt"),
                new XAttribute(Fo + "language", CultureInfo.CurrentCulture.TwoLetterISOLanguageName),
                new XAttribute(Fo + "country", CountryOf(CultureInfo.CurrentCulture)));

            return new XDocument(new XDeclaration("1.0", "UTF-8", null),
                new XElement(Office + "document-styles", Namespaces(), new XAttribute(Office + "version", "1.3"),
                    FontFaces(fonts),
                    new XElement(Office + "styles",
                        new XElement(Style + "default-style", new XAttribute(Style + "family", "paragraph"),
                            new XElement(Style + "paragraph-properties",
                                new XAttribute(Style + "tab-stop-distance", Units.Inches(doc.DefaultTabTw > 0 ? doc.DefaultTabTw : 720)),
                                new XAttribute(Style + "writing-mode", "page")),
                            defaultText),
                        new XElement(Style + "style", new XAttribute(Style + "name", "Standard"), new XAttribute(Style + "family", "paragraph"),
                            new XAttribute(Style + "class", "text")),
                        new XElement(Style + "style", new XAttribute(Style + "name", "Graphics"), new XAttribute(Style + "family", "graphic"),
                            new XElement(Style + "graphic-properties", new XAttribute(Text + "anchor-type", "as-char"),
                                new XAttribute(Style + "vertical-pos", "top"), new XAttribute(Style + "vertical-rel", "baseline")))),
                    new XElement(Office + "automatic-styles",
                        new XElement(Style + "page-layout", new XAttribute(Style + "name", "pm1"),
                            new XElement(Style + "page-layout-properties",
                                new XAttribute(Fo + "page-width", Units.Inches(width)), new XAttribute(Fo + "page-height", Units.Inches(height)),
                                new XAttribute(Style + "print-orientation", width > height ? "landscape" : "portrait"),
                                new XAttribute(Fo + "margin-top", Units.Inches(mt)), new XAttribute(Fo + "margin-bottom", Units.Inches(mb)),
                                new XAttribute(Fo + "margin-left", Units.Inches(ml)), new XAttribute(Fo + "margin-right", Units.Inches(mr))))),
                    new XElement(Office + "master-styles",
                        new XElement(Style + "master-page", new XAttribute(Style + "name", "Standard"), new XAttribute(Style + "page-layout-name", "pm1")))));
        }

        private static string CountryOf(CultureInfo culture)
        {
            try { return new RegionInfo(culture.Name).TwoLetterISORegionName; } catch { return "US"; }
        }

        private static void Save(ZipArchive zip, string path, XDocument xml)
        {
            ZipArchiveEntry entry = zip.CreateEntry(path, CompressionLevel.Optimal);
            using (Stream s = entry.Open())
            using (var writer = new StreamWriter(s, new UTF8Encoding(false)))
            {
                xml.Save(writer, SaveOptions.DisableFormatting);
            }
        }

        #endregion
    }
}
