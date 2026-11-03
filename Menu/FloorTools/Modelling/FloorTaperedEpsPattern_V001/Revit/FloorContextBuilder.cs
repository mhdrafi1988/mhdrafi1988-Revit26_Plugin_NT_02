using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Revit
{
    /// <summary>Builds a <see cref="FloorContext"/>; checks 3 (floor) and 4 (visible in view).</summary>
    public static class FloorContextBuilder
    {
        /// <summary>Shown when the floor's sketch can't be read (the roof tool refused extrusion roofs here).</summary>
        public const string FloorMessage =
            "The selected floor's sketch could not be read. This tool needs a sketch-based floor.";

        public const string NotVisibleMessage =
            "The selected floor is not visible in the active view. Make it visible (view range, visibility/graphics, crop) and run the tool again.";

        /// <summary>Returns false with a message when the floor is not usable at all (tool should exit).</summary>
        public static bool TryCheckFloor(Floor floor, ViewPlan view, out string message)
        {
            // Floor port: the roof tool refused anything but a FootPrintRoof here. Every Floor is
            // sketch-based, so the check is that its sketch can actually be read.
            if (floor == null || FloorSketchProfile.GetSketch(floor) == null)
            {
                message = FloorMessage;
                return false;
            }

            if (!IsVisibleInView(floor, view))
            {
                message = NotVisibleMessage;
                return false;
            }

            message = string.Empty;
            return true;
        }

        public static bool IsVisibleInView(Element element, View view) =>
            new FilteredElementCollector(element.Document, view.Id)
                .OfCategory(BuiltInCategory.OST_Floors)
                .WhereElementIsNotElementType()
                .ToElementIds()
                .Contains(element.Id);

        /// <summary>Reads the floor and view. Geometry problems end up in <see cref="FloorContext.Outline"/>.</summary>
        public static FloorContext Build(Document doc, ElementId viewId, ElementId floorId)
        {
            var view = doc.GetElement(viewId) as ViewPlan;
            var floor = doc.GetElement(floorId) as Floor;

            if (view == null || !view.IsValidObject || floor == null || !floor.IsValidObject)
            {
                return new FloorContext(doc, viewId, floorId)
                {
                    ViewName = view?.Name ?? "(view deleted)",
                    FloorName = floor == null ? "(floor deleted)" : Describe(floor),
                    Outline = Invalid("The floor or the view no longer exists. Close the tool and run it again.")
                };
            }

            Level? level = view.GenLevel;
            OutlineValidationResult outline = FloorFootprintReader.Read(floor, out int edges);
            if (outline.IsValid && !IsVisibleInView(floor, view))
            {
                outline = Invalid(NotVisibleMessage);
            }

            return new FloorContext(doc, viewId, floorId)
            {
                ViewName = view.Name,
                FloorName = Describe(floor),
                LevelName = level?.Name ?? "(no level)",
                LevelElevationFeet = level?.ProjectElevation ?? 0.0,
                ShortCurveToleranceFeet = doc.Application.ShortCurveTolerance,
                MinSegmentLengthMetres = System.Math.Max(
                    GeometryTolerance.MinSegmentLength,
                    UnitConverter.FeetToMetres(doc.Application.ShortCurveTolerance) * 1.01),
                SketchEdgeCount = edges,
                Outline = outline
            };
        }

        private static string Describe(Floor floor)
        {
            string type = floor.Document.GetElement(floor.GetTypeId())?.Name ?? "Floor";
            return $"{type}  [Id {floor.Id.Value}]";
        }

        private static OutlineValidationResult Invalid(string message) =>
            new OutlineValidationResult { Errors = new[] { message }, ShapeName = "Invalid" };
    }
}
