using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace UltraPad.Helpers.Formats
{
    public sealed class RtfReader
    {
        private sealed class State
        {
            public CharProps Char = new CharProps();
            public ParaProps Para = new ParaProps();
            public int UnicodeSkip = 1;
            public Dest Destination = Dest.Normal;
            public int FontCodePage = -1;
            public TabStop PendingTab = new TabStop();

            public State Clone()
            {
                var s = (State)MemberwiseClone();
                s.Char = Char.Clone();
                s.Para = Para.Clone();
                s.PendingTab = PendingTab.Clone();
                return s;
            }
        }

        private enum Dest { Normal, Skip, FontTable, FontEntry, ColorTable, Picture, ListTable, ListOverrideTable, Pn, PnText, Ignore }

        private sealed class FontInfo { public string Name = ""; public int CodePage = -1; }

        private sealed class PictureInfo
        {
            public string Format;
            public int PicW, PicH, GoalW, GoalH, ScaleX = 100, ScaleY = 100;
            public StringBuilder Hex = new StringBuilder();
            public bool Binary;
            public List<byte> Bin;
        }

        private sealed class ListLevelInfo { public ListKind Kind; public int Start = 1; }

        private readonly string _rtf;
        private int _pos;
        private readonly Stack<State> _stack = new Stack<State>();
        private State _s = new State();
        private int _groupDepth;
        private int _skipDepth = -1;

        private readonly Dictionary<int, FontInfo> _fonts = new Dictionary<int, FontInfo>();
        private FontInfo _currentFont;
        private int _currentFontNumber;
        private readonly List<int?> _colors = new List<int?>();
        private int _red, _green, _blue;
        private bool _colorSet;
        private int _defaultFont;
        private Encoding _ansi = Latin1();
        private int _defaultTab = 720;

        private readonly Dictionary<int, List<ListLevelInfo>> _lists = new Dictionary<int, List<ListLevelInfo>>();
        private readonly Dictionary<int, int> _overrides = new Dictionary<int, int>();
        private int _currentListId;
        private List<ListLevelInfo> _currentListLevels;
        private ListLevelInfo _currentLevel;
        private int _listOverrideListId;

        private PictureInfo _picture;
        private int _pictureDepth = -1;

        private readonly DocModel _doc = new DocModel();
        private Paragraph _para = new Paragraph();
        private readonly StringBuilder _text = new StringBuilder();
        private CharProps _textProps;
        private readonly List<byte> _pendingBytes = new List<byte>();
        private int _pendingBytesCodePage;
        private int _skipChars;
        private int _fieldInstDepth = -1;

        private RtfReader(string rtf) { _rtf = rtf ?? ""; }

        public static DocModel Read(string rtf)
        {
            var reader = new RtfReader(rtf);
            reader.Parse();
            return reader._doc;
        }

        private static Encoding Latin1()
        {
            try { return Encoding.GetEncoding(1252); } catch { return Encoding.GetEncoding("iso-8859-1"); }
        }

        private static Encoding CodePage(int cp)
        {
            if (cp <= 0) return null;
            try { return Encoding.GetEncoding(cp); } catch { return null; }
        }

        private static int CharsetToCodePage(int charset)
        {
            switch (charset)
            {
                case 0: return 1252;
                case 2: return 42;
                case 77: return 10000;
                case 128: return 932;
                case 129: return 949;
                case 134: return 936;
                case 136: return 950;
                case 161: return 1253;
                case 162: return 1254;
                case 163: return 1258;
                case 177: return 1255;
                case 178: return 1256;
                case 186: return 1257;
                case 204: return 1251;
                case 222: return 874;
                case 238: return 1250;
                case 255: return 437;
                default: return -1;
            }
        }

        private void Parse()
        {
            int n = _rtf.Length;
            while (_pos < n)
            {
                char c = _rtf[_pos];
                if (c == '{')
                {
                    _pos++;
                    FlushBytes();
                    _stack.Push(_s.Clone());
                    _groupDepth++;
                }
                else if (c == '}')
                {
                    _pos++;
                    FlushBytes();
                    EndGroup();
                }
                else if (c == '\\')
                {
                    ReadControl();
                }
                else if (c == '\r' || c == '\n')
                {
                    _pos++;
                }
                else
                {
                    _pos++;
                    OnChar(c);
                }
            }
            FlushBytes();
            FinishParagraph(last: true);
            _doc.DefaultTabTw = _defaultTab;
            _doc.Normalize();
        }

        private void EndGroup()
        {
            Dest ending = _s.Destination;
            int depth = _groupDepth;

            if (ending == Dest.Picture && depth == _pictureDepth) FinishPicture();
            if (ending == Dest.FontEntry) StoreFont();
            if (ending == Dest.ListTable && _currentLevel != null && depth == _levelDepth)
            {
                _currentLevel = null;
            }
            if (ending == Dest.ListTable && depth == _listDepth && _currentListLevels != null)
            {
                _lists[_currentListId] = _currentListLevels;
                _currentListLevels = null;
            }
            if (depth == _skipDepth) _skipDepth = -1;
            if (depth == _fieldInstDepth) _fieldInstDepth = -1;

            FlushText();
            if (_groupDepth == 1 && ending == Dest.Normal) FinishParagraph(last: true);
            ParaProps pn = ending == Dest.Pn ? _s.Para : null;
            if (_stack.Count > 0) _s = _stack.Pop();
            _groupDepth--;

            if (pn != null && _s.Destination != Dest.Pn)
            {
                _s.Para.List = pn.List;
                _s.Para.ListStart = pn.ListStart;
                _s.Para.ListLevel = pn.ListLevel;
            }
        }

        private int _levelDepth = -1, _listDepth = -1;

        private void StoreFont()
        {
            if (_currentFont == null) return;
            _currentFont.Name = _currentFont.Name.Trim().TrimEnd(';').Trim();
            if (!_fonts.ContainsKey(_currentFontNumber) || _currentFont.Name.Length > 0) _fonts[_currentFontNumber] = _currentFont;
            _currentFont = null;
        }

        private bool Skipping => _skipDepth >= 0 && _groupDepth >= _skipDepth;

        private void ReadControl()
        {
            _pos++;
            if (_pos >= _rtf.Length) return;
            char c = _rtf[_pos];

            if (!char.IsLetter(c))
            {
                _pos++;
                switch (c)
                {
                    case '\'':
                        if (_pos + 2 <= _rtf.Length && int.TryParse(_rtf.Substring(_pos, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int b))
                        {
                            _pos += 2;
                            OnByte((byte)b);
                        }
                        return;
                    case '*':
                        _ignorableNext = true;
                        return;
                    case '~': OnChar(' '); return;
                    case '-': return;
                    case '_': OnChar('‑'); return;
                    case '\r':
                    case '\n': FlushBytes(); OnParagraph(); return;
                    case '\\':
                    case '{':
                    case '}': OnChar(c); return;
                    case '|':
                    case ':': return;
                    default: return;
                }
            }

            int start = _pos;
            while (_pos < _rtf.Length && char.IsLetter(_rtf[_pos])) _pos++;
            string word = _rtf.Substring(start, _pos - start);
            bool hasParam = false;
            int param = 0;
            if (_pos < _rtf.Length && (_rtf[_pos] == '-' || char.IsDigit(_rtf[_pos])))
            {
                int ps = _pos;
                if (_rtf[_pos] == '-') _pos++;
                while (_pos < _rtf.Length && char.IsDigit(_rtf[_pos])) _pos++;
                long value;
                if (long.TryParse(_rtf.Substring(ps, _pos - ps), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value))
                {
                    param = (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, value));
                    hasParam = true;
                }
            }
            if (_pos < _rtf.Length && _rtf[_pos] == ' ') _pos++;

            bool ignorable = _ignorableNext;
            _ignorableNext = false;

            if (word == "bin" && hasParam)
            {
                int count = Math.Max(0, Math.Min(param, _rtf.Length - _pos));
                if (_s.Destination == Dest.Picture && _picture != null)
                {
                    _picture.Binary = true;
                    _picture.Bin = new List<byte>(count);
                    for (int i = 0; i < count; i++) _picture.Bin.Add((byte)_rtf[_pos + i]);
                }
                _pos += count;
                return;
            }

            if (word != "u" && word != "uc" && word != "'") FlushBytes();
            HandleWord(word, hasParam, param, ignorable);
        }

        private bool _ignorableNext;

        private void SkipGroup()
        {
            _s.Destination = Dest.Skip;
            if (_skipDepth < 0) _skipDepth = _groupDepth;
        }

        private void HandleWord(string word, bool hasParam, int param, bool ignorable)
        {
            if (Skipping) return;

            switch (word)
            {
                case "fonttbl": _s.Destination = Dest.FontTable; return;
                case "colortbl": _s.Destination = Dest.ColorTable; _colors.Clear(); _colorSet = false; return;
                case "pict":
                    _s.Destination = Dest.Picture;
                    _picture = new PictureInfo();
                    _pictureDepth = _groupDepth;
                    return;
                case "listtable": _s.Destination = Dest.ListTable; return;
                case "listoverridetable": _s.Destination = Dest.ListOverrideTable; return;
                case "pntext": _s.Destination = Dest.PnText; SkipGroup(); return;
                case "pn":
                    _s.Destination = Dest.Pn;
                    _s.Para.List = ListKind.Decimal;
                    _s.Para.ListStart = 1;
                    return;
                case "fldinst": SkipGroup(); return;
                case "fldrslt": return;
                case "field": return;
                case "shppict": return;
                case "nonshppict":
                case "stylesheet":
                case "info":
                case "header": case "headerl": case "headerr": case "headerf":
                case "footer": case "footerl": case "footerr": case "footerf":
                case "footnote": case "annotation": case "xe": case "tc":
                case "objdata": case "objclass": case "objname":
                case "themedata": case "colorschememapping": case "latentstyles":
                case "datastore": case "xmlnstbl": case "rsidtbl": case "generator":
                case "mmathPr": case "pgdsctbl": case "listtext": case "filetbl":
                case "revtbl": case "userprops": case "docvar": case "bkmkstart": case "bkmkend":
                case "fontemb": case "fontfile": case "falt": case "panose": case "leveltext": case "levelnumbers":
                case "listname": case "liststylename": case "picprop": case "blipuid": case "shpinst": case "sp":
                    SkipGroup(); return;
                case "result": return;
                case "object": return;
            }

            if (ignorable && _s.Destination != Dest.Pn && _s.Destination != Dest.Picture)
            {
                SkipGroup();
                return;
            }

            switch (_s.Destination)
            {
                case Dest.FontTable:
                case Dest.FontEntry:
                    if (word == "f" && hasParam)
                    {
                        StoreFont();
                        _s.Destination = Dest.FontEntry;
                        _currentFont = new FontInfo();
                        _currentFontNumber = param;
                    }
                    else if (word == "fcharset" && hasParam && _currentFont != null)
                    {
                        _currentFont.CodePage = CharsetToCodePage(param);
                    }
                    else if (word == "cpg" && hasParam && _currentFont != null)
                    {
                        _currentFont.CodePage = param;
                    }
                    return;

                case Dest.ColorTable:
                    if (word == "red") { _red = param; _colorSet = true; }
                    else if (word == "green") { _green = param; _colorSet = true; }
                    else if (word == "blue") { _blue = param; _colorSet = true; }
                    return;

                case Dest.Picture:
                    if (_picture == null) return;
                    switch (word)
                    {
                        case "pngblip": _picture.Format = "png"; break;
                        case "jpegblip": _picture.Format = "jpeg"; break;
                        case "emfblip": _picture.Format = "emf"; break;
                        case "wmetafile": _picture.Format = "wmf"; break;
                        case "dibitmap": _picture.Format = "dib"; break;
                        case "wbitmap": _picture.Format = null; break;
                        case "macpict": _picture.Format = null; break;
                        case "picw": _picture.PicW = param; break;
                        case "pich": _picture.PicH = param; break;
                        case "picwgoal": _picture.GoalW = param; break;
                        case "pichgoal": _picture.GoalH = param; break;
                        case "picscalex": _picture.ScaleX = param; break;
                        case "picscaley": _picture.ScaleY = param; break;
                    }
                    return;

                case Dest.ListTable:
                    HandleListTable(word, hasParam, param);
                    return;

                case Dest.ListOverrideTable:
                    if (word == "listid") _listOverrideListId = param;
                    else if (word == "ls" && hasParam) _overrides[param] = _listOverrideListId;
                    return;

                case Dest.Pn:
                    switch (word)
                    {
                        case "pnlvlblt": _s.Para.List = ListKind.Bullet; break;
                        case "pnlvlcont": _s.Para.List = ListKind.None; break;
                        case "pndec": _s.Para.List = ListKind.Decimal; break;
                        case "pnlcltr": _s.Para.List = ListKind.LowerLetter; break;
                        case "pnucltr": _s.Para.List = ListKind.UpperLetter; break;
                        case "pnlcrm": _s.Para.List = ListKind.LowerRoman; break;
                        case "pnucrm": _s.Para.List = ListKind.UpperRoman; break;
                        case "pnstart": _s.Para.ListStart = Math.Max(1, param); break;
                        case "pnlvl": _s.Para.ListLevel = Math.Max(0, param - 1); break;
                        case "pntxtb": case "pntxta": SkipGroup(); break;
                    }
                    return;
            }

            HandleFormatting(word, hasParam, param);
        }

        private void HandleListTable(string word, bool hasParam, int param)
        {
            switch (word)
            {
                case "list":
                    _currentListLevels = new List<ListLevelInfo>();
                    _listDepth = _groupDepth;
                    break;
                case "listlevel":
                    _currentLevel = new ListLevelInfo { Kind = ListKind.Decimal };
                    _currentListLevels?.Add(_currentLevel);
                    _levelDepth = _groupDepth;
                    break;
                case "levelnfc":
                case "levelnfcn":
                    if (_currentLevel != null) _currentLevel.Kind = NfcToKind(param);
                    break;
                case "levelstartat":
                    if (_currentLevel != null) _currentLevel.Start = Math.Max(1, param);
                    break;
                case "listid":
                    _currentListId = param;
                    break;
            }
        }

        private static ListKind NfcToKind(int nfc)
        {
            switch (nfc)
            {
                case 0: return ListKind.Decimal;
                case 1: return ListKind.UpperRoman;
                case 2: return ListKind.LowerRoman;
                case 3: return ListKind.UpperLetter;
                case 4: return ListKind.LowerLetter;
                case 23: return ListKind.Bullet;
                case 255: return ListKind.None;
                default: return ListKind.Decimal;
            }
        }

        private int _ls = -1, _ilvl;

        private void HandleFormatting(string word, bool hasParam, int param)
        {
            CharProps c = _s.Char;
            ParaProps p = _s.Para;
            bool on = !hasParam || param != 0;

            switch (word)
            {
                case "ansicpg": { Encoding e = CodePage(param); if (e != null) _ansi = e; break; }
                case "deff": _defaultFont = param; break;
                case "deftab": if (param > 0) _defaultTab = param; break;
                case "uc": _s.UnicodeSkip = Math.Max(0, param); break;
                case "u":
                    {
                        FlushBytes();
                        int code = param < 0 ? param + 65536 : param;
                        OnChar((char)code, fromUnicode: true);
                        _skipChars = _s.UnicodeSkip;
                        break;
                    }

                case "par": FlushBytes(); OnParagraph(); break;
                case "sect": FlushBytes(); OnParagraph(); break;
                case "line": AddInline(new LineBreakRun()); break;
                case "page": AddInline(new PageBreakRun()); break;
                case "tab": AddInline(new TabRun(_s.Char.Clone())); break;
                case "cell": AddInline(new TabRun(_s.Char.Clone())); break;
                case "row": FlushBytes(); OnParagraph(); break;
                case "emdash": OnChar('—'); break;
                case "endash": OnChar('–'); break;
                case "emspace": OnChar(' '); break;
                case "enspace": OnChar(' '); break;
                case "qmspace": OnChar(' '); break;
                case "bullet": OnChar('•'); break;
                case "lquote": OnChar('‘'); break;
                case "rquote": OnChar('’'); break;
                case "ldblquote": OnChar('“'); break;
                case "rdblquote": OnChar('”'); break;
                case "zwj": OnChar('‍'); break;
                case "zwnj": OnChar('‌'); break;

                case "plain":
                    {
                        var fresh = new CharProps();
                        if (_fonts.TryGetValue(_defaultFont, out FontInfo df) && df.Name.Length > 0) fresh.Font = df.Name;
                        _s.Char = fresh;
                        _s.FontCodePage = _fonts.TryGetValue(_defaultFont, out df) ? df.CodePage : -1;
                        break;
                    }
                case "f":
                    if (_fonts.TryGetValue(param, out FontInfo fi))
                    {
                        if (fi.Name.Length > 0) c.Font = fi.Name;
                        _s.FontCodePage = fi.CodePage;
                    }
                    break;
                case "fs": if (param > 0) c.HalfPoints = param; break;
                case "b": c.Bold = on; break;
                case "i": c.Italic = on; break;
                case "strike": c.Strike = on; break;
                case "striked": c.Strike = on; break;
                case "v": c.Hidden = on; break;
                case "ul": c.Underline = on ? UnderlineKind.Single : UnderlineKind.None; break;
                case "ulnone": c.Underline = UnderlineKind.None; break;
                case "uldb": c.Underline = UnderlineKind.Double; break;
                case "uld": case "uldashd": case "uldashdd": c.Underline = UnderlineKind.Dotted; break;
                case "uldash": case "ulldash": case "ulthdash": c.Underline = UnderlineKind.Dash; break;
                case "ulwave": case "ululdbwave": case "ulhwave": c.Underline = UnderlineKind.Wave; break;
                case "ulth": case "ulthd": c.Underline = UnderlineKind.Thick; break;
                case "ulw": c.Underline = UnderlineKind.Words; break;
                case "super": c.Position = VerticalPosition.Superscript; break;
                case "sub": c.Position = VerticalPosition.Subscript; break;
                case "nosupersub": c.Position = VerticalPosition.Baseline; break;
                case "up": c.Position = param > 0 ? VerticalPosition.Superscript : VerticalPosition.Baseline; break;
                case "dn": c.Position = param > 0 ? VerticalPosition.Subscript : VerticalPosition.Baseline; break;
                case "cf": c.Color = ColorAt(param); break;
                case "highlight": c.Highlight = ColorAt(param); break;
                case "chcbpat": case "cb": { int? h = ColorAt(param); if (h.HasValue && h.Value != 0xFFFFFF) c.Highlight = h; else if (param == 0) c.Highlight = null; break; }

                case "pard":
                    {
                        _s.Para = new ParaProps();
                        _ls = -1;
                        _ilvl = 0;
                        break;
                    }
                case "ql": p.Align = TextAlign.Left; break;
                case "qc": p.Align = TextAlign.Center; break;
                case "qr": p.Align = TextAlign.Right; break;
                case "qj": case "qd": p.Align = TextAlign.Justify; break;
                case "li": case "lin": p.LeftTw = param; break;
                case "ri": case "rin": p.RightTw = param; break;
                case "fi": p.FirstLineTw = param; break;
                case "sb": p.BeforeTw = Math.Max(0, param); break;
                case "sa": p.AfterTw = Math.Max(0, param); break;
                case "sl":
                    if (param == 0) { p.Line = 240; p.Rule = LineRule.Auto; }
                    else if (param < 0) { p.Line = -param; p.Rule = LineRule.Exact; }
                    else { p.Line = param; p.Rule = LineRule.AtLeast; }
                    break;
                case "slmult":
                    if (param == 1 && p.Rule != LineRule.Exact) p.Rule = LineRule.Auto;
                    break;
                case "pagebb": p.PageBreakBefore = true; break;
                case "tqr": _s.PendingTab.Align = TabAlign.Right; break;
                case "tqc": _s.PendingTab.Align = TabAlign.Center; break;
                case "tqdec": _s.PendingTab.Align = TabAlign.Decimal; break;
                case "tldot": case "tlmdot": _s.PendingTab.Leader = TabLeader.Dots; break;
                case "tlhyph": _s.PendingTab.Leader = TabLeader.Hyphens; break;
                case "tlul": case "tlth": case "tleq": _s.PendingTab.Leader = TabLeader.Underline; break;
                case "tx":
                case "tb":
                    {
                        TabStop t = _s.PendingTab.Clone();
                        t.PositionTw = param;
                        p.Tabs.Add(t);
                        _s.PendingTab = new TabStop();
                        break;
                    }
                case "ls":
                    _ls = param;
                    ApplyWordList(p);
                    break;
                case "ilvl":
                    _ilvl = Math.Max(0, param);
                    ApplyWordList(p);
                    break;
                case "intbl": break;
            }
        }

        private void ApplyWordList(ParaProps p)
        {
            if (_ls < 0) return;
            if (!_overrides.TryGetValue(_ls, out int listId)) listId = _ls;
            if (!_lists.TryGetValue(listId, out List<ListLevelInfo> levels) || levels.Count == 0)
            {
                p.List = ListKind.Bullet;
                return;
            }
            ListLevelInfo level = levels[Math.Min(_ilvl, levels.Count - 1)];
            p.List = level.Kind;
            p.ListStart = level.Start;
            p.ListLevel = _ilvl;
        }

        private int? ColorAt(int index)
        {
            if (index <= 0 || index >= _colors.Count) return null;
            return _colors[index];
        }

        private void OnByte(byte b)
        {
            if (_skipChars > 0) { _skipChars--; return; }
            if (Skipping) return;
            if (_s.Destination == Dest.Picture) return;

            int cp = _s.FontCodePage > 0 ? _s.FontCodePage : 0;
            if (_pendingBytes.Count > 0 && cp != _pendingBytesCodePage) FlushBytes();
            _pendingBytesCodePage = cp;
            _pendingBytes.Add(b);
        }

        private void FlushBytes()
        {
            if (_pendingBytes.Count == 0) return;
            byte[] bytes = _pendingBytes.ToArray();
            _pendingBytes.Clear();
            string s;
            if (_pendingBytesCodePage == 42)
            {
                var sb = new StringBuilder();
                foreach (byte b in bytes) sb.Append(b == 0xB7 ? '•' : (char)(0xF000 + b));
                s = sb.ToString();
            }
            else
            {
                Encoding e = (_pendingBytesCodePage > 0 ? CodePage(_pendingBytesCodePage) : null) ?? _ansi;
                s = e.GetString(bytes);
            }
            foreach (char ch in s) AppendChar(ch);
        }

        private void OnChar(char ch, bool fromUnicode = false)
        {
            if (!fromUnicode && _skipChars > 0) { _skipChars--; return; }
            FlushBytes();
            if (Skipping) return;

            switch (_s.Destination)
            {
                case Dest.FontEntry:
                    AppendChar(ch);
                    return;
                case Dest.FontTable:
                    return;
                case Dest.ColorTable:
                    if (ch == ';')
                    {
                        _colors.Add(_colorSet ? (int?)((_red << 16) | (_green << 8) | _blue) : null);
                        _red = _green = _blue = 0;
                        _colorSet = false;
                    }
                    return;
                case Dest.Picture:
                    if (_picture != null && Uri.IsHexDigit(ch)) _picture.Hex.Append(ch);
                    return;
                case Dest.Normal:
                    AppendChar(ch);
                    return;
                default:
                    return;
            }
        }

        private void AppendChar(char ch)
        {
            if (_s.Destination == Dest.FontEntry)
            {
                if (_currentFont == null) return;
                if (ch == ';') StoreFont();
                else _currentFont.Name += ch;
                return;
            }
            if (_s.Destination != Dest.Normal || Skipping) return;
            if (_s.Char.Hidden) return;

            if (_textProps != null && !_textProps.SameAs(_s.Char)) FlushText();
            if (_text.Length == 0) _textProps = _s.Char.Clone();
            _text.Append(ch);
        }

        private void FlushText()
        {
            if (_text.Length == 0) return;
            _para.Inlines.Add(new TextRun(_text.ToString(), _textProps ?? _s.Char.Clone()));
            _text.Clear();
            _textProps = null;
        }

        private void AddInline(Inline inline)
        {
            if (_s.Destination != Dest.Normal || Skipping) return;
            FlushText();
            _para.Inlines.Add(inline);
        }

        private void OnParagraph()
        {
            if (_s.Destination != Dest.Normal || Skipping) return;
            FinishParagraph(last: false);
        }

        private void FinishParagraph(bool last)
        {
            FlushText();
            if (last && _para.Inlines.Count == 0 && _doc.Paragraphs.Count > 0) return;

            _para.Props = _s.Para.Clone();
            _para.MarkProps = _s.Char.Clone();
            _doc.Paragraphs.Add(_para);
            _para = new Paragraph();
        }

        private void FinishPicture()
        {
            PictureInfo pic = _picture;
            _picture = null;
            _pictureDepth = -1;
            if (pic == null || pic.Format == null) return;

            byte[] data;
            if (pic.Binary && pic.Bin != null) data = pic.Bin.ToArray();
            else
            {
                string hex = pic.Hex.ToString();
                data = new byte[hex.Length / 2];
                for (int i = 0; i < data.Length; i++)
                    data[i] = (byte)((HexValue(hex[2 * i]) << 4) | HexValue(hex[2 * i + 1]));
            }
            if (data.Length == 0) return;

            string format = pic.Format;
            if (format == "dib")
            {
                int headerSize = BitConverter.ToInt32(data, 0);
                int bitCount = BitConverter.ToInt16(data, 14);
                int colorsUsed = data.Length > 36 ? BitConverter.ToInt32(data, 32) : 0;
                int paletteSize = (bitCount <= 8 ? (colorsUsed > 0 ? colorsUsed : 1 << bitCount) : 0) * 4;
                var bmp = new byte[14 + data.Length];
                bmp[0] = 0x42; bmp[1] = 0x4D;
                BitConverter.GetBytes(bmp.Length).CopyTo(bmp, 2);
                BitConverter.GetBytes(14 + headerSize + paletteSize).CopyTo(bmp, 10);
                Array.Copy(data, 0, bmp, 14, data.Length);
                data = bmp;
                format = "bmp";
            }

            ImageRun.PixelSize(data, out int pw, out int ph);
            int goalW = pic.GoalW > 0 ? pic.GoalW : (pw > 0 ? pw * 15 : pic.PicW * 15);
            int goalH = pic.GoalH > 0 ? pic.GoalH : (ph > 0 ? ph * 15 : pic.PicH * 15);
            if ((format == "emf" || format == "wmf") && pic.GoalW <= 0)
            {
                goalW = (int)(pic.PicW * 1440L / 2540);
                goalH = (int)(pic.PicH * 1440L / 2540);
            }

            if (format == "wmf" && !(data.Length > 4 && data[0] == 0xD7 && data[1] == 0xCD && data[2] == 0xC6 && data[3] == 0x9A))
            {
                data = AddPlaceableHeader(data, Math.Max(1, goalW), Math.Max(1, goalH));
            }

            var image = new ImageRun
            {
                Data = data,
                Format = format,
                WidthTw = Math.Max(1, goalW * pic.ScaleX / 100),
                HeightTw = Math.Max(1, goalH * pic.ScaleY / 100)
            };

            FlushText();
            _para.Inlines.Add(image);
        }

        private static byte[] AddPlaceableHeader(byte[] wmf, int widthTw, int heightTw)
        {
            var header = new byte[22];
            BitConverter.GetBytes(0x9AC6CDD7u).CopyTo(header, 0);
            BitConverter.GetBytes((short)0).CopyTo(header, 4);
            BitConverter.GetBytes((short)0).CopyTo(header, 6);
            BitConverter.GetBytes((short)0).CopyTo(header, 8);
            BitConverter.GetBytes((short)Math.Min(short.MaxValue, widthTw)).CopyTo(header, 10);
            BitConverter.GetBytes((short)Math.Min(short.MaxValue, heightTw)).CopyTo(header, 12);
            BitConverter.GetBytes((short)1440).CopyTo(header, 14);
            ushort checksum = 0;
            for (int i = 0; i < 20; i += 2) checksum ^= BitConverter.ToUInt16(header, i);
            BitConverter.GetBytes(checksum).CopyTo(header, 20);
            var result = new byte[header.Length + wmf.Length];
            header.CopyTo(result, 0);
            wmf.CopyTo(result, header.Length);
            return result;
        }

        private static int HexValue(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return 0;
        }
    }
}
