// =======================================================
// File: RidgeLineService.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Infrastructure.RoofOps
// New in V033. Draws the ridge lines found by Core.Services.RidgeFinder as
// detail lines in the active plan view, like the circle markers:
//   - Called inside the runner's open "Apply AutoSlope" transaction; it never
//     opens or commits its own, so Ctrl+Z undoes slope, markers and ridge
//     lines together, and Cancel or a failure rolls all of them back.
//   - Active view must be a plan view, else it skips with a warning.
//   - Line style: the one chosen in the window; if it is missing or Revit
//     refuses it, Thin Lines; if that fails too, Revit's default line style.
//     Every fallback is logged. Line styles are never created.
//   - A line that can't be created is skipped and counted; the run goes on.
//   - Lines from earlier runs are kept (as circle markers are).
// =======================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Models;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Services;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Infrastructure.RoofOps
{
    internal static class RidgeLineService
    {
        private const string RevitDefaultStyle = "Revit's default line style";

        /// <summary>
        /// Places one detail line per ridge line in <paramref name="activeView"/>. Never throws for
        /// expected problems (wrong view, missing style, a line Revit refuses): it logs and goes on.
        /// </summary>
        public static RidgeLineOutcome Place(
            Document doc,
            View activeView,
            IReadOnlyList<Point3> positions,
            RidgeResult ridges,
            AutoSlopeRequest request,
            Action<LogLevel, string> log)
        {
            var outcome = new RidgeLineOutcome();

            if (activeView is not ViewPlan plan)
            {
                log(LogLevel.Warning, "Ridge lines skipped: the active view is not a plan view.");
                return outcome;
            }
            if (plan.SketchPlane == null && plan.GenLevel == null)
            {
                log(LogLevel.Warning, "Ridge lines skipped: the active view has no level or sketch plane.");
                return outcome;
            }

            double viewElevFt = plan.GenLevel?.Elevation ?? 0;
            double minLengthFt = doc.Application.ShortCurveTolerance;
            GraphicsStyle style = ResolveStyle(doc, request, log, out string styleName);
            GraphicsStyle thin = style != null && styleName == AutoSlopeConstants.DefaultRidgeLineStyleName
                ? null
                : FindLineStyle(doc, AutoSlopeConstants.DefaultRidgeLineStyleName);
            string firstError = null;
            bool styleRefused = false;

            foreach (RidgeLine line in ridges.Lines)
            {
                Point3 a = positions[line.A], b = positions[line.B];
                var p1 = new XYZ(a.X, a.Y, viewElevFt);
                var p2 = new XYZ(b.X, b.Y, viewElevFt);
                if (p1.DistanceTo(p2) < minLengthFt)
                {
                    outcome.TooShort++;
                    continue;
                }

                DetailCurve dc;
                try
                {
                    dc = doc.Create.NewDetailCurve(plan, Line.CreateBound(p1, p2));
                }
                catch (Exception ex)
                {
                    outcome.Failed++;
                    firstError ??= ex.Message;
                    continue;
                }
                outcome.Placed++;

                if (style == null) continue; // Revit's default line style
                try
                {
                    dc.LineStyle = style;
                }
                catch (Exception ex)
                {
                    // Chosen style refused: fall back to Thin Lines for this and every later line.
                    if (!styleRefused)
                        log(LogLevel.Warning, $"Ridge lines: line style “{styleName}” could not be applied ({ex.Message}); {(thin != null ? AutoSlopeConstants.DefaultRidgeLineStyleName : RevitDefaultStyle)} used instead.");
                    styleRefused = true;
                    style = thin;
                    styleName = thin != null ? AutoSlopeConstants.DefaultRidgeLineStyleName : RevitDefaultStyle;
                    thin = null;
                    if (style == null) continue;
                    try { dc.LineStyle = style; }
                    catch (Exception ex2)
                    {
                        log(LogLevel.Warning, $"Ridge lines: {AutoSlopeConstants.DefaultRidgeLineStyleName} could not be applied either ({ex2.Message}); {RevitDefaultStyle} used instead.");
                        style = null;
                        styleName = RevitDefaultStyle;
                    }
                }
            }

            outcome.StyleUsed = styleName;
            if (outcome.Failed > 0)
                log(LogLevel.Warning, $"Ridge lines: {outcome.Failed} line(s) could not be placed ({firstError}).");
            if (outcome.TooShort > 0)
                log(LogLevel.Info, $"Ridge lines: {outcome.TooShort} line(s) shorter than Revit allows were left out.");
            return outcome;
        }

        /// <summary>The chosen style, else Thin Lines, else null (Revit's default). Logs each fallback.</summary>
        private static GraphicsStyle ResolveStyle(Document doc, AutoSlopeRequest request, Action<LogLevel, string> log, out string name)
        {
            string chosen = string.IsNullOrWhiteSpace(request.RidgeLineStyleName)
                ? AutoSlopeConstants.DefaultRidgeLineStyleName
                : request.RidgeLineStyleName;

            GraphicsStyle style = null;
            if (request.RidgeLineStyleIdValue > 0)
                style = doc.GetElement(new ElementId(request.RidgeLineStyleIdValue)) as GraphicsStyle;
            if (style == null || style.GraphicsStyleCategory?.Name != chosen)
                style = FindLineStyle(doc, chosen);
            if (style != null)
            {
                name = chosen;
                return style;
            }

            GraphicsStyle thin = FindLineStyle(doc, AutoSlopeConstants.DefaultRidgeLineStyleName);
            name = thin != null ? AutoSlopeConstants.DefaultRidgeLineStyleName : RevitDefaultStyle;
            log(LogLevel.Warning, $"Ridge lines: line style “{chosen}” was not found in this project; {name} used instead.");
            return thin;
        }

        /// <summary>Line style (OST_Lines sub-category) by name, or null.</summary>
        private static GraphicsStyle FindLineStyle(Document doc, string name)
        {
            try
            {
                Category lines = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
                Category sub = lines?.SubCategories?.Cast<Category>().FirstOrDefault(c => c.Name == name);
                return sub?.GetGraphicsStyle(GraphicsStyleType.Projection);
            }
            catch
            {
                return null;
            }
        }
    }
}
