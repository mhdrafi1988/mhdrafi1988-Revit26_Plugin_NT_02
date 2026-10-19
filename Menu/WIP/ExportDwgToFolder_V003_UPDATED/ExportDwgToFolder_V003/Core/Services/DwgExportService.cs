using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using Revit26_Plugin.ExportDwgToFolder.V003.Core.Models;

namespace Revit26_Plugin.ExportDwgToFolder.V003.Core.Services
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
    /// </summary>
    public class DwgExportService
    {
        private readonly Document _doc;

        public DwgExportService(Document doc)
        {
            _doc = doc;
        }

        /// <summary>
        /// Exports one DWG to <paramref name="destPath"/>.
        /// Returns null on success, or an error message on failure.
        /// </summary>
        public string Export(DwgInstanceInfo info, string destPath, bool overwrite)
        {
            if (!overwrite && File.Exists(destPath))
                return "SKIP";

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destPath));

                bool hasHostView = info.OwnerViewId != null && info.OwnerViewId != ElementId.InvalidElementId;
                if (hasHostView)
                    return ExportIsolated(info, destPath);

                if (info.SourceType == DwgSourceType.Link)
                    return ExportLinkedCopy(info, destPath);

                return "No host view found for this imported DWG — it may be a model-space import not visible in any scanned view.";
            }
            catch (Exception ex)
            {
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
        private string ExportIsolated(DwgInstanceInfo info, string destPath)
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
                        // Hide everything in the view except this one CAD link/import, so the
                        // exported DWG contains only that instance.
                        try
                        {
                            view.IsolateElementTemporary(info.ElementId);
                        }
                        catch (Exception isolateEx)
                        {
                            // A locked view template, a view in another temporary mode, or a view
                            // type that doesn't support temporary isolate all land here — give a
                            // reason instead of a bare Revit API message.
                            throw new InvalidOperationException(
                                $"Could not isolate this element in \"{view.Name}\" — check the view's " +
                                $"template and V/G overrides. ({isolateEx.Message})", isolateEx);
                        }

                        var opts = new DWGExportOptions
                        {
                            FileVersion = ACADVersion.R2010,
                            HideScopeBox = true,
                            HideUnreferenceViewTags = true,
                            HideReferencePlane = true,
                            ExportingAreas = false
                        };

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
                    return info.SourceType == DwgSourceType.Link
                        ? ExportLinkedCopy(info, destPath)
                        : "Revit Export produced no DWG file.";
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
