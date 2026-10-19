using System;
using System.Collections.Generic;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.LinkVersionChecker_V005.Core.Models
{
    /// <summary>
    /// Whether the copy of an ACC / cloud-sync link we are using is the current one.  [V005]
    ///
    /// Two independent sources feed this, and they answer different questions:
    ///   • a pure cloud link (ACC / BIM 360) — ExternalResourceReference.GetResourceVersionStatus(),
    ///     which needs an open model, a sign-in and the network, so it is In-Model only and on demand;
    ///   • a Desktop Connector / OneDrive path — Windows placeholder attributes, which are a plain
    ///     disk read and therefore work in every mode, during the normal parallel probe.
    ///
    /// NotApplicable is 0 so an unset <see cref="LinkInfo.Sync"/> means "nothing to report".
    /// Sorting uses <see cref="AccSyncInfo.SortOrder"/>, not the enum value.
    /// </summary>
    public enum AccSyncState
    {
        NotApplicable = 0,   // not an ACC or cloud-sync link — blank cell
        NotChecked,          // ACC row, the cloud check has not been run yet
        NotAvailableInMode,  // pure cloud row in Pre-Open / Folder Audit — no session to ask with
        Current,             // ResourceVersionStatus.Current
        Downloaded,          // sync path, file present and hydrated on this PC
        NotDownloaded,       // sync path, still a placeholder — the first open pays the download
        OutOfDate,           // ResourceVersionStatus.OutOfDate
        Unknown,             // ResourceVersionStatus.Unknown — offline, signed out, legacy path
        Failed               // invalid reference, server missing, or the call threw
    }

    /// <summary>Display text, glyph, colour and "how we know" for every <see cref="AccSyncState"/>.</summary>
    public static class AccSyncInfo
    {
        private sealed class Meta
        {
            public string Text;
            public string Glyph;
            public string Colour;
            public string DetectedBy;
            public int Sort;
            public bool Problem;
        }

        // Glyphs: ● we have the current bits · ○ in the cloud but not on this PC · ◌ unknown / not asked
        private const string Filled = "●";
        private const string Ring   = "○";
        private const string Dotted = "◌";

        private const string Green = "#2E7D32";
        private const string Amber = "#B26A00";
        private const string Red   = "#C62828";
        private const string Grey  = "#6B7280";

        private static readonly Dictionary<AccSyncState, Meta> Map = new Dictionary<AccSyncState, Meta>
        {
            [AccSyncState.Failed] = new Meta
            {
                Text = "Failed", Glyph = Filled, Colour = Red, Sort = 0, Problem = true,
                DetectedBy = "reference invalid, resource server missing, or the version call threw"
            },
            [AccSyncState.OutOfDate] = new Meta
            {
                Text = "Out of date", Glyph = Filled, Colour = Amber, Sort = 1, Problem = true,
                DetectedBy = "ResourceVersionStatus.OutOfDate — a newer version exists in ACC"
            },
            [AccSyncState.NotDownloaded] = new Meta
            {
                Text = "Not downloaded", Glyph = Ring, Colour = Green, Sort = 2, Problem = true,
                DetectedBy = "placeholder file — OFFLINE / RECALL_ON_DATA_ACCESS attribute set"
            },
            [AccSyncState.Unknown] = new Meta
            {
                Text = "Unknown", Glyph = Dotted, Colour = Grey, Sort = 3,
                DetectedBy = "ResourceVersionStatus.Unknown — ACC could not answer (offline or signed out)"
            },
            [AccSyncState.NotChecked] = new Meta
            {
                Text = "Not checked", Glyph = Dotted, Colour = Grey, Sort = 4,
                DetectedBy = "press Check Cloud Status — the version call is not run during a scan"
            },
            [AccSyncState.NotAvailableInMode] = new Meta
            {
                Text = "Not in this mode", Glyph = Dotted, Colour = Grey, Sort = 5,
                DetectedBy = "Pre-Open / Folder Audit has no Revit session, so ACC cannot be asked"
            },
            [AccSyncState.Downloaded] = new Meta
            {
                Text = "Downloaded", Glyph = Filled, Colour = Green, Sort = 6,
                DetectedBy = "file is on this PC with no recall attribute (not a version check)"
            },
            [AccSyncState.Current] = new Meta
            {
                Text = "Current", Glyph = Filled, Colour = Green, Sort = 7,
                DetectedBy = "ResourceVersionStatus.Current — this is the newest version in ACC"
            },
            [AccSyncState.NotApplicable] = new Meta
            {
                Text = "", Glyph = "", Colour = Grey, Sort = 8,
                DetectedBy = "not an ACC or cloud-sync link"
            },
        };

        public static string Text(AccSyncState s) => Map[s].Text;
        public static string Glyph(AccSyncState s) => Map[s].Glyph;
        public static string Colour(AccSyncState s) => Map[s].Colour;
        public static string DetectedBy(AccSyncState s) => Map[s].DetectedBy;
        public static int SortOrder(AccSyncState s) => Map[s].Sort;

        /// <summary>True for the states that deserve attention before the team opens the host.</summary>
        public static bool IsProblem(AccSyncState s) => Map[s].Problem;

        /// <summary>
        /// The shared radio-dot state for the ACC Docs column: filled green for Current / Downloaded,
        /// hollow green for Not downloaded, amber for Out of date, red for Failed, grey ring while
        /// unanswered, faint empty ring for a non-ACC link.
        /// </summary>
        public static AccLinkState ToDot(AccSyncState s)
        {
            switch (s)
            {
                case AccSyncState.Current: return AccLinkState.Current;
                case AccSyncState.Downloaded: return AccLinkState.Loaded;
                case AccSyncState.NotDownloaded: return AccLinkState.NotLoaded;
                case AccSyncState.OutOfDate: return AccLinkState.OutOfDate;
                case AccSyncState.Failed: return AccLinkState.Failed;
                case AccSyncState.NotApplicable: return AccLinkState.NotAcc;
                default: return AccLinkState.Unknown;
            }
        }

        /// <summary>True once the state carries a real answer (so "checked" has meaning).</summary>
        public static bool IsResolved(AccSyncState s) =>
            s != AccSyncState.NotChecked && s != AccSyncState.NotAvailableInMode && s != AccSyncState.NotApplicable;
    }
}
