using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Dtos;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.Reporting;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Ports;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Rules;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.Placement
{
    /// <summary>
    /// What the Generate Void, Export package and Apply package buttons do, with the clearance setup
    /// of the card turned into the settings a run takes. Voids are always placed in the active model,
    /// so Generate is for a structure that is the active model; a linked structure is handed over as
    /// an opening package and applied by running the tool inside that file.
    /// </summary>
    public sealed class VoidPlacementService
    {
        private readonly GenerateVoidsUseCase _generate;
        private readonly IRevitDispatcher _dispatcher;
        private readonly IModelReader _models;

        /// <summary>Creates the service.</summary>
        public VoidPlacementService(GenerateVoidsUseCase generate, IRevitDispatcher dispatcher, IModelReader models)
        {
            _generate = generate ?? throw new ArgumentNullException(nameof(generate));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _models = models ?? throw new ArgumentNullException(nameof(models));
        }

        /// <summary>
        /// Places a void for each opening in the active model and writes the Job ID, status and note
        /// back onto it. The openings are in the active model's coordinates already, so no transform
        /// is applied; only call this when the structural model is the active model.
        /// </summary>
        public Task<GenerateSummary> GenerateAsync(IReadOnlyList<OpeningData> openings, ClearanceSetup setup)
        {
            if (openings == null) throw new ArgumentNullException(nameof(openings));
            return _generate.RunAsync(openings, Transform3.Identity, VoidOpeningsSettings.From(setup ?? ClearanceSetup.CreateDefault()));
        }

        /// <summary>
        /// The opening package for a structural model, with every opening in that model's own
        /// coordinates, so it can be applied by opening that file and pressing Apply package.
        /// </summary>
        public async Task<OpeningPackage> BuildPackageAsync(ModelRef structural, ModelRef mep, IReadOnlyList<OpeningData> openings)
        {
            if (structural == null) throw new ArgumentNullException(nameof(structural));
            if (mep == null) throw new ArgumentNullException(nameof(mep));
            if (openings == null) throw new ArgumentNullException(nameof(openings));

            Transform3 structuralToHost = await _dispatcher.RunAsync(() => _models.GetToHostTransform(structural));
            return OpeningPackageBuilder.Build(structural, mep, openings, structuralToHost);
        }

        /// <summary>Places the openings of a package in the active model.</summary>
        public Task<GenerateSummary> ApplyPackageAsync(OpeningPackage package, ClearanceSetup setup)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));
            return _generate.ApplyPackageAsync(package, VoidOpeningsSettings.From(setup ?? ClearanceSetup.CreateDefault()));
        }
    }
}
