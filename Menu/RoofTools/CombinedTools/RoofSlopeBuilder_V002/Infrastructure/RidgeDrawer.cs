// =======================================================
// File: RidgeDrawer.cs
// Namespace: Revit26_Plugin_Revised.RoofSlopeBuilder.V002.Infrastructure
// New in V002. Draws the solver's ridge lines and ridge points in the active
// plan view.
//   - Must be called inside an open transaction; it never opens or commits one.
//   - Ridge lines: detail lines in the chosen line style, or a line-based
//     detail family (one instance per straight piece).
//   - Fallbacks, each counted for the report:
//       family type missing / not line-based  -> every ridge as a detail line
//       one family placement refused          -> that piece as a detail line
//       line style missing or refused         -> Thin Lines -> Revit's default
//       piece shorter than Revit allows       -> skipped
//   - Ridge points: small detail circles in the same line style.
//   - Never throws for expected Revit refusals.
// =======================================================
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.RoofSlopeBuilder.V002.Core;

namespace Revit26_Plugin_Revised.RoofSlopeBuilder.V002.Infrastructure
{
    /// <summary>What <see cref="RidgeDrawer.Draw"/> did for one roof.</summary>
    public class RidgeDrawReport
    {
        /// <summary>Ridge pieces drawn as detail lines by choice.</summary>
        public int DetailLines;
        /// <summary>Ridge pieces placed as line-based detail families.</summary>
        public int Families;
        /// <summary>Ridge pieces drawn as detail lines because the family could not be used.</summary>
        public int FallbackLines;
        /// <summary>Ridge points marked with a circle.</summary>
        public int Circles;
        /// <summary>Pieces or circles Revit refused even as a detail line.</summary>
        public int Failed;
        /// <summary>Pieces shorter than Revit allows, left out.</summary>
        public int TooShort;
        /// <summary>Notes for the report: fallbacks taken, the first error, why nothing was drawn.</summary>
        public List<string> Notes = new List<string>();

        /// <summary>True when anything was drawn.</summary>
        public bool Any { get { return DetailLines + Families + FallbackLines + Circles > 0; } }
    }

    /// <summary>Draws ridge lines and ridge points as annotation in a plan view.</summary>
    public static class RidgeDrawer
    {
        private const string ThinLines = "Thin Lines";
        private const string RevitDefault = "Revit's default line style";

        /// <summary>Names of the model's line styles (OST_Lines sub-categories), sorted.</summary>
        public static List<string> LineStyleNames(Document doc)
        {
            try
            {
                Category lines = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
                return lines.SubCategories.Cast<Category>().Select(c => c.Name)
                    .Where(n => !n.StartsWith("<")).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return new List<string> { ThinLines };
            }
        }

