using System;
using Autodesk.Revit.UI;
using Microsoft.Extensions.DependencyInjection;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ClashDetection;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ModelSelection;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.Placement;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Ports;
using Revit26_Plugin_Revised.VoidOpenings.V001.Infrastructure.ExternalEvents;
using Revit26_Plugin_Revised.VoidOpenings.V001.Infrastructure.Files;
using Revit26_Plugin_Revised.VoidOpenings.V001.Infrastructure.Revit;
using Revit26_Plugin_Revised.VoidOpenings.V001.UI.Services;
using Revit26_Plugin_Revised.VoidOpenings.V001.UI.ViewModels;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Commands
{
    /// <summary>
    /// Wires the tool together: one service container per open window. Build it inside the command,
    /// because the dispatcher creates a Revit external event; dispose it when the window closes.
    /// </summary>
    public static class CompositionRoot
    {
        /// <summary>Builds the container for one window.</summary>
        public static ServiceProvider Build(UIApplication application)
        {
            if (application == null) throw new ArgumentNullException(nameof(application));

            var services = new ServiceCollection();

            // Revit side
            services.AddSingleton(new RevitContext(application));
            services.AddSingleton<IRevitDispatcher, RevitDispatcher>();
            services.AddSingleton<IModelReader, RevitModelReader>();
            services.AddSingleton<IVoidWriter, RevitVoidWriter>();
            services.AddSingleton<IViewNavigator, ViewNavigator>();
            services.AddSingleton<VoidOpeningsFiles>();

            // Use cases
            services.AddSingleton<ModelSelectionService>();
            services.AddSingleton<AnalyzeClashesUseCase>();
            services.AddSingleton<GenerateVoidsUseCase>();

            // Window
            services.AddSingleton<FileDialogService>();
            services.AddSingleton<MainViewModel>();
            return services.BuildServiceProvider();
        }
    }
}
