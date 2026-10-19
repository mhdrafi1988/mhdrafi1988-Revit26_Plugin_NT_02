using Revit26_Plugin_Revised.BulkRename.V001.Core.Models;
using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.BulkRename.V001.Core.Services
{
    /// <summary>One row as the validator sees it.</summary>
    public readonly struct NameEntry
    {
        /// <summary>Name the item has in the model now.</summary>
        public string Current { get; }

        /// <summary>Name typed or generated for the row.</summary>
        public string New { get; }

        /// <summary>True when the row is ticked.</summary>
        public bool Included { get; }

        /// <summary>True when the item can never be renamed (built-in, owned by another user).</summary>
        public bool Locked { get; }

        /// <summary>Creates an entry.</summary>
        public NameEntry(string current, string newName, bool included, bool locked)
        {
            Current = current;
            New = newName;
            Included = included;
            Locked = locked;
        }
    }

    /// <summary>The validator's verdict for one row.</summary>
    public readonly struct NameCheck
    {
        /// <summary>Row state.</summary>
        public RenameStatus Status { get; }

        /// <summary>Why the row is Invalid or Duplicate; null otherwise.</summary>
        public string Message { get; }

        /// <summary>
        /// Name the item will have once Apply has run: the trimmed new name for a ticked, changed row,
        /// otherwise the current name. Apply renames a Ready row to exactly this name, so the trim rule
        /// lives here and nowhere else.
        /// </summary>
        public string FinalName { get; }

        /// <summary>Creates a verdict.</summary>
        public NameCheck(RenameStatus status, string finalName, string message = null)
        {
            Status = status;
            FinalName = finalName ?? string.Empty;
            Message = message;
        }
    }

    /// <summary>
    /// Checks every row of one item type together, because a duplicate name is a property of the
    /// whole list, not of one row. Pure logic with no Revit dependency.
    /// </summary>
    public static class NameValidator
    {
        /// <summary>
        /// Returns one verdict per entry, in the same order. A ticked, changed row is Ready unless its
        /// new name is empty or has a forbidden character (Invalid), or another item would end up with
        /// the same name, compared without case (Duplicate). Locked rows are always Locked; unticked or
        /// unchanged rows are Unchanged.
        /// </summary>
        public static NameCheck[] Check(IReadOnlyList<NameEntry> entries)
        {
            int n = entries.Count;
            var results = new NameCheck[n];
            var finals = new string[n];
            var changed = new bool[n];
            var holders = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            // The name each item will have once Apply has run.
            for (int i = 0; i < n; i++)
            {
                var e = entries[i];
                string current = e.Current ?? string.Empty;
                bool active = e.Included && !e.Locked;

                // A row nobody edited stays as it is, even if its current name has stray spaces;
                // a name that was edited is trimmed.
                string typed = e.New ?? string.Empty;
                string final = !active || string.Equals(typed, current, StringComparison.Ordinal)
                    ? current
                    : typed.Trim();

                finals[i] = final;
                changed[i] = active && !string.Equals(final, current, StringComparison.Ordinal);

                if (final.Length > 0)
                    holders[final] = holders.TryGetValue(final, out int count) ? count + 1 : 1;
            }

            for (int i = 0; i < n; i++)
            {
                string final = finals[i];

                if (entries[i].Locked)
                {
                    results[i] = new NameCheck(RenameStatus.Locked, final);
                }
                else if (!changed[i])
                {
                    results[i] = new NameCheck(RenameStatus.Unchanged, final);
                }
                else if (final.Length == 0)
                {
                    results[i] = new NameCheck(RenameStatus.Invalid, final, "The new name is empty.");
                }
                else if (NameRules.FindForbiddenChar(final) is char bad)
                {
                    results[i] = new NameCheck(RenameStatus.Invalid, final, $"Revit does not allow the character '{bad}' in a name.");
                }
                else if (holders[final] > 1)
                {
                    results[i] = new NameCheck(RenameStatus.Duplicate, final, $"Another item would also be named '{final}'.");
                }
                else
                {
                    results[i] = new NameCheck(RenameStatus.Ready, final);
                }
            }

            return results;
        }
    }
}
