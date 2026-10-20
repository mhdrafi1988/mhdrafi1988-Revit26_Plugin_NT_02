using System;
using System.Windows;
using System.Windows.Controls;

namespace Revit26_Plugin_Revised.Shared.Controls
{
    /// <summary>
    /// Standard three-zone tool window layout (CLAUDE.md v1.1).
    /// Header / Body / Footer slots plus the shared status strip
    /// (SummaryText, Progress 0–100, IsRunning). Template lives in
    /// ToolWindowShellTemplate.xaml.
    /// </summary>
    public class ToolWindowShell : Control
    {
        private static readonly Lazy<ControlTemplate> ShellTemplate = new(() =>
        {
            var dictionary = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Revit26_Plugin_Revised;component/Shared/Controls/ToolWindowShellTemplate.xaml")
            };
            return (ControlTemplate)dictionary["ToolWindowShellTemplate"];
        });

        public ToolWindowShell()
        {
            Template = ShellTemplate.Value;
        }

        /// <summary>The shell's body ScrollViewer — the one ScrollViewer a tool window has.</summary>
        public ScrollViewer BodyScrollViewer { get; private set; }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            BodyScrollViewer = GetTemplateChild("PART_BodyScroll") as ScrollViewer;
        }

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(ToolWindowShell));
        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public static readonly DependencyProperty SubtitleProperty =
            DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(ToolWindowShell));
        public string Subtitle
        {
            get => (string)GetValue(SubtitleProperty);
            set => SetValue(SubtitleProperty, value);
        }

        public static readonly DependencyProperty VersionProperty =
            DependencyProperty.Register(nameof(Version), typeof(string), typeof(ToolWindowShell));
        public string Version
        {
            get => (string)GetValue(VersionProperty);
            set => SetValue(VersionProperty, value);
        }

        public static readonly DependencyProperty HeaderContentProperty =
            DependencyProperty.Register(nameof(HeaderContent), typeof(object), typeof(ToolWindowShell));
        public object HeaderContent
        {
            get => GetValue(HeaderContentProperty);
            set => SetValue(HeaderContentProperty, value);
        }

        public static readonly DependencyProperty BodyContentProperty =
            DependencyProperty.Register(nameof(BodyContent), typeof(object), typeof(ToolWindowShell));
        public object BodyContent
        {
            get => GetValue(BodyContentProperty);
            set => SetValue(BodyContentProperty, value);
        }

        public static readonly DependencyProperty FooterContentProperty =
            DependencyProperty.Register(nameof(FooterContent), typeof(object), typeof(ToolWindowShell));
        public object FooterContent
        {
            get => GetValue(FooterContentProperty);
            set => SetValue(FooterContentProperty, value);
        }

        public static readonly DependencyProperty IsRunningProperty =
            DependencyProperty.Register(nameof(IsRunning), typeof(bool), typeof(ToolWindowShell));
        public bool IsRunning
        {
            get => (bool)GetValue(IsRunningProperty);
            set => SetValue(IsRunningProperty, value);
        }

        /// <summary>0–100.</summary>
        public static readonly DependencyProperty ProgressProperty =
            DependencyProperty.Register(nameof(Progress), typeof(double), typeof(ToolWindowShell));
        public double Progress
        {
            get => (double)GetValue(ProgressProperty);
            set => SetValue(ProgressProperty, value);
        }

        /// <summary>True → the progress bar animates instead of showing Progress (for tools with no percentage).</summary>
        public static readonly DependencyProperty IsIndeterminateProperty =
            DependencyProperty.Register(nameof(IsIndeterminate), typeof(bool), typeof(ToolWindowShell));
        public bool IsIndeterminate
        {
            get => (bool)GetValue(IsIndeterminateProperty);
            set => SetValue(IsIndeterminateProperty, value);
        }

        public static readonly DependencyProperty SummaryTextProperty =
            DependencyProperty.Register(nameof(SummaryText), typeof(string), typeof(ToolWindowShell));
        public string SummaryText
        {
            get => (string)GetValue(SummaryTextProperty);
            set => SetValue(SummaryTextProperty, value);
        }
    }
}
