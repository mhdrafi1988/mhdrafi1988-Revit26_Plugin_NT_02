using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.ExportDwgToFolder.V004.Core.Models;

namespace Revit26_Plugin_Revised.ExportDwgToFolder.V004.Core.Services
{
    /// <summary>
    /// Builds the <see cref="DWGExportOptions"/> used for every export in this tool — the one place
    /// options are decided, so layer behaviour can't drift between call sites.
    /// <para>
    /// NEW in V004. V002/V003 built the options inline with five properties and no layer settings at
    /// all, so Revit's defaults decided the layering and <c>PropOverrides</c> stayed at
    /// <c>ByEntity</c>: every exported entity carried its own colour/linetype/lineweight and the
    /// result did not read like a layered DWG.
    /// </para>
    /// <para>
    /// Two levers, in order of effect:
    /// (1) a named export setup saved in the model, loaded through
    /// <see cref="DWGExportOptions.GetPredefinedOptions"/>, which brings that setup's whole layer,
    /// linetype, pattern and font table with it;
    /// (2) <c>PropOverrides</c>, set from <see cref="LayerPropertyMode"/>, which decides whether
    /// entity properties sit on the layer, spawn extra layers, or stay stamped on the entity.
    /// </para>
    /// <para>
    /// This class sits in Core/Services beside <see cref="DwgExportService"/>, which already depends
    /// on the Revit API — a pre-existing deviation from CLAUDE.md's "Core is Revit-free" rule, kept
    /// here for consistency rather than half-migrated.
    /// </para>
    /// </summary>
    public static class DwgExportOptionsFactory
    {
        /// <summary>Label used in the UI and settings for "no saved setup, build from scratch".</summary>
        public const string DefaultsLabel = "(Revit defaults)";

        /// <summary>
        /// Lists the DWG/DXF export setups saved in <paramref name="doc"/>, with
        /// <see cref="DefaultsLabel"/> first. Never throws — a document that has never had an export
        /// setup saved, or an API that refuses the call, yields just the defaults entry.
        /// </summary>
        public static List<string> ListSetups(Document doc)
        {
            var names = new List<string> { DefaultsLabel };
            if (doc == null) return names;

            try
            {
                var found = ExportDWGSettings.ListNames(doc);
                if (found != null)
                {
                    foreach (var name in found)
                    {
                        if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name))
                            names.Add(name);
                    }
                }
            }
            catch
            {
                // No setups, or the document doesn't support the query — defaults only.
            }

            return names;
        }

        /// <summary>
        /// Builds the export options for <paramref name="settings"/>.
        /// </summary>
        /// <param name="doc">Document to load a named setup from.</param>
        /// <param name="settings">The tool's persisted options.</param>
        /// <param name="warning">
        /// Set to a human-readable reason when a requested setup could not be used and the defaults
        /// were substituted; null when nothing went wrong. The caller logs this as a Warning rather
        /// than failing the run — a setup deleted from the model since the last session should not
        /// stop an export.
        /// </param>
        public static DWGExportOptions Build(Document doc, ExportDwgSettings settings, out string warning)
        {
            warning = null;
            DWGExportOptions opts = null;

            string setupName = settings?.ExportSetupName;
            bool wantsSetup = !string.IsNullOrWhiteSpace(setupName) && setupName != DefaultsLabel;

            if (wantsSetup)
            {
                try
                {
                    // Carries the setup's layer / linetype / pattern / font tables. Returns null for
                    // a name that is no longer in the model.
                    opts = DWGExportOptions.GetPredefinedOptions(doc, setupName);
                    if (opts == null)
                        warning = $"DWG export setup \"{setupName}\" is no longer in this model — exporting with Revit defaults instead.";
                }
                catch (Exception ex)
                {
                    opts = null;
                    warning = $"DWG export setup \"{setupName}\" could not be loaded ({ex.Message}) — exporting with Revit defaults instead.";
                }
            }

            if (opts == null)
            {
                opts = new DWGExportOptions
                {
                    // V003's value, kept so a defaults export is unchanged apart from the
                    // layer-property lever below.
                    FileVersion = ACADVersion.R2010
                };
            }

            // The layer lever. Applied whether or not a setup was loaded, because a setup saved for
            // whole-model issue often has it the other way round.
            //
            // Revit's three modes, from its own documentation:
            //   ByEntity  — category properties BYLAYER, overrides BYENTITY. Revit's default, and
            //               what V002/V003 exported with. Best visual fidelity; overridden entities
            //               stop following their layer.
            //   ByLayer   — everything BYLAYER, no overrides. Fewest layers, so the output's layer
            //               set matches the source 1:1; Revit view overrides are dropped.
            //   NewLayer  — everything BYLAYER, each override gets its own new layer. Keeps the
            //               view's appearance, at the cost of layers that were not in the source.
            if (settings != null)
            {
                opts.PropOverrides = settings.LayerPropertyMode switch
                {
                    LayerPropertyMode.LayersOnly => PropOverrideMode.ByLayer,
                    LayerPropertyMode.LayersPlusOverrideLayers => PropOverrideMode.NewLayer,
                    _ => PropOverrideMode.ByEntity
                };
            }

            // Deliberately NOT touching opts.Colors: Revit's default is IndexColors (ACI), which is
            // what the source CAD used. Remapping to true colour is a large part of why a re-export
            // stops looking like the original file.

            // V003's four, re-applied on top of whatever the setup says — these are about getting one
            // clean instance out, not about layers.
            opts.HideScopeBox = true;
            opts.HideUnreferenceViewTags = true;
            opts.HideReferencePlane = true;
            opts.ExportingAreas = false;

            // One file per instance is the whole point of the isolate, so a setup's "merge views"
            // flag must not survive.
            opts.MergedViews = false;

            if (settings != null)
                opts.SharedCoords = settings.UseSharedCoords;

            return opts;
        }

        /// <summary>
        /// One-line description of the options in force, for the run's log header.
        /// </summary>
        public static string Describe(ExportDwgSettings settings)
        {
            string setup = string.IsNullOrWhiteSpace(settings?.ExportSetupName)
                ? DefaultsLabel
                : settings.ExportSetupName;

            string props = (settings?.LayerPropertyMode ?? LayerPropertyMode.LayersOnly) switch
            {
                LayerPropertyMode.LayersOnly => "layers 1:1 (PropOverrides = ByLayer)",
                LayerPropertyMode.LayersPlusOverrideLayers => "layers + a layer per override (PropOverrides = NewLayer)",
                _ => "fidelity first (PropOverrides = ByEntity, Revit default)"
            };

            string links = settings != null && settings.CopyOriginalForLinks
                ? "links copied from source"
                : "links re-exported from view";

            string coords = settings != null && settings.UseSharedCoords
                ? "shared coordinates"
                : "project origin";

            return $"Export setup \"{setup}\" · {props} · {links} · {coords}";
        }
    }
}
