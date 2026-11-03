using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>
    /// Codes for the contour-row layout:
    /// <list type="bullet">
    /// <item><c>F01, F02 …</c> full boards, one code per thickness band.</item>
    /// <item><c>H01-L / H01-R …</c> hip pieces; a mirrored pair shares the number, L/R gives the hand.</item>
    /// <item><c>C01, C02 …</c> every other cut piece (fillers, ridge row), one code per shape and band.</item>
    /// </list>
    /// Numbers run from the thinnest band upward.
    /// </summary>
    public static class RowPieceCoder
    {
        public static IReadOnlyList<BoardPiece> Assign(IReadOnlyList<BoardPiece> pieces)
        {
            string Band(BoardPiece p) => string.Format(CultureInfo.InvariantCulture, "{0:0.0}-{1:0.0}",
                Math.Round(p.MinThicknessMm, 1), Math.Round(p.MaxThicknessMm, 1));

            string Family(BoardPiece p) => p.Kind switch
            {
                PieceKind.Full => "F",
                PieceKind.HipLeft or PieceKind.HipRight => "H",
                _ => "C"
            };

            string Key(BoardPiece p) => Family(p) + "|" + Band(p) + "|" + (p.Kind == PieceKind.Full ? string.Empty : p.ShapeKey);

            var numbers = new Dictionary<string, string>();
            foreach (IGrouping<string, BoardPiece> family in pieces.GroupBy(Family))
            {
                int n = 0;
                foreach (string key in family
                    .OrderBy(p => Math.Round(p.MinThicknessMm, 1))
                    .ThenBy(p => Math.Round(p.MaxThicknessMm, 1))
                    .ThenBy(p => p.ShapeKey, StringComparer.Ordinal)
                    .Select(Key)
                    .Distinct())
                {
                    numbers[key] = family.Key + (++n).ToString("00", CultureInfo.InvariantCulture);
                }
            }

            return pieces.Select(p =>
            {
                string code = numbers[Key(p)];
                if (p.Kind == PieceKind.HipLeft)
                {
                    code += "-L";
                }
                else if (p.Kind == PieceKind.HipRight)
                {
                    code += "-R";
                }

                return p with { Code = code };
            }).ToList();
        }
    }
}
