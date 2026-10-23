using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.LinkMemory.V002.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.LinkMemory.V002.Core.Services
{
    /// <summary>Builds a memory from the model: collects its views and records which links they show. Read only.</summary>
    public static class LinkMemoryCapture
    {
        /// <summary>Memory of <paramref name="view"/> and its related views.</summary>
        public static LinkMemoryItem FromView(Document doc, View view, CollectOptions options, Action<LogEntry> log)
        {
            var collector = new ViewCollector(doc, options, log);
            LinkMemoryItem memory = Build(doc, collector, collector.ForView(view), options);
            memory.Scope = MemoryScope.ActiveView;
            memory.SourceUniqueId = view.UniqueId;
            memory.SourceName = ViewCollector.NameOf(view);
            memory.Name = memory.SourceName;
            return memory;
        }

        /// <summary>Memory of print set <paramref name="set"/>.</summary>
        public static LinkMemoryItem FromPrintSet(Document doc, ViewSheetSet set, CollectOptions options, Action<LogEntry> log)
        {
            var collector = new ViewCollector(doc, options, log);
            LinkMemoryItem memory = Build(doc, collector, collector.ForPrintSet(set), options);
            memory.Scope = MemoryScope.PrintSet;
            memory.SourceUniqueId = set.UniqueId;
            memory.SourceName = set.Name;
            memory.Name = set.Name;
            return memory;
        }

        /// <summary>Whole-model memory: no views; a link counts as used when it is loaded now (as in V001).</summary>
        public static LinkMemoryItem WholeModel(Document doc, CollectOptions options)
        {
            var memory = new LinkMemoryItem
            {
                Scope = MemoryScope.WholeModel,
                SourceName = "Whole model",
                Options = options.Clone(),
                SavedAt = DateTime.Now,
                SavedBy = doc.Application.Username
            };
            foreach (LiveLink l in LinkCatalog.Read(doc).Where(l => options.Includes(l.Kind)))
            {
                memory.Links.Add(new SavedLink
                {
                    UniqueId = l.UniqueId, Name = l.Name, Kind = l.Kind, Path = l.Path,
                    WasLoaded = l.IsLoaded, Use = l.IsLoaded ? LinkUse.Used : LinkUse.NotUsed
                });
            }
            return memory;
        }

        /// <summary>Print sets (View/Sheet Sets) of <paramref name="doc"/>, by name.</summary>
        public static List<ViewSheetSet> PrintSets(Document doc)
            => new FilteredElementCollector(doc).OfClass(typeof(ViewSheetSet)).Cast<ViewSheetSet>()
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();

        private static LinkMemoryItem Build(Document doc, ViewCollector collector, List<CollectedView> views, CollectOptions options)
        {
            List<LiveLink> live = LinkCatalog.Read(doc).Where(l => options.Includes(l.Kind)).ToList();
            var listed = new HashSet<string>(live.Select(l => l.UniqueId));
            var counts = new Dictionary<string, int>();

            var memory = new LinkMemoryItem
            {
                Options = options.Clone(),
                SavedAt = DateTime.Now,
                SavedBy = doc.Application.Username
            };

            foreach (CollectedView cv in views)
            {
                List<string> visible = collector.VisibleLinkTypes(cv.View).Where(listed.Contains).ToList();
                foreach (string id in visible) counts[id] = counts.TryGetValue(id, out int n) ? n + 1 : 1;

                string sheetNumber = null, sheetName = null;
                if (cv.View is ViewSheet sheet)
                {
                    sheetNumber = sheet.SheetNumber;
                    sheetName = sheet.Name;
                }
                else
                {
                    sheetNumber = cv.View.get_Parameter(BuiltInParameter.VIEWPORT_SHEET_NUMBER)?.AsString();
                    sheetName = cv.View.get_Parameter(BuiltInParameter.VIEWPORT_SHEET_NAME)?.AsString();
                }

                memory.Views.Add(new SavedView
                {
                    UniqueId = cv.View.UniqueId,
                    Name = ViewCollector.NameOf(cv.View),
                    ViewType = ViewCollector.TypeText(cv.View),
                    SheetNumber = sheetNumber ?? string.Empty,
                    SheetName = sheetName ?? string.Empty,
                    Reason = cv.Reason,
                    Depth = cv.Depth,
                    LinkIds = visible
                });
            }

            foreach (LiveLink l in live)
            {
                int count = counts.TryGetValue(l.UniqueId, out int n) ? n : 0;
                LinkUse use = count > 0 ? LinkUse.Used
                    // An unloaded link draws nothing, so Revit can't say whether the views would show it.
                    : !l.IsLoaded ? LinkUse.Unknown
                    : LinkUse.NotUsed;

                memory.Links.Add(new SavedLink
                {
                    UniqueId = l.UniqueId, Name = l.Name, Kind = l.Kind, Path = l.Path,
                    WasLoaded = l.IsLoaded, Use = use, ViewCount = count
                });
            }
            return memory;
        }
    }
}
