using Revit26_Plugin_Revised.Shared.Services;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.AnnotationOverlapDetection.V004.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.AnnotationOverlapDetection.V004.Helpers
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
        private const double AlignmentToleranceMm = 0.1;

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

                    results.Add(new AnnotationData
                    {
                        ElementId = elem.Id,
                        TypeName = type.Name,
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

        /// <summary>
        /// Step 5 + 6: for each selected type group, compare every pair once
        /// (A,B) not (B,A), skip self-pairs, and compute metrics for overlaps.
        /// </summary>
        public static List<OverlapResult> DetectOverlaps(Dictionary<string, List<AnnotationData>> groupedByType)
        {
            var results = new List<OverlapResult>();

            foreach (var group in groupedByType.Values)
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

                        if (!BoundingBoxCalculator.DoBoxesIntersect(a, b))
                            continue;

                        bool aligned = CalculateGridAlignment(a, b);
                        var (vGap, hGap) = BoundingBoxCalculator.CalculateGap(a, b);

                        results.Add(new OverlapResult
                        {
                            ElementId1 = a.ElementId.Value,
                            ElementId2 = b.ElementId.Value,
                            AnnotationType = a.TypeName,
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
        public static bool CalculateGridAlignment(AnnotationData a, AnnotationData b)
        {
            bool xAligned = Math.Abs(a.X - b.X) <= AlignmentToleranceMm;
            bool yAligned = Math.Abs(a.Y - b.Y) <= AlignmentToleranceMm;
            return xAligned || yAligned;
        }
    }
}
