using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.TrackerId.V002.Infrastructure.Revit;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.TrackerId.V002.UI.ViewModels
{
    // Select & zoom: the result (step 3), status (step 5) and health (step 6) grids can select their
    // elements in Revit. The window passes the grids' selected rows in through the *SelectedItems lists.
    public partial class TrackerIdViewModel
    {
        /// <summary>Rows selected in the step 3 result grid (set by the window).</summary>
        public IList SelectedPlanItems { get; set; } = new List<object>();

        /// <summary>Rows selected in the step 5 status grid (set by the window).</summary>
        public IList SelectedStatusItems { get; set; } = new List<object>();

        /// <summary>Rows selected in the step 6 health grid (set by the window).</summary>
        public IList SelectedHealthItems { get; set; } = new List<object>();

        /// <summary>Selects every row the step 3 filter shows in Revit.</summary>
        [RelayCommand]
        private void SelectShownPlanRows() =>
            SelectInRevit(PlanView?.Cast<PlanRowViewModel>().Select(r => r.ElementId), zoom: false, "shown result rows");

        /// <summary>Selects and zooms to the rows highlighted in the step 3 grid.</summary>
        [RelayCommand]
        private void ZoomToPlanRows() =>
            SelectInRevit(SelectedPlanItems?.OfType<PlanRowViewModel>().Select(r => r.ElementId), zoom: true, "highlighted rows");

        /// <summary>Selects every row the step 5 filter shows in Revit.</summary>
        [RelayCommand]
        private void SelectShownStatusRows() =>
            SelectInRevit(StatusView?.Cast<StatusRowViewModel>().Select(r => r.ElementId), zoom: false, "shown status rows");

        /// <summary>Selects and zooms to the rows highlighted in the step 5 grid.</summary>
        [RelayCommand]
        private void ZoomToStatusRows() =>
            SelectInRevit(SelectedStatusItems?.OfType<StatusRowViewModel>().Select(r => r.ElementId), zoom: true, "highlighted rows");

        /// <summary>Selects every element the step 6 filter shows in Revit.</summary>
        [RelayCommand]
        private void SelectShownHealthRows() =>
            SelectInRevit(HealthView?.Cast<HealthRowViewModel>().Where(r => r.Issue.ElementId.HasValue).Select(r => r.Issue.ElementId.Value),
                          zoom: false, "shown findings");

        /// <summary>Selects and zooms to the rows highlighted in the step 6 grid.</summary>
        [RelayCommand]
        private void ZoomToHealthRows() =>
            SelectInRevit(SelectedHealthItems?.OfType<HealthRowViewModel>().Where(r => r.Issue.ElementId.HasValue).Select(r => r.Issue.ElementId.Value),
                          zoom: true, "highlighted findings");

        /// <summary>Selects one element and zooms to it (double-click on a grid row).</summary>
        public void ZoomToElement(long elementId) => SelectInRevit(new[] { elementId }, zoom: true, $"element {elementId}");

        // Selects the elements in Revit (and zooms to them) through the job queue.
        private void SelectInRevit(IEnumerable<long> ids, bool zoom, string what)
        {
            var list = ids?.Distinct().ToList() ?? new List<long>();
            if (list.Count == 0)
            {
                SummaryText = "No rows to select.";
                return;
            }

            Run(zoom ? "Zooming…" : "Selecting…", app =>
            {
                var doc = DocumentFor(app);
                if (doc == null) return;
                int selected = SelectionService.Select(app.ActiveUIDocument, list, zoom);
                OnUi(() =>
                {
                    SummaryText = $"Selected {selected} element(s) in Revit ({what}).";
                    if (selected < list.Count) AddLog(LogLevel.Warning, $"{list.Count - selected} element(s) no longer exist in the model.");
                });
            });
        }
    }
}
