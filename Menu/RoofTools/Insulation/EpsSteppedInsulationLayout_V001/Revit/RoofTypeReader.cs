using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.EpsSteppedInsulationLayout.V001.Revit
{
    /// <summary>Reads available RoofType elements and their compound thicknesses.</summary>
    public static class RoofTypeReader
    {
        /// <summary>Returns all roof types in the document with their total thickness in mm.</summary>
        public static IReadOnlyList<RoofTypeOption> GetAll(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(RoofType))
                .Cast<RoofType>()
                .Select(rt => new RoofTypeOption(rt.Id, rt.Name, GetThicknessMm(rt)))
                .OrderBy(o => o.Name)
                .ToList();
        }

        private static double GetThicknessMm(RoofType roofType)
        {
            CompoundStructure cs = roofType.GetCompoundStructure();
            if (cs == null) return 0;
            double totalFeet = Enumerable.Range(0, cs.LayerCount)
                .Sum(i => cs.GetLayerWidth(i));
            return UnitUtils.ConvertFromInternalUnits(totalFeet, UnitTypeId.Millimeters);
        }
    }
}
