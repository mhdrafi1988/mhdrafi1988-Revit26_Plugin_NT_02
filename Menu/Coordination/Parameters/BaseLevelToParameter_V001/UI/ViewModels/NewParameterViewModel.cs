using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Revit26_Plugin_Revised.BaseLevelToParameter.V001.Core;

namespace Revit26_Plugin_Revised.BaseLevelToParameter.V001.UI.ViewModels
{
    /// <summary>
    /// The New Parameter dialog: name, shared parameter file and group. The parameter is only created
    /// when Run is pressed in the main window; this dialog checks what it can without Revit (name rules,
    /// project names, the file) and returns a <see cref="NewParameterSpec"/>.
    /// </summary>
    public sealed partial class NewParameterViewModel : ObservableObject
    {
        private readonly HashSet<string> _projectNames;

        /// <summary>Creates the dialog state.</summary>
        /// <param name="name">Name to start with (what was typed in the target box).</param>
        /// <param name="filePath">Shared parameter file to start with.</param>
        /// <param name="fileGroup">Group inside the file.</param>
        /// <param name="projectNames">Names of the parameters already in the project.</param>
        /// <param name="categories">Categories of the ticked elements (the parameter is bound to these).</param>
        public NewParameterViewModel(string name, string filePath, string fileGroup, IEnumerable<string> projectNames, IEnumerable<string> categories)
        {
            _projectNames = new HashSet<string>(projectNames ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (string c in categories ?? Enumerable.Empty<string>()) Categories.Add(c);
            _name = name ?? string.Empty;
            _filePath = filePath ?? string.Empty;
            _fileGroup = string.IsNullOrWhiteSpace(fileGroup) ? "Data" : fileGroup;
            Recheck();
        }

        /// <summary>Raised when the dialog should close; the argument is the dialog result.</summary>
        public event Action<bool> CloseRequested;

        /// <summary>Categories the new parameter will be bound to.</summary>
        public ObservableCollection<string> Categories { get; } = new();

        /// <summary>Groups found in the shared parameter file.</summary>
        public ObservableCollection<string> FileGroups { get; } = new();

        /// <summary>Pre-check lines shown in the dialog.</summary>
        public ObservableCollection<PrecheckItem> Checks { get; } = new();

        [ObservableProperty] private string _name;
        [ObservableProperty] private string _filePath;
        [ObservableProperty] private string _fileGroup;
        [ObservableProperty] private string _nameCounter = string.Empty;
        [ObservableProperty] private string _summary = string.Empty;
        [ObservableProperty] private bool _canCreate;

        /// <summary>Categories as one line.</summary>
        public string CategoriesText => Categories.Count == 0
            ? "No element is ticked yet; the parameter is bound to the categories of the elements ticked when you press Run."
            : string.Join(", ", Categories);

        /// <summary>The result, valid after Create.</summary>
        public NewParameterSpec Result => new()
        {
            Name = (Name ?? string.Empty).Trim(),
            FilePath = (FilePath ?? string.Empty).Trim(),
            FileGroup = string.IsNullOrWhiteSpace(FileGroup) ? "Data" : FileGroup.Trim()
        };

        partial void OnNameChanged(string value) => Recheck();
        partial void OnFilePathChanged(string value) => Recheck();
        partial void OnFileGroupChanged(string value) => Recheck();

        [RelayCommand]
        private void Browse()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select shared parameter file",
                Filter = "Shared parameter files (*.txt)|*.txt|All files (*.*)|*.*",
                CheckFileExists = true
            };
            try
            {
                string dir = string.IsNullOrWhiteSpace(FilePath) ? null : Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) dlg.InitialDirectory = dir;
            }
            catch { /* a bad remembered path just opens the dialog at its default folder */ }
            if (dlg.ShowDialog() == true) FilePath = dlg.FileName;
        }

        [RelayCommand]
        private void Create()
        {
            Recheck();
            if (CanCreate) CloseRequested?.Invoke(true);
        }

        private bool _rechecking;

        private void Recheck()
        {
            if (_rechecking) return;
            _rechecking = true;
            try { RecheckCore(); }
            finally { _rechecking = false; }
        }

        private void RecheckCore()
        {
            Checks.Clear();
            string name = Name ?? string.Empty;
            NameCounter = $"{name.Length} / {BaseLevelRules.MaxNameLength}";

            // Name
            string nameError = BaseLevelRules.ValidateNewName(name);
            if (nameError == null && _projectNames.Contains(name.Trim()))
                nameError = $"“{name.Trim()}” is already in the project. Close this dialog and pick it in the Parameter list.";
            Checks.Add(new PrecheckItem("Name", nameError == null ? CheckResult.Pass : CheckResult.Blocked,
                nameError ?? "No invalid characters (\\ : { } [ ] | ; < > ? ` ~). The built-in name check runs in the main window."));

            // File
            string text = null;
            PrecheckItem file;
            string path = (FilePath ?? string.Empty).Trim();
            if (path.Length == 0)
                file = new PrecheckItem("Shared parameter file", CheckResult.Blocked, "No shared parameter file is set. Pick one with Browse… to continue.");
            else if (!File.Exists(path))
                file = new PrecheckItem("Shared parameter file", CheckResult.Blocked, $"{path} does not exist.");
            else if (!SharedParamFileReader.IsWritable(path))
                file = new PrecheckItem("Shared parameter file", CheckResult.Blocked, $"{path} is read-only or locked.");
            else
            {
                try
                {
                    using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                    text = reader.ReadToEnd();
                    var matches = SharedParamFileReader.FindAll(SharedParamFileReader.Parse(text), name);
                    SharedParamFileEntry entry = matches.Count > 0 ? matches[0] : null;
                    file = matches.Count > 1
                        ? new PrecheckItem("Shared parameter file", CheckResult.Blocked, $"“{name.Trim()}” is in the file {matches.Count} times. Remove the duplicates from the file or use another name.")
                        : entry == null
                        ? new PrecheckItem("Shared parameter file", CheckResult.Pass, "Writable. Name not in the file; a new GUID will be added.")
                        : entry.IsText
                            ? new PrecheckItem("Shared parameter file", CheckResult.Pass, $"Writable. “{entry.Name}” is already in the file (group “{entry.GroupName}”); its GUID is reused. The main window checks that the GUID is not used by another parameter.")
                            : new PrecheckItem("Shared parameter file", CheckResult.Blocked, $"“{entry.Name}” is already in the file as {entry.DataType}, not Text. Use another name.");
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    file = new PrecheckItem("Shared parameter file", CheckResult.Blocked, $"{path} could not be read: {ex.Message}");
                }
            }
            Checks.Add(file);
            RefreshGroups(text);

            // Categories
            Checks.Add(Categories.Count == 0
                ? new PrecheckItem("Categories", CheckResult.Warning, "No element is ticked yet.")
                : new PrecheckItem("Categories", CheckResult.Pass, $"Bound to {Categories.Count} categor{(Categories.Count == 1 ? "y" : "ies")} of the ticked elements; the main window checks each one."));

            int pass = Checks.Count(c => c.Result == CheckResult.Pass);
            int warn = Checks.Count(c => c.Result == CheckResult.Warning);
            int blocked = Checks.Count(c => c.Result == CheckResult.Blocked);
            Summary = $"{pass} pass | {warn} warnings | {blocked} blocked";
            CanCreate = blocked == 0;
            OnPropertyChanged(nameof(CategoriesText));
        }

        private void RefreshGroups(string fileText)
        {
            var groups = SharedParamFileReader.GroupNames(fileText);
            if (groups.SequenceEqual(FileGroups)) return;
            string keep = FileGroup;
            FileGroups.Clear();
            foreach (string g in groups) FileGroups.Add(g);
            if (FileGroup != keep) FileGroup = keep;
        }
    }
}
