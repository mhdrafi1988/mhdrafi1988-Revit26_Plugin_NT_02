using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.BaseAndNearestLevel.V001.Core
{
    /// <summary>How the nearest level is picked from the candidate levels.</summary>
    public enum NearestRule
    {
        /// <summary>Smallest absolute distance, above or below. The default.</summary>
        ClosestEitherWay = 0,
        /// <summary>Only levels at or below the element's base elevation, so the nearest offset is never negative.</summary>
        ClosestAtOrBelow,
        /// <summary>Only levels at or above the element's base elevation, so the nearest offset is never positive.</summary>
        ClosestAtOrAbove
    }

    /// <summary>The four parameters the tool writes.</summary>
    public enum TargetSlot
    {
        /// <summary>Name of the element's own base level. Text.</summary>
        BaseLevelName = 0,
        /// <summary>The element's own base offset. Length.</summary>
        BaseOffset,
        /// <summary>Name of the level nearest the element's base elevation. Text.</summary>
        NearestLevelName,
        /// <summary>Signed distance from that nearest level to the element's base elevation. Length.</summary>
        NearestOffset
    }

    /// <summary>Result of one pre-check, and the severity of a row.</summary>
    public enum CheckResult
    {
        /// <summary>Nothing to report.</summary>
        Pass,
        /// <summary>Something is skipped or created automatically; the run can go ahead.</summary>
        Warning,
        /// <summary>The run cannot go ahead until this is fixed.</summary>
        Blocked
    }

    /// <summary>State of one element row in the grid.</summary>
    public enum RowState
    {
        /// <summary>The row is not ticked.</summary>
        NotChecked,
        /// <summary>Ticked, but its levels have not been read yet.</summary>
        Pending,
        /// <summary>Will be written by Write Parameters.</summary>
        Ready,
        /// <summary>All four parameters already hold the new values.</summary>
        UpToDate,
        /// <summary>Will be skipped (reason in the status text).</summary>
        Warning,
        /// <summary>A blocking pre-check stops the run.</summary>
        Blocked,
        /// <summary>The run wrote the values.</summary>
        Assigned,
        /// <summary>The run skipped the element.</summary>
        Skipped,
        /// <summary>The run could not write the values.</summary>
        Failed
    }

    /// <summary>What a run does with an element whose target parameter already holds a different value.</summary>
    public enum ExistingChoice
    {
        /// <summary>Follow the window's "Existing values" option.</summary>
        Default,
        /// <summary>Keep the current values for this element.</summary>
        Skip,
        /// <summary>Replace the current values for this element.</summary>
        Rewrite
    }

    /// <summary>Why an element cannot be re-hosted onto its nearest level.</summary>
    public enum RehostSkip
    {
        /// <summary>It can be re-hosted.</summary>
        None,
        /// <summary>Its nearest level is already its base level, so there is nothing to change.</summary>
        NoChange,
        /// <summary>A shape-edited floor or roof. Changing Level can reset its point edits, so it is skipped by default.</summary>
        ShapeEdited,
        /// <summary>Its level parameter is read-only, as on an extrusion roof.</summary>
        ReadOnlyLevel,
        /// <summary>In a group: the change would hit every instance of that group.</summary>
        InGroup,
        /// <summary>Owned by another user in a workshared model.</summary>
        OwnedByOther,
        /// <summary>It has no base level to change.</summary>
        NoBaseLevel
    }

    /// <summary>Which data type a slot needs, and how it reads in the UI.</summary>
    public static class TargetSlots
    {
        /// <summary>Every slot, in window order.</summary>
        public static readonly TargetSlot[] All =
        {
            TargetSlot.BaseLevelName, TargetSlot.BaseOffset, TargetSlot.NearestLevelName, TargetSlot.NearestOffset
        };

        /// <summary>True for the two level-name slots, which are Text; false for the two offsets, which are Length.</summary>
        public static bool IsText(TargetSlot slot) =>
            slot == TargetSlot.BaseLevelName || slot == TargetSlot.NearestLevelName;

        /// <summary>"Text" or "Length".</summary>
        public static string TypeName(TargetSlot slot) => IsText(slot) ? "Text" : "Length";

        /// <summary>Label shown beside the slot, for example "Base offset".</summary>
        public static string Label(TargetSlot slot) => slot switch
        {
            TargetSlot.BaseLevelName => "Base level name",
            TargetSlot.BaseOffset => "Base offset",
            TargetSlot.NearestLevelName => "Nearest level name",
            TargetSlot.NearestOffset => "Nearest offset",
            _ => slot.ToString()
        };

        /// <summary>1-based number shown in the window.</summary>
        public static int Number(TargetSlot slot) => (int)slot + 1;
    }

    /// <summary>One level the nearest-level search may choose.</summary>
    public sealed class LevelCandidate
    {
        /// <summary>Level element id value.</summary>
        public long Id { get; set; }

        /// <summary>Level name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Elevation in millimetres, from the level's internal elevation.</summary>
        public double ElevationMm { get; set; }

        /// <summary>True when the level's "Building Story" parameter is ticked.</summary>
        public bool IsBuildingStorey { get; set; }
    }

    /// <summary>The nearest level found for one element.</summary>
    public readonly struct NearestMatch
    {
        /// <summary>Creates a result.</summary>
        public NearestMatch(bool found, long levelId, string levelName, double offsetMm)
        {
            Found = found;
            LevelId = levelId;
            LevelName = levelName;
            OffsetMm = offsetMm;
        }

        /// <summary>True when a candidate level matched the rule.</summary>
        public bool Found { get; }

        /// <summary>Level element id value.</summary>
        public long LevelId { get; }

        /// <summary>Level name.</summary>
        public string LevelName { get; }

        /// <summary>Element base elevation minus that level's elevation, in millimetres.</summary>
        public double OffsetMm { get; }

        /// <summary>No level matched.</summary>
        public static NearestMatch None => new(false, 0, string.Empty, 0);
    }

    /// <summary>Everything one element contributes to the pre-checks, the grid and the run.</summary>
    public sealed class ElementFacts
    {
        /// <summary>Element id value.</summary>
        public long Id { get; set; }

        /// <summary>Category name: Roofs, Floors or Walls.</summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>"Family: Type".</summary>
        public string FamilyAndType { get; set; } = string.Empty;

        /// <summary>True when the element's level/offset pair was found.</summary>
        public bool HasBase { get; set; }

        /// <summary>Base level name.</summary>
        public string BaseLevelName { get; set; } = string.Empty;

        /// <summary>Base level element id value.</summary>
        public long BaseLevelId { get; set; }

        /// <summary>Base offset in millimetres.</summary>
        public double BaseOffsetMm { get; set; }

        /// <summary>Which parameter pair was read, for example "Base Constraint + Base Offset".</summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>The element's real base elevation in millimetres: base level elevation + base offset.</summary>
        public double ElevationMm { get; set; }

        /// <summary>True when a nearest level was found.</summary>
        public bool NearestFound { get; set; }

        /// <summary>Nearest level name.</summary>
        public string NearestLevelName { get; set; } = string.Empty;

        /// <summary>Nearest level element id value.</summary>
        public long NearestLevelId { get; set; }

        /// <summary>Nearest offset in millimetres.</summary>
        public double NearestOffsetMm { get; set; }

        /// <summary>True when the nearest level is not the base level.</summary>
        public bool IsChanged => NearestFound && HasBase && NearestLevelId != BaseLevelId;

        /// <summary>The element is in a group.</summary>
        public bool InGroup { get; set; }

        /// <summary>A shape-edited floor or roof.</summary>
        public bool ShapeEdited { get; set; }

        /// <summary>The element's own level parameter cannot be written, as on an extrusion roof.</summary>
        public bool LevelParamReadOnly { get; set; }

        /// <summary>Owner when the element is checked out by someone else; otherwise null.</summary>
        public string OwnedBy { get; set; }

        /// <summary>The element changed in the central model.</summary>
        public bool OutOfDate { get; set; }

        /// <summary>The category does not take bound parameters.</summary>
        public bool CategoryUnsupported { get; set; }

        /// <summary>Current value of each target parameter, as displayed, indexed by <see cref="TargetSlot"/>.</summary>
        public string[] CurrentValues { get; } = { string.Empty, string.Empty, string.Empty, string.Empty };

        /// <summary>True when that target parameter is read-only on this element.</summary>
        public bool[] SlotReadOnly { get; } = new bool[4];

        /// <summary>The value this run would write to each slot, indexed by <see cref="TargetSlot"/>.</summary>
        public string[] NewValues { get; } = { string.Empty, string.Empty, string.Empty, string.Empty };

        /// <summary>Any target parameter already holds a value.</summary>
        public bool HasAnyValue { get; set; }

        /// <summary>Any target parameter holds a value other than the one this run would write.</summary>
        public bool AnyDifferent { get; set; }

        /// <summary>Why this element cannot be re-hosted; <see cref="RehostSkip.None"/> when it can.</summary>
        public RehostSkip RehostBlock { get; set; }
    }

    /// <summary>What is known about one of the four target parameters.</summary>
    public sealed class SlotFacts
    {
        /// <summary>Which slot this is.</summary>
        public TargetSlot Slot { get; set; }

        /// <summary>Parameter name as typed or chosen.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>How many bound parameters of the project carry this name.</summary>
        public int ProjectMatchCount { get; set; }

        /// <summary>The parameter does not exist yet and will be created from <see cref="NewParameterSpec"/>.</summary>
        public bool IsNew { get; set; }

        /// <summary>The existing parameter's data type matches what the slot needs.</summary>
        public bool DataTypeOk { get; set; }

        /// <summary>Data type of the existing parameter, for the message when it is wrong.</summary>
        public string DataTypeName { get; set; } = string.Empty;

        /// <summary>The existing parameter is an instance parameter.</summary>
        public bool IsInstance { get; set; }

        /// <summary>The existing parameter is shared.</summary>
        public bool IsShared { get; set; }

        /// <summary>Values can vary between group instances.</summary>
        public bool VariesAcrossGroups { get; set; }

        /// <summary>Categories of the ticked elements that the existing binding does not cover.</summary>
        public List<string> MissingCategories { get; } = new();

        /// <summary>The new parameter to create, when <see cref="IsNew"/>.</summary>
        public NewParameterSpec Pending { get; set; }

        /// <summary>Shared parameter file facts for a new parameter.</summary>
        public FileFacts File { get; } = new();

        /// <summary>True when the slot has no name yet.</summary>
        public bool IsEmpty => string.IsNullOrWhiteSpace(Name);
    }

    /// <summary>Shared parameter file facts behind one new parameter.</summary>
    public sealed class FileFacts
    {
        /// <summary>Path of the shared parameter file.</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>The file exists on disk.</summary>
        public bool Exists { get; set; }

        /// <summary>The file can be written.</summary>
        public bool Writable { get; set; }

        /// <summary>How many entries of the file carry the parameter's name.</summary>
        public int MatchCount { get; set; }

        /// <summary>The matching entry, when there is one.</summary>
        public SharedParamFileEntry Entry { get; set; }

        /// <summary>Name of the project parameter that already uses the file entry's GUID.</summary>
        public string GuidUsedBy { get; set; }

        /// <summary>Why the file could not be read.</summary>
        public string ReadError { get; set; }
    }

    /// <summary>The document-level facts behind the pre-checks.</summary>
    public sealed class DocumentFacts
    {
        /// <summary>The document is a family document.</summary>
        public bool IsFamilyDocument { get; set; }

        /// <summary>The document is read-only.</summary>
        public bool IsReadOnly { get; set; }

        /// <summary>The document is workshared.</summary>
        public bool IsWorkshared { get; set; }

        /// <summary>Candidate levels the nearest-level search used.</summary>
        public List<LevelCandidate> Candidates { get; } = new();

        /// <summary>Levels left out because they are not building storeys.</summary>
        public List<string> ExcludedLevels { get; } = new();
    }

    /// <summary>A new shared parameter to create for one slot.</summary>
    public sealed class NewParameterSpec
    {
        /// <summary>Parameter name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Shared parameter file to add it to.</summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>Group inside that file.</summary>
        public string FileGroup { get; set; } = "Data";

        /// <summary>Which slot it is for; fixes the data type.</summary>
        public TargetSlot Slot { get; set; }
    }

    /// <summary>One pre-check row.</summary>
    public sealed class CheckRow
    {
        /// <summary>Creates a row.</summary>
        public CheckRow(string title, CheckResult result, string detail)
        {
            Title = title;
            Result = result;
            Detail = detail;
        }

        /// <summary>Row title, for example "c · Target parameters".</summary>
        public string Title { get; }

        /// <summary>Pass, Warning or Blocked.</summary>
        public CheckResult Result { get; }

        /// <summary>What the check found.</summary>
        public string Detail { get; }
    }

    /// <summary>Everything the pre-checks and the run read in one pass.</summary>
    public sealed class FactsResult
    {
        /// <summary>Document-level facts.</summary>
        public DocumentFacts Document { get; } = new();

        /// <summary>Facts for each of the four slots, indexed by <see cref="TargetSlot"/>.</summary>
        public SlotFacts[] Slots { get; } = new SlotFacts[4];

        /// <summary>One entry per ticked element that still exists.</summary>
        public List<ElementFacts> Elements { get; } = new();

        /// <summary>Ticked ids that no longer exist.</summary>
        public List<long> Missing { get; } = new();
    }

    /// <summary>What the view model asks the Revit thread to do.</summary>
    public sealed class RunRequest
    {
        /// <summary>Ticked element ids.</summary>
        public List<long> ElementIds { get; set; } = new();

        /// <summary>Parameter name chosen for each slot, indexed by <see cref="TargetSlot"/>.</summary>
        public string[] SlotNames { get; set; } = { string.Empty, string.Empty, string.Empty, string.Empty };

        /// <summary>New parameter to create for a slot, or null when it already exists.</summary>
        public NewParameterSpec[] Pending { get; set; } = new NewParameterSpec[4];

        /// <summary>Which level the nearest-level search prefers.</summary>
        public NearestRule Rule { get; set; } = NearestRule.ClosestEitherWay;

        /// <summary>Only levels whose "Building Story" parameter is ticked are candidates.</summary>
        public bool BuildingStoreysOnly { get; set; } = true;

        /// <summary>Differences below this many millimetres count as zero.</summary>
        public double ToleranceMm { get; set; } = 1.0;

        /// <summary>Default for an element whose target parameter already holds a different value.</summary>
        public bool Overwrite { get; set; }

        /// <summary>Per-element overrides of <see cref="Overwrite"/>.</summary>
        public Dictionary<long, ExistingChoice> Choices { get; set; } = new();

        /// <summary>Re-host shape-edited floors and roofs as well. Off by default.</summary>
        public bool RehostShapeEdited { get; set; }

        /// <summary>The choice for one element.</summary>
        public ExistingChoice ChoiceFor(long id) =>
            Choices != null && Choices.TryGetValue(id, out ExistingChoice c) ? c : ExistingChoice.Default;

        /// <summary>The slot's chosen name, trimmed.</summary>
        public string NameFor(TargetSlot slot) => (SlotNames[(int)slot] ?? string.Empty).Trim();
    }

    /// <summary>Result for one element of a run.</summary>
    public sealed class RunRowResult
    {
        /// <summary>Element id value.</summary>
        public long Id { get; set; }

        /// <summary>What happened.</summary>
        public RowState State { get; set; }

        /// <summary>Status text for the grid.</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>How many of the four parameters were written.</summary>
        public int Written { get; set; }
    }

    /// <summary>Result of Write Parameters or Re-host.</summary>
    public sealed class RunResult
    {
        /// <summary>The transaction committed.</summary>
        public bool Committed { get; set; }

        /// <summary>Why nothing was started; null when the run went ahead.</summary>
        public string NotStartedReason { get; set; }

        /// <summary>One entry per element.</summary>
        public List<RunRowResult> Rows { get; } = new();

        /// <summary>Lines for the activity log.</summary>
        public List<(Shared.Models.LogLevel Level, string Text)> Log { get; } = new();

        /// <summary>The facts the run read, so the window can refresh the grid.</summary>
        public FactsResult Facts { get; set; }

        /// <summary>Parameter values set in total.</summary>
        public int ValuesWritten { get; set; }
    }
}
