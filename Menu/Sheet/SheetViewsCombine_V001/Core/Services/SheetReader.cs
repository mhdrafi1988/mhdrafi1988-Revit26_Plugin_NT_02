using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.SheetViewsCombine.V001.Core.Layout;
using Revit26_Plugin_Revised.SheetViewsCombine.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.SheetViewsCombine.V001.Core.Services
{
    /// <summary>
    /// Reads sheets, their viewports and title blocks from the model. Read-only, except
    /// <see cref="MeasureTitleBlock"/>, which places a title block on a temporary sheet inside a
    /// transaction that is always rolled back.
    /// </summary>
    public static class SheetReader
    {
        /// <summary>Every sheet, the active sheet and the title block types.</summary>
        public static ProjectCatalog ReadCatalog(Document doc, View activeView)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            var sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                .Where(s => !s.IsTemplate)
                .Select(s => new SheetSummary { Key = s.Id.Value, Number = s.SheetNumber ?? string.Empty, Name = s.Name ?? string.Empty })
                .OrderBy(s => s.Number, NaturalComparer.Instance)
                .ToList();

            var titleBlocks = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType().OfType<FamilySymbol>()
                .Select(t => new NamedType { Key = t.Id.Value, Name = $"{t.FamilyName} : {t.Name}" })
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new ProjectCatalog
            {
                Sheets = sheets,
                ActiveSheetKey = activeView is ViewSheet active ? active.Id.Value : 0,
                TitleBlocks = titleBlocks,
                ExistingSheetNumbers = new HashSet<string>(sheets.Select(s => s.Number), StringComparer.OrdinalIgnoreCase)
            };
        }

        /// <summary>The sheet's viewports, title block and schedules, or null when <paramref name="sheetKey"/> is not a sheet.</summary>
        public static SheetContent ReadSheet(Document doc, long sheetKey)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (sheetKey == 0 || doc.GetElement(new ElementId(sheetKey)) is not ViewSheet sheet)
                return null;

            var views = new List<PlacedView>();
            foreach (ElementId vpId in sheet.GetAllViewports())
            {
                if (doc.GetElement(vpId) is not Viewport vp || doc.GetElement(vp.ViewId) is not View view)
                    continue;
                var footprint = ReadFootprint(vp, out double offX, out double offY);
                if (footprint == null)
                    continue;

                views.Add(new PlacedView
                {
                    ViewportKey = vp.Id.Value,
                    ViewKey = view.Id.Value,
                    Name = view.Name ?? string.Empty,
                    ViewTypeLabel = TypeLabel(view.ViewType),
                    TypeOrder = TypeOrder(view.ViewType),
                    Scale = SafeScale(view),
                    IsLegend = view.ViewType == ViewType.Legend,
                    SheetKey = sheet.Id.Value,
                    SheetNumber = sheet.SheetNumber ?? string.Empty,
                    DetailNumber = vp.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER)?.AsString() ?? string.Empty,
                    ViewportTypeKey = vp.GetTypeId().Value,
                    Footprint = footprint.Value,
                    CenterOffsetX = offX,
                    CenterOffsetY = offY,
                    LockReason = LockReason(doc, vp)
                });
            }

            // Schedules take room too (revision schedules inside the title block do not).
            var fixedItems = new List<SheetRect>();
            foreach (var schedule in new FilteredElementCollector(doc, sheet.Id).OfClass(typeof(ScheduleSheetInstance))
                         .Cast<ScheduleSheetInstance>().Where(s => !s.IsTitleblockRevisionSchedule))
            {
                BoundingBoxXYZ bb = schedule.get_BoundingBox(sheet);
                if (bb != null)
                {
                    var r = new SheetRect(bb.Min.X, bb.Min.Y, bb.Max.X, bb.Max.Y);
                    if (r.IsValid) fixedItems.Add(r);
                }
            }

            var (frame, typeKey, originX, originY, problem) = ReadTitleBlock(doc, sheet);
            return new SheetContent
            {
                Key = sheet.Id.Value,
                Number = sheet.SheetNumber ?? string.Empty,
                Name = sheet.Name ?? string.Empty,
                TitleBlockTypeKey = typeKey,
                Frame = frame,
                TitleBlockOriginX = originX,
                TitleBlockOriginY = originY,
                FrameProblem = problem,
                LockReason = LockReason(doc, sheet),
                Views = views,
                FixedItems = fixedItems
            };
        }

        /// <summary>
        /// The extent of title block <paramref name="titleBlockKey"/> when placed at 0,0 on a new sheet.
        /// Must run in Revit's API context with no transaction open; the model is not changed.
        /// </summary>
        public static (SheetRect? Frame, string Problem) MeasureTitleBlock(Document doc, long titleBlockKey)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (titleBlockKey == 0 || doc.GetElement(new ElementId(titleBlockKey)) is not FamilySymbol symbol)
                return (null, "Choose a title block for the new sheet.");
            if (doc.IsReadOnly)
                return (null, "The document is read-only.");
            if (doc.IsModifiable)
                return (null, "Revit is busy with another change. Press Refresh.");

            using var tx = new Transaction(doc, "Sheet Views Combine - measure");
            QuietFailures.Attach(tx);
            try
            {
                if (tx.Start() != TransactionStatus.Started)
                    return (null, "Revit could not measure the title block.");
                if (!symbol.IsActive)
                    symbol.Activate();
                var sheet = ViewSheet.Create(doc, symbol.Id);
                doc.Regenerate();
                var (frame, _, _, _, problem) = ReadTitleBlock(doc, sheet);
                return (frame, problem);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                return (null, "The title block could not be measured: " + ex.Message);
            }
            finally
            {
                if (tx.HasStarted() && !tx.HasEnded())
                    tx.RollBack();
            }
        }

        /// <summary>The viewport box united with its title, and where the box centre sits in it.</summary>
        public static SheetRect? ReadFootprint(Viewport vp, out double centerOffsetX, out double centerOffsetY)
        {
            centerOffsetX = centerOffsetY = 0;
            Outline box;
            try
            {
                box = vp.GetBoxOutline();
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return null;
            }
            if (box == null || box.IsEmpty)
                return null;

            double minX = box.MinimumPoint.X, minY = box.MinimumPoint.Y;
            double maxX = box.MaximumPoint.X, maxY = box.MaximumPoint.Y;
            try
            {
                // Empty or degenerate when the viewport type hides its title.
                Outline label = vp.GetLabelOutline();
                if (label != null && !label.IsEmpty
                    && label.MaximumPoint.X > label.MinimumPoint.X && label.MaximumPoint.Y > label.MinimumPoint.Y)
                {
                    minX = Math.Min(minX, label.MinimumPoint.X);
                    minY = Math.Min(minY, label.MinimumPoint.Y);
                    maxX = Math.Max(maxX, label.MaximumPoint.X);
                    maxY = Math.Max(maxY, label.MaximumPoint.Y);
                }
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // No readable title — the box alone.
            }

            var rect = new SheetRect(minX, minY, maxX, maxY);
            if (!rect.IsValid)
                return null;
            XYZ centre = vp.GetBoxCenter();
            centerOffsetX = centre.X - minX;
            centerOffsetY = centre.Y - minY;
            return rect;
        }

        /// <summary>Why <paramref name="element"/> cannot be changed by this user, or null.</summary>
        public static string LockReason(Document doc, Element element)
        {
            if (element == null)
                return "it is no longer in the model";
            if (!doc.IsWorkshared)
                return null;
            try
            {
                var status = WorksharingUtils.GetCheckoutStatus(doc, element.Id, out string owner);
                if (status == CheckoutStatus.OwnedByOtherUser)
                    return string.IsNullOrWhiteSpace(owner)
                        ? "it is checked out by another user"
                        : $"it is checked out by {owner}";
                if (WorksharingUtils.GetModelUpdatesStatus(doc, element.Id) == ModelUpdatesStatus.UpdatedInCentral)
                    return "it was changed in the central model; synchronize first";
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // Status unknown: let Revit decide when the change is made.
            }
            return null;
        }

        private static (SheetRect? Frame, long TypeKey, double OriginX, double OriginY, string Problem) ReadTitleBlock(Document doc, ViewSheet sheet)
        {
            var titleBlock = new FilteredElementCollector(doc, sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsNotElementType()
                .OfType<FamilyInstance>().FirstOrDefault();
            if (titleBlock == null)
                return (null, 0, 0, 0, "no title block on the sheet");

            BoundingBoxXYZ bb = titleBlock.get_BoundingBox(sheet);
            if (bb == null)
                return (null, titleBlock.Symbol?.Id.Value ?? 0, 0, 0, "the title block's size could not be read");
            var rect = new SheetRect(bb.Min.X, bb.Min.Y, bb.Max.X, bb.Max.Y);
            var origin = (titleBlock.Location as LocationPoint)?.Point ?? XYZ.Zero;
            return rect.IsValid
                ? (rect, titleBlock.Symbol?.Id.Value ?? 0, origin.X, origin.Y, null)
                : (null, titleBlock.Symbol?.Id.Value ?? 0, 0, 0, "the title block has no usable size");
        }

        private static int SafeScale(View view)
        {
            try
            {
                return view.ViewType is ViewType.Legend or ViewType.ThreeD or ViewType.Schedule ? 0 : view.Scale;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return 0;
            }
        }

        /// <summary>Readable label for a view type.</summary>
        public static string TypeLabel(ViewType type) => type switch
        {
            ViewType.FloorPlan => "Floor Plan",
            ViewType.CeilingPlan => "Ceiling Plan",
            ViewType.EngineeringPlan => "Structural Plan",
            ViewType.AreaPlan => "Area Plan",
            ViewType.Section => "Section",
            ViewType.Elevation => "Elevation",
            ViewType.Detail => "Detail",
            ViewType.DraftingView => "Drafting View",
            ViewType.ThreeD => "3D View",
            ViewType.Rendering => "Rendering",
            ViewType.Walkthrough => "Walkthrough",
            ViewType.Legend => "Legend",
            _ => type.ToString()
        };

        private static int TypeOrder(ViewType type) => type switch
        {
            ViewType.FloorPlan => 0,
            ViewType.CeilingPlan => 1,
            ViewType.EngineeringPlan => 2,
            ViewType.AreaPlan => 3,
            ViewType.Elevation => 4,
            ViewType.Section => 5,
            ViewType.Detail => 6,
            ViewType.DraftingView => 7,
            ViewType.ThreeD => 8,
            ViewType.Rendering => 9,
            ViewType.Walkthrough => 10,
            ViewType.Legend => 20,
            _ => 15
        };
    }

    /// <summary>Compares "A-2" before "A-10".</summary>
    public sealed class NaturalComparer : IComparer<string>
    {
        /// <summary>Shared instance.</summary>
        public static NaturalComparer Instance { get; } = new();

        /// <inheritdoc/>
        public int Compare(string x, string y)
        {
            x ??= string.Empty;
            y ??= string.Empty;
            int i = 0, j = 0;
            while (i < x.Length && j < y.Length)
            {
                if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
                {
                    int si = i, sj = j;
                    while (i < x.Length && char.IsDigit(x[i])) i++;
                    while (j < y.Length && char.IsDigit(y[j])) j++;
                    string a = x[si..i].TrimStart('0'), b = y[sj..j].TrimStart('0');
                    int c = a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);
                    if (c != 0) return c;
                }
                else
                {
                    int c = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                    if (c != 0) return c;
                    i++;
                    j++;
                }
            }
            return (x.Length - i).CompareTo(y.Length - j);
        }
    }

    /// <summary>
    /// Failure handling for this tool's transactions: warnings are dismissed; errors are
    /// collected and the transaction rolls back silently, so the tool can report them itself.
    /// </summary>
    public sealed class QuietFailures : IFailuresPreprocessor
    {
        /// <summary>Error messages Revit reported, if any.</summary>
        public List<string> Errors { get; } = new();

        /// <summary>Attaches a new instance to <paramref name="tx"/> and returns it.</summary>
        public static QuietFailures Attach(Transaction tx)
        {
            var handler = new QuietFailures();
            var options = tx.GetFailureHandlingOptions();
            options.SetFailuresPreprocessor(handler);
            options.SetClearAfterRollback(true);
            tx.SetFailureHandlingOptions(options);
            return handler;
        }

        /// <inheritdoc/>
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            bool hasErrors = false;
            foreach (FailureMessageAccessor failure in failuresAccessor.GetFailureMessages())
            {
                if (failure.GetSeverity() == FailureSeverity.Warning)
                {
                    failuresAccessor.DeleteWarning(failure);
                }
                else
                {
                    hasErrors = true;
                    Errors.Add(failure.GetDescriptionText());
                }
            }

            if (!hasErrors)
                return FailureProcessingResult.Continue;

            var options = failuresAccessor.GetFailureHandlingOptions();
            options.SetClearAfterRollback(true);
            failuresAccessor.SetFailureHandlingOptions(options);
            return FailureProcessingResult.ProceedWithRollBack;
        }
    }
}
