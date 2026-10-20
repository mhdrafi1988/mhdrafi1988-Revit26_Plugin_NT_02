using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.SharedParamStudio.V001.Infrastructure;

namespace Revit26_Plugin_Revised.SharedParamStudio.V001.UI.ViewModels
{
    public sealed partial class SharedParamStudioViewModel
    {
        // ── Tab 4: Retrieve from Project ──────────────────────────────────────

        [ObservableProperty] private string _retrieveFilePath = string.Empty;
        [ObservableProperty] private string _retrieveFileGroup = "Project parameters";
        [ObservableProperty] private string _retrieveSummary = "Press Refresh to list the shared parameters of the open project.";

        /// <summary>Shared parameters found in the project.</summary>
        public ObservableCollection<ProjectSharedParameter> RetrievedParameters { get; } = new();

        [RelayCommand]
        private void BrowseRetrieveFile()
        {
            string p = PickSave(RetrieveFilePath, "Shared parameter file to write to (type a new name to create one)");
            if (p != null) RetrieveFilePath = p;
        }

        [RelayCommand]
        private void RefreshRetrieved()
        {
            System.Collections.Generic.List<ProjectSharedParameter> found = null;
            int skipped = 0;
            Log(LogLevel.Info, "Reading the project's shared parameters…");
            RunJob(doc => found = SharedParamStudioService.CollectProjectParameters(doc, out skipped), () =>
            {
                RetrievedParameters.Clear();
                foreach (var p in found ?? Enumerable.Empty<ProjectSharedParameter>()) RetrievedParameters.Add(p);
                RetrieveSummary = $"{RetrievedParameters.Count} shared parameter(s) in the project" +
                                  (skipped > 0 ? $"; {skipped} non-shared project parameter(s) not listed." : ".");
                Log(LogLevel.Success, RetrieveSummary);
            });
        }

        [RelayCommand]
        private void SelectAllRetrieved() { foreach (var p in RetrievedParameters) p.IsSelected = true; }

        [RelayCommand]
        private void ClearRetrieved() { foreach (var p in RetrievedParameters) p.IsSelected = false; }

        [RelayCommand]
        private void WriteRetrievedToFile()
        {
            var chosen = RetrievedParameters.Where(p => p.IsSelected).ToList();
            if (chosen.Count == 0) { Log(LogLevel.Error, "Tick at least one parameter."); return; }
            if (string.IsNullOrWhiteSpace(RetrieveFilePath)) { Log(LogLevel.Error, "Choose the shared parameter file to write to."); return; }

            string path = RetrieveFilePath.Trim();
            string group = RetrieveFileGroup?.Trim();
            if (File.Exists(path) && !Confirm($"Add {chosen.Count} parameter(s) to the existing '{Path.GetFileName(path)}'? Existing entries are not changed and the old file is kept as .bak."))
                return;

            JobResult result = null;
            RunJob(doc => result = SharedParamStudioService.WriteToFile(doc, path, group, chosen), () =>
            {
                if (result != null) LogAll(result.Messages);
            });
        }

        [RelayCommand]
        private void ExportRetrievedCsv()
        {
            try
            {
                if (RetrievedParameters.Count == 0) { Log(LogLevel.Info, "Nothing to export; press Refresh first."); return; }
                var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "project_shared_parameters.csv" };
                if (dlg.ShowDialog() != true) return;

                var sb = new StringBuilder("Name,GUID,DataType,Binding,Group,Categories\r\n");
                foreach (var p in RetrievedParameters)
                    sb.Append(string.Join(",", Csv(p.Name), Csv(p.Guid.ToString()), Csv(p.DataTypeLabel), Csv(p.BindingKind), Csv(p.GroupLabel), Csv(p.Categories))).Append("\r\n");
                File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
                Log(LogLevel.Success, $"Saved {dlg.FileName}.");
            }
            catch (System.Exception ex)
            {
                Log(LogLevel.Error, $"Export failed — {ex.Message}");
            }
        }
    }
}
