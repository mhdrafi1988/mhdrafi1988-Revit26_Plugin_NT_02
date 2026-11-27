using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;

namespace Revit26_Plugin_Revised.SectionRoofDetailLines.V001.UI.Behaviors
{
    /// <summary>
    /// Makes a <see cref="ComboBox"/> searchable: typing in it narrows the drop-down to the items
    /// whose text (<see cref="object.ToString"/>) contains what was typed, anywhere and in any case,
    /// and the user picks from the narrowed list. Text that matches no item never becomes the
    /// choice: when the drop-down closes or the box loses focus, the filter is cleared and the box
    /// shows the current choice again.
    /// </summary>
    /// <remarks>
    /// The combo's <c>ItemsSource</c> must be a collection view of its own (not one shared with
    /// another control), because the filter is set on it. Bind <c>SelectedItem</c> to a property
    /// that ignores <c>null</c>: WPF clears the selection while the chosen item is filtered out.
    /// </remarks>
    public static class FilterComboBehavior
    {
        /// <summary>Turns type-to-filter on for a combo box.</summary>
        public static readonly DependencyProperty IsFilterableProperty =
            DependencyProperty.RegisterAttached("IsFilterable", typeof(bool), typeof(FilterComboBehavior),
                new PropertyMetadata(false, OnIsFilterableChanged));

        /// <summary>Gets whether type-to-filter is on.</summary>
        public static bool GetIsFilterable(DependencyObject obj) => (bool)obj.GetValue(IsFilterableProperty);

        /// <summary>Sets whether type-to-filter is on.</summary>
        public static void SetIsFilterable(DependencyObject obj, bool value) => obj.SetValue(IsFilterableProperty, value);

        private static readonly DependencyProperty StateProperty =
            DependencyProperty.RegisterAttached("State", typeof(State), typeof(FilterComboBehavior));

        private static void OnIsFilterableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ComboBox combo || !(bool)e.NewValue || combo.GetValue(StateProperty) != null) return;

            var state = new State(combo);
            combo.SetValue(StateProperty, state);
            combo.IsEditable = true;
            combo.IsTextSearchEnabled = false;
            combo.StaysOpenOnEdit = true;
            combo.Loaded += state.OnLoaded;
            combo.DropDownClosed += state.OnDropDownClosed;
            combo.LostKeyboardFocus += state.OnLostKeyboardFocus;
        }

        /// <summary>Per-combo state: the edit box and a guard against reacting to its own changes.</summary>
        private sealed class State
        {
            private readonly ComboBox _combo;
            private TextBox _box;
            private bool _busy;

            public State(ComboBox combo) => _combo = combo;

            public void OnLoaded(object sender, RoutedEventArgs e)
            {
                if (_box != null) return;
                _combo.ApplyTemplate();
                _box = _combo.Template?.FindName("PART_EditableTextBox", _combo) as TextBox;
                if (_box != null) _box.TextChanged += OnTextChanged;
            }

            private void OnTextChanged(object sender, TextChangedEventArgs e)
            {
                if (_busy || _box == null || !_box.IsKeyboardFocusWithin) return;

                string text = _box.Text ?? string.Empty;

                // Arrowing through the list, or text that is exactly an item: leave the list as it is.
                if (_combo.SelectedItem != null
                    && string.Equals(_combo.SelectedItem.ToString(), text, StringComparison.OrdinalIgnoreCase))
                    return;

                _busy = true;
                try
                {
                    int caret = _box.CaretIndex;
                    string needle = text.Trim();
                    _combo.Items.Filter = needle.Length == 0
                        ? null
                        : item => item != null
                            && item.ToString().IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

                    if (!_combo.IsDropDownOpen) _combo.IsDropDownOpen = true;

                    // Filtering can clear the selection, and WPF then empties the box: put the typing back.
                    if (_box.Text != text) _box.Text = text;
                    _box.SelectionLength = 0;
                    _box.CaretIndex = Math.Min(caret, text.Length);
                }
                finally
                {
                    _busy = false;
                }
            }

            public void OnDropDownClosed(object sender, EventArgs e) => Reset();

            public void OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
            {
                if (!_combo.IsKeyboardFocusWithin && !_combo.IsDropDownOpen) Reset();
            }

            /// <summary>Clears the filter and shows the current choice in the box again.</summary>
            private void Reset()
            {
                _busy = true;
                try
                {
                    if (_combo.Items.Filter != null) _combo.Items.Filter = null;
                    BindingOperations.GetBindingExpression(_combo, Selector.SelectedItemProperty)?.UpdateTarget();
                    string shown = _combo.SelectedItem?.ToString() ?? string.Empty;
                    if (_combo.Text != shown) _combo.Text = shown;
                }
                finally
                {
                    _busy = false;
                }
            }
        }
    }
}
