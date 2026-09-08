using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace NovaOptimizer.Controls
{
    public partial class HistoryGraph : UserControl
    {
        private const int MaxPoints = 60;
        private readonly List<double> _values = new(MaxPoints);
        private double _maxValue = 100.0;
        private string _unit = "%";

        public static readonly DependencyProperty LineBrushProperty =
            DependencyProperty.Register(
                nameof(LineBrush),
                typeof(Brush),
                typeof(HistoryGraph),
                new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0, 229, 255)), OnLineBrushChanged));

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(
                nameof(Title),
                typeof(string),
                typeof(HistoryGraph),
                new PropertyMetadata("TELEMETRY", OnTitleChanged));

        public Brush LineBrush
        {
            get => (Brush)GetValue(LineBrushProperty);
            set => SetValue(LineBrushProperty, value);
        }

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public double MaxValue
        {
            get => _maxValue;
            set { _maxValue = value > 0 ? value : 100.0; Redraw(); }
        }

        public string Unit
        {
            get => _unit;
            set { _unit = value; }
        }

        public HistoryGraph()
        {
            InitializeComponent();
            for (int i = 0; i < MaxPoints; i++)
            {
                _values.Add(0.0);
            }
            UpdateBrushes();
        }

        private static void OnLineBrushChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is HistoryGraph g)
            {
                g.UpdateBrushes();
            }
        }

        private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is HistoryGraph g && e.NewValue is string title)
            {
                g.TxtTitle.Text = title;
            }
        }

        private void UpdateBrushes()
        {
            CurveLine.Stroke = LineBrush;
            TxtCurrentValue.Foreground = LineBrush;

            if (LineBrush is SolidColorBrush scb)
            {
                var grad = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(0, 1)
                };
                grad.GradientStops.Add(new GradientStop(Color.FromArgb(160, scb.Color.R, scb.Color.G, scb.Color.B), 0.0));
                grad.GradientStops.Add(new GradientStop(Color.FromArgb(10, scb.Color.R, scb.Color.G, scb.Color.B), 1.0));
                AreaPolygon.Fill = grad;
            }
            else
            {
                AreaPolygon.Fill = LineBrush;
            }
        }

        public void AddValue(double val)
        {
            if (_values.Count >= MaxPoints)
            {
                _values.RemoveAt(0);
            }
            _values.Add(val);

            TxtCurrentValue.Text = $"{val:F0}{_unit}";
            Redraw();
        }

        private void UserControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawGridLines();
            Redraw();
        }

        private void DrawGridLines()
        {
            GridCanvas.Children.Clear();
            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 0 || h <= 0) return;

            var gridBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));

            // Horizontal grid lines at 25%, 50%, 75%
            for (int i = 1; i <= 3; i++)
            {
                double y = h * (i / 4.0);
                var line = new Line
                {
                    X1 = 0,
                    Y1 = y,
                    X2 = w,
                    Y2 = y,
                    Stroke = gridBrush,
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 2, 4 }
                };
                GridCanvas.Children.Add(line);
            }
        }

        private void Redraw()
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 0 || h <= 0 || _values.Count < 2) return;

            double stepX = w / (MaxPoints - 1);
            var points = new PointCollection(_values.Count);
            var areaPoints = new PointCollection(_values.Count + 2);

            areaPoints.Add(new Point(0, h));

            for (int i = 0; i < _values.Count; i++)
            {
                double val = Math.Clamp(_values[i], 0, _maxValue);
                double norm = val / _maxValue;
                double x = i * stepX;
                double y = h - (norm * (h - 24)) - 4; // leave 24px clearance for header text

                var pt = new Point(x, y);
                points.Add(pt);
                areaPoints.Add(pt);
            }

            areaPoints.Add(new Point((_values.Count - 1) * stepX, h));

            CurveLine.Points = points;
            AreaPolygon.Points = areaPoints;
        }
    }
}
