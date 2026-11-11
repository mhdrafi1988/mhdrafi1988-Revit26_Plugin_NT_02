using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Infrastructure.Revit
{
    /// <summary>
    /// Names the void families and their parameters. A custom family is accepted when it has the size
    /// parameters of its shape. Parameter names and GUIDs are permanent.
    /// </summary>
    public static class VoidFamilySpec
    {
        /// <summary>Built-in family for circular openings.</summary>
        public const string RoundFamily = "BIM_Void_DummySplice_Round";

        /// <summary>Built-in family for rectangular openings.</summary>
        public const string RectFamily = "BIM_Void_DummySplice_Rect";

        /// <summary>Width of a rectangular void (length, instance).</summary>
        public const string Width = "Void_Width";

        /// <summary>Height of a rectangular void (length, instance).</summary>
        public const string Height = "Void_Height";

        /// <summary>Diameter of a circular void (length, instance).</summary>
        public const string Diameter = "Void_Diameter";

        /// <summary>Depth of the void, centred on the insertion point (length, instance).</summary>
        public const string Depth = "Void_Depth";

        /// <summary>Job ID (text, instance).</summary>
        public const string JobId = "Void_JobId";

        /// <summary>Clash id (text, instance).</summary>
        public const string ClashId = "Void_ClashId";

        /// <summary>MEP element id (text, instance).</summary>
        public const string MepElementId = "Void_MepElementId";

        /// <summary>Structural element id (text, instance).</summary>
        public const string StructuralElementId = "Void_StructuralElementId";

        /// <summary>MEP category (text, instance).</summary>
        public const string MepCategory = "Void_MepCategory";

        /// <summary>Structural category (text, instance).</summary>
        public const string StructuralCategory = "Void_StructuralCategory";

        /// <summary>Creation date (text, instance).</summary>
        public const string CreatedOn = "Void_CreatedOn";

        /// <summary>Processing status (text, instance).</summary>
        public const string Status = "Void_Status";

        /// <summary>The text parameters with their shared-parameter GUIDs.</summary>
        public static IReadOnlyList<KeyValuePair<string, Guid>> DataParameters { get; } = new[]
        {
            new KeyValuePair<string, Guid>(JobId, new Guid("3c0f5a10-9b1e-4e52-a6d3-5b7c1e2f0a01")),
            new KeyValuePair<string, Guid>(ClashId, new Guid("3c0f5a10-9b1e-4e52-a6d3-5b7c1e2f0a02")),
            new KeyValuePair<string, Guid>(MepElementId, new Guid("3c0f5a10-9b1e-4e52-a6d3-5b7c1e2f0a03")),
            new KeyValuePair<string, Guid>(StructuralElementId, new Guid("3c0f5a10-9b1e-4e52-a6d3-5b7c1e2f0a04")),
            new KeyValuePair<string, Guid>(MepCategory, new Guid("3c0f5a10-9b1e-4e52-a6d3-5b7c1e2f0a05")),
            new KeyValuePair<string, Guid>(StructuralCategory, new Guid("3c0f5a10-9b1e-4e52-a6d3-5b7c1e2f0a06")),
            new KeyValuePair<string, Guid>(CreatedOn, new Guid("3c0f5a10-9b1e-4e52-a6d3-5b7c1e2f0a07")),
            new KeyValuePair<string, Guid>(Status, new Guid("3c0f5a10-9b1e-4e52-a6d3-5b7c1e2f0a08")),
        };
    }
}
