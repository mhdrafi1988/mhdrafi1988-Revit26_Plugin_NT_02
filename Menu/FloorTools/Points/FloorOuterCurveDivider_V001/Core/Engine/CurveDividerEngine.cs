// =======================================================
// File: CurveDividerEngine.cs
// Location: Core/Engine/
// New in V004. Orchestration only — resolves the floor from FloorId and
// delegates to CurveDivisionService.ApplyDivisions. Called by
// CurveDividerHandler from inside its TransactionGroup.
// =======================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.FloorOuterCurveDivider.V001.Core.Services;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.FloorOuterCurveDivider.V001.Core.Models;

namespace Revit26_Plugin_Revised.FloorOuterCurveDivider.V001.Core.Engine
{
    public static class CurveDividerEngine
    {
        public static CurveDividerResult Execute(UIApplication app, CurveDividerPayload payload)
        {
            Document doc = app.ActiveUIDocument.Document;

            Floor floor = doc.GetElement(payload.FloorId) as Floor;
            if (floor == null)
            {
                const string msg = "Floor element not found (it may have been deleted).";
                payload.Log?.Invoke(new LogEntry(LogLevel.Error, msg));
                return new CurveDividerResult { Success = false, ErrorMessage = msg };
            }

            var service = new CurveDivisionService();
            var log = service.ApplyDivisions(doc, floor, payload.SelectedEdges);

            return new CurveDividerResult { Success = true, LogEntries = log };
        }
    }
}
