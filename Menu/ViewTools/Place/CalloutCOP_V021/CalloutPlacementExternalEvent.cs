using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.CalloutCOP.V021.Helpers;
using Revit26_Plugin_Revised.CalloutCOP.V021.Services;
using Revit26_Plugin_Revised.CalloutCOP.V021.ViewModels;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.CalloutCOP.V021.ExternalEvents
{
    public class CalloutPlacementExternalEvent : IExternalEventHandler
    {
        private const double LeftOffsetMm = -50.0;
        private const double CenterOffsetMm = 0.0;
        private const double RightOffsetMm = 50.0;

        private readonly Document _doc;
        private readonly ObservableCollection<ViewItemViewModel> _views;
        private readonly ObservableCollection<LogEntry> _logs;
        private readonly Func<double> _sizeProvider; // mm
        private readonly Action<int, int, int> _onFinished; // success, failed, skipped

        public CalloutPlacementExternalEvent(
            Document doc,
            ObservableCollection<ViewItemViewModel> views,
            ObservableCollection<LogEntry> logs,
            Func<double> sizeProvider,
            Action<int, int, int> onFinished)
        {
            _doc = doc;
            _views = views;
            _logs = logs;
            _sizeProvider = sizeProvider;
            _onFinished = onFinished;
        }

        public void Execute(UIApplication app) =>
            ToolGuard.RunHandler(GetType(), app, ExecuteCore);

        private void ExecuteCore(UIApplication app)
        {
            int success = 0;
            int failed = 0;
            int skipped = 0;

            var targets = _views.Where(v => v.IsSelected).ToList();
            if (!targets.Any())
            {
                _logs.Add(new LogEntry(LogLevel.Warning, "No target views selected."));
                _onFinished?.Invoke(0, 0, 0);
                return;
            }

            var sizeMm = _sizeProvider();

            // Esc / Cancel with a Yes-No confirm; the callout transaction is rolled back on cancel.
            using ToolCancellation cancel = ToolCancellation.BeginFor(ToolCatalog.CalloutCOP, app);

            using var tx = new Transaction(_doc, "Callout COP V018 - Reference Callouts");
            tx.Start();

            var options = tx.GetFailureHandlingOptions();
            options.SetFailuresPreprocessor(new CalloutFailuresPreprocessor());
            tx.SetFailureHandlingOptions(options);

            try
            {
            foreach (var vm in targets)
            {
                ToolCancellation.CheckCurrent();
                var placements = BuildEffectivePlacements(vm);

                if (!placements.Any())
                {
                    _logs.Add(new LogEntry(LogLevel.Warning,
                        $"{vm.Name}: skipped - no reference views assigned (Left/Center/Right all blank)."));
                    skipped++;
                    continue;
                }

                using var subTx = new SubTransaction(_doc);

                try
                {
                    subTx.Start();

                    foreach (var (referenceView, offsetMm, role) in placements)
                    {
                        ReferenceCalloutService.CreateReferenceCallout(
                            _doc,
                            vm.View,
                            referenceView.View,
                            sizeMm,
                            offsetMm);
                    }

                    subTx.Commit();

                    _logs.Add(new LogEntry(LogLevel.Success,
                        $"{vm.Name}: placed {placements.Count} reference callout(s) [{string.Join(", ", placements.Select(p => p.role))}]."));
                    success++;
                }
                catch (Exception ex)
                {
                    if (subTx.HasStarted())
                        subTx.RollBack();

                    _logs.Add(new LogEntry(LogLevel.Error, $"{vm.Name}: {ex.Message}"));
                    failed++;
                }
            }

            ToolCancellation.CheckCurrent(); // last chance to cancel before the commit
            }
            catch (OperationCanceledException)
            {
                if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                _logs.Add(new LogEntry(LogLevel.Warning, "Cancelled by user. No reference callouts were placed."));
                _onFinished?.Invoke(0, 0, 0);
                throw; // ToolGuard treats it as a user cancel
            }

            var status = tx.Commit();
            if (status != TransactionStatus.Committed)
            {
                _logs.Add(new LogEntry(LogLevel.Error,
                    $"Transaction did not commit (status: {status})."));
            }

            _logs.Add(new LogEntry(LogLevel.Info,
                $"Finished. Placed: {success}, Failed: {failed}, Skipped: {skipped}"));
            _onFinished?.Invoke(success, failed, skipped);
        }

        private static List<(DraftingViewItemViewModel referenceView, double offsetMm, string role)> BuildEffectivePlacements(
            ViewItemViewModel vm)
        {
            var list = new List<(DraftingViewItemViewModel, double, string)>();

            if (vm.LeftView != null)
                list.Add((vm.LeftView, LeftOffsetMm, "Left"));

            if (vm.CenterView != null)
                list.Add((vm.CenterView, CenterOffsetMm, "Center"));

            if (vm.RightView != null)
                list.Add((vm.RightView, RightOffsetMm, "Right"));

            return list;
        }

        public string GetName() => "Callout COP V018 - Placement External Event";
    }
}
