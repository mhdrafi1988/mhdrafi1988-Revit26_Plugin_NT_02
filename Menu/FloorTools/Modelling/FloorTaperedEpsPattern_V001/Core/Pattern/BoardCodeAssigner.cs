using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>
    /// Groups boards with the same thickness range (rounded to 1 mm) and full/cut state
    /// and gives each group a code: F01, F02 … for full boards, C01, C02 … for cut boards.
    /// Stand-in for the V001 PieceGrouper until the V001 Core is copied in (see README).
    /// </summary>
    public static class BoardCodeAssigner
    {
        public static IReadOnlyList<BoardPiece> Assign(IReadOnlyList<BoardPiece> pieces)
        {
            var groups = pieces
                .GroupBy(p => (p.IsFull, Min: Math.Round(p.MinThicknessMm), Max: Math.Round(p.MaxThicknessMm)))
                .OrderByDescending(g => g.Key.IsFull)
                .ThenBy(g => g.Key.Min)
                .ThenBy(g => g.Key.Max)
                .ToList();

            var codeByKey = new Dictionary<(bool, double, double), string>();
            int full = 0, cut = 0;
            foreach (var g in groups)
            {
                string code = g.Key.IsFull
                    ? string.Format(CultureInfo.InvariantCulture, "F{0:00}", ++full)
                    : string.Format(CultureInfo.InvariantCulture, "C{0:00}", ++cut);
                codeByKey[g.Key] = code;
            }

            return pieces
                .Select(p => p with { Code = codeByKey[(p.IsFull, Math.Round(p.MinThicknessMm), Math.Round(p.MaxThicknessMm))] })
                .ToList();
        }
    }
}
