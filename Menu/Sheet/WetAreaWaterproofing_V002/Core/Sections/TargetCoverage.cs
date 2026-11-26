using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V002.Core.Sections
{
    /// <summary>A ticked room for <see cref="TargetCoverage"/>.</summary>
    /// <param name="Label">How the room is named in messages, e.g. "L02 · 2.14 Bath".</param>
    /// <param name="Input">What the section planner needs; null when the room has no usable outline.</param>
    public sealed record CoverageRoom(string Label, RoomSectionInput Input);

    /// <summary>What one section target finds and makes in the ticked rooms (tool 2.3.0).</summary>
    /// <param name="Kind">The target.</param>
    /// <param name="RoomsWith">Rooms where the target has at least one match.</param>
    /// <param name="RoomsTotal">Ticked rooms with a usable outline.</param>
    /// <param name="Matches">Every match in those rooms (doors, drains, fixtures, corners…).</param>
    /// <param name="Makes">Sections the run would place for this target (0 when it is off).</param>
    /// <param name="RoomsWithout">Labels of the rooms without a match.</param>
    /// <param name="Dropped">"L01 · 1.03 Ensuite: 1 dropped" for each room where the six-per-room limit left matches out.</param>
    /// <param name="DroppedRooms">Labels of those rooms.</param>
    public sealed record TargetCoverageRow(SectionTargetKind Kind, int RoomsWith, int RoomsTotal, int Matches, int Makes,
        IReadOnlyList<string> RoomsWithout, IReadOnlyList<string> Dropped, IReadOnlyList<string> DroppedRooms)
    {
        /// <summary>True when some room has no match or lost a section to the limit.</summary>
        public bool HasProblem => RoomsWithout.Count > 0 || Dropped.Count > 0;
    }

    /// <summary>Counts, before the run, what each section target finds and makes. Same planning as the run; no Revit types.</summary>
    public static class TargetCoverage
    {
        /// <summary>
        /// One row per target in <paramref name="targets"/>, in the same order. Rooms whose
        /// <see cref="CoverageRoom.Input"/> is null are left out of every count.
        /// </summary>
        public static IReadOnlyList<TargetCoverageRow> Count(IReadOnlyList<CoverageRoom> rooms,
            IReadOnlyList<SectionTargetSetting> targets, int max = SectionTargets.MaxPerRoom)
        {
            var result = new List<TargetCoverageRow>();
            if (targets == null) return result;
            List<CoverageRoom> usable = (rooms ?? Array.Empty<CoverageRoom>())
                .Where(r => r?.Input?.Outer != null && r.Input.Outer.Count >= 3).ToList();

            // What the run places per room, by target kind.
            var made = new Dictionary<CoverageRoom, Dictionary<SectionTargetKind, int>>();
            foreach (CoverageRoom room in usable)
                made[room] = SectionPlanner.Plan(room.Input, targets, true, "x", max).Sections
                    .GroupBy(s => s.Kind).ToDictionary(g => g.Key, g => g.Count());

            foreach (SectionTargetSetting target in targets.Where(t => t != null))
            {
                int roomsWith = 0, matches = 0, makes = 0;
                var without = new List<string>();
                var dropped = new List<string>();
                var droppedRooms = new List<string>();
                foreach (CoverageRoom room in usable)
                {
                    int found = SectionPlanner.CountMatches(room.Input, target);
                    matches += found;
                    if (found > 0) roomsWith++; else without.Add(room.Label);
                    if (!target.On) continue;

                    int placed = made[room].TryGetValue(target.Kind, out int n) ? n : 0;
                    makes += placed;
                    int wanted = target.AllMatches ? found : Math.Min(found, 1);
                    if (placed >= wanted) continue;
                    dropped.Add($"{room.Label}: {wanted - placed} dropped");
                    droppedRooms.Add(room.Label);
                }
                result.Add(new TargetCoverageRow(target.Kind, roomsWith, usable.Count, matches, makes, without, dropped, droppedRooms));
            }
            return result;
        }
    }
}
