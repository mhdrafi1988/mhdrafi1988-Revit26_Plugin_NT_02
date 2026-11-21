using System;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ClashDetection;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ModelSelection;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.UI.ViewModels
{
    /// <summary>One row of the RESULTS table: a clash, the opening sized for it and the user's decision.</summary>
    public sealed class ClashRowViewModel : ObservableObject
    {
        private const string None = "–";
        private readonly Action _decisionChanged;
        private readonly string _reviewNote;

        /// <summary>Creates a row.</summary>
        /// <param name="row">The opening and its decision.</param>
        /// <param name="decisionChanged">Called after the row is approved, unapproved or rejected.</param>
        public ClashRowViewModel(ReviewedOpening row, Action decisionChanged)
        {
            Review = row ?? throw new ArgumentNullException(nameof(row));
            _decisionChanged = decisionChanged;

            OpeningData opening = row.Opening;
            ClashData clash = opening.Clash;
            ClashId = opening.ClashId;
            StructuralModel = clash.StructuralModel?.DisplayName ?? string.Empty;
            StructuralCategory = CategoryCatalog.NameOf(clash.Structural.Category);
            StructuralElementId = clash.Structural.ElementId;
            MepModel = clash.MepModel?.DisplayName ?? string.Empty;
            MepCategory = CategoryCatalog.NameOf(clash.Mep.Category);
            MepElementId = clash.Mep.ElementId;

            MepProfile profile = clash.Segment?.Profile;
            MepSize = profile == null ? None
                : profile.Shape == ProfileShape.Round ? "Ø" + Mm(profile.DiameterMm)
                : Mm(profile.WidthMm) + " × " + Mm(profile.HeightMm);

            bool circular = opening.Shape == OpeningShape.Circular;
            Width = circular ? None : Mm(opening.WidthMm);
            Height = circular ? None : Mm(opening.HeightMm);
            Diameter = circular ? Mm(opening.DiameterMm) : None;
            Depth = Mm(opening.DepthMm);
            Clearance = Mm(opening.ClearanceMm);
            _reviewNote = opening.ReviewNote ?? string.Empty;
        }

        /// <summary>The opening and its decision.</summary>
        public ReviewedOpening Review { get; }

        /// <summary>Short id of the clash; the same on every run.</summary>
        public string ClashId { get; }

        /// <summary>Name of the structural model.</summary>
        public string StructuralModel { get; }

        /// <summary>Category of the structural element.</summary>
        public string StructuralCategory { get; }

        /// <summary>Revit id of the structural element.</summary>
        public long StructuralElementId { get; }

        /// <summary>Name of the MEP model.</summary>
        public string MepModel { get; }

        /// <summary>Category of the MEP element.</summary>
        public string MepCategory { get; }

        /// <summary>Revit id of the MEP element.</summary>
        public long MepElementId { get; }

        /// <summary>Cross-section of the MEP element without insulation: "Ø40" or "400 × 300".</summary>
        public string MepSize { get; }

        /// <summary>Opening width in millimetres; "–" for a circular opening.</summary>
        public string Width { get; }

        /// <summary>Opening height in millimetres; "–" for a circular opening.</summary>
        public string Height { get; }

        /// <summary>Opening diameter in millimetres; "–" for a rectangular opening.</summary>
        public string Diameter { get; }

        /// <summary>Length of the void through the structure, overshoot included.</summary>
        public string Depth { get; }

        /// <summary>The clearance value that was applied.</summary>
        public string Clearance { get; }

        /// <summary>
        /// Why the row needs a review, followed by what the last Generate said about it; empty when
        /// there is neither.
        /// </summary>
        public string Note => string.Join(" ", new[] { _reviewNote, Review.Opening.ResultNote }
            .Where(part => !string.IsNullOrWhiteSpace(part)));

        /// <summary>Job ID of the void placed for this row; "–" until one is generated.</summary>
        public string JobId => string.IsNullOrEmpty(Review.Opening.JobId) ? None : Review.Opening.JobId;

        /// <summary>
        /// What the last Generate did with this row: "Cut", "Generated" or "Failed"; "–" when no void
        /// has been generated for it.
        /// </summary>
        public string VoidStatus
        {
            get
            {
                switch (Review.Opening.Status)
                {
                    case OpeningStatus.Cut:
                    case OpeningStatus.Generated:
                    case OpeningStatus.Failed:
                        return Review.Opening.Status.ToString();
                    default:
                        return None;
                }
            }
        }

        /// <summary>Shows what a Generate run wrote onto the opening. Call it on the window's thread.</summary>
        public void RefreshResult()
        {
            OnPropertyChanged(nameof(JobId));
            OnPropertyChanged(nameof(VoidStatus));
            OnPropertyChanged(nameof(Note));
        }

        /// <summary>Ticked when the opening will be generated. A rejected row cannot be ticked; use Approve.</summary>
        public bool IsApproved
        {
            get => Review.IsApproved;
            set
            {
                if (value == Review.IsApproved || Review.IsRejected)
                    return;
                if (value) Review.Approve();
                else Review.Unapprove();
                RaiseDecision();
            }
        }

        /// <summary>True when the user rejected the row.</summary>
        public bool IsRejected => Review.IsRejected;

        /// <summary>False for a rejected row, whose checkbox is disabled.</summary>
        public bool CanTick => !Review.IsRejected;

        /// <summary>True when the row waits for a review.</summary>
        public bool IsReview => Review.Status == OpeningStatus.Review;

        /// <summary>"Detected", "Review" or "Rejected".</summary>
        public string Status => Review.Status.ToString();

        /// <summary>Approves the row; a rejected one comes back.</summary>
        public void Approve()
        {
            Review.Approve();
            RaiseDecision();
        }

        /// <summary>Rejects the row.</summary>
        public void Reject()
        {
            Review.Reject();
            RaiseDecision();
        }

        /// <summary>True when the row's text contains <paramref name="text"/> (clash id, element ids, models, categories, note).</summary>
        public bool Matches(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return true;
            string needle = text.Trim();
            return Has(ClashId, needle) || Has(StructuralElementId.ToString(CultureInfo.InvariantCulture), needle)
                || Has(MepElementId.ToString(CultureInfo.InvariantCulture), needle)
                || Has(StructuralModel, needle) || Has(MepModel, needle)
                || Has(StructuralCategory, needle) || Has(MepCategory, needle) || Has(Note, needle);
        }

        private static bool Has(string value, string needle)
            => value != null && value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        private static string Mm(double value) => Math.Round(value, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);

        private void RaiseDecision()
        {
            OnPropertyChanged(nameof(IsApproved));
            OnPropertyChanged(nameof(IsRejected));
            OnPropertyChanged(nameof(CanTick));
            OnPropertyChanged(nameof(IsReview));
            OnPropertyChanged(nameof(Status));
            _decisionChanged?.Invoke();
        }
    }
}
