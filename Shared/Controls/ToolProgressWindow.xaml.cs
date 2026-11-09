using System.Windows;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.Shared.Controls
{
    /// <summary>
    /// Small modeless progress pop-up (text, bar, Cancel button) for tools without a window.
    /// Shown and closed by <see cref="ToolCancellation"/>; tools do not use it directly.
    /// </summary>
    public partial class ToolProgressWindow : Window
    {
        /// <summary>Creates the pop-up with the tool title and the text shown before the first update.</summary>
        public ToolProgressWindow(string title, string initialText)
        {
            InitializeComponent();
            Title = title;
            StatusText.Text = initialText;
        }

        /// <summary>Sets the progress bar (0–100) and the text line.</summary>
        public void Update(double percent, string text)
        {
            Bar.Value = percent;
            if (!string.IsNullOrEmpty(text)) StatusText.Text = text;
        }

        /// <summary>Closes the pop-up when the run has ended (the host already stopped listening to Closing).</summary>
        public void CloseFromHost()
        {
            try { Close(); }
            catch { /* already closed */ }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) => ToolCancellation.RequestFor(this);
    }
}
