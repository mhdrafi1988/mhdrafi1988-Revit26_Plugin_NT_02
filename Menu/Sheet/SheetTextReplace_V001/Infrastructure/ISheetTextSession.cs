using Revit26_Plugin_Revised.SheetTextReplace.V001.Core.Models;
using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.SheetTextReplace.V001.Infrastructure
{
    /// <summary>Result of loading or refreshing a scope (a sheet, or a single view).</summary>
    public sealed class LoadResult
    {
        /// <summary>The scope's views and notes, or null when it could not be read (see <see cref="Error"/>).</summary>
        public ScopeContent Content { get; init; }

        /// <summary>Why the scope could not be read, or null.</summary>
        public string Error { get; init; }
    }

    /// <summary>
    /// The window's only link to Revit: reads sheets and views, applies the replace and shows notes, inside
    /// Revit's API context. Callbacks run on the UI thread once the request has been handled. Lets the
    /// ViewModel be tested without Revit.
    /// </summary>
    public interface ISheetTextSession : IDisposable
    {
        /// <summary>
        /// Everything the tool can work on, read when it opened: the model's sheets, then the views
        /// that can hold text notes.
        /// </summary>
        IReadOnlyList<ScopeItem> Scopes { get; }

        /// <summary>
        /// The scope the tool opened on — the active sheet, or the active view when the user chose to
        /// work on it alone — otherwise null.
        /// </summary>
        ScopeContent Initial { get; }

        /// <summary>Reads <paramref name="scope"/>. Returns false when Revit did not accept the request.</summary>
        bool Load(ScopeItem scope, Action<LoadResult> done);

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
