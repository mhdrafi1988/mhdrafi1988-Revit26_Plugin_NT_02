using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern;
using Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Revit;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.ViewModels
{
    /// <summary>One line layer: on/off checkbox and line-style picker.</summary>
    public sealed partial class LayerOptionViewModel : ObservableObject
    {
        private readonly LineStyleOption? _defaultStyle;
        private readonly bool _defaultEnabled;

        public LayerOptionViewModel(PatternLayer layer, string name, bool enabled,
            IReadOnlyList<LineStyleOption> styles, string defaultStyleName)
        {
            Layer = layer;
            Name = name;
            Styles = styles;
            _defaultEnabled = enabled;
            _defaultStyle = LineStyleProvider.FindOrFirst(styles, defaultStyleName);
            _isEnabled = enabled;
            _selectedStyle = _defaultStyle;
        }

        public PatternLayer Layer { get; }
        public string Name { get; }
        public IReadOnlyList<LineStyleOption> Styles { get; }

        [ObservableProperty]
        private bool _isEnabled;

        [ObservableProperty]
        private LineStyleOption? _selectedStyle;

        public event EventHandler? Changed;

        public void Reset()
        {
            IsEnabled = _defaultEnabled;
            SelectedStyle = _defaultStyle;
        }

        partial void OnIsEnabledChanged(bool value) => Changed?.Invoke(this, EventArgs.Empty);

        partial void OnSelectedStyleChanged(LineStyleOption? value) => Changed?.Invoke(this, EventArgs.Empty);
    }
}
