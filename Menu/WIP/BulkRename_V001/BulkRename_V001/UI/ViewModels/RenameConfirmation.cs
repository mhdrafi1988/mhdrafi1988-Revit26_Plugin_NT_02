using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Revit26_Plugin.BulkRename.V001.UI.ViewModels
{
    /// <summary>Builds the text of the "Rename …?" question shown before Apply. Pure text, no Revit or WPF.</summary>
    public static class RenameConfirmation
    {
        /// <summary>How many renames are listed by name; the rest are only counted.</summary>
        public const int MaxListed = 15;

        /// <summary>
        /// Text asking to confirm <paramref name="renames"/>, each a (current name, new name) pair, of the kind
        /// <paramref name="kind"/> (lower case and plural, for example "line patterns").
        /// </summary>
        public static string Build(string kind, IReadOnlyList<(string From, string To)> renames)
        {
            renames ??= Array.Empty<(string From, string To)>();

            var sb = new StringBuilder();
            sb.AppendLine($"Rename {renames.Count} {kind}?\n");
            foreach (var (from, to) in renames.Take(MaxListed))
                sb.AppendLine($"  • {from}  →  {to}");
            if (renames.Count > MaxListed) sb.AppendLine($"  … and {renames.Count - MaxListed} more");
            sb.AppendLine("\nYou can undo this with Ctrl+Z in Revit.");
            return sb.ToString();
        }
    }
}
