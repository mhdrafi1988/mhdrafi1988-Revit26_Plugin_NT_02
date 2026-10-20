using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using BatchDwgFamilyLinker.Models;
using BatchDwgFamilyLinker.ViewModels;
using BatchDwgFamilyLinker.Services;
using BatchDwgFamilyLinker.Logging;
using System.IO;
using System.Linq;
using Revit26_Plugin_Revised.Shared.Services;

namespace BatchDwgFamilyLinker
{
    public class FamilyBatchProcessor : IExternalEventHandler
    {
        public BatchOptions Options { get; set; }
        private readonly BatchLinkViewModel _vm;

        public FamilyBatchProcessor(BatchLinkViewModel vm)
        {
            _vm = vm;
        }

        public void Execute(UIApplication app)
            => ToolGuard.RunHandler(GetType(), app, ExecuteUnguarded);

        private void ExecuteUnguarded(UIApplication app)
        {
            var famFiles = Directory.GetFiles(Options.FamilyFolderPath, "*.rfa");
            _vm.TotalFamilies = famFiles.Length;

            foreach (var famPath in famFiles)
            {
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

                    FamilySaveService.Save(famDoc);
                    _vm.ProcessedCount++;
                    BatchLogger.Info(_vm, $"? Linked {famName}");
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
