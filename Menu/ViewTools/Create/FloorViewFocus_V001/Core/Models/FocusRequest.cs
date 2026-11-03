using System;
using System.Collections.Generic;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.FloorViewFocus.V001.Core.Models
{
    /// <summary>
    /// Payload handed from the ViewModel to the ExternalEvent handler.
    /// Carries stable UniqueIds (not Revit objects) so Core stays Revit-free.
    /// Callbacks are invoked from the Revit API thread; the ViewModel marshals to UI.
    /// </summary>
    public sealed class FocusRequest
    {
        public string ViewUniqueId { get; init; } = string.Empty;
        public IReadOnlyList<string> FloorUniqueIds { get; init; } = Array.Empty<string>();

        public double ViewMarginMm { get; init; }
        public double AnnotationMarginMm { get; init; }
        public double DefaultOffsetMm { get; init; }

        /// <summary>Real-time log sink.</summary>
        public Action<LogLevel, string>? OnLog { get; init; }

        /// <summary>Invoked exactly once when the handler finishes (success or failure).</summary>
        public Action<FocusResult>? OnCompleted { get; init; }
    }

    /// <summary>Outcome of one Run.</summary>
    public sealed class FocusResult
    {
        public bool Success { get; set; }

        /// <summary>Floors that contributed to the union bounding box.</summary>
        public int Included { get; set; }

        /// <summary>Floors skipped (deleted / no bounding box).</summary>
        public int Skipped { get; set; }

        /// <summary>Floors lost because the operation failed and was rolled back.</summary>
        public int Failed { get; set; }

        /// <summary>Union bounding box (no margin), in mm.</summary>
        public double BoxWidthMm { get; set; }
        public double BoxHeightMm { get; set; }

        /// <summary>Entered margin + default offset, in mm.</summary>
        public double ViewMarginTotalMm { get; set; }
        public double AnnotationMarginTotalMm { get; set; }

        public string? ErrorMessage { get; set; }
        public TimeSpan Duration { get; set; }
    }
}
