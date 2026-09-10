using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NovaOptimizer.Controls
{
    public partial class ToggleSwitch : UserControl
    {
        public static readonly DependencyProperty IsCheckedProperty =
            DependencyProperty.Register(
                nameof(IsChecked),
                typeof(bool),
                typeof(ToggleSwitch),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnIsCheckedChanged));

        public static readonly DependencyProperty HeaderProperty =
            DependencyProperty.Register(
                nameof(Header),
                typeof(string),
                typeof(ToggleSwitch),
                new PropertyMetadata(string.Empty, OnHeaderChanged));

        public static readonly DependencyProperty AccentBrushProperty =
            DependencyProperty.Register(
                nameof(AccentBrush),
                typeof(Brush),
                typeof(ToggleSwitch),
                new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6))));

        public event RoutedPropertyChangedEventHandler<bool>? CheckedChanged;

        public bool IsChecked
        {
            get => (bool)GetValue(IsCheckedProperty);
            set => SetValue(IsCheckedProperty, value);
        }

        public string Header
        {
            get => (string)GetValue(HeaderProperty);
            set => SetValue(HeaderProperty, value);
        }

        public Brush AccentBrush
        {
            get => (Brush)GetValue(AccentBrushProperty);
            set => SetValue(AccentBrushProperty, value);
        }

        public ToggleSwitch()
        {
            InitializeComponent();
            UpdateVisualState(false);
        }

        private static void OnIsCheckedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ToggleSwitch ts)
            {
                bool newVal = (bool)e.NewValue;
                bool oldVal = (bool)e.OldValue;
                ts.UpdateVisualState(true);
                ts.CheckedChanged?.Invoke(ts, new RoutedPropertyChangedEventArgs<bool>(oldVal, newVal));
            }
        }

        private static void OnHeaderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ToggleSwitch ts)
            {
                string text = (string)e.NewValue;
                if (string.IsNullOrEmpty(text))
                {
                    ts.TxtHeader.Visibility = Visibility.Collapsed;
                }
                else
                {
                    ts.TxtHeader.Text = text;
                    ts.TxtHeader.Visibility = Visibility.Visible;
                }
            }
        }

        private static readonly SolidColorBrush OffTrackBg = new(Color.FromRgb(0x0D, 0x12, 0x1B));
        private static readonly SolidColorBrush OffTrackBorder = new(Color.FromRgb(0x1E, 0x29, 0x3B));
        private static readonly SolidColorBrush OffThumbColor = new(Color.FromRgb(0x94, 0xA3, 0xB8));

        private static readonly DoubleAnimation CheckAnim = new()
        {
            To = 23.0,
            Duration = TimeSpan.FromMilliseconds(150),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        private static readonly DoubleAnimation UncheckAnim = new()
        {
            To = 3.0,
            Duration = TimeSpan.FromMilliseconds(150),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        static ToggleSwitch()
        {
            OffTrackBg.Freeze();
            OffTrackBorder.Freeze();
            OffThumbColor.Freeze();
            CheckAnim.Freeze();
            UncheckAnim.Freeze();
        }

        private void Toggle_Click(object sender, MouseButtonEventArgs e)
        {
            IsChecked = !IsChecked;
        }

        private void UpdateVisualState(bool animate)
        {
            double targetLeft = IsChecked ? 23.0 : 3.0;
            var targetTrackBg = IsChecked ? AccentBrush : OffTrackBg;
            var targetTrackBorder = IsChecked ? AccentBrush : OffTrackBorder;
            var targetThumbColor = IsChecked ? Brushes.White : OffThumbColor;

            if (animate)
            {
                Thumb.BeginAnimation(Canvas.LeftProperty, IsChecked ? CheckAnim : UncheckAnim);
            }
            else
            {
                Thumb.BeginAnimation(Canvas.LeftProperty, null);
                Canvas.SetLeft(Thumb, targetLeft);
            }

            Track.Background = targetTrackBg;
            Track.BorderBrush = targetTrackBorder;
            Thumb.Fill = targetThumbColor;
        }
    }
}
