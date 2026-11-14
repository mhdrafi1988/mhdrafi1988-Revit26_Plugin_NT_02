using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.ViewModels
{
    /// <summary>
    /// Board size comparison: lays the floor with each listed size (current settings) in the background
    /// and shows full boards, cuts, codes and slivers; "Use" applies a size to Board settings.
    /// </summary>
    public sealed partial class SizeComparisonViewModel : ObservableObject
    {
        public const string DefaultSizes = "1.0x0.5; 1.0x0.6; 0.8x0.6; 1.0x1.0";
        private int _version;

        public ObservableCollection<SizeRowViewModel> Rows { get; } = new ObservableCollection<SizeRowViewModel>();

        [ObservableProperty]
        private string _sizesText = DefaultSizes;

        [ObservableProperty]
        private string _errors = string.Empty;

        [ObservableProperty]
        private bool _isComputing;

        public event EventHandler? SizesChanged;

        /// <summary>Raised when the user clicks Use on a row.</summary>
        public event Action<BoardSize>? SizeChosen;

        [RelayCommand]
        private void Use(SizeRowViewModel? row)
        {
            if (row != null)
            {
                SizeChosen?.Invoke(row.Size);
            }
        }

        public void Reset() => SizesText = DefaultSizes;

        partial void OnSizesTextChanged(string value) => SizesChanged?.Invoke(this, EventArgs.Empty);

        /// <summary>Recomputes the table off the UI thread; stale results are dropped.</summary>
        public async Task RecomputeAsync(Polygon2D? outline, Pattern2DSettings? settings)
        {
            int version = ++_version;
            (IReadOnlyList<BoardSize> sizes, IReadOnlyList<string> errors) = SizeListParser.Parse(SizesText);
            Errors = string.Join("  ", errors);
            if (outline == null || settings == null || sizes.Count == 0)
            {
                Rows.Clear();
                return;
            }

            IsComputing = true;
            try
            {
                IReadOnlyList<SizeComparisonRow> rows = await Task.Run(() => SizeComparer.Compare(outline, settings, sizes));
                if (version != _version)
                {
                    return;
                }

                Rows.Clear();
                foreach (SizeComparisonRow r in rows)
                {
                    Rows.Add(new SizeRowViewModel(r));
                }
            }
            finally
            {
                if (version == _version)
                {
                    IsComputing = false;
                }
            }
        }
    }
}
