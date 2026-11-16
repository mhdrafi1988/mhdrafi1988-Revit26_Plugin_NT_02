using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Data;
using Microsoft.Win32;
using Revit26_Plugin_Revised.BaseLevelToParameter.V001.Core;

namespace Revit26_Plugin_Revised.BaseLevelToParameter.V001.UI.ViewModels
{
    /// <summary>
    /// The New Parameter dialog: name, shared parameter file, group and (1.4.0) the categories it is bound to.
    /// The parameter is only created
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
            : this(name, filePath, fileGroup, projectNames, categories, null, null, null)
        {
        }

        /// <summary>Creates the dialog state with the "Only the selected categories" list (1.4.0).</summary>
        /// <param name="name">Name to start with (what was typed in the target box).</param>
        /// <param name="filePath">Shared parameter file to start with.</param>
        /// <param name="fileGroup">Group inside the file.</param>
        /// <param name="projectNames">Names of the parameters already in the project.</param>
        /// <param name="categories">Categories of the ticked elements (the default binding).</param>
        /// <param name="availableCategories">Model categories of the project that allow bound parameters; null hides the option's list.</param>
        /// <param name="tickedCounts">Ticked elements per category name, shown in the list.</param>
        /// <param name="selectedCategories">
        /// Categories picked earlier for this pending parameter. Null or empty starts on "Categories of the ticked elements".
        /// </param>
        public NewParameterViewModel(string name, string filePath, string fileGroup, IEnumerable<string> projectNames, IEnumerable<string> categories,
            IEnumerable<string> availableCategories, IReadOnlyDictionary<string, int> tickedCounts, IEnumerable<string> selectedCategories)
        {
            _projectNames = new HashSet<string>(projectNames ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (string c in categories ?? Enumerable.Empty<string>()) Categories.Add(c);

            var picked = new HashSet<string>(selectedCategories ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var names = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
            foreach (string c in availableCategories ?? Enumerable.Empty<string>()) if (!string.IsNullOrWhiteSpace(c)) names.Add(c);
            foreach (string c in Categories) names.Add(c); // ticked categories are always listed
            foreach (string c in picked) names.Add(c);
            foreach (string c in names)
            {
                int count = tickedCounts != null && tickedCounts.TryGetValue(c, out int n) ? n : 0;
                var option = new CategoryOptionViewModel(c, count, picked.Contains(c));
                option.SelectionChanged += (_, _) => Recheck();
                CategoryOptions.Add(option);
            }
            CategoryOptionsView = CollectionViewSource.GetDefaultView(CategoryOptions);
            CategoryOptionsView.Filter = o => FilterCategory(o as CategoryOptionViewModel);
            _onlySelectedCategories = picked.Count > 0;

            _name = name ?? string.Empty;
            _filePath = filePath ?? string.Empty;
            _fileGroup = string.IsNullOrWhiteSpace(fileGroup) ? "Data" : fileGroup;
            Recheck();
        }

        /// <summary>Raised when the dialog should close; the argument is the dialog result.</summary>
        public event Action<bool> CloseRequested;

        /// <summary>Categories the new parameter will be bound to.</summary>
        public ObservableCollection<string> Categories { get; } = new();

        /// <summary>Every category the new parameter can be bound to, for the "Only the selected categories" grid (1.4.0).</summary>
        public ObservableCollection<CategoryOptionViewModel> CategoryOptions { get; } = new();

        /// <summary><see cref="CategoryOptions"/> filtered by <see cref="CategorySearch"/> (and <see cref="TickedCategoriesOnly"/>).</summary>
        public ICollectionView CategoryOptionsView { get; }

        /// <summary>
        /// False (the default, as before 1.4.0): bound to the categories of the ticked elements.
        /// True: bound only to the categories ticked in the grid.
        /// </summary>
        [ObservableProperty] private bool _onlySelectedCategories;

        /// <summary>Search text for the category grid (matches anywhere in the name).</summary>
        [ObservableProperty] private string _categorySearch = string.Empty;

        /// <summary>Show only categories that have ticked elements.</summary>
        [ObservableProperty] private bool _tickedCategoriesOnly;

        /// <summary>"Categories of the ticked elements" radio button.</summary>
        public bool TickedElementCategories
        {
            get => !OnlySelectedCategories;
            set { if (value) OnlySelectedCategories = false; }
        }

        /// <summary>Label of the default radio button, with the count.</summary>
        public string TickedElementCategoriesLabel => $"Categories of the ticked elements ({Categories.Count})";

        /// <summary>"8 of 52 selected · 3 shown" line under the grid.</summary>
        public string SelectedCategoriesText
        {
            get
            {
                int selected = CategoryOptions.Count(o => o.IsSelected);
                int shown = CategoryOptionsView?.Cast<object>().Count() ?? 0;
                return $"{selected} of {CategoryOptions.Count} selected · {shown} shown";
            }
        }

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
        public string CategoriesText => OnlySelectedCategories
            ? (CategoryOptions.Any(o => o.IsSelected)
                ? string.Join(", ", CategoryOptions.Where(o => o.IsSelected).Select(o => o.Name))
                : "No category selected.")
            : Categories.Count == 0
            ? "No element is ticked yet; the parameter is bound to the categories of the elements ticked when you press Run."
            : string.Join(", ", Categories);

        /// <summary>The result, valid after Create.</summary>
        public NewParameterSpec Result => new()
        {
            Name = (Name ?? string.Empty).Trim(),
            FilePath = (FilePath ?? string.Empty).Trim(),
            FileGroup = string.IsNullOrWhiteSpace(FileGroup) ? "Data" : FileGroup.Trim(),
            Categories = OnlySelectedCategories
                ? CategoryOptions.Where(o => o.IsSelected).Select(o => o.Name).ToList()
                : new List<string>()
        };

        partial void OnNameChanged(string value) => Recheck();
        partial void OnFilePathChanged(string value) => Recheck();
        partial void OnFileGroupChanged(string value) => Recheck();

        partial void OnOnlySelectedCategoriesChanged(bool value)
        {
            // First switch to the list: start from the ticked elements' categories.
            if (value && !CategoryOptions.Any(o => o.IsSelected))
                foreach (CategoryOptionViewModel o in CategoryOptions.Where(o => o.HasTicked)) o.IsSelected = true;
            OnPropertyChanged(nameof(TickedElementCategories));
            Recheck();
        }

        partial void OnCategorySearchChanged(string value) => RefreshCategoryView();
        partial void OnTickedCategoriesOnlyChanged(bool value) => RefreshCategoryView();

        private void RefreshCategoryView()
        {
            CategoryOptionsView?.Refresh();
            OnPropertyChanged(nameof(SelectedCategoriesText));
        }

        private bool FilterCategory(CategoryOptionViewModel o)
        {
            if (o == null) return false;
            if (TickedCategoriesOnly && !o.HasTicked) return false;
            string q = (CategorySearch ?? string.Empty).Trim();
            return q.Length == 0 || o.Name.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        /// <summary>Ticks every category the grid shows.</summary>
        [RelayCommand]
        private void SelectShownCategories() => SetShown(true);

        /// <summary>Unticks every category the grid shows.</summary>
        [RelayCommand]
        private void ClearShownCategories() => SetShown(false);

        /// <summary>Ticks exactly the categories of the ticked elements.</summary>
        [RelayCommand]
        private void SelectTickedCategories()
        {
            _rechecking = true;
            try { foreach (CategoryOptionViewModel o in CategoryOptions) o.IsSelected = o.HasTicked; }
            finally { _rechecking = false; }
            Recheck();
        }

        private void SetShown(bool value)
        {
            _rechecking = true;
            try { foreach (CategoryOptionViewModel o in CategoryOptionsView.Cast<CategoryOptionViewModel>().ToList()) o.IsSelected = value; }
            finally { _rechecking = false; }
            Recheck();
        }

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
            Checks.Add(CheckCategories());

            int pass = Checks.Count(c => c.Result == CheckResult.Pass);
            int warn = Checks.Count(c => c.Result == CheckResult.Warning);
            int blocked = Checks.Count(c => c.Result == CheckResult.Blocked);
            Summary = $"{pass} pass | {warn} warnings | {blocked} blocked";
            CanCreate = blocked == 0;
            OnPropertyChanged(nameof(CategoriesText));
            OnPropertyChanged(nameof(SelectedCategoriesText));
        }

        private PrecheckItem CheckCategories()
        {
            const string title = "Categories";
            if (!OnlySelectedCategories)
                return Categories.Count == 0
                    ? new PrecheckItem(title, CheckResult.Warning, "No element is ticked yet.")
                    : new PrecheckItem(title, CheckResult.Pass, $"Bound to {Categories.Count} categor{(Categories.Count == 1 ? "y" : "ies")} of the ticked elements; the main window checks each one.");

            var selected = CategoryOptions.Where(o => o.IsSelected).ToList();
            if (selected.Count == 0)
                return new PrecheckItem(title, CheckResult.Blocked, "Only the selected categories: tick at least one category in the list.");
            var left = CategoryOptions.Where(o => o.HasTicked && !o.IsSelected).Select(o => o.Name).ToList();
            string bound = $"Bound to {selected.Count} selected categor{(selected.Count == 1 ? "y" : "ies")}.";
            if (left.Count > 0)
                return new PrecheckItem(title, CheckResult.Warning,
                    $"{bound} {string.Join(", ", left)} {(left.Count == 1 ? "has" : "have")} ticked elements but {(left.Count == 1 ? "is" : "are")} not selected; those elements are skipped.");
            int noTicked = selected.Count(o => !o.HasTicked);
            return new PrecheckItem(title, CheckResult.Pass,
                bound + (noTicked > 0 ? $" {noTicked} of them have no ticked element yet; they get the parameter, empty." : string.Empty));
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
