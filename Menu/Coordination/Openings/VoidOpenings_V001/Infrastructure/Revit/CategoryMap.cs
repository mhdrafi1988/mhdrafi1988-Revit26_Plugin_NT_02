using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Infrastructure.Revit
{
    /// <summary>Which Revit built-in categories each of the tool's categories stands for.</summary>
    public static class CategoryMap
    {
        /// <summary>Revit categories of a structural category.</summary>
        public static IReadOnlyList<BuiltInCategory> Of(StructuralCategory category)
        {
            switch (category)
            {
                case StructuralCategory.Floor: return new[] { BuiltInCategory.OST_Floors };
                case StructuralCategory.Wall: return new[] { BuiltInCategory.OST_Walls };
                case StructuralCategory.StructuralFraming: return new[] { BuiltInCategory.OST_StructuralFraming };
                case StructuralCategory.StructuralColumn: return new[] { BuiltInCategory.OST_StructuralColumns };
                case StructuralCategory.StructuralFoundation: return new[] { BuiltInCategory.OST_StructuralFoundation };
                case StructuralCategory.Stair: return new[] { BuiltInCategory.OST_Stairs };
                case StructuralCategory.Landing: return new[] { BuiltInCategory.OST_StairsLandings };
                case StructuralCategory.Ramp: return new[] { BuiltInCategory.OST_Ramps };
                case StructuralCategory.GenericModel: return new[] { BuiltInCategory.OST_GenericModel };
                default: throw new ArgumentOutOfRangeException(nameof(category));
            }
        }

        /// <summary>Revit categories of an MEP category. Flexible pipes and ducts count with the rigid ones.</summary>
        public static IReadOnlyList<BuiltInCategory> Of(MepCategory category)
        {
            switch (category)
            {
                case MepCategory.Pipe: return new[] { BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_FlexPipeCurves };
                case MepCategory.PipeFitting: return new[] { BuiltInCategory.OST_PipeFitting };
                case MepCategory.PipeAccessory: return new[] { BuiltInCategory.OST_PipeAccessory };
                case MepCategory.Duct: return new[] { BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_FlexDuctCurves };
                case MepCategory.DuctFitting: return new[] { BuiltInCategory.OST_DuctFitting };
                case MepCategory.DuctAccessory: return new[] { BuiltInCategory.OST_DuctAccessory };
                case MepCategory.CableTray: return new[] { BuiltInCategory.OST_CableTray };
                case MepCategory.CableTrayFitting: return new[] { BuiltInCategory.OST_CableTrayFitting };
                case MepCategory.Conduit: return new[] { BuiltInCategory.OST_Conduit };
                case MepCategory.ConduitFitting: return new[] { BuiltInCategory.OST_ConduitFitting };
                default: throw new ArgumentOutOfRangeException(nameof(category));
            }
        }
    }
}
