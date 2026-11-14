using System;
using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Rules
{
    /// <summary>How a clearance value is applied to the MEP size.</summary>
    public enum ClearanceMode
    {
        /// <summary>Added on each side: opening = size + 2 × clearance.</summary>
        PerSide,
        /// <summary>Added once: opening = size + clearance.</summary>
        Total,
    }

    /// <summary>Which opening shape a rule asks for.</summary>
    public enum OpeningShapePolicy
    {
        /// <summary>Circular for round MEP, rectangular for rectangular MEP.</summary>
        Auto,
        /// <summary>Always circular.</summary>
        Circular,
        /// <summary>Always rectangular.</summary>
        Rectangular,
    }

    /// <summary>One user-defined clearance: an MEP category, a size range and the offset to apply.</summary>
    public sealed class ClearanceRule
    {
        /// <summary>MEP category the rule is for.</summary>
        public MepCategory Category { get; }

        /// <summary>Smallest nominal size the rule covers, inclusive.</summary>
        public double FromMm { get; }

        /// <summary>Largest nominal size the rule covers, inclusive.</summary>
        public double ToMm { get; }

        /// <summary>Clearance to apply.</summary>
        public double ClearanceMm { get; }

        /// <summary>Per side or total.</summary>
        public ClearanceMode Mode { get; }

        /// <summary>Opening shape the rule asks for.</summary>
        public OpeningShapePolicy Shape { get; }

        /// <summary>Creates a rule.</summary>
        public ClearanceRule(MepCategory category, double fromMm, double toMm, double clearanceMm,
            ClearanceMode mode, OpeningShapePolicy shape = OpeningShapePolicy.Auto)
        {
            Category = category;
            FromMm = fromMm;
            ToMm = toMm;
            ClearanceMm = clearanceMm;
            Mode = mode;
            Shape = shape;
        }

        /// <summary>True when the rule covers this nominal size.</summary>
        public bool Covers(double sizeMm) => sizeMm >= FromMm && sizeMm <= ToMm;
    }

    /// <summary>The user's clearance rules plus the default used when no rule fits.</summary>
    public sealed class ClearanceRuleSet
    {
        private readonly List<ClearanceRule> _rules;

        /// <summary>The rules, in the order the user listed them.</summary>
        public IReadOnlyList<ClearanceRule> Rules => _rules;

        /// <summary>Clearance used when no rule covers the element.</summary>
        public double DefaultClearanceMm { get; }

        /// <summary>How the default clearance is applied.</summary>
        public ClearanceMode DefaultMode { get; }

        /// <summary>Creates a rule set.</summary>
        public ClearanceRuleSet(IEnumerable<ClearanceRule> rules, double defaultClearanceMm, ClearanceMode defaultMode)
        {
            _rules = (rules ?? Enumerable.Empty<ClearanceRule>()).ToList();
            if (defaultClearanceMm < 0) throw new ArgumentOutOfRangeException(nameof(defaultClearanceMm));
            DefaultClearanceMm = defaultClearanceMm;
            DefaultMode = defaultMode;
        }

        /// <summary>
        /// The rule for an element: the first rule of its category that covers the size; for a fitting
        /// or accessory with no rule of its own, the first matching rule of its base category (pipe,
        /// duct, cable tray, conduit); otherwise a rule made from the default.
        /// </summary>
        public ClearanceRule Resolve(MepCategory category, double nominalSizeMm)
        {
            ClearanceRule rule = _rules.FirstOrDefault(r => r.Category == category && r.Covers(nominalSizeMm));
            if (rule != null)
                return rule;

            MepCategory baseCategory = BaseCategoryOf(category);
            if (baseCategory != category)
            {
                rule = _rules.FirstOrDefault(r => r.Category == baseCategory && r.Covers(nominalSizeMm));
                if (rule != null)
                    return rule;
            }

            return new ClearanceRule(category, 0, double.MaxValue, DefaultClearanceMm, DefaultMode);
        }

        /// <summary>The run category a fitting or accessory belongs with; the category itself otherwise.</summary>
        public static MepCategory BaseCategoryOf(MepCategory category)
        {
            switch (category)
            {
                case MepCategory.PipeFitting:
                case MepCategory.PipeAccessory:
                    return MepCategory.Pipe;
                case MepCategory.DuctFitting:
                case MepCategory.DuctAccessory:
                    return MepCategory.Duct;
                case MepCategory.CableTrayFitting:
                    return MepCategory.CableTray;
                case MepCategory.ConduitFitting:
                    return MepCategory.Conduit;
                default:
                    return category;
            }
        }
    }
}
