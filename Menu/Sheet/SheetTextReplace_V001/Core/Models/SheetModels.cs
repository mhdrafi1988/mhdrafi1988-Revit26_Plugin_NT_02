using System.Collections.Generic;

namespace Revit26_Plugin_Revised.SheetTextReplace.V001.Core.Models
{
    /// <summary>A sheet in the sheet picker.</summary>
    public sealed record SheetItem(long Id, string Number, string Name)
    {
        /// <summary>"A-201 – Building Sections".</summary>
        public string Label => string.IsNullOrWhiteSpace(Name) ? Number : $"{Number} – {Name}";

        /// <inheritdoc/>
        public override string ToString() => Label;
    }

    /// <summary>A text note as read from the model.</summary>
    public sealed class NoteInfo
    {
        /// <summary>Element id of the TextNote.</summary>
        public long Id { get; init; }

        /// <summary>The note's text as Revit returns it (line breaks are '\r').</summary>
        public string Text { get; init; } = string.Empty;

        /// <summary>Why the note cannot be changed (owned by another user, in a group…), or null.</summary>
        public string BlockReason { get; init; }

        /// <summary>
        /// Formatting the note has, for the "kept" tag in the preview (for example "Bold, underline"),
        /// or null when the note is plain text.
        /// </summary>
        public string Formatting { get; init; }
    }

    /// <summary>
    /// A view placed on the sheet, or the sheet itself (for notes placed directly on it), with the
    /// text notes shown in it.
    /// </summary>
    public sealed class ViewInfo
    {
        /// <summary>Type label used for the sheet's own notes.</summary>
        public const string SheetItselfType = "Sheet (no view)";

        /// <summary>Element id of the view (the sheet's id for the sheet itself).</summary>
        public long Key { get; init; }

        /// <summary>View name.</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>View type shown as a group in the Views list ("Section", "Legend"…).</summary>
        public string TypeLabel { get; init; } = string.Empty;

        /// <summary>"1 : 100", or "—" when the view has no scale.</summary>
        public string ScaleText { get; init; } = "—";

        /// <summary>True for the sheet's own notes (not a viewport).</summary>
        public bool IsSheetItself { get; init; }

        /// <summary>User who owns the view in a workshared model, or null.</summary>
        public string Owner { get; init; }

        /// <summary>
        /// Numbers of the other sheets that show this view (legends can be on many sheets; a change
        /// shows on all of them). Empty for most views.
        /// </summary>
        public IReadOnlyList<string> AlsoOnSheets { get; init; } = new List<string>();

        /// <summary>The text notes shown in this view.</summary>
        public IReadOnlyList<NoteInfo> Notes { get; init; } = new List<NoteInfo>();
    }

    /// <summary>Everything the window shows for one sheet.</summary>
    public sealed class SheetContent
    {
        /// <summary>The sheet.</summary>
        public SheetItem Sheet { get; init; }

        /// <summary>The sheet itself (first, when it has notes) and its views.</summary>
        public IReadOnlyList<ViewInfo> Views { get; init; } = new List<ViewInfo>();
    }

    /// <summary>One note to change, as previewed.</summary>
    public sealed class NoteChange
    {
        /// <summary>Element id of the TextNote.</summary>
        public long NoteId { get; init; }

        /// <summary>Name of the view the note is shown in, for messages.</summary>
        public string ViewName { get; init; } = string.Empty;

        /// <summary>The note's text when the preview was made; the note is skipped if it differs now.</summary>
        public string ExpectedText { get; init; } = string.Empty;

        /// <summary>The text to write.</summary>
        public string NewText { get; init; } = string.Empty;

        /// <summary>The edits from <see cref="ReplaceOutcome.EditPasses"/>, applied so formatting is kept.</summary>
        public IReadOnlyList<IReadOnlyList<TextEdit>> EditPasses { get; init; } = new List<IReadOnlyList<TextEdit>>();
    }

    /// <summary>A note that was skipped or failed during Apply, with the reason.</summary>
    public sealed record NoteIssue(long NoteId, string ViewName, string Text, string Reason);

    /// <summary>What the window asks Revit to change.</summary>
    public sealed class ApplyRequest
    {
        /// <summary>The sheet the notes are on.</summary>
        public long SheetId { get; init; }

        /// <summary>The ticked notes to change.</summary>
        public IReadOnlyList<NoteChange> Changes { get; init; } = new List<NoteChange>();
    }

    /// <summary>The result of an Apply.</summary>
    public sealed class ApplyResult
    {
        /// <summary>False when nothing was changed because of an error (see <see cref="Error"/>).</summary>
        public bool Success { get; init; }

        /// <summary>Why nothing was changed, when <see cref="Success"/> is false.</summary>
        public string Error { get; init; }

        /// <summary>Ids of the notes that were changed.</summary>
        public IReadOnlyList<long> ChangedIds { get; init; } = new List<long>();

        /// <summary>Notes left as they were, with the reason (changed since preview, now owned by someone…).</summary>
        public IReadOnlyList<NoteIssue> Skipped { get; init; } = new List<NoteIssue>();

        /// <summary>Notes Revit refused to change, with Revit's reason.</summary>
        public IReadOnlyList<NoteIssue> Failed { get; init; } = new List<NoteIssue>();

        /// <summary>Changed notes whose formatting could not be kept (their text was set as plain text).</summary>
        public IReadOnlyList<NoteIssue> FormattingReset { get; init; } = new List<NoteIssue>();

        /// <summary>How long the change took inside Revit, in milliseconds.</summary>
        public long ElapsedMs { get; init; }

        /// <summary>The sheet as it is after the Apply (re-read), or null when it could not be read.</summary>
        public SheetContent Content { get; init; }
    }
}
