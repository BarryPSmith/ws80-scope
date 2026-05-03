using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Ultrasonics
{
    internal class Chart : Canvas
    {
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var pos = e.GetPosition(this);
            var xPortion = pos.X / ActualWidth;
            MouseX = xPortion * (Data.Count() - 1);
        }

        public double MouseX
        {
            get { return (double)GetValue(MouseXProperty); }
            set { SetValue(MouseXProperty, value); }
        }

        // Using a DependencyProperty as the backing store for MouseX.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty MouseXProperty =
            DependencyProperty.Register("MouseX", typeof(double), typeof(Chart), new PropertyMetadata(0.0));



        public IEnumerable<double> Data
        {
            get { return (IEnumerable<double>)GetValue(DataProperty); }
            set { SetValue(DataProperty, value); }
        }

        // Using a DependencyProperty as the backing store for Data.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty DataProperty =
            DependencyProperty.Register("Data", typeof(IEnumerable<double>), typeof(Chart), 
                new FrameworkPropertyMetadata(default, FrameworkPropertyMetadataOptions.AffectsRender));



        public double Min
        {
            get { return (double)GetValue(MinProperty); }
            set { SetValue(MinProperty, value); }
        }

        // Using a DependencyProperty as the backing store for Min.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty MinProperty =
            DependencyProperty.Register("Min", typeof(double), typeof(Chart), 
                new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

        public double Max
        {
            get { return (double)GetValue(MaxProperty); }
            set { SetValue(MaxProperty, value); }
        }

        // Using a DependencyProperty as the backing store for Max.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty MaxProperty =
            DependencyProperty.Register("Max", typeof(double), typeof(Chart),
                new FrameworkPropertyMetadata(4100d, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush Stroke
        {
            get { return (Brush)GetValue(StrokeProperty); }
            set { SetValue(StrokeProperty, value); }
        }

        // Using a DependencyProperty as the backing store for Stroke.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty StrokeProperty =
            DependencyProperty.Register("Stroke", typeof(Brush), typeof(Chart), 
                new PropertyMetadata(Brushes.Black));

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            if (Data == null)
                return;

            int count = Data.Count();
            if (count < 2)
                return;

            int i = 0;
            double range = Max - Min;
            var pen = new Pen(Stroke, 1);
            List<Point> coordinates = new();
            foreach (var val in Data)
            {
                double Y = ActualHeight - (val - Min) / range * ActualHeight;
                double X = ActualWidth / (count - 1) * i;
                //if (!first)
                //  geometry.
                //dc.DrawLine(pen, new Point(lastX, lastY),
                //  new Point(X, Y));
                coordinates.Add(new Point(X, Y));
                i++;
            }
            PathGeometry geometry = new PathGeometry();
            PathFigure figure = new PathFigure(coordinates[0], new[] { new PolyLineSegment(coordinates.Skip(1), true) }, false);
            geometry.Figures.Add(figure);
            dc.DrawGeometry(null, pen, geometry);
        }
    }
}
