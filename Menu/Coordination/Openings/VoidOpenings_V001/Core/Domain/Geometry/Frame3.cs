namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry
{
    /// <summary>
    /// Position and orientation of an opening: <see cref="AxisZ"/> is the direction the void runs
    /// through the structure, <see cref="AxisX"/> the direction of its width and <see cref="AxisY"/>
    /// of its height. The three axes are unit length and perpendicular.
    /// </summary>
    public readonly struct Frame3
    {
        /// <summary>Centre of the opening.</summary>
        public Vec3 Origin { get; }

        /// <summary>Width direction.</summary>
        public Vec3 AxisX { get; }

        /// <summary>Height direction.</summary>
        public Vec3 AxisY { get; }

        /// <summary>Cut direction (through the structure).</summary>
        public Vec3 AxisZ { get; }

        /// <summary>Creates a frame from its origin and axes.</summary>
        public Frame3(Vec3 origin, Vec3 axisX, Vec3 axisY, Vec3 axisZ)
        {
            Origin = origin;
            AxisX = axisX;
            AxisY = axisY;
            AxisZ = axisZ;
        }
    }
}
