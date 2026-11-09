using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.AnnotationOverlapDetection.V004.Helpers;
using Revit26_Plugin_Revised.AnnotationOverlapDetection.V004.Models;

namespace Revit26_Plugin_Revised.AnnotationOverlapDetection.V004.ViewModels
{
    public partial class AnnotationOverlapViewModel : ObservableObject
    {
        private readonly Document _doc;
        private readonly View _activeView;
        private readonly ExternalEvent _zoomEvent;
        private readonly ZoomToElementEventHandler _zoomHandler;

        public ObservableCollection<AnnotationFamily> AnnotationFamilies { get; } = new();
        public ObservableCollection<OverlapResult> OverlapResults { get; } = new();

        [ObservableProperty]
        private int totalScanned;

        [ObservableProperty]
        private int totalOverlaps;

        [ObservableProperty]
        private string breakdownByTypeText = string.Empty;

        [ObservableProperty]
        private bool isLoading;

        [ObservableProperty]
        private string errorMessage = string.Empty;

        [ObservableProperty]
        private bool hasError;

        // ToolWindowShell status strip (CLAUDE.md v1.1)
        [ObservableProperty]
        private double progress;

        [ObservableProperty]
        private string summaryText = string.Empty;

        // Raw collected data, kept around so "Check Overlaps" doesn't re-scan the document
        private List<AnnotationData> _allAnnotations = new();

        public AnnotationOverlapViewModel(Document doc, View activeView,
            ExternalEvent zoomEvent, ZoomToElementEventHandler zoomHandler)
        {
            _doc = doc;
            _activeView = activeView;
            _zoomEvent = zoomEvent;
            _zoomHandler = zoomHandler;

            LoadAnnotationFamilies(doc);
        }

        /// <summary>
        /// Step 2 + 3: scan the view once and populate the type-selector list.
        /// </summary>
        public void LoadAnnotationFamilies(Document doc)
        {
            _allAnnotations = OverlapDetectionHelper.GetAnnotationsFromView(_activeView, doc);
            var grouped = OverlapDetectionHelper.GroupByType(_allAnnotations);

            AnnotationFamilies.Clear();
            foreach (var kvp in grouped.OrderBy(g => g.Key))
            {
                AnnotationFamilies.Add(new AnnotationFamily
                {
                    TypeName = kvp.Key,
                    Count = kvp.Value.Count,
                    IsSelected = true
                });
            }
        }

        [RelayCommand]
        private void SelectAll()
        {
            foreach (var family in AnnotationFamilies)
                family.IsSelected = true;
        }

        [RelayCommand]
        private void ClearAll()
        {
            foreach (var family in AnnotationFamilies)
                family.IsSelected = false;
        }

        [RelayCommand]
        private void CheckOverlaps()
        {
            HasError = false;
            ErrorMessage = string.Empty;

            var selectedTypes = AnnotationFamilies
                .Where(f => f.IsSelected)
                .Select(f => f.TypeName)
                .ToHashSet();

            if (selectedTypes.Count == 0)
            {
                HasError = true;
                ErrorMessage = "Please select at least one type.";
                return;
            }

            IsLoading = true;
            // Esc / Cancel with a Yes-No confirm during the (read-only) overlap scan.
            using ToolCancellation cancel = ToolCancellation.BeginFor(ToolCatalog.AnnotationOverlapDetection, null);
            try
            {
                RunOverlapDetection(selectedTypes);
                UpdateSummaryCard();
            }
            catch (OperationCanceledException)
            {
                // Nothing in the model changes in this tool; the grid keeps its previous results.
                ErrorMessage = "Cancelled by user.";
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = $"Overlap detection failed: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Step 5: run detection against only the selected type groups.
        /// </summary>
        private void RunOverlapDetection(HashSet<string> selectedTypes)
        {
            var filtered = _allAnnotations
                .Where(a => selectedTypes.Contains(a.TypeName))
                .ToList();

            var grouped = OverlapDetectionHelper.GroupByType(filtered);
            var results = OverlapDetectionHelper.DetectOverlaps(grouped);

            OverlapResults.Clear();
            foreach (var r in results)
                OverlapResults.Add(r);

            TotalScanned = filtered.Count;
        }

        /// <summary>
        /// Step 7: summary card - total scanned, total overlaps, breakdown by type.
        /// </summary>
        private void UpdateSummaryCard()
        {
            TotalOverlaps = OverlapResults.Count;

            var breakdown = OverlapResults
                .GroupBy(r => r.AnnotationType)
                .Select(g => $"{g.Count()} {g.Key}");

            BreakdownByTypeText = TotalOverlaps == 0
                ? "No overlaps found"
                : string.Join(", ", breakdown);
        }

        [RelayCommand]
        private void ZoomToElement(long elementIdValue)
        {
            _zoomHandler.ElementIdToZoom = new ElementId(elementIdValue);
            _zoomEvent.Raise();
        }
    }
}
