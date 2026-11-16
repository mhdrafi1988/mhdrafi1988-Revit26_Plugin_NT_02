using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Rules
{
    /// <summary>
    /// Everything the user sets on the OPENING CLEARANCE card: the clearance rules, the default
    /// used when no rule fits, and the run-wide sizing options. This is what the settings file holds.
    /// </summary>
    public sealed class ClearanceSetup
    {
        /// <summary>The rules, in the order the user listed them. The first rule that fits is used.</summary>
        public IReadOnlyList<ClearanceRule> Rules { get; }

        /// <summary>Clearance used when no rule covers the element.</summary>
        public double DefaultClearanceMm { get; }

        /// <summary>How the default clearance is applied.</summary>
        public ClearanceMode DefaultMode { get; }

        /// <summary>Run-wide sizing options (overshoot, rounding, insulation, axis).</summary>
        public OpeningSettings Options { get; }

        /// <summary>Creates a setup.</summary>
        public ClearanceSetup(IEnumerable<ClearanceRule> rules, double defaultClearanceMm, ClearanceMode defaultMode, OpeningSettings options)
        {
            if (defaultClearanceMm < 0) throw new ArgumentOutOfRangeException(nameof(defaultClearanceMm));
            Rules = (rules ?? Enumerable.Empty<ClearanceRule>()).ToList();
            DefaultClearanceMm = defaultClearanceMm;
            DefaultMode = defaultMode;
            Options = options ?? new OpeningSettings();
        }

        /// <summary>
        /// The setup of a first run: no rules, 50 mm on each side for everything, 50 mm overshoot,
        /// no rounding, insulation included, openings square to the structure.
        /// </summary>
        public static ClearanceSetup CreateDefault()
            => new ClearanceSetup(null, 50, ClearanceMode.PerSide, new OpeningSettings());

        /// <summary>The rules and default in the form the opening calculator takes.</summary>
        public ClearanceRuleSet ToRuleSet() => new ClearanceRuleSet(Rules, DefaultClearanceMm, DefaultMode);
    }
}
