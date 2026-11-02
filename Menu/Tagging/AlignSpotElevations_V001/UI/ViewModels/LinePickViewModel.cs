using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.AlignSpotElevations.V001.Core.Models;
using System;

namespace Revit26_Plugin_Revised.AlignSpotElevations.V001.UI.ViewModels
{
    /// <summary>
    /// One optional detail line of Align to detail lines (for the bend points, the leader ends or the text):
    /// the picked line, how the part slides onto it, and whether a vertical or horizontal move is possible.
    /// </summary>
    public sealed class LinePickViewModel : ObservableObject
    {
        private readonly Action _changed;
        private readonly Action<SpotPart> _pick;
        private readonly Func<LineMove> _getMove;
        private readonly Action<LineMove> _setMove;
        private DetailLine2D _line;
        private string _check = string.Empty;
        private int _verticalCount, _horizontalCount, _haveCount;

        /// <summary>Creates the box for <paramref name="part"/>.</summary>
        /// <param name="part">Which part this line is for.</param>
        /// <param name="header">Group box header, e.g. "End point line · optional".</param>
        /// <param name="getMove">Reads the saved move option.</param>
        /// <param name="setMove">Writes the saved move option.</param>
        /// <param name="pick">Asks the command to let the user pick a detail line in the view.</param>
        /// <param name="changed">Called after any change, to refresh the preview.</param>
        public LinePickViewModel(SpotPart part, string header, Func<LineMove> getMove, Action<LineMove> setMove,
            Action<SpotPart> pick, Action changed)
        {
            Part = part;
            Header = header;
            _getMove = getMove ?? throw new ArgumentNullException(nameof(getMove));
            _setMove = setMove ?? throw new ArgumentNullException(nameof(setMove));
            _pick = pick ?? throw new ArgumentNullException(nameof(pick));
            _changed = changed ?? throw new ArgumentNullException(nameof(changed));
            PickCommand = new RelayCommand(() => _pick(Part));
            ClearCommand = new RelayCommand(() => Line = null);
        }

        /// <summary>The part this line is for.</summary>
        public SpotPart Part { get; }

        /// <summary>Group box header.</summary>
        public string Header { get; }

        /// <summary>Hides the window so the user can click a detail line in the view.</summary>
        public IRelayCommand PickCommand { get; }

        /// <summary>Removes the picked line; the part then stays (or follows, as in Revit).</summary>
        public IRelayCommand ClearCommand { get; }

        /// <summary>The picked line, or null.</summary>
        public DetailLine2D Line
        {
            get => _line;
            set
            {
                if (!SetProperty(ref _line, value)) return;
                FixMove();
                RaiseAll();
                _changed();
            }
        }

        /// <summary>True when a line is picked.</summary>
        public bool HasLine => _line != null;

        /// <summary>Text next to the Pick button.</summary>
        public string LineText => _line == null
            ? "Not picked: these points stay"
            : $"Detail line {_line.Id} · {_line.Describe()}";

        /// <summary>Radio: Auto.</summary>
        public bool IsAuto { get => _getMove() == LineMove.Auto; set { if (value) SetMove(LineMove.Auto); } }

        /// <summary>Radio: Vertical (X stays).</summary>
        public bool IsVertical { get => _getMove() == LineMove.Vertical; set { if (value) SetMove(LineMove.Vertical); } }

        /// <summary>Radio: Horizontal (Y stays).</summary>
        public bool IsHorizontal { get => _getMove() == LineMove.Horizontal; set { if (value) SetMove(LineMove.Horizontal); } }

        /// <summary>False when the line is vertical, so a vertical move can never reach it.</summary>
        public bool CanVertical => _line == null || !_line.IsVertical;

        /// <summary>False when the line is horizontal, so a horizontal move can never reach it.</summary>
        public bool CanHorizontal => _line == null || !_line.IsHorizontal;

        /// <summary>"Vertical: possible for 3 of 4 · Horizontal: not possible (line is horizontal)".</summary>
        public string Check { get => _check; private set => SetProperty(ref _check, value); }

        /// <summary>
        /// Sets the counts behind <see cref="Check"/>: spots that have the part, and how many of them can
        /// reach the line vertically and horizontally.
        /// </summary>
        public void SetCounts(int have, int vertical, int horizontal)
        {
            _haveCount = have; _verticalCount = vertical; _horizontalCount = horizontal;
            if (_line == null) { Check = string.Empty; return; }
            string v = _line.IsVertical ? "Vertical: not possible (line is vertical)" : $"Vertical: possible for {_verticalCount} of {_haveCount}";
            string h = _line.IsHorizontal ? "Horizontal: not possible (line is horizontal)" : $"Horizontal: possible for {_horizontalCount} of {_haveCount}";
            Check = v + "  ·  " + h;
        }

        private void SetMove(LineMove move)
        {
            if (_getMove() == move) return;
            _setMove(move);
            RaiseAll();
            _changed();
        }

        /// <summary>A direction the new line can never be reached in falls back to Auto.</summary>
        private void FixMove()
        {
            if ((_getMove() == LineMove.Vertical && !CanVertical) || (_getMove() == LineMove.Horizontal && !CanHorizontal))
                _setMove(LineMove.Auto);
        }

        /// <summary>Re-reads every binding (after the saved option changed elsewhere).</summary>
        public void RaiseAll() => OnPropertyChanged(string.Empty);
    }
}
