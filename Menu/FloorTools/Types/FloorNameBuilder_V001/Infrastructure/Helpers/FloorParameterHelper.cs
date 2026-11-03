#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.FloorNameBuilder.V001.Core.Models;

namespace Revit26_Plugin_Revised.FloorNameBuilder.V001.Infrastructure.Helpers
{
    /// <summary>
    /// All Revit-API-dependent reads for this tool live here — ARCHITECTURE.md §3:
    /// Infrastructure/ -> Core/, Revit API; Core/ itself stays Revit-free.
    ///
    /// V002 changes vs V001:
    /// - ElementId.Value (long) instead of IntegerValue, which the Revit 2026 API no longer has.
    /// - Floor instances collected by category (OST_Floors) rather than OfClass(Floor).
    /// - A code's source parameter is looked up on the instance first, then on its type.
    /// - Length/slope values are read as plain numbers in project display units (no "mm" / "°"
    ///   symbol), so they can go straight into a name.
    /// - Active-view scope with no usable active view returns no rows instead of throwing.
    /// </summary>
    public static class FloorParameterHelper
    {
        public static List<FloorNameRow> CollectTypeRows(
            Document doc, FloorNameScope scope, ICollection<ElementId> currentSelection,
            IList<NameCodeDefinition> codeDefs)
        {
            var instances = CollectFloorInstances(doc, FloorNameScope.AllInProject, currentSelection);
            var usageCounts = instances
                .GroupBy(r => r.GetTypeId())
                .ToDictionary(g => g.Key, g => g.Count());

            IEnumerable<FloorType> floorTypes = scope switch
            {
                FloorNameScope.CurrentSelection or FloorNameScope.ActiveView =>
                    CollectFloorInstances(doc, scope, currentSelection)
                        .Select(r => doc.GetElement(r.GetTypeId()) as FloorType)
                        .Where(t => t != null)
                        .Cast<FloorType>(),

                _ => new FilteredElementCollector(doc)
                    .OfClass(typeof(FloorType))
                    .Cast<FloorType>()
            };

            floorTypes = floorTypes
                .GroupBy(t => t.Id.Value)
                .Select(g => g.First());

            if (scope == FloorNameScope.UsedTypesOnly)
                floorTypes = floorTypes.Where(t => usageCounts.TryGetValue(t.Id, out var n) && n > 0);

            var rows = new List<FloorNameRow>();
            foreach (var type in floorTypes.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
            {
                var row = new FloorNameRow
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

        public static List<FloorNameRow> CollectInstanceRows(
            Document doc, FloorNameScope scope, ICollection<ElementId> currentSelection,
            IList<NameCodeDefinition> codeDefs, string instanceTargetParameter)
        {
            var rows = new List<FloorNameRow>();
            foreach (var floor in CollectFloorInstances(doc, scope, currentSelection))
            {
                var current = floor.LookupParameter(instanceTargetParameter)?.AsString() ?? string.Empty;
                var type = doc.GetElement(floor.GetTypeId());

                var row = new FloorNameRow
                {
                    ElementId = floor.Id.Value,
                    CurrentName = current,
                    BaseName = current,
                    TypeName = type?.Name ?? string.Empty
                };
                PopulateCodes(row, floor, type, codeDefs);
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>
        /// Every FloorType name in the document — the Types-mode duplicate check uses this so a new
        /// name can't silently collide with a type that sits outside the current scope.
        /// </summary>
        public static List<string> CollectAllFloorTypeNames(Document doc) =>
            new FilteredElementCollector(doc)
                .OfClass(typeof(FloorType))
                .Select(t => t.Name)
                .ToList();

        /// <summary>True when the instance target parameter exists and is a writable text parameter on at least one floor.</summary>
        public static bool IsWritableTextParameter(Document doc, string parameterName)
        {
            var floor = CollectFloorInstances(doc, FloorNameScope.AllInProject, Array.Empty<ElementId>()).FirstOrDefault();
            var p = floor?.LookupParameter(parameterName);
            return p == null || (p.StorageType == StorageType.String && !p.IsReadOnly);
        }

        private static IEnumerable<Floor> CollectFloorInstances(
            Document doc, FloorNameScope scope, ICollection<ElementId> currentSelection)
        {
            switch (scope)
            {
                case FloorNameScope.CurrentSelection:
                    return currentSelection
                        .Select(id => doc.GetElement(id))
                        .OfType<Floor>()
                        .ToList();

                case FloorNameScope.ActiveView:
                    var view = doc.ActiveView;
                    if (view == null || view.IsTemplate) return Enumerable.Empty<Floor>();
                    try
                    {
                        return new FilteredElementCollector(doc, view.Id)
                            .OfCategory(BuiltInCategory.OST_Floors)
                            .WhereElementIsNotElementType()
                            .OfType<Floor>()
                            .ToList();
                    }
                    catch (Autodesk.Revit.Exceptions.ArgumentException)
                    {
                        // Views that can't host a view-scoped collector (schedules, browser views…).
                        return Enumerable.Empty<Floor>();
                    }

                default:
                    return new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Floors)
                        .WhereElementIsNotElementType()
                        .OfType<Floor>()
                        .ToList();
            }
        }

        private static void PopulateCodes(FloorNameRow row, Element source, Element? fallback, IList<NameCodeDefinition> codeDefs)
        {
            foreach (var def in codeDefs)
            {
                if (string.IsNullOrWhiteSpace(def.Code)) continue;

                var param = string.IsNullOrWhiteSpace(def.SourceParameter)
                    ? null
                    : source.LookupParameter(def.SourceParameter) ?? fallback?.LookupParameter(def.SourceParameter);

                row.Codes[def.Code] = new FloorCodeValue { Ticked = false, Value = ReadValue(param) };
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
