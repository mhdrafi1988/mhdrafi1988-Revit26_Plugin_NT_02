using System;
using System.Collections.Generic;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ModelSelection
{
    /// <summary>The categories the tool offers, in the order the window lists them, with their display names.</summary>
    public static class CategoryCatalog
    {
        /// <summary>Note shown on structural categories that Revit cannot cut with a void family.</summary>
        public const string MarkerOnlyNote = "Marker only, no cut";

        /// <summary>Structural categories in display order.</summary>
        public static IReadOnlyList<StructuralCategory> Structural { get; } = new[]
        {
            StructuralCategory.Floor,
            StructuralCategory.Wall,
            StructuralCategory.StructuralFraming,
            StructuralCategory.StructuralColumn,
            StructuralCategory.Stair,
            StructuralCategory.Landing,
            StructuralCategory.Ramp,
            StructuralCategory.StructuralFoundation,
            StructuralCategory.GenericModel,
        };

        /// <summary>MEP categories in display order.</summary>
        public static IReadOnlyList<MepCategory> Mep { get; } = new[]
        {
            MepCategory.Pipe,
            MepCategory.PipeAccessory,
            MepCategory.PipeFitting,
            MepCategory.Duct,
            MepCategory.DuctAccessory,
            MepCategory.DuctFitting,
            MepCategory.CableTray,
            MepCategory.CableTrayFitting,
            MepCategory.Conduit,
            MepCategory.ConduitFitting,
        };

        /// <summary>Display name of a structural category.</summary>
        public static string NameOf(StructuralCategory category)
        {
            switch (category)
            {
                case StructuralCategory.Floor: return "Floors";
                case StructuralCategory.Wall: return "Walls";
                case StructuralCategory.StructuralFraming: return "Structural Framing";
                case StructuralCategory.StructuralColumn: return "Structural Columns";
                case StructuralCategory.Stair: return "Stairs";
                case StructuralCategory.Landing: return "Landings";
                case StructuralCategory.Ramp: return "Ramps";
                case StructuralCategory.StructuralFoundation: return "Structural Foundations";
                case StructuralCategory.GenericModel: return "Generic Models";
                default: throw new ArgumentOutOfRangeException(nameof(category));
            }
        }

        /// <summary>Display name of an MEP category.</summary>
        public static string NameOf(MepCategory category)
        {
            switch (category)
            {
                case MepCategory.Pipe: return "Pipes";
                case MepCategory.PipeAccessory: return "Pipe Accessories";
                case MepCategory.PipeFitting: return "Pipe Fittings";
                case MepCategory.Duct: return "Ducts";
                case MepCategory.DuctAccessory: return "Duct Accessories";
                case MepCategory.DuctFitting: return "Duct Fittings";
                case MepCategory.CableTray: return "Cable Trays";
                case MepCategory.CableTrayFitting: return "Cable Tray Fittings";
                case MepCategory.Conduit: return "Conduits";
                case MepCategory.ConduitFitting: return "Conduit Fittings";
                default: throw new ArgumentOutOfRangeException(nameof(category));
            }
        }

        /// <summary>
        /// False for categories Revit does not let a void family cut (stairs, landings, ramps). Openings in
        /// those are placed as markers and reported, never cut. To be confirmed per category in Revit.
        /// </summary>
        public static bool CanBeCut(StructuralCategory category)
            => category != StructuralCategory.Stair
            && category != StructuralCategory.Landing
            && category != StructuralCategory.Ramp;
    }
}
