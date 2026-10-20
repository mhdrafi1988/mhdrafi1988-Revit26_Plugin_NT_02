using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.TrackerId.V001.Core.Models;
using Revit26_Plugin_Revised.TrackerId.V001.Core.Services;
using Revit26_Plugin_Revised.TrackerId.V001.Infrastructure.Revit;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Data;

namespace Revit26_Plugin_Revised.TrackerId.V001.UI.ViewModels
{
    // Step 5: read the team's status file, write Tracker_Status / Progress / StatusDate into Revit, and
    // colour a view by status. Works on every element with a Tracker_ID, whatever is ticked in step 1.
    public partial class TrackerIdViewModel
    {
        private StatusFileResult _statusFile;
        private List<StatusPlanRow> _statusPlan;

        /// <summary>The status file (Excel or CSV) on SharePoint.</summary>
        [ObservableProperty] private string _statusFilePath = string.Empty;

        /// <summary>Preview rows: one per element with a Tracker_ID.</summary>
        [ObservableProperty] private ObservableCollection<StatusRowViewModel> _statusRows = new();

        /// <summary>Filtered view of <see cref="StatusRows"/>.</summary>
        [ObservableProperty] private ICollectionView _statusView;

        /// <summary>"All", "Set", "Unchanged", "NoRow" or "Locked".</summary>
        [ObservableProperty] private string _statusFilter = "All";

        /// <summary>Elements whose status will be written.</summary>
        [ObservableProperty] private int _statusUpdateCount;

        /// <summary>One line under the status grid.</summary>
        [ObservableProperty] private string _statusSummaryText = "Choose the status file and click Read.";

        /// <summary>Status → colour table used for views and the template drop-down.</summary>
        public ObservableCollection<StatusColourRowViewModel> StatusColourRows { get; } = new();

        private void InitStatus()
        {
            StatusFilePath = _settings.StatusFilePath ?? string.Empty;
            var table = _settings.StatusColours.Count > 0 ? _settings.StatusColours : StatusColours.Defaults();
            foreach (var c in StatusColours.Complete(table, Enumerable.Empty<string>()))
                StatusColourRows.Add(new StatusColourRowViewModel(c.Status, c.Color));
        }

        partial void OnStatusFilePathChanged(string value) => ReadStatusCommand.NotifyCanExecuteChanged();
        partial void OnStatusFilterChanged(string value) => StatusView?.Refresh();

        /// <summary>Picks a status filter chip.</summary>
        [RelayCommand]
        private void SetStatusFilter(string filter) => StatusFilter = filter ?? "All";

        /// <summary>Asks for the status file.</summary>
        [RelayCommand]
        private void BrowseStatusFile()
        {
            string picked = PickStatusFile?.Invoke(StatusFilePath);
            if (!string.IsNullOrWhiteSpace(picked)) StatusFilePath = picked;
        }

        /// <summary>Reads the status file and matches it to every element with a Tracker_ID.</summary>
        [RelayCommand(CanExecute = nameof(CanReadStatus))]
        private void ReadStatus()
        {
            string path = StatusFilePath?.Trim();
            Run("Reading the status file…", app =>
            {
                var doc = DocumentFor(app);
                if (doc == null) return;

                StatusFileResult file;
                try
                {
                    file = StatusFileReader.Read(path);
                }
                catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
                {
                    OnUi(() =>
                    {
                        StatusSummaryText = "Could not read the status file: " + ex.Message;
                        AddLog(LogLevel.Error, StatusSummaryText);
                    });
                    return;
                }

                var tracked = ModelReader.ReadSnapshots(doc, ModelReader.TrackedCategoryIds(doc), null,
                    Array.Empty<string>(), Array.Empty<string>(), null, ReadScope.WholeModel);
                var holders = ModelReader.ReadIdHolders(doc);
                var plan = StatusMerger.Plan(tracked, file);
                var notInModel = StatusMerger.IdsNotInModel(file, holders);

                OnUi(() => ShowStatusPlan(file, plan, notInModel, path));
            });
        }

        private bool CanReadStatus() => !IsRunning && !string.IsNullOrWhiteSpace(StatusFilePath);

        private void ShowStatusPlan(StatusFileResult file, List<StatusPlanRow> plan, List<string> notInModel, string path)
        {
            _statusFile = file;
            _statusPlan = plan;

            StatusRows = new ObservableCollection<StatusRowViewModel>(plan.Select(p => new StatusRowViewModel(p)));
            var view = CollectionViewSource.GetDefaultView(StatusRows);
            view.Filter = o => o is StatusRowViewModel r && (StatusFilter == "All" || r.Change.ToString() == StatusFilter);
            StatusView = view;

            StatusUpdateCount = plan.Count(p => p.Change == StatusChange.Set);
            int unchanged = plan.Count(p => p.Change == StatusChange.Unchanged);
            int noRow = plan.Count(p => p.Change == StatusChange.NoRow);
            int locked = plan.Count(p => p.Change == StatusChange.Locked);
            StatusSummaryText = $"{file.Records.Count} Tracker_ID(s) in the file · {StatusUpdateCount} to update · {unchanged} up to date · " +
                                $"{noRow} element(s) not in the file · {notInModel.Count} file ID(s) not in this model" +
                                (locked > 0 ? $" · {locked} locked" : string.Empty);

            AddLog(LogLevel.Info, $"Read {file.RowCount} row(s) from {Path.GetFileName(path)}: {StatusSummaryText}.");
            foreach (string w in file.Warnings.Take(10)) AddLog(LogLevel.Warning, w);
            if (file.Warnings.Count > 10) AddLog(LogLevel.Warning, $"…and {file.Warnings.Count - 10} more warning(s).");
            if (notInModel.Count > 0)
                AddLog(LogLevel.Warning, $"Not in this model: {string.Join(", ", notInModel.Take(8))}{(notInModel.Count > 8 ? " …" : string.Empty)}");
            if (plan.Count == 0)
                AddLog(LogLevel.Warning, "No element in this model has a Tracker_ID yet. Write IDs in step 3 first.");

            // Every status used in the file gets a colour.
            var completed = StatusColours.Complete(StatusColourRows.Select(r => r.ToModel()), file.Records.Values.Select(r => r.Status));
            foreach (var c in completed.Skip(StatusColourRows.Count))
                StatusColourRows.Add(new StatusColourRowViewModel(c.Status, c.Color));

            SummaryText = StatusSummaryText;
            SaveSettings();
            RefreshCommands();
        }

