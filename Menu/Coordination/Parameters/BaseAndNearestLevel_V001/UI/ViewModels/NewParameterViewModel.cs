using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Revit26_Plugin_Revised.BaseAndNearestLevel.V001.Core;

namespace Revit26_Plugin_Revised.BaseAndNearestLevel.V001.UI.ViewModels
{
    /// <summary>
    /// Defines the new shared parameter for one slot. Nothing is created here: the main window creates it,
    /// in the shared parameter file and in the project, when Write Parameters runs. The data type is fixed
    /// by the slot — the two name slots are Text, the two offset slots are Length — and the binding is
    /// always Roofs, Floors and Walls, so the parameter matches what the tool writes.
    /// </summary>
    public sealed partial class NewParameterViewModel : ObservableObject
    {
        private readonly HashSet<string> _existingProjectNames;

        /// <summary>Creates the dialog's view model for <paramref name="slot"/>.</summary>
        public NewParameterViewModel(TargetSlot slot, string suggestedName, string filePath, string fileGroup,
            IEnumerable<string> existingProjectNames, IEnumerable<string> categories)
        {
            Slot = slot;
            Categories = (categories ?? Enumerable.Empty<string>())
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase).ToList();
            _existingProjectNames = new HashSet<string>(existingProjectNames ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            _name = suggestedName ?? string.Empty;
            _filePath = filePath ?? string.Empty;
            _fileGroup = string.IsNullOrWhiteSpace(fileGroup) ? "Data" : fileGroup;
            ReloadGroups();
            Validate();
        }

        /// <summary>Which of the four slots the parameter is for.</summary>
        public TargetSlot Slot { get; }

        /// <summary>"1 Base level name" and so on.</summary>
        public string SlotLabel => $"{TargetSlots.Number(Slot)} {TargetSlots.Label(Slot)}";

        /// <summary>"Text" or "Length"; fixed by the slot, so the dialog shows it disabled.</summary>
        public string TypeName => TargetSlots.TypeName(Slot);

        /// <summary>
        /// The categories the new parameter is bound to: the ones ticked in the window, so the parameter
        /// matches what the tool writes (1.1.0). Set from the constructor, not editable here.
        /// </summary>
        public IReadOnlyList<string> Categories { get; }

        /// <summary>"3 categories" or "no category ticked", under the pill list.</summary>
        public string CategorySummary => Categories.Count == 0
            ? "No category is ticked in the window, so there is nothing to bind to."
            : $"{Categories.Count} categor{(Categories.Count == 1 ? "y" : "ies")}, as ticked in the window.";

        /// <summary>Groups offered for the shared parameter file.</summary>
        public ObservableCollection<string> FileGroups { get; } = new();

        [ObservableProperty] private string _name;
        [ObservableProperty] private string _filePath;
        [ObservableProperty] private string _fileGroup;
        [ObservableProperty] private string _summary = string.Empty;
        [ObservableProperty] private CheckResult _summaryResult = CheckResult.Pass;
        [ObservableProperty] private bool _canCreate;

        partial void OnNameChanged(string value) => Validate();

        partial void OnFilePathChanged(string value)
        {
            ReloadGroups();
            Validate();
        }

        /// <summary>The spec the main window stores for this slot, or null when the dialog is not valid.</summary>
        public NewParameterSpec ToSpec() => CanCreate
            ? new NewParameterSpec
            {
                Name = Name.Trim(),
                FilePath = FilePath.Trim(),
                FileGroup = string.IsNullOrWhiteSpace(FileGroup) ? "Data" : FileGroup.Trim(),
                Slot = Slot
            }
            : null;

        [RelayCommand]
        private void Browse()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Shared parameter file",
                Filter = "Shared parameter file (*.txt)|*.txt|All files (*.*)|*.*",
                CheckFileExists = true
            };
            if (!string.IsNullOrWhiteSpace(FilePath))
            {
                try { dialog.InitialDirectory = Path.GetDirectoryName(FilePath); }
                catch (ArgumentException) { /* a malformed remembered path is simply ignored */ }
            }
            if (dialog.ShowDialog() == true) FilePath = dialog.FileName;
        }

        private void ReloadGroups()
        {
            string wanted = FileGroup;
            FileGroups.Clear();
            IEnumerable<string> groups = string.IsNullOrWhiteSpace(FilePath) || !File.Exists(FilePath)
                ? new[] { "Data" }
                : SharedParamFileReader.ReadGroups(FilePath);
            foreach (string g in groups) FileGroups.Add(g);
            if (!string.IsNullOrWhiteSpace(wanted) && !FileGroups.Contains(wanted)) FileGroups.Add(wanted);
            FileGroup = string.IsNullOrWhiteSpace(wanted) || !FileGroups.Contains(wanted) ? FileGroups.First() : wanted;
        }

        private void Validate()
        {
            string name = (Name ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                Fail("Give the parameter a name.");
                return;
            }
            if (_existingProjectNames.Contains(name))
            {
                Fail($"“{name}” is already a parameter of this project. Pick it in the slot instead of creating it.");
                return;
            }
            if (name.IndexOfAny(new[] { '\t', '\r', '\n' }) >= 0)
            {
                Fail("A parameter name cannot hold a tab or a line break.");
                return;
            }

            string path = (FilePath ?? string.Empty).Trim();
            if (path.Length == 0)
            {
                Fail("Choose the shared parameter file to add it to.");
                return;
            }
            if (!File.Exists(path))
            {
                Fail("That shared parameter file does not exist.");
                return;
            }
            if (!SharedParamFileReader.IsWritable(path))
            {
                Fail("That shared parameter file cannot be written. Close it, or clear its read-only flag.");
                return;
            }

            try
            {
                List<SharedParamFileEntry> matches = SharedParamFileReader.FindAll(SharedParamFileReader.Read(path), name);
                if (matches.Count > 1)
                {
                    Fail($"The file already holds {matches.Count} entries called “{name}”.");
                    return;
                }
                if (matches.Count == 1 && !matches[0].Fits(Slot))
                {
                    Fail($"“{name}” is already in the file as {matches[0].DataType}, not {TypeName}.");
                    return;
                }
                if (matches.Count == 1)
                {
                    Pass($"“{name}” is already in the file as {TypeName}; its GUID will be reused and the parameter bound to {Categories.Count} categor{(Categories.Count == 1 ? "y" : "ies")}.",
                        CheckResult.Warning);
                    return;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            {
                Fail("The shared parameter file cannot be read: " + ex.Message);
                return;
            }

            if (Categories.Count == 0)
            {
                Fail("No category is ticked in the window, so the parameter would have nothing to bind to. Tick at least one category first.");
                return;
            }
            Pass($"“{name}” will be added to the file as a {TypeName} parameter, then bound as a shared instance parameter to {CategorySummary.TrimEnd('.')}.",
                CheckResult.Pass);
        }

        private void Fail(string message)
        {
            Summary = message;
            SummaryResult = CheckResult.Blocked;
            CanCreate = false;
        }

        private void Pass(string message, CheckResult result)
        {
            Summary = message;
            SummaryResult = result;
            CanCreate = true;
        }
    }
}
