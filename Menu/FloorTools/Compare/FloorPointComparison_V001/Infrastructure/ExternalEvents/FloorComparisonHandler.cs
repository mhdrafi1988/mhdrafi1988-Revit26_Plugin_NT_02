using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.FloorPointComparison.V001.Core.Engine;
using Revit26_Plugin_Revised.FloorPointComparison.V001.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.FloorPointComparison.V001.Infrastructure.ExternalEvents
{
    public class FloorComparisonHandler : IExternalEventHandler
    {
        /// <summary>Set by the ViewModel immediately before raising the ExternalEvent.</summary>
        public static ComparisonPayload Payload;

        /// <summary>Revit entry point; delegates to ToolGuard.RunHandler.</summary>

        public void Execute(UIApplication app) =>

            ToolGuard.RunHandler(GetType(), app, ExecuteCore);


        private void ExecuteCore(UIApplication app)
        {
            if (Payload == null) return;
            ComparisonPayload current = Payload;

            Document doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;

            // Esc / Cancel with a Yes-No confirm; a cancelled marker placement is rolled back by its transaction.
            using var cancel = ToolCancellation.BeginFor(ToolCatalog.FloorPointComparison, app);
            try
            {
                Floor floorA = doc.GetElement(current.FloorAId) as Floor;
                Floor floorB = doc.GetElement(current.FloorBId) as Floor;

                if (floorA == null || floorB == null)
                {
                    current.Log?.Invoke(new LogEntry(LogLevel.Error, "One or both floors could not be found. Aborting."));
                    current.OnRecalculated?.Invoke(new ComparisonResult { Success = false, ErrorMessage = "Floor not found." });
                    return;
                }

                var pointsA = FloorPointExtractor.Extract(floorA, current.Log);
                var pointsB = FloorPointExtractor.Extract(floorB, current.Log);

                var result = FloorComparisonEngine.Compare(
                    pointsA, pointsB, current.PositionToleranceMm, current.ElevationToleranceMm);

                if (current.Mode == ComparisonMode.PlaceMarkers)
                {
                    View activeView = app.ActiveUIDocument?.ActiveView;
                    (int missingPlaced, int mismatchPlaced) counts;

                    using (Transaction tx = new Transaction(doc, "Place Floor Comparison Markers"))
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
                current.Log?.Invoke(new LogEntry(LogLevel.Error, $"[FloorComparisonHandler] Unhandled exception: {ex.Message}"));
                current.OnRecalculated?.Invoke(new ComparisonResult { Success = false, ErrorMessage = ex.Message });
            }
        }

        public string GetName() => "Floor Comparison Handler";
    }
}
