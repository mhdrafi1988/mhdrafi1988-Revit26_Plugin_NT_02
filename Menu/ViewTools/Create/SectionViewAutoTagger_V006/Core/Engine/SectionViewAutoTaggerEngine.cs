using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.SectionViewAutoTagger.V006
{
    /// <summary>
    /// Orchestrates a full batch Run: for each WorklistEntry, resolves
    /// taggable elements per selected category, computes stacked tag
    /// positions, and places tags — all inside a single Transaction per
    /// PlaceTagsEventHandler.Execute() call (one transaction per logical
    /// operation, per suite convention: the whole batch is one logical op).
    ///
    /// This class contains NO direct Revit API write calls itself — it is
    /// called FROM PlaceTagsEventHandler.Execute() (inside a valid API
    /// context, inside the transaction), and returns results for logging.
    ///
    /// V003: Tag type is no longer resolved here — each WorklistEntry's
    /// CategoryTagSelection already carries the FamilySymbol Id that was
    /// locked in at "Add to Worklist" time (confirmed: not re-resolved at
    /// Run time, even if loaded families changed since queuing). Leader end
    /// condition (global, from TagPlacementSettings) is applied to each
    /// created IndependentTag.
    /// </summary>
    public class SectionViewAutoTaggerEngine
    {
        private readonly TagStackLayoutService _stackLayout = new();
        private readonly CropBoundaryHelper _cropHelper = new();

        private const double MmToFeet = 1.0 / 304.8;

        /// <summary>
        /// Runs the full batch. Must be called from within an active
        /// Transaction (caller's responsibility — see PlaceTagsEventHandler).
        /// </summary>
        public RunResult RunBatch(
            Document doc,
            IReadOnlyList<WorklistEntry> worklist,
            TagPlacementSettings settings,
            Action<LogLevel, string> log)
        {
            var allResults = new List<TagResult>();

            log(LogLevel.Info, $"Worklist: {worklist.Sum(w => w.Views.Count)} view(s) queued, starting batch...");

            foreach (var entry in worklist)
            {
                ToolCancellation.CheckCurrent();
                foreach (var viewOption in entry.Views)
                {
                    var view = doc.GetElement(viewOption.ViewId) as View;
                    if (view == null)
                    {
                        log(LogLevel.Warning, $"View '{viewOption.ViewName}': could not resolve view element, skipped.");
                        continue;
                    }

                    log(LogLevel.Info, $"View '{view.Name}': scanning {string.Join(", ", entry.Categories.Select(c => c.CategoryName))}...");

                    var results = ProcessView(doc, view, entry.Categories, settings, log);
                    allResults.AddRange(results);
                }
            }

            var runResult = new RunResult(allResults);
            return runResult;
        }

        private List<TagResult> ProcessView(
            Document doc,
            View view,
            IReadOnlyList<CategoryTagSelection> categories,
            TagPlacementSettings settings,
            Action<LogLevel, string> log)
        {
            var results = new List<TagResult>();
            double alignmentLineX = _cropHelper.GetAlignmentLineX(view, settings.AlignmentSide, settings.OffsetMm);
            double spacingFeet = settings.SpacingMm * MmToFeet;

            var leaderEnd = settings.LeaderEndCondition == LeaderEndCondition.Attached
                ? Autodesk.Revit.DB.LeaderEndCondition.Attached
                : Autodesk.Revit.DB.LeaderEndCondition.Free;

            // Resolve tag types up front (from the locked-in selection, not
            // re-resolved) and collect elements across ALL selected
            // categories into one combined list. Stacking must run ONCE per
            // view across every category together — stacking each category
            // separately (the original bug) let a Door tag and a Window tag
            // land at the same Y position, since each category's stack
            // restarted its own startY independently.
            var elementsToTag = new List<(ElementId Id, string CategoryName, FamilySymbol TagType)>();

            foreach (var selection in categories)
            {
                var tagType = doc.GetElement(selection.TagTypeId) as FamilySymbol;
                if (tagType == null)
                {
                    log(LogLevel.Warning, $"Category '{selection.CategoryName}' in view '{view.Name}': locked-in tag type '{selection.TagTypeName}' no longer resolves (deleted/unloaded?), skipped.");
                    continue;
                }

                if (!tagType.IsActive)
                    tagType.Activate();

                var elementIds = new FilteredElementCollector(doc, view.Id)
                    .OfCategory(selection.Category)
                    .WhereElementIsNotElementType()
                    .ToElementIds()
                    .ToList();

                if (elementIds.Count == 0)
                {
                    log(LogLevel.Info, $"Category '{selection.CategoryName}' in view '{view.Name}': no elements found.");
                    continue;
                }

                foreach (var id in elementIds)
                    elementsToTag.Add((id, selection.CategoryName, tagType));
            }

            if (elementsToTag.Count == 0)
                return results;

            // Build view-space reference points (element location projected
            // through the view's crop box transform) for stacking — combined
            // across all categories.
            var referencePoints = new List<(ElementId Id, XYZ ViewPoint)>();
            foreach (var (id, _, _) in elementsToTag)
            {
                var el = doc.GetElement(id);
                XYZ locPoint = GetElementReferencePoint(el);
                if (locPoint == null) continue;

                XYZ viewSpacePoint = view.CropBox.Transform.Inverse.OfPoint(locPoint);
                referencePoints.Add((id, viewSpacePoint));
            }

            var stacked = _stackLayout.ComputeStackedPositions(referencePoints, alignmentLineX, spacingFeet);
            var elementLookup = elementsToTag.ToDictionary(e => e.Id, e => e);

            var placedByCategory = new Dictionary<string, int>();

            foreach (var plan in stacked)
            {
                ToolCancellation.CheckCurrent();
                if (!elementLookup.TryGetValue(plan.ElementId, out var meta))
                    continue;

                try
                {
                    XYZ worldHeadPoint = view.CropBox.Transform.OfPoint(plan.HeadPosition);

                    var reference = new Reference(doc.GetElement(plan.ElementId));
                    var tag = IndependentTag.Create(
                        doc,
                        meta.TagType.Id,
                        view.Id,
                        reference,
                        true,
                        TagOrientation.Horizontal,
                        worldHeadPoint);

                    tag.LeaderEndCondition = leaderEnd;

                    placedByCategory[meta.CategoryName] = placedByCategory.GetValueOrDefault(meta.CategoryName) + 1;
                    results.Add(new TagResult(plan.ElementId, meta.CategoryName, view.Name, TagResultStatus.Placed));
                }
                catch (OperationCanceledException)
                {
                    throw; // user cancel: never swallowed as a failure
                }
                catch (Exception ex)
                {
                    log(LogLevel.Error, $"Failed to tag element {plan.ElementId.Value} ({meta.CategoryName}) in '{view.Name}': {ex.Message}");
                    results.Add(new TagResult(plan.ElementId, meta.CategoryName, view.Name, TagResultStatus.Failed, ex.Message));
                }
            }

            foreach (var kvp in placedByCategory)
                log(LogLevel.Success, $"{kvp.Value} {kvp.Key} tag(s) placed in '{view.Name}'.");

            return results;
        }

        /// <summary>
        /// Resolves a representative point for stacking/leader purposes.
        /// ASSUMPTION: uses LocationPoint when available, else bounding box
        /// center as a fallback for line-based/hosted elements without a
        /// simple point location (e.g. Walls, Structural Framing).
        /// </summary>
        private XYZ GetElementReferencePoint(Element el)
        {
            if (el?.Location is LocationPoint lp)
                return lp.Point;

            if (el?.Location is LocationCurve lc)
                return (lc.Curve.GetEndPoint(0) + lc.Curve.GetEndPoint(1)) / 2.0;

            var bbox = el?.get_BoundingBox(null);
            if (bbox != null)
                return (bbox.Min + bbox.Max) / 2.0;

            return null;
        }
    }
}
