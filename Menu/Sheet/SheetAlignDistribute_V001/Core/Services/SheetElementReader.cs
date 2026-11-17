using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Layout;
using Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Services
{
    /// <summary>
    /// Reads one sheet — its title block frame, its viewports and its schedules — into a
    /// <see cref="SheetSnapshot"/>. Read-only, so no transaction is needed; everything downstream
    /// works off the snapshot and never touches the Revit API again.
    /// </summary>
    public static class SheetElementReader
    {
        /// <summary>
        /// Reads <paramref name="sheet"/> and nothing else: this tool only ever works on the sheet
        /// the user has open. Elements whose geometry cannot be read are counted and left out, so
        /// they are never moved.
        /// </summary>
        public static SheetSnapshot Read(Document doc, ViewSheet sheet, FootprintMode footprintMode)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (sheet == null) throw new ArgumentNullException(nameof(sheet));

            var (frame, frameProblem) = ReadTitleBlockFrame(doc, sheet);
            var elements = new List<SheetElementSnapshot>();
            int unreadable = 0;

            foreach (ElementId id in sheet.GetAllViewports())
            {
                if (doc.GetElement(id) is not Viewport viewport || doc.GetElement(viewport.ViewId) is not View view)
                {
                    unreadable++;
                    continue;
                }

                LayoutRect? footprint = ReadViewportFootprint(viewport, footprintMode);
                if (footprint == null)
                {
                    unreadable++;
                    continue;
                }

                elements.Add(new SheetElementSnapshot
                {
                    ElementId = viewport.Id,
                    Kind = SheetElementKind.Viewport,
                    Name = view.Name,
                    TypeName = ViewTypeLabel(view.ViewType),
                    DetailNumber = viewport.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER)?.AsString()?.Trim() ?? "",
                    Footprint = footprint.Value,
                    IsPinned = viewport.Pinned,
                    LockReason = LockReason(doc, viewport.Id)
                });
            }

            foreach (var schedule in new FilteredElementCollector(doc, sheet.Id)
                         .OfClass(typeof(ScheduleSheetInstance))
                         .Cast<ScheduleSheetInstance>()
                         .Where(s => !s.IsTitleblockRevisionSchedule))
            {
                LayoutRect? footprint = ReadBoundingBox(schedule, sheet);
                if (footprint == null)
                {
                    unreadable++;
                    continue;
                }

                elements.Add(new SheetElementSnapshot
                {
                    ElementId = schedule.Id,
                    Kind = SheetElementKind.Schedule,
                    Name = schedule.Name,
                    TypeName = "Schedule",
                    DetailNumber = "",
                    Footprint = footprint.Value,
                    IsPinned = schedule.Pinned,
                    LockReason = LockReason(doc, schedule.Id)
                });
            }

            return new SheetSnapshot
            {
                SheetId = sheet.Id,
                SheetLabel = $"{sheet.SheetNumber} - {sheet.Name}",
                TitleBlockFrame = frame,
                FrameProblem = frameProblem,
                Elements = elements,
                UnreadableCount = unreadable
            };
        }

        /// <summary>
        /// The viewport box, united with its title outline when the footprint mode asks for it.
        /// Including the title is what makes the titles line up and keeps a long title from sitting
        /// over the view next to it. Null when the box cannot be read.
        /// </summary>
        private static LayoutRect? ReadViewportFootprint(Viewport viewport, FootprintMode mode)
        {
            Outline box;
            try
            {
                box = viewport.GetBoxOutline();
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return null;
            }
            if (box == null || box.IsEmpty)
                return null;

            double minX = box.MinimumPoint.X, minY = box.MinimumPoint.Y;
            double maxX = box.MaximumPoint.X, maxY = box.MaximumPoint.Y;

            if (mode == FootprintMode.BoxAndTitle)
            {
                try
                {
                    // Empty or degenerate when the viewport type hides its title.
                    Outline label = viewport.GetLabelOutline();
                    if (label != null && !label.IsEmpty
                        && label.MaximumPoint.X > label.MinimumPoint.X
                        && label.MaximumPoint.Y > label.MinimumPoint.Y)
                    {
                        minX = Math.Min(minX, label.MinimumPoint.X);
                        minY = Math.Min(minY, label.MinimumPoint.Y);
                        maxX = Math.Max(maxX, label.MaximumPoint.X);
                        maxY = Math.Max(maxY, label.MaximumPoint.Y);
                    }
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    // No readable title — the box alone is the footprint.
                }
            }

            var rect = new LayoutRect(minX, minY, maxX, maxY);
            return rect.IsValid ? rect : null;
        }

        private static LayoutRect? ReadBoundingBox(Element element, ViewSheet sheet)
        {
            BoundingBoxXYZ box;
            try
            {
                box = element.get_BoundingBox(sheet);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return null;
            }
            if (box == null) return null;

            var rect = new LayoutRect(box.Min.X, box.Min.Y, box.Max.X, box.Max.Y);
            return rect.IsValid ? rect : null;
        }

        /// <summary>
        /// The title block instance's own bounding box is the sheet's printable frame — there is no
        /// separate sheet extent to subtract it from, because ViewSheet.get_BoundingBox(null) does
        /// not report one reliably. Two title blocks make the frame ambiguous, so nothing is done.
        /// </summary>
        private static (LayoutRect? Frame, string Problem) ReadTitleBlockFrame(Document doc, ViewSheet sheet)
        {
            var titleBlocks = new FilteredElementCollector(doc, sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsNotElementType()
                .ToElements();

            if (titleBlocks.Count == 0)
                return (null, "No title block on this sheet — the usable area is measured from the title block.");
            if (titleBlocks.Count > 1)
                return (null, $"This sheet has {titleBlocks.Count} title blocks, so the usable area is ambiguous and nothing is aligned.");

            LayoutRect? rect = ReadBoundingBox(titleBlocks[0], sheet);
            return rect == null
                ? (null, "The title block's extent could not be read.")
                : (rect, null);
        }

        /// <summary>
        /// Worksharing / ACC: an element owned by someone else, or changed in central since the last
        /// reload, cannot be moved — Revit would refuse the edit at commit time, which would roll
        /// the whole sheet back. Better to say so in the grid up front.
        /// </summary>
        private static string LockReason(Document doc, ElementId id)
        {
            if (!doc.IsWorkshared)
                return null;

            CheckoutStatus status = WorksharingUtils.GetCheckoutStatus(doc, id, out string owner);
            if (status == CheckoutStatus.OwnedByOtherUser)
                return "Owned by " + (string.IsNullOrWhiteSpace(owner) ? "another user" : owner) + ".";

            if (WorksharingUtils.GetModelUpdatesStatus(doc, id) == ModelUpdatesStatus.UpdatedInCentral)
                return "Changed in central — run Reload Latest first.";

            return null;
        }

        private static string ViewTypeLabel(ViewType type) => type switch
        {
            ViewType.FloorPlan => "Floor Plan",
            ViewType.CeilingPlan => "Ceiling Plan",
            ViewType.EngineeringPlan => "Structural Plan",
            ViewType.AreaPlan => "Area Plan",
            ViewType.Section => "Section",
            ViewType.Elevation => "Elevation",
            ViewType.Detail => "Detail",
            ViewType.DraftingView => "Drafting",
            ViewType.ThreeD => "3D",
            ViewType.Legend => "Legend",
            ViewType.Rendering => "Rendering",
            ViewType.Schedule => "Schedule",
            _ => type.ToString()
        };
    }
}
