using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.AnnotationOverlapDetection.V006.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.AnnotationOverlapDetection.V006.Helpers
{
    /// <summary>
    /// Orchestrates Steps 2, 3, 5 and 6 from the spec: collect annotations,
    /// group by type, detect overlapping pairs, and compute grid alignment
    /// and distance metrics for each pair.
    /// Read-only - callers are still expected to wrap the collection call in
    /// Transaction 1 ("Scan Annotations for Overlaps") even though nothing
    /// here modifies the model.
    /// </summary>
    internal static class OverlapDetectionHelper
    {
        /// <summary>Default "Aligned" tolerance, mm (user-set since 6.1.0).</summary>
        public const double DefaultAlignmentToleranceMm = 0.1;

        private static readonly Type[] AnnotationTypes =
        {
            typeof(TextNote),
            typeof(IndependentTag),
            typeof(SpatialElementTag),
            typeof(Dimension)
        };

        /// <summary>
        /// Step 2: collect all annotation elements visible in the active view.
        /// </summary>
        public static List<AnnotationData> GetAnnotationsFromView(View view, Document doc)
        {
            var results = new List<AnnotationData>();

            foreach (Type type in AnnotationTypes)
            {
                var collector = new FilteredElementCollector(doc, view.Id)
                    .OfClass(type)
                    .WhereElementIsNotElementType();

                foreach (Element elem in collector)
                {
                    ToolCancellation.CheckCurrent();
                    var box = BoundingBoxCalculator.GetBoundingBox(elem, view);
                    if (box == null)
                        continue;

                    AnnotationCategory category = Classify(elem);
                    results.Add(new AnnotationData
                    {
                        ElementId = elem.Id,
                        ClassName = type.Name,
                        Category = category,
                        TypeName = AnnotationPriority.Label(category),
                        X = box.Value.x,
                        Y = box.Value.y,
                        Width = box.Value.width,
                        Height = box.Value.height
                    });
                }
            }

            return results;
        }

        /// <summary>
        /// Step 3: group collected annotations by type name for the checkbox list.
        /// </summary>
        public static Dictionary<string, List<AnnotationData>> GroupByType(List<AnnotationData> annotations)
        {
            return annotations
                .GroupBy(a => a.TypeName)
                .ToDictionary(g => g.Key, g => g.ToList());
        }

        /// <summary>Priority category of an annotation element (V006).</summary>
        public static AnnotationCategory Classify(Element elem)
        {
            switch (elem)
            {
                case Dimension _:
                    return AnnotationCategory.Dimension;
                case SpatialElementTag _:
                    return AnnotationCategory.RoomTag;
                case TextNote _:
                    return AnnotationCategory.TextNote;
                case IndependentTag tag:
                    BuiltInCategory bic = tag.Category?.BuiltInCategory ?? BuiltInCategory.INVALID;
                    if (bic == BuiltInCategory.OST_KeynoteTags)
                        return AnnotationCategory.KeynoteTag;
                    if (bic == BuiltInCategory.OST_DoorTags || bic == BuiltInCategory.OST_WindowTags)
                        return AnnotationCategory.DoorWindowTag;
                    if (bic == BuiltInCategory.OST_RoomTags || bic == BuiltInCategory.OST_MEPSpaceTags || bic == BuiltInCategory.OST_AreaTags)
                        return AnnotationCategory.RoomTag;
                    return AnnotationCategory.OtherTag;
                default:
                    return AnnotationCategory.OtherTag;
            }
        }

        /// <summary>
        /// Step 5 + 6: compare every pair once (A,B) not (B,A), skip self-pairs, and compute metrics
        /// for overlaps. With <paramref name="acrossTypes"/> false only annotations of the same Revit
        /// class are compared (V005 behaviour); with true every pair is compared, so the priority order
        /// can decide between, say, a door tag and a text note.
        /// </summary>
        /// <param name="annotations">Annotations to compare.</param>
        /// <param name="acrossTypes">Also compare annotations of different Revit classes.</param>
        /// <param name="minOverlapMm">Overlap must be deeper than this on both axes (0 = any overlap).</param>
        /// <param name="alignToleranceMm">Tolerance for the "Aligned" column.</param>
        public static List<OverlapResult> DetectOverlaps(List<AnnotationData> annotations, bool acrossTypes,
            double minOverlapMm = 0, double alignToleranceMm = DefaultAlignmentToleranceMm)
        {
            var results = new List<OverlapResult>();
            IEnumerable<List<AnnotationData>> groups = acrossTypes
                ? new[] { annotations }
                : annotations.GroupBy(a => a.ClassName).Select(g => g.ToList());

            foreach (var group in groups)
            {
                for (int i = 0; i < group.Count; i++)
                {
                    ToolCancellation.CheckCurrent();
                    for (int j = i + 1; j < group.Count; j++)
                    {
                        AnnotationData a = group[i];
                        AnnotationData b = group[j];

                        if (a.ElementId == b.ElementId)
                            continue; // edge case 7: exclude self-overlap

                        if (!BoundingBoxCalculator.DoBoxesIntersect(a, b, minOverlapMm))
                            continue;

                        bool aligned = CalculateGridAlignment(a, b, alignToleranceMm);
                        var (vGap, hGap) = BoundingBoxCalculator.CalculateGap(a, b);

                        results.Add(new OverlapResult
                        {
                            ElementId1 = a.ElementId.Value,
                            ElementId2 = b.ElementId.Value,
                            AnnotationType = a.Category == b.Category ? a.TypeName : $"{a.TypeName} ↔ {b.TypeName}",
                            X = Math.Round(a.X, 4),
                            Y = Math.Round(a.Y, 4),
                            GridAligned = aligned ? "Yes" : "No",
                            VerticalDistance = vGap,
                            HorizontalDistance = hGap
                        });
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Step 6: "Yes" if X OR Y insertion coordinates match within a
        /// 0.1mm tolerance (edge case 6), "No" otherwise.
        /// </summary>
        public static bool CalculateGridAlignment(AnnotationData a, AnnotationData b,
            double toleranceMm = DefaultAlignmentToleranceMm)
        {
            bool xAligned = Math.Abs(a.X - b.X) <= toleranceMm;
            bool yAligned = Math.Abs(a.Y - b.Y) <= toleranceMm;
            return xAligned || yAligned;
        }
    }
}
