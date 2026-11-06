using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.ExportDwgToFolder.V004.Core.Models;

namespace Revit26_Plugin_Revised.ExportDwgToFolder.V004.UI.ViewModels
{
    /// <summary>One selectable action in a review row's Action combo.</summary>
    public class PostExportActionChoice
    {
        public PostExportActionChoice(PostExportAction value, string label)
        {
            Value = value;
            Label = label;
        }

        /// <summary>The action.</summary>
        public PostExportAction Value { get; }

        /// <summary>What the combo shows.</summary>
        public string Label { get; }

        public override string ToString() => Label;
    }

    /// <summary>
    /// One row of the post-export review grid: a successfully-exported CAD instance and what should
    /// happen to it in the model. NEW in V004.
    /// </summary>
    public partial class PostExportRowViewModel : ObservableObject
    {
        private readonly Action _changed;

        /// <summary>Wraps one exported grid row.</summary>
        /// <param name="source">The exported row from the main grid.</param>
        /// <param name="destinationPath">Full path of the DWG that was written.</param>
        /// <param name="route">How the file was produced, for the Result column.</param>
        /// <param name="changed">Called whenever Action or the Open tick changes.</param>
        public PostExportRowViewModel(DwgRowViewModel source, string destinationPath, string route, Action changed)
        {
            Source = source;
            _changed = changed;

            Name = source.Name;
            SourceGroup = source.SourceGroup;
            IsLink = source.Info.SourceType == DwgSourceType.Link;
            HostView = source.HostView;
            Sheet = source.Sheet;
            OutputFileName = source.OutputFileName;
            DestinationPath = destinationPath ?? string.Empty;
            SourceDescription = source.SourceDescription;
            HasSourcePath = source.HasSourcePath;
            ElementIdValue = source.ElementIdValue;
            Route = string.IsNullOrWhiteSpace(route) ? "Exported" : route;

            // Converting means re-importing the source file, so it needs a file that resolves.
            if (!IsLink)
            {
                CanConvert = false;
                ConvertBlockedReason = "Imports have no external file to re-import";
            }
            else if (string.IsNullOrWhiteSpace(source.Info.LinkedSourcePath))
            {
                CanConvert = false;
                ConvertBlockedReason = "Source path unresolved — nothing to re-import";
            }
            else
            {
                bool exists;
                try { exists = File.Exists(source.Info.LinkedSourcePath); }
                catch { exists = false; }
                CanConvert = exists;
                ConvertBlockedReason = exists ? string.Empty : "Source file not found — nothing to re-import";
            }

            // Re-linking points at the file this tool just wrote, so it only needs that file to
            // exist — which it does for every row in this grid.
            CanRelink = !string.IsNullOrWhiteSpace(DestinationPath);

            Choices = new List<PostExportActionChoice>
            {
                new PostExportActionChoice(PostExportAction.Keep, "Keep in model"),
                new PostExportActionChoice(PostExportAction.DeleteFromModel, "Delete from model")
            };
            if (CanConvert)
                Choices.Add(new PostExportActionChoice(PostExportAction.ConvertToImport, "Convert to import"));
            if (CanRelink)
                Choices.Add(new PostExportActionChoice(PostExportAction.ReplaceWithLinkToExported,
                    "Replace with link to exported file"));

            selectedChoice = Choices[0];
            resultText = Route;
        }

        /// <summary>The main-grid row this came from.</summary>
        public DwgRowViewModel Source { get; }

        /// <summary>Import symbol name.</summary>
        public string Name { get; }

        /// <summary>"Link" or "Import".</summary>
        public string SourceGroup { get; }

        /// <summary>True for a CAD link.</summary>
        public bool IsLink { get; }

        /// <summary>Host view name, or "–".</summary>
        public string HostView { get; }

        /// <summary>Sheet label.</summary>
        public string Sheet { get; }

        /// <summary>Output file name (no folder).</summary>
        public string OutputFileName { get; }

        /// <summary>Full path of the DWG written for this row.</summary>
        public string DestinationPath { get; }