        /// <summary>Line-based detail family types loaded in the model, as "Family : Type", sorted.</summary>
        public static List<string> DetailFamilyTypes(Document doc)
        {
            return LineBasedSymbols(doc).Select(Label).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private static IEnumerable<FamilySymbol> LineBasedSymbols(Document doc)
        {
            return new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                .Where(fs => fs.Family != null && fs.Family.FamilyPlacementType == FamilyPlacementType.CurveBasedDetail);
        }

        private static string Label(FamilySymbol fs) { return fs.Family.Name + " : " + fs.Name; }

        /// <summary>
        /// Draws <see cref="SolveResult.RidgeLines"/> and, when asked, <see cref="SolveResult.RidgeMarks"/>
        /// in <paramref name="view"/>. Call inside an open transaction.
        /// </summary>
        public static RidgeDrawReport Draw(Document doc, View view, SolveResult res, RoofSlopeSettings s)
        {
            RidgeDrawReport rep = new RidgeDrawReport();
            if (!s.DrawRidgeLines && !s.MarkRidgePoints) return rep;
            if (res.RidgeLines.Count == 0 && res.RidgeMarks.Count == 0)
            {
                rep.Notes.Add("no ridges to draw (a roof needs 2 or more selected drains)");
                return rep;
            }
            if (!(view is ViewPlan plan) || view.IsTemplate)
            {
                rep.Notes.Add("ridges not drawn: the active view is not a plan view");
                return rep;
            }

            double z;
            if (plan.GenLevel != null) z = plan.GenLevel.Elevation;
            else if (plan.SketchPlane != null) z = plan.SketchPlane.GetPlane().Origin.Z;
            else { rep.Notes.Add("ridges not drawn: the plan view has no level"); return rep; }

            double minLen = doc.Application.ShortCurveTolerance;
            StyleState style = new StyleState(doc, s.RidgeLineStyle, rep);

            if (s.DrawRidgeLines)
            {
                FamilySymbol symbol = s.RidgeLineKind == RidgeLineKind.DetailFamily ? ResolveSymbol(doc, s.RidgeFamilyType, rep) : null;
                string firstFamilyError = null;

                foreach (List<P2> line in res.RidgeLines)
                    for (int i = 1; i < line.Count; i++)
                    {
                        XYZ a = new XYZ(line[i - 1].X, line[i - 1].Y, z), b = new XYZ(line[i].X, line[i].Y, z);
                        if (a.DistanceTo(b) < minLen) { rep.TooShort++; continue; }
                        Line piece = Line.CreateBound(a, b);

                        if (symbol != null)
                        {
                            try
                            {
                                if (doc.Create.NewFamilyInstance(piece, symbol, plan) != null) { rep.Families++; continue; }
                                firstFamilyError = firstFamilyError ?? "Revit returned no instance";
                            }
                            catch (Autodesk.Revit.Exceptions.ApplicationException ex) { firstFamilyError = firstFamilyError ?? ex.Message; }
                            catch (ArgumentException ex) { firstFamilyError = firstFamilyError ?? ex.Message; }
                            catch (InvalidOperationException ex) { firstFamilyError = firstFamilyError ?? ex.Message; }
                            if (DetailCurve(doc, plan, piece, style, rep)) rep.FallbackLines++;
                        }
                        else if (DetailCurve(doc, plan, piece, style, rep)) rep.DetailLines++;
                    }

                if (rep.FallbackLines > 0)
                    rep.Notes.Add(string.Format("{0} ridge piece(s) could not be placed as \"{1}\" and were drawn as detail lines ({2})",
                        rep.FallbackLines, s.RidgeFamilyType, firstFamilyError));
            }

            if (s.MarkRidgePoints)
            {
                double r = Math.Max(s.RidgePointDiameter / 2, minLen);
                foreach (P2 p in res.RidgeMarks)
                {
                    // two half arcs, as Auto Slope By Point's circle markers do: a closed 0..2pi arc is refused
                    XYZ c = new XYZ(p.X, p.Y, z);
                    Arc halfA, halfB;
                    try
                    {
                        halfA = Arc.Create(c, r, 0, Math.PI, XYZ.BasisX, XYZ.BasisY);
                        halfB = Arc.Create(c, r, Math.PI, 2 * Math.PI, XYZ.BasisX, XYZ.BasisY);
                    }
                    catch (Autodesk.Revit.Exceptions.ApplicationException) { rep.Failed++; continue; }
                    bool a = DetailCurve(doc, plan, halfA, style, rep);
                    bool b = DetailCurve(doc, plan, halfB, style, rep);
                    if (a && b) rep.Circles++;
                }
            }

            if (style.Fallback != null) rep.Notes.Add(style.Fallback);
            if (rep.Failed > 0) rep.Notes.Add(rep.Failed + " ridge line(s) or circle(s) could not be drawn at all (" + style.FirstError + ")");
            if (rep.TooShort > 0) rep.Notes.Add(rep.TooShort + " ridge piece(s) shorter than Revit allows were left out");
            return rep;
        }

        /// <summary>The chosen line-based family type; null (and a note) when it cannot be used.</summary>
        private static FamilySymbol ResolveSymbol(Document doc, string label, RidgeDrawReport rep)
        {
            FamilySymbol symbol = string.IsNullOrWhiteSpace(label) ? null : LineBasedSymbols(doc).FirstOrDefault(fs => Label(fs) == label);
            if (symbol == null)
            {
                rep.Notes.Add(string.IsNullOrWhiteSpace(label)
                    ? "no line-based detail family chosen: ridges drawn as detail lines"
                    : "detail family \"" + label + "\" is not loaded in this model: ridges drawn as detail lines");
                return null;
            }
            try
            {
                if (!symbol.IsActive)
                {
                    symbol.Activate();
                    doc.Regenerate();   // activation only takes effect after a regenerate
                }
                return symbol;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                rep.Notes.Add("detail family \"" + label + "\" could not be activated (" + ex.Message + "): ridges drawn as detail lines");
                return null;
            }
        }

        /// <summary>Creates one detail curve and applies the line style (with fallback). False when Revit refused the curve.</summary>
        private static bool DetailCurve(Document doc, ViewPlan plan, Curve curve, StyleState style, RidgeDrawReport rep)
        {
            DetailCurve dc;
            try { dc = doc.Create.NewDetailCurve(plan, curve); }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex) { rep.Failed++; style.FirstError = style.FirstError ?? ex.Message; return false; }
            if (dc == null) { rep.Failed++; return false; }
            style.Apply(dc);
            return true;
        }

        /// <summary>The line style in use, falling back to Thin Lines and then Revit's default the first time one is refused.</summary>
        private sealed class StyleState
        {
            private readonly Document _doc;
            private GraphicsStyle _style;
            private string _name;
            public string Fallback;
            public string FirstError;

            public StyleState(Document doc, string chosen, RidgeDrawReport rep)
            {
                _doc = doc;
                chosen = string.IsNullOrWhiteSpace(chosen) ? ThinLines : chosen;
                _style = Find(chosen);
                _name = chosen;
                if (_style == null)
                {
                    _style = chosen == ThinLines ? null : Find(ThinLines);
                    _name = _style != null ? ThinLines : RevitDefault;
                    Fallback = "line style \"" + chosen + "\" not found: " + _name + " used";
                }
            }

            public void Apply(DetailCurve dc)
            {
                while (_style != null)
                {
                    try { dc.LineStyle = _style; return; }
                    catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                    {
                        string refused = _name;
                        _style = refused == ThinLines ? null : Find(ThinLines);
                        _name = _style != null ? ThinLines : RevitDefault;
                        Fallback = "line style \"" + refused + "\" was refused (" + ex.Message + "): " + _name + " used";
                    }
                }
            }

            private GraphicsStyle Find(string name)
            {
                try
                {
                    Category lines = _doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
                    Category sub = lines?.SubCategories?.Cast<Category>().FirstOrDefault(c => c.Name == name);
                    return sub?.GetGraphicsStyle(GraphicsStyleType.Projection);
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException) { return null; }
            }
        }
    }
}
