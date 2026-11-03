using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Revit;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.ViewModels
{
    /// <summary>Floor card: view, floor, level, edges, shape badge and any validation error.</summary>
    public sealed partial class FloorCardViewModel : ObservableObject
    {
        [ObservableProperty] private string _viewName = string.Empty;
        [ObservableProperty] private string _floorName = string.Empty;
        [ObservableProperty] private string _levelName = string.Empty;
        [ObservableProperty] private string _edges = string.Empty;
        [ObservableProperty] private string _shapeBadge = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasError))]
        private string? _errorText;

        public bool HasError => !string.IsNullOrEmpty(ErrorText);

        public void Update(FloorContext context)
        {
            ViewName = context.ViewName;
            FloorName = context.FloorName;
            LevelName = context.LevelName;
            ShapeBadge = context.Outline.ShapeName;
            Edges = context.Outline.Outline != null
                ? string.Format(CultureInfo.CurrentCulture, "{0} (sketch: {1})", context.Outline.Outline.Count, context.SketchEdgeCount)
                : context.SketchEdgeCount.ToString(CultureInfo.CurrentCulture);
            ErrorText = context.Outline.Errors.Count > 0 ? string.Join("\n", context.Outline.Errors) : null;
        }
    }
}
