using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.WorksetManager.V014.Core.Models;
using Revit26_Plugin_Revised.WorksetManager.V014.UI.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Revit26_Plugin_Revised.WorksetManager.V014.Core.Services
{
    public class WorksetService
    {
        private readonly Action<LogEntry> _log;
        private int _processedCount;

        public WorksetService(Action<LogEntry> logger) => _log = logger;

        // ─── Queries ──────────────────────────────────────────────────────────

        public List<string> GetLinkedFileNames(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkType))
                .Cast<RevitLinkType>()
                .Select(t => Path.GetFileNameWithoutExtension(t.Name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public bool HasInstances(Document doc, string linkName)
            => GetLinkInstances(doc, linkName).Any();

        public int GetInstanceCount(Document doc, string linkName)
            => GetLinkInstances(doc, linkName).Count;

        public string GetCurrentWorksetName(Document doc, string linkName)
        {
            var instances = GetLinkInstances(doc, linkName);
            if (!instances.Any()) return "No instances";

            var worksetNames = new HashSet<string>();
            foreach (var instance in instances)
            {
                var param = instance.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                if (param != null && param.HasValue)
                {
                    int wsId = param.AsInteger();
                    Workset workset = new FilteredWorksetCollector(doc)
                        .FirstOrDefault(ws => ws.Id.IntegerValue == wsId);
                    if (workset != null)
                        worksetNames.Add(workset.Name);
                }
            }

            if (worksetNames.Count == 0) return "None";
            if (worksetNames.Count == 1) return worksetNames.First();
            return "MIXED";
        }

        public string CheckExistingWorkset(Document doc, string worksetName)
        {
            Workset existing = new FilteredWorksetCollector(doc)
                .OfKind(WorksetKind.UserWorkset)
                .FirstOrDefault(ws =>
                    ws.Name.Equals(worksetName, StringComparison.OrdinalIgnoreCase));
            return existing?.Name;
        }

        public bool IsFullyAssigned(Document doc, string linkName, string proposedWorksetName)
        {
            var instances = GetLinkInstances(doc, linkName);
            if (!instances.Any()) return false;

            foreach (var instance in instances)
            {
                var param = instance.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                if (param == null || !param.HasValue) return false;

                int wsId = param.AsInteger();
                Workset ws = new FilteredWorksetCollector(doc)
                    .FirstOrDefault(w => w.Id.IntegerValue == wsId);

                if (ws == null || !ws.Name.Equals(proposedWorksetName, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }

        // ─── Name helpers ─────────────────────────────────────────────────────

        public void ResolveDuplicates(IEnumerable<WorksetItem> items)
        {
            // Seed with Grid 1 names so they are never collided into
            var usedNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items.Where(i => i.GridCategory == WorksetGridCategory.AlreadyAssigned))
                usedNames[item.ProposedWorksetName] = 1;

            foreach (var item in items.Where(i => i.GridCategory != WorksetGridCategory.AlreadyAssigned))
            {
                string baseName  = item.ProposedWorksetName;
                string finalName = baseName;
                int    count     = 1;

                while (usedNames.ContainsKey(finalName))
                {
                    count++;
                    finalName = $"{baseName}_{count}";
                }

                if (finalName != baseName)
                {
                    _log(new LogEntry(LogLevel.Warning,
                        $"Duplicate name '{baseName}' → renamed to '{finalName}'"));
                    item.ProposedWorksetName = finalName;
                }

                usedNames[finalName] = count;
            }
        }

        public string CleanName(string input)
        {
            if (string.IsNullOrEmpty(input)) return "Workset";

            string original = input;
            string cleaned  = input;

            char[] invalidChars = { ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~', '\t', '\n', '\r' };
            foreach (char c in invalidChars)
                cleaned = cleaned.Replace(c, '-');

            while (cleaned.Contains("--"))
                cleaned = cleaned.Replace("--", "-");

            cleaned = cleaned.Trim().Trim('-');

            if (string.IsNullOrEmpty(cleaned))
                cleaned = "Workset";

            if (cleaned.Length > 150)
            {
                string truncated = cleaned[..150];
                _log(new LogEntry(LogLevel.Warning,
                    $"Truncated '{cleaned}' to 150 chars: '{truncated}'"));
                cleaned = truncated;
            }

            return cleaned;
        }

        /// <summary>
        /// Variant used during live pattern editing — no log spam on every keystroke.
        /// </summary>
        public string CleanNameSilent(string input)
        {
            if (string.IsNullOrEmpty(input)) return "Workset";
            string cleaned = input;
            char[] invalidChars = { ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~', '\t', '\n', '\r' };
            foreach (char c in invalidChars)
                cleaned = cleaned.Replace(c, '-');
            while (cleaned.Contains("--"))
                cleaned = cleaned.Replace("--", "-");
            cleaned = cleaned.Trim().Trim('-');
            if (string.IsNullOrEmpty(cleaned)) cleaned = "Workset";
            if (cleaned.Length > 150) cleaned = cleaned[..150];
            return cleaned;
        }

        // ─── Write operations ─────────────────────────────────────────────────

        public void CreateAndAssign(
            Document doc,
            IEnumerable<(string ProposedName, string LinkName, bool CreateNew)> items,
            WorksetsViewModel viewModel)
        {
            _processedCount = 0;

            foreach (var (proposedName, linkName, createNew) in items)
            {
                _processedCount++;

                using Transaction tx = new(doc,
                    createNew ? $"Create workset: {proposedName}" : $"Resync workset: {proposedName}");
                try
                {
                    tx.Start();

                    WorksetId targetId;

                    if (createNew)
                    {
                        Workset workset = Workset.Create(doc, proposedName);
                        targetId = workset.Id;
                        _log(new LogEntry(LogLevel.Info, $"Created workset: '{proposedName}'"));
                    }
                    else
                    {
                        Workset existing = new FilteredWorksetCollector(doc)
                            .OfKind(WorksetKind.UserWorkset)
                            .FirstOrDefault(ws =>
                                ws.Name.Equals(proposedName, StringComparison.OrdinalIgnoreCase));

                        if (existing == null)
                        {
                            _log(new LogEntry(LogLevel.Error,
                                $"Workset '{proposedName}' not found — skipped."));
                            tx.RollBack();
                            continue;
                        }
                        targetId = existing.Id;
                    }

                    int assignedCount = AssignLinkInstances(doc, linkName, targetId);
                    _log(new LogEntry(LogLevel.Info,
                        $"Assigned {assignedCount} instance(s) of '{linkName}' → '{proposedName}'"));

                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    _log(new LogEntry(LogLevel.Error, $"Failed on '{proposedName}': {ex.Message}"));
                }

                if (_processedCount % 5 == 0)
                    viewModel?.KeepUIResponsive();
            }
        }

        // ─── Private helpers ──────────────────────────────────────────────────

        private int AssignLinkInstances(Document doc, string linkName, WorksetId worksetId)
        {
            var instances = GetLinkInstances(doc, linkName);
            foreach (RevitLinkInstance instance in instances)
                instance.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM)
                        ?.Set(worksetId.IntegerValue);
            return instances.Count;
        }

        private List<RevitLinkInstance> GetLinkInstances(Document doc, string linkName)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .Where(i =>
                {
                    var linkDoc = i.GetLinkDocument();
                    if (linkDoc == null) return false;
                    string title = Path.GetFileNameWithoutExtension(linkDoc.Title);
                    return title.Equals(linkName, StringComparison.OrdinalIgnoreCase);
                })
                .ToList();
        }
    }
}
