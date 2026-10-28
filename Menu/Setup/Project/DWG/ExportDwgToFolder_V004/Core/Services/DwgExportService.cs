using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.ExportDwgToFolder.V004.Core.Models;

namespace Revit26_Plugin_Revised.ExportDwgToFolder.V004.Core.Services
{
    /// <summary>
    /// Writes one DWG file (and optionally a sidecar .txt) for each <see cref="DwgInstanceInfo"/>.
    /// V002: both CAD links and CAD imports are now exported by temporarily isolating that single
    /// <see cref="ImportInstance"/> in its host view — hiding every other element — and exporting
    /// just that view, so the output DWG contains only the one instance, not everything else on the
    /// view. The isolate is done inside a <see cref="Transaction"/> that is always rolled back, so
    /// nothing about the temporary view state is ever kept in the model.
    /// A linked DWG with no resolvable host view (e.g. a model-space link not visible in any scanned
    /// view) falls back to a plain file copy of the external source file, since there is no view to
    /// isolate it in. An imported DWG with no host view cannot be exported at all (its geometry only
    /// exists embedded in the model) and is reported as an error, as before.
    /// <para>
    /// V004 (layer fidelity): the export options now come from
    /// <see cref="DwgExportOptionsFactory"/> instead of being built inline, so a DWG export setup
    /// saved in the model (and its layer table) can be used. And when
    /// <see cref="ExportDwgSettings.CopyOriginalForLinks"/> is on — the default — a CAD link whose
    /// source file resolves on disk is copied rather than re-exported: a copy is byte-for-byte
    /// identical, so its layer structure is exactly the source's. That deliberately reverses V002's
    /// "always re-export links through the view" decision, which is why it is a labelled setting and
    /// why every row reports which route produced its file.
    /// </para>
    /// </summary>
    public class DwgExportService
    {
        private readonly Document _doc;
        private readonly ExportDwgSettings _settings;

        /// <summary>
        /// Set once per run by the first <see cref="Export"/> call that builds options, when a
        /// requested export setup could not be loaded. The caller logs it once.
        /// </summary>
        public string OptionsWarning { get; private set; }

        public DwgExportService(Document doc, ExportDwgSettings settings)
        {
            _doc = doc;
            _settings = settings ?? new ExportDwgSettings();
        }

        /// <summary>
        /// Exports one DWG to <paramref name="destPath"/>.
        /// Returns null on success, <c>"SKIP"</c> when the file exists and overwrite is off, or an
        /// error message on failure.
        /// </summary>
        /// <param name="route">
        /// Which route produced the file — "copied source file" or "re-exported from view" — so the
        /// log and the row's status note never leave it ambiguous. Null when nothing was written.
        /// </param>
        public string Export(DwgInstanceInfo info, string destPath, bool overwrite, out string route)
        {
            route = null;

            if (!overwrite && File.Exists(destPath))
                return "SKIP";

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destPath));

                bool hasHostView = info.OwnerViewId != null && info.OwnerViewId != ElementId.InvalidElementId;

                // V004: a link with a resolvable source file is copied in preference to being
                // re-exported — byte-for-byte layers beat anything an export setup can do.
                if (info.SourceType == DwgSourceType.Link &&
                    _settings.CopyOriginalForLinks &&
                    !string.IsNullOrEmpty(info.LinkedSourcePath) &&
                    File.Exists(info.LinkedSourcePath))
                {
                    route = "copied source file";
                    return ExportLinkedCopy(info, destPath);
                }

                if (hasHostView)
                {
                    route = "re-exported from view";
                    return ExportIsolated(info, destPath, ref route);
                }

                if (info.SourceType == DwgSourceType.Link)
                {
                    route = "copied source file";
                    return ExportLinkedCopy(info, destPath);
                }

