using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Shared.Services;
using System;

namespace Revit26_Plugin_Revised.SheetAlignDistribute.V001.Infrastructure
{
    /// <summary>
    /// Runs one queued action from the modeless window inside Revit's API context.
    /// </summary>
    public sealed class SheetAlignDistributeHandler : IExternalEventHandler
    {
        private Action<UIApplication> _pending;

        /// <summary>Queues the action to run the next time the event is raised.</summary>
        public void Queue(Action<UIApplication> action) => _pending = action;

        /// <summary>Revit entry point — guarded so no exception reaches Revit.</summary>
        public void Execute(UIApplication app)
            => ToolGuard.RunHandler(GetType(), app, ExecuteUnguarded);

        private void ExecuteUnguarded(UIApplication app)
        {
            var action = _pending;
            _pending = null;
            if (action == null) return;

            // Esc / Cancel with a Yes-No confirm while the queued Revit work runs; the applier's
            // transaction rolls back on cancel, so the sheet is never left half-aligned.
            using ToolCancellation cancel = ToolCancellation.BeginFor(ToolCatalog.SheetAlignDistribute, app);
            action.Invoke(app);
        }

        /// <summary>Name shown by Revit for this handler.</summary>
        public string GetName() => "Sheet Align & Distribute V001";
    }
}
