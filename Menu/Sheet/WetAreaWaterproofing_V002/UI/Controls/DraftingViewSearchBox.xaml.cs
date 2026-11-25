using Revit26_Plugin_Revised.WetAreaWaterproofing.V002.Core.Sections;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V002.UI.Controls
{
    /// <summary>
    /// The "Points to" box: a text box that searches the drafting views by name, Title on Sheet, sheet number,
    /// sheet name or bubble number while the user types, and drops down the matches. <see cref="Text"/> is
    /// written only when the user picks a match or leaves the box (as the V001 combo box did with LostFocus),
    /// so a half-typed word never reaches the settings.
    /// </summary>
    public partial class DraftingViewSearchBox : UserControl
    {
        /// <summary>Most matches listed at once; typing more narrows the list.</summary>
        public const int MaxShown = 60;

        private bool _suppress;
        private INotifyCollectionChanged _watched;

        /// <summary>The drafting view name the box points to (two-way).</summary>
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
            nameof(Text), typeof(string), typeof(DraftingViewSearchBox),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTextChanged));

        /// <summary>The drafting views to search (<see cref="DraftingViewInfo"/> items).</summary>
        public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
            nameof(Items), typeof(IEnumerable), typeof(DraftingViewSearchBox), new PropertyMetadata(null, OnItemsChanged));

        /// <summary>Creates the box.</summary>
        public DraftingViewSearchBox()
        {
            InitializeComponent();
            UpdateStatus();
        }

        /// <summary>The drafting view name the box points to.</summary>
        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        /// <summary>The drafting views to search.</summary>
        public IEnumerable Items
        {
            get => (IEnumerable)GetValue(ItemsProperty);
            set => SetValue(ItemsProperty, value);
        }

        private IEnumerable<DraftingViewInfo> Views => Items?.OfType<DraftingViewInfo>() ?? Enumerable.Empty<DraftingViewInfo>();

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var box = (DraftingViewSearchBox)d;
            string value = e.NewValue as string ?? string.Empty;
            if (box.Box.Text != value) box.SetBoxText(value);
            box.UpdateStatus();
        }

        private static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var box = (DraftingViewSearchBox)d;
            if (box._watched != null) box._watched.CollectionChanged -= box.OnItemsCollectionChanged;
            box._watched = e.NewValue as INotifyCollectionChanged;
            if (box._watched != null) box._watched.CollectionChanged += box.OnItemsCollectionChanged;
            box.UpdateStatus();
        }

        private void OnItemsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e) => UpdateStatus();

        private void SetBoxText(string value)
        {
            _suppress = true;
            try
            {
                Box.Text = value;
                Box.CaretIndex = Box.Text.Length;
            }
            finally { _suppress = false; }
        }

        private void Box_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppress || !Box.IsKeyboardFocusWithin) return;
            ShowMatches(Box.Text);
        }

        private void Box_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => Box.SelectAll();

        private void Box_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (e.NewFocus is DependencyObject target && IsInside(target, Drop.Child)) return;
            Drop.IsOpen = false;
            Commit();
        }

        private void Box_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Down:
                    if (!Drop.IsOpen) ShowMatches(Box.Text);
                    else Move(+1);
                    e.Handled = true;
                    break;
                case Key.Up:
                    if (Drop.IsOpen) Move(-1);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    if (Drop.IsOpen && List.SelectedItem is DraftingViewInfo picked) Pick(picked);
                    else { Drop.IsOpen = false; Commit(); }
                    e.Handled = true;
                    break;
                case Key.Escape:
                    if (Drop.IsOpen) Drop.IsOpen = false;
                    else SetBoxText(Text ?? string.Empty); // second Esc: undo the typing
                    e.Handled = true;
                    break;
            }
        }

        private void DropButton_Click(object sender, RoutedEventArgs e)
        {
            if (Drop.IsOpen) { Drop.IsOpen = false; return; }
            Box.Focus();
            ShowMatches(string.Empty);
        }

        private void List_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            DependencyObject current = e.OriginalSource as DependencyObject;
            while (current != null && current is not ListBoxItem) current = VisualTreeHelper.GetParent(current);
            if (current is ListBoxItem item && item.DataContext is DraftingViewInfo picked)
            {
                Pick(picked);
                e.Handled = true;
            }
        }

        private void ShowMatches(string query)
        {
            IReadOnlyList<DraftingViewInfo> all = DraftingViewSearch.Filter(Views, query);
            List.ItemsSource = all.Take(MaxShown).ToList();
            List.SelectedIndex = all.Count > 0 && !string.IsNullOrWhiteSpace(query) ? 0 : -1;
            string typed = (query ?? string.Empty).Trim();
            CountLine.Text = all.Count == 0
                ? (typed.Length == 0 ? "The project has no drafting views: type a name, it is created at the run." : $"No match. Enter keeps \"{typed}\" as a new view name.")
                : all.Count > MaxShown ? $"Showing {MaxShown} of {all.Count}: type more to narrow."
                : $"{all.Count} match(es) · ↑↓ move · Enter picks · Esc closes";
            Drop.IsOpen = true;
            if (List.SelectedItem != null) List.ScrollIntoView(List.SelectedItem);
        }

        private void Move(int step)
        {
            int count = List.Items.Count;
            if (count == 0) return;
            int index = List.SelectedIndex + step;
            List.SelectedIndex = index < 0 ? count - 1 : index >= count ? 0 : index;
            List.ScrollIntoView(List.SelectedItem);
        }

        private void Pick(DraftingViewInfo view)
        {
            Drop.IsOpen = false;
            SetBoxText(view.Name);
            Commit();
        }

        /// <summary>
        /// Writes the box to <see cref="Text"/>. A sheet / bubble reference such as "A-501/3" that names one
        /// placed view becomes that view's name, so it is never taken as the name of a new view.
        /// </summary>
        private void Commit()
        {
            string typed = (Box.Text ?? string.Empty).Trim();
            DraftingViewInfo byReference = DraftingViewSearch.ResolveReference(Views, typed);
            string value = byReference?.Name ?? typed;
            if (Box.Text != value) SetBoxText(value);
            if (Text != value) Text = value;
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            if (StatusLine == null) return;
            StatusLine.Text = DraftingViewSearch.Describe(Views, Text);
        }

        private static bool IsInside(DependencyObject element, DependencyObject root)
        {
            while (element != null)
            {
                if (element == root) return true;
                element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
            }
            return false;
        }
    }
}
