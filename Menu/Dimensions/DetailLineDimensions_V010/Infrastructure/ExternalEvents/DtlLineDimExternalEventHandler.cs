using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using Revit26_Plugin_Revised.DtlLineDim.V010.Core.Engine;
using Revit26_Plugin_Revised.DtlLineDim.V010.Core.Services;
using Revit26_Plugin_Revised.DtlLineDim.V010.UI.ViewModels;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.DtlLineDim.V010.Infrastructure.ExternalEvents
{
    /// <summary>
    /// Single handler for all Revit API access required by DtlLineDim.
    /// The requested action is set on the ViewModel before ExternalEvent.Raise()
    /// is called; this handler reads it, performs the Revit-side work, and
    /// updates the ViewModel's ObservableCollections directly (safe: Revit's
    /// external event callback runs on the same UI thread as the WPF window).
    /// </summary>
    public class DtlLineDimExternalEventHandler : IExternalEventHandler
    {
        private readonly DtlLineDimViewModel _vm;

        public DtlLineDimRequest PendingRequest { get; set; } = DtlLineDimRequest.None;

        public DtlLineDimExternalEventHandler(DtlLineDimViewModel vm)
        {
            _vm = vm;
        }

        public void Execute(UIApplication app) =>
            ToolGuard.RunHandler(GetType(), app, ExecuteCore);

        private void ExecuteCore(UIApplication app)
        {
            Document doc = app.ActiveUIDocument?.Document;
            View view = doc?.ActiveView;

            try
            {
                if (doc == null)
                {
                    _vm.LogError("No active document — request aborted.");
                    return;
                }

                // Esc / Cancel with a Yes-No confirm while the dimensions are created.
                using ToolCancellation cancel = PendingRequest == DtlLineDimRequest.GenerateDimensions
                    ? ToolCancellation.BeginFor(ToolCatalog.DtlLineDim, app)
                    : null;
                switch (PendingRequest)
                {
                    case DtlLineDimRequest.Initialize:
                        RunInitialize(doc, view);
                        break;

                    case DtlLineDimRequest.GenerateDimensions:
                        RunGenerate(doc, view);
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                // The dimension transaction was rolled back as the exception unwound.
                _vm.LogError("Cancelled by user. No dimensions were created.");
                throw; // ToolGuard treats it as a user cancel; finally resets the busy state
            }
            catch (Exception ex)
            {
                _vm.LogError($"Unhandled error: {ex.Message}");
            }
            finally
            {
                PendingRequest = DtlLineDimRequest.None;
                _vm.IsBusy = false;
            }
        }

        private void RunInitialize(Document doc, View view)
        {
            bool valid = ViewValidationService.ValidatePlanView(view, out string reason);
            _vm.ApplyViewValidation(valid, reason);

            _vm.Log(valid ? Shared.Models.LogLevel.Info : Shared.Models.LogLevel.Warning, reason);

            DetailItemCollectorService.PopulateDetailItemTypes(doc, view, _vm.DetailItemTypes, _vm.LogEntries);
            DimensionTypeService.PopulateAlignedDimensionTypes(doc, _vm.DimensionTypes, _vm.LogEntries);

            _vm.RestoreLastSelections();
        }

        private void RunGenerate(Document doc, View view)
        {
            // Re-validate in case the active view changed since window open.
            bool valid = ViewValidationService.ValidatePlanView(view, out string reason);
            _vm.ApplyViewValidation(valid, reason);

            if (!valid)
            {
                _vm.Log(Shared.Models.LogLevel.Error, $"Run aborted: {reason}");
                _vm.SetRunSummary("Aborted — invalid view.");
                return;
            }

            var result = DimensionCreationService.CreateDimensions(
                doc, view,
                _vm.SelectedDetailItemType,
                _vm.SelectedDimensionType,
                _vm.EffectiveTickSizeMm,
                _vm.EffectiveOffsetMm,
                _vm.LogEntries);

            _vm.ApplyRunSummary(result.Created, result.Failed);
        }

        public string GetName() => "DtlLineDim External Event Handler";
    }
}
