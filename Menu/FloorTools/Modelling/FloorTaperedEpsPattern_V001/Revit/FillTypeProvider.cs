using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Revit
{
    /// <summary>Lists filled region types, and creates the tool's own EPS2D_Fill_nn types when asked.</summary>
    public static class FillTypeProvider
    {
        public const string ToolTypePrefix = "EPS2D_Fill_";
        public const int MaxToolTypes = 12;

        public static IReadOnlyList<FillTypeOption> GetTypes(Document doc) =>
            new FilteredElementCollector(doc)
                .OfClass(typeof(FilledRegionType))
                .Cast<FilledRegionType>()
                .Select(t => new FillTypeOption(t.Name, t.Id, Hex(t.ForegroundPatternColor)))
                .OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>
        /// Returns one type id per slot (up to <see cref="MaxToolTypes"/>, then repeating), creating
        /// EPS2D_Fill_01 … with a solid fill in the palette colour when missing. Existing tool types are
        /// reused as they are, so colours edited in Revit are kept. Call inside an open transaction.
        /// </summary>
        public static IReadOnlyList<ElementId> EnsureToolTypes(Document doc, int slotCount, out int createdCount)
        {
            createdCount = 0;
            var ids = new List<ElementId>();
            int needed = Math.Min(Math.Max(slotCount, 1), MaxToolTypes);
            List<FilledRegionType> all = new FilteredElementCollector(doc).OfClass(typeof(FilledRegionType)).Cast<FilledRegionType>().ToList();
            FilledRegionType? template = all.FirstOrDefault();
            if (template == null)
            {
                return ids;
            }

            ElementId solidId = SolidFillPatternId(doc);
            for (int i = 0; i < needed; i++)
            {
                string name = ToolTypePrefix + (i + 1).ToString("00");
                FilledRegionType? type = all.FirstOrDefault(t => t.Name == name);
                if (type == null)
                {
                    type = (FilledRegionType)template.Duplicate(name);
                    Core.Pattern.HatchColor c = Core.Pattern.HatchPalette.ForSlot(i);
                    if (solidId != ElementId.InvalidElementId)
                    {
                        type.ForegroundPatternId = solidId;
                    }

                    type.ForegroundPatternColor = new Color(c.R, c.G, c.B);
                    type.BackgroundPatternId = ElementId.InvalidElementId;
                    type.IsMasking = false;
                    createdCount++;
                }

                ids.Add(type.Id);
            }

            return ids;
        }

        private static string Hex(Color? c) =>
            c != null && c.IsValid ? $"#{c.Red:X2}{c.Green:X2}{c.Blue:X2}" : "#C0C0C0";

        private static ElementId SolidFillPatternId(Document doc)
        {
            FillPatternElement? solid = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(f => f.GetFillPattern().IsSolidFill);
            return solid?.Id ?? ElementId.InvalidElementId;
        }
    }
}
