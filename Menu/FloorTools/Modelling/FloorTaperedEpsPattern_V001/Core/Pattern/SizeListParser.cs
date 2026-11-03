using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>Parses "1.0x0.6; 1.2x0.6, 1.0×1.0" (separators ; , space; x × * between sizes).</summary>
    public static class SizeListParser
    {
        private static readonly Regex Item = new Regex(@"^\s*([0-9]*[.,]?[0-9]+)\s*[xX×*]\s*([0-9]*[.,]?[0-9]+)\s*$");

        public static (IReadOnlyList<BoardSize> Sizes, IReadOnlyList<string> Errors) Parse(string? text)
        {
            var sizes = new List<BoardSize>();
            var errors = new List<string>();
            foreach (string raw in (text ?? string.Empty).Split(';', '\n', '|'))
            {
                string token = raw.Trim();
                if (token.Length == 0)
                {
                    continue;
                }

                Match m = Item.Match(token);
                if (!m.Success)
                {
                    errors.Add($"\"{token}\" is not a size like 1.0x0.6.");
                    continue;
                }

                double l = double.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
                double w = double.Parse(m.Groups[2].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
                if (l < Pattern2DSettings.MinBoardSizeM - 1e-9 || l > Pattern2DSettings.MaxBoardSizeM + 1e-9 ||
                    w < Pattern2DSettings.MinBoardSizeM - 1e-9 || w > Pattern2DSettings.MaxBoardSizeM + 1e-9)
                {
                    errors.Add(string.Format(CultureInfo.InvariantCulture,
                        "{0:0.00}x{1:0.00}: each side must be {2:0.00}–{3:0.00} m.", l, w,
                        Pattern2DSettings.MinBoardSizeM, Pattern2DSettings.MaxBoardSizeM));
                    continue;
                }

                sizes.Add(new BoardSize(l, w));
            }

            return (sizes, errors);
        }
    }
}
