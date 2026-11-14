// =======================================================
// File: RoofSlopeBuilderCommand.cs
// Namespace: Revit26_Plugin_Revised.RoofSlopeBuilder.V002.Commands
// Ribbon string: "Revit26_Plugin_Revised.RoofSlopeBuilder.V002.Commands.RoofSlopeBuilderCommand"
// One command: inner loops + perpendicular points + ridge points + curve points + slope by drain,
// on every selected footprint roof.
//   - Uses the selection when it holds footprint roofs, otherwise asks the user to pick.
//   - Each roof is checked first (RoofChecks): read-only, group, slope-defining edges,
//     shape-edit support, borrowed by another user. Failing roofs are listed and left out.
//   - Settings are loaded from and saved to %AppData%\Revit26_Plugin_Revised\RoofSlopeBuilder.
//   - One transaction per roof inside one group: a failing roof is rolled back, the others kept,
//     one Undo removes the whole run.
//   - V002: ridge lines (detail lines or a line-based detail family, falling back
//     to detail lines) and ridge points (detail circles) drawn in the active plan
//     view, inside each roof's transaction; "Find ridges only" draws them without
//     changing the roofs.
//   - Body runs through ToolGuard.RunCommand.
// =======================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Revit26_Plugin_Revised.RoofSlopeBuilder.V002.Core;
using Revit26_Plugin_Revised.RoofSlopeBuilder.V002.Infrastructure;
using Revit26_Plugin_Revised.RoofSlopeBuilder.V002.UI;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.RoofSlopeBuilder.V002.Commands
{
    /// <summary>Ribbon command of Roof Slope Builder: pick roofs, set options, slope them to their drains.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RoofSlopeBuilderCommand : IExternalCommand
    {
        private static string Title => ToolCatalog.RoofSlopeBuilderV002.Name;

        private class RoofFilter : ISelectionFilter
        {
            public bool AllowElement(Element e) { return e is FootPrintRoof; }
            public bool AllowReference(Reference r, XYZ p) { return false; }
        }

        /// <summary>Revit entry point; delegates to <see cref="ToolGuard.RunCommand"/>.</summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteCore);

        private static Result ExecuteCore(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null)
            {
                TaskDialog.Show(Title, "Open a project first.");
                return Result.Cancelled;
            }
            Document doc = uidoc.Document;
            if (doc.IsFamilyDocument)
            {
                TaskDialog.Show(Title, "Roof Slope Builder works in a project, not in the Family Editor.");
                return Result.Cancelled;
            }

            // ---- 1. roofs: current selection, otherwise pick ----
            List<Element> selected = uidoc.Selection.GetElementIds().Select(id => doc.GetElement(id)).Where(e => e != null).ToList();
            List<FootPrintRoof> roofs = selected.OfType<FootPrintRoof>().ToList();
            int ignored = roofs.Count > 0 ? selected.Count - roofs.Count : 0;

            if (roofs.Count == 0)
            {
                try
                {
                    roofs = uidoc.Selection
                        .PickObjects(ObjectType.Element, new RoofFilter(), "Select footprint roofs to slope, then press Finish")
                        .Select(r => doc.GetElement(r)).OfType<FootPrintRoof>().ToList();
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            }
            if (roofs.Count == 0)
            {
                TaskDialog.Show(Title, "No footprint roof selected. Extrusion roofs and in-place roofs are not supported.");
                return Result.Cancelled;
            }

            // ---- 2. check + read geometry + default drains ----
            RoofSlopeSettings settings = SettingsStore.Load(out string settingsWarning);
            List<RoofModel> models = new List<RoofModel>();
            StringBuilder skipped = new StringBuilder();
            if (ignored > 0) skipped.AppendLine(ignored + " selected element(s) are not footprint roofs and were ignored.");

            foreach (FootPrintRoof roof in roofs)
            {
                string label = roof.Name + " [" + roof.Id.Value + "]";
                string block = RoofChecks.GetBlockReason(roof);
                if (block != null) { skipped.AppendLine(label + ": " + block); continue; }

                try
                {
                    RoofModel m = RoofReader.Read(roof);
                    if (!RoofChecks.HasVariableLayer(roof))
                        m.Note = "No Variable layer: the whole roof will tilt";
                    DrainDetector.ApplyDefaultSelection(m.Input.Drains, settings);
                    models.Add(m);
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is Autodesk.Revit.Exceptions.ApplicationException)
                {
                    skipped.AppendLine(label + ": " + ex.Message);
                }
            }
            if (models.Count == 0)
            {
                TaskDialog td = new TaskDialog(Title)
                {
                    MainIcon = TaskDialogIcon.TaskDialogIconWarning,
                    MainInstruction = "None of the selected roofs can be sloped",
                    MainContent = skipped.ToString()
                };
                td.Show();
                return Result.Cancelled;
            }

            // ---- 3. dialog ----
            View view = uidoc.ActiveView;
            RoofSlopeBuilderWindow win = new RoofSlopeBuilderWindow(models, settings, skipped.ToString(), settingsWarning,
                RidgeDrawer.LineStyleNames(doc), RidgeDrawer.DetailFamilyTypes(doc), view is ViewPlan && !view.IsTemplate);
            new WindowInteropHelper(win).Owner = commandData.Application.MainWindowHandle;
            if (win.ShowDialog() != true) return Result.Cancelled;
            bool findOnly = win.FindRidgesOnly;
            bool draw = settings.DrawRidgeLines || settings.MarkRidgePoints;

            string saveWarning = SettingsStore.Save(settings);

            // ---- 4. apply: one transaction per roof so a failing roof does not undo the others ----
            List<RoofReport> reports = new List<RoofReport>();
            Exception unexpected = null;
            using (TransactionGroup group = new TransactionGroup(doc, findOnly ? Title + " - find ridges" : Title))
            {
                group.Start();
                foreach (RoofModel m in models.Where(x => x.Use))
                {
                    FootPrintRoof roof = doc.GetElement(m.Id) as FootPrintRoof;
                    using (Transaction t = new Transaction(doc, (findOnly ? "Draw ridges, roof " : "Slope roof ") + m.Id.Value))
                    {
                        t.Start();
                        try
                        {
                            RoofReport rep = findOnly ? FindRidges(doc, view, roof, m, settings) : RoofWriter.Apply(doc, roof, m, settings);
                            // slope run: ridges go in the same transaction, so one Undo removes slope and drawing together;
                            // a drawing problem is a note, never a reason to drop the slope
                            if (!findOnly && rep.Ok && draw && rep.Result != null)
                                rep.Ridges = RidgeDrawer.Draw(doc, view, rep.Result, settings);
                            reports.Add(rep);
                            if (rep.Ok && t.Commit() != TransactionStatus.Committed)
                            {
                                rep.Ok = false;
                                rep.Error = "Revit did not accept the change (see its warnings)";
                            }
                            else if (!rep.Ok) t.RollBack();
                        }
                        catch (Exception ex)
                        {
                            if (t.HasStarted() && !t.HasEnded()) t.RollBack();
                            bool expected = ex is InvalidOperationException || ex is Autodesk.Revit.Exceptions.ApplicationException;
                            if (!expected && unexpected == null) unexpected = ex;
                            reports.Add(new RoofReport { Name = m.Name, Ok = false, Error = ex.Message });
                        }
                    }
                }
                if (reports.Any(r => r.Ok)) group.Assimilate();
                else group.RollBack();
            }

            if (unexpected != null) ToolGuard.Report(typeof(RoofSlopeBuilderCommand), unexpected);
            ShowReport(reports, skipped.ToString(), saveWarning, findOnly);
            return reports.Any(r => r.Ok) ? Result.Succeeded : Result.Failed;
        }

        /// <summary>
        /// "Find ridges only": solves the roof without touching its shape and draws the ridges.
        /// Ok only when something was drawn.
        /// </summary>
        private static RoofReport FindRidges(Document doc, View view, FootPrintRoof roof, RoofModel m, RoofSlopeSettings settings)
        {
            RoofReport rep = new RoofReport { Name = m.Name };
            if (roof == null || !roof.IsValidObject) throw new InvalidOperationException("The roof no longer exists.");
            m.Input.ExistingVertices = new List<P2>();   // only matters for point placement, not for the ridges
            SolveResult res = SlopeSolver.Solve(m.Input, settings);
            rep.Result = res;
            rep.Drains = res.DrainCount;
            if (res.Skipped) { rep.Error = "no drain selected"; return rep; }
            rep.Ridges = RidgeDrawer.Draw(doc, view, res, settings);
            rep.Ok = rep.Ridges.Any;
            if (!rep.Ok) rep.Error = rep.Ridges.Notes.Count > 0 ? string.Join("; ", rep.Ridges.Notes) : "nothing to draw";
            return rep;
        }

        private static string RidgeLine(RidgeDrawReport d)
        {
            List<string> parts = new List<string>();
            if (d.DetailLines > 0) parts.Add(d.DetailLines + " ridge detail lines");
            if (d.Families > 0) parts.Add(d.Families + " ridge detail families");
            if (d.FallbackLines > 0) parts.Add(d.FallbackLines + " fallback detail lines");
            if (d.Circles > 0) parts.Add(d.Circles + " ridge point circles");
            return parts.Count == 0 ? "no ridges drawn" : string.Join(", ", parts);
        }

        private static void ShowReport(List<RoofReport> reports, string skipped, string saveWarning, bool findOnly)
        {
            StringBuilder sb = new StringBuilder();
            foreach (RoofReport r in reports)
            {
                if (r.Ok && findOnly)
                    sb.AppendLine(r.Name + "\n   " + r.Drains + " drains, " + RidgeLine(r.Ridges));
                else if (r.Ok)
                    sb.AppendLine(string.Format("{0}\n   {1} drains, {2} points added ({3} ridge), {4} split lines, max height {5:0} mm{6}{7}",
                        r.Name, r.Drains, r.PointsAdded, r.RidgePoints, r.SplitLines, r.MaxHeightMm,
                        r.PointsFailed > 0 ? ", " + r.PointsFailed + " points rejected by Revit" : "",
                        r.SplitLinesFailed > 0 ? ", " + r.SplitLinesFailed + " split lines rejected" : ""));
                else
                    sb.AppendLine(r.Name + "\n   SKIPPED: " + r.Error);
                if (r.Ok && !findOnly && r.Ridges != null) sb.AppendLine("   " + RidgeLine(r.Ridges));
                foreach (string w in r.Warnings.Distinct()) sb.AppendLine("   ! " + w);
                if (r.Ridges != null && (r.Ok || !findOnly))
                    foreach (string n in r.Ridges.Notes.Distinct()) sb.AppendLine("   ! " + n);
                sb.AppendLine();
            }
            if (skipped.Length > 0) sb.AppendLine("Not processed:\n" + skipped);
            if (saveWarning != null) sb.AppendLine(saveWarning);

            TaskDialog td = new TaskDialog(Title);
            td.MainInstruction = string.Format(findOnly ? "Ridges drawn on {0} of {1} roofs" : "{0} of {1} roofs sloped",
                reports.Count(r => r.Ok), reports.Count);
            td.MainContent = sb.ToString();
            td.FooterText = ToolCatalog.RoofSlopeBuilderV002.Title;
            td.Show();
        }
    }
}
