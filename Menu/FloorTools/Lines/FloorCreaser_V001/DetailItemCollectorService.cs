// ==================================
// File: DetailItemCollectorService.cs
// Namespace: Revit26_Plugin_Revised.FloorCreaser.V001.Services
// ==================================

using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.FloorCreaser.V001.Services
{
    /// <summary>
    /// Collects all line-based detail component <see cref="FamilySymbol"/>s
    /// available in the document, sorted by family then type name.
    ///
    /// V010: "line-based" is now decided by the family's placement type
    /// (<see cref="FamilyPlacementType.CurveBasedDetail"/>), which is exactly
    /// what <c>NewFamilyInstance(Line, FamilySymbol, View)</c> requires. V009
    /// tested <c>Family.IsParametric</c> plus the presence of a length
    /// parameter on the type — indirect proxies that can hide a valid
    /// line-based family depending on how it was authored.
    /// </summary>
    public class DetailItemCollectorService
    {
        private readonly Document       _doc;
        private readonly LoggingService _log;

        public DetailItemCollectorService(Document doc, LoggingService log)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public IList<FamilySymbol> Collect()
        {
            _log.Section("Detail item symbols");

            List<FamilySymbol> all =
                new FilteredElementCollector(_doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_DetailComponents)
                    .Cast<FamilySymbol>()
                    .ToList();

            var lineBased = new List<FamilySymbol>();
            var skippedByFamily = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (FamilySymbol s in all)
            {
                FamilyPlacementType? placement = GetPlacementType(s);

                if (placement == FamilyPlacementType.CurveBasedDetail)
                {
                    lineBased.Add(s);
                    continue;
                }

                string familyName = s.FamilyName ?? "<unknown family>";
                skippedByFamily[familyName] = placement?.ToString() ?? "unreadable";
            }

            lineBased = lineBased
                .OrderBy(s => s.FamilyName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _log.Info($"Detail component types in project: {all.Count}  |  line-based (usable): {lineBased.Count}  |  " +
                      $"skipped: {all.Count - lineBased.Count} in {skippedByFamily.Count} family(ies)");

            foreach (var kv in skippedByFamily)
                _log.Debug($"  Skipped family '{kv.Key}' — placement type {kv.Value} (not line-based).");

            foreach (FamilySymbol s in lineBased)
                _log.Debug($"  Usable: {s.FamilyName} : {s.Name}  (id {s.Id.Value}, {(s.IsActive ? "active" : "not yet active")})");

            if (lineBased.Count == 0)
                _log.Warning("No line-based detail component families are loaded in this project. " +
                             "Load one (Insert ▸ Load Family ▸ Detail Items) and reopen the tool — nothing can be placed without it.");

            return lineBased;
        }

        private FamilyPlacementType? GetPlacementType(FamilySymbol symbol)
        {
            try
            {
                return symbol?.Family?.FamilyPlacementType;
            }
            catch (Exception ex)
            {
                _log.Debug($"  Could not read placement type of '{symbol?.FamilyName}' : {ex.Message}");
                return null;
            }
        }
    }
}
