using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.TrackerId.V001.Core.Models;
using Revit26_Plugin_Revised.TrackerId.V001.Core.Services;
using Revit26_Plugin_Revised.TrackerId.V001.Infrastructure.Revit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Revit26_Plugin_Revised.TrackerId.V001.UI.ViewModels
{
    // Step 4: the CSV export Power BI reads.
    public partial class TrackerIdViewModel
    {
        /// <summary>CSV file to write.</summary>
        [ObservableProperty] private string _exportPath = string.Empty;

        /// <summary>True: every element with a Tracker_ID. False: only the chosen types.</summary>
        [ObservableProperty] private bool _exportWholeModel = true;

        /// <summary>Also save a copy with today's date in its name.</summary>
        [ObservableProperty] private bool _exportDatedCopy;

        /// <summary>Result of the last export.</summary>
        [ObservableProperty] private string _lastExportText = "Not exported yet in this session.";

        /// <summary>The fixed column list, shown so the Power BI model can be set up before the first export.</summary>
        public string ExportColumnsText => string.Join(", ", CsvExportService.Columns);

        private void InitExport()
        {
            ExportPath = string.IsNullOrWhiteSpace(_settings.ExportPath)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Tracker", "Tracker_Elements.csv")
                : _settings.ExportPath;
            ExportWholeModel = _settings.ExportWholeModel;
            ExportDatedCopy = _settings.ExportDatedCopy;
        }

        partial void OnExportPathChanged(string value) => ExportCommand.NotifyCanExecuteChanged();
        partial void OnExportWholeModelChanged(bool value) => ExportCommand.NotifyCanExecuteChanged();

        /// <summary>Asks for the CSV file.</summary>
        [RelayCommand]
        private void BrowseExport()
        {
            string picked = PickExportPath?.Invoke(ExportPath);
            if (!string.IsNullOrWhiteSpace(picked)) ExportPath = picked;
        }

        /// <summary>Reads the Tracker parameters and writes the CSV.</summary>
        [RelayCommand(CanExecute = nameof(CanExport))]
        private void Export()
        {
            string path = ExportPath?.Trim();
            bool dated = ExportDatedCopy;
            var modelSegment = Segments.FirstOrDefault(s => s.Kind == SegmentKind.Model)?.ToDefinition();
            var projectParameters = NeededParameterNames().Project;
            List<long> ids = ExportWholeModel ? null : _snapshots?.Select(s => s.ElementId).ToList();
            if (!ExportWholeModel && ids == null)
            {
                AddLog(LogLevel.Warning, "The chosen elements are still being read. Try again in a moment.");
                return;
            }

            Run("Exporting…", app =>
            {
                var doc = DocumentFor(app);
                if (doc == null) return;

                var rows = ModelReader.ReadExportRows(doc, ids);
                string modelCode = TrackerIdPlanner.ModelCode(modelSegment, ModelReader.ReadModelIdentity(doc, projectParameters), out _);
                try
                {
                    var written = CsvExportService.Write(path, rows, doc.Title, modelCode, DateTime.Now, dated);
                    OnUi(() =>
                    {
                        LastExportText = $"{rows.Count} row(s) written at {DateTime.Now:HH:mm} to {string.Join(" and ", written)}";
                        SummaryText = $"Exported {rows.Count} row(s).";
                        AddLog(LogLevel.Success, LastExportText);
                        if (rows.Count == 0)
                            AddLog(LogLevel.Warning, "No element has a Tracker_ID yet, so the file only has its header. Write IDs in step 3 first.");
                        SaveSettings();
                    });
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
                {
                    OnUi(() =>
                    {
                        SummaryText = "Export failed: " + ex.Message;
                        AddLog(LogLevel.Error, SummaryText);
                    });
                }
            });
        }

        private bool CanExport() =>
            IsStep4 && !IsRunning && !string.IsNullOrWhiteSpace(ExportPath) && (ExportWholeModel || _snapshots != null);
    }
}
