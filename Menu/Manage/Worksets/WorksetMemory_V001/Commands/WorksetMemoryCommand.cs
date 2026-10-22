using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.WorksetMemory.V001.Core.Services;
using Revit26_Plugin_Revised.WorksetMemory.V001.UI.ViewModels;
using Revit26_Plugin_Revised.WorksetMemory.V001.UI.Views;
using System;
using System.Linq;
using System.Windows.Interop;

namespace Revit26_Plugin_Revised.WorksetMemory.V001.Commands
{
    /// <summary>
    /// Opens the Workset Memory window. When the user clicks Reload, the window closes and this
    /// command reopens the chosen model with the saved set's worksets open (the reopen must happen
    /// here, in the command's API context, with no modal window open).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class WorksetMemoryCommand : IExternalCommand
    {
        /// <inheritdoc/>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteUnguarded);

        private Result ExecuteUnguarded(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            Document doc = uiapp.ActiveUIDocument?.Document;
            if (doc != null && doc.IsFamilyDocument) doc = null;

            var viewModel = new WorksetMemoryViewModel(doc);
            var window = new WorksetMemoryWindow(viewModel);
            new WindowInteropHelper(window).Owner = uiapp.MainWindowHandle;
            window.ShowDialog();

            ReloadRequest request = viewModel.ReloadRequest;
            if (request == null) return Result.Cancelled;

            ReopenResult result;
            try
            {
                result = new WorksetReopenService(uiapp).Reopen(request);
            }
            catch (InvalidOperationException ex)
            {
                TaskDialog.Show(ToolCatalog.WorksetMemory.Title, ex.Message);
                return Result.Cancelled;
            }

            var lines = new System.Collections.Generic.List<string>
            {
                $"'{request.Model.Name}' is open with the {result.Opened} workset(s) of '{request.Set.Name}' open."
            };
            if (result.Missing.Count > 0)
                lines.Add($"Not in the model any more, skipped: {string.Join(", ", result.Missing.Take(10))}" +
                          (result.Missing.Count > 10 ? $" and {result.Missing.Count - 10} more" : ""));
            lines.AddRange(result.Warnings);

            TaskDialog.Show(ToolCatalog.WorksetMemory.Title, string.Join("\n\n", lines));
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Keeps the Workset Memory button enabled with no model open, so a remembered model can be
    /// opened straight with its saved worksets.
    /// </summary>
    public class WorksetMemoryAvailability : IExternalCommandAvailability
    {
        /// <inheritdoc/>
        public bool IsCommandAvailable(UIApplication app, CategorySet selectedCategories) => true;
    }
}
