// ==================================
// File: RunSummary.cs
// Namespace: Revit26_Plugin_Revised.FloorCreaser.V001
// ==================================

namespace Revit26_Plugin_Revised.FloorCreaser.V001.Services
{
    /// <summary>
    /// Immutable snapshot of a single Run result, displayed in the summary bar.
    /// </summary>
    public sealed class RunSummary
    {
        public int CreasesFound        { get; }
        public int BoundaryFound       { get; }
        public int Created             { get; }
        public int Failed              { get; }
        public int RidgePoints         { get; }
        public int DisconnectedPoints  { get; }

        public RunSummary(int creasesFound, int boundaryFound, int created, int failed, int ridgePoints = 0, int disconnectedPoints = 0)
        {
            CreasesFound       = creasesFound;
            BoundaryFound      = boundaryFound;
            Created            = created;
            Failed             = failed;
            RidgePoints        = ridgePoints;
            DisconnectedPoints = disconnectedPoints;
        }

        /// <summary>Blank summary shown before the first Run.</summary>
        public static RunSummary Empty => new RunSummary(0, 0, 0, 0, 0, 0);
    }
}
