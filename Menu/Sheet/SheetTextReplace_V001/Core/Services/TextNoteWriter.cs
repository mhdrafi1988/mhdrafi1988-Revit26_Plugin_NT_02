using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.SheetTextReplace.V001.Core.Models;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.SheetTextReplace.V001.Core.Services
{
    /// <summary>
    /// Writes new text into a TextNote while keeping its formatting: only the edited ranges of the
    /// note's <see cref="FormattedText"/> are replaced, so bold, italic, underline and lists in the
    /// rest of the note stay. Falls back to setting the plain text when that is not possible.
    /// Must run inside an open Transaction.
    /// </summary>
    public static class TextNoteWriter
    {
        /// <summary>
        /// Describes the formatting <paramref name="note"/> has ("Bold, underline", "List"), or null
        /// for plain text. Never throws.
        /// </summary>
        public static string DescribeFormatting(TextNote note)
        {
            try
            {
                var ft = note.GetFormattedText();
                var parts = new List<string>();
                if (ft.GetBoldStatus() != FormatStatus.None) parts.Add("bold");
                if (ft.GetItalicStatus() != FormatStatus.None) parts.Add("italic");
                if (ft.GetUnderlineStatus() != FormatStatus.None) parts.Add("underline");
                if (ft.GetAllCapsStatus() != FormatStatus.None) parts.Add("all caps");
                int length = (ft.GetPlainText() ?? string.Empty).Length;
                if (length > 0 && ft.GetListType(new TextRange(0, length)) != ListType.None) parts.Add("list");
                if (parts.Count == 0) return null;
                string text = string.Join(", ", parts);
                return char.ToUpperInvariant(text[0]) + text.Substring(1);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return null;
            }
            catch (System.Exception)
            {
                return null; // formatting is only shown as a hint; never block reading
            }
        }

        /// <summary>
        /// Writes <paramref name="change"/> into <paramref name="note"/>. Returns true when the
        /// formatting was kept, false when the note's text had to be set as plain text (its
        /// formatting is then reset). Revit exceptions from setting the plain text propagate.
        /// </summary>
        public static bool Write(TextNote note, NoteChange change)
        {
            if (TryWriteFormatted(note, change))
                return true;

            note.Text = change.NewText;
            return false;
        }

        private static bool TryWriteFormatted(TextNote note, NoteChange change)
        {
            try
            {
                var ft = note.GetFormattedText();
                string mirror = ft.GetPlainText() ?? string.Empty;
                if (mirror != change.ExpectedText)
                    return false; // positions would not line up

                foreach (var pass in change.EditPasses)
                {
                    foreach (var edit in pass.OrderByDescending(e => e.Start).ThenByDescending(e => e.Length))
                        mirror = ApplyOne(ft, mirror, edit);
                }

                if (Normalise(ft.GetPlainText()) != Normalise(change.NewText))
                    return false;

                note.SetFormattedText(ft);
                return Normalise(note.Text) == Normalise(change.NewText);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return false;
            }
            catch (System.ArgumentException)
            {
                return false;
            }
        }

        /// <summary>
        /// Applies one edit. Revit's SetPlainText is not used with an empty range or empty text:
        /// those edits are widened by one neighbouring character, which is written back unchanged.
        /// </summary>
        private static string ApplyOne(FormattedText ft, string mirror, TextEdit edit)
        {
            int start = edit.Start, length = edit.Length;
            string text = edit.Text ?? string.Empty;

            if (length == 0 || text.Length == 0)
            {
                bool leftOk = start > 0 && !ReplaceEngine.IsBreak(mirror[start - 1]);
                bool rightOk = start + length < mirror.Length && !ReplaceEngine.IsBreak(mirror[start + length]);
                if (leftOk || (!rightOk && start > 0))
                {
                    text = mirror[start - 1] + text;
                    start -= 1;
                    length += 1;
                }
                else if (start + length < mirror.Length)
                {
                    text += mirror[start + length];
                    length += 1;
                }
                else
                {
                    throw new System.ArgumentException("Nothing to anchor the edit to.");
                }
            }

            ft.SetPlainText(new TextRange(start, length), text);
            return mirror.Remove(start, length).Insert(start, text);
        }

        private static string Normalise(string s) => (s ?? string.Empty).Replace("\r\n", "\r").TrimEnd('\r', '\n');
    }
}
