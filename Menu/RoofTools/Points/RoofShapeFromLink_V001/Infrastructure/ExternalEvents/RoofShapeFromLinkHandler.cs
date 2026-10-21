using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.RoofShapeFromLink.V001.Core.Models;
using Revit26_Plugin_Revised.RoofShapeFromLink.V001.Core.Services;
using Revit26_Plugin_Revised.RoofShapeFromLink.V001.Infrastructure.Helpers;
using Revit26_Plugin_Revised.RoofShapeFromLink.V001.UI.ViewModels;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.RoofShapeFromLink.V001.Infrastructure.ExternalEvents
{
    /// <summary>
    /// Runs the tool's Revit work (read links, pick roofs, write elevations) inside a valid API context.
    /// The view model sets <see cref="Pending"/> and raises the event.
    /// </summary>
    public sealed class RoofShapeFromLinkHandler : IExternalEventHandler
    {
        /// <summary>What the next external event should do.</summary>
        public enum Job
        {
            /// <summary>Read linked elevations into the grid.</summary>
            Compute,

            /// <summary>Pick other footprint roofs, then read them.</summary>
            PickRoofs,

            /// <summary>Write the included rows to the roofs.</summary>
            Apply
        }

        /// <summary>Two points closer than this in X,Y (feet) are the same shape-editing point.</summary>
        private const double SamePointToleranceFt = 0.01;

        /// <summary>Applied elevation may differ from the target by this much (feet) before it is reported.</summary>
        private const double ReadbackToleranceFt = 0.001;

        private readonly RoofShapeFromLinkViewModel _vm;

        /// <summary>The job to run on the next <see cref="Execute"/>.</summary>
        public Job Pending { get; set; }

        /// <summary>Creates the handler for <paramref name="vm"/>.</summary>
        public RoofShapeFromLinkHandler(RoofShapeFromLinkViewModel vm) => _vm = vm;

        /// <inheritdoc/>
        public void Execute(UIApplication app) =>
            ToolGuard.RunHandler(GetType(), app, a =>
            {
                try
                {
                    switch (Pending)
                    {
                        case Job.PickRoofs: PickRoofs(a); break;
                        case Job.Apply: Apply(a); break;
                        default: Compute(a); break;
                    }
                }
                finally
                {
                    _vm.SetBusy(false, null);
                }
            });

        /// <inheritdoc/>
        public string GetName() => "Roof Shape From Link";

        // ── Links ─────────────────────────────────────────────────────────────

        /// <summary>Fills the link list with the loaded link instances. Called once when the window opens.</summary>
        public static List<LinkItem> ReadLinks(Document doc)
        {
            var items = new List<LinkItem>();
            foreach (RevitLinkInstance instance in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
            {
                if (instance.GetLinkDocument() == null) continue;   // unloaded links cannot be read
                items.Add(new LinkItem
                {
                    InstanceId = instance.Id.Value,
                    Name = instance.Name,
                    RoofCount = LinkedRoofSurfaceService.CountRoofs(instance)
                });
            }
            return items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // ── Compute ───────────────────────────────────────────────────────────

        private void Compute(UIApplication app)
        {
            Document doc = app.ActiveUIDocument.Document;

            if (!HostRoofService.IsPlanView(doc.ActiveView))
            {
                Warn("The active view is not a plan view. Open a floor, structural or ceiling plan and try again.");
                return;
            }

            List<FootPrintRoof> roofs = HostRoofService.FilterFootprintRoofs(doc, _vm.RoofIds, out int ignored);
            if (roofs.Count == 0)
            {
                Warn("None of the selected roofs is a footprint roof (or they were deleted). Use Change selection.");
                return;
            }
            if (ignored > 0) _vm.Log(LogLevel.Warning, $"{ignored} selected element(s) ignored: not footprint roofs.");

            List<RevitLinkInstance> links = _vm.Links
                .Where(l => l.IsSelected)
                .Select(l => doc.GetElement(new ElementId(l.InstanceId)) as RevitLinkInstance)
                .Where(l => l != null && l.GetLinkDocument() != null)
                .ToList();
            if (links.Count == 0)
            {
                Warn("Tick at least one loaded linked file to read elevations from.");
                return;
            }

            _vm.SetBusy(true, "Reading linked roofs…");
            var service = new LinkedRoofSurfaceService(links, _vm.BoundaryPointsOnly);
            foreach (string w in service.Warnings) _vm.Log(LogLevel.Warning, w);

            var rows = new List<ShapePointRow>();
            int done = 0;
            foreach (FootPrintRoof roof in roofs)
            {
                List<HostPoint> points = HostRoofService.ReadPoints(roof, out string reason);
                if (points.Count == 0)
                    _vm.Log(LogLevel.Warning, $"{HostRoofService.Label(roof)}: skipped, {reason}.");

                foreach (HostPoint p in points)
                {
                    LinkedCandidate c = service.Find(p.Position);
                    rows.Add(BuildRow(p, c));
                }
                _vm.Progress = ++done * 100.0 / roofs.Count;
            }

            _vm.SetRows(rows);
            int matched = rows.Count(r => r.SurfaceZ.HasValue || r.NearestZ.HasValue);
            _vm.SummaryText = $"{rows.Count} point(s) on {roofs.Count} roof(s); {matched} with a linked elevation.";
            _vm.Log(LogLevel.Info, _vm.SummaryText);
            if (rows.Count > matched)
                _vm.Log(LogLevel.Warning, $"{rows.Count - matched} point(s) have no linked roof above them: type a New Z by hand or leave them out.");
        }

        private ShapePointRow BuildRow(HostPoint p, LinkedCandidate c)
        {
            var row = new ShapePointRow
            {
                RoofId = p.RoofId.Value,
                RoofLabel = p.RoofLabel,
                Index = p.Index,
                XFt = p.Position.X,
                YFt = p.Position.Y,
                X = _vm.ToDisplay(p.Position.X),
                Y = _vm.ToDisplay(p.Position.Y),
                LinkedRoof = c.LinkedRoof,
                SurfaceZ = c.SurfaceZ.HasValue ? _vm.ToDisplay(c.SurfaceZ.Value) : null,
                NearestZ = c.NearestZ.HasValue ? _vm.ToDisplay(c.NearestZ.Value) : null,
                NearestDistance = c.NearestDistanceFt.HasValue ? _vm.ToDisplay(c.NearestDistanceFt.Value) : null,
                Note = c.Note,
                CurrentZ = _vm.ToDisplay(p.Position.Z),
                Offset = _vm.GlobalOffset
            };
            row.Source = row.HasCandidate(_vm.GlobalSource)
                ? _vm.GlobalSource
                : row.HasCandidate(ShapePointRow.SourceSurface) ? ShapePointRow.SourceSurface : ShapePointRow.SourceNearest;
            row.Recalculate();
            row.Include = row.NewZ.HasValue;   // rows with nothing found start unticked
            return row;
        }

        // ── Pick ──────────────────────────────────────────────────────────────

        private void PickRoofs(UIApplication app)
        {
            UIDocument uidoc = app.ActiveUIDocument;
            IList<Reference> picked;
            _vm.BeginPick();
            try
            {
                picked = uidoc.Selection.PickObjects(Autodesk.Revit.UI.Selection.ObjectType.Element,
                    new FootprintRoofFilter(), "Select footprint roofs, then click Finish");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return;
            }
            finally
            {
                _vm.EndPick();
            }

            if (picked == null || picked.Count == 0) return;
            _vm.RoofIds = picked.Select(r => r.ElementId).ToList();
            _vm.RoofSummary = $"{_vm.RoofIds.Count} footprint roof(s) selected";
            Compute(app);
        }

        // ── Apply ─────────────────────────────────────────────────────────────

        private void Apply(UIApplication app)
        {
            Document doc = app.ActiveUIDocument.Document;
            if (!HostRoofService.IsPlanView(doc.ActiveView))
            {
                Warn("The active view is not a plan view. Open a floor, structural or ceiling plan and try again.");
                return;
            }

            var groups = _vm.Rows.Where(r => r.Include && r.NewZ.HasValue).GroupBy(r => r.RoofId).ToList();
            if (groups.Count == 0)
            {
                Warn("Nothing to apply.");
                return;
            }

            _vm.SetBusy(true, "Applying…");
            int roofsDone = 0, pointsDone = 0, pointsFailed = 0, step = 0;

            using (var tg = new TransactionGroup(doc, "Roof Shape From Link"))
            {
                tg.Start();
                foreach (var group in groups)
                {
                    _vm.Progress = step++ * 100.0 / groups.Count;
                    var roof = doc.GetElement(new ElementId(group.Key)) as FootPrintRoof;
                    string blocker = HostRoofService.EditBlocker(doc, roof);
                    if (blocker != null)
                    {
                        _vm.Log(LogLevel.Error, $"Roof [{group.Key}] skipped: {blocker}.");
                        pointsFailed += group.Count();
                        continue;
                    }

                    using (var tx = new Transaction(doc, "Set roof shape points"))
                    {
                        tx.Start();
                        try
                        {
                            SlabShapeEditor editor = roof.GetSlabShapeEditor();
                            if (editor == null) throw new InvalidOperationException("shape editing is not available for this roof");
                            if (!editor.IsEnabled)
                            {
                                editor.Enable();
                                doc.Regenerate();
                                editor = roof.GetSlabShapeEditor();
                                if (editor == null || !editor.IsEnabled)
                                    throw new InvalidOperationException("shape editing could not be enabled");
                                _vm.Log(LogLevel.Info, $"{HostRoofService.Label(roof)}: shape editing enabled.");
                            }

                            int ok = ApplyRoof(doc, roof, editor, group.ToList(), out int failed);
                            tx.Commit();
                            pointsDone += ok;
                            pointsFailed += failed;
                            roofsDone++;
                        }
                        catch (Exception ex)
                        {
                            tx.RollBack();
                            pointsFailed += group.Count();
                            _vm.Log(LogLevel.Error, $"{HostRoofService.Label(roof)} not changed: {ex.Message}");
                            continue;
                        }
                    }
                    ReadBack(roof, group.ToList());
                }

                if (roofsDone > 0) tg.Assimilate();
                else tg.RollBack();
            }

            _vm.Progress = 100;
            _vm.SummaryText = $"Updated {pointsDone} point(s) on {roofsDone} roof(s)" +
                              (pointsFailed > 0 ? $"; {pointsFailed} point(s) not changed (see log)." : ".");
            _vm.Log(pointsFailed > 0 ? LogLevel.Warning : LogLevel.Info, _vm.SummaryText);
            _vm.UpdateCounts();
        }

        /// <summary>
        /// Sets each row's point to its New Z. <c>ModifySubElement</c> takes an offset from the roof's base
        /// surface, so each point is first set to 0 to read that base elevation, then set to target minus base.
        /// </summary>
        private int ApplyRoof(Document doc, FootPrintRoof roof, SlabShapeEditor editor, List<ShapePointRow> rows, out int failed)
        {
            failed = 0;
            var matched = Match(editor, rows, out failed);
            if (matched.Count == 0) return 0;

            foreach (var (vertex, _) in matched) editor.ModifySubElement(vertex, 0);
            doc.Regenerate();

            // Handles from before the regenerate may be stale: look the points up again.
            var fresh = Match(roof.GetSlabShapeEditor(), matched.Select(m => m.Row).ToList(), out int lost);
            failed += lost;
            foreach (var (vertex, row) in fresh)
            {
                double baseZ = vertex.Position.Z;
                editor.ModifySubElement(vertex, _vm.ToFeet(row.NewZ.Value) - baseZ);
            }
            return fresh.Count;
        }

        private List<(SlabShapeVertex Vertex, ShapePointRow Row)> Match(SlabShapeEditor editor, List<ShapePointRow> rows, out int failed)
        {
            failed = 0;
            var vertices = editor.SlabShapeVertices.Cast<SlabShapeVertex>().Where(v => v?.Position != null).ToList();
            var used = new HashSet<int>();
            var result = new List<(SlabShapeVertex, ShapePointRow)>();
            foreach (ShapePointRow row in rows)
            {
                int best = -1;
                double bestDist = SamePointToleranceFt;
                for (int i = 0; i < vertices.Count; i++)
                {
                    if (used.Contains(i)) continue;
                    double d = Math.Sqrt(Math.Pow(vertices[i].Position.X - row.XFt, 2) + Math.Pow(vertices[i].Position.Y - row.YFt, 2));
                    if (d <= bestDist) { best = i; bestDist = d; }
                }
                if (best < 0)
                {
                    failed++;
                    _vm.Log(LogLevel.Warning, $"{row.RoofLabel} point {row.Index}: not found any more (was the roof edited?). Read elevations again.");
                    continue;
                }
                used.Add(best);
                result.Add((vertices[best], row));
            }
            return result;
        }

        private void ReadBack(FootPrintRoof roof, List<ShapePointRow> rows)
        {
            var vertices = roof.GetSlabShapeEditor().SlabShapeVertices.Cast<SlabShapeVertex>().Where(v => v?.Position != null).ToList();
            foreach (ShapePointRow row in rows)
            {
                SlabShapeVertex v = vertices
                    .OrderBy(x => Math.Pow(x.Position.X - row.XFt, 2) + Math.Pow(x.Position.Y - row.YFt, 2))
                    .FirstOrDefault();
                if (v == null) continue;
                row.CurrentZ = _vm.ToDisplay(v.Position.Z);
                if (Math.Abs(v.Position.Z - _vm.ToFeet(row.NewZ.Value)) > ReadbackToleranceFt)
                    _vm.Log(LogLevel.Warning, $"{row.RoofLabel} point {row.Index}: Revit set {row.CurrentZ:F3} {_vm.UnitLabel}, not the requested {row.NewZ:F3}.");
            }
        }

        private void Warn(string message)
        {
            _vm.Log(LogLevel.Warning, message);
            _vm.SummaryText = message;
            TaskDialog.Show(ToolCatalog.RoofShapeFromLink.Name, message);
        }
    }
}
