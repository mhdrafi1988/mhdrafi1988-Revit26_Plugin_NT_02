using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin.BulkRename.V001.Core.Models
{
    /// <summary>What happened to one requested rename.</summary>
    public sealed class RenameOutcome
    {
        /// <summary>Name the item had before the run.</summary>
        public string From { get; }

        /// <summary>Name that was asked for.</summary>
        public string To { get; }

        /// <summary>True when the item now has <see cref="To"/>; false when it was skipped and left unchanged.</summary>
        public bool Renamed { get; }

        /// <summary>Why the item was skipped; empty when it was renamed.</summary>
        public string Reason { get; }

        private RenameOutcome(string from, string to, bool renamed, string reason)
        {
            From = from ?? string.Empty;
            To = to ?? string.Empty;
            Renamed = renamed;
            Reason = reason ?? string.Empty;
        }

        /// <summary>The item was renamed from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public static RenameOutcome Done(string from, string to) => new RenameOutcome(from, to, true, null);

        /// <summary>The item was left as <paramref name="from"/> because of <paramref name="reason"/>.</summary>
        public static RenameOutcome Skip(string from, string to, string reason) => new RenameOutcome(from, to, false, reason);
    }

    /// <summary>
    /// Result of one Bulk Rename run: one outcome per requested item, in the order they were decided.
    /// Renamed outcomes are final only once the transaction has committed, which is when the run returns.
    /// </summary>
    public sealed class RenameResult
    {
        /// <summary>One outcome per requested item.</summary>
        public IReadOnlyList<RenameOutcome> Outcomes { get; }

        /// <summary>Items renamed.</summary>
        public int Renamed { get; }

        /// <summary>Items skipped (changed since listing, now owned by another user, or Revit refused the name).</summary>
        public int Skipped => Outcomes.Count - Renamed;

        /// <summary>Creates a result from the outcomes of a run.</summary>
        public RenameResult(IReadOnlyList<RenameOutcome> outcomes)
        {
            Outcomes = outcomes ?? Array.Empty<RenameOutcome>();
            Renamed = Outcomes.Count(o => o.Renamed);
        }

        /// <inheritdoc/>
        public override string ToString() => $"{Renamed} renamed, {Skipped} skipped.";
    }
}
