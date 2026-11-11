using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.BubbleAutoRenumber.V009.Core.Models;

namespace Revit26_Plugin_Revised.BubbleAutoRenumber.V009.Core.Services
{
    /// <summary>
    /// Decides the whole run before anything is written: which views are in scope,
    /// what order they sit in, what number each one gets, and which placeholder it
    /// passes through on the way. No Revit API — the same plan drives the window's
    /// live preview and the actual write, so the grid's "New #" column is not an
    /// estimate of what will happen, it is what will happen.
    /// </summary>
    public static class RenumberPlanner
    {
        /// <summary>Shown in the New # column for a row that will not be touched.</summary>
        public const string NoNumber = "—";

        public const string ReadOnlyReason = "Detail number is read-only";

        public static RenumberPlan Plan(IReadOnlyList<BubbleRow> allRowsOnSheet, RenumberOptions options)
        {
            var plan = new RenumberPlan();

            if (allRowsOnSheet == null || options == null) return plan;

            // In scope, in reading order.
            List<BubbleRow> inScope = RowOrderingService.Order(
                allRowsOnSheet.Where(options.Includes),
                options.ThresholdFt);

            // Only writable rows take a number from the sequence; a read-only one keeps
            // what it has and does not consume a number.
            List<BubbleRow> targets = inScope.Where(r => r.IsWritable).ToList();
            var targetIds = new HashSet<long>(targets.Select(r => r.ViewId));

            // Numbers held by views that are NOT being renumbered — out of scope, or in
            // scope but read-only. These are the only genuinely taken numbers, and
            // seeding from them rather than from everything is the V008 fix.
            var reserved = new HashSet<string>();

            foreach (BubbleRow row in allRowsOnSheet)
            {
                if (targetIds.Contains(row.ViewId)) continue;
                if (string.IsNullOrWhiteSpace(row.CurrentNumber)) continue;

                reserved.Add(row.CurrentNumber.Trim());
            }

            List<string> finals = DetailNumberAllocator.Allocate(
                targets.Count, options.StartNumber, reserved, options.Style);

            List<string> temporaries = DetailNumberAllocator.AllocateTemporaries(
                targets.Count, reserved, finals);

            int next = 0;

            foreach (BubbleRow row in inScope)
            {
                if (row.IsWritable)
                {
                    var assignment = new RenumberAssignment
                    {
                        ViewId = row.ViewId,
                        ViewName = row.ViewName,
                        KindLabel = row.KindLabel,
                        PreviousNumber = row.CurrentNumber,
                        TempNumber = temporaries[next],
                        FinalNumber = finals[next]
                    };

                    plan.Assignments.Add(assignment);

                    plan.Preview.Add(new RenumberPreviewRow
                    {
                        ViewId = row.ViewId,
                        ViewName = row.ViewName,
                        KindLabel = row.KindLabel,
                        PillGroup = row.PillGroup,
                        CurrentNumber = string.IsNullOrEmpty(row.CurrentNumber) ? NoNumber : row.CurrentNumber,
                        NewNumber = assignment.FinalNumber,
                        IsSkipped = false
                    });

                    next++;
                }
                else
                {
                    plan.Skips.Add(new RenumberSkip
                    {
                        ViewId = row.ViewId,
                        ViewName = row.ViewName,
                        KindLabel = row.KindLabel,
                        CurrentNumber = row.CurrentNumber,
                        Reason = ReadOnlyReason
                    });

                    plan.Preview.Add(new RenumberPreviewRow
                    {
                        ViewId = row.ViewId,
                        ViewName = row.ViewName,
                        KindLabel = row.KindLabel,
                        PillGroup = row.PillGroup,
                        CurrentNumber = string.IsNullOrEmpty(row.CurrentNumber) ? NoNumber : row.CurrentNumber,
                        NewNumber = NoNumber,
                        IsSkipped = true,
                        SkipReason = ReadOnlyReason
                    });
                }
            }

            return plan;
        }
    }
}
