using Revit26_Plugin_Revised.TrackerId.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Revit26_Plugin_Revised.TrackerId.V001.Core.Services
{
    /// <summary>
    /// Read-only audit of every tracked element in the model. It reuses the planner's rules, so the
    /// health check and step 3 always agree on what is a copy, a duplicate or a move.
    /// </summary>
    public static class HealthChecker
    {
        /// <summary>
        /// Checks <paramref name="tracked"/> (every placed element of the categories bound to
        /// Tracker_ID). <paramref name="statusFile"/> is optional; without it the status checks are skipped.
        /// </summary>
        public static List<HealthIssue> Check(IReadOnlyList<ElementSnapshot> tracked, IReadOnlyList<TrackerIdHolder> holders,
            TrackerFormat format, GridLocator grids, StatusFileResult statusFile)
        {
            var issues = new List<HealthIssue>();
            var rows = TrackerIdPlanner.Plan(tracked, format, grids, holders);

            var groups = (holders ?? Array.Empty<TrackerIdHolder>())
                .GroupBy(h => h.TrackerId, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows)
            {
                var e = row.Element;
                bool hasId = !string.IsNullOrWhiteSpace(e.TrackerId);

                if (!hasId)
                {
                    issues.Add(Issue(HealthIssueKind.MissingId, e, "No Tracker_ID yet. Run step 3 on its category."));
                    continue;
                }

                if (row.Action == PlanAction.Copy)
                    issues.Add(Issue(HealthIssueKind.Copy, e, row.Note));
                else if (groups.TryGetValue(e.TrackerId, out int count))
                    issues.Add(Issue(HealthIssueKind.DuplicateId, e,
                        $"Shared by {count} elements. This one keeps it; the others are reported as copies and get new IDs in step 3."));

                if (row.Action != PlanAction.Copy && TrackerIdPlanner.IsMoved(row))
                    issues.Add(Issue(HealthIssueKind.Moved, e,
                        $"Was {Describe(e.StoredZone, e.StoredLevel, e.StoredGrid)}, now {Describe(row.Zone, row.Level, row.Grid)}. Step 3 updates the location parameters."));

                if (row.GridCheck)
                    issues.Add(Issue(HealthIssueKind.FarFromGrid, e,
                        $"Nearest grid {row.Grid} is {row.GridDistanceM?.ToString("0.0", CultureInfo.InvariantCulture)} m away (limit {format.GridToleranceM:0.#} m)."));

                if (statusFile != null && !statusFile.Records.ContainsKey(e.TrackerId))
                    issues.Add(Issue(HealthIssueKind.NoStatus, e, "Not listed in the status file."));
            }

            if (statusFile != null)
            {
                foreach (string id in StatusMerger.IdsNotInModel(statusFile, holders))
                {
                    issues.Add(new HealthIssue
                    {
                        Kind = HealthIssueKind.IdNotInModel,
                        TrackerId = id,
                        Detail = $"Listed in the status file (row {statusFile.Records[id].Row}) but no element in this model has it. Deleted, or in another model?"
                    });
                }
            }
            return issues;
        }

        private static HealthIssue Issue(HealthIssueKind kind, ElementSnapshot e, string detail) => new()
        {
            Kind = kind,
            ElementId = e.ElementId,
            TrackerId = e.TrackerId ?? string.Empty,
            Category = e.CategoryName ?? string.Empty,
            CategoryId = e.CategoryId,
            Type = string.IsNullOrEmpty(e.TypeMark) ? e.TypeName : $"{e.TypeMark} · {e.TypeName}",
            Detail = detail ?? string.Empty
        };

        private static string Describe(string zone, string level, string grid) =>
            string.Join(" / ", new[] { zone, level, grid }.Where(v => !string.IsNullOrWhiteSpace(v)));
    }
}
