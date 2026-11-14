// =======================================================
// File: FloorSketchProfile.cs
// Namespace: Revit26_Plugin_Revised.Shared.Services
// Reads a floor's sketch loops. FootPrintRoof has GetProfiles(); Floor has
// no such method, so the floor tools ported from roof tools (plugin 6.34.0)
// read the floor's Sketch instead: floor.SketchId, falling back to the
// first dependent Sketch. Read-only, no transaction.
// =======================================================

using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.Shared.Services
{
    /// <summary>Reads the closed loops of a floor's sketch (outer boundaries and openings).</summary>
    public static class FloorSketchProfile
    {
        /// <summary>
        /// The floor's sketch, or null when it cannot be read: <c>floor.SketchId</c> first, then the
        /// first dependent <see cref="Sketch"/>. Never throws.
        /// </summary>
        /// <param name="floor">The floor to read.</param>
        public static Sketch GetSketch(Floor floor)
        {
            if (floor == null || !floor.IsValidObject) return null;
            Document doc = floor.Document;
            try
            {
                if (floor.SketchId != null && floor.SketchId != ElementId.InvalidElementId &&
                    doc.GetElement(floor.SketchId) is Sketch own)
                    return own;

                foreach (ElementId id in floor.GetDependentElements(new ElementClassFilter(typeof(Sketch))))
                    if (doc.GetElement(id) is Sketch dependent)
                        return dependent;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // Unreadable sketch: callers treat null as "no loops".
            }
            return null;
        }

        /// <summary>
        /// Each sketch loop as a list of curves, in sketch order (the same shape as
        /// <c>FootPrintRoof.GetProfiles()</c> gives after taking each ModelCurve's GeometryCurve).
        /// Empty when the sketch cannot be read.
        /// </summary>
        /// <param name="floor">The floor to read.</param>
        public static List<List<Curve>> GetLoops(Floor floor)
        {
            var loops = new List<List<Curve>>();
            CurveArrArray profile = GetSketch(floor)?.Profile;
            if (profile == null) return loops;

            foreach (CurveArray loop in profile)
            {
                var curves = new List<Curve>();
                foreach (Curve c in loop)
                    if (c != null) curves.Add(c);
                if (curves.Count > 0) loops.Add(curves);
            }
            return loops;
        }
    }
}
