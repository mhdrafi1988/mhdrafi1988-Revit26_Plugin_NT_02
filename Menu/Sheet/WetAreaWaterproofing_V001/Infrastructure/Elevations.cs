using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Infrastructure
{
    /// <summary>
    /// The one place this tool reads a level's height from.
    /// <para>
    /// Every elevation the tool works with is a Revit <b>internal</b> Z in feet: the same base as
    /// element geometry, bounding boxes and the points handed back to the Revit API. Keeping one
    /// base is what lets a slab top, a level height and the Z of created geometry be compared and
    /// subtracted from one another.
    /// </para>
    /// </summary>
    public static class Elevations
    {
        /// <summary>
        /// Internal Z of <paramref name="level"/>, in feet.
        /// <para>
        /// This is <see cref="Level.Elevation"/>, never <see cref="Level.ProjectElevation"/>. The two
        /// differ by the Project Base Point's elevation, and only <see cref="Level.Elevation"/> shares
        /// its base with element geometry. Mixing them puts the slab search band, the roof's height
        /// above its level and the Z of created views off by that elevation.
        /// </para>
        /// </summary>
        /// <param name="level">Level to measure; null gives <paramref name="fallbackZFt"/>.</param>
        /// <param name="fallbackZFt">Value to use when <paramref name="level"/> is null.</param>
        public static double InternalZFt(Level level, double fallbackZFt = 0) => level?.Elevation ?? fallbackZFt;
    }
}
