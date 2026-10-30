using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.ScopeBoxApiSpike.Commands
{
    /// <summary>
    /// THROW-AWAY spike (branch spike/scopebox-api-spike, never merge). Finds out whether the Revit
    /// API can create or resize a scope box. Runs everything inside a Transaction that is ALWAYS rolled
    /// back, so the model is left unchanged. The report is shown and written to the Logs folder.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ScopeBoxApiSpikeCommand : IExternalCommand
    {
        /// <inheritdoc />
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteUnguarded);

        private static Result ExecuteUnguarded(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
            var log = new StringBuilder();

            // A. Does any public API type or Document.Create method mention scope boxes?
            log.AppendLine("== A. API surface ==");
            foreach (Type t in typeof(Document).Assembly.GetExportedTypes()
                         .Where(t => t.Name.IndexOf("ScopeBox", StringComparison.OrdinalIgnoreCase) >= 0
                                  || t.Name.IndexOf("VolumeOfInterest", StringComparison.OrdinalIgnoreCase) >= 0))
                log.AppendLine($"  type: {t.FullName}");
            foreach (MethodInfo m in typeof(Autodesk.Revit.Creation.Document).GetMethods()
                         .Where(m => m.Name.IndexOf("Scope", StringComparison.OrdinalIgnoreCase) >= 0
                                  || m.Name.IndexOf("Volume", StringComparison.OrdinalIgnoreCase) >= 0))
                log.AppendLine($"  Creation.Document.{m.Name}");
            log.AppendLine("  (nothing listed above = no dedicated scope box API)");

            // B. Copy / edit experiments on an existing scope box.
            Element sb = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_VolumeOfInterest)
                .WhereElementIsNotElementType().FirstOrDefault();
            if (sb == null)
            {
                log.AppendLine("== B. skipped: the model has no scope box. Create one and run again ==");
                return Finish(log);
            }

            BoundingBoxXYZ before = sb.get_BoundingBox(null);
            log.AppendLine($"== B. Source '{sb.Name}' (Id {sb.Id.Value}) ==");
            log.AppendLine($"  size {Size(before)}");
            log.AppendLine($"  Location: {sb.Location?.GetType().Name ?? "null"}");
            foreach (Parameter p in sb.Parameters)
                log.AppendLine($"  param '{p.Definition.Name}' readOnly={p.IsReadOnly} {p.StorageType}");

            using (var tx = new Transaction(doc, "Scope box API spike (rolled back)"))
            {
                tx.Start();
                try
                {
                    ElementId copyId;
                    try
                    {
                        copyId = ElementTransformUtils.CopyElement(doc, sb.Id, new XYZ(50, 0, 0)).First();
                        log.AppendLine($"  COPY: OK (new Id {copyId.Value})");
                    }
                    catch (Exception ex)
                    {
                        log.AppendLine($"  COPY: FAILED - {ex.GetType().Name}: {ex.Message}");
                        return Finish(log);
                    }

                    Element copy = doc.GetElement(copyId);
                    doc.Regenerate();
                    log.AppendLine($"  copy size {Size(copy.get_BoundingBox(null))}");

                    // Name is the only parameter expected to be writable.
                    try { copy.Name = "SPIKE_COPY"; log.AppendLine("  RENAME copy: OK"); }
                    catch (Exception ex) { log.AppendLine($"  RENAME copy: FAILED - {ex.Message}"); }

                    // Resize attempts: any writable Double parameter, then a Location-based route.
                    int tried = 0;
                    foreach (Parameter p in copy.Parameters.Cast<Parameter>()
                                 .Where(p => !p.IsReadOnly && p.StorageType == StorageType.Double))
                    {
                        tried++;
                        double old = p.AsDouble();
                        bool ok = p.Set(old + 10);
                        doc.Regenerate();
                        log.AppendLine($"  RESIZE via param '{p.Definition.Name}': set={ok}, size now {Size(copy.get_BoundingBox(null))}");
                    }
                    if (tried == 0) log.AppendLine("  RESIZE via parameter: no writable Double parameter");

                    log.AppendLine(copy.Location is LocationPoint
                        ? "  Location is a point (could move/rotate, not resize)"
                        : "  Location route: nothing usable (scale is not exposed for scope boxes)");
                }
                finally
                {
                    tx.RollBack();   // the spike never changes the model
                }
            }

            log.AppendLine("== Done: transaction rolled back, model unchanged ==");
            return Finish(log);
        }

        private static string Size(BoundingBoxXYZ bb) => bb == null
            ? "n/a"
            : $"{(bb.Max.X - bb.Min.X) * 304.8:0} x {(bb.Max.Y - bb.Min.Y) * 304.8:0} x {(bb.Max.Z - bb.Min.Z) * 304.8:0} mm";

        private static Result Finish(StringBuilder log)
        {
            string text = log.ToString();
            try
            {
                Directory.CreateDirectory(ToolGuard.LogFolder);
                File.WriteAllText(Path.Combine(ToolGuard.LogFolder, "ScopeBoxApiSpike.txt"), text);
            }
            catch { /* the dialog below is the primary output */ }
            TaskDialog.Show("Scope Box API spike", text);
            return Result.Succeeded;
        }
    }
}
