using System.Windows;
using System.Windows.Media;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.Shared.Controls
{
    /// <summary>
    /// Radio-button style dot for a link's ACC Docs status — the shared "ACC" column.
    /// Filled green = ACC link, loaded / current; hollow green = ACC link, not loaded;
    /// amber = out of date; red = failed; grey ring = unknown; faint empty ring = not ACC.
    /// <code>
    /// xmlns:shell="clr-namespace:Revit26_Plugin_Revised.Shared.Controls"
    /// &lt;shell:AccStatusDot State="{Binding AccState}"/&gt;
    /// </code>
    /// The tooltip comes from <see cref="AccLinkStatus.Tip"/> unless <see cref="AutoToolTip"/> is false.
    /// </summary>
    public class AccStatusDot : FrameworkElement
    {
        private static readonly Brush Green = Frozen(Color.FromRgb(0x2E, 0x7D, 0x32));
        private static readonly Brush Amber = Frozen(Color.FromRgb(0xB2, 0x6A, 0x00));
        private static readonly Brush Red = Frozen(Color.FromRgb(0xC6, 0x28, 0x28));
        private static readonly Brush Grey = Frozen(Color.FromRgb(0x6B, 0x72, 0x80));
        private static readonly Brush Faint = Frozen(Color.FromRgb(0xCB, 0xD2, 0xD9));

        /// <summary>The status to draw.</summary>
        public static readonly DependencyProperty StateProperty =
            DependencyProperty.Register(nameof(State), typeof(AccLinkState), typeof(AccStatusDot),
                new FrameworkPropertyMetadata(AccLinkState.NotAcc, FrameworkPropertyMetadataOptions.AffectsRender, OnStateChanged));

        /// <summary>When true (default) the tooltip is set from the state.</summary>
        public static readonly DependencyProperty AutoToolTipProperty =
            DependencyProperty.Register(nameof(AutoToolTip), typeof(bool), typeof(AccStatusDot),
                new PropertyMetadata(true, OnStateChanged));

        /// <summary>Outer diameter of the dot, in device-independent pixels.</summary>
        public static readonly DependencyProperty DiameterProperty =
            DependencyProperty.Register(nameof(Diameter), typeof(double), typeof(AccStatusDot),
                new FrameworkPropertyMetadata(12d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>Creates a dot showing <see cref="AccLinkState.NotAcc"/>.</summary>
        public AccStatusDot()
        {
            VerticalAlignment = VerticalAlignment.Center;
            HorizontalAlignment = HorizontalAlignment.Center;
            SnapsToDevicePixels = true;
            ToolTip = AccLinkStatus.Tip(AccLinkState.NotAcc);
        }

        /// <summary>The status to draw.</summary>
        public AccLinkState State
        {
            get => (AccLinkState)GetValue(StateProperty);
            set => SetValue(StateProperty, value);
        }

        /// <summary>When true (default) the tooltip is set from the state.</summary>
        public bool AutoToolTip
        {
            get => (bool)GetValue(AutoToolTipProperty);
            set => SetValue(AutoToolTipProperty, value);
        }

        /// <summary>Outer diameter of the dot, in device-independent pixels.</summary>
        public double Diameter
        {
            get => (double)GetValue(DiameterProperty);
            set => SetValue(DiameterProperty, value);
        }

        private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var dot = (AccStatusDot)d;
            dot.ToolTip = dot.AutoToolTip ? AccLinkStatus.Tip(dot.State) : null;
        }

        /// <inheritdoc/>
        protected override Size MeasureOverride(Size availableSize) => new Size(Diameter + 2, Diameter + 2);

        /// <inheritdoc/>
        protected override void OnRender(DrawingContext dc)
        {
            Brush ring;
            Brush fill = null;
            switch (State)
            {
                case AccLinkState.Loaded:
                case AccLinkState.Current: ring = Green; fill = Green; break;
                case AccLinkState.NotLoaded: ring = Green; break;
                case AccLinkState.OutOfDate: ring = Amber; fill = Amber; break;
                case AccLinkState.Failed: ring = Red; fill = Red; break;
                case AccLinkState.Unknown: ring = Grey; break;
                default: ring = Faint; break;
            }

            var centre = new Point(ActualWidth / 2, ActualHeight / 2);
            double r = Diameter / 2;
            // Transparent background so the whole dot, not just the ring, shows the tooltip.
            dc.DrawEllipse(Brushes.Transparent, new Pen(ring, 1.5), centre, r - 0.75, r - 0.75);
            if (fill != null) dc.DrawEllipse(fill, null, centre, r * 0.55, r * 0.55);
        }

        private static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
    }
}
