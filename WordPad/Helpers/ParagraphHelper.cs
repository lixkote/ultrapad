using System;
using System.Collections.Generic;
using Windows.UI.Text;
using Windows.UI.Xaml.Controls;

namespace WordPad.Helpers
{
    public static class ParagraphHelper
    {
        public const float Undefined = -9999999f;

        public static bool IsKnown(float v) => !float.IsNaN(v) && v > -1000000f && v < 1000000f;

        public static void ForEachSelectedParagraph(RichEditBox editor, Action<ITextParagraphFormat> action)
        {
            ITextDocument doc = editor.Document;
            ITextSelection sel = doc.Selection;
            int start = sel.StartPosition;
            int end = sel.EndPosition;

            doc.BeginUndoGroup();
            try
            {
                ITextRange range = doc.GetRange(start, start);
                range.Expand(TextRangeUnit.Paragraph);
                int guard = 0;
                while (guard++ < 100000)
                {
                    action(range.ParagraphFormat);

                    int paraEnd = range.EndPosition;
                    if (paraEnd >= end) break;

                    range.SetRange(paraEnd, paraEnd);
                    range.Expand(TextRangeUnit.Paragraph);
                    if (range.EndPosition <= paraEnd) break;
                }
            }
            finally
            {
                doc.EndUndoGroup();
            }
        }

        public static void SetIndents(RichEditBox editor, float left, float right, float firstLine)
        {
            ForEachSelectedParagraph(editor, f =>
            {
                float l = IsKnown(left) ? left : Safe(f.LeftIndent);
                float r = IsKnown(right) ? right : Safe(f.RightIndent);
                float fl = IsKnown(firstLine) ? firstLine : Safe(f.FirstLineIndent);
                if (l + fl < 0) fl = -l;
                f.SetIndents(fl, l, r);
            });
        }

        private static float Safe(float v) => IsKnown(v) ? v : 0;

        public static List<float> GetTabs(RichEditBox editor)
        {
            ITextParagraphFormat f = editor.Document.Selection.ParagraphFormat;
            int count = f.TabCount;
            if (count < 0 || count > 64) return null;
            var list = new List<float>();
            for (int i = 0; i < count; i++)
            {
                f.GetTab(i, out float pos, out TabAlignment align, out TabLeader leader);
                if (IsKnown(pos)) list.Add(pos);
            }
            list.Sort();
            return list;
        }

        public static void SetTabs(RichEditBox editor, IEnumerable<float> positions)
        {
            var copy = new List<float>(positions);
            copy.Sort();
            ForEachSelectedParagraph(editor, f =>
            {
                f.ClearAllTabs();
                foreach (float pos in copy)
                {
                    f.AddTab(pos, TabAlignment.Left, TabLeader.Spaces);
                }
            });
        }
    }
}
