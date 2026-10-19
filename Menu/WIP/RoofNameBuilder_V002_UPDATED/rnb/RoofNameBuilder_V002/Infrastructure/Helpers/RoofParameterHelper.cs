#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin.RoofNameBuilder.V002.Core.Models;

namespace Revit26_Plugin.RoofNameBuilder.V002.Infrastructure.Helpers
{
    /// <summary>
    /// All Revit-API-dependent reads for this tool live here — ARCHITECTURE.md §3:
    /// Infrastructure/ -> Core/, Revit API; Core/ itself stays Revit-free.
    ///
    /// V002 changes vs V001:
    /// - ElementId.Value (long) instead of IntegerValue, which the Revit 2026 API no longer has.
    /// - Roof instances collected by category (OST_Roofs) rather than OfClass(RoofBase).
    /// - A code's source parameter is looked up on the instance first, then on its type.
    /// - Length/slope values are read as plain numbers in project display units (no "mm" / "°"
    ///   symbol), so they can go straight into a name.
    /// - Active-view scope with no usable active view returns no rows instead of throwing.
    /// </summary>
    public static class RoofParameterHelper
    {
        public static List<RoofNameRow> CollectTypeRows(
            Document doc, RoofNameScope scope, ICollection<ElementId> currentSelection,
            IList<NameCodeDefinition> codeDefs)
        {
            var instances = CollectRoofInstances(doc, RoofNameScope.AllInProject, currentSelection);
            var usageCounts = instances
                .GroupBy(r => r.GetTypeId())
                .ToDictionary(g => g.Key, g => g.Count());

            IEnumerable<RoofType> roofTypes = scope switch
            {
                RoofNameScope.CurrentSelection or RoofNameScope.ActiveView =>
                    CollectRoofInstances(doc, scope, currentSelection)
                        .Select(r => doc.GetElement(r.GetTypeId()) as RoofType)
                        .Where(t => t != null)
                        .Cast<RoofType>(),

                _ => new FilteredElementCollector(doc)
                    .OfClass(typeof(RoofType))
                    .Cast<RoofType>()
            };

            roofTypes = roofTypes
                .GroupBy(t => t.Id.Value)
                .Select(g => g.First());

            if (scope == RoofNameScope.UsedTypesOnly)
                roofTypes = roofTypes.Where(t => usageCounts.TryGetValue(t.Id, out var n) && n > 0);

            var rows = new List<RoofNameRow>();
            foreach (var type in roofTypes.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
            {
                var row = new RoofNameRow
                {
                    ElementId = type.Id.Value,
                    CurrentName = type.Name,
                    BaseName = type.Name,
                    TypeName = type.Name,
                    InstanceCount = usageCounts.TryGetValue(type.Id, out var c) ? c : 0
                };
                PopulateCodes(row, type, null, codeDefs);
                rows.Add(row);
            }
            return rows;
        }

        public static List<RoofNameRow> CollectInstanceRows(
            Document doc, RoofNameScope scope, ICollection<ElementId> currentSelection,
            IList<NameCodeDefinition> codeDefs, string instanceTargetParameter)
        {
            var rows = new List<RoofNameRow>();
            foreach (var roof in CollectRoofInstances(doc, scope, currentSelection))
            {
                var current = roof.LookupParameter(instanceTargetParameter)?.AsString() ?? string.Empty;
                var type = doc.GetElement(roof.GetTypeId());

                var row = new RoofNameRow
                {
                    ElementId = roof.Id.Value,
                    CurrentName = current,
                    BaseName = current,
                    TypeName = type?.Name ?? string.Empty
                };
                PopulateCodes(row, roof, type, codeDefs);
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>
        /// Every RoofType name in the document — the Types-mode duplicate check uses this so a new
        /// name can't silently collide with a type that sits outside the current scope.
        /// </summary>
        public static List<string> CollectAllRoofTypeNames(Document doc) =>
            new FilteredElementCollector(doc)
                .OfClass(typeof(RoofType))
                .Select(t => t.Name)
                .ToList();

        /// <summary>True when the instance target parameter exists and is a writable text parameter on at least one roof.</summary>
        public static bool IsWritableTextParameter(Document doc, string parameterName)
        {
            var roof = CollectRoofInstances(doc, RoofNameScope.AllInProject, Array.Empty<ElementId>()).FirstOrDefault();
            var p = roof?.LookupParameter(parameterName);
            return p == null || (p.StorageType == StorageType.String && !p.IsReadOnly);
        }

        private static IEnumerable<RoofBase> CollectRoofInstances(
            Document doc, RoofNameScope scope, ICollection<ElementId> currentSelection)
        {
            switch (scope)
            {
                case RoofNameScope.CurrentSelection:
                    return currentSelection
                        .Select(id => doc.GetElement(id))
                        .OfType<RoofBase>()
                        .ToList();

                case RoofNameScope.ActiveView:
                    var view = doc.ActiveView;
                    if (view == null || view.IsTemplate) return Enumerable.Empty<RoofBase>();
                    try
                    {
                        return new FilteredElementCollector(doc, view.Id)
                            .OfCategory(BuiltInCategory.OST_Roofs)
                            .WhereElementIsNotElementType()
                            .OfType<RoofBase>()
                            .ToList();
                    }
                    catch (Autodesk.Revit.Exceptions.ArgumentException)
                    {
                        // Views that can't host a view-scoped collector (schedules, browser views…).
                        return Enumerable.Empty<RoofBase>();
                    }

                default:
                    return new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Roofs)
                        .WhereElementIsNotElementType()
                        .OfType<RoofBase>()
                        .ToList();
            }
        }

        private static void PopulateCodes(RoofNameRow row, Element source, Element? fallback, IList<NameCodeDefinition> codeDefs)
        {
            foreach (var def in codeDefs)
            {
                if (string.IsNullOrWhiteSpace(def.Code)) continue;

                var param = string.IsNullOrWhiteSpace(def.SourceParameter)
                    ? null
                    : source.LookupParameter(def.SourceParameter) ?? fallback?.LookupParameter(def.SourceParameter);

                row.Codes[def.Code] = new RoofCodeValue { Ticked = false, Value = ReadValue(param) };
            }
        }

        private static string ReadValue(Parameter? param)
        {
            if (param == null || !param.HasValue) return string.Empty;

            switch (param.StorageType)
            {
                case StorageType.Double:
                    try
                    {
                        // Plain number in the project's display units (e.g. 50 for 50 mm) — no unit symbol.
                        var unit = param.GetUnitTypeId();
                        var display = UnitUtils.ConvertFromInternalUnits(param.AsDouble(), unit);
                        return Math.Round(display, 2).ToString("0.##", CultureInfo.InvariantCulture);
                    }
                    catch (Exception)
                    {
                        // Unitless/number specs have no unit type id.
                        return param.AsValueString() ?? param.AsDouble().ToString("0.##", CultureInfo.InvariantCulture);
                    }

                case StorageType.Integer:
                    return param.AsValueString() ?? param.AsInteger().ToString(CultureInfo.InvariantCulture);

                case StorageType.String:
                    return param.AsString() ?? string.Empty;

                default:
                    // ElementId parameters (e.g. a material) — AsValueString gives the element's name.
                    return param.AsValueString() ?? string.Empty;
            }
        }
    }
}
