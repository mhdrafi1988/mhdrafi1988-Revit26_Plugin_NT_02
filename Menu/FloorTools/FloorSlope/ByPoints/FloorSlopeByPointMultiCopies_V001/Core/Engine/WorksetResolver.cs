// =======================================================
// File: WorksetResolver.cs
// Namespace: Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001
// Purpose: Resolve (or create) the user-workset that a slope-variant
//          floor copy belongs to. Called from within MultiSlopeVariantEngine's
//          per-slope SubTransaction — Workset.Create requires an open
//          transaction, same as any other document change.
// =======================================================

using Autodesk.Revit.DB;
using System.Linq;

namespace Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001.Core.Engine
{
    public static class WorksetResolver
    {
        public readonly struct Result
        {
            public Result(WorksetId worksetId, bool wasCreated)
            {
                WorksetId = worksetId;
                WasCreated = wasCreated;
            }

            public WorksetId WorksetId { get; }
            public bool WasCreated { get; }
        }

        /// <summary>
        /// Finds an existing user workset by name, or creates one if none exists.
        /// Caller must guard doc.IsWorkshared before calling — a non-workshared
        /// document has no worksets to resolve.
        /// </summary>
        public static Result Resolve(Document doc, string worksetName)
        {
            Workset existing = new FilteredWorksetCollector(doc)
                .OfKind(WorksetKind.UserWorkset)
                .FirstOrDefault(w => w.Name == worksetName);

            if (existing != null)
                return new Result(existing.Id, wasCreated: false);

            Workset created = Workset.Create(doc, worksetName);
            return new Result(created.Id, wasCreated: true);
        }
    }
}
