using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.BaseAndNearestLevel.V001.Core;
using Revit26_Plugin_Revised.BaseAndNearestLevel.V001.Infrastructure;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.BaseAndNearestLevel.V001.UI.ViewModels
{
    /// <summary>
    /// The window's commands: listing elements, reading the levels, the two actions that change the model,
    /// and the grid's helpers. Every Revit call goes through <see cref="RunJob"/>, which queues it on the
    /// API thread and reports a failure once, through the log and <see cref="ToolGuard.Report"/>.
    /// </summary>
    public sealed partial class ViewModel_BaseAndNearestLevel
    {
        // ── source ────────────────────────────────────────────────────────────

        [RelayCommand]
        private void LoadSelection() => RunJob(doc =>
        {
            var uidoc = new UIDocument(doc);
            ICollection<ElementId> ids = uidoc.Selection.GetElementIds();
            if (ids.Count == 0)
            {
                Log(LogLevel.Warning, "Nothing is selected in Revit. Select roofs, floors or walls, then press Load Selection.");
                return;
            }
            List<ElementListItem> items = ElementListService.FromIds(doc, ids, out int ignored);
            FillRows(items);
            SourceText = $"Revit selection · {items.Count} element(s)";
            Subtitle = $"{DocumentTitle} · Revit selection";
            Log(LogLevel.Info, ignored == 0
                ? $"Listed {items.Count} roof(s), floor(s) and wall(s) from the Revit selection."
                : $"Listed {items.Count} from the Revit selection. {ignored} selected element(s) are not a roof, floor or wall and were left out.");
        });

        [RelayCommand]
        private void LoadActiveView() => RunJob(doc =>
        {
            View view = new UIDocument(doc).ActiveView;
            if (!ElementListService.CanListFrom(view))
            {
                Log(LogLevel.Warning, $"“{view?.Name}” cannot list model elements. Open a plan, section, elevation or 3D view.");
                return;
            }
            ListFromView(doc, view);
        });

        [RelayCommand]
        private void LoadAll() => RunJob(doc =>
        {
            List<ElementListItem> items = ElementListService.List(doc, null);
            FillRows(items);
            SourceText = $"Whole host model · {items.Count} element(s)";
            Subtitle = $"{DocumentTitle} · whole model";
            Log(LogLevel.Info, $"Listed {items.Count} roof(s), floor(s) and wall(s) of the host model.");
        });

        private void ListFromView(Document doc, View view)
        {
            if (!ElementListService.CanListFrom(view))
            {
                FillRows(new List<ElementListItem>());
                SourceText = "No view to list from";
                Subtitle = DocumentTitle;
                Log(LogLevel.Warning, "The active view cannot list model elements. Use Load All Model Elements, or open a plan, section, elevation or 3D view.");
                return;
            }
            List<ElementListItem> items = ElementListService.List(doc, view);
            FillRows(items);
            SourceText = $"Active view · {view.Name} · {items.Count} element(s)";
            Subtitle = $"{DocumentTitle} · {view.Name}";
            Log(LogLevel.Info, $"Listed {items.Count} roof(s), floor(s) and wall(s) of “{view.Name}”.");
        }

        // ── read and check ────────────────────────────────────────────────────

        /// <summary>Reads the base level, the nearest level and the current values, then runs the pre-checks.</summary>
        [RelayCommand(CanExecute = nameof(CanRead))]
        private void ReadLevels() => ReadAndCheck(reread: true);

        private bool CanRead() => !IsRunning && Rows.Count > 0;

        [RelayCommand]
        private void Recheck() => ReadAndCheck(reread: false);

        private void ReadAndCheck(bool reread)
        {
            RunRequest request = BuildRequest();
            if (request.ElementIds.Count == 0)
            {
                Log(LogLevel.Warning, "No roof, floor or wall is ticked.");
                return;
            }

            RunJob(doc =>
            {
                FactsResult facts = TargetParameterService.GatherFacts(doc, request);
                List<CheckRow> checks = BaseAndNearestRules.EvaluateChecks(facts, request);
                ApplyFacts(facts, checks, request, reread);
            });
        }

        private void ApplyFacts(FactsResult facts, List<CheckRow> checks, RunRequest request, bool announce)
        {
            var byId = facts.Elements.ToDictionary(e => e.Id);
            foreach (ElementRowViewModel row in Rows)
            {
                if (byId.TryGetValue(row.Id, out ElementFacts e))
                    row.Apply(e, BaseAndNearestRules.EvaluateRow(e, request.Overwrite, request.ChoiceFor(row.Id)));
                else if (row.IsChecked)
                    row.Apply(null, new RowVerdict(RowState.Warning, "Not read · untick or reload the list"));
            }

            Checks.Clear();
            foreach (CheckRow check in checks) Checks.Add(check);
            ChecksSummary = BaseAndNearestRules.Summary(checks);
            ChecksBlocked = BaseAndNearestRules.AnyBlocked(checks);

            DocumentFacts d = facts.Document;
            LevelSummary = d.Candidates.Count == 0
                ? "No candidate level."
                : $"{d.Candidates.Count} candidate level(s)" +
                  (d.ExcludedLevels.Count > 0 ? $", {d.ExcludedLevels.Count} excluded by the filter" : string.Empty) + ".";

            foreach (SlotViewModel slot in Slots) slot.Describe(_allParameters);

            _levelsRead = true;
            UpdateMetrics();
            RowsView.Refresh();
            NotifyActions();

            if (!announce) return;
            Log(LogLevel.Info, $"Read {facts.Elements.Count} element(s). {LevelSummary} Rule: {NearestLevelCalculator.RuleLabel(request.Rule)}.");
            if (d.ExcludedLevels.Count > 0)
                Log(LogLevel.Info, "Levels excluded by “Building-storey levels only”: " + string.Join(", ", d.ExcludedLevels) + ".");
            if (ChangedCount > 0)
                Log(LogLevel.Info, $"{ChangedCount} element(s) sit nearer a level other than their base level.");
            foreach (CheckRow check in checks.Where(c => c.Result != CheckResult.Pass))
                Log(check.Result == CheckResult.Blocked ? LogLevel.Error : LogLevel.Warning, $"{check.Title} — {check.Detail}");
            Log(ChecksBlocked ? LogLevel.Error : LogLevel.Success, "Pre-checks finished — " + ChecksSummary + ".");
        }

        // ── action 1: write the four parameters ───────────────────────────────

        /// <summary>Writes the four parameters on every ready element.</summary>
        [RelayCommand(CanExecute = nameof(CanWrite))]
        private void WriteParameters()
        {
            RunRequest request = BuildRequest();
            int rewrites = Rows.Count(r => r.IsChecked && r.HasExistingValue && r.State == RowState.Ready);
            if (rewrites > 0 && ConfirmRewrite != null &&
                !ConfirmRewrite($"{rewrites} element(s) already hold a different value. Writing replaces those values. Go ahead?"))
            {
                Log(LogLevel.Info, "Write cancelled before anything was changed.");
                return;
            }

            RunJob(doc =>
            {
                RunResult result = TargetParameterService.WriteParameters(doc, request);
                ApplyRun(result, request, "Write Parameters");
            });
        }

        // ── action 2: re-host onto the nearest level ──────────────────────────

        /// <summary>
        /// Re-hosts the ticked elements onto their nearest level, after the confirmation dialog. The level
        /// parameter is set first and the offset second, so nothing moves.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanRehost))]
        private void Rehost()
        {
            var confirmation = RehostConfirmation.Build(
                Rows.Where(r => r.IsChecked && CategoryAllowed(r.Category)).Select(r => r.Facts),
                RehostShapeEdited);

            if (!confirmation.CanRun)
            {
                Log(LogLevel.Warning, "No element can be re-hosted. Every ticked element is already on its nearest level, or is skipped.");
                return;
            }

            if (ConfirmRehost != null)
            {
                RehostConfirmation answer = ConfirmRehost(confirmation);
                if (answer == null)
                {
                    Log(LogLevel.Info, "Re-host cancelled before anything was changed.");
                    return;
                }
                if (answer.IncludeShapeEdited != RehostShapeEdited) RehostShapeEdited = answer.IncludeShapeEdited;
            }

            RunRequest request = BuildRequest();
            RunJob(doc =>
            {
                RunResult result = RehostService.Run(doc, request);
                ApplyRun(result, request, "Re-host");
            });
        }

        // ── shared result handling ────────────────────────────────────────────

        private void ApplyRun(RunResult result, RunRequest request, string what)
        {
            foreach ((LogLevel level, string text) in result.Log) Log(level, text);

            if (result.NotStartedReason != null)
            {
                Log(LogLevel.Warning, $"{what} did not start — {result.NotStartedReason}");
                RunSummary = result.NotStartedReason;
                return;
            }

            var byId = result.Rows.ToDictionary(r => r.Id, r => r);
            foreach (ElementRowViewModel row in Rows)
                if (byId.TryGetValue(row.Id, out RunRowResult r)) row.ApplyResult(r);

            int assigned = result.Rows.Count(r => r.State == RowState.Assigned);
            int skipped = result.Rows.Count(r => r.State == RowState.Skipped);
            int failed = result.Rows.Count(r => r.State == RowState.Failed);

            RunSummary = what == "Re-host"
                ? $"Re-hosted {assigned} · skipped {skipped} · failed {failed}"
                : $"Written {assigned} · up to date {UpToDateCount} · skipped {skipped} · failed {failed} · {result.ValuesWritten} parameter value(s) set";
            Log(failed > 0 ? LogLevel.Warning : LogLevel.Success, $"{what} finished — {RunSummary}.");

            // Re-read so the grid, the metrics and the pre-checks describe the model as it is now.
            if (result.Facts != null)
            {
                List<CheckRow> checks = BaseAndNearestRules.EvaluateChecks(result.Facts, request);
                ApplyFacts(result.Facts, checks, request, announce: false);
            }
            UpdateMetrics();
        }

        // ── grid helpers ──────────────────────────────────────────────────────

        [RelayCommand]
        private void TickAll() => SetTicks(true);

        [RelayCommand]
        private void UntickAll() => SetTicks(false);

        private void SetTicks(bool ticked)
        {
            _bulkTick = true;
            try
            {
                foreach (ElementRowViewModel row in Rows.Where(r => CategoryAllowed(r.Category)))
                {
                    row.SuppressTickCallback = true;
                    row.IsChecked = ticked;
                    row.SuppressTickCallback = false;
                }
            }
            finally
            {
                _bulkTick = false;
            }
            UpdateMetrics();
            ScheduleRecheck();
        }

        /// <summary>Sets the "If value exists" choice on the highlighted rows, or on all of them when none is highlighted.</summary>
        [RelayCommand]
        private void SetChoice(string choice)
        {
            if (!Enum.TryParse(choice, out ExistingChoice wanted)) return;
            List<ElementRowViewModel> targets = SelectedRows is { Count: > 0 }
                ? SelectedRows
                : Rows.Where(r => CategoryAllowed(r.Category)).ToList();

            _bulkChoice = true;
            try
            {
                foreach (ElementRowViewModel row in targets)
                {
                    row.SuppressChoiceCallback = true;
                    row.Choice = wanted;
                    row.SuppressChoiceCallback = false;
                }
            }
            finally
            {
                _bulkChoice = false;
            }
            Log(LogLevel.Info, $"“If value exists” set to {wanted} on {targets.Count} row(s).");
            Reevaluate();
        }

        /// <summary>Shows only the rows behind one metric tile; the same tile again shows all of them.</summary>
        [RelayCommand]
        private void SetTileFilter(string tile)
        {
            _tileFilter = _tileFilter == tile ? string.Empty : tile ?? string.Empty;
            OnPropertyChanged(nameof(ActiveTile));
            RowsView.Refresh();
        }

        /// <summary>Which metric tile is filtering the grid, or an empty string.</summary>
        public string ActiveTile => _tileFilter;

        [RelayCommand]
        private void SelectInRevit() => RunJob(doc =>
        {
            List<ElementId> ids = TargetIds();
            if (ids.Count == 0)
            {
                Log(LogLevel.Warning, "Highlight rows in the grid first.");
                return;
            }
            new UIDocument(doc).Selection.SetElementIds(ids);
            Log(LogLevel.Info, $"Selected {ids.Count} element(s) in Revit.");
        });

        [RelayCommand]
        private void ZoomTo() => RunJob(doc =>
        {
            List<ElementId> ids = TargetIds();
            if (ids.Count == 0)
            {
                Log(LogLevel.Warning, "Highlight rows in the grid first.");
                return;
            }
            var uidoc = new UIDocument(doc);
            uidoc.Selection.SetElementIds(ids);
            uidoc.ShowElements(ids);
            Log(LogLevel.Info, $"Zoomed to {ids.Count} element(s).");
        });

        private List<ElementId> TargetIds() =>
            (SelectedRows is { Count: > 0 } ? SelectedRows : new List<ElementRowViewModel>())
            .Select(r => new ElementId(r.Id)).ToList();

        [RelayCommand]
        private void Clear()
        {
            foreach (ElementRowViewModel row in Rows) row.Reset();
            Checks.Clear();
            _levelsRead = false;
            ChecksSummary = "Press Read Levels to read the ticked elements and run the pre-checks.";
            ChecksBlocked = true;
            RunSummary = string.Empty;
            LevelSummary = string.Empty;
            UpdateMetrics();
            RowsView.Refresh();
            NotifyActions();
            Log(LogLevel.Info, "Cleared the read values. The element list is unchanged.");
        }

        /// <inheritdoc/>
        protected override void OnClearLog()
        {
            base.OnClearLog();
            RunSummary = string.Empty;
        }

        // ── New Parameter dialog ──────────────────────────────────────────────

        private void OpenNewParameterDialog(SlotViewModel slot)
        {
            if (ShowNewParameterDialog == null) return;

            string suggested = string.IsNullOrWhiteSpace(slot.Name) ? slot.SuggestedName : slot.Name.Trim();
            string file = !string.IsNullOrWhiteSpace(_settings?.SharedParamFilePath)
                ? _settings.SharedParamFilePath
                : _revitSharedParamFile;

            var dialogViewModel = new NewParameterViewModel(slot.Slot, suggested, file,
                _settings?.FileGroup ?? "Data", _allParameters.Select(p => p.Name));

            if (!ShowNewParameterDialog(dialogViewModel)) return;

            NewParameterSpec spec = dialogViewModel.ToSpec();
            if (spec == null) return;

            slot.Pending = spec;
            slot.Name = spec.Name;
            slot.Describe(_allParameters);

            _settings ??= new BaseAndNearestLevelSettings();
            _settings.SharedParamFilePath = spec.FilePath;
            _settings.FileGroup = spec.FileGroup;

            Log(LogLevel.Info, $"“{spec.Name}” ({TargetSlots.TypeName(spec.Slot)}) will be created for slot {TargetSlots.Number(spec.Slot)} when you write.");
            ScheduleRecheck();
        }

        // ── Revit thread ──────────────────────────────────────────────────────

        private void RunJob(Action<Document> job) => RunJob(job, null);

        private void RunJob(Action<Document> job, Action after)
        {
            if (IsRunning)
            {
                Log(LogLevel.Warning, "Another job is still running.");
                return;
            }
            IsRunning = true;
            _handler.Job = job;
            _handler.Completed = failure =>
            {
                IsRunning = false;
                if (failure is OperationCanceledException or Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    Log(LogLevel.Warning, "Cancelled by user. Model changes made by this run were undone.");
                    return;
                }
                if (failure != null)
                {
                    Log(LogLevel.Error, $"Failed and rolled back — {failure.Message}");
                    ToolGuard.Report(GetType(), failure);
                    return;
                }
                after?.Invoke();
            };

            ExternalEventRequest raised = _event.Raise();
            if (raised is not (ExternalEventRequest.Accepted or ExternalEventRequest.Pending))
            {
                IsRunning = false;
                Log(LogLevel.Error, $"Revit did not accept the request ({raised}). Click in the Revit window and try again.");
            }
        }
    }
}
