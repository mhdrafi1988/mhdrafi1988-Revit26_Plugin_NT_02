using Revit26_Plugin_Revised.TrackerId.V002.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.TrackerId.V002.Core.Services
{
    /// <summary>
    /// Matches status rows to elements by Tracker_ID (without regard to case, as Power BI does) and
    /// decides what to write. A missing row never clears an element's status: the file may list only
    /// part of the project.
    /// </summary>
    public static class StatusMerger
    {
        /// <summary>One plan row per element that has a Tracker_ID.</summary>
        public static List<StatusPlanRow> Plan(IEnumerable<ElementSnapshot> elements, StatusFileResult file)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));
            var rows = new List<StatusPlanRow>();

            foreach (var e in elements ?? Enumerable.Empty<ElementSnapshot>())
            {
                if (string.IsNullOrWhiteSpace(e.TrackerId)) continue;
                file.Records.TryGetValue(e.TrackerId, out var record);

                var change = !string.IsNullOrEmpty(e.LockReason) ? StatusChange.Locked
                           : record == null ? StatusChange.NoRow
                           : Matches(e, record) ? StatusChange.Unchanged
                           : StatusChange.Set;
                rows.Add(new StatusPlanRow { Element = e, Record = record, Change = change });
            }
            return rows;
        }

        /// <summary>Tracker_IDs the file lists that no element in the model has.</summary>
        public static List<string> IdsNotInModel(StatusFileResult file, IEnumerable<TrackerIdHolder> holders)
        {
            var inModel = new HashSet<string>((holders ?? Enumerable.Empty<TrackerIdHolder>()).Select(h => h.TrackerId), StringComparer.OrdinalIgnoreCase);
            return file.Records.Keys.Where(id => !inModel.Contains(id)).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // Progress counts as unchanged when the file has no progress (the column may be missing).
        private static bool Matches(ElementSnapshot e, StatusRecord r) =>
            string.Equals(e.StoredStatus ?? string.Empty, r.Status ?? string.Empty, StringComparison.Ordinal) &&
            (!r.Progress.HasValue || (e.StoredProgress.HasValue && Math.Abs(e.StoredProgress.Value - r.Progress.Value) < 0.005)) &&
            (!r.Date.HasValue || string.Equals(e.StoredStatusDate ?? string.Empty, r.DateText, StringComparison.Ordinal));
    }
}
