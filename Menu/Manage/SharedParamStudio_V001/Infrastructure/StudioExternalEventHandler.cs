using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.SharedParamStudio.V001.Infrastructure
{
    /// <summary>
    /// Runs one queued job on the Revit API thread for the modeless window. The window sets
    /// <see cref="Job"/> and <see cref="Completed"/>, then raises the external event.
    /// </summary>
    public sealed class StudioExternalEventHandler : IExternalEventHandler
    {
        /// <summary>Work to run against the active document.</summary>
        public Action<Document> Job { get; set; }

        /// <summary>Called afterwards on the API thread; the argument is the failure, or null.</summary>
        public Action<Exception> Completed { get; set; }

        /// <inheritdoc/>
        public void Execute(UIApplication app)
            => ToolGuard.RunHandler(GetType(), app, a =>
            {
                Exception failure = null;
                try
                {
                    Document doc = a.ActiveUIDocument?.Document
                        ?? throw new InvalidOperationException("No active Revit document.");
                    Job?.Invoke(doc);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
                Completed?.Invoke(failure);
            });

        /// <inheritdoc/>
        public string GetName() => ToolCatalog.SharedParamStudio.Name;
    }
}
