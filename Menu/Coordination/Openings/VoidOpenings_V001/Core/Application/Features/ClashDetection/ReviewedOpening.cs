using System;
using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ClashDetection
{
    /// <summary>
    /// One result row: an opening and the user's decision on it. A plain opening starts approved, one
    /// that needs a review starts unapproved, so nothing doubtful is generated without a person
    /// ticking it. Rejecting keeps the row but takes it out of generation; approving brings it back.
    /// </summary>
    public sealed class ReviewedOpening
    {
        private readonly OpeningStatus _detectedStatus;

        /// <summary>Creates the row for a freshly analysed opening.</summary>
        public ReviewedOpening(OpeningData opening)
        {
            Opening = opening ?? throw new ArgumentNullException(nameof(opening));
            _detectedStatus = opening.Status == OpeningStatus.Review ? OpeningStatus.Review : OpeningStatus.Detected;
            Status = _detectedStatus;
            IsApproved = _detectedStatus == OpeningStatus.Detected;
        }

        /// <summary>The opening.</summary>
        public OpeningData Opening { get; }

        /// <summary>Detected, Review or Rejected.</summary>
        public OpeningStatus Status { get; private set; }

        /// <summary>True when the opening will be generated.</summary>
        public bool IsApproved { get; private set; }

        /// <summary>True when the user rejected the opening.</summary>
        public bool IsRejected => Status == OpeningStatus.Rejected;

        /// <summary>Approves the opening; a rejected one goes back to Detected or Review.</summary>
        public void Approve()
        {
            Status = _detectedStatus;
            IsApproved = true;
        }

        /// <summary>Takes the approval back without rejecting.</summary>
        public void Unapprove()
        {
            if (!IsRejected)
                IsApproved = false;
        }

        /// <summary>Rejects the opening: it stays in the list and is never generated.</summary>
        public void Reject()
        {
            Status = OpeningStatus.Rejected;
            IsApproved = false;
        }

        /// <summary>
        /// Rows for a new analysis that keep the decisions of the previous one: a clash that was there
        /// before (same clash key) stays approved, unapproved or rejected as the user left it.
        /// </summary>
        public static IReadOnlyList<ReviewedOpening> From(IEnumerable<OpeningData> openings, IEnumerable<ReviewedOpening> previous)
        {
            var before = new Dictionary<string, ReviewedOpening>(StringComparer.Ordinal);
            foreach (ReviewedOpening row in previous ?? Enumerable.Empty<ReviewedOpening>())
                before[row.Opening.Clash.Key.ToString()] = row;

            var rows = new List<ReviewedOpening>();
            foreach (OpeningData opening in openings ?? Enumerable.Empty<OpeningData>())
            {
                var row = new ReviewedOpening(opening);
                if (before.TryGetValue(opening.Clash.Key.ToString(), out ReviewedOpening old))
                {
                    if (old.IsRejected) row.Reject();
                    else if (old.IsApproved) row.Approve();
                    else row.Unapprove();
                }
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>
        /// Box to zoom to, in host millimetres: the opening with <paramref name="marginMm"/> of
        /// surroundings on every side.
        /// </summary>
        public static Aabb ZoomBox(OpeningData opening, double marginMm = 1000)
        {
            if (opening == null) throw new ArgumentNullException(nameof(opening));
            double across = Math.Max(opening.DiameterMm, Math.Max(opening.WidthMm, opening.HeightMm));
            double half = Math.Max(across, opening.DepthMm) / 2 + Math.Max(0, marginMm);
            return Aabb.FromPoints(new[] { opening.Center }).Expand(half);
        }
    }
}
