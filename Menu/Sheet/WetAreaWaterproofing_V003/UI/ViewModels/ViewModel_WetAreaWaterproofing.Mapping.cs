using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V003.Core.Mapping;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V003.Core.Models;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V003.Infrastructure;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V003.UI.ViewModels
{
    /// <summary>A category the "+ Add category" search offers.</summary>
    public sealed class CategoryChoiceViewModel
    {
        /// <summary>Wraps <paramref name="choice"/>; <paramref name="added"/> when it is already loaded.</summary>
        public CategoryChoiceViewModel(CategoryChoice choice, bool added)
        {
            Name = choice.Name;
            Note = added ? "added" : choice.ModelCount == 1 ? "in 1 model" : $"in {choice.ModelCount} models";
            IsAdded = added;
        }

        /// <summary>Category name.</summary>
        public string Name { get; }
        /// <summary>"in 3 models" or "added".</summary>
        public string Note { get; }
        /// <summary>Already under "Categories".</summary>
        public bool IsAdded { get; }
    }

    /// <summary>One row of the Mapping table (tool 3.0.0).</summary>
    public sealed partial class MappingRowViewModel : ObservableObject
    {
        private readonly Action _changed;
        private readonly Func<IEnumerable<LoadedType>> _types;
        private bool _loading;

        /// <summary>Choices of the Type box, in <see cref="TypeMatch"/> order.</summary>
        public static IReadOnlyList<string> MatchChoices { get; } = new[] { "Any type", "Keywords", "Picked types" };

        /// <summary>Wraps a saved row. <paramref name="types"/> gives the types "Load types" found.</summary>
        public MappingRowViewModel(MappingRule rule, Func<IEnumerable<LoadedType>> types, Action changed)
        {
            _loading = true;
            Id = rule.Id;
            _on = rule.On;
            _name = rule.Name;
            _category = rule.Category;
            _matchChoice = MatchChoices[(int)rule.Match];
            _keywordsText = string.Join(", ", rule.Keywords);
            _pickedTypes = rule.TypeNames.ToList();
            _modeIndex = (int)rule.Mode;
            _allMatches = rule.AllMatches;
            _lengthText = rule.LengthMm.ToString("0.#", CultureInfo.CurrentCulture);
            _draftingViewName = rule.DraftingViewName;
            _types = types;
            _changed = changed;
            UpdateCutChoices(rule.Cut);
            _loading = false;
            RefreshTypes();
        }

        private List<string> _pickedTypes;

        /// <summary>Row id (kept in the settings).</summary>
        public string Id { get; }
        /// <summary>Where the cut can go for this category.</summary>
        public ObservableCollection<string> CutChoices { get; } = new();
        /// <summary>Loaded types of the category, ticked when picked.</summary>
        public ObservableCollection<FilterItemViewModel> TypeChoices { get; } = new();
        /// <summary>Choices of the Type box.</summary>
        public IReadOnlyList<string> Matches => MatchChoices;

        [ObservableProperty] private bool _on;
        [ObservableProperty] private string _name;
        [ObservableProperty] private string _category;
        [ObservableProperty] private string _matchChoice;
        [ObservableProperty] private string _keywordsText;
        [ObservableProperty] private string _cutChoice;
        [ObservableProperty] private int _modeIndex;
        [ObservableProperty] private bool _allMatches;
        [ObservableProperty] private string _lengthText;
        [ObservableProperty] private string _draftingViewName;
        [ObservableProperty] private string _foundText = "–";
        [ObservableProperty] private bool _isTypePopupOpen;

        /// <summary>True for the Room row (full-room cuts): no type, no element.</summary>
        public bool IsRoom => Category == MappingRules.RoomCategory;
        /// <summary>The Type box can be changed.</summary>
        public bool HasTypes => !IsRoom;
        /// <summary>How the types are picked.</summary>
        public TypeMatch Match => (TypeMatch)Math.Max(0, MatchChoices.ToList().IndexOf(MatchChoice ?? MatchChoices[0]));
        /// <summary>Keywords box shown.</summary>
        public bool IsKeywords => HasTypes && Match == TypeMatch.Keywords;
        /// <summary>Type picker shown.</summary>
        public bool IsPicked => HasTypes && Match == TypeMatch.Types;
        /// <summary>Where the cut goes.</summary>
        public MappingCut Cut => MappingRules.CutsFor(Category).FirstOrDefault(c => MappingRules.CutLabel(c, Category) == CutChoice);
        /// <summary>What is placed.</summary>
        public SectionMode Mode => Enum.IsDefined(typeof(SectionMode), ModeIndex) ? (SectionMode)ModeIndex : SectionMode.ReferenceSection;
        /// <summary>"Points to" is a drafting view (Ref. Section / Ref. Callout).</summary>
        public bool UsesDraftingView => SectionModes.UsesDraftingView(Mode);
        /// <summary>What a Section / Callout row creates, shown instead of the "Points to" box.</summary>
        public string CreatesText => Mode == SectionMode.Section ? $"New: WP Section - <room> {Name}" : $"New: WP Callout - <room> {Name}";
        /// <summary>"Past room" for the full-room cuts, else "Length".</summary>
        public string LengthLabel => IsRoom ? "Past room" : "Length";
        /// <summary>"Every match" makes sense (not for the full-room or longest-wall cut).</summary>
        public bool CanHaveMany => Cut is MappingCut.ThroughElement or MappingCut.RoomCorners;
        /// <summary>Text of the Type picker button.</summary>
        public string PickedSummary => _pickedTypes.Count == 0 ? "Pick types…" : _pickedTypes.Count == 1 ? _pickedTypes[0] : $"{_pickedTypes.Count} types";
        /// <summary>Category, and the models it is read from, under the category box.</summary>
        public string CategoryHint => IsRoom ? "always" : string.Join(" · ", (_types() ?? Enumerable.Empty<LoadedType>())
            .Where(t => t.Category == Category).Select(t => t.Model == RoomCollector.HostKey ? "Host" : t.Model).Distinct().Take(3));

        /// <summary>Mode radio buttons.</summary>
        public bool IsSection { get => Mode == SectionMode.Section; set { if (value) ModeIndex = (int)SectionMode.Section; } }
        /// <inheritdoc cref="IsSection"/>
        public bool IsCallout { get => Mode == SectionMode.Callout; set { if (value) ModeIndex = (int)SectionMode.Callout; } }
        /// <inheritdoc cref="IsSection"/>
        public bool IsRefSection { get => Mode == SectionMode.ReferenceSection; set { if (value) ModeIndex = (int)SectionMode.ReferenceSection; } }
        /// <inheritdoc cref="IsSection"/>
        public bool IsRefCallout { get => Mode == SectionMode.ReferenceCallout; set { if (value) ModeIndex = (int)SectionMode.ReferenceCallout; } }

        partial void OnOnChanged(bool value) => Changed();
        partial void OnNameChanged(string value) { OnPropertyChanged(nameof(CreatesText)); Changed(); }
        partial void OnKeywordsTextChanged(string value) { RefreshFound(); Changed(); }
        partial void OnAllMatchesChanged(bool value) => Changed();
        partial void OnLengthTextChanged(string value) => Changed();
        partial void OnDraftingViewNameChanged(string value) => Changed();
        partial void OnCategoryChanged(string value)
        {
            if (_loading) return;
            UpdateCutChoices(null);
            LengthText = MappingRules.DefaultLengthMm(Cut, value).ToString("0.#", CultureInfo.CurrentCulture);
            _pickedTypes.Clear();
            foreach (string p in new[] { nameof(IsRoom), nameof(HasTypes), nameof(IsKeywords), nameof(IsPicked), nameof(PickedSummary), nameof(LengthLabel) })
                OnPropertyChanged(p);
            RefreshTypes();
            Changed();
        }
        partial void OnMatchChoiceChanged(string value)
        {
            OnPropertyChanged(nameof(Match));
            OnPropertyChanged(nameof(IsKeywords));
            OnPropertyChanged(nameof(IsPicked));
            RefreshFound();
            Changed();
        }
        partial void OnCutChoiceChanged(string value)
        {
            OnPropertyChanged(nameof(Cut));
            OnPropertyChanged(nameof(CanHaveMany));
            Changed();
        }
        partial void OnModeIndexChanged(int value)
        {
            foreach (string p in new[] { nameof(Mode), nameof(UsesDraftingView), nameof(CreatesText), nameof(IsSection), nameof(IsCallout), nameof(IsRefSection), nameof(IsRefCallout) })
                OnPropertyChanged(p);
            Changed();
        }

        private void Changed()
        {
            if (!_loading) _changed?.Invoke();
        }

        private void UpdateCutChoices(MappingCut? keep)
        {
            CutChoices.Clear();
            foreach (MappingCut c in MappingRules.CutsFor(Category)) CutChoices.Add(MappingRules.CutLabel(c, Category));
            string wanted = keep.HasValue ? MappingRules.CutLabel(keep.Value, Category) : null;
            CutChoice = wanted != null && CutChoices.Contains(wanted) ? wanted : CutChoices.FirstOrDefault();
        }

        /// <summary>Rebuilds the type list and the Found count from the last "Load types".</summary>
        public void RefreshTypes()
        {
            TypeChoices.Clear();
            var names = (_types() ?? Enumerable.Empty<LoadedType>()).Where(t => t.Category == Category)
                .Select(t => t.TypeName).Distinct().OrderBy(n => n).ToList();
            foreach (string missing in _pickedTypes.Where(p => !names.Contains(p)).ToList()) names.Add(missing); // a saved pick not loaded now stays
            foreach (string n in names)
            {
                var item = new FilterItemViewModel(n, _pickedTypes.Contains(n));
                item.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName != nameof(FilterItemViewModel.IsChecked)) return;
                    if (item.IsChecked) { if (!_pickedTypes.Contains(item.Text)) _pickedTypes.Add(item.Text); }
                    else _pickedTypes.Remove(item.Text);
                    OnPropertyChanged(nameof(PickedSummary));
                    RefreshFound();
                    Changed();
                };
                TypeChoices.Add(item);
            }
            OnPropertyChanged(nameof(CategoryHint));
            RefreshFound();
        }

        private void RefreshFound()
        {
            var types = _types()?.ToList();
            if (IsRoom) { FoundText = "–"; return; }
            if (types == null || types.Count == 0) { FoundText = "?"; return; }
            FoundText = MappingRules.CountFound(ToRule(out _) ?? new MappingRule(), types).ToString(CultureInfo.CurrentCulture);
        }

        /// <summary>The row as a setting; null (with <paramref name="error"/>) when the length is not a valid number.</summary>
        public MappingRule ToRule(out string error)
        {
            error = null;
            bool ok = double.TryParse(LengthText, NumberStyles.Float, CultureInfo.CurrentCulture, out double length)
                   || double.TryParse(LengthText, NumberStyles.Float, CultureInfo.InvariantCulture, out length);
            if (!ok || length <= 0 || length > 20000)
            {
                error = $"Mapping row \"{Name}\": the length must be a number from 1 to 20000 mm.";
                return null;
            }
            return new MappingRule
            {
                Id = Id, On = On, Name = (Name ?? string.Empty).Trim(), Category = Category, Match = IsRoom ? TypeMatch.Any : Match,
                Keywords = (KeywordsText ?? string.Empty).Split(',', ';').Select(k => k.Trim().ToLowerInvariant()).Where(k => k.Length > 0).ToList(),
                TypeNames = _pickedTypes.ToList(), Cut = Cut, Mode = Mode, AllMatches = CanHaveMany && AllMatches,
                LengthMm = length, DraftingViewName = (DraftingViewName ?? string.Empty).Trim()
            };
        }
    }

    public sealed partial class ViewModel_WetAreaWaterproofing
    {
        private List<LoadedType> _loadedTypes = new();
        private List<CategoryChoice> _categoryChoices;

        /// <summary>"Look in": the host model and each loaded link, ticked when its elements are read.</summary>
        public ObservableCollection<FilterItemViewModel> MappingSources { get; } = new();
        /// <summary>"Categories": the categories whose elements are read.</summary>
        public ObservableCollection<string> LoadedCategories { get; } = new();
        /// <summary>The "+ Add category" search results.</summary>
        public ObservableCollection<CategoryChoiceViewModel> CategoryMatches { get; } = new();
        /// <summary>Choices of a row's Category box: the loaded categories, then Room.</summary>
        public ObservableCollection<string> RowCategories { get; } = new();
        /// <summary>The Mapping table, in order (rows higher up win when a room would get more than six cuts).</summary>
        public ObservableCollection<MappingRowViewModel> MappingRows { get; } = new();

        [ObservableProperty] private string _categorySearch = string.Empty;
        [ObservableProperty] private bool _isCategoryPopupOpen;
        [ObservableProperty] private string _loadTypesText = "Load types";
        [ObservableProperty] private string _loadResultText = string.Empty;
        [ObservableProperty] private bool _hasLoadResult;
        [ObservableProperty] private bool _typesStale = true;
        [ObservableProperty] private string _mappingSummary = string.Empty;

        /// <summary>Where the Mapping is remembered, for the hint under the loader.</summary>
        public string MemoryText => $"Saved in {WetAreaSettings.FileName}: models, categories and mapping rows";

        partial void OnCategorySearchChanged(string value) => UpdateCategoryMatches();

        /// <summary>Fills the Mapping fields from the settings. Called once, from the constructor.</summary>
        private void InitMapping()
        {
            foreach (string c in _settings.MappingCategories) LoadedCategories.Add(c);
            UpdateRowCategories();
            foreach (MappingRule rule in _settings.Mappings)
                MappingRows.Add(new MappingRowViewModel(rule, () => _loadedTypes, OnMappingChanged));
            UpdateMappingSummary();
            UpdateLoadButton();
        }

        /// <summary>Fills "Look in" from a model read; keeps the user's ticks.</summary>
        private void ApplyMappingSources(ModelSnapshot snapshot)
        {
            var off = new HashSet<string>(MappingSources.Count > 0
                ? MappingSources.Skip(1).Where(s => !s.IsChecked).Select(s => s.Text)
                : _settings.MappingLinksOff);
            bool host = MappingSources.Count > 0 ? MappingSources[0].IsChecked : _settings.MappingHost;
            foreach (FilterItemViewModel s in MappingSources) s.PropertyChanged -= OnMappingSourceChanged;
            MappingSources.Clear();
            MappingSources.Add(new FilterItemViewModel("Host model", host));
            foreach (string link in snapshot.Links) MappingSources.Add(new FilterItemViewModel(link, !off.Contains(link)));
            foreach (FilterItemViewModel s in MappingSources) s.PropertyChanged += OnMappingSourceChanged;
            UpdateLoadButton();
        }

        private void OnMappingSourceChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(FilterItemViewModel.IsChecked)) return;
            _categoryChoices = null; // other models: other categories
            TypesStale = true;
            UpdateLoadButton();
            MarkNeedsRefresh();
        }

        private void OnMappingChanged()
        {
            UpdateMappingSummary();
            MarkNeedsRefresh();
        }

        private void UpdateRowCategories()
        {
            var wanted = LoadedCategories.Concat(new[] { MappingRules.RoomCategory })
                .Concat(MappingRows.Select(r => r.Category)).Distinct().ToList();
            for (int i = RowCategories.Count - 1; i >= 0; i--)
                if (!wanted.Contains(RowCategories[i])) RowCategories.RemoveAt(i);
            foreach (string c in wanted) if (!RowCategories.Contains(c)) RowCategories.Add(c);
        }

        private void UpdateLoadButton()
        {
            int models = MappingSources.Count(s => s.IsChecked);
            LoadTypesText = $"Load types · {LoadedCategories.Count} categor{(LoadedCategories.Count == 1 ? "y" : "ies")} · {models} model{(models == 1 ? "" : "s")}";
            LoadTypesCommand.NotifyCanExecuteChanged();
        }

        private void UpdateMappingSummary()
        {
            int on = MappingRows.Count(r => r.On);
            var notLoaded = MappingRows.Where(r => r.On && !r.IsRoom && !LoadedCategories.Contains(r.Category))
                .Select(r => r.Category).Distinct().ToList();
            MappingSummary = (on == 0 ? "No row is on: no section will be placed." : $"{on} row(s) on · at most 6 cuts per room · rows higher up win")
                + (notLoaded.Count > 0 ? $" · not loaded, so skipped: {string.Join(", ", notLoaded)}" : string.Empty);
        }

        /// <summary>Opens the "+ Add category" list, reading the model's categories the first time.</summary>
        public void OpenCategorySearch()
        {
            if (_categoryChoices != null) { UpdateCategoryMatches(); IsCategoryPopupOpen = true; return; }
            if (IsBusy || !PushMappingSources()) return;
            IsBusy = true;
            _session.ListCategories(_settings, (choices, error) =>
            {
                IsBusy = false;
                if (choices == null)
                {
                    AddLog(LogLevel.Error, "The categories could not be read: " + error);
                    return;
                }
                _categoryChoices = choices.ToList();
                UpdateCategoryMatches();
                IsCategoryPopupOpen = true;
            });
        }

        private void UpdateCategoryMatches()
        {
            CategoryMatches.Clear();
            if (_categoryChoices == null) return;
            string q = (CategorySearch ?? string.Empty).Trim();
            if (q.Length == 0) return; // the full list is never shown: type to search
            foreach (CategoryChoice c in _categoryChoices
                .Where(c => c.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(c => c.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(c => c.Name).Take(12))
                CategoryMatches.Add(new CategoryChoiceViewModel(c, LoadedCategories.Contains(c.Name)));
            IsCategoryPopupOpen = true;
        }

        /// <summary>Adds a category to "Categories" (from the search list).</summary>
        [RelayCommand]
        private void AddCategory(CategoryChoiceViewModel choice)
        {
            if (choice == null || choice.IsAdded || LoadedCategories.Contains(choice.Name)) return;
            LoadedCategories.Add(choice.Name);
            CategorySearch = string.Empty;
            IsCategoryPopupOpen = false;
            AfterCategoriesChanged();
            AddLog(LogLevel.Info, $"Category \"{choice.Name}\" added. Press Load types, then add a mapping row for it.");
        }

        /// <summary>Removes a category from "Categories". Its rows stay but are skipped until it is loaded again.</summary>
        [RelayCommand]
        private void RemoveCategory(string name)
        {
            if (!LoadedCategories.Remove(name)) return;
            AfterCategoriesChanged();
        }

        private void AfterCategoriesChanged()
        {
            UpdateRowCategories();
            TypesStale = true;
            UpdateLoadButton();
            UpdateMappingSummary();
            MarkNeedsRefresh();
        }

        private bool CanLoadTypes() => !IsBusy && LoadedCategories.Count > 0 && MappingSources.Any(s => s.IsChecked);

        /// <summary>Reads only the chosen categories from the ticked models and fills each row's type list and Found count.</summary>
        [RelayCommand(CanExecute = nameof(CanLoadTypes))]
        private void LoadTypes()
        {
            if (!PushMappingSources()) return;
            IsBusy = true;
            DateTime started = DateTime.Now;
            AddLog(LogLevel.Info, $"Loading the types of {LoadedCategories.Count} categor{(LoadedCategories.Count == 1 ? "y" : "ies")}…");
            _session.LoadTypes(_settings, (types, error) =>
            {
                IsBusy = false;
                if (types == null)
                {
                    AddLog(LogLevel.Error, "The types could not be loaded: " + error);
                    return;
                }
                _loadedTypes = types.ToList();
                foreach (MappingRowViewModel row in MappingRows) row.RefreshTypes();
                double seconds = (DateTime.Now - started).TotalSeconds;
                int typeCount = _loadedTypes.Select(t => (t.Category, t.TypeName)).Distinct().Count();
                LoadResultText = $"✓ Loaded {typeCount} types ({_loadedTypes.Sum(t => t.Count)} elements) in {seconds:0.0} s";
                HasLoadResult = true;
                TypesStale = false;
                AddLog(LogLevel.Success, LoadResultText.TrimStart('✓', ' '));
            });
        }

        /// <summary>Adds an empty row at the end (first loaded category, any type, through the element).</summary>
        [RelayCommand]
        private void AddMappingRow()
        {
            string category = LoadedCategories.FirstOrDefault() ?? MappingRules.RoomCategory;
            var rule = new MappingRule { Name = "New row", Category = category, Cut = MappingRules.CutsFor(category)[0] };
            rule.LengthMm = MappingRules.DefaultLengthMm(rule.Cut, category);
            MappingRows.Add(new MappingRowViewModel(rule, () => _loadedTypes, OnMappingChanged));
            OnMappingChanged();
        }

        /// <summary>Deletes a row.</summary>
        [RelayCommand]
        private void RemoveMappingRow(MappingRowViewModel row)
        {
            if (row != null && MappingRows.Remove(row)) OnMappingChanged();
        }

        /// <summary>Moves a row up (higher rows win when a room would get more than six cuts).</summary>
        [RelayCommand]
        private void MappingRowUp(MappingRowViewModel row) => MoveMappingRow(row, -1);

        /// <summary>Moves a row down.</summary>
        [RelayCommand]
        private void MappingRowDown(MappingRowViewModel row) => MoveMappingRow(row, +1);

        private void MoveMappingRow(MappingRowViewModel row, int step)
        {
            int index = row == null ? -1 : MappingRows.IndexOf(row);
            int to = index + step;
            if (index < 0 || to < 0 || to >= MappingRows.Count) return;
            MappingRows.Move(index, to);
            OnMappingChanged();
        }

        /// <summary>Puts the suggested default rows and categories back.</summary>
        [RelayCommand]
        private void ResetMapping()
        {
            MappingRows.Clear();
            foreach (MappingRule rule in MappingRules.Defaults())
                MappingRows.Add(new MappingRowViewModel(rule, () => _loadedTypes, OnMappingChanged));
            foreach (string c in MappingRules.DefaultCategories)
                if (!LoadedCategories.Contains(c)) LoadedCategories.Add(c);
            AfterCategoriesChanged();
            AddLog(LogLevel.Info, "Mapping reset to the default rows.");
        }

        /// <summary>Copies "Look in" and "Categories" into the settings.</summary>
        private bool PushMappingSources()
        {
            if (MappingSources.Count > 0)
            {
                _settings.MappingHost = MappingSources[0].IsChecked;
                _settings.MappingLinksOff = MappingSources.Skip(1).Where(s => !s.IsChecked).Select(s => s.Text).ToList();
            }
            _settings.MappingCategories = LoadedCategories.ToList();
            return true;
        }

        /// <summary>Copies the Mapping into the settings; false (with a log line) when a row is not valid.</summary>
        private bool PushMapping()
        {
            var rules = new List<MappingRule>();
            foreach (MappingRowViewModel row in MappingRows)
            {
                MappingRule rule = row.ToRule(out string error);
                if (rule == null)
                {
                    AddLog(LogLevel.Error, error);
                    return false;
                }
                rules.Add(rule);
            }
            PushMappingSources();
            _settings.Mappings = rules;
            return true;
        }
    }
}
