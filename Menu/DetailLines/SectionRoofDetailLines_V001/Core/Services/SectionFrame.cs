using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.SectionRoofDetailLines.V001.Core.Services
{
    /// <summary>
    /// A section view's geometry, in the one shape the rest of the tool needs: the plane its
    /// detail lines live in, and the box of model space it shows.
    /// <para>
    /// A detail curve is a 2D, view-specific element, so every curve the tool creates has to lie
    /// in the view's own plane — for a section view, the plane through <see cref="View.Origin"/>
    /// with <see cref="View.ViewDirection"/> as its normal. That is the plane the model is cut
    /// on, which is why the cut profile of a roof or wall comes out of the same plane and needs
    /// no projection afterwards.
    /// </para>
    /// <para>
    /// The visible box is the view's crop box. Its transform's basis vectors are the view's
    /// right, up and view directions, so a point's position is tested in crop-box coordinates
    /// rather than against the model's X/Y/Z — that keeps every test correct for a section at
    /// any angle, which an axis-aligned test would not be. Nothing here changes the model.
    /// </para>
    /// </summary>
    public sealed class SectionFrame
    {
        private readonly Transform _boxToModel;
        private readonly Transform _modelToBox;

        private SectionFrame(ViewSection view, Plane cutPlane, BoundingBoxXYZ cropBox, bool cropInactive)
        {
            View = view;
            CutPlane = cutPlane;
            CropInactive = cropInactive;
            BoxMin = cropBox.Min;
            BoxMax = cropBox.Max;
            _boxToModel = cropBox.Transform;
            _modelToBox = cropBox.Transform.Inverse;
        }

        /// <summary>The section view this frame describes.</summary>
        public ViewSection View { get; }

        /// <summary>The plane the view's detail lines live in, and the model is cut on.</summary>
        public Plane CutPlane { get; }

        /// <summary>True when the view's crop box is switched off — the box is still used as the
        /// run's extent, but Revit may show more than it covers, so the caller warns.</summary>
        public bool CropInactive { get; }

        /// <summary>Low corner of the crop box, in crop-box coordinates.</summary>
        public XYZ BoxMin { get; }

        /// <summary>High corner of the crop box, in crop-box coordinates.</summary>
        public XYZ BoxMax { get; }

        /// <summary>The view's up direction, as a model-space unit vector.</summary>
        public XYZ Up => View.UpDirection;

        /// <summary>The view's right direction, as a model-space unit vector.</summary>
        public XYZ Right => View.RightDirection;

        /// <summary>
        /// Reads <paramref name="view"/>'s plane and crop box, or returns null when Revit gives no
        /// crop box for it (a view the tool then leaves alone rather than guessing an extent for).
        /// </summary>
        public static SectionFrame Create(ViewSection view)
        {
            if (view == null) return null;

            BoundingBoxXYZ cropBox;
            bool cropInactive;
            try
            {
                cropBox = view.CropBox;
                cropInactive = !view.CropBoxActive;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return null;
            }

            if (cropBox == null || cropBox.Transform == null) return null;

            XYZ normal = view.ViewDirection;
            if (normal == null || normal.IsZeroLength()) return null;

            Plane plane = Plane.CreateByNormalAndOrigin(normal.Normalize(), view.Origin);
            return new SectionFrame(view, plane, cropBox, cropInactive);
        }

        /// <summary>Position of <paramref name="modelPoint"/> in crop-box coordinates: X across the
        /// view, Y up it, Z along the view direction.</summary>
        public XYZ ToBox(XYZ modelPoint) => _modelToBox.OfPoint(modelPoint);

        /// <summary>The model point at <paramref name="boxPoint"/> in crop-box coordinates.</summary>
        public XYZ ToModel(XYZ boxPoint) => _boxToModel.OfPoint(boxPoint);

        /// <summary>
        /// True when the box of <paramref name="elementBox"/> — read in its own document and
        /// brought into the model by <paramref name="toModel"/> — may reach into the visible box.
        /// Conservative: it never rules out an element that is in fact visible, and lets through a
        /// few that are not, which the cut itself then discards.
        /// </summary>
        public bool MayIntersect(BoundingBoxXYZ elementBox, Transform toModel, double padFeet)
        {
            if (elementBox == null) return false;

            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

            foreach (XYZ corner in Corners(elementBox))
            {
                XYZ model = toModel == null ? corner : toModel.OfPoint(corner);
                XYZ box = _modelToBox.OfPoint(model);
                minX = Math.Min(minX, box.X); maxX = Math.Max(maxX, box.X);
                minY = Math.Min(minY, box.Y); maxY = Math.Max(maxY, box.Y);
                minZ = Math.Min(minZ, box.Z); maxZ = Math.Max(maxZ, box.Z);
            }

            return minX <= BoxMax.X + padFeet && maxX >= BoxMin.X - padFeet
                && minY <= BoxMax.Y + padFeet && maxY >= BoxMin.Y - padFeet
                && minZ <= BoxMax.Z + padFeet && maxZ >= BoxMin.Z - padFeet;
        }

        /// <summary>
        /// The visible box as an axis-aligned <see cref="Outline"/> in the coordinates of a linked
        /// document, for a <see cref="BoundingBoxIntersectsFilter"/> on that link. The box is
        /// rotated with the section, so the outline is the box of its eight corners — a superset,
        /// narrowed afterwards by <see cref="MayIntersect"/>.
        /// </summary>
        public Outline OutlineInLinkSpace(Transform linkToModel, double padFeet)
        {
            Transform modelToLink = linkToModel == null ? Transform.Identity : linkToModel.Inverse;

            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

            foreach (XYZ corner in BoxCornersInModel())
            {
                XYZ link = modelToLink.OfPoint(corner);
                minX = Math.Min(minX, link.X); maxX = Math.Max(maxX, link.X);
                minY = Math.Min(minY, link.Y); maxY = Math.Max(maxY, link.Y);
                minZ = Math.Min(minZ, link.Z); maxZ = Math.Max(maxZ, link.Z);
            }

            return new Outline(
                new XYZ(minX - padFeet, minY - padFeet, minZ - padFeet),
                new XYZ(maxX + padFeet, maxY + padFeet, maxZ + padFeet));
        }

        /// <summary>The eight corners of the visible box, in model coordinates.</summary>
        public IEnumerable<XYZ> BoxCornersInModel()
        {
            foreach (XYZ local in LocalCorners(BoxMin, BoxMax))
                yield return _boxToModel.OfPoint(local);
        }

        /// <summary>
        /// <paramref name="curve"/> cut down to the part inside the visible box, or null when none
        /// of it is. Only the across and up directions are tested: a curve already in the cut
        /// plane has nothing to clip along the view direction.
        /// </summary>
        public Curve ClipToBox(Curve curve, double padFeet)
        {
            if (curve == null) return null;
            if (curve is not Line line) return InsideBox(curve, padFeet) ? curve : null;

            XYZ a = ToBox(line.GetEndPoint(0));
            XYZ b = ToBox(line.GetEndPoint(1));

            double t0 = 0.0, t1 = 1.0;
            if (!ClipAxis(a.X, b.X, BoxMin.X - padFeet, BoxMax.X + padFeet, ref t0, ref t1)) return null;
            if (!ClipAxis(a.Y, b.Y, BoxMin.Y - padFeet, BoxMax.Y + padFeet, ref t0, ref t1)) return null;
            if (t1 - t0 <= 1e-9) return null;

            XYZ p0 = line.Evaluate(t0, true);
            XYZ p1 = line.Evaluate(t1, true);
            return p0.DistanceTo(p1) < 1e-7 ? null : Line.CreateBound(p0, p1);
        }

        /// <summary>True when every tessellated point of <paramref name="curve"/> is inside the
        /// visible box, across and up.</summary>
        public bool InsideBox(Curve curve, double padFeet)
        {
            if (curve == null) return false;
            foreach (XYZ point in curve.Tessellate())
            {
                XYZ box = ToBox(point);
                if (box.X < BoxMin.X - padFeet || box.X > BoxMax.X + padFeet) return false;
                if (box.Y < BoxMin.Y - padFeet || box.Y > BoxMax.Y + padFeet) return false;
            }
            return true;
        }

        // Liang-Barsky on one axis: narrows [t0,t1] to the part of a->b inside [lo,hi],
        // or returns false when the segment misses that slab entirely.
        private static bool ClipAxis(double a, double b, double lo, double hi, ref double t0, ref double t1)
        {
            double delta = b - a;
            if (Math.Abs(delta) < 1e-12)
                return a >= lo && a <= hi;

            double enter = (lo - a) / delta;
            double exit = (hi - a) / delta;
            if (enter > exit) (enter, exit) = (exit, enter);

            t0 = Math.Max(t0, enter);
            t1 = Math.Min(t1, exit);
            return t0 <= t1;
        }

        private static IEnumerable<XYZ> Corners(BoundingBoxXYZ box)
        {
            Transform t = box.Transform ?? Transform.Identity;
            foreach (XYZ local in LocalCorners(box.Min, box.Max))
                yield return t.OfPoint(local);
        }

        private static IEnumerable<XYZ> LocalCorners(XYZ min, XYZ max)
        {
            yield return new XYZ(min.X, min.Y, min.Z);
            yield return new XYZ(max.X, min.Y, min.Z);
            yield return new XYZ(max.X, max.Y, min.Z);
            yield return new XYZ(min.X, max.Y, min.Z);
            yield return new XYZ(min.X, min.Y, max.Z);
            yield return new XYZ(max.X, min.Y, max.Z);
            yield return new XYZ(max.X, max.Y, max.Z);
            yield return new XYZ(min.X, max.Y, max.Z);
        }
    }
}
