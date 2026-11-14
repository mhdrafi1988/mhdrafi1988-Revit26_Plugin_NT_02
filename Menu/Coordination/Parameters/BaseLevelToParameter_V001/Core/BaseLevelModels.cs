using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.BaseLevelToParameter.V001.Core
{
    /// <summary>Result of one pre-check (a–e), and the severity of a row.</summary>
    public enum CheckResult
    {
        /// <summary>Nothing to report.</summary>
        Pass,
        /// <summary>Something is skipped or changed automatically; the run can go ahead.</summary>
        Warning,
        /// <summary>The run cannot go ahead until this is fixed.</summary>
        Blocked
    }

    /// <summary>State of one element row in the grid.</summary>
    public enum RowState
    {
        /// <summary>The row is not ticked.</summary>
        NotChecked,
        /// <summary>Ticked, but its base level has not been read yet.</summary>
        Pending,
        /// <summary>Will be written by Run.</summary>
        Ready,
        /// <summary>Already has the new value; Run does not rewrite it.</summary>
        UpToDate,
        /// <summary>Will be skipped by Run (reason in the status text).</summary>
        Warning,
        /// <summary>A blocking pre-check stops the run.</summary>
        Blocked,
        /// <summary>Run wrote the value.</summary>
        Assigned,
        /// <summary>Run skipped the element.</summary>
        Skipped,
        /// <summary>Run could not write the value.</summary>
        Failed
    }

    /// <summary>One pre-check line shown in the window: "a · Name", result, detail.</summary>
    public sealed class PrecheckItem
    {
        /// <summary>Creates a pre-check line.</summary>
        public PrecheckItem(string title, CheckResult result, string detail)
        {
            Title = title;
            Result = result;
            Detail = detail ?? string.Empty;
        }

        /// <summary>Check title, for example "a · Name".</summary>
        public string Title { get; }

        /// <summary>Pass, Warning or Blocked.</summary>
        public CheckResult Result { get; }

        /// <summary>What was found and what Run will do about it.</summary>
        public string Detail { get; }
    }

    /// <summary>
    /// A parameter the user defined in the New Parameter dialog. It is created (in the shared parameter
    /// file and in the project) only when Run is pressed.
    /// </summary>
    public sealed class NewParameterSpec
    {
        /// <summary>Parameter name (max 25 characters).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Full path of the shared parameter file the definition goes into.</summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>Group inside the shared parameter file.</summary>
        public string FileGroup { get; set; } = string.Empty;
    }

    /// <summary>What the Revit side found about the target parameter, the document and the shared parameter file.</summary>
    public sealed class TargetFacts
    {
        /// <summary>Name typed or picked as the target.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>True when the target is a pending <see cref="NewParameterSpec"/> rather than a parameter already in the project.</summary>
        public bool IsNew { get; set; }

        /// <summary>Categories (of the ticked elements) that already have a built-in parameter with this name.</summary>
        public List<string> BuiltInClashCategories { get; set; } = new();

        /// <summary>How many project parameters have this name (more than one is blocked).</summary>
        public int ProjectMatchCount { get; set; }

        /// <summary>True when the project parameter's data type is Text.</summary>
        public bool IsText { get; set; }

        /// <summary>True when the project parameter is bound as an instance parameter.</summary>
        public bool IsInstance { get; set; }

        /// <summary>True when the project parameter is a shared parameter.</summary>
        public bool IsShared { get; set; }

        /// <summary>GUID of the shared project parameter, when it is shared.</summary>
        public Guid? ProjectGuid { get; set; }

        /// <summary>True when the project parameter's value can vary between group instances.</summary>
        public bool VariesAcrossGroups { get; set; }

        /// <summary>Ticked-element categories that allow parameters but are not in the current binding.</summary>
        public List<string> MissingCategories { get; set; } = new();

        /// <summary>Ticked-element categories that do not allow bound parameters.</summary>
        public List<string> UnsupportedCategories { get; set; } = new();

        /// <summary>Number of distinct categories among the ticked elements.</summary>
        public int CategoryCount { get; set; }

        /// <summary>Shared parameter file path used for the check (the new parameter's, or Revit's current one).</summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>True when the file exists.</summary>
        public bool FileExists { get; set; }

        /// <summary>True when the file can be written.</summary>
        public bool FileWritable { get; set; }

        /// <summary>Error text when the file could not be read, else null.</summary>
        public string FileReadError { get; set; }

        /// <summary>The file's entry with the target name, or null.</summary>
        public SharedParamFileEntry FileEntry { get; set; }

        /// <summary>How many entries in the file have the target name (more than one is blocked for a new parameter).</summary>
        public int FileMatchCount { get; set; }

        /// <summary>
        /// Name of the project parameter that already uses the GUID of the file's entry, when that name differs
        /// from the target name; null otherwise. Revit cannot add the entry then, so it is blocked.
        /// </summary>
        public string FileGuidUsedBy { get; set; }

        /// <summary>True for a family document.</summary>
        public bool IsFamilyDocument { get; set; }

        /// <summary>True when the document cannot be edited.</summary>
        public bool IsReadOnly { get; set; }

        /// <summary>True for a workshared (central / cloud) model.</summary>
        public bool IsWorkshared { get; set; }
    }

    /// <summary>What the Revit side found about one ticked element.</summary>
    public sealed class ElementFacts
    {
        /// <summary>Element id value.</summary>
        public long Id { get; set; }

        /// <summary>Category name.</summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>True when a base level was found.</summary>
        public bool HasLevel { get; set; }

        /// <summary>Base level name.</summary>
        public string LevelName { get; set; } = string.Empty;

        /// <summary>Base offset in millimetres (0 when the element has a level but no offset parameter).</summary>
        public double OffsetMm { get; set; }

        /// <summary>Name of the parameter the level was read from, for the log.</summary>
        public string LevelSource { get; set; } = string.Empty;

        /// <summary>Current value of the target parameter on the element ("" when empty or not bound).</summary>
        public string CurrentValue { get; set; } = string.Empty;

        /// <summary>User who owns the element in a workshared model, or null.</summary>
        public string OwnedBy { get; set; }

        /// <summary>True when the element changed in the central model since this copy was synced.</summary>
        public bool OutOfDate { get; set; }

        /// <summary>True when the element is a member of a model group.</summary>
        public bool InGroup { get; set; }

        /// <summary>True when the target parameter is read-only on this element.</summary>
        public bool ParamReadOnly { get; set; }

        /// <summary>True when the element's category does not allow bound parameters.</summary>
        public bool CategoryUnsupported { get; set; }
    }

    /// <summary>Status of one row after evaluation.</summary>
    public readonly struct RowVerdict
    {
        /// <summary>Creates a verdict.</summary>
        public RowVerdict(RowState state, string text)
        {
            State = state;
            Text = text;
        }

        /// <summary>Row state.</summary>
        public RowState State { get; }

        /// <summary>Status text shown in the grid.</summary>
        public string Text { get; }
    }

    /// <summary>Outcome of one element in a run.</summary>
    public sealed class RunRowResult
    {
        /// <summary>Element id value.</summary>
        public long Id { get; set; }

        /// <summary>Assigned, Skipped or Failed.</summary>
        public RowState State { get; set; }

        /// <summary>Status text.</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>Value written, when assigned.</summary>
        public string Value { get; set; } = string.Empty;
    }
}
