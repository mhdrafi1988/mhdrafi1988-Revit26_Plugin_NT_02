using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Mechanical;
using Revit26_Plugin_Revised.AnnotationOverlapReport.V001.Core;

namespace Revit26_Plugin_Revised.AnnotationOverlapReport.V001.Infrastructure
{
    /// <summary>How the tool treats an annotation category.</summary>
    public enum CategoryKind
    {
        /// <summary>Listed and checked for overlaps.</summary>
        Checked,

        /// <summary>
        /// Listed greyed out: Revit's box covers the whole line or region, not just the head
        /// (grids, levels, section / elevation / callout marks, view titles). Planned for 1.1.
        /// </summary>
        Deferred,

        /// <summary>Not an annotation label; never listed.</summary>
        Ignored
    }

    /// <summary>What one view holds: measured boxes, deferred head counts, and elements with no box.</summary>
    public sealed class ViewScan
    {
        /// <summary>Boxes of every checked annotation, in paper mm.</summary>
        public List<PaperBox> Boxes { get; } = new List<PaperBox>();

        /// <summary>Count per deferred category name.</summary>
        public Dictionary<string, int> DeferredCounts { get; } = new Dictionary<string, int>();

        /// <summary>Checked annotations Revit returned no box for in this view.</summary>
        public int NotMeasured { get; set; }
    }

    /// <summary>Reads annotations and their boxes from a view. Read-only: opens no transaction.</summary>
    public static class AnnotationCollector
    {
        private const double FeetToMm = 304.8;

        private static readonly HashSet<BuiltInCategory> Deferred = new HashSet<BuiltInCategory>
        {
            BuiltInCategory.OST_Grids, BuiltInCategory.OST_GridHeads,
            BuiltInCategory.OST_Levels, BuiltInCategory.OST_LevelHeads,
            BuiltInCategory.OST_Sections, BuiltInCategory.OST_SectionHeads,
            BuiltInCategory.OST_Elev, BuiltInCategory.OST_ElevationMarks,
            BuiltInCategory.OST_Callouts, BuiltInCategory.OST_CalloutHeads,
            BuiltInCategory.OST_Viewers, BuiltInCategory.OST_Viewports
        };

        private static readonly HashSet<BuiltInCategory> Ignored = new HashSet<BuiltInCategory>
        {
            BuiltInCategory.OST_TitleBlocks, BuiltInCategory.OST_CLines, BuiltInCategory.OST_VolumeOfInterest,
            BuiltInCategory.OST_SectionBox, BuiltInCategory.OST_Matchline, BuiltInCategory.OST_Cameras,
            BuiltInCategory.OST_ScheduleGraphics, BuiltInCategory.OST_RevisionClouds,
            BuiltInCategory.OST_IOSDetailGroups, BuiltInCategory.OST_IOSAttachedDetailGroups,
            BuiltInCategory.OST_PlanRegion, BuiltInCategory.OST_MultiReferenceAnnotations,
            BuiltInCategory.OST_WeakDims, BuiltInCategory.OST_Constraints, BuiltInCategory.OST_ReferenceLines
        };

        /// <summary>Says whether a category is checked, listed as "coming in 1.1", or ignored.</summary>
        public static CategoryKind Classify(Category category)
        {
            if (category == null)
                return CategoryKind.Ignored;

            BuiltInCategory builtIn = category.BuiltInCategory;
            if (Deferred.Contains(builtIn))
                return CategoryKind.Deferred;
            if (category.CategoryType != CategoryType.Annotation || Ignored.Contains(builtIn))
                return CategoryKind.Ignored;
            return CategoryKind.Checked;
        }

        /// <summary>
        /// Category ids worth collecting in <paramref name="doc"/> (checked and deferred), so the per-view
        /// collector does not walk model elements.
        /// </summary>
        public static List<ElementId> RelevantCategoryIds(Document doc)
        {
            var ids = new List<ElementId>();
            foreach (Category category in doc.Settings.Categories)
            {
                if (Classify(category) != CategoryKind.Ignored)
                    ids.Add(category.Id);
            }
            return ids;
        }

