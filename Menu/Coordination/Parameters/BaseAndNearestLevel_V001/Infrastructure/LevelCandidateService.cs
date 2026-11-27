using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.BaseAndNearestLevel.V001.Core;

namespace Revit26_Plugin_Revised.BaseAndNearestLevel.V001.Infrastructure
{
    /// <summary>Lists the host model's levels as nearest-level candidates. Runs on the Revit API thread.</summary>
    public static class LevelCandidateService
    {
        /// <summary>
        /// Every level of <paramref name="doc"/>, lowest first. When <paramref name="buildingStoreysOnly"/> is
        /// true, levels whose "Building Story" parameter is not ticked are left out and their names are added to
        /// <paramref name="excluded"/>, so reference and T.O.S. levels do not win the search.
        /// A filter that would leave no candidate at all is ignored, and every level is returned instead.
        /// </summary>
        public static List<LevelCandidate> List(Document doc, bool buildingStoreysOnly, out List<string> excluded)
        {
            excluded = new List<string>();
            var all = new List<LevelCandidate>();

            foreach (Level level in new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>())
            {
                all.Add(new LevelCandidate
                {
                    Id = level.Id.Value,
                    Name = level.Name,
                    ElevationMm = UnitsMm.FromFeet(level.Elevation),
                    IsBuildingStorey = IsBuildingStorey(level)
                });
            }

            all = all.OrderBy(c => c.ElevationMm).ThenBy(c => c.Name).ToList();
            if (!buildingStoreysOnly) return all;

            var storeys = all.Where(c => c.IsBuildingStorey).ToList();
            if (storeys.Count == 0) return all;   // nothing is marked as a storey; the filter would block the run

            excluded = all.Where(c => !c.IsBuildingStorey).Select(c => c.Name).ToList();
            return storeys;
        }

        /// <summary>True when the level's "Building Story" parameter is ticked. A level without that parameter counts as one.</summary>
        private static bool IsBuildingStorey(Level level)
        {
            Parameter p = level.get_Parameter(BuiltInParameter.LEVEL_IS_BUILDING_STORY);
            return p == null || p.StorageType != StorageType.Integer || p.AsInteger() != 0;
        }
    }
}
