using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.TrackerId.V002.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.TrackerId.V002.Infrastructure.Revit
{
    /// <summary>Outcome of one write run.</summary>
    public sealed class WriteResult
    {
        /// <summary>True when the transaction was committed.</summary>
        public bool Committed { get; set; }

        /// <summary>True when the model changed since the preview, so nothing was written.</summary>
        public bool Stale { get; set; }

        /// <summary>New Tracker_IDs written.</summary>
        public int IdsWritten { get; set; }

        /// <summary>Elements whose Tracker_SourceGuid was stamped.</summary>
        public int GuidsStamped { get; set; }

        /// <summary>Elements whose Tracker_Zone / Level / Grid changed.</summary>
        public int LocationsUpdated { get; set; }

        /// <summary>Elements skipped because they are locked.</summary>
        public int Skipped { get; set; }

        /// <summary>What the parameter setup did.</summary>
        public ParameterSetupResult Setup { get; set; }

        /// <summary>Why nothing was written, when <see cref="Committed"/> is false.</summary>
        public List<string> Errors { get; } = new();
    }

    /// <summary>
    /// Writes a confirmed plan in one transaction: all or nothing. Before writing it checks that the
    /// model still matches the preview (every element still holds the Tracker_ID it was planned with,
    /// and no new ID has been taken meanwhile); if not, nothing is written and the caller re-reads.
    /// </summary>
    public static class TrackerWriter
    {
        /// <summary>
        /// Writes <paramref name="rows"/> (elements of <paramref name="categories"/>). Locked and duplicate
        /// rows are skipped. Must run in a Revit API context with no transaction open.
        /// </summary>
        public static WriteResult Write(Document doc, IReadOnlyCollection<Category> categories, IReadOnlyList<PlanRow> rows,
            bool writeLocation, bool writeSourceGuid)
        {
            var result = new WriteResult();
            var writable = rows.Where(r => r.Action != PlanAction.Locked && r.Action != PlanAction.Duplicate).ToList();
            result.Skipped = rows.Count - writable.Count;

            if (rows.Any(r => r.Action == PlanAction.Duplicate))
            {
                result.Errors.Add("Some new IDs repeat. Fix the format first.");
                return result;
            }

            // A new ID taken by someone else since the preview (another user synced, or an undo) means the plan is out of date.
            var taken = new HashSet<string>(ModelReader.ReadIdHolders(doc).Select(h => h.TrackerId), StringComparer.OrdinalIgnoreCase);
            var clash = writable.FirstOrDefault(r => r.GetsNewId && taken.Contains(r.ResultId));
            if (clash != null)
            {
                result.Stale = true;
                result.Errors.Add($"'{clash.ResultId}' is already used in the model. The preview was refreshed; check it and write again.");
                return result;
            }

            var names = new List<string> { TrackerParameters.TrackerId };
            if (writeSourceGuid) names.Add(TrackerParameters.SourceGuid);
            if (writeLocation) names.AddRange(new[] { TrackerParameters.Zone, TrackerParameters.Level, TrackerParameters.Grid });

            using var transaction = new Transaction(doc, "Tracker ID: write IDs");
            transaction.Start();

            result.Setup = TrackerParameterService.Ensure(doc, categories, names);
            if (result.Setup.Errors.Count > 0)
            {
                result.Errors.AddRange(result.Setup.Errors);
                transaction.RollBack();
                return result;
            }
            doc.Regenerate();

            foreach (var row in writable)
            {
                var element = doc.GetElement(new ElementId(row.Element.ElementId));
                if (element == null)
                {
                    result.Stale = true;
                    result.Errors.Add($"Element {row.Element.ElementId} no longer exists.");
                    break;
                }

                string current = element.LookupParameter(TrackerParameters.TrackerId)?.AsString()?.Trim() ?? string.Empty;
                if (!string.Equals(current, row.Element.TrackerId, StringComparison.Ordinal))
                {
                    result.Stale = true;
                    result.Errors.Add($"The Tracker_ID of element {row.Element.ElementId} changed since the preview.");
                    break;
                }

                string error = null;
                if (row.GetsNewId)
                {
                    error = Set(element, TrackerParameters.TrackerId, row.ResultId);
                    if (error == null) result.IdsWritten++;
                }

                bool stamp = writeSourceGuid && (row.GetsNewId || row.Action == PlanAction.Adopt);
                if (error == null && stamp && !string.Equals(row.Element.SourceGuid, element.UniqueId, StringComparison.Ordinal))
                {
                    error = Set(element, TrackerParameters.SourceGuid, element.UniqueId);
                    if (error == null) result.GuidsStamped++;
                }

                if (error == null && writeLocation)
                {
                    bool changed = false;
                    error = SetIfChanged(element, TrackerParameters.Zone, row.Zone, ref changed)
                            ?? SetIfChanged(element, TrackerParameters.Level, row.Level, ref changed)
                            ?? SetIfChanged(element, TrackerParameters.Grid, row.Grid, ref changed);
                    if (error == null && changed) result.LocationsUpdated++;
                }

                if (error != null)
                {
                    result.Errors.Add(error);
                    break;
                }
            }

            if (result.Errors.Count > 0)
            {
                transaction.RollBack();
                return result;
            }

            result.Committed = transaction.Commit() == TransactionStatus.Committed;
            if (!result.Committed) result.Errors.Add("Revit did not accept the change. Nothing was written.");
            return result;
        }

        private static string SetIfChanged(Element element, string name, string value, ref bool changed)
        {
            string current = element.LookupParameter(name)?.AsString() ?? string.Empty;
            if (string.Equals(current, value ?? string.Empty, StringComparison.Ordinal)) return null;
            changed = true;
            return Set(element, name, value);
        }

        // Returns null on success, or why the value could not be set.
        private static string Set(Element element, string name, string value)
        {
            var p = element.LookupParameter(name);
            if (p == null) return $"Element {element.Id.Value} has no '{name}' parameter.";
            if (p.IsReadOnly) return $"'{name}' is read-only on element {element.Id.Value}.";
            return p.Set(value ?? string.Empty) ? null : $"Revit refused to set '{name}' on element {element.Id.Value}.";
        }
    }
}