                route = null;
                return "No host view found for this imported DWG — it may be a model-space import not visible in any scanned view.";
            }
            catch (Exception ex)
            {
                route = null;
                return ex.Message;
            }
        }

        /// <summary>Writes the sidecar .txt file alongside the exported DWG.</summary>
        public void WriteSidecar(DwgInstanceInfo info, string dwgDestPath)
        {
            string sidecarPath = Path.ChangeExtension(dwgDestPath, ".txt");
            string content =
                $"View Name: {info.ViewName ?? "(none)"}\r\n" +
                $"Associated Sheet: {info.SheetInfo}\r\n" +
                $"Element ID: {info.ElementId.Value}\r\n" +
                $"Symbol Name: {info.SymbolName}\r\n";
            File.WriteAllText(sidecarPath, content, System.Text.Encoding.UTF8);
        }

        // ── Isolated export (Link + Import, when a host view is known) ────────

        /// <summary>
        /// Temporarily isolates just <paramref name="info"/>'s element in its host view (hiding every
        /// other element), exports that view to a temp folder, then renames the result to
        /// <paramref name="destPath"/>. The isolate never survives past this call — it runs inside a
        /// transaction that is rolled back whether the export succeeds or not.
        /// </summary>
        private string ExportIsolated(DwgInstanceInfo info, string destPath, ref string route)
        {
            var view = _doc.GetElement(info.OwnerViewId) as View;
            if (view == null)
                return "Host view could not be resolved.";

            string fileNameNoExt = Path.GetFileNameWithoutExtension(destPath);

            // Revit appends the view name to the exported file, so we export via a temp folder
            // then rename the result to the desired output name.
            string tempFolder = Path.Combine(Path.GetTempPath(), $"ExportDwg_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempFolder);

            try
            {
                using (var tx = new Transaction(_doc, "Isolate CAD instance for export"))
                {
                    tx.Start();
                    try
                    {
                        // V004 (4.1.2): hide everything in the view except this one CAD link/import
                        // with a real hide, not a temporary isolate — DWG export does not reliably
                        // honour Temporary Hide/Isolate, which let other Revit elements leak into the
                        // file. All of it is undone by the rollback below.
                        try
                        {
                            HideEverythingExcept(view, info.ElementId);
                        }
                        catch (Exception hideEx)
                        {
                            throw new InvalidOperationException(
                                $"Could not hide the other elements in \"{view.Name}\" — check the view's " +
                                $"template and V/G overrides. ({hideEx.Message})", hideEx);
                        }

                        // Make sure the export sees the hidden state, not the pre-hide view.
                        _doc.Regenerate();

                        // V004: options (and with them the layer table) come from the factory —
                        // a named export setup when one is chosen, layer-safe defaults otherwise.
                        var opts = DwgExportOptionsFactory.Build(_doc, _settings, out string optionsWarning);
                        if (optionsWarning != null && OptionsWarning == null)
                            OptionsWarning = optionsWarning;

                        var viewIds = new List<ElementId> { info.OwnerViewId };
                        _doc.Export(tempFolder, fileNameNoExt, viewIds, opts);
                    }
                    finally
                    {
                        // Never keep the temporary isolate around, whether the export above
                        // succeeded, failed, or threw.
                        tx.RollBack();
                    }
                }

                // Revit may produce "stem.dwg" or "stem - View Name.dwg"; find whatever it wrote.
                var produced = Directory.GetFiles(tempFolder, "*.dwg");
                if (produced.Length == 0)
                {
                    // Nothing came out of the isolated export — for a link, fall back to a plain
                    // copy of the source file rather than failing the row outright.
                    if (info.SourceType != DwgSourceType.Link)
                    {
                        route = null;
                        return "Revit Export produced no DWG file.";
                    }

                    route = "copied source file (export produced nothing)";
                    return ExportLinkedCopy(info, destPath);
                }

                // If more than one file, take the one whose name starts with our stem.
                string chosen = produced[0];
                foreach (var f in produced)
                {
                    if (Path.GetFileName(f).StartsWith(fileNameNoExt, StringComparison.OrdinalIgnoreCase))
                    {
                        chosen = f;
                        break;
                    }
                }

                if (File.Exists(destPath))
                    File.Delete(destPath);

                File.Move(chosen, destPath);
                return null; // success
            }
            finally
            {
                try { Directory.Delete(tempFolder, recursive: true); } catch { }
            }
        }

        /// <summary>
        /// Hides, in <paramref name="view"/>, every element except <paramref name="keepId"/>, and turns
        /// off the crop region display. Only called inside a transaction that is rolled back, so the
        /// view always returns to its earlier state. Uses a view template's settings only where
        /// allowed: a view template is detached first, since it can lock V/G (also rolled back).
        /// </summary>
        private void HideEverythingExcept(View view, ElementId keepId)
        {
            // A template controlling V/G can block HideElements; detach it (rolled back later).
            if (view.ViewTemplateId != ElementId.InvalidElementId)
                view.ViewTemplateId = ElementId.InvalidElementId;

            // A temporary hide/isolate mode left on by the user would also hide things from us.
            if (view.IsInTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate))
                view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);

            try { view.CropBoxVisible = false; } catch { /* not every view has a crop box */ }

            var toHide = new List<ElementId>();
            foreach (var el in new FilteredElementCollector(_doc, view.Id).WhereElementIsNotElementType())
            {
                if (el.Id == keepId) continue;
                if (el.Id == view.Id) continue;
                try
                {
                    if (el.CanBeHidden(view) && !el.IsHidden(view))
                        toHide.Add(el.Id);
                }
                catch { /* element that can't be queried in this view — leave it */ }
            }

            if (toHide.Count > 0)
            {
                try
                {
                    view.HideElements(toHide);
                }
                catch
                {
                    // One bad element fails the whole batch — fall back to one at a time so the rest
                    // still get hidden.
                    foreach (var id in toHide)
                    {
                        try { view.HideElements(new List<ElementId> { id }); } catch { }
                    }
                }
            }

            // Items that are not elements in the view collector (linked RVT contents, grids/levels
            // coming through scope boxes) follow their category, so turn off every category that
            // can be hidden except the CAD instance's own.
            var keep = _doc.GetElement(keepId);
            var keepCatId = keep?.Category?.Id;
            foreach (Category cat in _doc.Settings.Categories)
            {
                if (cat == null || (keepCatId != null && cat.Id == keepCatId)) continue;
                try
                {
                    if (view.CanCategoryBeHidden(cat.Id))
                        view.SetCategoryHidden(cat.Id, true);
                }
                catch { }
            }
        }

        // ── Linked, no host view ────────────────────────────────────────────

        /// <summary>
        /// Fallback for a CAD link with no resolvable host view (nothing to isolate in): copies the
        /// external source file directly, unchanged from V001's behaviour for that case.
        /// </summary>
        private string ExportLinkedCopy(DwgInstanceInfo info, string destPath)
        {
            if (string.IsNullOrEmpty(info.LinkedSourcePath))
                return "Source file path could not be resolved — the link may be unloaded or use a relative path that is not accessible from this machine.";

            if (!File.Exists(info.LinkedSourcePath))
                return $"Source file not found: {info.LinkedSourcePath}";

            // The SKIP check in Export() already ruled out "file exists and overwrite is off", so
            // it is always safe to overwrite here.
            File.Copy(info.LinkedSourcePath, destPath, overwrite: true);
            return null; // success
        }
    }
}
