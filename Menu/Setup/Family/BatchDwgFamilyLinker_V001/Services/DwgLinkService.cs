using System;
using Autodesk.Revit.DB;
using BatchDwgFamilyLinker.Models;

namespace BatchDwgFamilyLinker.Services
{
    public static class DwgLinkService
    {
        public static void LinkDwg(
            Document doc,
            View view,
            string dwgPath,
            DwgPlacementMode mode)
        {
            var options = new DWGImportOptions
            {
                Unit = ImportUnit.Millimeter,
                Placement = mode == DwgPlacementMode.OriginToOrigin
                    ? ImportPlacement.Origin
                    : ImportPlacement.Centered,
                ColorMode = ImportColorMode.Preserved,
                VisibleLayersOnly = false
            };

            // Required output parameter
            ElementId importedElementId;

            bool success = doc.Import(
                dwgPath,
                options,
                view,
                out importedElementId
            );

            if (!success)
                throw new InvalidOperationException("DWG import failed.");
        }
    }
}
