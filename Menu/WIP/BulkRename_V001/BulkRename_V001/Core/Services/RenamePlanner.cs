using System;
using System.Collections.Generic;

namespace Revit26_Plugin.BulkRename.V001.Core.Services
{
    /// <summary>One rename the user asked for, identified by a caller-chosen key.</summary>
    public readonly struct RenameChange
    {
        /// <summary>Caller's key, handed back in <see cref="RenameStep.Key"/>.</summary>
        public int Key { get; }

        /// <summary>Name the item has now.</summary>
        public string Current { get; }

        /// <summary>Name the item should get.</summary>
        public string New { get; }

        /// <summary>Creates a change.</summary>
        public RenameChange(int key, string current, string newName)
        {
            Key = key;
            Current = current;
            New = newName;
        }
    }

    /// <summary>One name assignment to perform, in order.</summary>
    public sealed class RenameStep
    {
        /// <summary>Key of the <see cref="RenameChange"/> this step belongs to.</summary>
        public int Key { get; }

        /// <summary>Name to assign.</summary>
        public string Name { get; }

        /// <summary>True for a throw-away name that only frees the item's old name.</summary>
        public bool IsTemporary { get; }

        /// <summary>Creates a step.</summary>
        public RenameStep(int key, string name, bool isTemporary)
        {
            Key = key;
            Name = name;
            IsTemporary = isTemporary;
        }
    }

    /// <summary>
    /// Orders renames so no step ever asks for a name that another item still holds. Revit refuses a
    /// name that is already taken, so swaps (A to B and B to A), chains (A to B, B to C) and case-only
    /// changes (Dash to DASH) go through a temporary name first. Pure logic with no Revit dependency.
    /// </summary>
    public static class RenamePlanner
    {
        /// <summary>
        /// Plans the renames: first every conflicting item moves to a temporary name from
        /// <paramref name="newTempName"/>, then the items with no conflict get their final names, then the
        /// conflicting items get theirs. An item conflicts when its new name equals, ignoring case, the
        /// current name of another item in the list, or its own current name (a case-only change).
        /// </summary>
        public static List<RenameStep> Plan(IReadOnlyList<RenameChange> changes, Func<string> newTempName)
        {
            var currentNames = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            for (int p = 0; p < changes.Count; p++)
            {
                string current = changes[p].Current ?? string.Empty;
                if (!currentNames.TryGetValue(current, out var list))
                    currentNames[current] = list = new List<int>();
                list.Add(p);
            }

            var conflicting = new bool[changes.Count];
            for (int p = 0; p < changes.Count; p++)
            {
                var change = changes[p];
                if (string.Equals(change.Current, change.New, StringComparison.OrdinalIgnoreCase))
                {
                    conflicting[p] = true;
                }
                else if (currentNames.TryGetValue(change.New ?? string.Empty, out var holders))
                {
                    conflicting[p] = holders.Exists(h => h != p);
                }
            }

            var steps = new List<RenameStep>(changes.Count * 2);
            for (int p = 0; p < changes.Count; p++)
                if (conflicting[p]) steps.Add(new RenameStep(changes[p].Key, newTempName(), true));
            for (int p = 0; p < changes.Count; p++)
                if (!conflicting[p]) steps.Add(new RenameStep(changes[p].Key, changes[p].New, false));
            for (int p = 0; p < changes.Count; p++)
                if (conflicting[p]) steps.Add(new RenameStep(changes[p].Key, changes[p].New, false));
            return steps;
        }
    }
}
