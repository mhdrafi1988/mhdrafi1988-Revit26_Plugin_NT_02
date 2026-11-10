using System;
using System.Linq;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.SmartViewToSheetPlacer.V224.Infrastructure.ExternalEvents;

namespace Revit26_Plugin_Revised.SmartViewToSheetPlacer.V224.ViewModels
{
    /// <summary>Stage 4: Confirm &amp; Place — the one Revit-API-touching action
    /// besides LoadViews/OpenSheets. Raises the shared ExternalEvent to create
    /// sheets and place viewports inside a single Transaction (see Handler).</summary>
    public partial class SmartViewToSheetPlacerViewModel
    {
        // ---- Stage 4 state ----
        [ObservableProperty] private bool _stage4Complete;
        public string Stage4StatusLabel => Stage4Complete ? "Complete" : (Stage3Complete ? "In Progress" : "Not Started");

        // ---- Placement progress (bar + percentage + placed / remaining) ----
        [ObservableProperty] private int _progressTotal;
        [ObservableProperty] private int _progressProcessed;
        [ObservableProperty] private int _progressPlaced;
        [ObservableProperty] private string _progressCurrent = string.Empty;

        public bool HasProgress => ProgressTotal > 0;
        public double ProgressPercent => ProgressTotal > 0 ? 100.0 * ProgressProcessed / ProgressTotal : 0;
        public string ProgressPercentText => $"{ProgressPercent:0}%";
        public int ProgressRemaining => Math.Max(0, ProgressTotal - ProgressProcessed);

        /// <summary>"12 placed · 16 remaining of 28", plus a skipped/failed count when non-zero.</summary>
        public string ProgressDetailText
        {
            get
            {
                var text = $"{ProgressPlaced} placed · {ProgressRemaining} remaining of {ProgressTotal}";
                int notPlaced = ProgressProcessed - ProgressPlaced;
                return notPlaced > 0 ? $"{text} · {notPlaced} skipped/failed" : text;
            }
        }

        private void NotifyProgressChanged()
        {
            OnPropertyChanged(nameof(HasProgress));
            OnPropertyChanged(nameof(ProgressPercent));
            OnPropertyChanged(nameof(ProgressPercentText));
            OnPropertyChanged(nameof(ProgressRemaining));
            OnPropertyChanged(nameof(ProgressDetailText));
        }

        private void ResetProgress(int total)
        {
            ProgressTotal = total;
            ProgressProcessed = 0;
            ProgressPlaced = 0;
            ProgressCurrent = string.Empty;
            NotifyProgressChanged();
        }

        /// <summary>
        /// Handler callback, fired once per processed view while Execute() is
        /// still running. Execute() blocks Revit's UI thread, so after updating
        /// the bound values we flush the dispatcher at Render priority — that
        /// repaints the bar without pumping input (clicks stay queued until the
        /// placement finishes).
        /// </summary>
        private void OnHandlerProgress(int processed, int total, int placed, string current)
        {
            void Apply()
            {
                ProgressTotal = total;
                ProgressProcessed = processed;
                ProgressPlaced = placed;
                ProgressCurrent = current;
                NotifyProgressChanged();
            }

            if (_dispatcher.CheckAccess())
            {
                Apply();
                _dispatcher.Invoke(DispatcherPriority.Render, new Action(() => { }));
            }
            else
            {
                _dispatcher.BeginInvoke(new Action(Apply));
            }
        }

        [RelayCommand]
        private void BackToStage3()
        {
            Stage4Expanded = false;
            Stage3Expanded = true;
        }

        [RelayCommand(CanExecute = nameof(CanPlaceViews))]
        private void PlaceViews()
        {
            if (SelectedTitleblock == null) return;

            // Views flagged Overflow no longer fit their sheet: ask before placing them.
            int overflow = SuggestedSheets.Sum(sh => sh.Placements.Count(p => !p.Fits));
            if (overflow > 0)
            {
                var dialog = new Autodesk.Revit.UI.TaskDialog(Revit26_Plugin_Revised.Shared.Services.ToolCatalog.SmartViewToSheetPlacer.Title)
                {
                    MainInstruction = $"{overflow} view(s) are marked Overflow",
                    MainContent = "They no longer fit their sheet and may overlap other views or run past the margin.\n\n" +
                                  "Place them anyway? Choose No to go back and move them to another sheet.",
                    CommonButtons = Autodesk.Revit.UI.TaskDialogCommonButtons.Yes | Autodesk.Revit.UI.TaskDialogCommonButtons.No,
                    DefaultButton = Autodesk.Revit.UI.TaskDialogResult.No
                };
                if (dialog.Show() != Autodesk.Revit.UI.TaskDialogResult.Yes)
                {
                    Logs.Add(new LogEntry(LogLevel.Warning,
                        $"Placement not started: {overflow} view(s) are marked Overflow. Move them to another sheet in Suggested Placement, or place anyway."));
                    return;
                }
                Logs.Add(new LogEntry(LogLevel.Warning, $"Placing with {overflow} Overflow view(s), as confirmed."));
            }

            IsBusy = true;
            BusyMessage = "Creating sheets and placing views...";
            ResetProgress(SuggestedSheets.Sum(sh => sh.Placements.Count));

            _handler.TitleblockFamilySymbolId = SelectedTitleblock.FamilySymbolId;
            _handler.MarginTopMm = MarginTopMm;
            _handler.MarginBottomMm = MarginBottomMm;
            _handler.MarginLeftMm = MarginLeftMm;
            _handler.MarginRightMm = MarginRightMm;
            _handler.SheetsToPlace = SuggestedSheets.ToList();
            _handler.Request = SmartViewToSheetPlacerRequest.PlaceViews;
            _event.Raise();
        }

        private bool CanPlaceViews() => !IsBusy && SuggestedSheets.Count > 0;

        partial void OnStage4CompleteChanged(bool value)
        {
            OnPropertyChanged(nameof(Stage4StatusLabel));
            OnPropertyChanged(nameof(Stage5StatusLabel));
        }

        /// <summary>
        /// Populates Stage 5 results from the handler's PlaceViews output and
        /// advances the accordion. Lives here (not Stage5 partial) because it's
        /// the direct continuation of this stage's PlaceViews action.
        /// </summary>
        private void HandlePlaceViewsCompleted()
        {
            if (!_handler.LastRunSucceeded)
            {
                Logs.Add(new LogEntry(LogLevel.Error, "Placement did not complete successfully. See log above."));
                return;
            }

            PlacedSheetCount = SuggestedSheets.Count(s => s.CreatedSheetId != null);
            PlacedViewCount = _handler.PlacedCount;
            FailedCount = _handler.FailedCount;

            ProgressPlaced = _handler.PlacedCount;
            ProgressProcessed = ProgressTotal;
            ProgressCurrent = string.Empty;
            NotifyProgressChanged();

            Stage4Complete = true;
            Stage4Expanded = false;
            Stage5Expanded = true;

            AutoSaveLogs();
        }
    }
}
