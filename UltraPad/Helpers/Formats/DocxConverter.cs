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
    public static class DocxConverter
    {
        private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
        private static readonly XNamespace Pic = "http://schemas.openxmlformats.org/drawingml/2006/picture";
        private static readonly XNamespace WP = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
        private static readonly XNamespace V = "urn:schemas-microsoft-com:vml";
        private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";
        private static readonly XNamespace CT = "http://schemas.openxmlformats.org/package/2006/content-types";
        private static readonly XNamespace MC = "http://schemas.openxmlformats.org/markup-compatibility/2006";

        private static readonly Dictionary<string, int> HighlightColors = new Dictionary<string, int>
        {
            { "yellow", 0xFFFF00 }, { "green", 0x00FF00 }, { "cyan", 0x00FFFF }, { "magenta", 0xFF00FF },
            { "blue", 0x0000FF }, { "red", 0xFF0000 }, { "darkBlue", 0x000080 }, { "darkCyan", 0x008080 },
            { "darkGreen", 0x008000 }, { "darkMagenta", 0x800080 }, { "darkRed", 0x800000 }, { "darkYellow", 0x808000 },
            { "darkGray", 0x808080 }, { "lightGray", 0xC0C0C0 }, { "black", 0x000000 }, { "white", 0xFFFFFF }
        };

        #region Reading

        private sealed class StyleDef
        {
            public string Id, BasedOn, Type;
            public XElement PPr, RPr;
        }

        private sealed class LevelDef
        {
            public ListKind Kind = ListKind.Decimal;
            public int Start = 1;
            public XElement PPr, RPr;
        }

        private sealed class Reader
        {
            public ZipArchive Zip;
            public string DocumentPath = "word/document.xml";
            public Dictionary<string, StyleDef> Styles = new Dictionary<string, StyleDef>();
            public string DefaultParagraphStyle;
            public XElement DefaultPPr, DefaultRPr;
            public Dictionary<string, string> Rels = new Dictionary<string, string>();
            public Dictionary<string, Dictionary<int, LevelDef>> Abstracts = new Dictionary<string, Dictionary<int, LevelDef>>();
            public Dictionary<string, string> NumToAbstract = new Dictionary<string, string>();
            public Dictionary<string, Dictionary<int, int>> NumStartOverrides = new Dictionary<string, Dictionary<int, int>>();
            public string MinorFont = "Calibri", MajorFont = "Calibri Light";
            public DocModel Doc = new DocModel();
            public int FieldDepth;
            public bool InFieldInstruction;
        }

        public static DocModel Read(Stream stream)
        {
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
            {
                var r = new Reader { Zip = zip };

                XDocument rootRels = LoadXml(zip, "_rels/.rels");
                if (rootRels != null)
                {
                    XElement main = rootRels.Root.Elements(Rel + "Relationship")
                        .FirstOrDefault(e => ((string)e.Attribute("Type") ?? "").EndsWith("/officeDocument"));
                    if (main != null) r.DocumentPath = ((string)main.Attribute("Target")).TrimStart('/');
                }

                XDocument document = LoadXml(zip, r.DocumentPath) ?? throw new InvalidDataException("No document.xml");
                string folder = r.DocumentPath.Contains("/") ? r.DocumentPath.Substring(0, r.DocumentPath.LastIndexOf('/') + 1) : "";

                XDocument docRels = LoadXml(zip, folder + "_rels/" + Path.GetFileName(r.DocumentPath) + ".rels");
                if (docRels != null)
                {
                    foreach (XElement e in docRels.Root.Elements(Rel + "Relationship"))
                    {
                        string target = (string)e.Attribute("Target") ?? "";
                        if ((string)e.Attribute("TargetMode") == "External") continue;
                        string full = target.StartsWith("/") ? target.TrimStart('/') : CombinePath(folder, target);
                        r.Rels[(string)e.Attribute("Id")] = full;
                        string type = (string)e.Attribute("Type") ?? "";
                        if (type.EndsWith("/styles")) ReadStyles(r, LoadXml(zip, full));
                        else if (type.EndsWith("/numbering")) ReadNumbering(r, LoadXml(zip, full));
                        else if (type.EndsWith("/theme")) ReadTheme(r, LoadXml(zip, full));
                    }
                }

                XElement body = document.Root.Element(W + "body");
                if (body != null)
                {
                    ReadBlocks(r, body);
                    XElement sect = body.Element(W + "sectPr");
                    if (sect != null) ReadSection(r.Doc, sect);
                }
                if (r.Doc.Paragraphs.Count == 0) r.Doc.Paragraphs.Add(new Paragraph());
                r.Doc.Normalize();
                return r.Doc;
            }
        }

        private static string CombinePath(string folder, string target)
        {
            var parts = new List<string>((folder + target).Split('/'));
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] == "..") { parts.RemoveAt(i); if (i > 0) { parts.RemoveAt(i - 1); i--; } i--; }
                else if (parts[i] == ".") { parts.RemoveAt(i); i--; }
            }
            return string.Join("/", parts);
        }

        private static XDocument LoadXml(ZipArchive zip, string path)
        {
            if (path == null) return null;
            ZipArchiveEntry entry = zip.GetEntry(path) ?? zip.Entries.FirstOrDefault(e => string.Equals(e.FullName, path, StringComparison.OrdinalIgnoreCase));
            if (entry == null) return null;
            using (Stream s = entry.Open()) return XDocument.Load(s);
        }

        private static byte[] LoadBytes(ZipArchive zip, string path)
        {
            ZipArchiveEntry entry = zip.GetEntry(path) ?? zip.Entries.FirstOrDefault(e => string.Equals(e.FullName, path, StringComparison.OrdinalIgnoreCase));
            if (entry == null) return null;
            using (Stream s = entry.Open())
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        private static void ReadTheme(Reader r, XDocument theme)
        {
            if (theme == null) return;
            XElement minor = theme.Descendants(A + "minorFont").FirstOrDefault()?.Element(A + "latin");
            XElement major = theme.Descendants(A + "majorFont").FirstOrDefault()?.Element(A + "latin");
            if (minor != null && !string.IsNullOrEmpty((string)minor.Attribute("typeface"))) r.MinorFont = (string)minor.Attribute("typeface");
            if (major != null && !string.IsNullOrEmpty((string)major.Attribute("typeface"))) r.MajorFont = (string)major.Attribute("typeface");
        }

        private static void ReadStyles(Reader r, XDocument styles)
        {
            if (styles == null) return;
            XElement defaults = styles.Root.Element(W + "docDefaults");
            r.DefaultRPr = defaults?.Element(W + "rPrDefault")?.Element(W + "rPr");
            r.DefaultPPr = defaults?.Element(W + "pPrDefault")?.Element(W + "pPr");
            foreach (XElement s in styles.Root.Elements(W + "style"))
            {
                var def = new StyleDef
                {
                    Id = (string)s.Attribute(W + "styleId"),
                    Type = (string)s.Attribute(W + "type"),
                    BasedOn = (string)s.Element(W + "basedOn")?.Attribute(W + "val"),
                    PPr = s.Element(W + "pPr"),
                    RPr = s.Element(W + "rPr")
                };
                if (def.Id == null) continue;
                r.Styles[def.Id] = def;
                if (def.Type == "paragraph" && IsOn(s.Attribute(W + "default"))) r.DefaultParagraphStyle = def.Id;
            }
        }

        private static void ReadNumbering(Reader r, XDocument numbering)
        {
            if (numbering == null) return;
            foreach (XElement an in numbering.Root.Elements(W + "abstractNum"))
            {
                var levels = new Dictionary<int, LevelDef>();
                foreach (XElement lvl in an.Elements(W + "lvl"))
                {
                    int ilvl = (int?)lvl.Attribute(W + "ilvl") ?? 0;
                    string fmt = (string)lvl.Element(W + "numFmt")?.Attribute(W + "val") ?? "decimal";
                    levels[ilvl] = new LevelDef
                    {
                        Kind = FormatToKind(fmt),
                        Start = (int?)lvl.Element(W + "start")?.Attribute(W + "val") ?? 1,
                        PPr = lvl.Element(W + "pPr"),
                        RPr = lvl.Element(W + "rPr")
                    };
                }
                r.Abstracts[(string)an.Attribute(W + "abstractNumId")] = levels;
            }
            foreach (XElement num in numbering.Root.Elements(W + "num"))
            {
                string id = (string)num.Attribute(W + "numId");
                r.NumToAbstract[id] = (string)num.Element(W + "abstractNumId")?.Attribute(W + "val");
                var starts = new Dictionary<int, int>();
                foreach (XElement o in num.Elements(W + "lvlOverride"))
                {
                    int? start = (int?)o.Element(W + "startOverride")?.Attribute(W + "val");
                    if (start.HasValue) starts[(int?)o.Attribute(W + "ilvl") ?? 0] = start.Value;
                }
                r.NumStartOverrides[id] = starts;
            }
        }

        private static ListKind FormatToKind(string fmt)
        {
            switch (fmt)
            {
                case "bullet": return ListKind.Bullet;
                case "lowerLetter": return ListKind.LowerLetter;
                case "upperLetter": return ListKind.UpperLetter;
                case "lowerRoman": return ListKind.LowerRoman;
                case "upperRoman": return ListKind.UpperRoman;
                case "none": return ListKind.None;
                default: return ListKind.Decimal;
            }
        }

        private static bool IsOn(XAttribute a)
        {
            if (a == null) return false;
            string v = a.Value;
            return v == "1" || v == "true" || v == "on";
        }

        private static bool? Toggle(XElement rPr, string name)
        {
            XElement e = rPr?.Element(W + name);
            if (e == null) return null;
            string v = (string)e.Attribute(W + "val");
            return v == null || !(v == "0" || v == "false" || v == "off");
        }

        private static int? IntAttr(XElement e, string name)
        {
            string v = (string)e?.Attribute(W + name);
            if (v == null) return null;
            if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) return n;
            if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return (int)Math.Round(d);
            return null;
        }

        private static void ReadSection(DocModel doc, XElement sect)
        {
            XElement size = sect.Element(W + "pgSz");
            XElement mar = sect.Element(W + "pgMar");
            if (size != null)
            {
                doc.PageWidthTw = IntAttr(size, "w") ?? 0;
                doc.PageHeightTw = IntAttr(size, "h") ?? 0;
            }
            if (mar != null)
            {
                doc.MarginLeftTw = IntAttr(mar, "left") ?? -1;
                doc.MarginRightTw = IntAttr(mar, "right") ?? -1;
                doc.MarginTopTw = Math.Abs(IntAttr(mar, "top") ?? -1);
                doc.MarginBottomTw = Math.Abs(IntAttr(mar, "bottom") ?? -1);
            }
        }

        private static void ReadBlocks(Reader r, XElement container)
        {
            foreach (XElement e in container.Elements())
            {
                if (e.Name == W + "p") ReadParagraph(r, e);
                else if (e.Name == W + "tbl")
                {
                    foreach (XElement row in e.Elements(W + "tr"))
                        foreach (XElement cell in row.Elements(W + "tc"))
                            ReadBlocks(r, cell);
                }
                else if (e.Name == W + "sdt") ReadBlocks(r, e.Element(W + "sdtContent") ?? new XElement("x"));
                else if (e.Name == W + "customXml" || e.Name == W + "ins" || e.Name == W + "smartTag") ReadBlocks(r, e);
                else if (e.Name == MC + "AlternateContent")
                {
                    XElement choice = e.Element(MC + "Fallback") ?? e.Element(MC + "Choice");
                    if (choice != null) ReadBlocks(r, choice);
                }
            }
        }

        private static IEnumerable<StyleDef> StyleChain(Reader r, string id)
        {
            var chain = new List<StyleDef>();
            var seen = new HashSet<string>();
            while (id != null && seen.Add(id) && r.Styles.TryGetValue(id, out StyleDef s))
            {
                chain.Insert(0, s);
                id = s.BasedOn;
            }
            return chain;
        }

        private static void ReadParagraph(Reader r, XElement p)
        {
            XElement pPr = p.Element(W + "pPr");
            string styleId = (string)pPr?.Element(W + "pStyle")?.Attribute(W + "val") ?? r.DefaultParagraphStyle;
            List<StyleDef> styles = StyleChain(r, styleId).ToList();

            var para = new Paragraph();
            ParaProps pp = para.Props;
            pp.AfterTw = 0;

            XElement numPr = pPr?.Element(W + "numPr");
            if (numPr == null) foreach (StyleDef s in styles) numPr = s.PPr?.Element(W + "numPr") ?? numPr;
            LevelDef level = null;
            string numId = null;
            int ilvl = 0;
            if (numPr != null)
            {
                numId = (string)numPr.Element(W + "numId")?.Attribute(W + "val");
                ilvl = IntAttr(numPr.Element(W + "ilvl"), "val") ?? 0;
                if (numId != null && numId != "0" && r.NumToAbstract.TryGetValue(numId, out string abs) && abs != null &&
                    r.Abstracts.TryGetValue(abs, out Dictionary<int, LevelDef> levels))
                {
                    levels.TryGetValue(ilvl, out level);
                }
            }

            ApplyPPr(pp, r.DefaultPPr);
            foreach (StyleDef s in styles) ApplyPPr(pp, s.PPr);
            if (level != null) ApplyPPr(pp, level.PPr);
            ApplyPPr(pp, pPr);

            if (level != null && level.Kind != ListKind.None)
            {
                pp.List = level.Kind;
                pp.ListLevel = ilvl;
                pp.ListStart = level.Start;
                if (r.NumStartOverrides.TryGetValue(numId, out Dictionary<int, int> o) && o.TryGetValue(ilvl, out int st)) pp.ListStart = st;
            }

            CharProps baseChar = new CharProps();
            ApplyRPr(r, baseChar, r.DefaultRPr);
            foreach (StyleDef s in styles) ApplyRPr(r, baseChar, s.RPr);
            CharProps mark = baseChar.Clone();
            ApplyRPr(r, mark, pPr?.Element(W + "rPr"));
            para.MarkProps = mark;

            r.Doc.Paragraphs.Add(para);
            ReadRuns(r, para, p, baseChar);
        }

        private static void ApplyPPr(ParaProps pp, XElement pPr)
        {
            if (pPr == null) return;

            string jc = (string)pPr.Element(W + "jc")?.Attribute(W + "val");
            switch (jc)
            {
                case "center": pp.Align = TextAlign.Center; break;
                case "right": case "end": pp.Align = TextAlign.Right; break;
                case "both": case "distribute": case "mediumKashida": case "highKashida": case "lowKashida": pp.Align = TextAlign.Justify; break;
                case "left": case "start": pp.Align = TextAlign.Left; break;
            }

            XElement ind = pPr.Element(W + "ind");
            if (ind != null)
            {
                int? left = IntAttr(ind, "left") ?? IntAttr(ind, "start");
                int? right = IntAttr(ind, "right") ?? IntAttr(ind, "end");
                int? first = IntAttr(ind, "firstLine");
                int? hanging = IntAttr(ind, "hanging");
                if (left.HasValue) pp.LeftTw = left.Value;
                if (right.HasValue) pp.RightTw = right.Value;
                if (hanging.HasValue) pp.FirstLineTw = -hanging.Value;
                else if (first.HasValue) pp.FirstLineTw = first.Value;
            }

            XElement spacing = pPr.Element(W + "spacing");
            if (spacing != null)
            {
                int? before = IntAttr(spacing, "before");
                int? after = IntAttr(spacing, "after");
                int? line = IntAttr(spacing, "line");
                string rule = (string)spacing.Attribute(W + "lineRule");
                if (before.HasValue) pp.BeforeTw = Math.Max(0, before.Value);
                if (after.HasValue) pp.AfterTw = Math.Max(0, after.Value);
                if (IsOn(spacing.Attribute(W + "beforeAutospacing"))) pp.BeforeTw = 280;
                if (IsOn(spacing.Attribute(W + "afterAutospacing"))) pp.AfterTw = 280;
                if (line.HasValue && line.Value > 0)
                {
                    pp.Line = line.Value;
                    pp.Rule = rule == "exact" ? LineRule.Exact : rule == "atLeast" ? LineRule.AtLeast : LineRule.Auto;
                }
            }

            XElement tabs = pPr.Element(W + "tabs");
            if (tabs != null)
            {
                foreach (XElement t in tabs.Elements(W + "tab"))
                {
                    string val = (string)t.Attribute(W + "val");
                    int pos = IntAttr(t, "pos") ?? 0;
                    if (val == "clear")
                    {
                        pp.Tabs.RemoveAll(x => x.PositionTw == pos);
                        continue;
                    }
                    if (val == "bar" || val == "num") continue;
                    var stop = new TabStop { PositionTw = pos };
                    switch (val)
                    {
                        case "center": stop.Align = TabAlign.Center; break;
                        case "right": case "end": stop.Align = TabAlign.Right; break;
                        case "decimal": stop.Align = TabAlign.Decimal; break;
                    }
                    switch ((string)t.Attribute(W + "leader"))
                    {
                        case "dot": case "middleDot": stop.Leader = TabLeader.Dots; break;
                        case "hyphen": stop.Leader = TabLeader.Hyphens; break;
                        case "underscore": case "heavy": stop.Leader = TabLeader.Underline; break;
                    }
                    pp.Tabs.RemoveAll(x => x.PositionTw == pos);
                    pp.Tabs.Add(stop);
                }
                pp.Tabs.Sort((a, b) => a.PositionTw.CompareTo(b.PositionTw));
            }

            bool? pageBreak = Toggle(pPr, "pageBreakBefore");
            if (pageBreak.HasValue) pp.PageBreakBefore = pageBreak.Value;
        }

        private static void ApplyRPr(Reader r, CharProps c, XElement rPr)
        {
            if (rPr == null) return;

            XElement fonts = rPr.Element(W + "rFonts");
            if (fonts != null)
            {
                string f = (string)fonts.Attribute(W + "ascii") ?? (string)fonts.Attribute(W + "hAnsi");
                string theme = (string)fonts.Attribute(W + "asciiTheme") ?? (string)fonts.Attribute(W + "hAnsiTheme");
                if (theme != null) f = theme.StartsWith("major") ? r.MajorFont : r.MinorFont;
                if (!string.IsNullOrEmpty(f)) c.Font = f;
            }

            int? sz = IntAttr(rPr.Element(W + "sz"), "val");
            if (sz.HasValue && sz.Value > 0) c.HalfPoints = sz.Value;

            bool? b = Toggle(rPr, "b"); if (b.HasValue) c.Bold = b.Value;
            bool? i = Toggle(rPr, "i"); if (i.HasValue) c.Italic = i.Value;
            bool? s = Toggle(rPr, "strike"); if (s.HasValue) c.Strike = s.Value;
            bool? ds = Toggle(rPr, "dstrike"); if (ds == true) c.Strike = true;
            bool? vanish = Toggle(rPr, "vanish"); if (vanish.HasValue) c.Hidden = vanish.Value;

            XElement u = rPr.Element(W + "u");
            if (u != null)
            {
                switch ((string)u.Attribute(W + "val") ?? "single")
                {
                    case "none": c.Underline = UnderlineKind.None; break;
                    case "double": c.Underline = UnderlineKind.Double; break;
                    case "dotted": case "dottedHeavy": c.Underline = UnderlineKind.Dotted; break;
                    case "dash": case "dashLong": case "dashedHeavy": case "dashLongHeavy": case "dotDash": case "dotDotDash":
                    case "dashDotHeavy": case "dashDotDotHeavy": c.Underline = UnderlineKind.Dash; break;
                    case "wave": case "wavyDouble": case "wavyHeavy": c.Underline = UnderlineKind.Wave; break;
                    case "thick": c.Underline = UnderlineKind.Thick; break;
                    case "words": c.Underline = UnderlineKind.Words; break;
                    default: c.Underline = UnderlineKind.Single; break;
                }
            }

            string va = (string)rPr.Element(W + "vertAlign")?.Attribute(W + "val");
            if (va == "superscript") c.Position = VerticalPosition.Superscript;
            else if (va == "subscript") c.Position = VerticalPosition.Subscript;
            else if (va == "baseline") c.Position = VerticalPosition.Baseline;

            string color = (string)rPr.Element(W + "color")?.Attribute(W + "val");
            if (color != null)
            {
                if (color == "auto") c.Color = null;
                else if (Units.TryParseHexColor(color, out int rgb)) c.Color = rgb;
            }

            string shd = (string)rPr.Element(W + "shd")?.Attribute(W + "fill");
            if (shd != null && shd != "auto" && Units.TryParseHexColor(shd, out int fill)) c.Highlight = fill;

            string hl = (string)rPr.Element(W + "highlight")?.Attribute(W + "val");
            if (hl != null)
            {
                if (hl == "none") c.Highlight = null;
                else if (HighlightColors.TryGetValue(hl, out int h)) c.Highlight = h;
            }
        }

        private static void ReadRuns(Reader r, Paragraph para, XElement container, CharProps baseChar)
        {
            foreach (XElement e in container.Elements())
            {
                XName n = e.Name;
                if (n == W + "r") ReadRun(r, para, e, baseChar);
                else if (n == W + "hyperlink" || n == W + "ins" || n == W + "smartTag" || n == W + "fldSimple" ||
                         n == W + "customXml" || n == W + "moveTo" || n == W + "bdo" || n == W + "dir")
                    ReadRuns(r, para, e, baseChar);
                else if (n == W + "sdt") ReadRuns(r, para, e.Element(W + "sdtContent") ?? new XElement("x"), baseChar);
                else if (n == MC + "AlternateContent")
                {
                    XElement choice = e.Element(MC + "Fallback") ?? e.Element(MC + "Choice");
                    if (choice != null) ReadRuns(r, para, choice, baseChar);
                }
            }
        }

        private static void ReadRun(Reader r, Paragraph para, XElement run, CharProps baseChar)
        {
            XElement rPr = run.Element(W + "rPr");
            CharProps c = baseChar.Clone();
            string rStyle = (string)rPr?.Element(W + "rStyle")?.Attribute(W + "val");
            if (rStyle != null) foreach (StyleDef s in StyleChain(r, rStyle)) ApplyRPr(r, c, s.RPr);
            ApplyRPr(r, c, rPr);
            if (c.Hidden) return;

            foreach (XElement e in run.Elements())
            {
                XName n = e.Name;
                if (n == W + "fldChar")
                {
                    string type = (string)e.Attribute(W + "fldCharType");
                    if (type == "begin") { r.FieldDepth++; r.InFieldInstruction = true; }
                    else if (type == "separate") r.InFieldInstruction = false;
                    else if (type == "end") { r.FieldDepth = Math.Max(0, r.FieldDepth - 1); r.InFieldInstruction = false; }
                    continue;
                }
                if (r.InFieldInstruction) continue;

                if (n == W + "t") para.Inlines.Add(new TextRun(e.Value, c));
                else if (n == W + "tab") para.Inlines.Add(new TabRun(c));
                else if (n == W + "br")
                {
                    string type = (string)e.Attribute(W + "type");
                    if (type == "page") para.Inlines.Add(new PageBreakRun());
                    else if (type != "column") para.Inlines.Add(new LineBreakRun());
                }
                else if (n == W + "cr") para.Inlines.Add(new LineBreakRun());
                else if (n == W + "noBreakHyphen") para.Inlines.Add(new TextRun("‑", c));
                else if (n == W + "sym")
                {
                    string code = (string)e.Attribute(W + "char");
                    if (code != null && int.TryParse(code, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int ch))
                    {
                        if (ch >= 0xF000) ch -= 0xF000;
                        CharProps sc = c.Clone();
                        string font = (string)e.Attribute(W + "font");
                        if (!string.IsNullOrEmpty(font)) sc.Font = font;
                        para.Inlines.Add(new TextRun(font == "Symbol" && ch == 0xB7 ? "•" : ((char)ch).ToString(), sc));
                    }
                }
                else if (n == W + "drawing") ReadDrawing(r, para, e);
                else if (n == W + "pict" || n == W + "object") ReadVml(r, para, e);
                else if (n == MC + "AlternateContent")
                {
                    XElement choice = e.Element(MC + "Choice") ?? e.Element(MC + "Fallback");
                    if (choice != null)
                    {
                        XElement drawing = choice.Element(W + "drawing");
                        if (drawing != null) ReadDrawing(r, para, drawing);
                        else foreach (XElement pict in choice.Elements(W + "pict")) ReadVml(r, para, pict);
                    }
                }
            }
        }

        private static void ReadDrawing(Reader r, Paragraph para, XElement drawing)
        {
            XElement holder = drawing.Element(WP + "inline") ?? drawing.Element(WP + "anchor");
            if (holder == null) return;
            XElement blip = holder.Descendants(A + "blip").FirstOrDefault();
            string embed = (string)blip?.Attribute(R + "embed");
            if (embed == null || !r.Rels.TryGetValue(embed, out string path)) return;
            byte[] data = LoadBytes(r.Zip, path);
            if (data == null) return;

            XElement extent = holder.Element(WP + "extent");
            long cx = (long?)extent?.Attribute("cx") ?? 0, cy = (long?)extent?.Attribute("cy") ?? 0;
            AddImage(para, data, path, (int)(cx / Units.EmuPerTwip), (int)(cy / Units.EmuPerTwip));
        }

        private static void ReadVml(Reader r, Paragraph para, XElement pict)
        {
            XElement image = pict.Descendants(V + "imagedata").FirstOrDefault();
            string id = (string)image?.Attribute(R + "id");
            if (id == null || !r.Rels.TryGetValue(id, out string path)) return;
            byte[] data = LoadBytes(r.Zip, path);
            if (data == null) return;

            int w = 0, h = 0;
            string style = (string)image.Parent?.Attribute("style") ?? "";
            foreach (string part in style.Split(';'))
            {
                string[] kv = part.Split(':');
                if (kv.Length != 2) continue;
                if (!Units.TryParseLength(kv[1], out double tw)) continue;
                if (kv[0].Trim() == "width") w = (int)tw;
                else if (kv[0].Trim() == "height") h = (int)tw;
            }
            AddImage(para, data, path, w, h);
        }

        private static void AddImage(Paragraph para, byte[] data, string path, int wTw, int hTw)
        {
            string format = ImageRun.Sniff(data);
            if (format == null)
            {
                string ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
                format = ext == "jpg" ? "jpeg" : ext == "tif" ? "tiff" : ext;
            }
            para.Inlines.Add(new ImageRun { Data = data, Format = format, WidthTw = wTw, HeightTw = hTw });
        }

        #endregion

        #region Writing

        public static void Write(DocModel doc, Stream stream)
        {
            var media = new List<KeyValuePair<string, ImageRun>>();
            var abstractKinds = new List<ListKind>();
            var nums = new List<KeyValuePair<ListKind, int>>();

            var body = new XElement(W + "body");
            int docPrId = 1;
            ListNumbering.Info[] numbering = ListNumbering.Compute(doc);
            var levelNums = new Dictionary<int, int>();

            for (int pi = 0; pi < doc.Paragraphs.Count; pi++)
            {
                Paragraph p = doc.Paragraphs[pi];
                var pPr = new XElement(W + "pPr");
                ParaProps pp = p.Props;

                if (pp.List == ListKind.None) levelNums.Clear();
                else
                {
                    ListNumbering.Info info = numbering[pi];
                    foreach (int deeper in levelNums.Keys.Where(k => k > pp.ListLevel).ToList()) levelNums.Remove(deeper);
                    if (info.NewRun && !info.Continues)
                    {
                        if (!abstractKinds.Contains(pp.List)) abstractKinds.Add(pp.List);
                        nums.Add(new KeyValuePair<ListKind, int>(pp.List, info.Number));
                        levelNums[pp.ListLevel] = nums.Count;
                    }
                    int numId = levelNums.TryGetValue(pp.ListLevel, out int id) ? id : nums.Count;
                    pPr.Add(new XElement(W + "numPr",
                        new XElement(W + "ilvl", new XAttribute(W + "val", Math.Min(8, pp.ListLevel))),
                        new XElement(W + "numId", new XAttribute(W + "val", numId))));
                }

                if (pp.PageBreakBefore) pPr.Add(new XElement(W + "pageBreakBefore"));

                if (pp.Tabs.Count > 0)
                {
                    var tabs = new XElement(W + "tabs");
                    foreach (TabStop t in pp.Tabs)
                    {
                        var tab = new XElement(W + "tab",
                            new XAttribute(W + "val", t.Align == TabAlign.Center ? "center" : t.Align == TabAlign.Right ? "right" : t.Align == TabAlign.Decimal ? "decimal" : "left"),
                            new XAttribute(W + "pos", t.PositionTw));
                        if (t.Leader != TabLeader.None)
                            tab.Add(new XAttribute(W + "leader", t.Leader == TabLeader.Dots ? "dot" : t.Leader == TabLeader.Hyphens ? "hyphen" : "underscore"));
                        tabs.Add(tab);
                    }
                    pPr.Add(tabs);
                }

                var spacing = new XElement(W + "spacing",
                    new XAttribute(W + "before", pp.BeforeTw),
                    new XAttribute(W + "after", pp.AfterTw),
                    new XAttribute(W + "line", pp.Line > 0 ? pp.Line : 240),
                    new XAttribute(W + "lineRule", pp.Rule == LineRule.Exact ? "exact" : pp.Rule == LineRule.AtLeast ? "atLeast" : "auto"));
                pPr.Add(spacing);

                var ind = new XElement(W + "ind", new XAttribute(W + "left", pp.LeftTw), new XAttribute(W + "right", pp.RightTw));
                if (pp.FirstLineTw < 0) ind.Add(new XAttribute(W + "hanging", -pp.FirstLineTw));
                else ind.Add(new XAttribute(W + "firstLine", pp.FirstLineTw));
                pPr.Add(ind);

                pPr.Add(new XElement(W + "jc", new XAttribute(W + "val",
                    pp.Align == TextAlign.Center ? "center" : pp.Align == TextAlign.Right ? "right" : pp.Align == TextAlign.Justify ? "both" : "left")));

                pPr.Add(RunProps(p.MarkProps));

                var para = new XElement(W + "p", pPr);
                foreach (Inline inline in p.Inlines)
                {
                    if (inline is TextRun tr)
                    {
                        var run = new XElement(W + "r", RunProps(tr.Props));
                        var sb = new StringBuilder();
                        foreach (char ch in tr.Text)
                        {
                            if (ch == '\t' || ch == '\n' || ch == '\v')
                            {
                                if (sb.Length > 0) { run.Add(TextElement(sb.ToString())); sb.Clear(); }
                                run.Add(new XElement(ch == '\t' ? W + "tab" : W + "br"));
                            }
                            else if (ch == '\r') { }
                            else if (XmlSafe(ch)) sb.Append(ch);
                        }
                        if (sb.Length > 0) run.Add(TextElement(sb.ToString()));
                        para.Add(run);
                    }
                    else if (inline is TabRun tab) para.Add(new XElement(W + "r", RunProps(tab.Props), new XElement(W + "tab")));
                    else if (inline is LineBreakRun) para.Add(new XElement(W + "r", new XElement(W + "br")));
                    else if (inline is PageBreakRun) para.Add(new XElement(W + "r", new XElement(W + "br", new XAttribute(W + "type", "page"))));
                    else if (inline is ImageRun img)
                    {
                        string relId = "rIdImg" + (media.Count + 1);
                        media.Add(new KeyValuePair<string, ImageRun>(relId, img));
                        para.Add(new XElement(W + "r", Drawing(img, relId, docPrId++)));
                    }
                }
                body.Add(para);
            }

            var sect = new XElement(W + "sectPr");
            if (doc.HasPage)
            {
                var size = new XElement(W + "pgSz", new XAttribute(W + "w", doc.PageWidthTw), new XAttribute(W + "h", doc.PageHeightTw));
                if (doc.PageWidthTw > doc.PageHeightTw) size.Add(new XAttribute(W + "orient", "landscape"));
                sect.Add(size);
                sect.Add(new XElement(W + "pgMar",
                    new XAttribute(W + "top", Math.Max(0, doc.MarginTopTw)), new XAttribute(W + "right", Math.Max(0, doc.MarginRightTw)),
                    new XAttribute(W + "bottom", Math.Max(0, doc.MarginBottomTw)), new XAttribute(W + "left", Math.Max(0, doc.MarginLeftTw)),
                    new XAttribute(W + "header", 720), new XAttribute(W + "footer", 720), new XAttribute(W + "gutter", 0)));
            }
            body.Add(sect);

            var document = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(W + "document",
                    new XAttribute(XNamespace.Xmlns + "w", W), new XAttribute(XNamespace.Xmlns + "r", R),
                    new XAttribute(XNamespace.Xmlns + "wp", WP), new XAttribute(XNamespace.Xmlns + "a", A),
                    new XAttribute(XNamespace.Xmlns + "pic", Pic),
                    body));

            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                var types = new XElement(CT + "Types",
                    new XElement(CT + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                    new XElement(CT + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                    new XElement(CT + "Override", new XAttribute("PartName", "/word/document.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml")),
                    new XElement(CT + "Override", new XAttribute("PartName", "/word/styles.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml")),
                    new XElement(CT + "Override", new XAttribute("PartName", "/word/settings.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml")),
                    new XElement(CT + "Override", new XAttribute("PartName", "/docProps/core.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.core-properties+xml")),
                    new XElement(CT + "Override", new XAttribute("PartName", "/docProps/app.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.extended-properties+xml")));
                if (nums.Count > 0)
                    types.Add(new XElement(CT + "Override", new XAttribute("PartName", "/word/numbering.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml")));
                foreach (string ext in media.Select(m => m.Value.Extension).Distinct())
                    types.Add(new XElement(CT + "Default", new XAttribute("Extension", ext), new XAttribute("ContentType", media.First(m => m.Value.Extension == ext).Value.MimeType)));
                Save(zip, "[Content_Types].xml", new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), types));

                Save(zip, "_rels/.rels", Relationships(
                    Relationship("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "word/document.xml"),
                    Relationship("rId2", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties", "docProps/core.xml"),
                    Relationship("rId3", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties", "docProps/app.xml")));

                var docRels = new List<XElement>
                {
                    Relationship("rIdStyles", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles", "styles.xml"),
                    Relationship("rIdSettings", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings", "settings.xml")
                };
                if (nums.Count > 0) docRels.Add(Relationship("rIdNumbering", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering", "numbering.xml"));
                for (int i = 0; i < media.Count; i++)
                    docRels.Add(Relationship(media[i].Key, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image", "media/image" + (i + 1) + "." + media[i].Value.Extension));
                Save(zip, "word/_rels/document.xml.rels", Relationships(docRels.ToArray()));

                Save(zip, "word/document.xml", document);
                Save(zip, "word/styles.xml", Styles());
                Save(zip, "word/settings.xml", new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
                    new XElement(W + "settings", new XAttribute(XNamespace.Xmlns + "w", W),
                        new XElement(W + "defaultTabStop", new XAttribute(W + "val", doc.DefaultTabTw > 0 ? doc.DefaultTabTw : 720)),
                        new XElement(W + "characterSpacingControl", new XAttribute(W + "val", "doNotCompress")),
                        new XElement(W + "compat", new XElement(W + "compatSetting",
                            new XAttribute(W + "name", "compatibilityMode"), new XAttribute(W + "uri", "http://schemas.microsoft.com/office/word"), new XAttribute(W + "val", "15"))))));
                if (nums.Count > 0) Save(zip, "word/numbering.xml", Numbering(abstractKinds, nums));

                for (int i = 0; i < media.Count; i++)
                {
                    ZipArchiveEntry entry = zip.CreateEntry("word/media/image" + (i + 1) + "." + media[i].Value.Extension, CompressionLevel.Optimal);
                    using (Stream s = entry.Open()) s.Write(media[i].Value.Data, 0, media[i].Value.Data.Length);
                }

                XNamespace cp = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
                XNamespace dc = "http://purl.org/dc/elements/1.1/";
                XNamespace dcterms = "http://purl.org/dc/terms/";
                XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
                string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
                Save(zip, "docProps/core.xml", new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
                    new XElement(cp + "coreProperties",
                        new XAttribute(XNamespace.Xmlns + "cp", cp), new XAttribute(XNamespace.Xmlns + "dc", dc),
                        new XAttribute(XNamespace.Xmlns + "dcterms", dcterms), new XAttribute(XNamespace.Xmlns + "xsi", xsi),
                        new XElement(dcterms + "created", new XAttribute(xsi + "type", "dcterms:W3CDTF"), now),
                        new XElement(dcterms + "modified", new XAttribute(xsi + "type", "dcterms:W3CDTF"), now))));
                XNamespace ep = "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";
                Save(zip, "docProps/app.xml", new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
                    new XElement(ep + "Properties", new XElement(ep + "Application", "UltraPad"))));
            }
        }

        private static bool XmlSafe(char ch) => ch == '\t' || ch == '\n' || ch == '\r' || (ch >= 0x20 && ch <= 0xD7FF) || (ch >= 0xE000 && ch <= 0xFFFD) || char.IsSurrogate(ch);

        private static XElement TextElement(string text)
        {
            var t = new XElement(W + "t", text);
            if (text.Length > 0 && (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[text.Length - 1]) || text.Contains("  ")))
                t.Add(new XAttribute(XNamespace.Xml + "space", "preserve"));
            return t;
        }

        private static XElement RunProps(CharProps c)
        {
            var rPr = new XElement(W + "rPr");
            string font = string.IsNullOrEmpty(c.Font) ? CharProps.DefaultFont : c.Font;
            rPr.Add(new XElement(W + "rFonts", new XAttribute(W + "ascii", font), new XAttribute(W + "hAnsi", font),
                new XAttribute(W + "eastAsia", font), new XAttribute(W + "cs", font)));
            if (c.Bold) rPr.Add(new XElement(W + "b"), new XElement(W + "bCs"));
            if (c.Italic) rPr.Add(new XElement(W + "i"), new XElement(W + "iCs"));
            if (c.Strike) rPr.Add(new XElement(W + "strike"));
            if (c.Hidden) rPr.Add(new XElement(W + "vanish"));
            if (c.Color.HasValue) rPr.Add(new XElement(W + "color", new XAttribute(W + "val", Units.Hex(c.Color.Value))));
            rPr.Add(new XElement(W + "sz", new XAttribute(W + "val", c.HalfPoints)), new XElement(W + "szCs", new XAttribute(W + "val", c.HalfPoints)));
            if (c.Highlight.HasValue)
            {
                string named = HighlightColors.FirstOrDefault(kv => kv.Value == c.Highlight.Value).Key;
                if (named != null) rPr.Add(new XElement(W + "highlight", new XAttribute(W + "val", named)));
                else rPr.Add(new XElement(W + "shd", new XAttribute(W + "val", "clear"), new XAttribute(W + "color", "auto"), new XAttribute(W + "fill", Units.Hex(c.Highlight.Value))));
            }
            if (c.Underline != UnderlineKind.None)
            {
                string u;
                switch (c.Underline)
                {
                    case UnderlineKind.Double: u = "double"; break;
                    case UnderlineKind.Dotted: u = "dotted"; break;
                    case UnderlineKind.Dash: u = "dash"; break;
                    case UnderlineKind.Wave: u = "wave"; break;
                    case UnderlineKind.Thick: u = "thick"; break;
                    case UnderlineKind.Words: u = "words"; break;
                    default: u = "single"; break;
                }
                rPr.Add(new XElement(W + "u", new XAttribute(W + "val", u)));
            }
            if (c.Position != VerticalPosition.Baseline)
                rPr.Add(new XElement(W + "vertAlign", new XAttribute(W + "val", c.Position == VerticalPosition.Superscript ? "superscript" : "subscript")));
            return rPr;
        }

        private static XElement Drawing(ImageRun img, string relId, int id)
        {
            ImageRun.PixelSize(img.Data, out int pw, out int ph);
            long cx = (long)(img.WidthTw > 0 ? img.WidthTw : Math.Max(1, pw) * 15) * Units.EmuPerTwip;
            long cy = (long)(img.HeightTw > 0 ? img.HeightTw : Math.Max(1, ph) * 15) * Units.EmuPerTwip;
            string name = "Picture " + id;

            return new XElement(W + "drawing",
                new XElement(WP + "inline", new XAttribute("distT", 0), new XAttribute("distB", 0), new XAttribute("distL", 0), new XAttribute("distR", 0),
                    new XElement(WP + "extent", new XAttribute("cx", cx), new XAttribute("cy", cy)),
                    new XElement(WP + "effectExtent", new XAttribute("l", 0), new XAttribute("t", 0), new XAttribute("r", 0), new XAttribute("b", 0)),
                    new XElement(WP + "docPr", new XAttribute("id", id), new XAttribute("name", name)),
                    new XElement(WP + "cNvGraphicFramePr", new XElement(A + "graphicFrameLocks", new XAttribute("noChangeAspect", 1))),
                    new XElement(A + "graphic",
                        new XElement(A + "graphicData", new XAttribute("uri", "http://schemas.openxmlformats.org/drawingml/2006/picture"),
                            new XElement(Pic + "pic",
                                new XElement(Pic + "nvPicPr",
                                    new XElement(Pic + "cNvPr", new XAttribute("id", id), new XAttribute("name", name)),
                                    new XElement(Pic + "cNvPicPr")),
                                new XElement(Pic + "blipFill",
                                    new XElement(A + "blip", new XAttribute(R + "embed", relId)),
                                    new XElement(A + "stretch", new XElement(A + "fillRect"))),
                                new XElement(Pic + "spPr",
                                    new XElement(A + "xfrm",
                                        new XElement(A + "off", new XAttribute("x", 0), new XAttribute("y", 0)),
                                        new XElement(A + "ext", new XAttribute("cx", cx), new XAttribute("cy", cy))),
                                    new XElement(A + "prstGeom", new XAttribute("prst", "rect"), new XElement(A + "avLst"))))))));
        }

        private static XDocument Styles()
        {
            return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(W + "styles", new XAttribute(XNamespace.Xmlns + "w", W),
                    new XElement(W + "docDefaults",
                        new XElement(W + "rPrDefault", new XElement(W + "rPr",
                            new XElement(W + "rFonts", new XAttribute(W + "ascii", CharProps.DefaultFont), new XAttribute(W + "hAnsi", CharProps.DefaultFont),
                                new XAttribute(W + "eastAsia", CharProps.DefaultFont), new XAttribute(W + "cs", CharProps.DefaultFont)),
                            new XElement(W + "sz", new XAttribute(W + "val", 22)), new XElement(W + "szCs", new XAttribute(W + "val", 22)))),
                        new XElement(W + "pPrDefault", new XElement(W + "pPr",
                            new XElement(W + "spacing", new XAttribute(W + "after", 0), new XAttribute(W + "line", 240), new XAttribute(W + "lineRule", "auto"))))),
                    new XElement(W + "style", new XAttribute(W + "type", "paragraph"), new XAttribute(W + "default", 1), new XAttribute(W + "styleId", "Normal"),
                        new XElement(W + "name", new XAttribute(W + "val", "Normal")), new XElement(W + "qFormat")),
                    new XElement(W + "style", new XAttribute(W + "type", "character"), new XAttribute(W + "default", 1), new XAttribute(W + "styleId", "DefaultParagraphFont"),
                        new XElement(W + "name", new XAttribute(W + "val", "Default Paragraph Font")), new XElement(W + "uiPriority", new XAttribute(W + "val", 1)),
                        new XElement(W + "semiHidden"), new XElement(W + "unhideWhenUsed")),
                    new XElement(W + "style", new XAttribute(W + "type", "paragraph"), new XAttribute(W + "styleId", "ListParagraph"),
                        new XElement(W + "name", new XAttribute(W + "val", "List Paragraph")), new XElement(W + "basedOn", new XAttribute(W + "val", "Normal")))));
        }

        private static XDocument Numbering(List<ListKind> abstractKinds, List<KeyValuePair<ListKind, int>> nums)
        {
            var root = new XElement(W + "numbering", new XAttribute(XNamespace.Xmlns + "w", W));
            for (int a = 0; a < abstractKinds.Count; a++)
            {
                ListKind kind = abstractKinds[a];
                var abs = new XElement(W + "abstractNum", new XAttribute(W + "abstractNumId", a),
                    new XElement(W + "multiLevelType", new XAttribute(W + "val", "hybridMultilevel")));
                for (int lvl = 0; lvl < 9; lvl++)
                {
                    string fmt, text;
                    var rPr = new XElement(W + "rPr");
                    switch (kind)
                    {
                        case ListKind.Bullet: fmt = "bullet"; text = "•"; rPr.Add(new XElement(W + "rFonts", new XAttribute(W + "ascii", "Calibri"), new XAttribute(W + "hAnsi", "Calibri"))); break;
                        case ListKind.LowerLetter: fmt = "lowerLetter"; text = "%" + (lvl + 1) + "."; break;
                        case ListKind.UpperLetter: fmt = "upperLetter"; text = "%" + (lvl + 1) + "."; break;
                        case ListKind.LowerRoman: fmt = "lowerRoman"; text = "%" + (lvl + 1) + "."; break;
                        case ListKind.UpperRoman: fmt = "upperRoman"; text = "%" + (lvl + 1) + "."; break;
                        default: fmt = "decimal"; text = "%" + (lvl + 1) + "."; break;
                    }
                    var level = new XElement(W + "lvl", new XAttribute(W + "ilvl", lvl),
                        new XElement(W + "start", new XAttribute(W + "val", 1)),
                        new XElement(W + "numFmt", new XAttribute(W + "val", fmt)),
                        new XElement(W + "lvlText", new XAttribute(W + "val", text)),
                        new XElement(W + "lvlJc", new XAttribute(W + "val", "left")),
                        new XElement(W + "pPr", new XElement(W + "ind", new XAttribute(W + "left", 720 * (lvl + 1)), new XAttribute(W + "hanging", 360))));
                    if (rPr.HasElements) level.Add(rPr);
                    abs.Add(level);
                }
                root.Add(abs);
            }
            for (int i = 0; i < nums.Count; i++)
            {
                root.Add(new XElement(W + "num", new XAttribute(W + "numId", i + 1),
                    new XElement(W + "abstractNumId", new XAttribute(W + "val", abstractKinds.IndexOf(nums[i].Key))),
                    Enumerable.Range(0, 9).Select(l => new XElement(W + "lvlOverride", new XAttribute(W + "ilvl", l),
                        new XElement(W + "startOverride", new XAttribute(W + "val", Math.Max(1, nums[i].Value)))))));
            }
            return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), root);
        }

        private static XElement Relationship(string id, string type, string target) =>
            new XElement(Rel + "Relationship", new XAttribute("Id", id), new XAttribute("Type", type), new XAttribute("Target", target));

        private static XDocument Relationships(params XElement[] rels) =>
            new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(Rel + "Relationships", rels));

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
