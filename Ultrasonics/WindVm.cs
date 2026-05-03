using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Ultrasonics
{
    public class ChartVm : ObservableRecipient
    {
        public ChartVm(WindFrame frame, int ch1, int ch2, string backColour,
            bool zoom, bool fft)
        {
            var ch1S = frame.Source[(ch1, ch2)].Ch1;
            var ch2S = frame.Source[(ch1, ch2)].Ch2;
            if (!fft)
            {
                var tr1 = ch1S.Trace;
                var tr2 = ch2S.Trace;
                if (zoom)
                {
                    tr1 = SingleUltProcessor.GetSubTrace(tr1);
                    tr2 = SingleUltProcessor.GetSubTrace(tr2);
                }
                Data1 = tr1
                    .Select(s => (double)s).ToArray();
                Data2 = tr2
                    .Select(s => (double)s).ToArray();
            }
            else
            {
                var spec1 = SingleUltProcessor.GetSpectrum(ch1S);
                var spec2 = SingleUltProcessor.GetSpectrum(ch2S);
                double[] data1, data2;
                if (zoom)
                {
                    data1 = spec1.Skip(SingleUltProcessor.KeyIndexes.Min() - 6)
                        .Take(15)
                        .Select(c => c.Magnitude).ToArray();
                    data2 = spec2.Skip(SingleUltProcessor.KeyIndexes.Min() - 6)
                        .Take(15)
                        .Select(c => c.Magnitude).ToArray();
                }
                else
                {
                    data1 = spec1.Skip(1).Select(c => c.Magnitude).ToArray();
                    data2 = spec2.Skip(1).Select(c => c.Magnitude).ToArray();
                }
                var max = data1.Concat(data2).Max();
                Data1 = data1.Select(v => v / max * 4096).ToArray();
                Data2 = data2.Select(v => v / max * 4096).ToArray();
            }
            BackColour = backColour;
            double freq = 40000;
#if false
            Phase = frame.Source[(ch1, ch2)].TimeDiffs[1] * freq;
            SNR = frame.Source[(ch1, ch2)].SNR;
#else
            Phase = OnceOff.GetPowerAsStm(frame.Source[(ch1, ch2)].Ch1);
            SNR = OnceOff.GetPowerAsStm(frame.Source[(ch1, ch2)].Ch2);
#endif
        }

        public double[] Data1 { get; set; }
        public double[] Data2 { get; set; }
        public double Phase { get; set; }
        public double SNR { get; set; }
        public string BackColour { get; set; }

        double _mouseX;
        public double MouseX
        {
            get => _mouseX;
            set => SetProperty(ref _mouseX, value);
        }
    }

    public class WindVm : ObservableRecipient
    {
        int _excitationCount;
        public int ExcitationCount 
        {
            get => _excitationCount;
            set => SetProperty(ref _excitationCount, value);
        }

        WindFrame? _frame;
        public WindFrame? Frame
        {
            get => _frame;
            set => SetProperty(ref _frame, value);
        }

        List<WindFrame?> _lastFrames = new List<WindFrame?>();
        public void ClearHistory() => _lastFrames.Clear();

        double _directionDegrees;
        public double DirectionDegrees
        {
            get => _directionDegrees;
            set => SetProperty(ref _directionDegrees, value);
        }

        double _directionRadians;
        public double DirectionRadians
        {
            get => _directionRadians;
            set => SetProperty(ref _directionRadians, value);
        }

        double _speed;
        public double Speed
        {
            get => _speed;
            set => SetProperty(ref _speed, value);
        }

        Vector2 _averageWind;
        public Vector2 AverageWind
        {
            get => _averageWind;
            set => SetProperty(ref _averageWind, value);
        }

        double _averageDir;
        public double AverageDir
        {
            get => _averageDir;
            set => SetProperty(ref _averageDir, value);
        }

        double _averageSpeed;
        public double AverageSpeed
        {
            get => _averageSpeed;
            set => SetProperty(ref _averageSpeed, value);
        }

        DateTimeOffset _frameTime;
        public DateTimeOffset FrameTime
        {
            get => _frameTime;
            set => SetProperty(ref _frameTime, value);
        }

        TimeSpan _frameDuration;
        public TimeSpan FrameDuration
        {
            get => _frameDuration;
            set => SetProperty(ref _frameDuration, value);
        }

        double _windResidual;
        public double WindResidual
        {
            get => _windResidual;
            set => SetProperty(ref _windResidual, value);
        }

        int _calibrationCount;
        public int CalibrationCount
        {
            get => _calibrationCount;
            set => SetProperty(ref _calibrationCount, value);
        }

        bool _showCharts;
        public bool ShowCharts
        {
            get => _showCharts;
            set => SetProperty(ref _showCharts, value);
        }

        private ObservableCollection<ChartVm> _charts = new ObservableCollection<ChartVm>();
        public ObservableCollection<ChartVm> Charts
        {
            get => _charts;
            set => SetProperty(ref _charts, value);
        }

        bool _zoomCharts;
        public bool ZoomCharts
        {
            get => _zoomCharts;
            set
            {
                if (SetProperty(ref _zoomCharts, value))
                    UpdateCharts(Frame);
            }
        }
        bool _fftCharts;
        public bool FFTCharts
        {
            get => _fftCharts;
            set
            {
                if (SetProperty(ref _fftCharts, value))
                    UpdateCharts(Frame);
            }
        }

        Vector2? _webWind;
        public Vector2? WebWind
        {
            get => _webWind;
            set
            {
                if (SetProperty(ref _webWind, value) && value != null)
                {
                    var v = value.Value;
                    WebSpeed = v.Length();
                    WebDir = Math.Atan2(v.X, v.Y) * 180 / Math.PI;
                    SpeedRatio = Speed / WebSpeed;
                }
            }
        }

        double _webSpeed;
        public double WebSpeed
        {
            get => _webSpeed;
            set => SetProperty(ref _webSpeed, value);
        }

        double _webDir;
        public double WebDir
        {
            get => _webDir;
            set => SetProperty(ref _webDir, value);
        }

        double _speedRatio;
        public double SpeedRatio
        {
            get => _speedRatio;
            set => SetProperty(ref _speedRatio, value);
        }

        public WindVm(int excitationCount)
        {
            ExcitationCount = excitationCount;
        }

        DateTimeOffset _lastSet;
        public void SetFrame(WindFrame frame, bool uiSuspended)
        {
            if (ExcitationCount == 0)
            { }

            Frame = frame;

            UpdateHistory(frame);

            if (!uiSuspended)
                UpdateUI(frame);
        }

        public void UpdateUI(WindFrame? frame)
        {
            if (frame == null)
                return;
            ExcitationCount = frame.Source.Values.First().Ch1.ExcitationCount;
            Speed = frame.Wind.Length();
            DirectionRadians = Math.Atan2(frame.Wind.Y, frame.Wind.X);
            DirectionDegrees = DirectionRadians / Math.PI * 180;
            var channels = frame.Source.Values.SelectMany(s => new[] { s.Ch1, s.Ch2 });
            FrameTime = frame.Time;
            var minTime = channels.Min(c => c.Timestamp);
            FrameDuration = FrameTime - minTime;
            WindResidual = frame.WindResidual;
            if (ShowCharts)
                UpdateCharts(frame);
            var averageVec = AverageVec(
                _lastFrames
                .Where(f => f != null),
                f => f.Wind);
            AverageWind = averageVec;
            AverageDir = Math.Atan2(averageVec.Y, averageVec.X) * 180 / Math.PI;
            AverageSpeed = averageVec.Length();
        }

        private void UpdateCharts(WindFrame? frame)
        {
            if (frame == null)
                return;
            Charts = new ObservableCollection<ChartVm>
            {
                new ChartVm(frame, 0, 1, "LightPink", ZoomCharts, FFTCharts),
                new ChartVm(frame, 0, 2, "LightBlue", ZoomCharts, FFTCharts),
                new ChartVm(frame, 0, 3, "LightGreen", ZoomCharts, FFTCharts),
                new ChartVm(frame, 1, 2, "Orange", ZoomCharts, FFTCharts),
                new ChartVm(frame, 1, 3, "Olive", ZoomCharts, FFTCharts),
                new ChartVm(frame, 2, 3, "Firebrick", ZoomCharts, FFTCharts)
            };
        }

        void UpdateHistory(WindFrame frame)
        {
            int nullIdx = -1;
            DateTimeOffset minTime = frame.Time - TimeSpan.FromSeconds(MainVm._averageTime);
            for (int i = 0; i < _lastFrames.Count; i++)
            {
                var cur = _lastFrames[i];
                if (cur == null)
                {
                    nullIdx = i;
                    continue;
                }
                if (cur.Time < minTime || cur.Time > frame.Time)
                {
                    nullIdx = i;
                    _lastFrames[i] = null;
                }
            }
            if (nullIdx >= 0)
                _lastFrames[nullIdx] = frame;
            else
                _lastFrames.Add(frame);
        }

        public static Vector2 AverageVec<T>(IEnumerable<T> source, Func<T, Vector2> selector)
        {
            Vector2 sum = Vector2.Zero;
            int count = 0;
            foreach (var v in source)
            {
                count++;
                sum += selector(v);
            }
            return sum / count;
        }   
    }
}
