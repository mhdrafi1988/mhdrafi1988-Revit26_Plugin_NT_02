using Revit26_Plugin_Revised.SheetTextReplace.V001.Core.Models;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Revit26_Plugin_Revised.SheetTextReplace.V001.UI.Views
{
    /// <summary>
    /// Attached property that fills a TextBlock with <see cref="TextSegment"/>s: removed text struck
    /// through in red, inserted text in green, the rest plain. Line breaks show as "↵" and a new line.
    /// </summary>
    public static class SegmentText
    {
        /// <summary>The segments to show.</summary>
        public static readonly DependencyProperty SegmentsProperty = DependencyProperty.RegisterAttached(
            "Segments", typeof(IReadOnlyList<TextSegment>), typeof(SegmentText),
            new PropertyMetadata(null, OnSegmentsChanged));

        /// <summary>Gets the segments of <paramref name="element"/>.</summary>
        public static IReadOnlyList<TextSegment> GetSegments(DependencyObject element)
            => (IReadOnlyList<TextSegment>)element.GetValue(SegmentsProperty);

        /// <summary>Sets the segments of <paramref name="element"/>.</summary>
        public static void SetSegments(DependencyObject element, IReadOnlyList<TextSegment> value)
            => element.SetValue(SegmentsProperty, value);

        private static void OnSegmentsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBlock block) return;
            block.Inlines.Clear();
            if (e.NewValue is not IReadOnlyList<TextSegment> segments) return;

            Brush removedFore = Find(block, "BrushStatusError", Color.FromRgb(0xB2, 0x3A, 0x35));
            Brush removedBack = Find(block, "BrushStatusErrorSoft", Color.FromRgb(0xFB, 0xE8, 0xE7));
            Brush insertedFore = Find(block, "BrushStatusOk", Color.FromRgb(0x2E, 0x7D, 0x4F));
            Brush insertedBack = Find(block, "BrushStatusOkSoft", Color.FromRgb(0xE6, 0xF4, 0xEC));
            Brush breakFore = Find(block, "BrushTextTertiary", Color.FromRgb(0x8F, 0xA3, 0xB8));

            for (int s = 0; s < segments.Count; s++)
            {
                var segment = segments[s];
                string text = segment.Text.Replace("\r\n", "\r").Replace('\n', '\r');
                if (s == segments.Count - 1)
                    text = text.TrimEnd('\r'); // Revit ends most notes with a line break: don't show it
                string[] lines = text.Split('\r');
                for (int i = 0; i < lines.Length; i++)
                {
                    if (i > 0)
                    {
                        block.Inlines.Add(new Run("↵") { Foreground = breakFore, FontSize = block.FontSize * 0.85 });
                        block.Inlines.Add(new LineBreak());
                    }
                    if (lines[i].Length == 0) continue;

                    var run = new Run(lines[i]);
                    switch (segment.Kind)
                    {
                        case SegmentKind.Removed:
                            run.Foreground = removedFore;
                            run.Background = removedBack;
                            run.TextDecorations = TextDecorations.Strikethrough;
                            break;
                        case SegmentKind.Inserted:
                            run.Foreground = insertedFore;
                            run.Background = insertedBack;
                            run.FontWeight = FontWeights.SemiBold;
                            break;
                    }
                    block.Inlines.Add(run);
                }
            }
        }

        private static Brush Find(FrameworkElement element, string key, Color fallback)
            => element.TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);
    }
}
