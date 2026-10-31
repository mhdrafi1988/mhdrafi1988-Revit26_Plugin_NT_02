using System.Collections.Generic;

namespace Revit26_Plugin_Revised.SheetTextReplace.V001.Core.Models
{
    /// <summary>What to find in the text notes and how to change the notes that match.</summary>
    public sealed record ReplaceOptions
    {
        /// <summary>Text (or regular expression, with <see cref="UseRegex"/>) to find. Required.</summary>
        public string Find { get; init; } = string.Empty;

        /// <summary>Text that replaces each match. With <see cref="UseRegex"/> it may use $1-style groups.</summary>
        public string Replacement { get; init; } = string.Empty;

        /// <summary>Added at the start of every note that changes.</summary>
        public string Prefix { get; init; } = string.Empty;

        /// <summary>Added at the end of every note that changes (before a trailing line break).</summary>
        public string Suffix { get; init; } = string.Empty;

        /// <summary>Match upper and lower case exactly.</summary>
        public bool MatchCase { get; init; }

        /// <summary>Match only where the found text is not part of a longer word.</summary>
        public bool WholeWord { get; init; }

        /// <summary>Treat <see cref="Find"/> as a .NET regular expression.</summary>
        public bool UseRegex { get; init; }

        /// <summary>
        /// In notes that change: remove spaces at the start and end of each line and turn runs of
        /// spaces into one. Line breaks are kept.
        /// </summary>
        public bool TrimSpaces { get; init; }
    }

    /// <summary>How a piece of displayed text relates to the replace.</summary>
    public enum SegmentKind
    {
        /// <summary>Unchanged text.</summary>
        Same,

        /// <summary>Text that the replace removes (shown struck through in red).</summary>
        Removed,

        /// <summary>Text that the replace adds (shown in green).</summary>
        Inserted
    }

    /// <summary>One run of displayed text in the preview.</summary>
    public sealed record TextSegment(string Text, SegmentKind Kind);

    /// <summary>
    /// One change to a string: replace <see cref="Length"/> characters at <see cref="Start"/>
    /// with <see cref="Text"/>. A length of 0 is an insertion; an empty text is a deletion.
    /// </summary>
    public readonly record struct TextEdit(int Start, int Length, string Text);

    /// <summary>The result of running the replace on one note's text.</summary>
    public sealed class ReplaceOutcome
    {
        /// <summary>True when the find text occurs in the note.</summary>
        public bool Matched { get; init; }

        /// <summary>Number of matches found.</summary>
        public int MatchCount { get; init; }

        /// <summary>The note's text before the replace.</summary>
        public string OldText { get; init; } = string.Empty;

        /// <summary>The note's text after replace, trim, prefix and suffix.</summary>
        public string NewText { get; init; } = string.Empty;

        /// <summary>True when <see cref="NewText"/> differs from <see cref="OldText"/>.</summary>
        public bool Changed => Matched && NewText != OldText;

        /// <summary>The old text with the matched parts marked <see cref="SegmentKind.Removed"/>.</summary>
        public IReadOnlyList<TextSegment> OldSegments { get; init; } = new List<TextSegment>();

        /// <summary>The new text with the added parts marked <see cref="SegmentKind.Inserted"/>.</summary>
        public IReadOnlyList<TextSegment> NewSegments { get; init; } = new List<TextSegment>();

        /// <summary>
        /// The edits that turn <see cref="OldText"/> into <see cref="NewText"/>, in passes
        /// (replace, trim, prefix/suffix). Each pass is in ascending order and uses the positions of
        /// the text as it is before that pass. Applying only the edits keeps the formatting of the
        /// rest of the note (see <c>TextNoteWriter</c>).
        /// </summary>
        public IReadOnlyList<IReadOnlyList<TextEdit>> EditPasses { get; init; } = new List<IReadOnlyList<TextEdit>>();
    }
}
