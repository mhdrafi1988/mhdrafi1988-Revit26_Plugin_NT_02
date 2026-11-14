using Revit26_Plugin_Revised.SheetTextReplace.V001.Core.Models;
using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.SheetTextReplace.V001.Infrastructure
{
    /// <summary>Result of loading or refreshing a sheet.</summary>
    public sealed class LoadResult
    {
        /// <summary>The sheet's views and notes, or null when it could not be read (see <see cref="Error"/>).</summary>
        public SheetContent Content { get; init; }

        /// <summary>Why the sheet could not be read, or null.</summary>
        public string Error { get; init; }
    }

    /// <summary>
    /// The window's only link to Revit: reads sheets, applies the replace and shows notes, inside
    /// Revit's API context. Callbacks run on the UI thread once the request has been handled. Lets the
    /// ViewModel be tested without Revit.
    /// </summary>
    public interface ISheetTextSession : IDisposable
    {
        /// <summary>All sheets of the model, read when the tool opened.</summary>
        IReadOnlyList<SheetItem> Sheets { get; }

        /// <summary>The active sheet's content when the tool was opened on a sheet, otherwise null.</summary>
        SheetContent Initial { get; }

        /// <summary>Reads <paramref name="sheetId"/>. Returns false when Revit did not accept the request.</summary>
        bool Load(long sheetId, Action<LoadResult> done);

        /// <summary>Applies <paramref name="request"/> in one undoable transaction. Returns false when Revit did not accept the request.</summary>
        bool Apply(ApplyRequest request, Action<ApplyResult> done);

        /// <summary>
        /// Shows <paramref name="noteId"/> in Revit (opens a view that shows it, zooms to it and
        /// selects it). The callback gets an error message, or null on success. Returns false when
        /// Revit did not accept the request.
        /// </summary>
        bool ShowNote(long noteId, Action<string> done);
    }
}
