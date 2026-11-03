using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Revit
{
    /// <summary>Collects the line styles usable for detail lines and finds the per-layer defaults.</summary>
    public static class LineStyleProvider
    {
        public const string DefaultGrid = "Thin Lines";
        public const string DefaultHipRidge = "Wide Lines";
        public const string DefaultContour = "Hidden";

        // Boundary-type styles that Revit does not accept on ordinary detail lines.
        private static readonly string[] Excluded =
        {
            "area boundary", "room separation", "space separation", "sketch"
        };

        public static IReadOnlyList<LineStyleOption> GetStyles(Document doc)
        {
            Category? lines = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            if (lines == null)
            {
                return Array.Empty<LineStyleOption>();
            }

            var result = new List<LineStyleOption>();
            foreach (Category sub in lines.SubCategories)
            {
                if (Excluded.Contains(Normalize(sub.Name)))
                {
                    continue;
                }

                GraphicsStyle? style = sub.GetGraphicsStyle(GraphicsStyleType.Projection);
                if (style != null)
                {
                    result.Add(new LineStyleOption(sub.Name, style.Id));
                }
            }

            return result.OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Finds a style by name ignoring case and angle brackets ("&lt;Thin Lines&gt;" = "Thin Lines");
        /// falls back to the first style.
        /// </summary>
        public static LineStyleOption? FindOrFirst(IReadOnlyList<LineStyleOption> styles, string name)
        {
            string wanted = Normalize(name);
            return styles.FirstOrDefault(s => Normalize(s.Name) == wanted) ?? styles.FirstOrDefault();
        }

        /// <summary>Projection style of "&lt;Invisible lines&gt;", used as the boundary of filled regions; null if absent.</summary>
        public static ElementId? FindInvisible(Document doc)
        {
            Category? lines = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            if (lines == null)
            {
                return null;
            }

            foreach (Category sub in lines.SubCategories)
            {
                if (Normalize(sub.Name) == "invisible lines")
                {
                    return sub.GetGraphicsStyle(GraphicsStyleType.Projection)?.Id;
                }
            }

            return null;
        }

        private static string Normalize(string name) => name.Trim().Trim('<', '>').Trim().ToLowerInvariant();
    }
}
