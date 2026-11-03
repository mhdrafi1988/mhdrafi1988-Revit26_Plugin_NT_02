using System;
using System.Collections.Generic;
using Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Revit
{
    /// <summary>Outcome of one Run, reported back to the window.</summary>
    public sealed class PatternWriteResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;
        public int LinesCreated { get; init; }
        public int TextNotesCreated { get; init; }
        public int FilledRegionsCreated { get; init; }
        public int FillTypesCreated { get; init; }
        public int SkippedShortSegments { get; init; }
        public int ReplacedGroups { get; init; }
        public IReadOnlyList<string> GroupNames { get; init; } = Array.Empty<string>();
        public IReadOnlyDictionary<PatternLayer, int> LinesPerLayer { get; init; } = new Dictionary<PatternLayer, int>();
        public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

        public static PatternWriteResult Failed(string message) => new PatternWriteResult { Success = false, Message = message };
    }
}
