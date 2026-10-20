using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.TrackerId.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Revit26_Plugin_Revised.TrackerId.V001.Infrastructure.Revit
{
    /// <summary>Outcome of a status import.</summary>
    public sealed class StatusWriteResult
    {
        /// <summary>True when the transaction was committed.</summary>
        public bool Committed { get; set; }

        /// <summary>Elements whose status parameters were written.</summary>
        public int Written { get; set; }

        /// <summary>Locked elements left out.</summary>
        public int Skipped { get; set; }

        /// <summary>What the parameter setup did.</summary>
        public ParameterSetupResult Setup { get; set; }

        /// <summary>Why nothing was written, when <see cref="Committed"/> is false.</summary>
        public List<string> Errors { get; } = new();
    }

    /// <summary>
    /// Writes Tracker_Status, Tracker_Progress and Tracker_StatusDate from a confirmed status plan, in one
    /// transaction: all or nothing. Only rows marked <see cref="StatusChange.Set"/> are written; a blank
    /// progress or date in the file leaves that parameter as it is.
    /// </summary>
    public static class StatusWriter
    {
        /// <summary>Writes <paramref name="rows"/>. Must run in a Revit API context with no transaction open.</summary>
        public static StatusWriteResult Write(Document doc, IReadOnlyList<StatusPlanRow> rows)
        {
            var result = new StatusWriteResult { Skipped = rows.Count(r => r.Change == StatusChange.Locked) };
            var toWrite = rows.Where(r => r.Change == StatusChange.Set && r.Record != null).ToList();
            if (toWrite.Count == 0) return result;

            var elements = new List<(StatusPlanRow Row, Element Element)>();
            foreach (var row in toWrite)
            {
                var element = doc.GetElement(new ElementId(row.Element.ElementId));
                if (element == null)
                {
                    result.Errors.Add($"Element {row.Element.ElementId} no longer exists. Read the status file again.");
                    return result;
                }
                elements.Add((row, element));
            }

            var categories = elements.Select(e => e.Element.Category).Where(c => c != null)
                .GroupBy(c => c.Id.Value).Select(g => g.First()).ToList();

            using var transaction = new Transaction(doc, "Tracker ID: import status");
            transaction.Start();

            result.Setup = TrackerParameterService.Ensure(doc, categories, TrackerParameters.StatusNames);
            if (result.Setup.Errors.Count > 0)
            {
                result.Errors.AddRange(result.Setup.Errors);
                transaction.RollBack();
                return result;
            }
            doc.Regenerate();

            foreach (var (row, element) in elements)
            {
                string error = SetText(element, TrackerParameters.Status, row.Record.Status)
                               ?? (row.Record.Progress.HasValue ? SetNumber(element, TrackerParameters.Progress, row.Record.Progress.Value) : null)
                               ?? (row.Record.Date.HasValue ? SetText(element, TrackerParameters.StatusDate, row.Record.DateText) : null);
                if (error != null)
                {
                    result.Errors.Add(error);
                    transaction.RollBack();
                    return result;
                }
                result.Written++;
            }

            result.Committed = transaction.Commit() == TransactionStatus.Committed;
            if (!result.Committed) result.Errors.Add("Revit did not accept the change. Nothing was written.");
            return result;
        }

        private static string SetText(Element element, string name, string value)
        {
            var p = Writable(element, name, out string error);
            if (p == null) return error;
            return p.Set(value ?? string.Empty) ? null : $"Revit refused to set '{name}' on element {element.Id.Value}.";
        }

        // Tracker_Progress is a Number parameter; one made by hand as Text gets the number as text.
        private static string SetNumber(Element element, string name, double value)
        {
            var p = Writable(element, name, out string error);
            if (p == null) return error;
            bool ok = p.StorageType switch
            {
                StorageType.Double => p.Set(value),
                StorageType.Integer => p.Set((int)Math.Round(value)),
                _ => p.Set(value.ToString("0.##", CultureInfo.InvariantCulture))
            };
            return ok ? null : $"Revit refused to set '{name}' on element {element.Id.Value}.";
        }

        private static Parameter Writable(Element element, string name, out string error)
        {
            error = null;
            var p = element.LookupParameter(name);
            if (p == null) error = $"Element {element.Id.Value} has no '{name}' parameter.";
            else if (p.IsReadOnly) error = $"'{name}' is read-only on element {element.Id.Value}.";
            return error == null ? p : null;
        }
    }
}
