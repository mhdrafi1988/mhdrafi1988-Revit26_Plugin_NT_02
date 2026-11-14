using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.AnnotationOverlapReport.V001.Core;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.AnnotationOverlapReport.V001.Infrastructure
{
    /// <summary>What the window asks the scan to do.</summary>
    public sealed class ScanRequest
    {
        /// <summary>True only counts annotations per type (fills the type list); false also finds overlaps.</summary>
        public bool CountOnly { get; set; }

        /// <summary>True scans the active view (or active sheet with its views); false scans <see cref="ViewIds"/>.</summary>
        public bool ActiveViewOnly { get; set; }

        /// <summary>Ids of the ticked views and sheets.</summary>
        public List<long> ViewIds { get; set; } = new List<long>();

        /// <summary>Type names to leave out of the check.</summary>
        public HashSet<string> ExcludedTypes { get; set; } = new HashSet<string>();

        /// <summary>True also compares annotations of different types.</summary>
        public bool AcrossTypes { get; set; } = true;
    }

    /// <summary>Result of one scan, handed back to the window on the Revit thread.</summary>
    public sealed class ScanOutcome
    {
        /// <summary>The request this outcome answers.</summary>
        public ScanRequest Request { get; set; }

        /// <summary>Overlapping pairs (empty for a count-only scan).</summary>
        public List<ReportRow> Rows { get; } = new List<ReportRow>();

        /// <summary>Measured annotations per checked type name.</summary>
        public Dictionary<string, int> TypeCounts { get; } = new Dictionary<string, int>();

        /// <summary>Elements per deferred head type name.</summary>
        public Dictionary<string, int> DeferredCounts { get; } = new Dictionary<string, int>();

        /// <summary>Run facts.</summary>
        public ReportRunInfo Info { get; } = new ReportRunInfo();

        /// <summary>Number of scanned views that have at least one overlap.</summary>
        public int ViewsWithOverlaps { get; set; }

        /// <summary>Set when the scan could not run at all; the message to show.</summary>
        public string Error { get; set; }

        /// <summary>True when the user cancelled the run.</summary>
        public bool Cancelled { get; set; }
    }

    /// <summary>
    /// Runs the scan on the Revit thread. The modeless window sets <see cref="Request"/> and raises the
    /// external event; <see cref="Completed"/> is called with the outcome. Read-only: no transaction.
    /// </summary>
    public sealed class ScanEventHandler : IExternalEventHandler
    {
        /// <summary>Document the window was opened on.</summary>
        public Document Document { get; set; }

        /// <summary>The next scan to run.</summary>
        public ScanRequest Request { get; set; }

        /// <summary>Called on the Revit thread when the scan ends, also on error or cancel.</summary>
        public Action<ScanOutcome> Completed { get; set; }

        /// <inheritdoc />
        public void Execute(UIApplication app)
        {
            ScanRequest request = Request;
            ToolGuard.RunHandler(GetType(), app, a =>
            {
                try
                {
                    ExecuteCore(a, request);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException)
                                           && !(ex is Autodesk.Revit.Exceptions.OperationCanceledException))
                {
                    // Tell the window first so it never stays "busy", then let ToolGuard log the error.
                    Completed?.Invoke(new ScanOutcome
                    {
                        Request = request,
                        Error = "The check failed: " + ex.Message + " Details are in the log. Nothing in the model was changed."
                    });
                    throw;
                }
            },
            () => Completed?.Invoke(new ScanOutcome { Request = request, Cancelled = true }));
        }

        /// <inheritdoc />
        public string GetName() => "Annotation Overlap Report: scan";

        private void ExecuteCore(UIApplication app, ScanRequest request)
        {
            if (request == null)
                return;

            var outcome = new ScanOutcome { Request = request };
            outcome.Info.ToolVersion = ToolCatalog.AnnotationOverlapReport.Version;

            UIDocument uiDoc = app.ActiveUIDocument;
            Document doc = Document;
            if (doc == null || !doc.IsValidObject || uiDoc == null || !uiDoc.Document.Equals(doc))
            {
                outcome.Error = "The model this window was opened on is closed or not active. Close this window and run the tool again.";
                Completed?.Invoke(outcome);
                return;
            }

            List<View> views = request.ActiveViewOnly
                ? ViewCatalog.Expand(doc, new[] { uiDoc.ActiveView?.Id.Value ?? -1 })
                : ViewCatalog.Expand(doc, request.ViewIds);

            if (views.Count == 0)
            {
                outcome.Error = request.ActiveViewOnly
                    ? "The active view is not supported. Supported: " + ViewCatalog.SupportedList + "."
                    : "No views or sheets are ticked.";
                Completed?.Invoke(outcome);
                return;
            }

            List<ElementId> categoryIds = AnnotationCollector.RelevantCategoryIds(doc);

            using (request.CountOnly ? null : ToolCancellation.BeginFor(ToolCatalog.AnnotationOverlapReport, app))
            {
                for (int i = 0; i < views.Count; i++)
                {
                    View view = views[i];
                    string name = SafeName(view);
                    ToolCancellation.CheckCurrent();
                    ToolCancellation.ReportCurrent(i, views.Count, "Checking " + name + "…");

                    try
                    {
                        ScanView(doc, view, name, categoryIds, request, outcome);
                        outcome.Info.ViewsScanned++;
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException)
                                               && !(ex is Autodesk.Revit.Exceptions.OperationCanceledException))
                    {
                        outcome.Info.SkippedViews.Add(name + ": " + ex.Message);
                    }
                }
            }

            outcome.Info.RunAt = DateTime.Now;
            Completed?.Invoke(outcome);
        }

        private static void ScanView(Document doc, View view, string name, ICollection<ElementId> categoryIds,
            ScanRequest request, ScanOutcome outcome)
        {
            ViewScan scan = AnnotationCollector.Collect(doc, view, categoryIds);

            foreach (IGrouping<string, PaperBox> group in scan.Boxes.GroupBy(b => b.TypeName))
            {
                outcome.TypeCounts.TryGetValue(group.Key, out int count);
                outcome.TypeCounts[group.Key] = count + group.Count();
            }
            foreach (KeyValuePair<string, int> deferred in scan.DeferredCounts)
            {
                outcome.DeferredCounts.TryGetValue(deferred.Key, out int count);
                outcome.DeferredCounts[deferred.Key] = count + deferred.Value;
            }
            outcome.Info.NotMeasured += scan.NotMeasured;

            if (request.CountOnly)
                return;

            List<PaperBox> boxes = scan.Boxes.Where(b => !request.ExcludedTypes.Contains(b.TypeName)).ToList();
            outcome.Info.AnnotationsScanned += boxes.Count;

            List<OverlapPair> pairs = OverlapFinder.Find(boxes, request.AcrossTypes);
            if (pairs.Count == 0)
                return;

            outcome.ViewsWithOverlaps++;
            string sheet = ViewCatalog.SheetNumberOf(view);
            string scale = ViewCatalog.ScaleText(view);
            foreach (OverlapPair pair in pairs)
            {
                outcome.Rows.Add(new ReportRow
                {
                    ViewId = view.Id.Value,
                    Sheet = sheet,
                    View = name,
                    Scale = scale,
                    TypeA = pair.A.TypeName,
                    TextA = pair.A.Text,
                    IdA = pair.A.Id,
                    TypeB = pair.B.TypeName,
                    TextB = pair.B.Text,
                    IdB = pair.B.Id,
                    OverlapMm = pair.OverlapMm
                });
            }
        }

        private static string SafeName(View view)
        {
            try { return view.Name; }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { return "View " + view.Id.Value; }
        }
    }
}
