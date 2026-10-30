using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.ViewAutoRenamer.V007.Models;
using Revit26_Plugin_Revised.ViewAutoRenamer.V007.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Revit26_Plugin_Revised.ViewAutoRenamer.V007.Services;

public class RenameViewsHandler : IExternalEventHandler
{
    public List<ViewItemViewModel> Payload { get; set; } = new();
    public ViewsListViewModel      Vm      { get; set; } = null!;

    /// <summary>Runs the rename through ToolGuard so no exception reaches Revit.</summary>
    public void Execute(UIApplication app) =>
        Revit26_Plugin_Revised.Shared.Services.ToolGuard.RunHandler(typeof(RenameViewsHandler), app, Run);

    private void Run(UIApplication app)
    {
        var doc = app.ActiveUIDocument.Document;

        int ok = 0, fixedDup = 0, fail = 0, unchanged = 0, skippedOwned = 0;
        var renamed = new List<(ViewItemViewModel item, string name)>();

        using var tx = new Transaction(doc, "VAR02 Rename Views");

        try
        {
            if (tx.Start() != TransactionStatus.Started)
            {
                Dispatch(() => Vm.LogError("Could not start transaction — no changes made."));
                return;
            }

            // Only the name groups being renamed are scanned (not every view in the project).
            var groups = new HashSet<ViewTypeGroup>(Payload.Select(i => i.TypeGroup));
            var existingNamesByGroup = BuildExistingNamesByGroup(doc, groups);

            foreach (var item in Payload)
            {
                try
                {
                    var v = doc.GetElement(item.ElementId) as View;
                    if (v == null) continue;

                    // Nothing to do when the name is already right: assigning Name would
                    // still make Revit regenerate the view, so leave it alone.
                    if (string.Equals(v.Name, item.PreviewName, StringComparison.Ordinal))
                    {
                        unchanged++;
                        continue;
                    }

                    if (doc.IsWorkshared)
                    {
                        var status = WorksharingUtils.GetCheckoutStatus(doc, v.Id);
                        if (status == CheckoutStatus.OwnedByOtherUser)
                        {
                            skippedOwned++;
                            Dispatch(() => Vm.LogWarning($"\"{item.OriginalName}\" is checked out by another user — skipped."));
                            continue;
                        }
                    }

                    if (!existingNamesByGroup.TryGetValue(item.TypeGroup, out var existingNames))
                    {
                        existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        existingNamesByGroup[item.TypeGroup] = existingNames;
                    }

                    // Remove the element's current name so it doesn't block its own rename
                    existingNames.Remove(v.Name);

                    string finalName = item.PreviewName;
                    if (existingNames.Contains(finalName))
                    {
                        finalName = GenerateUniqueName(finalName, existingNames, Vm.DuplicateStrategy);
                        fixedDup++;
                    }

                    // Pinned views can't be renamed: unpin, rename, then pin again.
                    bool wasPinned = v.Pinned;
                    if (wasPinned) v.Pinned = false;
                    v.Name = finalName;
                    if (wasPinned) v.Pinned = true;

                    existingNames.Add(finalName);
                    renamed.Add((item, finalName));
                    ok++;
                }
                catch (Exception ex)
                {
                    // Per-item failure — log it, keep processing the rest.
                    fail++;
                    Dispatch(() => Vm.LogError($"\"{item.OriginalName}\": {ex.Message}"));
                }
            }

            if (ok == 0)
            {
                tx.RollBack();
                Dispatch(() => Vm.LogInfo(
                    $"Nothing to rename — {unchanged} already named correctly, {skippedOwned} skipped, {fail} failed. The model was not touched."));
                return;
            }

            var commit = tx.Commit();
            if (commit != TransactionStatus.Committed)
            {
                Dispatch(() => Vm.LogError($"Transaction failed to commit (status: {commit}) — no changes were saved."));
                return;
            }

            Dispatch(() =>
            {
                foreach (var (item, name) in renamed) item.ApplyCommittedName(name);
                Vm.OnRenameCommitted();
                Vm.LogSuccess(
                    $"Rename complete — Renamed: {ok}  |  Duplicates fixed: {fixedDup}  |  Unchanged (skipped): {unchanged}  |  Failed: {fail}. Ctrl+Z in Revit undoes it in one step.");
            });
        }
        catch (Exception ex)
        {
            // Unexpected failure outside the per-item loop. Roll back so the model is never left half-renamed.
            if (tx.GetStatus() == TransactionStatus.Started)
                tx.RollBack();

            Dispatch(() => Vm.LogError($"Rename transaction failed and was rolled back: {ex.Message}"));
        }
    }

    /// <summary>
    /// Builds one existing-names HashSet per requested ViewTypeGroup. Only views of the
    /// matching Revit view types are read, so a rename never scans unrelated views.
    /// </summary>
    private static Dictionary<ViewTypeGroup, HashSet<string>> BuildExistingNamesByGroup(
        Document doc, ISet<ViewTypeGroup> wanted)
    {
        var result = new Dictionary<ViewTypeGroup, HashSet<string>>();

        foreach (var v in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>())
        {
            if (v.IsTemplate) continue;
            var group = MapViewTypeToGroup(v.ViewType);
            if (group == null || !wanted.Contains(group.Value)) continue;

            if (!result.TryGetValue(group.Value, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                result[group.Value] = set;
            }
            set.Add(v.Name);
        }

        return result;
    }

    private static ViewTypeGroup? MapViewTypeToGroup(ViewType viewType) => viewType switch
    {
        ViewType.Section        => ViewTypeGroup.SectionOrCallout,
        ViewType.Elevation      => ViewTypeGroup.Elevation,
        ViewType.FloorPlan      => ViewTypeGroup.FloorPlan,
        ViewType.CeilingPlan    => ViewTypeGroup.CeilingPlan,
        ViewType.EngineeringPlan=> ViewTypeGroup.StructuralPlan,
        ViewType.AreaPlan       => ViewTypeGroup.AreaPlan,
        ViewType.DraftingView   => ViewTypeGroup.Drafting,
        ViewType.Legend         => ViewTypeGroup.Legend,
        ViewType.Schedule       => ViewTypeGroup.Schedule,
        _                       => null
    };

    // ── Dispatcher marshal (handler runs on Revit API thread) ────────────────
    private static void Dispatch(Action action) =>
        System.Windows.Application.Current?.Dispatcher.Invoke(action);

    // ── Unique-name generator ─────────────────────────────────────────────
    private static string GenerateUniqueName(
        string baseName,
        HashSet<string> existing,
        DuplicateFixStrategy strategy)
    {
        int i = 1;
        while (true)
        {
            string candidate = strategy switch
            {
                DuplicateFixStrategy.NumberedBrackets => $"{baseName} ({i})",
                DuplicateFixStrategy.AlphabetSuffix   => $"{baseName}-{ToAlpha(i)}",
                DuplicateFixStrategy.DupSuffix         => i == 1 ? $"{baseName}_dup" : $"{baseName}_dup{i}",
                _                                      => $"{baseName} ({i})"
            };
            if (!existing.Contains(candidate)) return candidate;
            i++;
        }
    }

    // 1→A, 26→Z, 27→AA … (no char-overflow past Z)
    private static string ToAlpha(int n)
    {
        var sb = new StringBuilder();
        while (n > 0) { n--; sb.Insert(0, (char)('A' + n % 26)); n /= 26; }
        return sb.ToString();
    }

    public string GetName() => "VAR02 Rename Handler";
}
