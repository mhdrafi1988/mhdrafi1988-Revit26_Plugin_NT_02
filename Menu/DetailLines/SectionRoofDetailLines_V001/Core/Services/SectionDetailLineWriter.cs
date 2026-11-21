using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.SectionRoofDetailLines.V001.Core.Services
{
    /// <summary>What one call to the writer created.</summary>
    public sealed class WriteResult
    {
        /// <summary>Detail lines created.</summary>
        public int Created { get; set; }

        /// <summary>Lines left out because the view already had the same line in the same style.</summary>
        public int SkippedExisting { get; set; }

        /// <summary>Lines Revit refused, or that were too short to create.</summary>
        public int Failed { get; set; }
    }

    /// <summary>
    /// Creates the detail lines in a section view. Must be called inside an open transaction —
    /// the caller owns the transaction so that one run of every section is a single Undo step.
    /// </summary>
    public sealed class SectionDetailLineWriter
    {
        /// <summary>How close two endpoints must be for two lines to count as the same line, in
        /// feet — about 1 mm, so re-running the tool does not stack a second copy on the first.</summary>
        private const double SameLineFeet = 1.0 / 304.8;

        /// <summary>
        /// Creates one detail line per curve in <paramref name="curves"/>, in
        /// <paramref name="view"/>, in <paramref name="style"/> (null leaves the view's default
        /// style). With <paramref name="skipExisting"/>, a curve whose ends already match a line
        /// in that view and style is left out instead of duplicated.
        /// </summary>
        public WriteResult Write(
            Document doc, View view, IEnumerable<Curve> curves, GraphicsStyle style,
            bool skipExisting, Action<string> onWarning)
        {
            var result = new WriteResult();
            if (doc == null || view == null || curves == null) return result;

            double shortCurve = doc.Application.ShortCurveTolerance;
            List<ExistingLine> existing = skipExisting ? ReadExisting(doc, view, style?.Name) : null;

            foreach (Curve curve in curves)
            {
                if (curve == null) continue;

                double length;
                try
                {
                    length = curve.Length;
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    result.Failed++;
                    continue;
                }

                if (length <= shortCurve)
                {
                    result.Failed++;
                    continue;
                }

                if (existing != null && Matches(existing, curve))
                {
                    result.SkippedExisting++;
                    continue;
                }

                DetailCurve created;
                try
                {
                    created = doc.Create.NewDetailCurve(view, curve);
                }
                catch (Exception ex)
                {
                    // A curve Revit will not take as a detail line — not in the view's plane, or
                    // outside what it accepts — is reported and skipped; the rest still go in.
                    onWarning?.Invoke($"Revit refused one line in '{view.Name}': {ex.Message}");
                    result.Failed++;
                    continue;
                }

                if (style != null)
                {
                    try
                    {
                        created.LineStyle = style;
                    }
                    catch (Exception ex)
                    {
                        onWarning?.Invoke($"Line style '{style.Name}' could not be applied: {ex.Message}");
                    }
                }

                result.Created++;

                // A line created now counts for the next curve in the same run, so two identical
                // curves from two elements do not both go in.
                existing?.Add(new ExistingLine(curve.GetEndPoint(0), curve.GetEndPoint(1)));
            }

            return result;
        }

        /// <summary>
        /// The project's detail line styles — the subcategories of Lines — by name, for the two
        /// style pickers. Empty when the category cannot be read.
        /// </summary>
        public static List<string> GetLineStyleNames(Document doc)
        {
            var names = new List<string>();
            Category lines = doc?.Settings?.Categories?.get_Item(BuiltInCategory.OST_Lines);
            if (lines?.SubCategories == null) return names;

            foreach (Category sub in lines.SubCategories)
            {
                if (sub?.GetGraphicsStyle(GraphicsStyleType.Projection) == null) continue;
                if (!string.IsNullOrWhiteSpace(sub.Name)) names.Add(sub.Name);
            }
            return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// The <see cref="GraphicsStyle"/> named <paramref name="name"/>, or null when the name is
        /// empty or the project has no such style — in which case the line keeps the view's
        /// default style rather than the run failing.
        /// </summary>
        public static GraphicsStyle FindStyle(Document doc, string name)
        {
            if (doc == null || string.IsNullOrWhiteSpace(name)) return null;
            Category lines = doc.Settings?.Categories?.get_Item(BuiltInCategory.OST_Lines);
            if (lines?.SubCategories == null) return null;

            foreach (Category sub in lines.SubCategories)
                if (string.Equals(sub?.Name, name, StringComparison.OrdinalIgnoreCase))
                    return sub.GetGraphicsStyle(GraphicsStyleType.Projection);
            return null;
        }

        private static List<ExistingLine> ReadExisting(Document doc, View view, string styleName)
        {
            var lines = new List<ExistingLine>();
            IEnumerable<CurveElement> elements;
            try
            {
                elements = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(CurveElement))
                    .Cast<CurveElement>()
                    .ToList();
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return lines;
            }

            foreach (CurveElement element in elements)
            {
                if (styleName != null)
                {
                    string existingStyle = SafeStyleName(element);
                    if (existingStyle != null && !string.Equals(existingStyle, styleName, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                Curve curve = SafeCurve(element);
                if (curve == null) continue;
                lines.Add(new ExistingLine(curve.GetEndPoint(0), curve.GetEndPoint(1)));
            }

            return lines;
        }

        private static bool Matches(List<ExistingLine> existing, Curve curve)
        {
            XYZ a = curve.GetEndPoint(0);
            XYZ b = curve.GetEndPoint(1);
            foreach (ExistingLine line in existing)
                if (line.SameEnds(a, b, SameLineFeet)) return true;
            return false;
        }

        private static Curve SafeCurve(CurveElement element)
        {
            try { return element.GeometryCurve; }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { return null; }
        }

        private static string SafeStyleName(CurveElement element)
        {
            try { return element.LineStyle?.Name; }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { return null; }
        }

        private readonly struct ExistingLine
        {
            private readonly XYZ _a;
            private readonly XYZ _b;

            public ExistingLine(XYZ a, XYZ b)
            {
                _a = a;
                _b = b;
            }

            /// <summary>True when the two endpoints are the same pair, in either order.</summary>
            public bool SameEnds(XYZ a, XYZ b, double tolerance)
                => (_a.DistanceTo(a) <= tolerance && _b.DistanceTo(b) <= tolerance)
                || (_a.DistanceTo(b) <= tolerance && _b.DistanceTo(a) <= tolerance);
        }
    }
}
