namespace Revit26_Plugin_Revised.TagToText.V001.Core.Models
{
    /// <summary>
    /// Options remembered between runs, stored in
    /// %AppData%\Revit26_Plugin_Revised\TagToText\settings.json.
    /// Every default matches what the window shows the first time it opens.
    /// Version 2 added <see cref="GroupByType"/>.
    /// </summary>
    public sealed class TagToTextSettings
    {
        /// <summary>Current file format version.</summary>
        public const int CurrentVersion = 2;

        /// <summary>File format version. Files without it are read as version 1.</summary>
        public int Version { get; set; } = CurrentVersion;

        /// <summary>Selected tags or all tags in the active view.</summary>
        public SourceMode Source { get; set; } = SourceMode.Selection;

        /// <summary>List Spot Elevations.</summary>
        public bool IncludeSpotElevation { get; set; } = true;

        /// <summary>List Spot Coordinates.</summary>
        public bool IncludeSpotCoordinate { get; set; } = true;

        /// <summary>List Spot Slopes.</summary>
        public bool IncludeSpotSlope { get; set; } = true;

        /// <summary>List element tags.</summary>
        public bool IncludeElementTags { get; set; } = true;

        /// <summary>List Room / Area / Space tags.</summary>
        public bool IncludeSpatialTags { get; set; } = true;

        /// <summary>
        /// Show the tag table grouped by tag type, with a tick box per type. On by default; files from
        /// before version 2 don't have it, so they get it on too.
        /// </summary>
        public bool GroupByType { get; set; } = true;

        /// <summary>Name of the last text type used; empty means the project default.</summary>
        public string TextTypeName { get; set; } = string.Empty;

        /// <summary>Text added before every copied value.</summary>
        public string Prefix { get; set; } = string.Empty;

        /// <summary>Text added after every copied value.</summary>
        public string Suffix { get; set; } = string.Empty;

        /// <summary>Give the text a leader where the tag had one.</summary>
        public bool KeepLeader { get; set; } = true;

        /// <summary>Rotate the text like the tag.</summary>
        public bool MatchRotation { get; set; }

        /// <summary>Write "From tag &lt;Id&gt; · &lt;date&gt;" into the text note's Comments.</summary>
        public bool AddSourceNote { get; set; } = true;

        /// <summary>What to do with the original tags (same view only).</summary>
        public OriginalTagAction OriginalAction { get; set; } = OriginalTagAction.Keep;

        /// <summary>Same view or a new duplicate view.</summary>
        public TargetMode Target { get; set; } = TargetMode.SameView;

        /// <summary>How the new view is duplicated.</summary>
        public DuplicateMode Duplicate { get; set; } = DuplicateMode.WithoutDetailing;

        /// <summary>Text added after the current view's name to name the new view.</summary>
        public string NewViewSuffix { get; set; } = " - Text";

        /// <summary>Open the new view when the run finishes.</summary>
        public bool OpenNewView { get; set; } = true;

        /// <summary>Brings an older file up to <see cref="CurrentVersion"/>. Safe to call on any instance.</summary>
        public TagToTextSettings Migrate()
        {
            if (Version < 1) Version = 1;
            // Version 1 -> 2: GroupByType is new. A version 1 file has no value for it, so it keeps its default.
            TextTypeName ??= string.Empty;
            Prefix ??= string.Empty;
            Suffix ??= string.Empty;
            NewViewSuffix ??= " - Text";
            Version = CurrentVersion;
            return this;
        }
    }
}
