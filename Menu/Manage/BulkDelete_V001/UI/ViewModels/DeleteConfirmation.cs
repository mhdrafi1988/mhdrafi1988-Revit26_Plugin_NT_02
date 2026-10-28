using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Revit26_Plugin_Revised.BulkDelete.V001.UI.ViewModels
{
    /// <summary>Builds the text of the "Delete …?" question shown before Delete. Pure text, no Revit or WPF.</summary>
    public static class DeleteConfirmation
    {
        /// <summary>How many items are listed by name; the rest are only counted.</summary>
        public const int MaxListed = 15;

        /// <summary>
        /// Text asking to confirm deleting <paramref name="names"/>, which are items of the kind
        /// <paramref name="kind"/> (lower case and plural, for example "line patterns").
        /// </summary>
        public static string Build(string kind, IReadOnlyList<string> names)
        {
            names ??= Array.Empty<string>();

            var sb = new StringBuilder();
            sb.AppendLine($"Delete {names.Count} {kind} from the model?\n");
            foreach (var name in names.Take(MaxListed))
                sb.AppendLine($"  • {name}");
            if (names.Count > MaxListed) sb.AppendLine($"  … and {names.Count - MaxListed} more");
            sb.AppendLine("\nAnything that still uses them may change appearance.");
            sb.AppendLine("An item whose deletion would also delete other elements is skipped.");
            sb.AppendLine("You can undo this with Ctrl+Z in Revit.");
            return sb.ToString();
        }
    }
}
