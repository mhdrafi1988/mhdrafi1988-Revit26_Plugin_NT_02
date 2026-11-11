using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.RoofPointComparison.V003.Core.Engine;
using Revit26_Plugin_Revised.RoofPointComparison.V003.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.RoofPointComparison.V003.Infrastructure.ExternalEvents
{
    public class RoofComparisonHandler : IExternalEventHandler
    {
        /// <summary>Set by the ViewModel immediately before raising the ExternalEvent.</summary>
        public static ComparisonPayload Payload;

        public void Execute(UIApplication app) =>
            ToolGuard.RunHandler(GetType(), app, ExecuteCore);

        private void ExecuteCore(UIApplication app)
        {
            if (Payload == null) return;
            ComparisonPayload current = Payload;

            Document doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;

            // Esc / Cancel with a Yes-No confirm; a cancelled marker placement is rolled back by its transaction.
            using var cancel = ToolCancellation.BeginFor(ToolCatalog.RoofPointComparison, app);
            try
            {
                RoofBase roofA = doc.GetElement(current.RoofAId) as RoofBase;
                RoofBase roofB = doc.GetElement(current.RoofBId) as RoofBase;

                if (roofA == null || roofB == null)
                {
                    current.Log?.Invoke(new LogEntry(LogLevel.Error, "One or both roofs could not be found. Aborting."));
                    current.OnRecalculated?.Invoke(new ComparisonResult { Success = false, ErrorMessage = "Roof not found." });
                    return;
                }

                var pointsA = RoofPointExtractor.Extract(roofA, current.Log);
                var pointsB = RoofPointExtractor.Extract(roofB, current.Log);

                var result = RoofComparisonEngine.Compare(
                    pointsA, pointsB, current.PositionToleranceMm, current.ElevationToleranceMm);

                if (current.Mode == ComparisonMode.PlaceMarkers)
                {
                    View activeView = app.ActiveUIDocument?.ActiveView;
                    (int missingPlaced, int mismatchPlaced) counts;

                    using (Transaction tx = new Transaction(doc, "Place Roof Comparison Markers"))
                    {
                        tx.Start();
                        counts = ComparisonMarkerService.PlaceMarkers(
                            doc, activeView, result.Entries,
                            current.MissingMarkerGroup, current.MismatchMarkerGroup, current.Log);
                        tx.Commit();
                    }

                    current.OnMarkersPlaced?.Invoke(counts.missingPlaced, counts.mismatchPlaced);
                }

                current.OnRecalculated?.Invoke(result);
            }
            catch (OperationCanceledException)
            {
                current.Log?.Invoke(new LogEntry(LogLevel.Warning, "Cancelled by user. Nothing was changed."));
                current.OnRecalculated?.Invoke(new ComparisonResult { Success = false, ErrorMessage = "Cancelled by user." });
                throw; // ToolGuard treats it as a user cancel
            }
            catch (Exception ex)
            {
                current.Log?.Invoke(new LogEntry(LogLevel.Error, $"[RoofComparisonHandler] Unhandled exception: {ex.Message}"));
                current.OnRecalculated?.Invoke(new ComparisonResult { Success = false, ErrorMessage = ex.Message });
            }
        }

        public string GetName() => "Roof Comparison Handler";
    }
}
