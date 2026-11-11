namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry
{
    /// <summary>
    /// Rigid transform (rotation plus move) between two models' coordinates, for example from a linked
    /// model to the host. The basis vectors are unit length and perpendicular.
    /// </summary>
    public readonly struct Transform3
    {
        /// <summary>Where the source origin lands.</summary>
        public Vec3 Origin { get; }

        /// <summary>Where the source X direction points.</summary>
        public Vec3 BasisX { get; }

        /// <summary>Where the source Y direction points.</summary>
        public Vec3 BasisY { get; }

        /// <summary>Where the source Z direction points.</summary>
        public Vec3 BasisZ { get; }

        /// <summary>Creates a transform.</summary>
        public Transform3(Vec3 origin, Vec3 basisX, Vec3 basisY, Vec3 basisZ)
        {
            Origin = origin;
            BasisX = basisX;
            BasisY = basisY;
            BasisZ = basisZ;
        }

        /// <summary>The transform that changes nothing.</summary>
        public static Transform3 Identity => new Transform3(Vec3.Zero, Vec3.UnitX, Vec3.UnitY, Vec3.UnitZ);

        /// <summary>Transforms a point.</summary>
        public Vec3 Apply(Vec3 point) => Origin + ApplyVector(point);

        /// <summary>Transforms a direction (no move).</summary>
        public Vec3 ApplyVector(Vec3 vector) => BasisX * vector.X + BasisY * vector.Y + BasisZ * vector.Z;

        /// <summary>Transforms a frame.</summary>
        public Frame3 Apply(Frame3 frame) => new Frame3(
            Apply(frame.Origin), ApplyVector(frame.AxisX), ApplyVector(frame.AxisY), ApplyVector(frame.AxisZ));

        /// <summary>The opposite transform.</summary>
        public Transform3 Inverse() => new Transform3(
            new Vec3(-Origin.Dot(BasisX), -Origin.Dot(BasisY), -Origin.Dot(BasisZ)),
            new Vec3(BasisX.X, BasisY.X, BasisZ.X),
            new Vec3(BasisX.Y, BasisY.Y, BasisZ.Y),
            new Vec3(BasisX.Z, BasisY.Z, BasisZ.Z));
    }
}
