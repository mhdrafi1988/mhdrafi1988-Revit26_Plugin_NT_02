using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.EpsSteppedInsulationLayout.V001.Core;

namespace Revit26_Plugin_Revised.EpsSteppedInsulationLayout.V001.Revit
{
    /// <summary>Creates flat roof elements for each insulation cell and layer.</summary>
    public static class InsulationPlacer
    {
        /// <summary>
        /// Places all insulation panels inside a single transaction group.
        /// Returns the count of successfully created roofs.
        /// </summary>
        public static PlacementResult Place(PlacementRequest request)
        {
            Document doc = request.Context.Document;
            Level level = doc.GetElement(request.Context.LevelId) as Level;
            if (level == null) return PlacementResult.Failed("Level not found.");

            RoofType baseType = doc.GetElement(request.BaseRoofTypeId) as RoofType;
            RoofType stepType = doc.GetElement(request.StepRoofTypeId) as RoofType;
            if (baseType == null) return PlacementResult.Failed("100 mm roof type not found.");
            if (stepType == null) return PlacementResult.Failed("35 mm roof type not found.");

            double roofBaseOffsetFeet = request.Context.BaseOffsetFeet;
            double baseMm = request.Settings.BaseThicknessMm;
            double stepMm = request.Settings.StepThicknessMm;

            int created = 0;
            int failed = 0;

            using (var tg = new TransactionGroup(doc, "EPS Stepped Insulation"))
            {
                tg.Start();

                foreach (InsulationCell cell in request.Layout.Cells)
                {
                    if (cell.Boundary == null || cell.Boundary.Count < 3)
                        continue;

                    // Base layer (100 mm) — sits on top of the selected roof
                    double baseBottomFeet = request.Context.LevelElevationFeet + roofBaseOffsetFeet;
                    if (TryCreatePanel(doc, cell, level, baseType, baseBottomFeet, cell.Serial + "_B", out _))
                        created++;
                    else
                        failed++;

                    // Stepping layers (35 mm each)
                    for (int s = 1; s <= cell.StepLayerCount; s++)
                    {
                        double layerBottomMm = baseMm + (s - 1) * stepMm;
                        double layerBottomFeet = baseBottomFeet + MmToFeet(layerBottomMm);

                        string serial = $"{cell.Serial}_S{s:D2}";
                        if (TryCreatePanel(doc, cell, level, stepType, layerBottomFeet, serial, out _))
                            created++;
                        else
                            failed++;
                    }
                }

                if (created == 0)
                {
                    tg.RollBack();
                    return PlacementResult.Failed($"No panels created. {failed} failed.");
                }

                tg.Assimilate();
            }

            string msg = $"{created} panels created.";
            if (failed > 0) msg += $" {failed} failed.";
            return new PlacementResult { Success = true, RoofsCreated = created, Message = msg };
        }

        private static bool TryCreatePanel(
            Document doc,
            InsulationCell cell,
            Level level,
            RoofType roofType,
            double elevationFeet,
            string serial,
            out FootPrintRoof roof)
        {
            roof = null;
            try
            {
                using (var t = new Transaction(doc, $"Place {serial}"))
                {
                    t.Start();

                    CurveArray footprint = BuildFootprint(cell.Boundary);
                    roof = doc.Create.NewFootPrintRoof(footprint, level, roofType, out _);

                    if (roof == null)
                    {
                        t.RollBack();
                        return false;
                    }

                    // Set offset so the panel sits at the correct height
                    double offsetFromLevel = elevationFeet - level.ProjectElevation;
                    Parameter offsetParam = roof.get_Parameter(BuiltInParameter.ROOF_LEVEL_OFFSET_PARAM);
                    if (offsetParam != null && !offsetParam.IsReadOnly)
                        offsetParam.Set(offsetFromLevel);

                    // Disable slope on all edges (flat panel)
                    ModelCurveArrArray modelCurves = roof.GetProfiles();
                    if (modelCurves != null && modelCurves.Size > 0)
                    {
                        foreach (ModelCurve mc in modelCurves.get_Item(0))
                        {
                            roof.set_DefinesSlope(mc, false);
                        }
                    }

                    // Set EPS_Serial comment
                    Parameter commentsParam = roof.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                    if (commentsParam != null && !commentsParam.IsReadOnly)
                        commentsParam.Set(serial);

                    // Try setting Mark parameter too
                    Parameter markParam = roof.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
                    if (markParam != null && !markParam.IsReadOnly)
                        markParam.Set(serial);

                    t.Commit();
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static CurveArray BuildFootprint(IReadOnlyList<Vec2> boundary)
        {
            var curves = new CurveArray();
            for (int i = 0; i < boundary.Count; i++)
            {
                Vec2 a = boundary[i];
                Vec2 b = boundary[(i + 1) % boundary.Count];
                XYZ start = ToXYZ(a);
                XYZ end = ToXYZ(b);
                if (start.DistanceTo(end) < 0.001) continue; // skip degenerate
                curves.Append(Line.CreateBound(start, end));
            }
            return curves;
        }

        private static XYZ ToXYZ(Vec2 pt) =>
            new XYZ(MetresToFeet(pt.X), MetresToFeet(pt.Y), 0);

        private static double MetresToFeet(double metres) =>
            UnitUtils.ConvertToInternalUnits(metres, UnitTypeId.Meters);

        private static double MmToFeet(double mm) =>
            UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
    }
}
