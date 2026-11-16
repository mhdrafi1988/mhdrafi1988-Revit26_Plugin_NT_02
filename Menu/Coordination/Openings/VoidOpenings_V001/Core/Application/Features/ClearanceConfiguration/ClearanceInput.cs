using System;
using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Rules;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ClearanceConfiguration
{
    /// <summary>One rule row as typed: the numbers are still text and may be wrong or empty.</summary>
    public sealed class ClearanceRuleInput
    {
        /// <summary>MEP category the rule is for.</summary>
        public MepCategory Category { get; set; } = MepCategory.Pipe;

        /// <summary>Smallest size covered; empty means 0.</summary>
        public string FromText { get; set; } = string.Empty;

        /// <summary>Largest size covered; empty means no upper limit.</summary>
        public string ToText { get; set; } = string.Empty;

        /// <summary>Clearance to apply; required.</summary>
        public string ClearanceText { get; set; } = string.Empty;

        /// <summary>Each side or total.</summary>
        public ClearanceMode Mode { get; set; } = ClearanceMode.PerSide;

        /// <summary>Opening shape the rule asks for.</summary>
        public OpeningShapePolicy Shape { get; set; } = OpeningShapePolicy.Auto;

        /// <summary>The row that shows a valid rule.</summary>
        public static ClearanceRuleInput From(ClearanceRule rule)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            return new ClearanceRuleInput
            {
                Category = rule.Category,
                FromText = MmText.Format(rule.FromMm),
                ToText = rule.HasUpperLimit ? MmText.Format(rule.ToMm) : string.Empty,
                ClearanceText = MmText.Format(rule.ClearanceMm),
                Mode = rule.Mode,
                Shape = rule.Shape,
            };
        }
    }

    /// <summary>The OPENING CLEARANCE card as typed, before validation.</summary>
    public sealed class ClearanceInput
    {
        /// <summary>The rule rows, top to bottom.</summary>
        public List<ClearanceRuleInput> Rules { get; } = new List<ClearanceRuleInput>();

        /// <summary>Default offset; required.</summary>
        public string DefaultClearanceText { get; set; } = string.Empty;

        /// <summary>How the default offset is applied.</summary>
        public ClearanceMode DefaultMode { get; set; } = ClearanceMode.PerSide;

        /// <summary>Overshoot on each side of the structure; required.</summary>
        public string OvershootText { get; set; } = string.Empty;

        /// <summary>Rounding module; empty or 0 turns rounding off.</summary>
        public string RoundUpToText { get; set; } = string.Empty;

        /// <summary>True to add insulation thickness to the MEP size.</summary>
        public bool IncludeInsulation { get; set; } = true;

        /// <summary>Which way openings point.</summary>
        public OpeningAxisMode AxisMode { get; set; } = OpeningAxisMode.PerpendicularToStructure;

        /// <summary>Not on the card; carried through unchanged so a save does not lose it.</summary>
        public double MaxCircularAngleDeg { get; set; } = 15;

        /// <summary>The card content that shows a valid setup.</summary>
        public static ClearanceInput From(ClearanceSetup setup)
        {
            if (setup == null) throw new ArgumentNullException(nameof(setup));
            var input = new ClearanceInput
            {
                DefaultClearanceText = MmText.Format(setup.DefaultClearanceMm),
                DefaultMode = setup.DefaultMode,
                OvershootText = MmText.Format(setup.Options.OvershootMm),
                RoundUpToText = MmText.Format(setup.Options.RoundUpToMm),
                IncludeInsulation = setup.Options.IncludeInsulation,
                AxisMode = setup.Options.AxisMode,
                MaxCircularAngleDeg = setup.Options.MaxCircularAngleDeg,
            };
            input.Rules.AddRange(setup.Rules.Select(ClearanceRuleInput.From));
            return input;
        }
    }
}