        /// <summary>Writes the status of the "to update" rows into Revit, in one transaction.</summary>
        [RelayCommand(CanExecute = nameof(CanImportStatus))]
        private void ImportStatus()
        {
            var plan = _statusPlan?.ToList();
            if (plan == null) return;

            if (!Confirm($"Write the status of {StatusUpdateCount} element(s) into Revit?\n\n" +
                         "• Tracker_Status, Tracker_Progress and Tracker_StatusDate are set from the file\n" +
                         "• Elements not listed in the file keep their status\n" +
                         "• Missing status parameters are added to the project\n\n" +
                         "Ctrl+Z in Revit undoes the whole import."))
                return;

            Run("Writing status…", app =>
            {
                var doc = DocumentFor(app);
                if (doc == null) return;

                var result = StatusWriter.Write(doc, plan);
                OnUi(() =>
                {
                    if (result.Setup != null)
                    {
                        if (result.Setup.Created.Count > 0) AddLog(LogLevel.Info, "Added to the project: " + string.Join(", ", result.Setup.Created));
                        foreach (var w in result.Setup.Warnings) AddLog(LogLevel.Warning, w);
                    }

                    if (result.Committed)
                    {
                        AddLog(LogLevel.Success, $"Wrote the status of {result.Written} element(s)." +
                                                 (result.Skipped > 0 ? $" Skipped {result.Skipped} locked element(s)." : string.Empty));
                        SummaryText = $"Status written to {result.Written} element(s).";
                        ReadStatus();
                    }
                    else
                    {
                        foreach (string e in result.Errors) AddLog(LogLevel.Error, e);
                        SummaryText = "Nothing was written: " + (result.Errors.FirstOrDefault() ?? "unknown problem.");
                    }
                });
            });
        }

        private bool CanImportStatus() => IsStep5 && !IsRunning && StatusUpdateCount > 0;

        // ── Colours ───────────────────────────────────────────────────────────

        /// <summary>Colours the active view by Tracker_Status.</summary>
        [RelayCommand(CanExecute = nameof(CanColour))]
        private void ColourActiveView() => Colour(use3DView: false);

        /// <summary>Colours the "Tracker - Status" 3D view (created when missing) and opens it.</summary>
        [RelayCommand(CanExecute = nameof(CanColour))]
        private void Colour3DView() => Colour(use3DView: true);

        private bool CanColour() => !IsRunning && StatusColourRows.Count > 0;

        private void Colour(bool use3DView)
        {
            var colours = StatusColours.Complete(StatusColourRows.Select(r => r.ToModel()), Enumerable.Empty<string>());
            Run("Colouring the view…", app =>
            {
                var doc = DocumentFor(app);
                if (doc == null) return;
                var uidoc = app.ActiveUIDocument;

                var result = StatusViewService.Apply(doc, uidoc.ActiveView, colours, use3DView);
                if (result.Committed && use3DView && doc.GetElement(new ElementId(result.ViewId)) is View view)
                {
                    try { uidoc.ActiveView = view; }
                    catch (Autodesk.Revit.Exceptions.ApplicationException) { /* the view stays coloured; the user opens it */ }
                }

                OnUi(() =>
                {
                    if (result.Committed)
                    {
                        AddLog(LogLevel.Success, $"Coloured view '{result.ViewName}' with {result.Filters} status filter(s)" +
                                                 (result.ViewCreated ? " (view created)." : "."));
                        SummaryText = $"View '{result.ViewName}' coloured by status.";
                        SaveSettings();
                    }
                    else
                    {
                        foreach (string e in result.Errors) AddLog(LogLevel.Error, e);
                        SummaryText = "View not coloured: " + (result.Errors.FirstOrDefault() ?? "unknown problem.");
                    }
                });
            });
        }

        /// <summary>Adds a row to the colour table.</summary>
        [RelayCommand]
        private void AddStatusColour()
        {
            var next = StatusColours.Complete(StatusColourRows.Select(r => r.ToModel()), new[] { "New status" }).Last();
            StatusColourRows.Add(new StatusColourRowViewModel(next.Status, next.Color));
            RefreshCommands();
        }

        /// <summary>Removes a row from the colour table.</summary>
        [RelayCommand]
        private void RemoveStatusColour(StatusColourRowViewModel row)
        {
            if (row != null) StatusColourRows.Remove(row);
            RefreshCommands();
        }

        /// <summary>Puts the default colours back, keeping statuses found in the file.</summary>
        [RelayCommand]
        private void ResetStatusColours()
        {
            var inFile = _statusFile?.Records.Values.Select(r => r.Status) ?? Enumerable.Empty<string>();
            StatusColourRows.Clear();
            foreach (var c in StatusColours.Complete(StatusColours.Defaults(), inFile))
                StatusColourRows.Add(new StatusColourRowViewModel(c.Status, c.Color));
            RefreshCommands();
        }
    }
}
