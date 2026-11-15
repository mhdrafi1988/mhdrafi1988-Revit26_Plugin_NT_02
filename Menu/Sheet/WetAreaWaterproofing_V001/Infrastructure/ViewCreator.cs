using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Geometry;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Models;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Report;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Run;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Sections;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Infrastructure
{
    /// <summary>
    /// Creates the room plans, the drafting views and the reference-section markers. One instance
    /// serves one run: it remembers the view names in use and the drafting views already resolved.
    /// Every method needs an open transaction.
    /// </summary>
    public sealed class ViewCreator
    {
        private readonly Document _doc;
        private readonly WetAreaSettings _s;
        private readonly string _runId;
        private readonly HashSet<string> _viewNames;
        private readonly Dictionary<string, (ElementId Id, string Name)> _drafting = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Drafting views this run created.</summary>
        public int DraftingCreated { get; private set; }

        /// <summary>Reads the view names in use so new views never clash with them.</summary>
        public ViewCreator(Document doc, WetAreaSettings settings, string runId)
        {
            _doc = doc;
            _s = settings;
            _runId = runId;
            _viewNames = new HashSet<string>(
                new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Select(v => v.Name),
                StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Checks that the views this run needs can be made; null when they can, else what is missing.</summary>
        public string CheckReady(bool needsDrafting)
        {
            if (PlanType() == null) return "The project has no floor plan view type.";
            if (needsDrafting && DraftingType() == null) return "The project has no drafting view type.";
            if (!string.IsNullOrEmpty(_s.PlanTemplateName) && Template() == null)
                return $"View template \"{_s.PlanTemplateName}\" is not in the project any more. Pick another, or none.";
            return null;
        }

        private ViewFamilyType PlanType() => new FilteredElementCollector(_doc).OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>().Where(t => t.ViewFamily == ViewFamily.FloorPlan)
            .OrderBy(t => t.Name == _s.PlanViewTypeName ? 0 : 1).ThenBy(t => t.Name).FirstOrDefault();

        private ViewFamilyType DraftingType() => new FilteredElementCollector(_doc).OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>().Where(t => t.ViewFamily == ViewFamily.Drafting).OrderBy(t => t.Name).FirstOrDefault();

        private View Template() => new FilteredElementCollector(_doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
            .FirstOrDefault(v => v.IsTemplate && v.Name == _s.PlanTemplateName);

        /// <summary>
        /// The room's plan: the one an earlier run made (found by its tag) or a new one. The crop is
        /// set to the room's outline plus the margin, widened to hold every cut line in
        /// <paramref name="sections"/>. Returns the plan and whether it is new.
        /// </summary>
        public (ViewPlan Plan, bool IsNew) EnsurePlan(RoomCandidate room, RoofJob job, IReadOnlyList<PlannedSection> sections)
        {
            ViewPlan plan = room.ExistingPlanId != 0 ? _doc.GetElement(new ElementId(room.ExistingPlanId)) as ViewPlan : null;
            bool isNew = plan == null;
            if (isNew)
            {
                plan = ViewPlan.Create(_doc, PlanType().Id, new ElementId(job.HostLevelId));
                string name = ViewNames.Unique(
                    ViewNames.Fill(_s.PlanNamePattern, room.Row.Level, room.Row.Number, room.Row.Name), _viewNames);
                plan.Name = name;
                _viewNames.Add(name);

                View template = string.IsNullOrEmpty(_s.PlanTemplateName) ? null : Template();
                if (template != null) plan.ViewTemplateId = template.Id;
                try { plan.Scale = _s.PlanScale; }
                catch (Autodesk.Revit.Exceptions.ApplicationException) { /* the view template controls the scale */ }
            }

            // Crop: outline box + margin, plus the ends of the cut lines so no marker is cut off.
            var points = job.Outer.ToList();
            double margin = _s.CropMarginMm;
            double minX = points.Min(p => p.X) - margin, maxX = points.Max(p => p.X) + margin;
            double minY = points.Min(p => p.Y) - margin, maxY = points.Max(p => p.Y) + margin;
            foreach (PlannedSection section in sections ?? Array.Empty<PlannedSection>())
                foreach (Pt end in new[] { section.Head, section.Tail })
                {
                    minX = Math.Min(minX, end.X - 200); maxX = Math.Max(maxX, end.X + 200);
                    minY = Math.Min(minY, end.Y - 200); maxY = Math.Max(maxY, end.Y + 200);
                }

            BoundingBoxXYZ crop = plan.CropBox;
            Transform toCrop = crop.Transform.Inverse; // a plan set to True North has a rotated crop
            double cMinX = double.MaxValue, cMinY = double.MaxValue, cMaxX = double.MinValue, cMaxY = double.MinValue;
            foreach (double x in new[] { minX, maxX })
                foreach (double y in new[] { minY, maxY })
                {
                    XYZ p = toCrop.OfPoint(new XYZ(x / BoundaryReader.FtToMm, y / BoundaryReader.FtToMm, 0));
                    cMinX = Math.Min(cMinX, p.X); cMaxX = Math.Max(cMaxX, p.X);
                    cMinY = Math.Min(cMinY, p.Y); cMaxY = Math.Max(cMaxY, p.Y);
                }
            crop.Min = new XYZ(cMinX, cMinY, crop.Min.Z);
            crop.Max = new XYZ(cMaxX, cMaxY, crop.Max.Z);
            plan.CropBox = crop;
            plan.CropBoxActive = true;
            plan.CropBoxVisible = true;

            ElementTag.Set(plan, _runId, job.RoomKey, ElementTag.Plan);
            return (plan, isNew);
        }

        /// <summary>The drafting view called <paramref name="name"/>: the existing one, or a new one.</summary>
        public (ElementId Id, string Name) EnsureDrafting(string name)
        {
            name = ViewNames.Clean(name);
            if (name.Length == 0) name = WetAreaSettings.DefaultSharedDraftingViewName;
            if (_drafting.TryGetValue(name, out var known)) return known;

            ViewDrafting view = new FilteredElementCollector(_doc).OfClass(typeof(ViewDrafting)).Cast<ViewDrafting>()
                .FirstOrDefault(v => !v.IsTemplate && string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));
            if (view == null)
            {
                view = ViewDrafting.Create(_doc, DraftingType().Id);
                string unique = ViewNames.Unique(name, _viewNames); // another kind of view may hold the name
                view.Name = unique;
                _viewNames.Add(unique);
                try { view.Scale = _s.DraftingScale; }
                catch (Autodesk.Revit.Exceptions.ApplicationException) { /* keep the default scale */ }
                ElementTag.Set(view, _runId, string.Empty, ElementTag.Drafting);
                DraftingCreated++;
            }
            var result = (view.Id, view.Name);
            _drafting[name] = result;
            return result;
        }

        /// <summary>
        /// Removes the room's markers from an earlier run and places <paramref name="sections"/> on
        /// <paramref name="plan"/>. A section Revit refuses is reported in <paramref name="problems"/>
        /// and the others are still placed.
        /// </summary>
        public List<CreatedSection> PlaceSections(ViewPlan plan, string roomKey, IReadOnlyList<PlannedSection> sections, List<string> problems)
        {
            var old = Markers().Where(m => m.OwnerViewId == plan.Id
                && ElementTag.TryGet(m, out _, out string key, out string kind) && kind == ElementTag.Section && key == roomKey)
                .Select(m => m.Id).ToList();
            if (old.Count > 0) _doc.Delete(old);

            var placed = new List<CreatedSection>();
            double z = plan.GenLevel?.ProjectElevation ?? 0;
            foreach (PlannedSection section in sections)
            {
                try
                {
                    (ElementId draftingId, string draftingName) = EnsureDrafting(section.DraftingViewName);
                    var before = new HashSet<ElementId>(Markers().Select(m => m.Id));

                    // Returns nothing: it creates a marker (category Viewers), found by comparing ids.
                    ViewSection.CreateReferenceSection(_doc, plan.Id, draftingId, ToXyz(section.Head, z), ToXyz(section.Tail, z));

                    Element marker = Markers().FirstOrDefault(m => !before.Contains(m.Id));
                    if (marker == null)
                    {
                        problems.Add($"{section.Label}: Revit did not create the section marker.");
                        continue;
                    }
                    ElementTag.Set(marker, _runId, roomKey, ElementTag.Section);
                    placed.Add(new CreatedSection(section, marker.Id.Value, draftingId.Value, draftingName));
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    problems.Add($"{section.Label}: {ex.Message}");
                }
            }
            return placed;
        }

        private IEnumerable<Element> Markers()
            => new FilteredElementCollector(_doc).OfCategory(BuiltInCategory.OST_Viewers).WhereElementIsNotElementType();

        private static XYZ ToXyz(Pt p, double z) => new(p.X / BoundaryReader.FtToMm, p.Y / BoundaryReader.FtToMm, z);
    }
}