        /// <summary>Source file path, or the "embedded in the model" statement for an import.</summary>
        public string SourceDescription { get; }

        /// <summary>True when <see cref="SourceDescription"/> is a real path.</summary>
        public bool HasSourcePath { get; }

        /// <summary>Revit element ID.</summary>
        public long ElementIdValue { get; }

        /// <summary>"copied source file" / "re-exported from view".</summary>
        public string Route { get; }

        /// <summary>Whether Convert to import is offered for this row.</summary>
        public bool CanConvert { get; }

        /// <summary>Why Convert is not offered; empty when it is.</summary>
        public string ConvertBlockedReason { get; }

        /// <summary>Whether "Replace with link to exported file" is offered for this row.</summary>
        public bool CanRelink { get; }

        /// <summary>
        /// Tooltip for the Action cell: why Convert is missing, or what re-linking will point at.
        /// </summary>
        public string ActionHint =>
            string.IsNullOrWhiteSpace(ConvertBlockedReason)
                ? $"Replace with link to exported file points this at {DestinationPath}"
                : $"{ConvertBlockedReason}. Replace with link to exported file points this at {DestinationPath}";

        /// <summary>The actions this row allows — Convert is absent when it can't be done.</summary>
        public List<PostExportActionChoice> Choices { get; }

        /// <summary>The chosen action.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ChosenAction))]
        private PostExportActionChoice selectedChoice;

        /// <summary>The chosen action's value.</summary>
        public PostExportAction ChosenAction => SelectedChoice?.Value ?? PostExportAction.Keep;

        /// <summary>Ticked to open this file after applying. Capped at five across the grid.</summary>
        [ObservableProperty] private bool isOpenSelected;

        /// <summary>Result / route text for the last column.</summary>
        [ObservableProperty] private string resultText = string.Empty;

        partial void OnSelectedChoiceChanged(PostExportActionChoice value) => _changed?.Invoke();
        partial void OnIsOpenSelectedChanged(bool value) => _changed?.Invoke();

        /// <summary>Sets the action without going through the combo (used by the bulk buttons).</summary>
        public void SetAction(PostExportAction action)
        {
            var choice = Choices.FirstOrDefault(c => c.Value == action);
            if (choice != null) SelectedChoice = choice;
        }

        /// <summary>Builds the request half of this row's decision.</summary>
        public PostExportDecision ToDecision() => new PostExportDecision
        {
            ElementId = Source.Info.ElementId,
            ElementIdValue = ElementIdValue,
            Name = Name,
            Action = ChosenAction,
            SourceType = Source.Info.SourceType,
            LinkedSourcePath = Source.Info.LinkedSourcePath,
            OwnerViewId = Source.Info.OwnerViewId,
            ExportedFilePath = DestinationPath,
            OpenAfter = IsOpenSelected
        };
    }

    /// <summary>
    /// The post-export review: one row per successfully-exported CAD instance, each with a Keep /
    /// Delete / Convert action, plus an Open tick capped at five files. NEW in V004 — it replaces
    /// V002/V003's three-command-link Revit <c>TaskDialog</c>, which could only choose between
    /// "all imports", "all links" and "everything".
    /// <para>
    /// Skipped and failed rows are never offered here; only rows whose DWG was actually written.
    /// </para>
    /// </summary>
    public partial class PostExportReviewViewModel : ObservableObject
    {
        /// <summary>
        /// How many exported files may be opened in one go. More than a handful of AutoCAD windows
        /// at once is never what anyone meant, so the cap is enforced on the tick itself rather than
        /// silently truncating the list at Open time.
        /// </summary>
        public const int MaxOpenFiles = 5;

        private readonly string _outputFolder;
        private bool _bulkSetting;

        /// <summary>Raised when the user has finished — true to apply, false to skip.</summary>
        public event EventHandler<bool> CloseRequested;

