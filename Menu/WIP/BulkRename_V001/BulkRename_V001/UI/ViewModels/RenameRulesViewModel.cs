using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin.BulkRename.V001.Core.Services;
using System;

namespace Revit26_Plugin.BulkRename.V001.UI.ViewModels
{
    /// <summary>
    /// The rules card: find/replace, prefix/suffix, tidy, case and numbering. Each command checks its own
    /// inputs, then hands a transform to the window's view model, which runs it on the ticked, visible rows.
    /// The text logic itself is in <see cref="NameRules"/>.
    /// </summary>
    public sealed partial class RenameRulesViewModel : ObservableObject
    {
        private readonly Action<string, Func<string, int, string>> _apply;
        private readonly Action<string, bool> _report;

        /// <summary>Find text of the find/replace rule.</summary>
        [ObservableProperty] private string findText = string.Empty;

        /// <summary>Replacement text of the find/replace rule.</summary>
        [ObservableProperty] private string replaceText = string.Empty;

        /// <summary>Find/replace is case sensitive.</summary>
        [ObservableProperty] private bool matchCase;

        /// <summary>The find text is a regular expression.</summary>
        [ObservableProperty] private bool useRegex;

        /// <summary>Text added to or removed from the start of names.</summary>
        [ObservableProperty] private string prefix = string.Empty;

        /// <summary>Text added to or removed from the end of names.</summary>
        [ObservableProperty] private string suffix = string.Empty;

        /// <summary>Numbering pattern; {n} is the number, {name} the row's current new name.</summary>
        [ObservableProperty] private string numberPattern = "{name}_{n}";

        /// <summary>First number.</summary>
        [ObservableProperty] private int numberStart = 1;

        /// <summary>Difference between one number and the next.</summary>
        [ObservableProperty] private int numberStep = 1;

        /// <summary>Digits the number is padded to with zeros.</summary>
        [ObservableProperty] private int numberDigits = 2;

        /// <summary>Reminder of the numbering tokens.</summary>
        public string NumberingHint => "{n} = number, {name} = the row's current new name";

        /// <summary>Creates the rules.</summary>
        /// <param name="apply">
        /// Runs a rule: receives a label for the status line and a transform from (new name, index of the row
        /// among the targets, in grid order) to the new name.
        /// </param>
        /// <param name="report">Shows a message in the status line; true also writes it to the log as a warning.</param>
        public RenameRulesViewModel(Action<string, Func<string, int, string>> apply, Action<string, bool> report)
        {
            _apply = apply ?? throw new ArgumentNullException(nameof(apply));
            _report = report ?? throw new ArgumentNullException(nameof(report));
        }

        /// <summary>Runs the find/replace rule on the ticked, visible rows.</summary>
        [RelayCommand]
        private void ApplyFindReplace()
        {
            if (string.IsNullOrEmpty(FindText))
            {
                _report("Type the text to find first.", false);
                return;
            }

            if (UseRegex && !NameRules.IsValidRegex(FindText, out string error))
            {
                _report("Not a valid regular expression: " + error, true);
                return;
            }

            string find = FindText, replace = ReplaceText ?? string.Empty;
            bool matchCase = MatchCase, regex = UseRegex;
            _apply("Find & replace", (name, _) => NameRules.Replace(name, find, replace, matchCase, regex));
        }

        /// <summary>Adds the prefix to the ticked, visible rows.</summary>
        [RelayCommand]
        private void AddPrefix()
        {
            string text = Prefix;
            if (RequireText(text, "prefix")) _apply("Add prefix", (name, _) => NameRules.AddPrefix(name, text));
        }

        /// <summary>Removes the prefix from the ticked, visible rows that start with it.</summary>
        [RelayCommand]
        private void RemovePrefix()
        {
            string text = Prefix;
            if (RequireText(text, "prefix")) _apply("Remove prefix", (name, _) => NameRules.RemovePrefix(name, text));
        }

        /// <summary>Adds the suffix to the ticked, visible rows.</summary>
        [RelayCommand]
        private void AddSuffix()
        {
            string text = Suffix;
            if (RequireText(text, "suffix")) _apply("Add suffix", (name, _) => NameRules.AddSuffix(name, text));
        }

        /// <summary>Removes the suffix from the ticked, visible rows that end with it.</summary>
        [RelayCommand]
        private void RemoveSuffix()
        {
            string text = Suffix;
            if (RequireText(text, "suffix")) _apply("Remove suffix", (name, _) => NameRules.RemoveSuffix(name, text));
        }

        /// <summary>Trims names and collapses runs of spaces.</summary>
        [RelayCommand]
        private void CleanSpaces() => _apply("Clean spaces", (name, _) => NameRules.CleanSpaces(name));

        /// <summary>Turns spaces into underscores.</summary>
        [RelayCommand]
        private void SpacesToUnderscores() => _apply("Spaces to underscores", (name, _) => NameRules.SpacesToUnderscores(name));

        /// <summary>Upper-cases names.</summary>
        [RelayCommand]
        private void UpperCase() => _apply("UPPER CASE", (name, _) => NameRules.ToUpper(name));

        /// <summary>Lower-cases names.</summary>
        [RelayCommand]
        private void LowerCase() => _apply("lower case", (name, _) => NameRules.ToLower(name));

        /// <summary>Title-cases names.</summary>
        [RelayCommand]
        private void TitleCase() => _apply("Title Case", (name, _) => NameRules.ToTitle(name));

        /// <summary>Numbers the ticked, visible rows in grid order using the numbering pattern.</summary>
        [RelayCommand]
        private void ApplyNumbering()
        {
            if (!NameRules.PatternHasNumber(NumberPattern))
            {
                _report("The numbering pattern needs {n}, for example {name}_{n}.", false);
                return;
            }

            string pattern = NumberPattern;
            int start = NumberStart, step = NumberStep, digits = NumberDigits;
            _apply("Numbering", (name, index) => NameRules.Number(pattern, name, start + index * step, digits));
        }

        private bool RequireText(string text, string what)
        {
            if (!string.IsNullOrEmpty(text)) return true;
            _report($"Type the {what} first.", false);
            return false;
        }
    }
}
