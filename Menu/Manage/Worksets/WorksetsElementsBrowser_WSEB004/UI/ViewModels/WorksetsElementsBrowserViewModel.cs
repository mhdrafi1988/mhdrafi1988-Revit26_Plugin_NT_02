using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.WorksetsElementsBrowser.WSEB004.Core.Models;
using Revit26_Plugin_Revised.WorksetsElementsBrowser.WSEB004.Infrastructure.ExternalEvents;
using Revit26_Plugin_Revised.WorksetsElementsBrowser.WSEB004.UI.Views;

namespace Revit26_Plugin_Revised.WorksetsElementsBrowser.WSEB004.UI.ViewModels
{
    public partial class WorksetsElementsBrowserViewModel : ObservableObject, IDisposable
    {
        private readonly UIDocument _uiDoc;
        private readonly ExternalEvent _event;
        private readonly WorksetsElementsBrowserHandler _handler;
        private readonly Dispatcher _dispatcher;
        private readonly IntPtr _mainWindowHandle;

        /// <summary>Set right before Raise() to tell OnHandlerRequestCompleted what follow-up to run once results land.</summary>
        private Action _pendingCompletion;

        public ObservableCollection<TreeNodeViewModel> RootNodes { get; } = new();

        [ObservableProperty] private string _documentTitle = string.Empty;
        [ObservableProperty] private string _searchText = string.Empty;
        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private string _summaryText = string.Empty;
        [ObservableProperty] private double _progress;   // ToolWindowShell status strip (CLAUDE.md v1.1)
        [ObservableProperty] private string _checkedSummaryText = "0 elements checked";
        [ObservableProperty] private bool _hideEmptyWorksets = true;

        /// <summary>Choices for the toolbar "Group by" combo box.</summary>
        public IReadOnlyList<GroupByOption> GroupByOptions { get; } = new[]
        {
            new GroupByOption(GroupByMode.CategoryThenType, "Category › Type"),
            new GroupByOption(GroupByMode.TypeThenCategory, "Type › Category"),
            new GroupByOption(GroupByMode.CategoryOnly, "Category only"),
            new GroupByOption(GroupByMode.TypeOnly, "Type only")
        };

        /// <summary>How rows under each workset are grouped. Defaults to the original Category › Type layout.</summary>
        [ObservableProperty] private GroupByMode _groupBy = GroupByMode.CategoryThenType;

        private List<WorksetNodeData> _loadedTree = new();

        partial void OnGroupByChanged(GroupByMode value)
        {
            // Pure in-memory re-shape of data already loaded — no Revit round trip needed.
            if (_loadedTree.Count > 0) RebuildTree();
        }

        public WorksetsElementsBrowserViewModel(UIDocument uiDoc, IntPtr mainWindowHandle)
        {
            _uiDoc = uiDoc;
            _mainWindowHandle = mainWindowHandle;
            _dispatcher = Dispatcher.CurrentDispatcher;

            _handler = new WorksetsElementsBrowserHandler(uiDoc);
            _handler.RequestCompleted += OnHandlerRequestCompleted;
            _event = ExternalEvent.Create(_handler);

            DocumentTitle = uiDoc.Document.Title;

            RunLoadTree();
        }

        partial void OnSearchTextChanged(string value)
        {
            foreach (var root in RootNodes)
                root.ApplyFilter(value ?? string.Empty);
        }

        partial void OnHideEmptyWorksetsChanged(bool value)
        {
            ApplyEmptyWorksetsVisibility(value);
        }

