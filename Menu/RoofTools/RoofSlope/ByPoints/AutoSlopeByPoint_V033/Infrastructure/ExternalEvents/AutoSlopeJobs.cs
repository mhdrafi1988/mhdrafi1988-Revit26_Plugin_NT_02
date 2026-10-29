// =======================================================
// File: AutoSlopeJobs.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Infrastructure.ExternalEvents
// New in V032. One handler runs one of two jobs:
//   RunJob     — a full AutoSlope run inside one TransactionGroup.
//                Assimilate ONLY on success; cancel and failure RollBack
//                the whole group, so the roof is exactly as before (B1).
//   RepickJob  — pick new drain points from the open window (Phase 4).
//   ZoomJob    — V032: select the roof and zoom the active view to the
//                rows chosen in the results panel. (Roof shape points are
//                not elements, so the API cannot select them one by one.)
// =======================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Models;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Infrastructure.RoofOps;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Infrastructure.ExternalEvents
{
    public abstract class AutoSlopeJob
    {
        public abstract void Execute(UIApplication app);
    }

    public sealed class RunJob : AutoSlopeJob
    {
        private readonly AutoSlopeRequest _request;
        private readonly Action<RunProgressInfo> _progress;
        private readonly Action<LogLevel, string> _log;
        private readonly CancellationToken _cancel;
        private readonly Action<AutoSlopeResult> _onCompleted;

        public RunJob(
            AutoSlopeRequest request,
            Action<RunProgressInfo> progress,
            Action<LogLevel, string> log,
            CancellationToken cancel,
            Action<AutoSlopeResult> onCompleted)
        {
            _request = request;
            _progress = progress ?? (_ => { });
            _log = log ?? ((_, __) => { });
            _cancel = cancel;
            _onCompleted = onCompleted;
        }

        public override void Execute(UIApplication app)
        {
            AutoSlopeResult result;
            try
            {
                Document doc = app.ActiveUIDocument?.Document
                    ?? throw new InvalidOperationException("No document is open.");

                using (var group = new TransactionGroup(doc, "AutoSlope"))
                {
                    group.Start();
                    result = AutoSlopeRunner.Run(app, _request, _progress, _log, _cancel);

                    if (result.Success)
                    {
                        group.Assimilate();
                    }
                    else
                    {
                        group.RollBack();
                        _log(LogLevel.Info, "Rolled back: the roof is exactly as it was before Run.");
                    }
                }
            }
            catch (Exception ex)
            {
                // Unexpected: the using block has already rolled the group back.
                _log(LogLevel.Error, $"Unexpected error: {ex.Message}. All changes were rolled back.");
                _onCompleted?.Invoke(AutoSlopeResult.Failed(ex.Message, DateTime.Now));
                throw; // ToolGuard.RunHandler logs it and shows the error dialog.
            }

            _onCompleted?.Invoke(result);
        }
    }

    public sealed class RepickJob : AutoSlopeJob
    {
        private readonly long _roofIdValue;
        private readonly Action<List<Point3>> _onPicked;
        private readonly Action _onCancelled;

        public RepickJob(long roofIdValue, Action<List<Point3>> onPicked, Action onCancelled)
        {
            _roofIdValue = roofIdValue;
            _onPicked = onPicked;
            _onCancelled = onCancelled;
        }

        public override void Execute(UIApplication app)
        {
            UIDocument uidoc = app.ActiveUIDocument;
            if (uidoc == null) { _onCancelled?.Invoke(); return; }

            IList<Reference> refs;
            try
            {
                refs = uidoc.Selection.PickObjects(
                    ObjectType.PointOnElement,
                    new PointOnRoofFilter(new ElementId(_roofIdValue)),
                    "Pick drain points on the roof, then click Finish (Esc keeps the current drains)");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                _onCancelled?.Invoke();
                return;
            }

            var points = refs.Where(r => r?.GlobalPoint != null).Select(r => r.GlobalPoint.ToPoint3()).ToList();
            if (points.Count == 0) _onCancelled?.Invoke();
            else _onPicked?.Invoke(points);
        }
    }

    public sealed class ZoomJob : AutoSlopeJob
    {
        private readonly long _roofIdValue;
        private readonly IReadOnlyList<Point3> _points;
        private readonly Action<LogLevel, string> _log;

        public ZoomJob(long roofIdValue, IReadOnlyList<Point3> points, Action<LogLevel, string> log)
        {
            _roofIdValue = roofIdValue;
            _points = points;
            _log = log ?? ((_, __) => { });
        }

        public override void Execute(UIApplication app)
        {
            UIDocument uidoc = app.ActiveUIDocument;
            if (uidoc == null || _points == null || _points.Count == 0) return;

            View view = uidoc.ActiveView;
            UIView uiView = uidoc.GetOpenUIViews().FirstOrDefault(v => v.ViewId == view?.Id);
            if (uiView == null)
            {
                _log(LogLevel.Warning, "Zoom to: the active view cannot be zoomed. Open a plan or 3D view and try again.");
                return;
            }

            double pad = RevitConvert.MToFt(2.0);
            var min = new XYZ(_points.Min(p => p.X) - pad, _points.Min(p => p.Y) - pad, _points.Min(p => p.Z) - pad);
            var max = new XYZ(_points.Max(p => p.X) + pad, _points.Max(p => p.Y) + pad, _points.Max(p => p.Z) + pad);

            uidoc.Selection.SetElementIds(new List<ElementId> { new ElementId(_roofIdValue) });
            uiView.ZoomAndCenterRectangle(min, max);
            _log(LogLevel.Info, $"Zoomed to {_points.Count} point(s) in \"{view.Name}\" and selected the roof.");
        }
    }
}
