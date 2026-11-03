using System;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.TaperedEpsPattern2D.V006.Revit;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.TaperedEpsPattern2D.V006.Events
{
    /// <summary>
    /// Runs <see cref="DetailPatternWriter"/> in Revit's API context. Created once per command
    /// invocation (instance state only); the window sets <see cref="Pending"/> and raises the event.
    /// </summary>
    public sealed class RunPatternHandler : IExternalEventHandler
    {
        public PatternWriteRequest? Pending { get; set; }

        /// <summary>Raised on Revit's UI thread after every attempt.</summary>
        public event Action<PatternWriteResult>? Completed;

        public void Execute(UIApplication app) =>
            ToolGuard.RunHandler(GetType(), app, ExecuteCore);

        private void ExecuteCore(UIApplication app)
        {
            PatternWriteRequest? request = Pending;
            Pending = null;

            PatternWriteResult result;
            try
            {
                result = request == null
                    ? PatternWriteResult.Failed("Nothing to run.")
                    : DetailPatternWriter.Write(request);
            }
            catch (Exception ex)
            {
                result = PatternWriteResult.Failed("Unexpected error: " + ex.Message);
            }

            Completed?.Invoke(result);
        }

        public string GetName() => "Tapered EPS 2D Pattern – Run";
    }
}
