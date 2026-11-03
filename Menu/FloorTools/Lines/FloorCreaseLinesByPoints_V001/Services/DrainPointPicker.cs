using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace Revit26_Plugin_Revised.FloorCreaseLinesByPoints.V001.Services
{
    /// <summary>
    /// Utility to pick drain points on a floor, snapped to the nearest known vertex.
    /// </summary>
    public static class DrainPointPicker
    {
        /// <summary>
        /// Lets the user pick points on the floor surface and returns the nearest known vertex
        /// for each pick.
        /// <para>
        /// If the floor already has Shape Editing enabled (e.g. from a prior AutoSlopeByPoint
        /// run), its existing vertices are read directly — no document change is made.
        /// </para>
        /// <para>
        /// If Shape Editing has never been enabled, there are no vertex dots for Revit to
        /// draw, and nothing to snap to. To give the user the same visual vertex markers as
        /// before, Shape Editing is enabled temporarily (TX-A, committed) so Revit renders
        /// the corner dots during the pick prompt. There is no Disable() in the Revit API —
        /// the equivalent "undo" (TX-B) is achieved by rolling back the enclosing
        /// TransactionGroup once the pick is done, which reverts the enable and removes the
        /// auto-created vertices, leaving the floor exactly as it was found.
        /// </para>
        /// </summary>
        /// <param name="uiDoc">Active UIDocument</param>
        /// <param name="floor">The floor on which drains are placed</param>
        /// <returns>List of XYZ positions (Z=0) of the selected drain vertices.</returns>
        /// <exception cref="InvalidOperationException">If shape editing cannot be enabled or no vertices exist.</exception>
        /// <exception cref="Autodesk.Revit.Exceptions.OperationCanceledException">If user cancels picking.</exception>
        public static List<XYZ> PickDrainPoints(UIDocument uiDoc, Floor floor)
        {
            Document doc = uiDoc.Document;
            SlabShapeEditor editor = floor.GetSlabShapeEditor();
            if (editor == null)
                throw new InvalidOperationException("This floor does not support shape editing (GetSlabShapeEditor() returned null).");

            bool alreadyEnabled = editor.IsEnabled;
            List<XYZ> result;

            using (TransactionGroup tg = new TransactionGroup(doc, "Temporary Shape Editing for Drain Selection"))
            {
                tg.Start();

                if (!alreadyEnabled)
                {
                    // TX-A: Enable shape editing so Revit draws the vertex dots
                    // while the user picks. Committed (not left open) so the
                    // pick prompt below isn't run inside an active transaction.
                    using (Transaction tx = new Transaction(doc, "TX-A | Enable Floor Shape Editing (temporary)"))
                    {
                        tx.Start();
                        editor.Enable();
                        tx.Commit();
                    }
                }

                // Let user pick points on the floor
                IList<Reference> pickedRefs;
                try
                {
                    pickedRefs = uiDoc.Selection.PickObjects(
                        ObjectType.PointOnElement,
                        "Pick drain points on the floor surface (shape editing vertices). Press ESC to cancel.");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    tg.RollBack();
                    throw; // rethrow so the command can handle cancellation
                }

                // Validate that all picked points belong to the selected floor
                var drainPoints = new List<XYZ>();
                foreach (var refPick in pickedRefs)
                {
                    if (refPick.ElementId != floor.Id)
                    {
                        tg.RollBack();
                        throw new InvalidOperationException("One or more points were not picked on the selected floor.");
                    }
                    drainPoints.Add(refPick.GlobalPoint);
                }

                if (drainPoints.Count == 0)
                {
                    tg.RollBack();
                    throw new InvalidOperationException("No drain points were picked.");
                }

                // Re-fetch in case TX-A just enabled it, then retrieve all existing vertices
                editor = floor.GetSlabShapeEditor();
                var vertices = (editor != null && editor.IsEnabled)
                    ? editor.SlabShapeVertices
                        .Cast<SlabShapeVertex>()
                        .Select(v => v.Position)
                        .Where(p => p != null)
                        .ToList()
                    : new List<XYZ>();

                if (vertices.Count == 0)
                {
                    tg.RollBack();
                    throw new InvalidOperationException("The floor has no shape editing vertices. Cannot pick drains.");
                }

                // Snap each clicked point to the nearest existing vertex
                var finalDrainLocations = new List<XYZ>();
                foreach (var clickedPt in drainPoints)
                {
                    XYZ nearest = FindNearestVertex(clickedPt, vertices);
                    finalDrainLocations.Add(new XYZ(nearest.X, nearest.Y, 0));
                }

                result = finalDrainLocations;

                // TX-B (no real "disable" exists): rolling back the group reverts
                // TX-A if it ran (removing the auto-created vertices and dots), or
                // is a no-op if the floor already had Shape Editing enabled.
                tg.RollBack();
            }

            return result;
        }

        private static XYZ FindNearestVertex(XYZ clickedPoint, List<XYZ> vertices)
        {
            double minDistSq = double.MaxValue;
            XYZ nearest = null;
            foreach (var v in vertices)
            {
                double dx = clickedPoint.X - v.X;
                double dy = clickedPoint.Y - v.Y;
                double distSq = dx * dx + dy * dy;
                if (distSq < minDistSq)
                {
                    minDistSq = distSq;
                    nearest = v;
                }
            }
            return nearest ?? XYZ.Zero;
        }
    }
}