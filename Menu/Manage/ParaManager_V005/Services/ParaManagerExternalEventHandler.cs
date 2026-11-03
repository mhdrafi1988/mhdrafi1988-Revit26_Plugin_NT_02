using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.ParaManager.V005.Models;
using System;
using System.Collections.Generic;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.ParaManager.V005.Services
{
    /// <summary>
    /// Single IExternalEventHandler/ExternalEvent pair for ParaManager (suite default:
    /// one handler per tool, since this tool has exactly one distinct Revit-side action —
    /// the bulk bind). Bridges the modeless WPF window to the Revit API thread.
    /// </summary>
    public class ParaManagerExternalEventHandler : IExternalEventHandler
    {
        /// <summary>Set by the ViewModel immediately before Raise(); read here on the API thread.</summary>
        public string SharedParameterFilePath { get; set; }
        public IReadOnlyList<ParameterAssignmentRow> RowsToAssign { get; set; }
        public BindingChoice Binding { get; set; }

        /// <summary>Raised on the API thread when the batch finishes (success or handled failure).</summary>
        public event Action<List<RowResult>, Exception> Completed;

        public void Execute(UIApplication app) =>
            ToolGuard.RunHandler(GetType(), app, ExecuteCore);

        private void ExecuteCore(UIApplication app)
        {
            List<RowResult> results = null;
            Exception failure = null;

            try
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null)
                    throw new InvalidOperationException("No active Revit document.");

                var service = new ParameterAssignmentService(doc, SharedParameterFilePath);
                results = service.RunBatch(RowsToAssign, Binding);
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            Completed?.Invoke(results, failure);
        }

        public string GetName() => "ParaManager — Bulk Parameter Assignment";
    }
}
