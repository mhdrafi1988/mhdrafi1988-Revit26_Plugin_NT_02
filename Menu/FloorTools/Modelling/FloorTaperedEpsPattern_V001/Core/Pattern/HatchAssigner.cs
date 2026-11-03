using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>Assigns a colour slot to every piece for the chosen <see cref="HatchMode"/>, and builds the legend.</summary>
    public static class HatchAssigner
    {
        public static (IReadOnlyList<HatchRegion> Regions, IReadOnlyList<LegendEntry> Legend) Assign(
            IReadOnlyList<BoardPiece> boards, HatchMode mode)
        {
            if (mode == HatchMode.None || boards.Count == 0)
            {
                return (Array.Empty<HatchRegion>(), Array.Empty<LegendEntry>());
            }

            Func<BoardPiece, int> slotOf;
            Func<int, string> labelOf;
            switch (mode)
            {
                case HatchMode.AlternateRows:
                    slotOf = b => (b.Row + 1) % 2;           // row 1 → slot 0
                    labelOf = s => s == 0 ? "Odd rows (1, 3, 5 …)" : "Even rows (2, 4, 6 …)";
                    break;

                case HatchMode.AlternateBoards:
                    slotOf = b => (b.Row + b.Column) % 2;    // checkerboard
                    labelOf = s => s == 0 ? "Boards A (checkerboard)" : "Boards B (checkerboard)";
                    break;

                case HatchMode.PieceType:
                    slotOf = b => b.Kind switch
                    {
                        PieceKind.Full => 0,
                        PieceKind.HipLeft or PieceKind.HipRight => 1,
                        PieceKind.RidgeRow => 3,
                        _ => 2
                    };
                    labelOf = s => s switch
                    {
                        0 => "Full board",
                        1 => "Cut along a hip",
                        2 => "Straight cut",
                        _ => "Ridge row"
                    };
                    break;

                default: // BoardCode
                    List<string> codes = boards.Select(b => b.Code).Distinct()
                        .OrderBy(c => c.Length > 0 ? c[0] switch { 'F' => 0, 'H' => 1, _ => 2 } : 3)
                        .ThenBy(c => c, StringComparer.Ordinal)
                        .ToList();
                    var index = codes.Select((c, i) => (c, i)).ToDictionary(x => x.c, x => x.i);
                    slotOf = b => index[b.Code];
                    labelOf = s => codes[s];
                    break;
            }

            var regions = boards.Select(b => new HatchRegion(b.Polygon, slotOf(b), b.EdgeIndex, b.Id)).ToList();
            var legend = regions.GroupBy(r => r.Slot)
                .OrderBy(g => g.Key)
                .Select(g => new LegendEntry(g.Key, labelOf(g.Key), g.Count()))
                .ToList();
            return (regions, legend);
        }
    }
}
