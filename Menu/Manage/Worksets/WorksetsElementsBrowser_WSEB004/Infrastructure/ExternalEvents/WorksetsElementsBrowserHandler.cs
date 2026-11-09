using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.WorksetsElementsBrowser.WSEB004.Core.Models;
using Revit26_Plugin_Revised.WorksetsElementsBrowser.WSEB004.Core.Services;

namespace Revit26_Plugin_Revised.WorksetsElementsBrowser.WSEB004.Infrastructure.ExternalEvents
{
    /// <summary>
    /// Requests this tool's orchestrator handler can execute, set by the
    /// ViewModel immediately before ExternalEvent.Raise() — per Revit API
    /// threading rules only one Execute() runs at a time, so there is no
    /// race between setting Request and it being read.
    /// </summary>
    public enum WorksetsElementsBrowserRequest
    {
        LoadTree,
        LoadViews,
        ApplyAction,
        SelectElements,
        SaveSelectionSet
    }

    /// <summary>
    /// Single shared orchestrator for all Revit-API-touching actions in this
    /// tool: building the Workset/Category/Type tree, listing 3D views, and
    /// applying Isolate/Select/Isolate+Select of a set of elements in a
    /// chosen 3D view (which also becomes the active view).
    /// </summary>
    public class WorksetsElementsBrowserHandler : IExternalEventHandler
    {
        private readonly UIDocument _uiDoc;
        private readonly WorksetTreeBuilder _treeBuilder = new();

        public WorksetsElementsBrowserRequest Request { get; set; }

        // ---- Inputs (set by ViewModel before Raise()) ----
        public List<ElementId> ActionElementIds { get; set; } = new();
        public ElementId TargetViewId { get; set; }
        public ElementActionMode ActionMode { get; set; }

        /// <summary>Name for the Revit selection set to create (SaveSelectionSet).</summary>
        public string SelectionSetName { get; set; } = string.Empty;

        /// <summary>When true, an existing selection set of that name is replaced; when false it is left untouched.</summary>
        public bool OverwriteExisting { get; set; }

        // ---- Outputs (read by ViewModel after RequestCompleted fires) ----
        public List<WorksetNodeData> LoadedTree { get; private set; } = new();
        public List<View3DOption> LoadedViews { get; private set; } = new();
        public string ErrorMessage { get; private set; } = string.Empty;
        public bool LastRunSucceeded { get; private set; }

        /// <summary>What the last SaveSelectionSet request did.</summary>
        public SelectionSetOutcome SelectionSetResult { get; private set; }

        /// <summary>Number of elements stored by the last SaveSelectionSet request.</summary>
        public int SelectionSetElementCount { get; private set; }

        public event Action RequestCompleted;

        public WorksetsElementsBrowserHandler(UIDocument uiDoc)
        {
            _uiDoc = uiDoc;
        }

        public void Execute(UIApplication app) =>
            ToolGuard.RunHandler(GetType(), app, ExecuteCore);

        private void ExecuteCore(UIApplication app)
        {
            WsebDebugLog.Write($"Execute() entered, Request={Request}");
            ErrorMessage = string.Empty;
            // Esc / Cancel with a Yes-No confirm while the workset tree loads (the other requests are instant).
            using ToolCancellation cancel = Request == WorksetsElementsBrowserRequest.LoadTree
                ? ToolCancellation.BeginFor(ToolCatalog.WorksetsElementsBrowser, app)
                : null;
            try
            {
                switch (Request)
                {
                    case WorksetsElementsBrowserRequest.LoadTree:
                        ExecuteLoadTree();
                        break;
                    case WorksetsElementsBrowserRequest.LoadViews:
                        ExecuteLoadViews();
                        break;
                    case WorksetsElementsBrowserRequest.ApplyAction:
                        ExecuteApplyAction();
                        break;
                    case WorksetsElementsBrowserRequest.SelectElements:
                        ExecuteSelectElements();
                        break;
                    case WorksetsElementsBrowserRequest.SaveSelectionSet:
                        ExecuteSaveSelectionSet();
                        break;
                }
                LastRunSucceeded = true;
                WsebDebugLog.Write($"Execute() finished OK, Request={Request}");
            }
            catch (OperationCanceledException)
            {
                LastRunSucceeded = false;
                ErrorMessage = "Cancelled by user.";
                throw; // ToolGuard treats it as a user cancel; finally still completes the request
            }
            catch (Exception ex)
            {
                LastRunSucceeded = false;
                ErrorMessage = ex.Message;
                WsebDebugLog.Write($"Execute() THREW, Request={Request}: {ex}");
                if (ex is not InvalidOperationException) ToolGuard.Report(typeof(WorksetsElementsBrowserHandler), ex); // expected validation messages are shown in the UI only
            }
            finally
            {
                RequestCompleted?.Invoke();
            }
        }

