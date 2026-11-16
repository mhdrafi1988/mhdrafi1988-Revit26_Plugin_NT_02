using System;
using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Ports;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Rules;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ClashDetection
{
    /// <summary>What one Analyze run works on: the two models, their ticked categories and the clearance setup.</summary>
    public sealed class AnalysisRequest
    {
        /// <summary>The structural model.</summary>
        public ModelRef Structural { get; }

        /// <summary>Ticked structural categories.</summary>
        public IReadOnlyList<StructuralCategory> StructuralCategories { get; }

        /// <summary>The MEP model.</summary>
        public ModelRef Mep { get; }

        /// <summary>Ticked MEP categories.</summary>
        public IReadOnlyList<MepCategory> MepCategories { get; }

        /// <summary>Clearance rules and sizing options.</summary>
        public ClearanceSetup Setup { get; }

        /// <summary>Creates a request.</summary>
        public AnalysisRequest(ModelRef structural, IEnumerable<StructuralCategory> structuralCategories,
            ModelRef mep, IEnumerable<MepCategory> mepCategories, ClearanceSetup setup)
        {
            Structural = structural;
            StructuralCategories = (structuralCategories ?? Enumerable.Empty<StructuralCategory>()).Distinct().ToList();
            Mep = mep;
            MepCategories = (mepCategories ?? Enumerable.Empty<MepCategory>()).Distinct().ToList();
            Setup = setup;
        }

        /// <summary>
        /// Why the request cannot run, in the user's words; null when it can. The window shows this
        /// next to the Analyze button.
        /// </summary>
        public string Problem()
        {
            if (Structural == null) return "Choose a structural model.";
            if (!Structural.IsLoaded) return "The structural link is not loaded.";
            if (Mep == null) return "Choose an MEP model.";
            if (!Mep.IsLoaded) return "The MEP link is not loaded.";
            if (StructuralCategories.Count == 0) return "Tick at least one structural category.";
            if (MepCategories.Count == 0) return "Tick at least one MEP category.";
            if (Setup == null) return "Fix the errors on the OPENING CLEARANCE card.";
            return null;
        }
    }

    /// <summary>Where an Analyze run is.</summary>
    public sealed class AnalysisProgress
    {
        /// <summary>What is happening, for the status text.</summary>
        public string Stage { get; }

        /// <summary>Overall progress, 0 to 1.</summary>
        public double Fraction { get; }

        /// <summary>Creates a progress report.</summary>
        public AnalysisProgress(string stage, double fraction)
        {
            Stage = stage ?? string.Empty;
            Fraction = Math.Max(0, Math.Min(1, fraction));
        }
    }

    /// <summary>What an Analyze run found.</summary>
    public sealed class AnalysisResult
    {
        /// <summary>The request the run answered.</summary>
        public AnalysisRequest Request { get; }

        /// <summary>One opening per clash, in MEP element order and then along each element.</summary>
        public IReadOnlyList<OpeningData> Openings { get; }

        /// <summary>Number of MEP elements analysed.</summary>
        public int MepElementCount { get; }

        /// <summary>Number of structural elements analysed (those near the MEP elements).</summary>
        public int StructuralElementCount { get; }

        /// <summary>Selected elements that could not be analysed, MEP first.</summary>
        public IReadOnlyList<SkippedElement> NotAnalysed { get; }

        /// <summary>How long the run took.</summary>
        public TimeSpan Duration { get; }

        /// <summary>Creates a result.</summary>
        public AnalysisResult(AnalysisRequest request, IReadOnlyList<OpeningData> openings, int mepElementCount,
            int structuralElementCount, IReadOnlyList<SkippedElement> notAnalysed, TimeSpan duration)
        {
            Request = request ?? throw new ArgumentNullException(nameof(request));
            Openings = openings ?? new List<OpeningData>();
            MepElementCount = mepElementCount;
            StructuralElementCount = structuralElementCount;
            NotAnalysed = notAnalysed ?? new List<SkippedElement>();
            Duration = duration;
        }
    }
}
