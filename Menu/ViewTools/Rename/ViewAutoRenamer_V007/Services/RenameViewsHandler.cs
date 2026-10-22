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

        int ok = 0, fixedDup = 0, fail = 0;

        using var tx = new Transaction(doc, "VAR02 Rename Views");

        try
        {
            if (tx.Start() != TransactionStatus.Started)
            {
                Dispatch(() => Vm.LogError("Could not start transaction — no changes made."));
                return;
            }

            // Snapshot of all current view names, grouped PER ViewTypeGroup —
            // matches Revit's actual name-uniqueness rule (Section+Callout
            // share one namespace; each other view-type family independent).
            var existingNamesByGroup = BuildExistingNamesByGroup(doc);

            foreach (var item in Payload)
            {
                try
                {
                    var v = doc.GetElement(item.ElementId) as View;
                    if (v == null) continue;

                    if (!existingNamesByGroup.TryGetValue(item.TypeGroup, out var existingNames))
                    {
                        existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        existingNamesByGroup[item.TypeGroup] = existingNames;
                    }

                    // Un-pin silently if needed, but log a warning per view
                    if (v.Pinned)
                    {
                        v.Pinned = false;
                        Dispatch(() => Vm.LogWarning($"\"{item.OriginalName}\" was pinned — un-pinned automatically."));
                    }

                    // Remove the element's current name so it doesn't block its own rename
                    existingNames.Remove(v.Name);

                    string finalName = item.PreviewName;
                    if (existingNames.Contains(finalName))
                    {
                        finalName = GenerateUniqueName(finalName, existingNames, Vm.DuplicateStrategy);
                        fixedDup++;
                    }

                    v.Name = finalName;
                    existingNames.Add(finalName);
                    ok++;
                }
                catch (Exception ex)
                {
                    // Per-item failure — skip silently in the model, log it, keep processing the rest.
                    fail++;
                    Dispatch(() => Vm.LogError($"\"{item.OriginalName}\": {ex.Message}"));
                }
            }

            var status = tx.Commit();
            if (status != TransactionStatus.Committed)
            {
                Dispatch(() => Vm.LogError($"Transaction failed to commit (status: {status}) — no changes were saved."));
                return;
            }

            Dispatch(() => Vm.LogSuccess(
                $"Rename complete — OK: {ok}  |  Duplicates fixed: {fixedDup}  |  Failed: {fail}"));
        }
        catch (Exception ex)
        {
            // Unexpected failure outside the per-item loop (e.g. collector or commit threw).
            // Roll back so the model is never left half-renamed.
            if (tx.GetStatus() == TransactionStatus.Started)
                tx.RollBack();

            Dispatch(() => Vm.LogError($"Rename transaction failed and was rolled back: {ex.Message}"));
        }
    }

    /// <summary>
    /// Builds one existing-names HashSet per ViewTypeGroup by re-classifying
    /// every non-template view currently in the model. Mirrors the grouping
    /// logic in OpenViewAutoRenamerCommand.ClassifyView so collision checks
    /// stay consistent between load and commit.
    /// </summary>
    private static Dictionary<ViewTypeGroup, HashSet<string>> BuildExistingNamesByGroup(Document doc)
    {
        var result = new Dictionary<ViewTypeGroup, HashSet<string>>();

        var allViews = new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => !v.IsTemplate);

        foreach (var v in allViews)
        {
            var group = MapViewTypeToGroup(v.ViewType);
            if (group == null) continue; // out-of-scope view type — ignore for collision purposes

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