        public string GetName() => "WorksetsElementsBrowser Orchestrator";

        private void ExecuteLoadTree()
        {
            LoadedTree = _treeBuilder.Build(_uiDoc.Document);
        }

        private void ExecuteLoadViews()
        {
            LoadedViews = new FilteredElementCollector(_uiDoc.Document)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .Where(v => !v.IsTemplate)
                .OrderBy(v => v.Name)
                .Select(v => new View3DOption(v.Id, v.Name))
                .ToList();
        }

        /// <summary>
        /// Activates the chosen 3D view, then isolates and/or selects the
        /// given elements in it, per ActionMode. Temporary isolation is set
        /// via View.IsolateElementsTemporary (reset by the user through
        /// Revit's own "Reset Temporary Hide/Isolate", same as any manual
        /// isolate) rather than permanent visibility overrides.
        /// </summary>
        private void ExecuteApplyAction()
        {
            WsebDebugLog.Write($"ExecuteApplyAction: TargetViewId={TargetViewId}, ActionMode={ActionMode}, ActionElementIds.Count={ActionElementIds?.Count ?? -1}");

            var doc = _uiDoc.Document;
            var view = doc.GetElement(TargetViewId) as View3D;
            if (view == null)
                throw new InvalidOperationException("The selected 3D view no longer exists.");

            var validIds = ActionElementIds
                .Where(id => doc.GetElement(id) != null)
                .ToList();
            WsebDebugLog.Write($"ExecuteApplyAction: view resolved '{view.Name}', validIds.Count={validIds.Count}");

            _uiDoc.ActiveView = view;
            WsebDebugLog.Write("ExecuteApplyAction: ActiveView set");

            if (ActionMode == ElementActionMode.Isolate || ActionMode == ElementActionMode.IsolateAndSelect)
            {
                using var tx = new Transaction(doc, "Worksets & Elements Browser - Isolate");
                tx.Start();
                if (view.IsInTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate))
                    view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);

                if (validIds.Count > 0)
                    view.IsolateElementsTemporary(validIds);
                tx.Commit();
                WsebDebugLog.Write("ExecuteApplyAction: isolate transaction committed");
            }

            if (ActionMode == ElementActionMode.Select || ActionMode == ElementActionMode.IsolateAndSelect)
            {
                _uiDoc.Selection.SetElementIds(validIds);
                WsebDebugLog.Write("ExecuteApplyAction: selection set");
            }
        }

        /// <summary>Sets the Revit selection to the given elements without changing the active view.</summary>
        private void ExecuteSelectElements()
        {
            var doc = _uiDoc.Document;
            var validIds = ActionElementIds.Where(id => doc.GetElement(id) != null).ToList();
            if (validIds.Count == 0)
                throw new InvalidOperationException("None of those elements exist any more. Reload the browser.");
            _uiDoc.Selection.SetElementIds(validIds);
        }

        /// <summary>
        /// Saves <see cref="ActionElementIds"/> as a Revit selection set (<see cref="SelectionFilterElement"/>)
        /// named <see cref="SelectionSetName"/>, in one transaction that rolls back on any failure.
        /// A name already used by a non-selection-set filter is refused; a name used by a selection set is
        /// replaced only when <see cref="OverwriteExisting"/> is true.
        /// </summary>
        private void ExecuteSaveSelectionSet()
        {
            SelectionSetResult = SelectionSetOutcome.None;
            SelectionSetElementCount = 0;

            var doc = _uiDoc.Document;
            string name = (SelectionSetName ?? string.Empty).Trim();
            if (name.Length == 0)
                throw new InvalidOperationException("Enter a name for the selection set.");
            if (doc.IsReadOnly)
                throw new InvalidOperationException("The document is read-only; a selection set cannot be saved.");

            var validIds = ActionElementIds.Where(id => doc.GetElement(id) != null).Distinct().ToList();
            if (validIds.Count == 0)
                throw new InvalidOperationException("None of the checked elements exist any more. Reload the browser.");

            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(FilterElement))
                .Cast<FilterElement>()
                .FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

            if (existing != null && existing is not SelectionFilterElement)
                throw new InvalidOperationException($"The name \"{name}\" is already used by a view filter. Choose another name.");

            if (existing != null && !OverwriteExisting)
            {
                SelectionSetResult = SelectionSetOutcome.NameExists;
                return;
            }

            using var tx = new Transaction(doc, "Worksets & Elements Browser - Save Selection Set");
            tx.Start();
            try
            {
                var set = existing as SelectionFilterElement ?? SelectionFilterElement.Create(doc, name);
                set.SetElementIds(validIds);
                tx.Commit();
                SelectionSetResult = existing == null ? SelectionSetOutcome.Created : SelectionSetOutcome.Updated;
                SelectionSetElementCount = validIds.Count;
            }
            catch
            {
                if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                throw;
            }
        }
    }
}
