using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.RoofPointElevationSync.V005
{
    /// <summary>
    /// Runs the actual Revit-side work (finding points / applying elevations)
    /// on the Revit API thread via ExternalEvent.
    /// </summary>
    public class RoofSyncEventHandler : IExternalEventHandler
    {
        public enum Mode { FindMatches, Apply }

        public Mode RequestedMode { get; set; }
        public RoofBase RoofA { get; set; }
        public RoofBase RoofB { get; set; }
        public double XyToleranceFeet { get; set; }
        public System.Collections.Generic.List<PointMatchRow> MatchRows { get; set; }

        public Action<System.Collections.Generic.List<PointMatchRow>> OnMatchesFound { get; set; }
        public Action<bool, string> OnApplyCompleted { get; set; }
        public Action<string, LogLevel> Log { get; set; }

        public void Execute(UIApplication app) =>
            ToolGuard.RunHandler(GetType(), app, ExecuteCore);

        private void ExecuteCore(UIApplication app)
        {
            // Esc / Cancel with a Yes-No confirm; a cancelled Apply is rolled back by the transaction below.
            using var cancel = ToolCancellation.BeginFor(ToolCatalog.RoofPointElevationSync, app);
            try
            {
                var service = new RoofPointMatchingService(Log);

                if (RequestedMode == Mode.FindMatches)
                {
                    var rows = service.FindMatches(RoofA, RoofB, XyToleranceFeet);
                    OnMatchesFound?.Invoke(rows);
                }
                else if (RequestedMode == Mode.Apply)
                {
                    Document doc = RoofA.Document;
                    var editorA = RoofA.GetSlabShapeEditor();
                    var editorB = RoofB.GetSlabShapeEditor();

                    using (var t = new Transaction(doc, "Roof Point Elevation Sync"))
                    {
                        t.Start();
                        try
                        {
                            service.ApplyMatches(editorA, editorB, MatchRows);
                            t.Commit();
                            int applied = MatchRows.Count(r => r.IsIncluded && !r.IsEqual);
                            OnApplyCompleted?.Invoke(true, $"{applied} point(s) updated.");
                        }
                        catch (OperationCanceledException)
                        {
                            t.RollBack(); // user cancelled: nothing stays changed
                            throw;
                        }
                        catch (Exception ex)
                        {
                            t.RollBack();
                            Log?.Invoke($"Apply failed, transaction rolled back: {ex.Message}", LogLevel.Error);
                            OnApplyCompleted?.Invoke(false, ex.Message);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Log?.Invoke("Cancelled by user. Nothing was changed.", LogLevel.Warning);
                if (RequestedMode == Mode.Apply)
                    OnApplyCompleted?.Invoke(false, "Cancelled by user.");
                else
                    OnMatchesFound?.Invoke(new System.Collections.Generic.List<PointMatchRow>());
                throw; // ToolGuard treats it as a user cancel
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Unexpected error: {ex.Message}", LogLevel.Error);
                if (RequestedMode == Mode.Apply)
                    OnApplyCompleted?.Invoke(false, ex.Message);
                else
                    OnMatchesFound?.Invoke(new System.Collections.Generic.List<PointMatchRow>());
            }
        }

        public string GetName() => "Roof Point Elevation Sync Event Handler";
    }
}