        /// <summary>
        /// Collects the annotations of <paramref name="view"/>. Checked annotations become boxes in paper mm;
        /// deferred head types are only counted.
        /// </summary>
        /// <param name="doc">Document that owns the view.</param>
        /// <param name="view">A supported 2D view or sheet.</param>
        /// <param name="categoryIds">Result of <see cref="RelevantCategoryIds"/> for this document.</param>
        public static ViewScan Collect(Document doc, View view, ICollection<ElementId> categoryIds)
        {
            var scan = new ViewScan();
            if (doc == null || view == null || categoryIds == null || categoryIds.Count == 0)
                return scan;

            int scale = ViewCatalog.ScaleOf(view);
            XYZ right = view.RightDirection;
            XYZ up = view.UpDirection;

            var collector = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .WherePasses(new ElementMulticategoryFilter(categoryIds));

            foreach (Element element in collector)
            {
                Category category = element.Category;
                CategoryKind kind = Classify(category);
                if (kind == CategoryKind.Ignored)
                    continue;

                if (kind == CategoryKind.Deferred)
                {
                    scan.DeferredCounts.TryGetValue(category.Name, out int count);
                    scan.DeferredCounts[category.Name] = count + 1;
                    continue;
                }

                // Labels are drawn in one view; anything else in an annotation category (datums, regions) is not a label.
                if (!element.ViewSpecific)
                    continue;

                BoundingBoxXYZ box = element.get_BoundingBox(view);
                if (box == null)
                {
                    scan.NotMeasured++;
                    continue;
                }

                PaperBox paper = Project(box, right, up, scale);
                if (paper.Width <= 0 || paper.Height <= 0)
                {
                    scan.NotMeasured++;
                    continue;
                }

                paper.Id = element.Id.Value;
                paper.TypeName = category.Name;
                paper.Text = TextOf(element);
                scan.Boxes.Add(paper);
            }

            return scan;
        }

        /// <summary>
        /// The text an annotation shows: note text, tag text or dimension value; the element name otherwise.
        /// Never throws; returns an empty string when nothing can be read.
        /// </summary>
        public static string TextOf(Element element)
        {
            try
            {
                switch (element)
                {
                    case TextNote note:
                        return Shorten(note.Text);
                    case IndependentTag tag:
                        return Shorten(tag.TagText);
                    case RoomTag roomTag:
                        return Shorten(roomTag.TagText);
                    case AreaTag areaTag:
                        return Shorten(areaTag.TagText);
                    case SpaceTag spaceTag:
                        return Shorten(spaceTag.TagText);
                    case Dimension dimension:
                        return Shorten(DimensionText(dimension));
                    default:
                        return Shorten(element?.Name);
                }
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException || ex is InvalidOperationException)
            {
                return "";
            }
        }

        private static string DimensionText(Dimension dimension)
        {
            string value = dimension.ValueString;
            if (!string.IsNullOrWhiteSpace(value))
                return value;

            DimensionSegmentArray segments = dimension.Segments;
            if (segments == null || segments.Size == 0)
                return dimension.Name;

            var parts = new List<string>();
            foreach (DimensionSegment segment in segments)
            {
                if (parts.Count == 3) { parts.Add("…"); break; }
                parts.Add(segment.ValueString);
            }
            return string.Join(" / ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        private static string Shorten(string text)
        {
            string clean = ExcelReportWriter.Clean(text);
            return clean.Length > 80 ? clean.Substring(0, 79) + "…" : clean;
        }

        /// <summary>
        /// Projects all eight corners of a model-space box onto the view's right / up axes (so it works in
        /// plans, sections, elevations, drafting views and sheets) and converts to paper mm.
        /// </summary>
        private static PaperBox Project(BoundingBoxXYZ box, XYZ right, XYZ up, int scale)
        {
            double minR = double.MaxValue, maxR = double.MinValue;
            double minU = double.MaxValue, maxU = double.MinValue;
            foreach (double x in new[] { box.Min.X, box.Max.X })
            {
                foreach (double y in new[] { box.Min.Y, box.Max.Y })
                {
                    foreach (double z in new[] { box.Min.Z, box.Max.Z })
                    {
                        var point = new XYZ(x, y, z);
                        double r = point.DotProduct(right);
                        double u = point.DotProduct(up);
                        minR = Math.Min(minR, r); maxR = Math.Max(maxR, r);
                        minU = Math.Min(minU, u); maxU = Math.Max(maxU, u);
                    }
                }
            }

            return new PaperBox
            {
                X = OverlapFinder.ModelMmToPaperMm(minR * FeetToMm, scale),
                Y = OverlapFinder.ModelMmToPaperMm(minU * FeetToMm, scale),
                Width = OverlapFinder.ModelMmToPaperMm((maxR - minR) * FeetToMm, scale),
                Height = OverlapFinder.ModelMmToPaperMm((maxU - minU) * FeetToMm, scale)
            };
        }
    }
}
