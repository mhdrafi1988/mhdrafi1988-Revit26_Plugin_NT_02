using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Revit26_Plugin_Revised.TrackerId.V002.UI.ViewModels
{
    /// <summary>A row of the naming page's type-code table.</summary>
    public sealed partial class TypeCodeRowViewModel : ObservableObject
    {
        private readonly System.Action _changed;

        /// <summary>Creates the row.</summary>
        public TypeCodeRowViewModel(long typeId, string category, string type, string code, System.Action changed)
        {
            TypeId = typeId;
            Category = category;
            Type = type;
            _code = code ?? string.Empty;
            _changed = changed;
        }

        /// <summary>Type element id.</summary>
        public long TypeId { get; }

        /// <summary>Category name.</summary>
        public string Category { get; }

        /// <summary>Family and type name.</summary>
        public string Type { get; }

        /// <summary>Code used in the ID; blank uses the Type segment's source.</summary>
        [ObservableProperty] private string _code;

        partial void OnCodeChanged(string value) => _changed?.Invoke();
    }

    // Page 2: how new Tracker IDs are named. The segment editor lives in the Format partial; this adds
    // upper case, the type-code table and the preview of the next IDs.
    public partial class TrackerIdViewModel
    {
        private readonly Dictionary<long, string> _typeCodes = new();

        /// <summary>Writes the whole ID in capitals.</summary>
        [ObservableProperty] private bool _upperCase;

        /// <summary>Codes for the ticked types.</summary>
        public ObservableCollection<TypeCodeRowViewModel> TypeCodeRows { get; } = new();

        /// <summary>The next new IDs, as they would be written.</summary>
        public ObservableCollection<string> NextIds { get; } = new();

        private void InitNaming()
        {
            UpperCase = _settings.UpperCase;
            foreach (var pair in _settings.TypeCodes ?? new Dictionary<long, string>())
                if (!string.IsNullOrWhiteSpace(pair.Value)) _typeCodes[pair.Key] = pair.Value.Trim();
        }

        partial void OnUpperCaseChanged(bool value) => ScheduleReplan();

        // Codes typed for the types in use; codes of types no longer ticked are kept for later.
        private Dictionary<long, string> CurrentTypeCodes() => new(_typeCodes);

        private void RefreshTypeCodes()
        {
            TypeCodeRows.Clear();
            foreach (var t in Types.Where(t => t.IsSelected))
            {
                _typeCodes.TryGetValue(t.Info.Id, out string code);
                TypeCodeRows.Add(new TypeCodeRowViewModel(t.Info.Id, t.CategoryName, $"{t.FamilyName} : {t.TypeName}", code, OnTypeCodesEdited));
            }
        }

        private void OnTypeCodesEdited()
        {
            foreach (var row in TypeCodeRows)
            {
                string code = row.Code?.Trim() ?? string.Empty;
                if (code.Length == 0) _typeCodes.Remove(row.TypeId);
                else _typeCodes[row.TypeId] = code;
            }
            ScheduleReplan();
        }

        private void RefreshNextIds()
        {
            NextIds.Clear();
            if (_plan == null) return;
            foreach (var r in _plan.Where(r => r.GetsNewId && !string.IsNullOrEmpty(r.ResultId)).Take(5))
                NextIds.Add(r.ResultId);
        }

        private void ClearPlan()
        {
            InvalidateSnapshots();
            PlanRows = new ObservableCollection<PlanRowViewModel>();
            PlanView = null;
            NextIds.Clear();
            OnPropertyChanged(nameof(PlanHeader));
        }
    }
}
