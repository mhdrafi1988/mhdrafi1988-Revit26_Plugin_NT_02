using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.SharedParamStudio.V001.Core;

namespace Revit26_Plugin_Revised.SharedParamStudio.V001.UI.ViewModels
{
    public sealed partial class SharedParamStudioViewModel
    {
        // ── Tab 3: Duplicate Check ────────────────────────────────────────────

        private SpFile _dupFile;
        private DuplicateReport _dupReport;

        [ObservableProperty] private string _duplicatePath = string.Empty;
        [ObservableProperty] private string _duplicateFilter = string.Empty;
        [ObservableProperty] private string _duplicateSummary = "Pick a file and press Scan.";

        /// <summary>Every member of every duplicate set found.</summary>
        public ObservableCollection<DuplicateRow> DuplicateRows { get; } = new();

        /// <summary>The duplicate rows after the search box filter.</summary>
        public System.ComponentModel.ICollectionView DuplicateView { get; private set; }

        partial void OnDuplicateFilterChanged(string value) => DuplicateView?.Refresh();

        private void InitDuplicateView()
        {
            DuplicateView = System.Windows.Data.CollectionViewSource.GetDefaultView(DuplicateRows);
            DuplicateView.Filter = o => o is DuplicateRow r && Matches(DuplicateFilter, r.Name, r.Guid, r.Group, r.DataType, r.Kind);
        }

        [RelayCommand]
        private void BrowseDuplicateFile() { var p = PickOpen(DuplicatePath); if (p != null) DuplicatePath = p; }

        [RelayCommand]
        private void ScanDuplicates()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(DuplicatePath)) { Log(LogLevel.Error, "Pick a shared parameter file first."); return; }

                _dupFile = SpFileReader.Read(DuplicatePath.Trim());
                _dupReport = DuplicateAnalyzer.Analyze(_dupFile);

                DuplicateRows.Clear();
                foreach (var i in _dupReport.Items)
                {
                    DuplicateRows.Add(new DuplicateRow
                    {
                        Kind = i.Kind == DuplicateKind.SameGuid ? "Same GUID" : "Same name",
                        Name = i.Param.Name,
                        Guid = i.Param.Guid,
                        DataType = i.Param.DataType,
                        Group = i.Param.GroupName,
                        Line = i.Param.LineNumber,
                        Action = i.IsFirst ? "Keep" : "Remove",
                        Note = i.DataTypesDiffer ? "data types differ" : string.Empty
                    });
                }

                DuplicateSummary = _dupReport.SetCount == 0
                    ? $"No duplicates in {_dupReport.Scanned} parameters."
                    : $"{_dupReport.SetCount} duplicate set(s) among {_dupReport.Scanned} parameters; {_dupReport.Removable.Count()} would be removed.";
                Log(_dupReport.SetCount == 0 ? LogLevel.Success : LogLevel.Warning, DuplicateSummary);
            }
            catch (System.Exception ex)
            {
                Log(LogLevel.Error, $"Scan failed — {ex.Message}");
            }
        }

        [RelayCommand]
        private void SaveCleanCopy()
        {
            try
            {
                if (_dupFile == null || _dupReport == null) { Log(LogLevel.Error, "Scan a file first."); return; }
                if (!_dupReport.Removable.Any()) { Log(LogLevel.Info, "Nothing to remove."); return; }

                string suggestion = Path.Combine(Path.GetDirectoryName(DuplicatePath.Trim()) ?? string.Empty,
                    Path.GetFileNameWithoutExtension(DuplicatePath.Trim()) + "_clean.txt");
                string path = PickSave(suggestion, "Save the file without its duplicates as");
                if (path == null) return;
                if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(DuplicatePath.Trim()), System.StringComparison.OrdinalIgnoreCase) &&
                    !Confirm("This replaces the scanned file (the old one is kept as .bak). Continue?"))
                    return;

                var clean = DuplicateAnalyzer.RemoveDuplicates(_dupFile, _dupReport);
                SpFileWriter.Write(clean, path);
                Log(LogLevel.Success, $"Saved {path}: removed {_dupFile.Params.Count - clean.Params.Count} duplicate(s), kept the first of each set.");
            }
            catch (System.Exception ex)
            {
                Log(LogLevel.Error, $"Save failed — {ex.Message}");
            }
        }

        [RelayCommand]
        private void ExportDuplicateCsv()
        {
            try
            {
                if (DuplicateRows.Count == 0) { Log(LogLevel.Info, "No duplicates to export."); return; }
                var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "duplicate_parameters.csv" };
                if (dlg.ShowDialog() != true) return;

                var sb = new StringBuilder("Kind,Name,GUID,DataType,Group,Line,Action,Note\r\n");
                foreach (var r in DuplicateRows)
                    sb.Append(string.Join(",", Csv(r.Kind), Csv(r.Name), Csv(r.Guid), Csv(r.DataType), Csv(r.Group), r.Line, Csv(r.Action), Csv(r.Note))).Append("\r\n");
                File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
                Log(LogLevel.Success, $"Report saved to {dlg.FileName}.");
            }
            catch (System.Exception ex)
            {
                Log(LogLevel.Error, $"Export failed — {ex.Message}");
            }
        }
    }
}
