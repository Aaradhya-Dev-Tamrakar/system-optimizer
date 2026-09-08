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
                new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0, 229, 255))));

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

        private void Toggle_Click(object sender, MouseButtonEventArgs e)
        {
            IsChecked = !IsChecked;
        }

        private void UpdateVisualState(bool animate)
        {
            double targetLeft = IsChecked ? 23.0 : 3.0;
            var targetTrackBg = IsChecked ? AccentBrush : new SolidColorBrush(Color.FromRgb(0x1F, 0x24, 0x33));
            var targetTrackBorder = IsChecked ? AccentBrush : new SolidColorBrush(Color.FromRgb(0x32, 0x38, 0x4D));
            var targetThumbColor = IsChecked ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF));

            if (animate)
            {
                var anim = new DoubleAnimation
                {
                    To = targetLeft,
                    Duration = TimeSpan.FromMilliseconds(150),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                Thumb.BeginAnimation(Canvas.LeftProperty, anim);
            }
            else
            {
                Canvas.SetLeft(Thumb, targetLeft);
            }

            Track.Background = targetTrackBg;
            Track.BorderBrush = targetTrackBorder;
            Thumb.Fill = targetThumbColor;
        }
    }
}
