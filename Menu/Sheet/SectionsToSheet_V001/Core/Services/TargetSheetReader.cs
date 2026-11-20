using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Layout;
using Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Services
{
    /// <summary>
    /// Reads the existing sheet chosen as the target into a <see cref="TargetSheetContent"/>: its
    /// title block extent, its viewports (box united with title, box centre, detail number), every
    /// other item on it by bounding box, and worksharing locks. Read-only.
    /// </summary>
    public static class TargetSheetReader
    {
        /// <summary>
        /// Reads sheet <paramref name="sheetKey"/>. Returns null when it is not a sheet (deleted, or
        /// a placeholder); <paramref name="problem"/> then says why.
        /// </summary>
        public static TargetSheetContent Read(Document doc, long sheetKey, out string problem)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            problem = null;
            if (sheetKey == 0)
            {
                problem = "Choose the sheet to place the sections on.";
                return null;
            }
            if (doc.GetElement(new ElementId(sheetKey)) is not ViewSheet sheet || sheet.IsPlaceholder)
            {
                problem = "The chosen sheet is no longer in the model. Choose another one.";
                return null;
            }

            var titleBlocks = new FilteredElementCollector(doc, sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsNotElementType().ToElements();
            string titleBlockName = null;
            LayoutRect? frame = null;
            string frameProblem = null;
            if (titleBlocks.Count == 0)
            {
                frameProblem = $"Sheet {sheet.SheetNumber} has no title block, so its usable area is unknown. Add a title block to it, or choose another sheet.";
            }
            else
            {
                if (doc.GetElement(titleBlocks[0].GetTypeId()) is FamilySymbol symbol)
                    titleBlockName = $"{symbol.FamilyName} : {symbol.Name}";
                (frame, frameProblem) = FootprintMeasurer.ReadSheetFrame(doc, sheet);
            }

            var items = new List<TargetItem>();
            foreach (ElementId id in sheet.GetAllViewports())
            {
                if (doc.GetElement(id) is not Viewport vp || FootprintMeasurer.ReadOutline(vp) is not (LayoutRect rect, XYZ centre))
                    continue;
                var view = doc.GetElement(vp.ViewId) as View;
                items.Add(new TargetItem
                {
                    Key = vp.Id.Value,
                    ViewKey = vp.ViewId.Value,
                    Name = view?.Name ?? "View",
                    IsViewport = true,
                    Rect = rect,
                    BoxCenterX = centre.X,
                    BoxCenterY = centre.Y,
                    DetailNumber = vp.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER)?.AsString(),
                    Scale = SafeScale(view),
                    LockReason = doc.IsWorkshared ? SectionCollector.Lock(doc, vp.Id, "the viewport") : null
                });
            }

            var titleBlockIds = new HashSet<ElementId>(titleBlocks.Select(t => t.Id));
            foreach (Element e in new FilteredElementCollector(doc, sheet.Id).WhereElementIsNotElementType())
            {
                if (e is Viewport || titleBlockIds.Contains(e.Id) || e.Category == null)
                    continue;
                if (e is ScheduleSheetInstance ssi && ssi.IsTitleblockRevisionSchedule)
                    continue;
                long category = e.Category.Id.Value;
                if (category == (long)BuiltInCategory.OST_GuideGrid || category == (long)BuiltInCategory.OST_TitleBlocks)
                    continue;

                BoundingBoxXYZ box;
                try { box = e.get_BoundingBox(sheet); }
                catch (Autodesk.Revit.Exceptions.ApplicationException) { continue; }
                if (box == null)
                    continue;
                var r = new LayoutRect(box.Min.X, box.Min.Y, box.Max.X, box.Max.Y);
                if (!r.IsValid)
                    continue;

                string name = e is ScheduleSheetInstance s && doc.GetElement(s.ScheduleId) is View schedule
                    ? schedule.Name
                    : e.Category.Name;
                items.Add(new TargetItem { Key = e.Id.Value, Name = name, Rect = r });
            }

            return new TargetSheetContent
            {
                Key = sheet.Id.Value,
                Number = sheet.SheetNumber,
                Name = sheet.Name,
                TitleBlockName = titleBlockName,
                Frame = frame,
                FrameProblem = frame == null ? frameProblem ?? $"The title block of {sheet.SheetNumber} could not be read." : null,
                LockReason = doc.IsWorkshared ? SectionCollector.Lock(doc, sheet.Id, "sheet " + sheet.SheetNumber) : null,
                Items = items
            };
        }

        private static int SafeScale(View view)
        {
            if (view == null)
                return 0;
            try { return view.Scale; }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { return 0; }
        }
    }
}
