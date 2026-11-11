using System;
using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Services
{
    /// <summary>A stored opening whose clash was not found again, with what the check concluded.</summary>
    public sealed class StoredOpeningCheck
    {
        /// <summary>The record.</summary>
        public StoredOpening Record { get; set; }

        /// <summary>Existing, Updated or SourceDeleted.</summary>
        public TrackingState State { get; set; }

        /// <summary>Explanation for the results table.</summary>
        public string Note { get; set; }
    }

    /// <summary>
    /// Compares the openings of this run with the records stored by earlier runs, so an opening is
    /// never generated twice and a changed or orphaned one is reported.
    /// </summary>
    public static class OpeningReconciler
    {
        /// <summary>
        /// Sets <see cref="OpeningData.Tracking"/>, Job ID, instance and status on each detected opening,
        /// and returns the records whose clash no longer exists.
        /// </summary>
        /// <param name="detected">Openings calculated in this run (host coordinates).</param>
        /// <param name="stored">Records read from the void instances in the structural model.</param>
        /// <param name="structuralToHost">Transform of the structural model into the host.</param>
        /// <param name="mepModelKey">The MEP model of this run. Records of other MEP models are left alone.</param>
        /// <param name="structuralCategories">Structural categories checked in this run.</param>
        /// <param name="mepCategories">MEP categories checked in this run.</param>
        /// <param name="mep">The MEP elements of this run, to check records whose clash was not found again; may be null.</param>
        /// <returns>The records in this run's scope whose clash was not found again, each with its state.</returns>
        public static IReadOnlyList<StoredOpeningCheck> Reconcile(IEnumerable<OpeningData> detected, IEnumerable<StoredOpening> stored,
            Transform3 structuralToHost, string mepModelKey,
            IEnumerable<StructuralCategory> structuralCategories, IEnumerable<MepCategory> mepCategories, MepModel mep = null)
        {
            if (detected == null) throw new ArgumentNullException(nameof(detected));

            var byKey = new Dictionary<string, StoredOpening>(StringComparer.Ordinal);
            foreach (StoredOpening record in stored ?? Enumerable.Empty<StoredOpening>())
                if (!string.IsNullOrEmpty(record?.ClashKey))
                    byKey[record.ClashKey] = record;

            Transform3 hostToStructural = structuralToHost.Inverse();
            var matched = new HashSet<string>(StringComparer.Ordinal);

            foreach (OpeningData opening in detected)
            {
                string key = opening.Clash.Key.ToString();
                if (!byKey.TryGetValue(key, out StoredOpening record))
                {
                    opening.Tracking = TrackingState.New;
                    continue;
                }

                matched.Add(key);
                opening.JobId = record.JobId;
                opening.InstanceUniqueId = record.InstanceUniqueId;

                OpeningSignature.TryParse(record.Signature, out OpeningSignature generatedWith);
                OpeningSignature.TryParse(record.CurrentSignature, out OpeningSignature current);
                OpeningStatus storedStatus = string.Equals(record.Status, "Cut", StringComparison.OrdinalIgnoreCase)
                    ? OpeningStatus.Cut : OpeningStatus.Generated;

                if (generatedWith != null && current != null && !current.Matches(generatedWith))
                {
                    opening.Tracking = TrackingState.ManuallyModified;
                    opening.Status = storedStatus;
                }
                else if (generatedWith == null || !OpeningSignature.Of(opening, hostToStructural).Matches(generatedWith))
                {
                    // Source geometry moved or resized: to be regenerated, so the status stays Detected / Review.
                    opening.Tracking = TrackingState.Updated;
                }
                else
                {
                    opening.Tracking = TrackingState.Existing;
                    opening.Status = storedStatus;
                }
            }

            var structuralNames = new HashSet<string>((structuralCategories ?? Enumerable.Empty<StructuralCategory>()).Select(c => c.ToString()));
            var mepNames = new HashSet<string>((mepCategories ?? Enumerable.Empty<MepCategory>()).Select(c => c.ToString()));
            var mepById = (mep?.Elements ?? (IReadOnlyList<MepElementData>)new MepElementData[0])
                .GroupBy(e => e.UniqueId).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            return byKey.Values
                .Where(r => !matched.Contains(r.ClashKey)
                    && string.Equals(r.MepModelKey, mepModelKey, StringComparison.Ordinal)
                    && structuralNames.Contains(r.StructuralCategory ?? string.Empty)
                    && mepNames.Contains(r.MepCategory ?? string.Empty))
                .Select(r => CheckUnmatched(r, structuralToHost, mepById))
                .ToList();
        }

        /// <summary>
        /// A record whose clash was not found again. Once the structure is cut the MEP element passes
        /// through the hole and no longer clashes, so the record is checked against the opening itself:
        /// the MEP element still runs through it and fits (Existing), runs through it but is no longer
        /// cleared (Updated), or is gone or elsewhere (SourceDeleted).
        /// </summary>
        private static StoredOpeningCheck CheckUnmatched(StoredOpening record, Transform3 structuralToHost,
            IReadOnlyDictionary<string, MepElementData> mepById)
        {
            var check = new StoredOpeningCheck { Record = record, State = TrackingState.SourceDeleted,
                Note = "The clash this opening was made for no longer exists." };

            if (!mepById.TryGetValue(record.MepUniqueId ?? string.Empty, out MepElementData element))
            {
                check.Note = "The MEP element of this opening is no longer in the model.";
                return check;
            }

            OpeningSignature opening = null;
            if (!OpeningSignature.TryParse(record.CurrentSignature, out opening)
                && !OpeningSignature.TryParse(record.Signature, out opening))
                return check;

            Vec3 centre = structuralToHost.Apply(opening.Center);
            Vec3 axis = structuralToHost.ApplyVector(opening.AxisZ).Normalized();
            Vec3 u = structuralToHost.ApplyVector(opening.AxisX).RejectFrom(axis).Normalized();
            if (u.IsNearZero()) u = axis.AnyPerpendicular();
            Vec3 v = axis.Cross(u).Normalized();

            bool passes = false, fits = false;
            foreach (MepSegment segment in element.Segments)
            {
                Vec3 d = segment.Axis.End - segment.Axis.Start;
                double along = d.Dot(axis);
                if (Math.Abs(along) < 1e-9)
                    continue; // parallel to the opening's plane
                double t = (centre - segment.Axis.Start).Dot(axis) / along;
                if (t < -1e-9 || t > 1 + 1e-9)
                    continue;

                Vec3 local = segment.Axis.PointAt(t) - centre;
                double pu = local.Dot(u), pv = local.Dot(v);
                double half = segment.Profile.NominalSizeMm / 2 + Math.Max(0, element.InsulationMm);

                bool inside, clear;
                if (opening.Shape == OpeningShape.Circular)
                {
                    double radial = Math.Sqrt(pu * pu + pv * pv), radius = opening.DiameterMm / 2;
                    inside = radial <= radius;
                    clear = radial + half <= radius + Tolerance;
                }
                else
                {
                    double hw = opening.WidthMm / 2, hh = opening.HeightMm / 2;
                    inside = Math.Abs(pu) <= hw && Math.Abs(pv) <= hh;
                    clear = Math.Abs(pu) + half <= hw + Tolerance && Math.Abs(pv) + half <= hh + Tolerance;
                }

                passes |= inside;
                fits |= inside && clear;
            }

            if (fits)
            {
                check.State = TrackingState.Existing;
                check.Note = "The structure is already cut here; the MEP element runs through the opening.";
            }
            else if (passes)
            {
                check.State = TrackingState.Updated;
                check.Note = "The MEP element moved: it still runs through the opening, but the opening no longer clears it. " +
                             "Delete this void and press Analyze again to size a new one.";
            }
            else
            {
                check.Note = "The MEP element no longer runs through this opening.";
            }
            return check;
        }

        private const double Tolerance = 1.0;
    }
}
