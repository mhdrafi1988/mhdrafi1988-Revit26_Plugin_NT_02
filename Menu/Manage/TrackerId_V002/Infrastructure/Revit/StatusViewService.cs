using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.TrackerId.V002.Core.Models;
using Revit26_Plugin_Revised.TrackerId.V002.Core.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Revit26_Plugin_Revised.TrackerId.V002.Infrastructure.Revit
{
    /// <summary>Outcome of colouring a view.</summary>
    public sealed class ColourResult
    {
        /// <summary>True when the transaction was committed.</summary>
        public bool Committed { get; set; }

        /// <summary>The view coloured (open it afterwards).</summary>
        public long ViewId { get; set; }

        /// <summary>Its name.</summary>
        public string ViewName { get; set; } = string.Empty;

        /// <summary>Filters created or updated.</summary>
        public int Filters { get; set; }

        /// <summary>True when the "Tracker – Status" 3D view was created now.</summary>
        public bool ViewCreated { get; set; }

        /// <summary>Why nothing was changed, when <see cref="Committed"/> is false.</summary>
        public List<string> Errors { get; } = new();
    }

    /// <summary>
    /// Colours a view by Tracker_Status: one view filter per status ("Tracker Status - Installed" …)
    /// plus one for elements with no status, each with a solid-fill colour override. Filters are reused
    /// by name, so running it again just updates rules, categories and colours.
    /// </summary>
    public static class StatusViewService
    {
        /// <summary>Name of the 3D view the tool creates.</summary>
        public const string ViewName = "Tracker - Status";

        private const string FilterPrefix = "Tracker Status - ";
        private const string NoStatusName = FilterPrefix + "(none)";

        /// <summary>
        /// Colours <paramref name="activeView"/>, or the "Tracker - Status" 3D view (created when missing)
        /// when <paramref name="use3DView"/> is set. Must run with no transaction open.
        /// </summary>
        public static ColourResult Apply(Document doc, View activeView, IReadOnlyList<StatusColour> colours, bool use3DView)
        {
            var result = new ColourResult();

            var (definition, _) = TrackerParameterService.FindBinding(doc, TrackerParameters.Status);
            if (definition is not InternalDefinition statusDefinition)
            {
                result.Errors.Add("Tracker_Status is not in the project yet. Import a status file first.");
                return result;
            }

            var filterable = new HashSet<long>(ParameterFilterUtilities.GetAllFilterableCategories().Select(id => id.Value));
            var categoryIds = TrackerParameterService.BoundCategories(doc, TrackerParameters.Status)
                .Where(c => filterable.Contains(c.Id.Value)).Select(c => c.Id).ToList();
            if (categoryIds.Count == 0)
            {
                result.Errors.Add("Tracker_Status is not bound to any category that view filters can use.");
                return result;
            }

            var solid = new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
                .FirstOrDefault(f => f.GetFillPattern().IsSolidFill);

            using var transaction = new Transaction(doc, "Tracker ID: colour by status");
            transaction.Start();

            View view = use3DView ? GetOr3DView(doc, result) : activeView;
            if (view == null)
            {
                result.Errors.Add("No view to colour. Open a plan, section or 3D view, or use the Tracker 3D view.");
                transaction.RollBack();
                return result;
            }

            string problem = WhyNotColourable(doc, view);
            if (problem != null)
            {
                result.Errors.Add(problem);
                transaction.RollBack();
                return result;
            }

            var existing = new FilteredElementCollector(doc).OfClass(typeof(ParameterFilterElement)).Cast<ParameterFilterElement>()
                .ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var colour in colours.Where(c => !string.IsNullOrWhiteSpace(c.Status)))
            {
                var rule = ParameterFilterRuleFactory.CreateEqualsRule(statusDefinition.Id, colour.Status.Trim());
                var filter = Upsert(doc, existing, FilterPrefix + SafeName(colour.Status), categoryIds, new ElementParameterFilter(rule));
                Show(view, filter.Id, colour.Color, solid);
                result.Filters++;
            }

            var none = Upsert(doc, existing, NoStatusName, categoryIds,
                new ElementParameterFilter(ParameterFilterRuleFactory.CreateHasNoValueParameterRule(statusDefinition.Id)));
            Show(view, none.Id, StatusColours.NoStatusColor, solid);
            result.Filters++;

            result.ViewId = view.Id.Value;
            result.ViewName = view.Name;
            result.Committed = transaction.Commit() == TransactionStatus.Committed;
            if (!result.Committed) result.Errors.Add("Revit did not accept the change.");
            return result;
        }

        /// <summary>Why <paramref name="view"/> cannot take filters, or null when it can.</summary>
        public static string WhyNotColourable(Document doc, View view)
        {
            if (view.IsTemplate) return "The active view is a view template. Open a normal view.";
            if (!view.AreGraphicsOverridesAllowed()) return $"View '{view.Name}' does not allow graphic overrides. Open a plan, section or 3D view.";

            if (view.ViewTemplateId != ElementId.InvalidElementId && doc.GetElement(view.ViewTemplateId) is View template &&
                template.GetTemplateParameterIds().Contains(new ElementId(BuiltInParameter.VIS_GRAPHICS_FILTERS)))
                return $"View '{view.Name}' gets its filters from view template '{template.Name}'. Use the Tracker 3D view, or remove the template from the view.";
            return null;
        }

        private static View GetOr3DView(Document doc, ColourResult result)
        {
            var view = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate && v.Name == ViewName);
            if (view != null) return view;

            var type = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .FirstOrDefault(t => t.ViewFamily == ViewFamily.ThreeDimensional);
            if (type == null) return null;

            view = View3D.CreateIsometric(doc, type.Id);
            view.ViewTemplateId = ElementId.InvalidElementId;
            view.Name = ViewName;
            view.DisplayStyle = DisplayStyle.Shading;
            result.ViewCreated = true;
            return view;
        }

        private static ParameterFilterElement Upsert(Document doc, Dictionary<string, ParameterFilterElement> existing,
            string name, IList<ElementId> categoryIds, ElementFilter filter)
        {
            if (existing.TryGetValue(name, out var found))
            {
                found.SetCategories(categoryIds);
                found.SetElementFilter(filter);
                return found;
            }
            var created = ParameterFilterElement.Create(doc, name, categoryIds, filter);
            existing[name] = created;
            return created;
        }

        private static void Show(View view, ElementId filterId, string hex, FillPatternElement solid)
        {
            if (!view.GetFilters().Contains(filterId)) view.AddFilter(filterId);
            view.SetFilterVisibility(filterId, true);

            StatusColours.TryParse(hex, out byte r, out byte g, out byte b);
            var color = new Color(r, g, b);
            var overrides = new OverrideGraphicSettings();
            if (solid != null)
            {
                overrides.SetSurfaceForegroundPatternId(solid.Id);
                overrides.SetCutForegroundPatternId(solid.Id);
            }
            overrides.SetSurfaceForegroundPatternColor(color);
            overrides.SetCutForegroundPatternColor(color);
            view.SetFilterOverrides(filterId, overrides);
        }

        // Revit refuses these characters in filter names.
        private static string SafeName(string status) => Regex.Replace(status.Trim(), @"[\\:{}\[\]|;<>?`~]", "_");
    }
}
