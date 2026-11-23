using Revit26_Plugin_Revised.Shared.Services;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.SheetTextReplace.V001.Core.Models;
using Revit26_Plugin_Revised.SheetTextReplace.V001.Core.Services;
using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.SheetTextReplace.V001.Infrastructure
{
    /// <summary>
    /// <see cref="ISheetTextSession"/> backed by an ExternalEvent. Must be created inside Revit's API
    /// context (the command).
    /// </summary>
    public sealed class RevitSheetTextSession : ISheetTextSession
    {
        private readonly Document _doc;
        private readonly SheetTextReplaceHandler _handler = new();
        private readonly ExternalEvent _externalEvent;

        /// <summary>
        /// Reads the model's sheets and text-capable views and, when <paramref name="initialScope"/> is
        /// given (the active sheet, or the active view the user chose to work on alone), its notes;
        /// then prepares the ExternalEvent.
        /// </summary>
        public RevitSheetTextSession(Document doc, ScopeItem initialScope)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            Scopes = SheetTextReader.ReadScopes(doc);
            if (initialScope != null)
                Initial = ReadScope(initialScope, out _);
            _externalEvent = ExternalEvent.Create(_handler);
        }

        /// <inheritdoc/>
        public IReadOnlyList<ScopeItem> Scopes { get; }

        /// <inheritdoc/>
        public ScopeContent Initial { get; }

        /// <inheritdoc/>
        public bool Load(ScopeItem scope, Action<LoadResult> done) => Raise(_ =>
        {
            LoadResult result = null;
            try
            {
                var content = ReadScope(scope, out string error);
                result = content == null
                    ? new LoadResult { Error = error }
                    : new LoadResult { Content = content };
            }
            finally
            {
                done(result ?? new LoadResult { Error = "Unexpected error while reading the sheet or view — see the error dialog." });
            }
        });

        /// <inheritdoc/>
        public bool Apply(ApplyRequest request, Action<ApplyResult> done) => Raise(_ =>
        {
            ApplyResult result = null;
            try
            {
                // Check the sheet or view is still there before touching anything.
                var target = Resolve(request.Scope, out string error);
                if (target == null)
                {
                    result = new ApplyResult { Success = false, Error = error + " Nothing was changed." };
                    return;
                }

                var applied = TextReplaceApplier.Apply(_doc, request);
                result = new ApplyResult
                {
                    Success = applied.Success,
                    Error = applied.Error,
                    ChangedIds = applied.ChangedIds,
                    Skipped = applied.Skipped,
                    Failed = applied.Failed,
                    FormattingReset = applied.FormattingReset,
                    ElapsedMs = applied.ElapsedMs,
                    // Re-read: the window shows what is in the model now.
                    Content = ReadTarget(target)
                };
            }
            finally
            {
                done(result ?? new ApplyResult { Success = false, Error = ToolCancellation.CancelledOr("Unexpected error — see the error dialog. Nothing was changed.") });
            }
        });

        /// <inheritdoc/>
        public bool ShowNote(long noteId, Action<string> done) => Raise(app =>
        {
            string error = "Unexpected error — see the error dialog.";
            try
            {
                var uiDoc = app.ActiveUIDocument;
                if (!_doc.IsValidObject)
                    error = "The document was closed. Close this window.";
                else if (uiDoc == null || !uiDoc.Document.Equals(_doc))
                    error = "Switch back to this model's window in Revit, then try again.";
                else if (_doc.GetElement(new ElementId(noteId)) is not TextNote)
                    error = "That note no longer exists. Press Refresh.";
                else
                {
                    var ids = new List<ElementId> { new ElementId(noteId) };
                    uiDoc.ShowElements(ids);
                    uiDoc.Selection.SetElementIds(ids);
                    error = null;
                }
            }
            finally
            {
                done(error);
            }
        });

        private bool Raise(Action<UIApplication> work)
        {
            // One request at a time: a second one would replace the first, whose callback would then
            // never run and the window would stay busy.
            if (!_handler.TryQueue(work)) return false;
            if (_externalEvent.Raise() == ExternalEventRequest.Accepted) return true;
            _handler.ClearPending();
            return false;
        }

        /// <summary>
        /// The sheet or view <paramref name="scope"/> points at, or null with <paramref name="error"/>
        /// set when it can no longer be used. Cheap: it only looks the element up.
        /// </summary>
        private View Resolve(ScopeItem scope, out string error)
        {
            error = null;
            if (scope == null)
            {
                error = "No sheet or view is picked.";
                return null;
            }
            if (!_doc.IsValidObject)
            {
                error = "The document was closed. Close this window.";
                return null;
            }

            var element = _doc.GetElement(new ElementId(scope.Id));
            if (scope.IsView)
            {
                if (element is View view && view is not ViewSheet) return view;
                error = "The view no longer exists. Pick another sheet or view.";
                return null;
            }

            if (element is ViewSheet sheet) return sheet;
            error = "The sheet no longer exists. Pick another sheet or view.";
            return null;
        }

        /// <summary>Reads the notes of a resolved sheet (with its views) or single view.</summary>
        private ScopeContent ReadTarget(View target)
            => target is ViewSheet sheet ? SheetTextReader.Read(_doc, sheet) : SheetTextReader.ReadView(_doc, target);

        /// <summary>
        /// Reads <paramref name="scope"/> — a sheet with its views, or one view on its own. Returns
        /// null and sets <paramref name="error"/> when it can no longer be read.
        /// </summary>
        private ScopeContent ReadScope(ScopeItem scope, out string error)
        {
            var target = Resolve(scope, out error);
            return target == null ? null : ReadTarget(target);
        }

        /// <summary>Releases the ExternalEvent.</summary>
        public void Dispose() => _externalEvent.Dispose();
    }
}