        /// <summary>Creates the review over the rows that exported successfully.</summary>
        /// <param name="exportedRows">Rows with <see cref="DwgRowStatus.Exported"/> status.</param>
        /// <param name="destinationPaths">Full written path per row, keyed by element id value.</param>
        /// <param name="routes">Export route per row, keyed by element id value.</param>
        /// <param name="outputFolder">Root output folder, for "Open output folder".</param>
        public PostExportReviewViewModel(
            IEnumerable<DwgRowViewModel> exportedRows,
            IDictionary<long, string> destinationPaths,
            IDictionary<long, string> routes,
            string outputFolder)
        {
            _outputFolder = outputFolder ?? string.Empty;

            var paths = destinationPaths ?? new Dictionary<long, string>();
            var routeMap = routes ?? new Dictionary<long, string>();

            foreach (var row in exportedRows ?? Enumerable.Empty<DwgRowViewModel>())
            {
                paths.TryGetValue(row.ElementIdValue, out string dest);
                routeMap.TryGetValue(row.ElementIdValue, out string route);
                Rows.Add(new PostExportRowViewModel(row, dest ?? row.DestinationPath, route, Recount));
            }

            Recount();
        }

        /// <summary>The reviewed rows.</summary>
        public ObservableCollection<PostExportRowViewModel> Rows { get; } =
            new ObservableCollection<PostExportRowViewModel>();

        /// <summary>Total rows under review.</summary>
        public int ExportedCount => Rows.Count;

        /// <summary>Rows set to delete.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ApplyNote))]
        private int deleteCount;

        /// <summary>Rows set to convert from link to import.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ApplyNote))]
        private int convertCount;

        /// <summary>Rows set to be replaced with a link to their exported file.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ApplyNote))]
        private int relinkCount;

        /// <summary>Rows left alone.</summary>
        [ObservableProperty] private int keepCount;

        /// <summary>Files ticked to open.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(OpenButtonText), nameof(OpenCountText))]
        private int openSelectedCount;

        /// <summary>Shown when a sixth Open tick was refused.</summary>
        [ObservableProperty] private bool showOpenLimitNote;

        /// <summary>"Open: 3 of 5 selected".</summary>
        public string OpenCountText => $"Open: {OpenSelectedCount} of {MaxOpenFiles} selected";

        /// <summary>Open button caption.</summary>
        public string OpenButtonText => OpenSelectedCount == 0
            ? "Open files"
            : $"Open {OpenSelectedCount} file{(OpenSelectedCount == 1 ? "" : "s")}";

        /// <summary>Footer line stating exactly what Apply will do.</summary>
        public string ApplyNote
        {
            get
            {
                var parts = new List<string>();
                if (DeleteCount > 0) parts.Add($"{DeleteCount:N0} to delete");
                if (ConvertCount > 0) parts.Add($"{ConvertCount:N0} to convert to import");
                if (RelinkCount > 0) parts.Add($"{RelinkCount:N0} to re-link");
                return parts.Count == 0
                    ? "Nothing to apply — every row is set to Keep."
                    : string.Join(" · ", parts) + " · one Ctrl+Z undoes it all.";
            }
        }

        /// <summary>Header line.</summary>
        public string HeaderText =>
            $"{ExportedCount:N0} CAD instance(s) exported — decide what happens in the model";

        // ── Commands ─────────────────────────────────────────────────────────

        /// <summary>Sets every row to Keep.</summary>
        [RelayCommand]
        private void SetAllKeep() => SetAll(PostExportAction.Keep);

        /// <summary>Sets every row to Delete.</summary>
        [RelayCommand]
        private void SetAllDelete() => SetAll(PostExportAction.DeleteFromModel);

        /// <summary>Sets every convertible row to Convert; leaves the rest as they are.</summary>
        [RelayCommand]
        private void SetAllConvert() => SetAll(PostExportAction.ConvertToImport);

        /// <summary>Sets every row to be replaced with a link to its exported file.</summary>
        [RelayCommand]
        private void SetAllRelink() => SetAll(PostExportAction.ReplaceWithLinkToExported);

