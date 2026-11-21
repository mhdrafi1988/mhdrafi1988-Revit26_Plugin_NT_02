using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.SectionRoofDetailLines.V001.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.SectionRoofDetailLines.V001.Core.Services
{
    /// <summary>
    /// Reads everything the window shows — the sheet's sections, what each one cuts or shows, and
    /// the project's line styles — into one <see cref="ModelSnapshot"/>. Read-only: no transaction
    /// is opened, so it is safe to call whenever Revit gives an API context.
    /// </summary>
    public sealed class ModelReader
    {
        private readonly VisibleElementCollector _visible = new();

        /// <summary>
        /// Reads the model for <paramref name="activeView"/>. Every section offered is read for
        /// its contents, so the counts in the list are the real ones rather than an estimate;
        /// that is also what makes the element list the union of what the sheet's sections show.
        /// </summary>
        public ModelSnapshot Read(Document doc, View activeView)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            var snapshot = new ModelSnapshot();
            snapshot.Sections = SheetSectionCollector.Collect(
                doc, activeView, out SectionSourceKind source, out string sheetLabel);
            snapshot.SectionSource = source;
            snapshot.SheetLabel = sheetLabel;
            snapshot.LineStyleNames = SectionDetailLineWriter.GetLineStyleNames(doc);

            foreach (SectionTarget target in snapshot.Sections)
            {
                if (doc.GetElement(new ElementId(target.ViewKey)) is not ViewSection view) continue;

                SectionFrame frame = SectionFrame.Create(view);
                if (frame == null)
                {
                    target.LockReason ??= "Revit gives no crop box for this view";
                    continue;
                }

                List<VisibleElementRow> rows = _visible.Collect(doc, frame, snapshot.Notes);
                snapshot.Elements.AddRange(rows);

                target.ElementCount = rows.Count;
                target.RoofCount = Count(rows, ElementRole.Roof);
                target.WallCount = Count(rows, ElementRole.Wall);
            }

            if (snapshot.Sections.Count == 0)
                snapshot.Notes.Add(new LogEntry(LogLevel.Warning,
                    "No section views were found to draw in. Put the sections on the sheet first, or open a section view."));

            return snapshot;
        }

        private static int Count(List<VisibleElementRow> rows, ElementRole role)
        {
            var n = 0;
            foreach (VisibleElementRow row in rows)
                if (row.Role == role) n++;
            return n;
        }
    }
}
