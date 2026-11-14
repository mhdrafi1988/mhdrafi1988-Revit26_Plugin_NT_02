using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.WorksetMemory.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Revit26_Plugin_Revised.WorksetMemory.V001.Core.Services
{
    /// <summary>What the user asked the window to do once it closes.</summary>
    public sealed class ReloadRequest
    {
        /// <summary>Model to (re)open.</summary>
        public ModelIdentity Model { get; init; }

        /// <summary>Set whose open worksets to apply.</summary>
        public WorksetSet Set { get; init; }

        /// <summary>Make the set's active workset current again after opening.</summary>
        public bool RestoreActiveWorkset { get; init; }
    }

    /// <summary>Outcome of <see cref="WorksetReopenService.Reopen"/>, for the summary dialog.</summary>
    public sealed class ReopenResult
    {
        /// <summary>Worksets opened.</summary>
        public int Opened { get; set; }

        /// <summary>Saved worksets that are no longer in the model.</summary>
        public List<string> Missing { get; } = new();

        /// <summary>Non-fatal problems (active workset not restored, placeholder left open…).</summary>
        public List<string> Warnings { get; } = new();
    }

    /// <summary>
    /// Opens a model with exactly the worksets of a saved set open. Revit has no API to open or
    /// close worksets in a model that is already open, so an open model is closed first and then
    /// reopened with a <see cref="WorksetConfiguration"/>. Because the active document can't be
    /// closed by the API, a blank placeholder project is made active for the moment the model is
    /// closed, and closed again afterwards.
    /// Must run in a Revit API context with no modal dialog open and no transaction open.
    /// </summary>
    public sealed class WorksetReopenService
    {
        private readonly UIApplication _uiapp;

        /// <summary>Creates the service for <paramref name="uiapp"/>.</summary>
        public WorksetReopenService(UIApplication uiapp)
        {
            _uiapp = uiapp ?? throw new ArgumentNullException(nameof(uiapp));
        }

        /// <summary>The open document that is <paramref name="model"/>, or null.</summary>
        public static Document FindOpen(Autodesk.Revit.ApplicationServices.Application app, ModelIdentity model)
        {
            var docs = app.Documents.Cast<Document>().Where(d => !d.IsLinked && !d.IsFamilyDocument).ToList();
            return docs.FirstOrDefault(d => !string.IsNullOrEmpty(model.OpenPath)
                                            && string.Equals(d.PathName, model.OpenPath, StringComparison.OrdinalIgnoreCase))
                ?? docs.FirstOrDefault(model.Matches);
        }

        /// <summary>
        /// Closes the model if it is open, then opens it with the set's worksets open.
        /// Throws <see cref="InvalidOperationException"/> with a user-facing message when it can't.
        /// </summary>
        public ReopenResult Reopen(ReloadRequest request)
        {
            if (request?.Model == null || request.Set == null) throw new ArgumentNullException(nameof(request));
            var result = new ReopenResult();

            // A model open in this session is reopened from where it is open now, which may be a newer
            // local copy than the one saved with the set.
            Document openDoc = FindOpen(_uiapp.Application, request.Model);
            ModelIdentity target = (openDoc != null ? ModelIdentity.FromDocument(openDoc) : null) ?? request.Model;

            ModelPath path = target.ToModelPath()
                ?? throw new InvalidOperationException("No path is saved for this model, so it can't be reopened.");
            if (!target.IsCloud) CheckFileExists(target, path);

            if (openDoc != null && openDoc.IsModified)
                throw new InvalidOperationException(
                    $"'{openDoc.Title}' has unsaved changes. Save or Synchronize with Central first, then reload.");

            // Resolve ids before anything is closed, so a failure here leaves the session untouched.
            IList<WorksetPreview> previews = WorksharingUtils.GetUserWorksetInfo(path);
            var toOpen = new List<WorksetId>();
            foreach (SavedWorkset saved in request.Set.Worksets.Where(w => w.IsOpen))
            {
                WorksetPreview live = WorksetMemoryService.Match(previews, saved.Name, saved.Id, p => p.Name, p => p.Id.IntegerValue);
                if (live == null) result.Missing.Add(saved.Name);
                else toOpen.Add(live.Id);
            }

            var config = new WorksetConfiguration(WorksetConfigurationOption.CloseAllWorksets);
            if (toOpen.Count > 0) config.Open(toOpen);
            var options = new OpenOptions();
            options.SetOpenWorksetsConfiguration(config);

            Document placeholder = null;
            if (openDoc != null)
            {
                bool isActive = _uiapp.ActiveUIDocument?.Document?.Equals(openDoc) == true;
                if (isActive)
                {
                    try
                    {
                        placeholder = OpenPlaceholder();
                    }
                    catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                    {
                        throw new InvalidOperationException(
                            $"The placeholder project could not be opened ({ex.Message}), so the model was not closed. " +
                            "If another Revit session is reloading worksets, wait for it to finish and try again.", ex);
                    }
                }

                if (!openDoc.Close(false))
                    throw new InvalidOperationException(
                        "Revit did not close the model (another add-in may have stopped it). Nothing was reopened; " +
                        "switch back to the model's window and close the 'WorksetMemory_Placeholder' project.");
            }

            UIDocument reopened;
            try
            {
                reopened = _uiapp.OpenAndActivateDocument(path, options, false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidOperationException(
                    $"The model was closed but could not be reopened: {ex.Message}\nOpen it again from File › Open.", ex);
            }
            result.Opened = toOpen.Count;

            if (placeholder != null)
            {
                try { placeholder.Close(false); }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    result.Warnings.Add($"The placeholder project could not be closed ({ex.Message}); close it yourself.");
                }
            }

            if (request.RestoreActiveWorkset && !string.IsNullOrEmpty(request.Set.ActiveWorksetName))
                RestoreActive(reopened.Document, request.Set, result);

            return result;
        }

        // A file path that is gone can't be opened; say so before anything is closed. Revit Server
        // paths are not files, so they are left to Revit.
        private static void CheckFileExists(ModelIdentity model, ModelPath path)
        {
            if (path.ServerPath) return;
            string file = ModelPathUtils.ConvertModelPathToUserVisiblePath(path);
            if (File.Exists(file)) return;

            bool hasCentral = !string.IsNullOrWhiteSpace(model.CentralPath)
                              && !string.Equals(model.CentralPath, file, StringComparison.OrdinalIgnoreCase);
            if (hasCentral)
                throw new InvalidOperationException(
                    $"The local copy of '{model.Name}' saved with this set was not found:\n{file}\n\n" +
                    $"Its central model is {model.CentralPath}.\n" +
                    "Open the central with File › Open and 'Create New Local' ticked, then run WS Memory again: " +
                    "the new local copy is remembered and the saved sets still apply.");
            throw new InvalidOperationException($"The model file was not found:\n{file}");
        }

        private static void RestoreActive(Document doc, WorksetSet set, ReopenResult result)
        {
            var worksets = new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset).ToWorksets().ToList();
            Workset active = WorksetMemoryService.Match(worksets, set.ActiveWorksetName, set.ActiveWorksetId,
                w => w.Name, w => w.Id.IntegerValue);
            if (active == null)
            {
                result.Warnings.Add($"Active workset '{set.ActiveWorksetName}' is no longer in the model.");
                return;
            }
            if (!active.IsOpen)
            {
                result.Warnings.Add($"Active workset '{active.Name}' is closed, so it was not made current.");
                return;
            }

            try
            {
                doc.GetWorksetTable().SetActiveWorksetId(active.Id);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                result.Warnings.Add($"Could not make '{active.Name}' the active workset: {ex.Message}");
            }
        }

        // A blank project saved to the tool's folder and opened, so the model can stop being active.
        private Document OpenPlaceholder()
        {
            string file = JsonFileStore.PathFor(WorksetMemoryService.ToolFolder, "WorksetMemory_Placeholder.rvt");
            Document blank = _uiapp.Application.NewProjectDocument(UnitSystem.Metric);
            try
            {
                blank.SaveAs(file, new SaveAsOptions { OverwriteExistingFile = true, MaximumBackups = 1 });
            }
            finally
            {
                blank.Close(false);
            }
            return _uiapp.OpenAndActivateDocument(file).Document;
        }
    }
}
