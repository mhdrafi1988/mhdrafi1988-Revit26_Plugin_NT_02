using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.SectionRoofDetailLines.V001.Core.Models;
using Revit26_Plugin_Revised.SectionRoofDetailLines.V001.Core.Services;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.SectionRoofDetailLines.V001.Core.Run
{
    /// <summary>What the window asks the run to do.</summary>
    public sealed class RunRequest
    {
        /// <summary>Element ids of the section views to draw in, in the order they are drawn.</summary>
        public List<long> ViewKeys { get; set; } = new();

        /// <summary>The element types ticked in the window.</summary>
        public HashSet<TypeKey> SelectedTypes { get; set; } = new();

        /// <summary>Line styles, adjacency tolerance and the rest, as shown in the window.</summary>
        public SectionRoofLinesSettings Settings { get; set; } = new();
    }

    /// <summary>
    /// Draws the lines: for each section in the run, reads what it cuts, works out the curves and
    /// creates them.
    /// <para>
    /// The whole run is one transaction inside one transaction group, assimilated at the end, so
    /// every section's lines undo together in a single Ctrl+Z. A single element that cannot be
    /// read is reported and skipped; it never takes the run down with it. A cancel, or any error
    /// that reaches this far, rolls the group back, so the model is never left with half a run in
    /// it.
    /// </para>
    /// </summary>
    public sealed class SectionLineRunner
    {
        private const double Feet = 304.8;

        private readonly VisibleElementCollector _visible = new();
        private readonly CutProfileService _cuts = new();
        private readonly SectionLineBuilder _builder = new();
        private readonly SectionDetailLineWriter _writer = new();

        /// <summary>
        /// Runs <paramref name="request"/> against <paramref name="doc"/>. Call on Revit's API
        /// thread, inside no transaction of the caller's own — this method owns the transaction.
        /// </summary>
        public RunReport Run(Document doc, RunRequest request)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (request == null) throw new ArgumentNullException(nameof(request));

            var report = new RunReport();
            SectionRoofLinesSettings settings = request.Settings.Normalize();
            double toleranceFeet = settings.AdjacencyToleranceMm / Feet;
            double shortCurve = doc.Application.ShortCurveTolerance;

            GraphicsStyle roofStyle = SectionDetailLineWriter.FindStyle(doc, settings.RoofLineStyleName);
            GraphicsStyle wallStyle = SectionDetailLineWriter.FindStyle(doc, settings.WallLineStyleName);
            WarnMissingStyle(report, settings.RoofLineStyleName, roofStyle, "roof");
            WarnMissingStyle(report, settings.WallLineStyleName, wallStyle, "wall");

            Dictionary<long, LinkSource> links = ReadLinks(doc);

            using var group = new TransactionGroup(doc, "Section Roof Detail Lines");
            group.Start();

            using (var transaction = new Transaction(doc, "Create section detail lines"))
            {
                try
                {
                    transaction.Start();

                    foreach (long viewKey in request.ViewKeys)
                    {
                        ToolCancellation.CheckCurrent();
                        RunOneView(doc, viewKey, request, settings, links,
                            roofStyle, wallStyle, toleranceFeet, shortCurve, report);
                    }

                    ToolCancellation.CheckCurrent(); // last chance before the commit
                    TransactionStatus status = transaction.Commit();
                    if (status != TransactionStatus.Committed)
                    {
                        group.RollBack();
                        report.Details.Add(new LogEntry(LogLevel.Error,
                            $"Revit did not commit the change ({status}). Nothing was created."));
                        return Nothing(report);
                    }
                }
                catch (OperationCanceledException)
                {
                    if (transaction.GetStatus() == TransactionStatus.Started) transaction.RollBack();
                    group.RollBack();
                    throw; // ToolGuard reads this as a user cancel, and the view model logs it
                }
                catch (Exception ex)
                {
                    if (transaction.GetStatus() == TransactionStatus.Started) transaction.RollBack();
                    group.RollBack();
                    report.Details.Add(new LogEntry(LogLevel.Error,
                        $"The run was rolled back — nothing was created: {ex.Message}"));
                    return Nothing(report);
                }
            }

            group.Assimilate(); // one Undo entry for the whole run
            return report;
        }

        private void RunOneView(
            Document doc, long viewKey, RunRequest request, SectionRoofLinesSettings settings,
            Dictionary<long, LinkSource> links, GraphicsStyle roofStyle, GraphicsStyle wallStyle,
            double toleranceFeet, double shortCurve, RunReport report)
        {
            if (doc.GetElement(new ElementId(viewKey)) is not ViewSection view)
            {
                report.Details.Add(new LogEntry(LogLevel.Warning,
                    $"Section view {viewKey} is no longer in the model — skipped."));
                return;
            }

            SectionFrame frame = SectionFrame.Create(view);
            if (frame == null)
            {
                report.Details.Add(new LogEntry(LogLevel.Warning,
                    $"'{view.Name}': Revit gives no crop box for this view, so the tool cannot tell what it shows — skipped."));
                return;
            }

            if (frame.CropInactive)
                report.Details.Add(new LogEntry(LogLevel.Warning,
                    $"'{view.Name}': the crop box is switched off. The run uses the crop box as its extent, "
                    + "so the view may show more than the lines cover."));

            var notes = new List<LogEntry>();
            List<VisibleElementRow> rows = _visible.Collect(doc, frame, notes);

            List<CutProfile> roofs = settings.DrawRoofProfile
                ? ReadProfiles(doc, frame, rows, request.SelectedTypes, ElementRole.Roof, links, report)
                : new List<CutProfile>();

            // Wall lines need the roofs' profiles to measure adjacency against, so they are read
            // even when the roof profile itself is not drawn.
            List<CutProfile> roofsForAdjacency = settings.DrawRoofProfile
                ? roofs
                : ReadProfiles(doc, frame, rows, request.SelectedTypes, ElementRole.Roof, links, report);

            List<CutProfile> walls = settings.DrawWallLines
                ? ReadProfiles(doc, frame, rows, request.SelectedTypes, ElementRole.Wall, links, report)
                : new List<CutProfile>();

            var created = 0;
            var skippedExisting = 0;

            if (settings.DrawRoofProfile && roofs.Count > 0)
            {
                List<Curve> curves = _builder.BuildRoofCurves(frame, roofs, shortCurve);
                WriteResult write = _writer.Write(doc, view, curves, roofStyle, settings.SkipExistingLines,
                    message => report.Details.Add(new LogEntry(LogLevel.Warning, $"'{view.Name}': {message}")));
                created += write.Created;
                skippedExisting += write.SkippedExisting;
                report.RoofsDrawn += roofs.Count;
                report.ElementsSkipped += write.Failed;
            }

            if (settings.DrawWallLines && walls.Count > 0)
            {
                List<Curve> curves = _builder.BuildWallCurves(
                    frame, walls, roofsForAdjacency, toleranceFeet, settings.WallLineOnBothFaces, shortCurve,
                    message => report.Details.Add(new LogEntry(LogLevel.Info, $"'{view.Name}': {message}")),
                    out int wallsDrawn);

                WriteResult write = _writer.Write(doc, view, curves, wallStyle, settings.SkipExistingLines,
                    message => report.Details.Add(new LogEntry(LogLevel.Warning, $"'{view.Name}': {message}")));
                created += write.Created;
                skippedExisting += write.SkippedExisting;
                report.WallsDrawn += wallsDrawn;
                report.ElementsSkipped += write.Failed;
            }

            report.SectionsDone++;
            report.LinesCreated += created;
            report.LinesSkippedExisting += skippedExisting;
            report.LinesByView[viewKey] = created;

            report.Details.Add(new LogEntry(created > 0 ? LogLevel.Success : LogLevel.Info,
                $"'{view.Name}': {created} line(s) from {roofs.Count} roof(s) and {walls.Count} wall(s) cut here"
                + (skippedExisting > 0 ? $"; {skippedExisting} already in the view" : string.Empty) + "."));

            foreach (LogEntry note in notes)
                report.Details.Add(note);
        }

        /// <summary>
        /// The cut profile of every ticked element in <paramref name="role"/> that
        /// <paramref name="frame"/>'s view shows. An element the section does not truly cut yields
        /// no profile and drops out here.
        /// </summary>
        private List<CutProfile> ReadProfiles(
            Document doc, SectionFrame frame, List<VisibleElementRow> rows,
            HashSet<TypeKey> selected, ElementRole role,
            Dictionary<long, LinkSource> links, RunReport report)
        {
            var profiles = new List<CutProfile>();
            var seen = new HashSet<ElementKey>();

            foreach (VisibleElementRow row in rows)
            {
                if (row.Role != role) continue;
                if (!selected.Contains(row.Type)) continue;
                if (!seen.Add(row.Key)) continue;

                ToolCancellation.CheckCurrent();

                Element element;
                Transform toModel;
                if (row.Key.IsHost)
                {
                    element = doc.GetElement(new ElementId(row.Key.ElementId));
                    toModel = null;
                }
                else
                {
                    if (!links.TryGetValue(row.Key.LinkInstanceId, out LinkSource link))
                    {
                        report.ElementsSkipped++;
                        continue;
                    }
                    element = link.Document.GetElement(new ElementId(row.Key.ElementId));
                    toModel = link.ToModel;
                }

                if (element == null)
                {
                    report.ElementsSkipped++;
                    continue;
                }

                string label = $"{row.CategoryName} '{row.TypeName}' ({row.SourceLabel}, id {row.Key.ElementId})";

                List<Solid> solids = _cuts.GetSolids(element, toModel,
                    message => report.Details.Add(new LogEntry(LogLevel.Warning, $"{label}: {message}")));
                if (solids.Count == 0)
                {
                    report.ElementsSkipped++;
                    continue;
                }

                List<Curve> curves = _cuts.GetCutCurves(solids, frame.CutPlane,
                    message => report.Details.Add(new LogEntry(LogLevel.Warning, $"{label}: {message}")));
                if (curves.Count == 0)
                {
                    // Visible in the view, but behind the cut plane rather than on it — the
                    // section shows it without cutting it, so there is no profile to trace.
                    report.ElementsSkipped++;
                    continue;
                }

                var points = new List<XYZ>();
                foreach (Curve curve in curves)
                    points.AddRange(curve.Tessellate());

                profiles.Add(new CutProfile
                {
                    ElementId = row.Key.ElementId,
                    Label = label,
                    Curves = curves,
                    Points = points
                });
            }

            return profiles;
        }

        private static Dictionary<long, LinkSource> ReadLinks(Document doc)
        {
            var links = new Dictionary<long, LinkSource>();
            foreach (RevitLinkInstance link in new FilteredElementCollector(doc)
                         .OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
            {
                try
                {
                    Document linkDoc = link.GetLinkDocument();
                    if (linkDoc == null) continue;
                    links[link.Id.Value] = new LinkSource(linkDoc, link.GetTotalTransform());
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    // A link Revit will not open is simply not available to read from.
                }
            }
            return links;
        }

        private static void WarnMissingStyle(RunReport report, string wanted, GraphicsStyle found, string what)
        {
            if (string.IsNullOrWhiteSpace(wanted) || found != null) return;
            report.Details.Add(new LogEntry(LogLevel.Warning,
                $"Line style '{wanted}' is not in this project, so the {what} lines keep the view's default style."));
        }

        private static RunReport Nothing(RunReport report)
        {
            report.SectionsDone = 0;
            report.RoofsDrawn = 0;
            report.WallsDrawn = 0;
            report.LinesCreated = 0;
            report.LinesSkippedExisting = 0;
            report.LinesByView.Clear();
            return report;
        }
    }

    /// <summary>A loaded link: its document, and the transform that brings its geometry into the
    /// host model.</summary>
    internal readonly struct LinkSource
    {
        public LinkSource(Document document, Transform toModel)
        {
            Document = document;
            ToModel = toModel;
        }

        public Document Document { get; }

        public Transform ToModel { get; }
    }
}
