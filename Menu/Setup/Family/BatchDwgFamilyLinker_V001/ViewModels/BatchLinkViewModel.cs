using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BatchDwgFamilyLinker.Models;
using Microsoft.Win32;
using System.IO;

namespace BatchDwgFamilyLinker.ViewModels
{
    public partial class BatchLinkViewModel : ObservableObject
    {
        private readonly ExternalEvent _externalEvent;
        private readonly FamilyBatchProcessor _handler;

        public BatchLinkViewModel(UIApplication uiApp)
        {
            _handler = new FamilyBatchProcessor(this);
            _externalEvent = ExternalEvent.Create(_handler);
            LiveLog = string.Empty;
        }

        // --------------------------------------------------
        // Bindable properties
        // --------------------------------------------------

        [ObservableProperty] private string familyFolderPath;
        [ObservableProperty] private string dwgFolderPath;

        [ObservableProperty] private bool isOriginToOrigin = true;
        [ObservableProperty] private bool isCenterToCenter;

        [ObservableProperty] private int totalFamilies;
        [ObservableProperty] private int processedCount;
        [ObservableProperty] private int failedCount;

        [ObservableProperty] private string liveLog;

        public string ProgressPercentText =>
            TotalFamilies > 0 ? $"{(int)System.Math.Round(ProcessedCount * 100.0 / TotalFamilies)}%" : "0%";

        partial void OnProcessedCountChanged(int value) => OnPropertyChanged(nameof(ProgressPercentText));
        partial void OnTotalFamiliesChanged(int value) => OnPropertyChanged(nameof(ProgressPercentText));

        // --------------------------------------------------
        // Validation
        // --------------------------------------------------

        public bool CanStart =>
            Directory.Exists(FamilyFolderPath) &&
            Directory.Exists(DwgFolderPath);

        partial void OnFamilyFolderPathChanged(string value)
        {
            ValidateFolder(value, "Family");
            OnPropertyChanged(nameof(CanStart));
        }

        partial void OnDwgFolderPathChanged(string value)
        {
            ValidateFolder(value, "DWG");
            OnPropertyChanged(nameof(CanStart));
        }

        private void ValidateFolder(string path, string label)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            if (!Directory.Exists(path))
                AppendLog($"?? Invalid {label} folder path");
            else
                AppendLog($"?? {label} folder set: {path}");
        }

        // --------------------------------------------------
        // Browse buttons (still available)
        // --------------------------------------------------

        [RelayCommand]
        private void BrowseFamilyFolder()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select any Revit Family inside the Family Folder",
                Filter = "Revit Family (*.rfa)|*.rfa",
                CheckFileExists = true
            };

            if (dlg.ShowDialog() == true)
                FamilyFolderPath = Path.GetDirectoryName(dlg.FileName);
        }

        [RelayCommand]
        private void BrowseDwgFolder()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select any DWG inside the DWG Folder",
                Filter = "AutoCAD Drawing (*.dwg)|*.dwg",
                CheckFileExists = true
            };

            if (dlg.ShowDialog() == true)
                DwgFolderPath = Path.GetDirectoryName(dlg.FileName);
        }

        // --------------------------------------------------
        // Batch execution
        // --------------------------------------------------

        [RelayCommand]
        private void StartBatch()
        {
            if (!CanStart)
            {
                AppendLog("? Cannot start batch. Check folder paths.");
                return;
            }

            ProcessedCount = 0;
            FailedCount = 0;
            TotalFamilies = 0;

            AppendLog("? Batch started");

            _handler.Options = new BatchOptions
            {
                FamilyFolderPath = FamilyFolderPath,
                DwgFolderPath = DwgFolderPath,
                PlacementMode = IsOriginToOrigin
                    ? DwgPlacementMode.OriginToOrigin
                    : DwgPlacementMode.CenterToCenter
            };

            _externalEvent.Raise();
        }

        // --------------------------------------------------
        // Close window
        // --------------------------------------------------

        [RelayCommand]
        private void Close()
        {
            System.Windows.Application.Current.Windows[0]?.Close();
        }

        // --------------------------------------------------
        // Logging
        // --------------------------------------------------

        public void AppendLog(string msg)
        {
            LiveLog += $"{System.DateTime.Now:HH:mm:ss}  {msg}\n";
        }
    }
}
