using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.CreateSections.V012.ViewModels;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.CreateSections.V012.Services
{
    /// <summary>
    /// Revit ExternalEvent handler responsible for executing section
    /// creation logic on the Revit API thread while the dialog stays
    /// open (modeless).
    ///
    /// V008: replaces V07's dead/broken SectionFromLineExternalEvent.
    /// - ExternalEvent is created ONCE in the constructor and cached,
    ///   not recreated on every Raise() call (V07 called
    ///   ExternalEvent.Create(this).Raise() inline, leaking a new
    ///   ExternalEvent instance per click).
    /// - Actually wired up and used: the V07 command called the
    ///   orchestrator directly off the VM's CreateRequested event on
    ///   the UI thread (a disguised modal flow); V008's command instead
    ///   subscribes CreateRequested to Raise() here, and the dialog is
    ///   shown with Show() (modeless), not ShowDialog().
    /// </summary>
    public class SectionCreationExternalEvent : IExternalEventHandler
    {
        private readonly UIDocument _uiDoc;
        private readonly ViewPlan _planView;
        private readonly SectionFromLineViewModel _viewModel;
        private readonly ExternalEvent _externalEvent;

        private IList<Reference> _references;

        public SectionCreationExternalEvent(
            UIDocument uiDoc,
            ViewPlan planView,
            SectionFromLineViewModel viewModel)
        {
            _uiDoc = uiDoc ?? throw new ArgumentNullException(nameof(uiDoc));
            _planView = planView ?? throw new ArgumentNullException(nameof(planView));
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

            // Created ONCE here, not per-call — this is the fix for the
            // V07 bug where a new ExternalEvent was instantiated on every Raise().
            _externalEvent = ExternalEvent.Create(this);
        }

        /// <summary>Call this to request execution. Safe to call repeatedly.</summary>
        public void Raise(IList<Reference> references)
        {
            _references = references;
            _externalEvent.Raise();
        }

        public void Execute(UIApplication app) =>
            ToolGuard.RunHandler(GetType(), app, ExecuteCore);

        private void ExecuteCore(UIApplication app)
        {
            Document doc = _uiDoc.Document;

            if (_references == null || _references.Count == 0)
                return;

            try
            {
                var orchestrator = new SectionFromLineOrchestrator(
                    doc,
                    _planView,
                    _viewModel);

                orchestrator.Start(_references);
            }
            catch (Exception ex)
            {
                _viewModel.IsRunning = false;
                TaskDialog.Show("Create Sections From Detail Lines", ex.Message);
            }
        }

        public string GetName()
        {
            return "Create Sections From Detail Lines — External Event";
        }
    }
}
