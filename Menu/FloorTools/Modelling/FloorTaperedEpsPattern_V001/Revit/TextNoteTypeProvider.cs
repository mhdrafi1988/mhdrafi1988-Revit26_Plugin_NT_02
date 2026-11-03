using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Revit
{
    /// <summary>Collects text note types; the document default is listed first.</summary>
    public static class TextNoteTypeProvider
    {
        public static IReadOnlyList<TextNoteTypeOption> GetTypes(Document doc)
        {
            ElementId defaultId = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);

            return new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>()
                .Select(t => new TextNoteTypeOption(t.Name, t.Id))
                .OrderByDescending(o => o.TypeId == defaultId)
                .ThenBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
