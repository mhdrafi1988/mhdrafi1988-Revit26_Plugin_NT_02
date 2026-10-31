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
        /// Reads the model's sheets and, when <paramref name="activeSheet"/> is given, its notes; then
        /// prepares the ExternalEvent.
        /// </summary>
        public RevitSheetTextSession(Document doc, ViewSheet activeSheet)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            Sheets = SheetTextReader.ReadSheets(doc);
            if (activeSheet != null)
                Initial = SheetTextReader.Read(doc, activeSheet);
            _externalEvent = ExternalEvent.Create(_handler);
        }

        /// <inheritdoc/>
        public IReadOnlyList<SheetItem> Sheets { get; }

        /// <inheritdoc/>
        public SheetContent Initial { get; }

        /// <inheritdoc/>
        public bool Load(long sheetId, Action<LoadResult> done) => Raise(_ =>
        {
            LoadResult result = null;
            try
            {
                var sheet = GetSheet(sheetId, out string error);
                result = sheet == null
                    ? new LoadResult { Error = error }
                    : new LoadResult { Content = SheetTextReader.Read(_doc, sheet) };
            }
            finally
            {
                done(result ?? new LoadResult { Error = "Unexpected error while reading the sheet — see the error dialog." });
            }
        });

        /// <inheritdoc/>
        public bool Apply(ApplyRequest request, Action<ApplyResult> done) => Raise(_ =>
        {
            ApplyResult result = null;
            try
            {
                var sheet = GetSheet(request.SheetId, out string error);
                if (sheet == null)
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
                    Content = SheetTextReader.Read(_doc, sheet) // re-read: the window shows what is in the model now
                };
            }
            finally
            {
                done(result ?? new ApplyResult { Success = false, Error = "Unexpected error — see the error dialog. Nothing was changed." });
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
            _handler.Queue(work);
            return _externalEvent.Raise() == ExternalEventRequest.Accepted;
        }

        private ViewSheet GetSheet(long sheetId, out string error)
        {
            error = null;
            if (!_doc.IsValidObject)
                error = "The document was closed. Close this window.";
            else if (_doc.GetElement(new ElementId(sheetId)) is not ViewSheet sheet)
                error = "The sheet no longer exists. Pick another sheet.";
            else
                return sheet;
            return null;
        }

        /// <summary>Releases the ExternalEvent.</summary>
        public void Dispose() => _externalEvent.Dispose();
    }
}
