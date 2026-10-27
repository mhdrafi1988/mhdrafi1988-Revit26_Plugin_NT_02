using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.RoofTag.V018.Helpers;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.RoofTag.V018.Core
{
    /// <summary>Places the tags for the surviving points inside one transaction.</summary>
    internal static class TagPlacementRunner
    {
        /// <summary>
        /// Places a spot elevation at each point. A point that cannot be tagged is logged and skipped;
        /// an unexpected exception disposes the transaction, which rolls every tag back.
        /// </summary>
        public static void Run(Document doc, RoofBase roof, IEnumerable<XYZ> points, RoofTagOptions options, Action<LogEntry> log)
        {
            using Transaction tx = new Transaction(doc, "Place Roof Tags");
            tx.Start();

            foreach (XYZ pt in points)
            {
                RoofTagGeometryHelper.GetTaggingReferenceOnRoof(roof, pt, out Reference faceRef, out XYZ projected);
                log(RoofTagPlacementService.PlaceTag(doc, faceRef, roof, projected ?? pt, options));
            }

            tx.Commit();
        }
    }
}
