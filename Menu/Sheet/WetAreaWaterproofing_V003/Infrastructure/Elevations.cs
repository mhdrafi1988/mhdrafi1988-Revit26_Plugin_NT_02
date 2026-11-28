using Autodesk.Revit.DB;
using System.Linq;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V003.Infrastructure
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

        /// <summary>
        /// Feet to add to an internal Z to get the elevation Revit's UI shows for it — the project
        /// elevation, measured from the Project Base Point. <b>Display only:</b> never add this to a value
        /// that is then compared with or subtracted from element geometry.
        /// <para>
        /// Read as <see cref="Level.ProjectElevation"/> minus <see cref="Level.Elevation"/> of any level,
        /// which is the Project Base Point's elevation and the same for every level. Taking the difference
        /// rather than the base point's own parameter keeps the sign right by construction. Returns 0 for a
        /// document with no level, and for the common case of a Project Base Point at elevation 0.
        /// </para>
        /// </summary>
        /// <param name="doc">Document to measure; null gives 0.</param>
        public static double ProjectBaseOffsetFt(Document doc)
        {
            if (doc == null) return 0;
            Level level = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().FirstOrDefault();
            return level == null ? 0 : level.ProjectElevation - level.Elevation;
        }
    }
}