        /// <summary>Opens the ticked files in whatever application owns .dwg on this machine.</summary>
        [RelayCommand(CanExecute = nameof(CanOpenSelected))]
        private void OpenSelectedFiles()
        {
            foreach (var row in Rows.Where(r => r.IsOpenSelected).Take(MaxOpenFiles))
            {
                if (TryOpen(row.DestinationPath, out string error))
                    row.ResultText = "Opened";
                else
                    row.ResultText = $"Could not open — {error}";
            }
        }

        private bool CanOpenSelected() => OpenSelectedCount > 0;

        /// <summary>Opens the root output folder in Explorer.</summary>
        [RelayCommand]
        private void OpenOutputFolder()
        {
            if (string.IsNullOrWhiteSpace(_outputFolder)) return;
            TryOpen(_outputFolder, out _);
        }

        /// <summary>Opens Explorer with one row's file selected.</summary>
        [RelayCommand]
        private void ShowInFolder(PostExportRowViewModel row)
        {
            if (row == null || string.IsNullOrWhiteSpace(row.DestinationPath)) return;
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{row.DestinationPath}\"")
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                row.ResultText = $"Could not show in folder — {ex.Message}";
            }
        }

        /// <summary>Apply the chosen actions and close.</summary>
        [RelayCommand]
        private void Apply() => CloseRequested?.Invoke(this, true);

        /// <summary>Leave the model alone and close.</summary>
        [RelayCommand]
        private void SkipAll() => CloseRequested?.Invoke(this, false);

        // ── Results ──────────────────────────────────────────────────────────

        /// <summary>The decisions to hand to the external event handler.</summary>
        public List<PostExportDecision> BuildDecisions() =>
            Rows.Select(r => r.ToDecision()).ToList();

        /// <summary>Files the user ticked to open, capped.</summary>
        public List<string> FilesToOpen() =>
            Rows.Where(r => r.IsOpenSelected)
                .Take(MaxOpenFiles)
                .Select(r => r.DestinationPath)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();

        // ── Helpers ──────────────────────────────────────────────────────────

        private void SetAll(PostExportAction action)
        {
            _bulkSetting = true;
            foreach (var row in Rows)
            {
                // Convert is only legal where the source file resolves, and re-linking needs the
                // exported file. A row that can't take the action keeps whatever it already had
                // rather than being quietly switched to something else.
                if (action == PostExportAction.ConvertToImport && !row.CanConvert) continue;
                if (action == PostExportAction.ReplaceWithLinkToExported && !row.CanRelink) continue;
                row.SetAction(action);
            }
            _bulkSetting = false;
            Recount();
        }

        /// <summary>
        /// Recounts the action tiles and enforces the Open cap. A tick that would make it six is
        /// refused — reverted, with a visible note — rather than being accepted and truncated later.
        /// </summary>
        private void Recount()
        {
            if (_bulkSetting) return;

            int open = 0;
            foreach (var row in Rows)
            {
                if (!row.IsOpenSelected) continue;
                open++;
                if (open > MaxOpenFiles)
                {
                    // Revert without re-entering this method.
                    _bulkSetting = true;
                    row.IsOpenSelected = false;
                    _bulkSetting = false;
                    open--;
                    ShowOpenLimitNote = true;
                }
            }

            if (open < MaxOpenFiles) ShowOpenLimitNote = false;

            OpenSelectedCount = open;
            DeleteCount = Rows.Count(r => r.ChosenAction == PostExportAction.DeleteFromModel);
            ConvertCount = Rows.Count(r => r.ChosenAction == PostExportAction.ConvertToImport);
            RelinkCount = Rows.Count(r => r.ChosenAction == PostExportAction.ReplaceWithLinkToExported);
            KeepCount = Rows.Count(r => r.ChosenAction == PostExportAction.Keep);

            ApplyCommand.NotifyCanExecuteChanged();
            OpenSelectedFilesCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(ApplyNote));
        }

        /// <summary>
        /// Opens one path with its registered application. Every failure is per-file, so one missing
        /// DWG never stops the rest from opening.
        /// </summary>
        private static bool TryOpen(string path, out string error)
        {
            error = null;
            try
            {
                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    error = "file no longer exists";
                    return false;
                }

                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
