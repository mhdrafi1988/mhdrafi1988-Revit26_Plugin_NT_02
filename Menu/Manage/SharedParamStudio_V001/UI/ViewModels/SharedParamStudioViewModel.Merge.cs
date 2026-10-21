using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.SharedParamStudio.V001.Core;

namespace Revit26_Plugin_Revised.SharedParamStudio.V001.UI.ViewModels
{
    public sealed partial class SharedParamStudioViewModel
    {
        // ── Tab 2: Merge Files ────────────────────────────────────────────────

        [ObservableProperty] private string _mergeFirstPath = string.Empty;
        [ObservableProperty] private string _mergeSecondPath = string.Empty;
        [ObservableProperty] private string _mergeOutputPath = string.Empty;
        [ObservableProperty] private PolicyOption _selectedPolicy;
        [ObservableProperty] private NewParamRow _selectedNewParam;
        [ObservableProperty] private string _mergePreviewText = "Pick two files and press Preview to see what a merge would do.";

        /// <summary>What to do when the files disagree about a parameter.</summary>
        public IReadOnlyList<PolicyOption> PolicyOptions { get; } = new[]
        {
            new PolicyOption { Label = "Keep the first file's version", Value = ConflictPolicy.KeepFirst },
            new PolicyOption { Label = "Use the second file's version", Value = ConflictPolicy.KeepSecond },
            new PolicyOption { Label = "Keep both (rename the second's copy)", Value = ConflictPolicy.KeepBoth },
        };

        /// <summary>New parameters appended to the merged file.</summary>
        public ObservableCollection<NewParamRow> NewParams { get; } = new();

        /// <summary>Data type tokens for the new-parameter grid.</summary>
        public IReadOnlyList<string> DataTypeTokens => SpKeys.DataTypeTokens;

        [RelayCommand]
        private void BrowseMergeFirst() { var p = PickOpen(MergeFirstPath); if (p != null) MergeFirstPath = p; }

        [RelayCommand]
        private void BrowseMergeSecond() { var p = PickOpen(MergeSecondPath); if (p != null) MergeSecondPath = p; }

        [RelayCommand]
        private void BrowseMergeOutput()
        {
            var p = PickSave(MergeOutputPath, "Save the merged file as");
            if (p != null) MergeOutputPath = p;
        }

        [RelayCommand]
        private void AddNewParamRow()
        {
            var row = new NewParamRow();
            NewParams.Add(row);
            SelectedNewParam = row;
        }

        [RelayCommand]
        private void RemoveNewParamRow()
        {
            if (SelectedNewParam != null) NewParams.Remove(SelectedNewParam);
        }

        [RelayCommand]
        private void PreviewMerge() => TryMerge(write: false);

        [RelayCommand]
        private void RunMerge() => TryMerge(write: true);

        private void TryMerge(bool write)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(MergeFirstPath) || string.IsNullOrWhiteSpace(MergeSecondPath))
                {
                    Log(LogLevel.Error, "Pick both files to merge.");
                    return;
                }
                if (write && string.IsNullOrWhiteSpace(MergeOutputPath))
                {
                    Log(LogLevel.Error, "Choose where to save the merged file.");
                    return;
                }

                var first = SpFileReader.Read(MergeFirstPath.Trim());
                var second = SpFileReader.Read(MergeSecondPath.Trim());
                var staged = NewParams.Where(n => !string.IsNullOrWhiteSpace(n.Name)).Select(n => n.ToSpParam()).ToList();
                var result = SpMerger.Merge(first, second, SelectedPolicy.Value, staged);

                MergePreviewText =
                    $"First file: {first.Params.Count} parameters · Second file: {second.Params.Count} parameters\n" +
                    $"Merged file: {result.Merged.Params.Count} parameters in {result.Merged.Groups.Count} groups\n" +
                    $"Added from second: {result.AddedFromSecond} · Identical: {result.Identical} · Conflicts: {result.Conflicts} · New: {result.NewParameters}";
                foreach (var n in result.Notes) AddLog(n.IsWarning ? LogLevel.Warning : LogLevel.Info, n.Message);

                var after = DuplicateAnalyzer.Analyze(result.Merged);
                if (after.SetCount > 0)
                    Log(LogLevel.Warning, $"The merged file still has {after.SetCount} duplicate set(s); run Duplicate Check on it.");

                if (!write)
                {
                    Log(LogLevel.Info, "Preview only — nothing written.");
                    return;
                }

                string output = MergeOutputPath.Trim();
                if (File.Exists(output) &&
                    !Confirm($"'{Path.GetFileName(output)}' already exists. Replace it? The old file is kept as '{Path.GetFileName(output)}.bak'."))
                {
                    Log(LogLevel.Info, "Merge cancelled.");
                    return;
                }

                SpFileWriter.Write(result.Merged, output);
                Log(LogLevel.Success, $"Merged file written to {output} ({result.Merged.Params.Count} parameters).");
            }
            catch (System.Exception ex)
            {
                Log(LogLevel.Error, $"Merge failed — {ex.Message}");
            }
        }
    }
}
