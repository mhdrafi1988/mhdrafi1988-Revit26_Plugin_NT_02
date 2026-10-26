using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.UI;

namespace Revit26_Plugin_Revised.Menu.Ribbon
{
    /// <summary>
    /// Lays a flat list of ribbon items (push buttons and/or pulldown buttons)
    /// directly on a panel as small stacked icons with their names, 3 per column,
    /// splitting 4 as 2 + 2 so no single full-size button is left over
    /// (4 → 2+2, 5 → 3+2, 7 → 3+2+2). Only a panel holding exactly one item
    /// gets a full-size button.
    /// </summary>
    internal static class RibbonLayoutHelper
    {
        // Tooltip punctuation kept as escapes so the source stays ASCII-safe.
        private const string TipSeparator = " — ";   // em dash
        private const string TipBullet = "• ";       // bullet

        public static IList<RibbonItem> AddStackedButtons(RibbonPanel panel, IList<RibbonItemData> items)
        {
            foreach (var pushData in items.OfType<PushButtonData>())
                ApplyCatalogTip(pushData);

            var created = new List<RibbonItem>();
            int i = 0;
            while (i < items.Count)
            {
                int remaining = items.Count - i;
                // Taking 3 from 4 would strand the last item as a wide full-size button.
                if (remaining >= 3 && remaining != 4)
                {
                    var stacked3 = panel.AddStackedItems(items[i], items[i + 1], items[i + 2]);
                    RebindStackedText(stacked3, items[i], items[i + 1], items[i + 2]);
                    created.AddRange(stacked3);
                    i += 3;
                }
                else if (remaining >= 2)
                {
                    var stacked2 = panel.AddStackedItems(items[i], items[i + 1]);
                    RebindStackedText(stacked2, items[i], items[i + 1]);
                    created.AddRange(stacked2);
                    i += 2;
                }
                else
                {
                    // Lone leftover: shown as a full-size button, so give it a
                    // LargeImage too — otherwise a button added via AddItem
                    // (rather than stacked) renders with a blank icon slot.
                    // Image/LargeImage aren't declared on RibbonItemData itself,
                    // so handle the two concrete types this helper is ever given.
                    if (items[i] is PushButtonData pushData && pushData.LargeImage == null)
                        pushData.LargeImage = pushData.Image;
                    else if (items[i] is PulldownButtonData pulldownData && pulldownData.LargeImage == null)
                        pulldownData.LargeImage = pulldownData.Image;
                    created.Add(panel.AddItem(items[i]));
                    i += 1;
                }
            }
            return created;
        }

        /// <summary>
        /// Adds <paramref name="visible"/> as stacks on the panel, then a slide-out (the arrow on the
        /// panel title bar) holding <paramref name="slideOut"/>. A panel has one slide-out only, so call
        /// this once per panel. Returns every created item, so existing
        /// <see cref="WirePulldownButton"/> calls work unchanged.
        /// </summary>
        public static IList<RibbonItem> AddStackedButtonsWithSlideOut(
            RibbonPanel panel, IList<RibbonItemData> visible, IList<RibbonItemData> slideOut)
        {
            var created = new List<RibbonItem>(AddStackedButtons(panel, visible));
            panel.AddSlideOut();
            created.AddRange(AddStackedButtons(panel, slideOut));
            return created;
        }

        /// <summary>
        /// Works around a Revit ribbon bug: a stacked item's label, set via the
        /// Text passed to its Data object's constructor, is frequently dropped
        /// at render time (icon shows, text doesn't) — this only happens inside
        /// AddStackedItems, never on a lone button added via AddItem. Explicitly
        /// re-assigning ItemText on the RibbonItem AddStackedItems just returned
        /// forces the label to actually bind. The tooltip is re-applied the same
        /// way, so every stacked button reliably shows its "Tool — Version" tip.
        /// </summary>
        private static void RebindStackedText(IList<RibbonItem> createdItems, params RibbonItemData[] sourceData)
        {
            for (int j = 0; j < createdItems.Count && j < sourceData.Length; j++)
            {
                if (sourceData[j] is ButtonData buttonData)
                {
                    if (!string.IsNullOrEmpty(buttonData.Text))
                        createdItems[j].ItemText = buttonData.Text;
                    if (!string.IsNullOrEmpty(buttonData.ToolTip))
                        createdItems[j].ToolTip = buttonData.ToolTip;
                }
            }
        }

