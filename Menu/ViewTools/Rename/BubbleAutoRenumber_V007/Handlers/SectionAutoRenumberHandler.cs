using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.BubbleAutoRenumber.V007.Models;
using Revit26_Plugin_Revised.BubbleAutoRenumber.V007.Services;
using Revit26_Plugin_Revised.BubbleAutoRenumber.V007.ViewModels;
using System;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.BubbleAutoRenumber.V007.Handlers
{
    public class SectionAutoRenumberHandler : IExternalEventHandler
    {
        // ─── payload set by the ViewModel before Raise() ───────────────────
        public ViewSheet? TargetSheet   { get; set; }
        public int        StartNumber   { get; set; }
        public double     ThresholdFt   { get; set; }

        // ─── callback posted back to the ViewModel on completion ───────────
        public Action<RenumberSummary>? OnCompleted { get; set; }

        public void Execute(UIApplication app) =>
            ToolGuard.RunHandler(GetType(), app, ExecuteCore);

        private void ExecuteCore(UIApplication app)
        {
            if (TargetSheet is null || OnCompleted is null) return;

            Document doc = app.ActiveUIDocument.Document;

            try
            {
                var summary = SectionAutoRenumberService.Run(
                    doc,
                    TargetSheet,
                    StartNumber,
                    ThresholdFt);

                OnCompleted(summary);
            }
            catch (Exception ex)
            {
                OnCompleted(new RenumberSummary
                {
                    SheetNumber = TargetSheet.SheetNumber,
                    SheetName   = TargetSheet.Name,
                    LogLines    = { $"[error] {ex.Message}" }
                });
            }
        }

        public string GetName() => "SectionAutoRenumber";
    }
}
