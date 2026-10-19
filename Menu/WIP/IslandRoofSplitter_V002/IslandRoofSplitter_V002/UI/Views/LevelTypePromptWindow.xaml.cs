// IslandRoofSplitter V002 — code-behind for the launch prompt. Wiring only: fills the two combo boxes from
// the SplitterContext, pre-selects through Core LevelResolver, and hands back the chosen ids.
// Shown by IslandRoofSplitterCommand only when LevelDetected / RoofTypeDetected is false and both lists
// are non-empty. Creates and changes nothing in the document.
#nullable enable
using System.Windows;
using System.Windows.Controls;
using Revit26_Plugin.IslandRoofSplitter.V002.Core.Models;
using Revit26_Plugin.IslandRoofSplitter.V002.Core.Services;

namespace Revit26_Plugin.IslandRoofSplitter.V002.UI.Views
{
    public partial class LevelTypePromptWindow : Window
    {
        /// <summary>Set when the user confirms; null when they cancel.</summary>
        public long? ChosenLevelId { get; private set; }
        public long? ChosenRoofTypeId { get; private set; }

        public LevelTypePromptWindow(SplitterContext ctx)
        {
            InitializeComponent();

            ReasonText.Text =
                !ctx.LevelDetected && !ctx.RoofTypeDetected
                    ? "The source roof has no usable level and its type could not be read. Choose what the new roofs should use."
                    : !ctx.LevelDetected
                        ? "The source roof has no usable level (unset, or the level was deleted). Choose the level for the new roofs."
                        : "The source roof's type could not be read. Choose the type for the new roofs.";

            LevelCombo.ItemsSource = ctx.Levels;
            TypeCombo.ItemsSource = ctx.RoofTypes;

            var level = LevelResolver.Resolve(ctx.Levels, ctx.LevelId, ctx.LevelDetected, ctx.RoofTopZFt,
                                              out bool guessed);
            LevelCombo.SelectedItem = level;
            LevelNote.Text = level == null
                ? "No level pre-selected — pick one."
                : guessed
                    ? $"Pre-selected {level.Display} — the level nearest the roof's top Z. Change it if that is not the one."
                    : $"Detected on the source roof: {level.Display}.";

            var type = LevelResolver.ResolveType(ctx.RoofTypes, ctx.RoofTypeId, ctx.RoofTypeDetected);
            TypeCombo.SelectedItem = type;
            TypeNote.Text = type == null
                ? "No type pre-selected — pick one."
                : $"Detected on the source roof: {type.Name}.";

            UpdateOkState();
        }

        private void Selection_Changed(object sender, SelectionChangedEventArgs e) => UpdateOkState();

        private void UpdateOkState()
        {
            if (OkButton == null) return; // fires once during InitializeComponent, before the button exists
            OkButton.IsEnabled = LevelCombo.SelectedItem is LevelOption && TypeCombo.SelectedItem is NamedId;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (LevelCombo.SelectedItem is not LevelOption level || TypeCombo.SelectedItem is not NamedId type) return;
            ChosenLevelId = level.Id;
            ChosenRoofTypeId = type.Id;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            ChosenLevelId = null;
            ChosenRoofTypeId = null;
            DialogResult = false;
            Close();
        }
    }
}
