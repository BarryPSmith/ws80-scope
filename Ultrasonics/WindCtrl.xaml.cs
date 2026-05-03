using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Ultrasonics
{
    /// <summary>
    /// Interaction logic for WindCtrl.xaml
    /// </summary>
    public partial class WindCtrl : UserControl
    {
        WindVm? Vm => DataContext as WindVm;

        public WindCtrl()
        {
            InitializeComponent();
        }

        private void UserControl_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (Vm == null)
                return;
            PropertyChangedEventManager.AddHandler(Vm, FrameChanged, nameof(WindVm.Frame));
            PropertyChangedEventManager.AddHandler(Vm, WebChanged, nameof(WindVm.WebWind));
        }

        private void WebChanged(object? sender, PropertyChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (Vm?.WebWind == null)
                    return;
                EnsureGeometry();
                Canvas.SetLeft(webEllipse, WindToCanvasX(Vm.WebWind.Value.X) - webEllipse.Width / 2);
                Canvas.SetTop(webEllipse, WindToCanvasY(Vm.WebWind.Value.Y) - webEllipse.Height / 2);
            });
        }

        float Range = 32;

        private void FrameChanged(object? sender, PropertyChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (Vm == null)
                    return;
                EnsureGeometry();
                var frame = Vm.Frame;
                if (frame == null)
                    return;
                //Range = (float)Math.Max(16, frame.Wind.Length() * 2.2);
                Canvas.SetLeft(WindElipse, WindToCanvasX(frame.Wind.X) - WindElipse.Width / 2);
                Canvas.SetTop(WindElipse, WindToCanvasY(frame.Wind.Y) - WindElipse.Height / 2);
                Canvas.SetLeft(avgEllipse, WindToCanvasX(Vm.AverageWind.X) - avgEllipse.Width / 2);
                Canvas.SetTop(avgEllipse, WindToCanvasY(Vm.AverageWind.Y) - avgEllipse.Height / 2);
                foreach (var kvp in frame.Source)
                {
                    var lines = WindLines[kvp.Key];
                    var data = kvp.Value;
                    for (int shift = 0; shift < data.RadiusVecs.Length; shift++)
                    {
                        var line = lines[shift];
                        var radiusVec = data.RadiusVecs[shift];
                        var tangentVec = WindCalculator.channelTangentVectors[kvp.Key];
                        var pt1 = radiusVec - Range * tangentVec;
                        var pt2 = radiusVec + Range * tangentVec;
                        line.X1 = WindToCanvasX(pt1.X);
                        line.X2 = WindToCanvasX(pt2.X);
                        line.Y1 = WindToCanvasY(pt1.Y);
                        line.Y2 = WindToCanvasY(pt2.Y);
                    }
                }
            });
        }

        double CanvasMinDim => Math.Min(canv.ActualWidth, canv.ActualHeight);

        double WindToCanvasX(double windX)
        {
            return windX / Range * CanvasMinDim / 2 + canv.ActualWidth / 2;
        }

        double WindToCanvasY(double windY)
            => -windY / Range * CanvasMinDim / 2 + canv.ActualHeight / 2;

        //        01           02          03
        //                     12          13
        //                                 23

        Brush[] colours = new[] {
            Brushes.Red, Brushes.Blue,   Brushes.Green,
                         Brushes.Orange, Brushes.Olive, 
                                         Brushes.Firebrick,
            Brushes.Black
        };
        Ellipse? WindElipse, avgEllipse, webEllipse;
        Dictionary<(int, int), Line[]>? WindLines;
        void EnsureGeometry()
        {
            if (WindElipse != null && WindLines != null)
                return;
            WindLines = new Dictionary<(int, int), Line[]>();
            WindElipse = new Ellipse()
            {
                Fill = Brushes.Black,
                Opacity = 0.8,
                Width = 5,
                Height = 5
            };
            avgEllipse = new Ellipse()
            {
                Fill = Brushes.Red,
                Opacity = 0.8,
                Width = 6,
                Height = 6
            };

            webEllipse = new Ellipse()
            {
                Fill = Brushes.Green,
                Opacity = 0.8,
                Width = 6,
                Height = 6
            };
            canv.Children.Add(WindElipse);
            canv.Children.Add(avgEllipse);
            canv.Children.Add(webEllipse);
            int i = 0;
            for (int ch1 = 0; ch1 < 3; ch1++)
                for (int ch2 = ch1 + 1; ch2 < 4; ch2++)
                {
                    WindLines[(ch1, ch2)] = new Line[3];
                    var color = colours[i++];
                    for (int shift = 0; shift < 3; shift++)
                    {
                        var line = new Line()
                        {
                            Stroke = color
                        };
                        WindLines[(ch1, ch2)][shift] = line;
                        canv.Children.Add(line);
                    }
                }
        }
    }
}
