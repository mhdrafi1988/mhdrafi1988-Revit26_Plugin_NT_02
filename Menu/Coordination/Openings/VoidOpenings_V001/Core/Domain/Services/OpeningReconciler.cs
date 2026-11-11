using System;
using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Services
{
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
        /// <returns>Records to show as "source deleted".</returns>
        public static IReadOnlyList<StoredOpening> Reconcile(IEnumerable<OpeningData> detected, IEnumerable<StoredOpening> stored,
            Transform3 structuralToHost, string mepModelKey,
            IEnumerable<StructuralCategory> structuralCategories, IEnumerable<MepCategory> mepCategories)
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

            return byKey.Values
                .Where(r => !matched.Contains(r.ClashKey)
                    && string.Equals(r.MepModelKey, mepModelKey, StringComparison.Ordinal)
                    && structuralNames.Contains(r.StructuralCategory ?? string.Empty)
                    && mepNames.Contains(r.MepCategory ?? string.Empty))
                .ToList();
        }
    }
}
