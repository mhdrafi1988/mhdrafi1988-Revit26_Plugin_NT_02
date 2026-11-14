using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Revit26_Plugin_Revised.BaseLevelToParameter.V001.Core
{
    /// <summary>
    /// Revit-free rules of the tool: the value format, the parameter-name rules, the pre-checks a–e and
    /// each row's status. Kept pure so they can be unit-tested and re-run from the window (for example
    /// when "Overwrite existing values" is toggled) without a Revit call.
    /// </summary>
    public static class BaseLevelRules
    {
        /// <summary>Longest parameter name the tool accepts for a new parameter.</summary>
        public const int MaxNameLength = 25;

        /// <summary>Characters Revit does not allow in a parameter name.</summary>
        public static readonly char[] IllegalNameChars = { '\\', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~' };

        /// <summary>
        /// Offset text: whole millimetres, signed, rounded half away from zero; zero is "0".
        /// Examples: 450 → "+450", -150 → "-150", 0.4 → "0", 2849.6 → "+2850".
        /// </summary>
        public static string FormatOffset(double offsetMm)
        {
            double rounded = Math.Round(offsetMm, MidpointRounding.AwayFromZero);
            if (rounded == 0) return "0";
            string digits = Math.Abs(rounded).ToString("0", CultureInfo.InvariantCulture);
            return (rounded > 0 ? "+" : "-") + digits;
        }

        /// <summary>The value written to the parameter: "&lt;Level Name&gt; (&lt;Offset&gt;)", for example "Level 02 (+450)".</summary>
        public static string FormatValue(string levelName, double offsetMm) =>
            $"{levelName} ({FormatOffset(offsetMm)})";

        /// <summary>
        /// Checks a name for a new parameter: not empty, no leading or trailing space, at most
        /// <see cref="MaxNameLength"/> characters, none of <see cref="IllegalNameChars"/>.
        /// Returns null when the name is fine, else the reason.
        /// </summary>
        public static string ValidateNewName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Name is empty.";
            if (name.Trim().Length != name.Length) return "Name starts or ends with a space.";
            if (name.Length > MaxNameLength) return $"Name has {name.Length} characters; the limit is {MaxNameLength}.";
            int bad = name.IndexOfAny(IllegalNameChars);
            if (bad >= 0) return $"Name contains '{name[bad]}', which Revit does not allow.";
            return null;
        }

        /// <summary>
        /// Builds the pre-check lines a–e from what the Revit side found.
        /// <paramref name="elements"/> are the ticked elements (base level already read).
        /// </summary>
        public static List<PrecheckItem> EvaluateChecks(TargetFacts t, IReadOnlyList<ElementFacts> elements, bool overwrite) =>
            EvaluateChecks(t, elements, overwrite, ValueFormat.LevelParenOffset, null);

        /// <summary>
        /// Pre-check lines a–e, plus "f · Value format" when <paramref name="format"/> is
        /// <see cref="ValueFormat.Custom"/> (Blocked when the custom pattern is not valid).
        /// </summary>
        public static List<PrecheckItem> EvaluateChecks(TargetFacts t, IReadOnlyList<ElementFacts> elements, bool overwrite,
            ValueFormat format, string customPattern)
        {
            var list = new List<PrecheckItem>
            {
                CheckName(t),
                CheckProject(t),
                CheckFile(t),
                CheckCategories(t, elements),
                CheckDocument(t, elements, overwrite)
            };
            if (format == ValueFormat.Custom)
            {
                string error = ValueFormatter.ValidateCustom(customPattern);
                list.Add(error == null
                    ? new PrecheckItem("f · Value format", CheckResult.Pass, $"Custom pattern “{customPattern}”: {ValueFormatter.Sample(format, customPattern)}")
                    : new PrecheckItem("f · Value format", CheckResult.Blocked, error));
            }
            return list;
        }

        /// <summary>True when any check is Blocked.</summary>
        public static bool AnyBlocked(IEnumerable<PrecheckItem> checks) => checks.Any(c => c.Result == CheckResult.Blocked);

        /// <summary>
        /// Status of one ticked element. <paramref name="blocked"/> is true when any pre-check is Blocked.
        /// <paramref name="variesAcrossGroups"/> is whether the target value may differ between group instances.
        /// </summary>
        public static RowVerdict EvaluateRow(ElementFacts e, bool blocked, bool overwrite, bool variesAcrossGroups) =>
            EvaluateRow(e, blocked, overwrite, variesAcrossGroups, null);

        /// <summary>
        /// Status of one ticked element. When <paramref name="newValue"/> equals the element's current value the row
        /// is <see cref="RowState.UpToDate"/> and is never rewritten.
        /// </summary>
        public static RowVerdict EvaluateRow(ElementFacts e, bool blocked, bool overwrite, bool variesAcrossGroups, string newValue)
        {
            if (blocked) return new RowVerdict(RowState.Blocked, "Blocked · see pre-checks");
            if (!e.HasLevel) return new RowVerdict(RowState.Warning, "Skipped · no base level");
            if (e.CategoryUnsupported) return new RowVerdict(RowState.Warning, "Skipped · category takes no parameters");
            if (!string.IsNullOrEmpty(e.OwnedBy)) return new RowVerdict(RowState.Warning, $"Skipped · owned by {e.OwnedBy}");
            if (e.OutOfDate) return new RowVerdict(RowState.Warning, "Skipped · changed in central, reload latest");
            if (e.InGroup && !variesAcrossGroups) return new RowVerdict(RowState.Warning, "Skipped · in a group (value cannot vary by group)");
            if (e.ParamReadOnly) return new RowVerdict(RowState.Warning, "Skipped · parameter is read-only");
            if (newValue != null && string.Equals(e.CurrentValue, newValue, StringComparison.Ordinal))
                return new RowVerdict(RowState.UpToDate, "Up to date");
            if (!overwrite && !string.IsNullOrEmpty(e.CurrentValue)) return new RowVerdict(RowState.Warning, "Skipped · has value");
            return new RowVerdict(RowState.Ready, "Ready");
        }

        /// <summary>The summary line "X ready | Y warnings | Z blocked" for the given row states.</summary>
        public static string Summary(int ready, int warnings, int blocked) =>
            $"{ready} ready | {warnings} warnings | {blocked} blocked";

        /// <summary>The summary line with "· N up to date" added when <paramref name="upToDate"/> is above 0.</summary>
        public static string Summary(int ready, int warnings, int blocked, int upToDate) =>
            Summary(ready, warnings, blocked) + (upToDate > 0 ? $" · {upToDate} up to date" : string.Empty);

        /// <summary>The run summary line "X assigned | Y skipped | Z failed".</summary>
        public static string RunSummary(int assigned, int skipped, int failed) =>
            $"{assigned} assigned | {skipped} skipped | {failed} failed";

        // ── a · Name ──────────────────────────────────────────────────────────
        private static PrecheckItem CheckName(TargetFacts t)
        {
            const string title = "a · Name";
            if (string.IsNullOrWhiteSpace(t.Name))
                return new PrecheckItem(title, CheckResult.Blocked, "Pick a parameter, or create one with New Parameter….");

            if (t.IsNew)
            {
                string error = ValidateNewName(t.Name);
                if (error != null) return new PrecheckItem(title, CheckResult.Blocked, error);
            }
            if (t.BuiltInClashCategories.Count > 0)
                return new PrecheckItem(title, CheckResult.Blocked,
                    $"“{t.Name}” is a built-in parameter name on {Join(t.BuiltInClashCategories)}. Use another name.");

            string length = t.IsNew ? $", {t.Name.Length} / {MaxNameLength} characters" : string.Empty;
            return new PrecheckItem(title, CheckResult.Pass, $"“{t.Name}”{length}, no invalid characters, no clash with a built-in parameter.");
        }

        // ── b · Already in project ────────────────────────────────────────────
        private static PrecheckItem CheckProject(TargetFacts t)
        {
            const string title = "b · Already in project";
            if (string.IsNullOrWhiteSpace(t.Name)) return new PrecheckItem(title, CheckResult.Blocked, "No parameter picked.");

            if (t.ProjectMatchCount > 1)
                return new PrecheckItem(title, CheckResult.Blocked, $"The project has {t.ProjectMatchCount} parameters named “{t.Name}”. Remove the duplicates first.");

            if (t.ProjectMatchCount == 0)
                return t.IsNew
                    ? new PrecheckItem(title, CheckResult.Pass, "Not in the project; Run creates it as a shared instance Text parameter (Properties group: Data).")
                    : new PrecheckItem(title, CheckResult.Blocked, $"The project has no parameter named “{t.Name}”. Create it with New Parameter….");

            if (!t.IsText)
                return new PrecheckItem(title, CheckResult.Blocked, $"“{t.Name}” is in the project but its data type is not Text.");
            if (!t.IsInstance)
                return new PrecheckItem(title, CheckResult.Blocked, $"“{t.Name}” is in the project as a type parameter; an instance parameter is needed.");

            string kind = t.IsShared ? "shared" : "project";
            if (t.MissingCategories.Count > 0)
                return new PrecheckItem(title, CheckResult.Warning,
                    $"Found: {kind}, instance, Text → reused. Not bound to {Join(t.MissingCategories)}; Run adds {(t.MissingCategories.Count == 1 ? "this category" : $"these {t.MissingCategories.Count} categories")} to the binding.");
            return new PrecheckItem(title, CheckResult.Pass, $"Found: {kind}, instance, Text → reused. Bound to every category of the ticked elements.");
        }

        // ── c · Shared parameter file ─────────────────────────────────────────
        private static PrecheckItem CheckFile(TargetFacts t)
        {
            const string title = "c · Shared parameter file";
            bool needed = t.IsNew && t.ProjectMatchCount == 0;

            if (!needed)
            {
                if (t.ProjectMatchCount == 1 && !t.IsShared)
                    return new PrecheckItem(title, CheckResult.Pass, "Project parameter (not shared); no file needed.");
                if (t.ProjectMatchCount != 1 || string.IsNullOrWhiteSpace(t.FilePath) || !t.FileExists || t.FileEntry == null)
                    return new PrecheckItem(title, CheckResult.Pass, "Not needed: the parameter is already in the project.");
                if (t.ProjectGuid.HasValue && t.FileEntry.Guid != t.ProjectGuid.Value)
                    return new PrecheckItem(title, CheckResult.Blocked,
                        $"“{t.Name}” in {FileName(t.FilePath)} has GUID {Short(t.FileEntry.Guid)}, but the project parameter has {Short(t.ProjectGuid.Value)}.");
                if (!t.FileEntry.IsText)
                    return new PrecheckItem(title, CheckResult.Blocked, $"“{t.Name}” in {FileName(t.FilePath)} is {t.FileEntry.DataType}, not Text.");
                return new PrecheckItem(title, CheckResult.Pass, $"{t.FilePath}: same name, GUID matches the project parameter ({Short(t.FileEntry.Guid)}).");
            }

            if (string.IsNullOrWhiteSpace(t.FilePath))
                return new PrecheckItem(title, CheckResult.Blocked, "No shared parameter file is set. Pick one in New Parameter… (Browse…).");
            if (!t.FileExists)
                return new PrecheckItem(title, CheckResult.Blocked, $"{t.FilePath} does not exist. Pick another file in New Parameter….");
            if (t.FileReadError != null)
                return new PrecheckItem(title, CheckResult.Blocked, $"{t.FilePath} could not be read: {t.FileReadError}");
            if (!t.FileWritable)
                return new PrecheckItem(title, CheckResult.Blocked, $"{t.FilePath} is read-only or locked. Pick a writable file in New Parameter….");
            if (t.FileMatchCount > 1)
                return new PrecheckItem(title, CheckResult.Blocked,
                    $"“{t.Name}” is in {FileName(t.FilePath)} {t.FileMatchCount} times. Remove the duplicates from the file or use another name.");
            if (t.FileEntry != null && !t.FileEntry.IsText)
                return new PrecheckItem(title, CheckResult.Blocked, $"“{t.Name}” is already in {FileName(t.FilePath)} as {t.FileEntry.DataType}, not Text. Use another name.");
            if (t.FileEntry != null && !string.IsNullOrEmpty(t.FileGuidUsedBy))
                return new PrecheckItem(title, CheckResult.Blocked,
                    $"The GUID of “{t.Name}” in {FileName(t.FilePath)} ({Short(t.FileEntry.Guid)}) is already used in the project by “{t.FileGuidUsedBy}”. Use another name or fix the file.");
            if (t.FileEntry != null)
                return new PrecheckItem(title, CheckResult.Pass, $"{t.FilePath}: “{t.Name}” is already in the file (group “{t.FileEntry.GroupName}”); its GUID {Short(t.FileEntry.Guid)} is reused.");
            return new PrecheckItem(title, CheckResult.Pass, $"{t.FilePath}: exists and is writable; Run adds “{t.Name}” with a new GUID.");
        }

        // ── d · Categories ────────────────────────────────────────────────────
        private static PrecheckItem CheckCategories(TargetFacts t, IReadOnlyList<ElementFacts> elements)
        {
            const string title = "d · Categories";
            if (elements.Count == 0)
                return new PrecheckItem(title, CheckResult.Blocked, "No element is ticked.");
            int supported = t.CategoryCount - t.UnsupportedCategories.Count;
            if (supported <= 0)
                return new PrecheckItem(title, CheckResult.Blocked, "None of the ticked elements' categories allow bound parameters.");
            if (t.UnsupportedCategories.Count > 0)
                return new PrecheckItem(title, CheckResult.Warning,
                    $"{supported} of {t.CategoryCount} categories allow bound parameters. {Join(t.UnsupportedCategories)} do not; their elements are skipped.");
            return new PrecheckItem(title, CheckResult.Pass, $"{t.CategoryCount} of {t.CategoryCount} categories allow bound parameters.");
        }

        // ── e · Document and elements ─────────────────────────────────────────
        private static PrecheckItem CheckDocument(TargetFacts t, IReadOnlyList<ElementFacts> elements, bool overwrite)
        {
            const string title = "e · Document and elements";
            if (t.IsFamilyDocument) return new PrecheckItem(title, CheckResult.Blocked, "This is a family document. Open a project.");
            if (t.IsReadOnly) return new PrecheckItem(title, CheckResult.Blocked, "The document is read-only.");

            int owned = elements.Count(e => !string.IsNullOrEmpty(e.OwnedBy));
            int outOfDate = elements.Count(e => e.OutOfDate);
            int readOnly = elements.Count(e => e.ParamReadOnly);
            int inGroup = t.VariesAcrossGroups ? 0 : elements.Count(e => e.InGroup);
            int withValue = elements.Count(e => !string.IsNullOrEmpty(e.CurrentValue));
            int noLevel = elements.Count(e => !e.HasLevel);

            var parts = new List<string> { $"Project document, editable{(t.IsWorkshared ? ", workshared" : string.Empty)}." };
            if (owned > 0) parts.Add($"{owned} element(s) owned by another user → skipped.");
            if (outOfDate > 0) parts.Add($"{outOfDate} element(s) changed in central → skipped (reload latest).");
            parts.Add($"Parameter read-only on {readOnly} element(s){(readOnly > 0 ? " → skipped" : string.Empty)}.");
            if (inGroup > 0) parts.Add($"{inGroup} element(s) in groups, value cannot vary by group → skipped.");
            parts.Add(withValue == 0
                ? "No element has a value yet."
                : overwrite
                    ? $"{withValue} element(s) already have a value → overwritten."
                    : $"{withValue} element(s) already have a value → skipped (Overwrite is off).");
            if (noLevel > 0) parts.Add($"{noLevel} element(s) have no base level → skipped.");

            bool warn = owned + outOfDate + readOnly + inGroup + noLevel > 0 || (withValue > 0 && !overwrite);
            return new PrecheckItem(title, warn ? CheckResult.Warning : CheckResult.Pass, string.Join(" ", parts));
        }

        private static string Join(IReadOnlyCollection<string> names) =>
            names.Count <= 1 ? names.FirstOrDefault() ?? string.Empty
                : string.Join(", ", names.Take(names.Count - 1)) + " and " + names.Last();

        private static string Short(Guid g)
        {
            string s = g.ToString("N");
            return s.Substring(0, 4) + "…" + s.Substring(s.Length - 4);
        }

        private static string FileName(string path)
        {
            try { return System.IO.Path.GetFileName(path); }
            catch { return path; }
        }
    }
}