        /// <summary>
        /// Builds the placeholder PulldownButtonData for 1+ coexisting versions
        /// of the same tool, collected under one dropdown button — no version
        /// runs by default on a bare click, the list always shows on click.
        /// The button face never shows a version number, only the tool name
        /// passed in <paramref name="text"/>.
        /// <paramref name="primary"/> — always the highest version number —
        /// supplies the button's own icon. Insert the returned data into the
        /// list passed to <see cref="AddStackedButtons"/>, then call
        /// <see cref="WirePulldownButton"/> afterward with the same name and
        /// every version, highest version first, to finish populating the
        /// dropdown and its version-list tooltip.
        /// </summary>
        public static PulldownButtonData CreatePulldownButtonData(string name, string text, PushButtonData primary)
        {
            return new PulldownButtonData(name, text)
            {
                Image = primary.Image,
                // Placeholder only — WirePulldownButton replaces it with the
                // list of versions actually in the dropdown.
                ToolTip = text
            };
        }

        /// <summary>
        /// Finishes wiring a PulldownButton created via <see cref="AddStackedButtons"/>:
        /// finds it among the returned items by name and adds every version to
        /// its dropdown list, in the given order. Rule: list the highest
        /// version number first, then each older version in descending order —
        /// never the other way round, regardless of which version is "current".
        /// The first (primary/highest) version should carry an icon on its
        /// PushButtonData — the rest carry their own icons and text in the dropdown.
        /// The pulldown's own tooltip is then set to list every version wired in
        /// (see <see cref="Shared.Services.ToolInfo.Tip"/> — each entry is the
        /// button's tooltip title), so it can never disagree with the dropdown contents.
        /// </summary>
        public static void WirePulldownButton(IList<RibbonItem> createdItems, string name, params PushButtonData[] versions)
        {
            var pulldown = createdItems.OfType<PulldownButton>().FirstOrDefault(p => p.Name == name);
            if (pulldown == null) return;

            foreach (var version in versions)
            {
                ApplyCatalogTip(version);
                pulldown.AddPushButton(version);
            }

            pulldown.ToolTip = BuildVersionsTip(pulldown.ItemText, versions);
        }

        /// <summary>
        /// Makes a button's tooltip title line come from <see cref="Shared.Services.ToolCatalog"/>:
        /// when the button's command class belongs to a catalogued tool, the first line becomes
        /// "Name — vX.Y.Z (build Vnnn)" and any detail lines below it are kept. Buttons for
        /// versions the catalog doesn't list keep the tooltip they were built with.
        /// </summary>
        private static void ApplyCatalogTip(PushButtonData data)
        {
            var tool = Shared.Services.ToolCatalog.Find(data.ClassName);
            if (tool == null) return;

            string tip = data.ToolTip ?? string.Empty;
            int eol = tip.IndexOf('\n');
            data.ToolTip = eol >= 0 ? tool.Tip() + tip.Substring(eol) : tool.Tip();
        }

        /// <summary>
        /// Returns the standard ribbon tooltip: "toolName — version", then an optional detail line.
        /// </summary>
        public static string VersionTip(string toolName, string version, string detail = null)
        {
            string title = $"{toolName}{TipSeparator}{version}";
            return string.IsNullOrWhiteSpace(detail) ? title : title + "\n" + detail;
        }

        private static string BuildVersionsTip(string tool, PushButtonData[] versions)
        {
            var sb = new StringBuilder(tool)
                .Append(TipSeparator)
                .Append(versions.Length == 1 ? "1 version" : versions.Length + " versions");

            foreach (var version in versions)
            {
                // First line of the version's own tooltip is its "Tool — Version" title.
                string title = string.IsNullOrEmpty(version.ToolTip) ? version.Text : version.ToolTip;
                int eol = title.IndexOf('\n');
                if (eol >= 0) title = title.Substring(0, eol);
                sb.Append('\n').Append(TipBullet).Append(title.TrimEnd());
            }
            return sb.ToString();
        }
    }
}
