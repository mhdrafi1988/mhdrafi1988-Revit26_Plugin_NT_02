using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.SheetTextReplace.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.SheetTextReplace.V001.Core.Services
{
    /// <summary>
    /// Reads the scopes of a model (its sheets and the views that can hold text notes) and the text
    /// notes of one scope: for a sheet, the notes placed directly on it and the notes shown in each
    /// view placed on it; for a single view, the notes shown in that view. Runs inside Revit's API
    /// context.
    /// </summary>
    public static class SheetTextReader
    {
        /// <summary>View types that can hold text notes, so are offered as a single-view scope.</summary>
        private static readonly HashSet<ViewType> TextCapableTypes = new()
        {
            ViewType.FloorPlan, ViewType.CeilingPlan, ViewType.EngineeringPlan, ViewType.AreaPlan,
            ViewType.Section, ViewType.Elevation, ViewType.Detail, ViewType.DraftingView, ViewType.Legend
        };

        /// <summary>
        /// True when <paramref name="view"/> is a view the tool can work on its own — a real, non-sheet
        /// view of a type that can hold text notes.
        /// </summary>
        public static bool CanHoldTextNotes(View view)
        {
            if (view == null || !view.IsValidObject) return false;
            if (view is ViewSheet || view.IsTemplate) return false;
            return TextCapableTypes.Contains(view.ViewType);
        }

        /// <summary>
        /// Everything the scope picker lists: all real sheets (no placeholders) ordered by sheet
        /// number, then every view that can hold text notes, ordered by name.
        /// </summary>
        public static List<ScopeItem> ReadScopes(Document doc)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder && !s.IsTemplate)
                .Select(s => ScopeItem.ForSheet(s.Id.Value, s.SheetNumber ?? string.Empty, s.Name ?? string.Empty))
                .OrderBy(s => s.Number, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var views = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(CanHoldTextNotes)
                .Select(ScopeOf)
                .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            sheets.AddRange(views);
            return sheets;
        }

        /// <summary>The picker entry for a single view.</summary>
        public static ScopeItem ScopeOf(View view)
            => ScopeItem.ForView(view.Id.Value, view.Name ?? string.Empty, ViewTypeLabel(view.ViewType));

        /// <summary>Reads the text notes of <paramref name="view"/> alone, without its sheet.</summary>
        public static ScopeContent ReadView(Document doc, View view)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (view == null) throw new ArgumentNullException(nameof(view));

            List<TextNote> notes;
            try
            {
                notes = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(TextNote))
                    .Cast<TextNote>()
                    .ToList();
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                notes = new List<TextNote>(); // a view type that cannot be searched (rare)
            }

            // Every sheet this view is placed on: a change here shows on all of them.
            OtherSheetsByView(doc).TryGetValue(view.Id.Value, out var sheetsOfView);

            var info = new ViewInfo
            {
                Key = view.Id.Value,
                Name = view.Name ?? string.Empty,
                TypeLabel = ViewTypeLabel(view.ViewType),
                ScaleText = ScaleText(view),
                Owner = OwnerOf(doc, view.Id),
                AlsoOnSheets = (sheetsOfView ?? new List<(long, string)>())
                    .Select(s => s.Item2)
                    .Distinct()
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                Notes = ReadNotes(doc, notes, view, new HashSet<long>())
            };

            return new ScopeContent { Scope = ScopeOf(view), Views = new List<ViewInfo> { info } };
        }

        /// <summary>Reads the views and text notes of <paramref name="sheet"/>.</summary>
        public static ScopeContent Read(Document doc, ViewSheet sheet)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (sheet == null) throw new ArgumentNullException(nameof(sheet));

            var otherSheetsByView = OtherSheetsByView(doc);
            var seen = new HashSet<long>();
            var views = new List<ViewInfo>();

            // Notes placed directly on the sheet (general notes, stamps).
            var sheetNotes = new FilteredElementCollector(doc, sheet.Id)
                .OfClass(typeof(TextNote))
                .Cast<TextNote>()
                .Where(n => n.OwnerViewId == sheet.Id)
                .ToList();
            if (sheetNotes.Count > 0)
            {
                views.Add(new ViewInfo
                {
                    Key = sheet.Id.Value,
                    Name = "Placed on the sheet",
                    TypeLabel = ViewInfo.SheetItselfType,
                    IsSheetItself = true,
                    Owner = OwnerOf(doc, sheet.Id),
                    Notes = ReadNotes(doc, sheetNotes, sheet, seen)
                });
            }

            foreach (ElementId viewportId in sheet.GetAllViewports())
            {
                if (doc.GetElement(viewportId) is not Viewport viewport) continue;
                if (doc.GetElement(viewport.ViewId) is not View view) continue;

                List<TextNote> notes;
                try
                {
                    // Notes visible in the view: includes a primary view's notes shown in a dependent view.
                    notes = new FilteredElementCollector(doc, view.Id)
                        .OfClass(typeof(TextNote))
                        .Cast<TextNote>()
                        .ToList();
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException)
                {
                    notes = new List<TextNote>(); // a view type that cannot be searched (rare)
                }

                otherSheetsByView.TryGetValue(view.Id.Value, out var sheetsOfView);
                views.Add(new ViewInfo
                {
                    Key = view.Id.Value,
                    Name = view.Name ?? string.Empty,
                    TypeLabel = ViewTypeLabel(view.ViewType),
                    ScaleText = ScaleText(view),
                    Owner = OwnerOf(doc, view.Id),
                    AlsoOnSheets = (sheetsOfView ?? new List<(long, string)>())
                        .Where(s => s.Item1 != sheet.Id.Value)
                        .Select(s => s.Item2)
                        .Distinct()
                        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    Notes = ReadNotes(doc, notes, view, seen)
                });
            }

            return new ScopeContent
            {
                Scope = ScopeItem.ForSheet(sheet.Id.Value, sheet.SheetNumber ?? string.Empty, sheet.Name ?? string.Empty),
                Views = views
            };
        }

        /// <summary>
        /// Why <paramref name="note"/> cannot be changed now, or null. Checked when reading and again
        /// at Apply time.
        /// </summary>
        public static string BlockReason(Document doc, TextNote note, ElementId viewId)
        {
            if (doc.IsReadOnly)
                return "Read-only model";
            if (note.GroupId != ElementId.InvalidElementId)
                return "In a group";

            if (doc.IsWorkshared)
            {
                string owner = OwnerOf(doc, note.Id);
                if (owner != null) return "Owned by " + owner;
                if (viewId != null && viewId != ElementId.InvalidElementId)
                {
                    owner = OwnerOf(doc, viewId);
                    if (owner != null) return "View owned by " + owner;
                }

                try
                {
                    var updates = WorksharingUtils.GetModelUpdatesStatus(doc, note.Id);
                    if (updates == ModelUpdatesStatus.UpdatedInCentral || updates == ModelUpdatesStatus.DeletedInCentral)
                        return "Reload latest first";
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    // Status unknown (e.g. central not reachable): Revit itself refuses the edit if needed.
                }
            }

            try
            {
                var option = note.DesignOption;
                if (option != null && option.Id != DesignOption.GetActiveDesignOptionId(doc))
                    return "Read-only (inactive design option)";
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // Design option not readable: ignore.
            }

            return null;
        }

        /// <summary>The user who owns <paramref name="id"/> in a workshared model, or null.</summary>
        public static string OwnerOf(Document doc, ElementId id)
        {
            if (!doc.IsWorkshared) return null;
            try
            {
                var status = WorksharingUtils.GetCheckoutStatus(doc, id, out string owner);
                if (status == CheckoutStatus.OwnedByOtherUser)
                    return string.IsNullOrWhiteSpace(owner) ? "another user" : owner;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // Element not in a workset table (rare): treat as not owned.
            }
            return null;
        }

        private static List<NoteInfo> ReadNotes(Document doc, IEnumerable<TextNote> notes, View view, HashSet<long> seen)
        {
            var list = new List<NoteInfo>();
            foreach (var note in notes)
            {
                // A note can show in two viewports (dependent views): list it once.
                if (!seen.Add(note.Id.Value)) continue;
                list.Add(new NoteInfo
                {
                    Id = note.Id.Value,
                    Text = note.Text ?? string.Empty,
                    BlockReason = BlockReason(doc, note, view.Id),
                    Formatting = TextNoteWriter.DescribeFormatting(note)
                });
            }
            return list;
        }

        /// <summary>View id → the sheets (id, number) it is placed on, for legends shown on several sheets.</summary>
        private static Dictionary<long, List<(long, string)>> OtherSheetsByView(Document doc)
        {
            var map = new Dictionary<long, List<(long, string)>>();
            foreach (var viewport in new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>())
            {
                if (doc.GetElement(viewport.SheetId) is not ViewSheet s) continue;
                long key = viewport.ViewId.Value;
                if (!map.TryGetValue(key, out var list))
                    map[key] = list = new List<(long, string)>();
                list.Add((s.Id.Value, s.SheetNumber ?? string.Empty));
            }
            return map;
        }

        private static string ScaleText(View view)
        {
            try
            {
                int scale = view.Scale;
                return scale > 0 ? $"1 : {scale}" : "—";
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return "—";
            }
        }

        /// <summary>Display name of a view type, used to group the Views list.</summary>
        public static string ViewTypeLabel(ViewType type) => type switch
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
            _ => type.ToString()
        };
    }
}
