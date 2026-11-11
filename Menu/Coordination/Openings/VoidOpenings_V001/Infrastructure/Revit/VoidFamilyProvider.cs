using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Dtos;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Infrastructure.Revit
{
    /// <summary>
    /// Supplies the family symbol for each opening. Order of preference: the custom family named in the
    /// settings, the built-in parametric family (built and loaded on first use), and, if the parametric
    /// build does not pass its own check, one fixed-size family per opening size.
    /// </summary>
    public sealed class VoidFamilyProvider
    {
        private readonly Document _document;
        private readonly VoidOpeningsSettings _settings;
        private readonly ICollection<string> _notes;
        private readonly Dictionary<string, FamilySymbol> _symbols = new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<bool, bool> _fixedSize = new Dictionary<bool, bool>();

        /// <summary>Folder the built families are saved in: %AppData%\Revit26_Plugin_Revised\VoidOpenings\Families.</summary>
        public static string FamilyFolder { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Revit26_Plugin_Revised", "VoidOpenings", "Families");

        /// <summary>Creates a provider for one Generate run.</summary>
        public VoidFamilyProvider(Document document, VoidOpeningsSettings settings, ICollection<string> notes)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _notes = notes;
        }

        /// <summary>
        /// Makes sure every family the placements need is loaded. Call with no transaction open: building
        /// a family opens a family document, and loading runs its own transaction.
        /// </summary>
        public void Prepare(IEnumerable<VoidPlacement> placements)
        {
            foreach (VoidPlacement placement in placements)
            {
                if (_symbols.ContainsKey(KeyOf(placement)))
                    continue;

                // Resolving can switch a shape to fixed-size families, which changes the key, so the key
                // is read again after it.
                FamilySymbol symbol = Resolve(placement);
                _symbols[KeyOf(placement)] = symbol;
            }
        }

        /// <summary>The symbol for a placement; <see cref="Prepare"/> must have run for it.</summary>
        public FamilySymbol SymbolFor(VoidPlacement placement) => _symbols[KeyOf(placement)];

        private static bool IsRound(VoidPlacement placement)
            => string.Equals(placement.Shape, nameof(OpeningShape.Circular), StringComparison.OrdinalIgnoreCase);

        private string KeyOf(VoidPlacement placement)
        {
            bool round = IsRound(placement);
            return _fixedSize.TryGetValue(round, out bool isFixed) && isFixed ? FixedName(placement) : (round ? "round" : "rect");
        }

        private FamilySymbol Resolve(VoidPlacement placement)
        {
            bool round = IsRound(placement);

            string custom = round ? _settings.CustomRoundFamily : _settings.CustomRectFamily;
            if (!string.IsNullOrWhiteSpace(custom))
                return FirstSymbol(FindFamily(custom)
                    ?? throw new InvalidOperationException($"The custom void family '{custom}' is not loaded in this model. Load it, or switch back to the built-in family."));

            string name = round ? VoidFamilySpec.RoundFamily : VoidFamilySpec.RectFamily;
            if (!(_fixedSize.TryGetValue(round, out bool isFixed) && isFixed))
            {
                Family loaded = FindFamily(name);
                if (loaded != null)
                    return FirstSymbol(loaded);

                var size = new VoidFamilyBuilder.Size(placement.WidthMm, placement.HeightMm, placement.DiameterMm, placement.DepthMm);
                string path = Path.Combine(FamilyFolder, name + ".rfa");
                if (VoidFamilyBuilder.TryBuild(_document.Application, round, true, size, path, out string problem))
                {
                    _notes?.Add($"Built and loaded the void family {name}.");
                    return FirstSymbol(Load(path, name));
                }

                _fixedSize[round] = true;
                _notes?.Add($"The parametric {(round ? "circular" : "rectangular")} void family could not be built ({problem}). " +
                            "Using one fixed-size family per opening size instead.");
            }

            string fixedName = FixedName(placement);
            Family fixedFamily = FindFamily(fixedName);
            if (fixedFamily == null)
            {
                var size = new VoidFamilyBuilder.Size(placement.WidthMm, placement.HeightMm, placement.DiameterMm, placement.DepthMm);
                string path = Path.Combine(FamilyFolder, fixedName + ".rfa");
                if (!VoidFamilyBuilder.TryBuild(_document.Application, round, false, size, path, out string problem))
                    throw new InvalidOperationException($"The void family could not be built: {problem}");
                fixedFamily = Load(path, fixedName);
            }
            return FirstSymbol(fixedFamily);
        }

        private static string FixedName(VoidPlacement p)
        {
            string N(double mm) => Math.Round(mm).ToString(CultureInfo.InvariantCulture);
            return IsRound(p)
                ? $"{VoidFamilySpec.RoundFamily}_D{N(p.DiameterMm)}_L{N(p.DepthMm)}"
                : $"{VoidFamilySpec.RectFamily}_W{N(p.WidthMm)}_H{N(p.HeightMm)}_L{N(p.DepthMm)}";
        }

        private Family FindFamily(string name)
            => new FilteredElementCollector(_document).OfClass(typeof(Family)).Cast<Family>()
                .FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

        private Family Load(string path, string name)
        {
            Family family;
            using (var transaction = new Transaction(_document, "Void Openings: load void family"))
            {
                transaction.Start();
                _document.LoadFamily(path, new KeepExisting(), out family);
                transaction.Commit();
            }
            return family ?? FindFamily(name)
                ?? throw new InvalidOperationException($"Revit did not load the void family {name} from {path}.");
        }

        private FamilySymbol FirstSymbol(Family family)
        {
            ElementId id = family.GetFamilySymbolIds().FirstOrDefault();
            return (id == null ? null : _document.GetElement(id) as FamilySymbol)
                ?? throw new InvalidOperationException($"The family {family.Name} has no type to place.");
        }

        private sealed class KeepExisting : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = false;
                return true;
            }

            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Family;
                overwriteParameterValues = false;
                return true;
            }
        }
    }
}
