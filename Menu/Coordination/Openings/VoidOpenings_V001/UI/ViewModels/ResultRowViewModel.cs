using CommunityToolkit.Mvvm.ComponentModel;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.Reporting;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.UI.ViewModels
{
    /// <summary>
    /// One line of the results table. It shows an opening of this run, a stored opening whose clash is
    /// gone, or an opening of an applied package.
    /// </summary>
    public sealed class ResultRowViewModel : ObservableObject
    {
        private ExportRow _row;
        private bool _isApproved;

        /// <summary>Row for an opening of this run.</summary>
        public ResultRowViewModel(OpeningData opening)
        {
            Opening = opening;
            _row = ExportRow.From(opening);

            // New and changed openings are ticked for generation, unless they need a review first.
            _isApproved = CanApprove && opening.Status == OpeningStatus.Detected
                && (opening.Tracking == TrackingState.New || opening.Tracking == TrackingState.Updated);
        }

        /// <summary>Row with no opening of this run behind it (source deleted, or applied from a package).</summary>
        public ResultRowViewModel(ExportRow row)
        {
            _row = row;
        }

        /// <summary>The opening of this run; null for other rows.</summary>
        public OpeningData Opening { get; }

        /// <summary>The values shown and exported.</summary>
        public ExportRow Row => _row;

        /// <summary>True when the row can be ticked for generation: an opening of this run that has no unchanged void yet.</summary>
        public bool CanApprove => Opening != null && Opening.Tracking != TrackingState.Existing;

        /// <summary>True when the user approved the opening for Generate Void or for the package.</summary>
        public bool IsApproved
        {
            get => _isApproved;
            set => SetProperty(ref _isApproved, value && CanApprove);
        }

        /// <summary>True when the centre of the opening is known, so the view can zoom to it.</summary>
        public bool HasLocation => Opening != null;

        /// <summary>Clash id.</summary>
        public string ClashId => _row.ClashId;
        /// <summary>Job ID, or "on generate".</summary>
        public string JobId => string.IsNullOrEmpty(_row.JobId) ? "on generate" : _row.JobId;
        /// <summary>Structural model name.</summary>
        public string StructuralModel => _row.StructuralModel;
        /// <summary>Structural category name.</summary>
        public string StructuralCategory => _row.StructuralCategory;
        /// <summary>Structural element id.</summary>
        public long StructuralElementId => _row.StructuralElementId;
        /// <summary>MEP model name.</summary>
        public string MepModel => _row.MepModel;
        /// <summary>MEP category name.</summary>
        public string MepCategory => _row.MepCategory;
        /// <summary>MEP element id.</summary>
        public long MepElementId => _row.MepElementId;
        /// <summary>Width, or a dash for circular.</summary>
        public string Width => Size(_row.WidthMm);
        /// <summary>Height, or a dash for circular.</summary>
        public string Height => Size(_row.HeightMm);
        /// <summary>Diameter, or a dash for rectangular.</summary>
        public string Diameter => Size(_row.DiameterMm);
        /// <summary>Clearance.</summary>
        public string Clearance => Size(_row.ClearanceMm);
        /// <summary>Tracking text.</summary>
        public string Tracking => _row.Tracking;
        /// <summary>Status text.</summary>
        public string Status => _row.Status;
        /// <summary>Review or result note.</summary>
        public string Note => _row.Note;

        /// <summary>True when any shown text contains <paramref name="text"/> (used by the Find box).</summary>
        public bool Matches(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;
            string needle = text.Trim();
            foreach (string value in new[] { ClashId, _row.JobId, StructuralElementId.ToString(), MepElementId.ToString(), Status, Tracking, StructuralCategory, MepCategory })
                if (value != null && value.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        /// <summary>Reads the opening again after Generate changed it.</summary>
        public void Refresh()
        {
            if (Opening == null) return;
            _row = ExportRow.From(Opening);
            if (!CanApprove) _isApproved = false;
            OnPropertyChanged(string.Empty);
        }

        private static string Size(double mm) => mm > 0 ? mm.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) : "–";
    }
}
