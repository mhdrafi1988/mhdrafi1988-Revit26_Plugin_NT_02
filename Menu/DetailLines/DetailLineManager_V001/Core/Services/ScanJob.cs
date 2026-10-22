using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.DetailLineManager.V001.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Revit26_Plugin_Revised.DetailLineManager.V001.Core.Services
{
    /// <summary>
    /// One Find lines run (or a line-style usage count), split into short steps. The window runs one
    /// step per external-event call and raises the event again until <see cref="Step"/> returns true,
    /// so Revit stays responsive, progress shows between steps and Cancel takes effect at the next step.
    /// Call <see cref="Step"/> only in the Revit API context.
    /// </summary>
    public sealed class ScanJob
    {
        private enum Stage { Collect, Rows, Analyse, Usage, Finish, Done }

        private readonly SearchScope _scope;
        private readonly double _toleranceMm;
        private readonly bool _countUsage;
        private readonly Action<LogLevel, string> _log;

        private Stage _stage;
        private List<ElementId> _ids = new();
        private int _index;
        private int _unreadable;
        private readonly List<DuplicateDetector.Candidate> _candidates = new();
        private readonly List<(int Index, XYZ Start, XYZ End)> _straight = new();
        private EditabilityService _editability;
        private readonly DetailLineScanner.RowCache _cache = new();
        private UsageCounter _usage;

        /// <summary>Creates a search job.</summary>
        /// <param name="scope">Which lines to collect.</param>
        /// <param name="toleranceMm">Short-line, duplicate and overlap tolerance in mm.</param>
        /// <param name="countUsage">Count line-style usage across the project (needed for Delete unused).</param>
        /// <param name="usageOnly">Only count line-style usage; collect no lines.</param>
        /// <param name="log">Receives progress messages, skips and warnings.</param>
        public ScanJob(SearchScope scope, double toleranceMm, bool countUsage, bool usageOnly, Action<LogLevel, string> log)
        {
            _scope = scope;
            _toleranceMm = Math.Max(0, toleranceMm);
            _countUsage = countUsage || usageOnly;
            _log = log ?? ((_, _) => { });
            _stage = usageOnly ? Stage.Usage : Stage.Collect;
            Result.UsageOnly = usageOnly;
        }

        /// <summary>The result; complete once <see cref="Step"/> has returned true.</summary>
        public ScanResult Result { get; } = new();

        /// <summary>True once <see cref="Cancel"/> was called.</summary>
        public bool IsCancelled { get; private set; }

        /// <summary>What the job is doing, for the status strip.</summary>
        public string Phase { get; private set; } = "Starting";

        /// <summary>Items done in the current phase.</summary>
        public int Done { get; private set; }

        /// <summary>Items in the current phase, or 0 when unknown.</summary>
        public int Total { get; private set; }

        /// <summary>Asks the job to stop; the next <see cref="Step"/> call does nothing.</summary>
        public void Cancel() => IsCancelled = true;

        /// <summary>
        /// Does up to <paramref name="budget"/> of work. Returns true when the job is finished
        /// (or was cancelled).
        /// </summary>
        public bool Step(UIDocument uiDoc, TimeSpan budget)
        {
            if (IsCancelled) return true;
            var doc = uiDoc.Document;
            var clock = Stopwatch.StartNew();

            while (_stage != Stage.Done && clock.Elapsed < budget)
            {
                switch (_stage)
                {
                    case Stage.Collect:
                        Collect(uiDoc);
                        return false; // show the count before reading
                    case Stage.Rows:
                        ReadRows(doc, clock, budget);
                        break;
                    case Stage.Analyse:
                        Analyse();
                        return false;
                    case Stage.Usage:
                        CountUsage(doc, budget - clock.Elapsed);
                        break;
                    case Stage.Finish:
                        Finish(doc);
                        break;
                }
            }
            return _stage == Stage.Done;
        }

        private void Collect(UIDocument uiDoc)
        {
            Phase = "Collecting detail lines";
            var doc = uiDoc.Document;
            var sketchMembers = DetailLineScanner.CollectSketchMembers(doc);
            _ids = DetailLineScanner.CollectIds(uiDoc, _scope, sketchMembers, _log, out string scopeText, out int sketchSkipped);
            Result.ScopeText = scopeText;
            _editability = new EditabilityService(doc);

            int links = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).GetElementCount();
            _log(LogLevel.Info, $"Scope: {scopeText} · {_ids.Count} detail line(s) collected"
                + (links > 0 ? $" · {links} linked model instance(s) not searched" : string.Empty)
                + (sketchSkipped > 0 ? $" · {sketchSkipped} sketch boundary line(s) left out" : string.Empty) + ".");

            _index = 0;
            Total = _ids.Count;
            Done = 0;
            _stage = Stage.Rows;
        }

        private void ReadRows(Document doc, Stopwatch clock, TimeSpan budget)
        {
            Phase = "Reading lines";
            while (_index < _ids.Count)
            {
                var id = _ids[_index++];
                if (doc.GetElement(id) is CurveElement curve && curve.IsValidObject && DetailLineScanner.IsDetailLine(curve))
                {
                    try
                    {
                        var row = DetailLineScanner.BuildRow(doc, curve, _editability, _cache, out Curve geometry);
                        int index = Result.Rows.Count;
                        _candidates.Add(DuplicateDetector.Describe(index, row.ViewId, row.Curve, geometry));
                        if (geometry is Line line && line.IsBound)
                            _straight.Add((index, line.GetEndPoint(0), line.GetEndPoint(1)));
                        Result.Rows.Add(row);
                    }
                    catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                    {
                        _unreadable++;
                        if (_unreadable <= 20)
                            _log(LogLevel.Warning, $"Skipped line {id.Value}: could not read it ({ex.Message}).");
                    }
                }

                if ((_index & 63) == 0 && clock.Elapsed > budget) break;
            }
            Done = _index;
            if (_index >= _ids.Count)
            {
                if (_unreadable > 20)
                    _log(LogLevel.Warning, $"… {_unreadable - 20} more unreadable line(s) skipped.");
                _stage = Stage.Analyse;
                Total = Done = 0;
            }
        }

        private void Analyse()
        {
            Phase = "Finding duplicates and overlaps";
            double tolFeet = UnitUtils.ConvertToInternalUnits(Math.Max(_toleranceMm, 0.001), UnitTypeId.Millimeters);

            var clusters = DuplicateDetector.FindClusters(_candidates, tolFeet);
            foreach (var pair in clusters)
                Result.Rows[pair.Key].DuplicateCluster = pair.Value;
            Result.DuplicateClusters = clusters.Values.Distinct().Count();

            var segments = _straight.Select(s => new OverlapDetector.Segment
            {
                Index = s.Index,
                IdValue = Result.Rows[s.Index].IdValue,
                ViewKey = Result.Rows[s.Index].ViewId?.Value ?? 0,
                StyleKey = Result.Rows[s.Index].StyleCategoryId?.Value ?? -1,
                DuplicateCluster = Result.Rows[s.Index].DuplicateCluster,
                Start = s.Start,
                End = s.End
            });
            var overlaps = OverlapDetector.Find(segments, tolFeet);
            foreach (var pair in overlaps.Groups)
                Result.Rows[pair.Key].OverlapGroup = pair.Value;
            foreach (var pair in overlaps.CoveredBy)
                Result.Rows[pair.Key].CoveredBy = Result.Rows[pair.Value].IdValue;
            Result.OverlapGroups = overlaps.Groups.Values.Distinct().Count();
            if (overlaps.ApproximatedBuckets > 0)
                _log(LogLevel.Info, $"{overlaps.ApproximatedBuckets} view/style set(s) have over 3000 straight lines; "
                    + "overlaps there were checked per direction and a few may be missed.");

            foreach (var row in Result.Rows)
                row.ApplyTolerance(_toleranceMm);

            _stage = _countUsage ? Stage.Usage : Stage.Finish;
        }

        private void CountUsage(Document doc, TimeSpan budget)
        {
            Phase = "Counting line style usage";
            _usage ??= new UsageCounter(doc);
            bool done = _usage.Step(budget > TimeSpan.Zero ? budget : TimeSpan.FromMilliseconds(1));
            Done = _usage.Done;
            Total = _usage.Total;
            if (done) _stage = Stage.Finish;
        }

        private void Finish(Document doc)
        {
            Phase = "Finishing";
            Result.UsageCounted = _countUsage;
            Result.Styles.AddRange(LineStyleService.LoadStyles(doc, _countUsage ? _usage?.Usage ?? new Dictionary<ElementId, int>() : null));

            string styles = _countUsage
                ? $"Line styles: {Result.Styles.Count} ({Result.Styles.Count(s => s.IsUnused)} unused, built-in styles excluded)."
                : $"Line styles: {Result.Styles.Count} (usage not counted; open the Delete unused tab to count it).";

            if (Result.UsageOnly)
            {
                _log(LogLevel.Success, styles);
            }
            else
            {
                var rows = Result.Rows;
                _log(LogLevel.Success,
                    $"Found {rows.Count} line(s): {rows.Count(r => r.IsDuplicate)} in {Result.DuplicateClusters} duplicate cluster(s), "
                    + $"{rows.Count(r => r.IsOverlapping)} in {Result.OverlapGroups} overlap group(s) ({rows.Count(r => r.IsCovered)} fully covered), "
                    + $"{rows.Count(r => r.IsShort)} shorter than {_toleranceMm:0.##} mm, "
                    + $"{rows.Count(r => r.InGroup)} in groups, {rows.Count(r => r.IsOwnedByOther)} owned by other users. " + styles);
            }
            _stage = Stage.Done;
        }
    }
}
