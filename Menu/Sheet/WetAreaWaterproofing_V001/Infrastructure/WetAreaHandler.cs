using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Shared.Services;
using System;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Infrastructure
{
    /// <summary>Runs one queued action from the modeless window inside Revit's API context.</summary>
    public sealed class WetAreaHandler : IExternalEventHandler
    {
        private Action<UIApplication> _pending;
        private Action _onCancelled;

        /// <summary>Queues the action to run the next time the event is raised.</summary>
        public void Queue(Action<UIApplication> action, Action onCancelled)
        {
            _pending = action;
            _onCancelled = onCancelled;
        }

        /// <summary>Revit entry point — guarded so no exception reaches Revit.</summary>
        public void Execute(UIApplication app)
        {
            Action onCancelled = _onCancelled;
            _onCancelled = null;
            ToolGuard.RunHandler(GetType(), app, ExecuteUnguarded, onCancelled);
        }

        private void ExecuteUnguarded(UIApplication app)
        {
            var action = _pending;
            _pending = null;
            if (action == null) return;

            // Esc / Cancel with a Yes-No confirm while the queued Revit work runs.
            using ToolCancellation cancel = ToolCancellation.BeginFor(ToolCatalog.WetAreaWaterproofing, app);
            action.Invoke(app);
        }

        /// <summary>Name shown by Revit for this handler.</summary>
        public string GetName() => "Wet Area Waterproofing V001";
    }
}
