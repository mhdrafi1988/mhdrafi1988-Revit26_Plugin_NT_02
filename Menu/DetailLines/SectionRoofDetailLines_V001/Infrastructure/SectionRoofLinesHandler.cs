using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Shared.Services;
using System;

namespace Revit26_Plugin_Revised.SectionRoofDetailLines.V001.Infrastructure
{
    /// <summary>
    /// Runs one queued action from the modeless window inside Revit's API context.
    /// <para>
    /// The window is modeless, so nothing it does on the WPF thread may touch the Revit API
    /// directly; each action — reading the model, or creating the lines — is queued here and run
    /// when Revit raises the event. <see cref="ToolGuard.RunHandler"/> keeps any exception out of
    /// Revit's idling loop, and <see cref="ToolCancellation"/> gives the run an Esc / Cancel that
    /// rolls it back.
    /// </para>
    /// </summary>
    public sealed class SectionRoofLinesHandler : IExternalEventHandler
    {
        private Action<UIApplication> _pending;

        /// <summary>Queues the action to run the next time the event is raised.</summary>
        public void Queue(Action<UIApplication> action) => _pending = action;

        /// <summary>Revit entry point — guarded so no exception reaches Revit.</summary>
        public void Execute(UIApplication app)
            => ToolGuard.RunHandler(GetType(), app, ExecuteUnguarded);

        private void ExecuteUnguarded(UIApplication app)
        {
            Action<UIApplication> action = _pending;
            _pending = null;
            if (action == null) return;

            using ToolCancellation cancel = ToolCancellation.BeginFor(ToolCatalog.SectionRoofDetailLines, app);
            action.Invoke(app);
        }

        /// <summary>Name Revit shows for this handler.</summary>
        public string GetName() => "Section Roof Detail Lines V001";
    }
}
