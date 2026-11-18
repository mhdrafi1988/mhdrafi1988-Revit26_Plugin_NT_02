using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ModelSelection;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Rules;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.UI.ViewModels
{
    /// <summary>One entry of a drop-down list: a value and the text shown for it.</summary>
    public sealed class ChoiceItem<T>
    {
        /// <summary>The value the entry stands for.</summary>
        public T Value { get; }

        /// <summary>Text shown in the list.</summary>
        public string Label { get; }

        /// <summary>Creates an entry.</summary>
        public ChoiceItem(T value, string label)
        {
            Value = value;
            Label = label;
        }
    }

    /// <summary>The drop-down lists of the OPENING CLEARANCE card.</summary>
    public static class ClearanceOptions
    {
        /// <summary>MEP categories a rule can be for, in the order of the MEP MODEL panel.</summary>
        public static IReadOnlyList<ChoiceItem<MepCategory>> Categories { get; } =
            CategoryCatalog.Mep.Select(c => new ChoiceItem<MepCategory>(c, CategoryCatalog.NameOf(c))).ToList();

        /// <summary>How an offset is applied.</summary>
        public static IReadOnlyList<ChoiceItem<ClearanceMode>> Modes { get; } = new[]
        {
            new ChoiceItem<ClearanceMode>(ClearanceMode.PerSide, "Each side"),
            new ChoiceItem<ClearanceMode>(ClearanceMode.Total, "Total"),
        };

        /// <summary>Opening shape a rule asks for.</summary>
        public static IReadOnlyList<ChoiceItem<OpeningShapePolicy>> Shapes { get; } = new[]
        {
            new ChoiceItem<OpeningShapePolicy>(OpeningShapePolicy.Auto, "Auto"),
            new ChoiceItem<OpeningShapePolicy>(OpeningShapePolicy.Circular, "Circular"),
            new ChoiceItem<OpeningShapePolicy>(OpeningShapePolicy.Rectangular, "Rectangular"),
        };

        /// <summary>Which way openings point.</summary>
        public static IReadOnlyList<ChoiceItem<OpeningAxisMode>> Axes { get; } = new[]
        {
            new ChoiceItem<OpeningAxisMode>(OpeningAxisMode.PerpendicularToStructure, "Perpendicular to structure"),
            new ChoiceItem<OpeningAxisMode>(OpeningAxisMode.FollowMep, "Follow MEP direction"),
        };
    }
}
