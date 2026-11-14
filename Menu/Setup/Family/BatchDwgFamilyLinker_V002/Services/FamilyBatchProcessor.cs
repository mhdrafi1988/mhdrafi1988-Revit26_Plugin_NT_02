using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using BatchDwgFamilyLinker.V002.Models;
using BatchDwgFamilyLinker.V002.ViewModels;
using BatchDwgFamilyLinker.V002.Services;
using BatchDwgFamilyLinker.V002.Logging;
using System.IO;
using System.Linq;
using Revit26_Plugin_Revised.Shared.Services;

namespace BatchDwgFamilyLinker.V002
{
    public class FamilyBatchProcessor : IExternalEventHandler
    {
        public BatchOptions Options { get; set; }
        private readonly BatchLinkViewModel _vm;

        public FamilyBatchProcessor(BatchLinkViewModel vm)
        {
            _vm = vm;
        }

        public void Execute(UIApplication app) =>
            ToolGuard.RunHandler(GetType(), app, ExecuteCore);

        private void ExecuteCore(UIApplication app)
        {
            var famFiles = Directory.GetFiles(Options.FamilyFolderPath, "*.rfa");
            _vm.TotalFamilies = famFiles.Length;

            // Esc / Cancel with a Yes-No confirm between and during families. Families already saved are kept.
            using ToolCancellation cancel = ToolCancellation.BeginFor(ToolCatalog.BatchDwgFamilyLinker, app);
            cancel.ConfirmMessage = "Cancel the process?\nThe family being processed is not saved. Families already linked and saved are kept.";

            foreach (var famPath in famFiles)
            {
                ToolCancellation.CheckCurrent();
                Document famDoc = null;

                try
                {
                    var famName = Path.GetFileName(famPath);
                    BatchLogger.Info(_vm, $"Opening {famName}");

                    famDoc = app.Application.OpenDocumentFile(famPath);

                    var dwgPath = Path.Combine(
                        Options.DwgFolderPath,
                        Path.GetFileNameWithoutExtension(famPath) + ".dwg");

                    if (!File.Exists(dwgPath))
                    {
                        BatchLogger.Warn(_vm, $"DWG not found for {famName}");
                        _vm.FailedCount++;
                        continue;
                    }

                    var planView = FamilyViewResolver.GetPlanView(famDoc);
                    if (planView == null)
                    {
                        BatchLogger.Error(_vm, $"No plan view in {famName}");
                        _vm.FailedCount++;
                        continue;
                    }

                    using (var tx = new Transaction(famDoc, "Link DWG"))
                    {
                        tx.Start();
                        DwgLinkService.LinkDwg(
                            famDoc,
                            planView,
                            dwgPath,
                            Options.PlacementMode);
                        tx.Commit();
                    }

                    ToolCancellation.CheckCurrent(); // last chance before this family is saved
                    FamilySaveService.Save(famDoc);
                    _vm.ProcessedCount++;
                    BatchLogger.Info(_vm, $"? Linked {famName}");
                }
                catch (System.OperationCanceledException)
                {
                    BatchLogger.Warn(_vm, $"Cancelled: {_vm.ProcessedCount} of {famFiles.Length} families linked and saved. Saved files are kept; the family being processed was not saved.");
                    throw; // ToolGuard treats it as a user cancel; finally closes the open family without saving
                }
                catch (System.Exception ex)
                {
                    _vm.FailedCount++;
                    BatchLogger.Error(_vm, ex.Message);
                }
                finally
                {
                    famDoc?.Close(false);
                }
            }
        }

        public string GetName() => "Batch DWG Family Processor";
    }
}