        private void ApplyEmptyWorksetsVisibility(bool hideEmpty)
        {
            foreach (var root in RootNodes)
            {
                // A closed workset lists no elements only because it is not loaded: keep it visible so it can be seen as closed.
                if (root.Kind == ElementTreeNodeKind.Workset && root.Children.Count == 0 && !root.IsClosed)
                    root.IsVisible = !hideEmpty;
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Load
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Single choke point for every Raise() in this ViewModel. Guards
        /// against a second request starting while one is still in flight
        /// (e.g. a double-click on an action button) — without this, the
        /// second call would overwrite _handler's input properties and
        /// _pendingCompletion out from under the first request.
        /// </summary>
        private void RaiseRequest(WorksetsElementsBrowserRequest request, Action onCompleted)
        {
            if (IsBusy)
            {
                Infrastructure.ExternalEvents.WsebDebugLog.Write($"RaiseRequest({request}) SKIPPED: already IsBusy");
                return;
            }
            IsBusy = true;
            _handler.Request = request;
            _pendingCompletion = onCompleted;
            var raiseResult = _event.Raise();
            Infrastructure.ExternalEvents.WsebDebugLog.Write($"RaiseRequest({request}) -> Raise() returned {raiseResult}");
        }

        private void RunLoadTree() => RaiseRequest(WorksetsElementsBrowserRequest.LoadTree, HandleLoadTreeCompleted);

        private void HandleLoadTreeCompleted()
        {
            RootNodes.Clear();

            if (!_handler.LastRunSucceeded)
            {
                TaskDialog.Show("Worksets & Elements Browser", $"Could not load worksets and elements: {_handler.ErrorMessage}");
                return;
            }

            _loadedTree = _handler.LoadedTree;
            RebuildTree();
        }

        /// <summary>
        /// Builds the tree from <see cref="_loadedTree"/> in the current <see cref="GroupBy"/> mode.
        /// Leaf rows (the lowest level) always carry the element ids; parents derive theirs from the leaves.
        /// Check state is reset when the grouping changes, as rows no longer map one-to-one.
        /// </summary>
        private void RebuildTree()
        {
            RootNodes.Clear();

            int worksetCount = 0, groupCount = 0, elementCount = 0;
            var categories = new HashSet<string>();
            var types = new HashSet<string>();

            foreach (var worksetData in _loadedTree)
            {
                worksetCount++;

                // Flatten to (category, type, ids) rows, then regroup per mode.
                var rows = worksetData.Categories
                    .SelectMany(c => c.Types.Select(t => (Category: c.Name, Type: t.Name, Ids: t.ElementIds)))
                    .ToList();

                int worksetElementCount = rows.Sum(r => r.Ids.Count);
                elementCount += worksetElementCount;
                foreach (var r in rows) { categories.Add(r.Category); types.Add(r.Type); }

                var worksetNode = new TreeNodeViewModel(
                    ElementTreeNodeKind.Workset,
                    worksetData.Name,
                    elementCount: worksetElementCount,
                    editableBadge: worksetData.IsEditable ? "editable" : "not editable",
                    owner: worksetData.Owner,
                    isOpen: worksetData.IsOpen)
                {
                    IsExpanded = false
                };
                worksetNode.CheckedChanged += RefreshCheckedSummary;

                switch (GroupBy)
                {
                    case GroupByMode.CategoryThenType:
                        AddTwoLevel(worksetNode, rows, r => r.Category, ElementTreeNodeKind.Category, r => r.Type, ElementTreeNodeKind.Type, ref groupCount);
                        break;
                    case GroupByMode.TypeThenCategory:
                        AddTwoLevel(worksetNode, rows, r => r.Type, ElementTreeNodeKind.Type, r => r.Category, ElementTreeNodeKind.Category, ref groupCount);
                        break;
                    case GroupByMode.CategoryOnly:
                        AddOneLevel(worksetNode, rows, r => r.Category, ElementTreeNodeKind.Category, ref groupCount);
                        break;
                    case GroupByMode.TypeOnly:
                        AddOneLevel(worksetNode, rows, r => r.Type, ElementTreeNodeKind.Type, ref groupCount);
                        break;
                }

                RootNodes.Add(worksetNode);
            }

            int closedCount = _loadedTree.Count(w => !w.IsOpen);
            SummaryText = $"{worksetCount} workset{(worksetCount == 1 ? "" : "s")}{(closedCount > 0 ? $" ({closedCount} closed)" : "")} · " +
                           $"{categories.Count} categor{(categories.Count == 1 ? "y" : "ies")} · " +
                           $"{types.Count} type{(types.Count == 1 ? "" : "s")} · " +
                           $"{elementCount:N0} elements";

            if (!string.IsNullOrEmpty(SearchText)) OnSearchTextChanged(SearchText);
            ApplyEmptyWorksetsVisibility(HideEmptyWorksets);
            RefreshCheckedSummary();
        }

        private static void AddOneLevel(
            TreeNodeViewModel parent,
            List<(string Category, string Type, List<ElementId> Ids)> rows,
            Func<(string Category, string Type, List<ElementId> Ids), string> key,
            ElementTreeNodeKind kind,
            ref int groupCount)
        {
            foreach (var g in rows.GroupBy(key).OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                var ids = g.SelectMany(r => r.Ids).ToList();
                parent.AddChild(new TreeNodeViewModel(kind, g.Key, elementCount: ids.Count, elementIds: ids));
                groupCount++;
            }
        }

        private static void AddTwoLevel(
            TreeNodeViewModel parent,
            List<(string Category, string Type, List<ElementId> Ids)> rows,
            Func<(string Category, string Type, List<ElementId> Ids), string> outerKey,
            ElementTreeNodeKind outerKind,
            Func<(string Category, string Type, List<ElementId> Ids), string> innerKey,
            ElementTreeNodeKind innerKind,
            ref int groupCount)
        {
            foreach (var outer in rows.GroupBy(outerKey).OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                var outerNode = new TreeNodeViewModel(outerKind, outer.Key, elementCount: outer.Sum(r => r.Ids.Count));
                parent.AddChild(outerNode);
                groupCount++;

                foreach (var inner in outer.GroupBy(innerKey).OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
                {
                    var ids = inner.SelectMany(r => r.Ids).ToList();
                    outerNode.AddChild(new TreeNodeViewModel(innerKind, inner.Key, elementCount: ids.Count, elementIds: ids));
                }
            }
        }

        private void RefreshCheckedSummary()
        {
            var ids = CollectCheckedElementIds().ToList();
            int rowCount = RootNodes.SelectMany(r => r.SelfAndDescendants()).Count(n => n.IsLeaf && n.ElementIds.Count > 0 && n.IsChecked == true);
            CheckedSummaryText = $"{rowCount} row{(rowCount == 1 ? "" : "s")} checked · {ids.Count:N0} elements";
        }

        private List<ElementId> CollectCheckedElementIds()
            => RootNodes.SelectMany(r => r.CollectCheckedElementIds()).Distinct().ToList();

        // ─────────────────────────────────────────────────────────────
        // Toolbar commands
        // ─────────────────────────────────────────────────────────────
        [RelayCommand]
        private void ExpandAll() => SetExpandedRecursive(RootNodes, true);

        [RelayCommand]
        private void CollapseAll() => SetExpandedRecursive(RootNodes, false);

        [RelayCommand]
        private void CheckAll()
        {
            foreach (var root in RootNodes) root.IsChecked = true;
        }

        [RelayCommand]
        private void ClearChecks()
        {
            foreach (var root in RootNodes) root.IsChecked = false;
        }

        private static void SetExpandedRecursive(IEnumerable<TreeNodeViewModel> nodes, bool expanded)
        {
            foreach (var node in nodes)
            {
                if (node.Children.Count > 0)
                    node.IsExpanded = expanded;
                SetExpandedRecursive(node.Children, expanded);
            }
        }

        /// <summary>
        /// Opens Revit's own Worksets dialog, where worksets are opened and closed. The browser stays
        /// open; press Reload afterwards to list the elements of the worksets that are now open.
        /// </summary>
        [RelayCommand]
        private void OpenWorksetsDialog()
        {
            RaiseRequest(WorksetsElementsBrowserRequest.OpenWorksetsDialog, () =>
            {
                if (!_handler.LastRunSucceeded)
                    TaskDialog.Show("Worksets & Elements Browser", $"Could not open the Worksets dialog: {_handler.ErrorMessage}");
            });
        }

        /// <summary>Reads the worksets and elements from the model again (checks are cleared).</summary>
        [RelayCommand]
        private void Reload() => RunLoadTree();

        // ─────────────────────────────────────────────────────────────
        // Checked-rows actions
        // ─────────────────────────────────────────────────────────────
        [RelayCommand]
        private void IsolateChecked() => RunCheckedAction(ElementActionMode.Isolate);

        [RelayCommand]
        private void SelectChecked() => RunCheckedAction(ElementActionMode.Select);

        [RelayCommand]
        private void IsolateAndSelectChecked() => RunCheckedAction(ElementActionMode.IsolateAndSelect);

        /// <summary>The dedicated toolbar action requested alongside the three checked-rows buttons — always Isolate + Select, the "just show me everything checked" one-click case.</summary>
        [RelayCommand]
        private void ShowCheckedIn3D() => RunCheckedAction(ElementActionMode.IsolateAndSelect);

        private void RunCheckedAction(ElementActionMode mode)
        {
            var ids = CollectCheckedElementIds();
            if (ids.Count == 0)
            {
                TaskDialog.Show("Worksets & Elements Browser", "No elements are checked. Check one or more types first.");
                return;
            }
            ShowInThreeD($"Show all {ids.Count:N0} checked element(s) in a 3D view?", ids, mode);
        }

        /// <summary>Invoked from the View's code-behind when the user clicks a Type row's name, or a row's "Show in 3D" button. Acts on every element under the row.</summary>
        public void OnTypeRowClicked(TreeNodeViewModel node)
        {
            var ids = node.CollectAllElementIds().Distinct().ToList();
            if (ids.Count == 0) return;

            string prompt = $"Show all {ids.Count:N0} elements of \"{node.Name}\" in a 3D view?";
            var td = new TaskDialog("Worksets & Elements Browser");
            td.MainContent = prompt;
            td.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;
            if (td.Show() != TaskDialogResult.Yes) return;

            ShowInThreeD(prompt, ids, ElementActionMode.IsolateAndSelect);
        }

        /// <summary>Selects, in the Revit model, every element under the given row (Category or Type) — no 3D view needed.</summary>
        public void SelectRow(TreeNodeViewModel node)
        {
            var ids = node.CollectAllElementIds().Distinct().ToList();
            if (ids.Count == 0) return;
            SelectInRevit(ids);
        }

        /// <summary>Checks every row of the same kind and name in all worksets (e.g. category "Walls" everywhere).</summary>
        public void CheckSameInAllWorksets(TreeNodeViewModel node)
        {
            if (node.Kind == ElementTreeNodeKind.Workset) return;
            foreach (var n in RootNodes.SelectMany(r => r.SelfAndDescendants()))
                if (n.Kind == node.Kind && n.Name == node.Name)
                    n.IsChecked = true;
        }

        /// <summary>Checks every leaf row currently visible under the search filter.</summary>
        [RelayCommand]
        private void CheckFiltered()
        {
            foreach (var root in RootNodes)
                CheckVisibleLeaves(root);
        }

        private static void CheckVisibleLeaves(TreeNodeViewModel node)
        {
            if (!node.IsVisible) return;
            if (node.IsLeaf)
            {
                if (node.ElementIds.Count > 0) node.IsChecked = true;
                return;
            }
            foreach (var c in node.Children) CheckVisibleLeaves(c);
        }

        /// <summary>
        /// Loads the model's 3D views, lets the user pick one, then applies
        /// Isolate/Select/Both to it. Two Revit-API round trips chained
        /// through RaiseRequest — never call ExternalEvent.Raise() directly
        /// from inside a completion callback (see OnHandlerRequestCompleted).
        /// </summary>
        private void ShowInThreeD(string prompt, List<ElementId> ids, ElementActionMode mode)
        {
            RaiseRequest(WorksetsElementsBrowserRequest.LoadViews, () =>
            {
                if (!_handler.LastRunSucceeded)
                {
                    TaskDialog.Show("Worksets & Elements Browser", $"Could not load 3D views: {_handler.ErrorMessage}");
                    return;
                }

                if (_handler.LoadedViews.Count == 0)
                {
                    TaskDialog.Show("Worksets & Elements Browser", "This model has no 3D views to show elements in.");
                    return;
                }

                var picker = new View3DPickerWindow();
                new System.Windows.Interop.WindowInteropHelper(picker).Owner = _mainWindowHandle;
                picker.Initialize(prompt, _handler.LoadedViews);
                bool? dialogResult = picker.ShowDialog();
                Infrastructure.ExternalEvents.WsebDebugLog.Write($"Picker closed: dialogResult={dialogResult}, SelectedView={picker.SelectedView?.Name}");
                if (dialogResult != true || picker.SelectedView == null) return;

                _handler.ActionElementIds = ids;
                _handler.TargetViewId = picker.SelectedView.ViewId;
                _handler.ActionMode = mode;
                Infrastructure.ExternalEvents.WsebDebugLog.Write($"About to RaiseRequest(ApplyAction), IsBusy={IsBusy}, ids.Count={ids.Count}, mode={mode}");
                RaiseRequest(WorksetsElementsBrowserRequest.ApplyAction, HandleApplyActionCompleted);
            });
        }

        private void SelectInRevit(List<ElementId> ids)
        {
            _handler.ActionElementIds = ids;
            RaiseRequest(WorksetsElementsBrowserRequest.SelectElements, () =>
            {
                if (!_handler.LastRunSucceeded)
                    TaskDialog.Show("Worksets & Elements Browser", $"Could not select the elements: {_handler.ErrorMessage}");
            });
        }

        // ─────────────────────────────────────────────────────────────
        // Save as Revit selection set
        // ─────────────────────────────────────────────────────────────

        /// <summary>Saves every checked element as a Revit selection set (Manage ▸ Selection Sets).</summary>
        [RelayCommand]
        private void SaveSelectionSet()
        {
            var ids = CollectCheckedElementIds();
            if (ids.Count == 0)
            {
                TaskDialog.Show("Worksets & Elements Browser", "No elements are checked. Check one or more rows first.");
                return;
            }

            var dialog = new SelectionSetNameWindow();
            new System.Windows.Interop.WindowInteropHelper(dialog).Owner = _mainWindowHandle;
            dialog.Initialize($"Save {ids.Count:N0} checked element(s) as a Revit selection set.", SuggestSelectionSetName());
            if (dialog.ShowDialog() != true) return;

            RunSaveSelectionSet(ids, dialog.SetName, overwrite: false);
        }

        private void RunSaveSelectionSet(List<ElementId> ids, string name, bool overwrite)
        {
            _handler.ActionElementIds = ids;
            _handler.SelectionSetName = name;
            _handler.OverwriteExisting = overwrite;
            RaiseRequest(WorksetsElementsBrowserRequest.SaveSelectionSet, () =>
            {
                if (!_handler.LastRunSucceeded)
                {
                    TaskDialog.Show("Worksets & Elements Browser", $"Could not save the selection set: {_handler.ErrorMessage}");
                    return;
                }

                switch (_handler.SelectionSetResult)
                {
                    case SelectionSetOutcome.NameExists:
                        var td = new TaskDialog("Worksets & Elements Browser")
                        {
                            MainContent = $"A selection set named \"{name}\" already exists. Replace its contents?",
                            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No
                        };
                        if (td.Show() == TaskDialogResult.Yes)
                            RunSaveSelectionSet(ids, name, overwrite: true);
                        break;
                    case SelectionSetOutcome.Created:
                    case SelectionSetOutcome.Updated:
                        string verb = _handler.SelectionSetResult == SelectionSetOutcome.Created ? "Saved" : "Updated";
                        TaskDialog.Show("Worksets & Elements Browser",
                            $"{verb} selection set \"{name}\" with {_handler.SelectionSetElementCount:N0} element(s).\n\n" +
                            "Find it under Manage ▸ Selection ▸ Edit/Load Selection Sets.");
                        break;
                }
            });
        }

        /// <summary>Default name: "Workset - Row" when one leaf row under one workset is checked, else "Workset selection" / "Browser selection".</summary>
        private string SuggestSelectionSetName()
        {
            var checkedRoots = RootNodes.Where(r => r.IsChecked != false).ToList();
            if (checkedRoots.Count != 1) return "Browser selection";

            var leaves = checkedRoots[0].SelfAndDescendants()
                .Where(n => n.IsLeaf && n.ElementIds.Count > 0 && n.IsChecked == true).ToList();
            return leaves.Count == 1
                ? $"{checkedRoots[0].Name} - {leaves[0].Name}"
                : $"{checkedRoots[0].Name} selection";
        }

        private void HandleApplyActionCompleted()
        {
            Infrastructure.ExternalEvents.WsebDebugLog.Write($"HandleApplyActionCompleted: LastRunSucceeded={_handler.LastRunSucceeded}, ErrorMessage={_handler.ErrorMessage}");
            if (!_handler.LastRunSucceeded)
            {
                TaskDialog.Show("Worksets & Elements Browser", $"Could not show elements in the selected view: {_handler.ErrorMessage}");
            }
            // Window stays open per this project's window-lifecycle convention.
        }

        // ─────────────────────────────────────────────────────────────
        // Window close
        // ─────────────────────────────────────────────────────────────
        [RelayCommand]
        private void Close() => CloseRequested?.Invoke();

        public event Action CloseRequested;

        /// <summary>
        /// Fires from inside the handler's own Execute(UIApplication), on
        /// Revit's UI thread but still nested in that call stack — Revit
        /// denies/ignores ExternalEvent.Raise() called reentrantly like that,
        /// and a modal ShowDialog() opened from the same nested frame is
        /// unreliable too. BeginInvoke (not Invoke) defers the continuation
        /// — including any chained Raise() for a follow-up request and any
        /// ShowDialog() — to run only after Execute() has fully returned and
        /// control is back with Revit's own message loop. Without this, the
        /// LoadViews -> pick a view -> ApplyAction chain silently did
        /// nothing: views never populated and isolate/select never ran.
        /// </summary>
        private void OnHandlerRequestCompleted()
        {
            _dispatcher.BeginInvoke(new Action(() =>
            {
                IsBusy = false;
                var completion = _pendingCompletion;
                _pendingCompletion = null;
                completion?.Invoke();
            }));
        }

        public void Dispose()
        {
            _handler.RequestCompleted -= OnHandlerRequestCompleted;
            _event?.Dispose();
        }
    }
}
