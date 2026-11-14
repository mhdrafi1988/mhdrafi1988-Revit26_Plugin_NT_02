using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.AnnotationOverlapReport.V001.Core
{
    /// <summary>Facts about one check run, shown in the status line and written to the "Run info" sheet.</summary>
    public sealed class ReportRunInfo
    {
        /// <summary>Tool version that produced the run.</summary>
        public string ToolVersion { get; set; }

        /// <summary>When the run finished.</summary>
        public DateTime RunAt { get; set; }

        /// <summary>Number of views and sheets scanned.</summary>
        public int ViewsScanned { get; set; }

        /// <summary>Number of annotations measured and compared.</summary>
        public int AnnotationsScanned { get; set; }

        /// <summary>Annotations Revit gave no box for in their view; they could not be checked.</summary>
        public int NotMeasured { get; set; }

        /// <summary>Views that failed or are not supported, each with the reason.</summary>
        public List<string> SkippedViews { get; set; } = new List<string>();
    }
}
