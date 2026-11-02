using System.Windows;
using System.Windows.Input;
using Autodesk.Revit.UI;

namespace Revit26_Plugin_Revised.WorksetsElementsBrowser.WSEB004.UI.Views
{
    /// <summary>
    /// Small modal prompt for the name of a new Revit selection set. Call <see cref="Initialize"/>,
    /// then ShowDialog(); on a true result <see cref="SetName"/> holds the trimmed name.
    /// </summary>
    public partial class SelectionSetNameWindow : Window
    {
        /// <summary>The name the user confirmed.</summary>
        public string SetName { get; private set; } = string.Empty;

        /// <summary>Creates the dialog.</summary>
        public SelectionSetNameWindow()
        {
            InitializeComponent();
        }

        /// <summary>Sets the explanatory text and the suggested name (selected for quick overtyping).</summary>
        public void Initialize(string prompt, string suggestedName)
        {
            PromptText.Text = prompt;
            NameBox.Text = suggestedName;
            Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
        }

        private void NameBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            TryConfirm();
            e.Handled = true;
        }

        private void Save_Click(object sender, RoutedEventArgs e) => TryConfirm();

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void TryConfirm()
        {
            string name = NameBox.Text.Trim();
            if (name.Length == 0)
            {
                TaskDialog.Show("Worksets & Elements Browser", "Enter a name for the selection set.");
                return;
            }
            SetName = name;
            DialogResult = true;
            Close();
        }
    }
}
