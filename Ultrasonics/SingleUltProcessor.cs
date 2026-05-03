using Aelian.FFT;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Odbc;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Markup;

namespace Ultrasonics
{
    public class WindFrameInfo
    {
        public required SerialPacket Ch1 { get; set; }
        public required SerialPacket Ch2 { get; set; }
        //public double PhaseDiff { get; set; }
        required public double[] TimeDiffs { get; set; }
        public double SNR { get; set; }
        required public Vector2[] RadiusVecs { get; set; }
    }

    public class WindFrame
    {
        public required Dictionary<(int, int), WindFrameInfo> Source { get; set; }
        public Vector2 Wind;
        public Vector2 ShortWind;
        public Vector2 LongWind;
        public double WindResidual;
        public DateTimeOffset Time;
    }

    public class SingleUltProcessor
    {
        static SingleUltProcessor()
        {
            FastFourierTransform.Initialize();
        }

        public static int[] KeyIndexes { get; set; } = new[] { 10, 11 };
        public static int StartIndex { get; set; } = 192;
        public static int DataSize { get; set; } = 256;
        public static double SampleTime { get; set; } = 1E-6;
        public TimeSpan FrameMaxTimespan { get; set; } = TimeSpan.FromSeconds(0.5);
        public WindCalculator Calculator { get; set; } = new WindCalculator();
        public event EventHandler<WindFrame>? NewFrameAvailable;

        protected ConcurrentDictionary<(int, int), SerialPacket> _recentPackets = new ConcurrentDictionary<(int, int), SerialPacket>();

        bool _calibrating = false;
        ConcurrentBag<WindFrame> _calibrationFrames = new ConcurrentBag<WindFrame>();
        public int CalibrationFrameCount => _calibrationFrames.Count;
        public void BeginCalibration()
        {
            _calibrating = true;
            _calibrationFrames = new ConcurrentBag<WindFrame>();
        }

        public void EndCalibration()
        {
            _calibrating = false;
            var averageDiffs = new Dictionary<(int ch1, int ch2), double>();
            for (int ch1 = 0; ch1 < 3; ch1++)
                for (int ch2 = ch1 + 1; ch2 < 4; ch2++)
                    averageDiffs[(ch1, ch2)] = _calibrationFrames.Average(f =>
                    {
                        var td = f.Source[(ch1, ch2)].TimeDiffs;
                        return td[td.Length / 2];
                    });
            Calculator.CalibrationData = averageDiffs;
        }

        public static Complex[] GetSpectrum(SerialPacket packet)
        {
            var trace = GetSubTrace(packet.Trace);
                //.Select(e => new Complex(e, 0)).ToArray();
            // window our data:
            var avg = trace.Average(v => v);
            Complex[] buffer = new Complex[trace.Length];
            double Hann(int i) => Math.Sin(Math.PI * (i + 1) / (DataSize + 1)) * Math.Sin(Math.PI * i / (DataSize - 1));
            for (int i = 0; i < trace.Length; i++)
            {
                buffer[i] = (trace[i] - avg) * Hann(i);
            }
            FastFourierTransform.FFT(buffer, true);
            return buffer;
        }

        public virtual void ProcessSerialPacket(SerialPacket inPacket)
        {
            var key = (inPacket.OutputChannel, inPacket.InputChannel);
            if (key == (0, 1))
                _recentPackets.Clear();
            _recentPackets[key] = inPacket;
            NotifyIfFrameAvailable();
        }

        protected bool NotifyIfFrameAvailable()
        {
            var channelPairs = Enumerable.Range(0, 4)
                .SelectMany(i => Enumerable.Range(0, 4)
                                            .Except(new[] { i })
                                            .Select(j => (i, j)));
            var recentPacketsLocal = _recentPackets.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            if (!channelPairs.All(k => recentPacketsLocal.ContainsKey(k)))
                return false;

            var minTS = recentPacketsLocal.Values.Min(p => p.Timestamp);
            var maxTS = recentPacketsLocal.Values.Max(p => p.Timestamp);
            if (maxTS - minTS > FrameMaxTimespan)
                return false;

            byte TransducerPairToChannel((int, int) key)
            {
                var min = Math.Min(key.Item1, key.Item2);
                var max = Math.Max(key.Item1, key.Item2);
                switch (min)
                {
                    case 0:
                        switch (max)
                        {
                            case 1: return 0;
                            case 2: return 1;
                            case 3: return 2;
                            default: throw new Exception();
                        }
                    case 1:
                        switch (max)
                        {
                            case 2: return 3;
                            case 3: return 4;
                            default: throw new Exception();
                        }
                    case 2:
                        switch (max)
                        {
                            case 3: return 5;
                            default: throw new Exception();
                        }
                    default: throw new Exception();
                }
            }
            checked
            {
                foreach (var packet in _recentPackets)
                {
                    _firmware.g_wind_measurement = GetSubTrace(packet.Value.Trace)
                        .Select(v => (ushort)v).ToArray();
                    var ch = TransducerPairToChannel(packet.Key);
                    byte dir = (byte)(packet.Key.Item1 < packet.Key.Item2 ? 0 : 1);
                    _firmware.processWindWaveform(ch, dir);
                }
                _firmware.calculate_wind(out _, out _);
            }

            if (this is EdgeUltProcessor)
            { }
            var frame = CreateFrame(recentPacketsLocal);
            if (frame == null)
            {
                _recentPackets.Clear();
                return false;
            }
            if (_calibrating)
                _calibrationFrames.Add(frame);
            NewFrameAvailable?.Invoke(this, frame);
            
            return true;
        }

        public static short[] GetSubTrace(short[] trace)
        {
            if (trace.Length >= StartIndex + DataSize)
                return trace[StartIndex..(StartIndex + DataSize)];
            else
                return trace;
        }

        FirmwareCopy _firmware = new FirmwareCopy();

        public virtual (double[] diffs, double snr)? GetTimeDifference(SerialPacket packet1, SerialPacket packet2)
        {
            double[] diffs = new double[3];
            var buffer1 = GetSpectrum(packet1);
            var buffer2 = GetSpectrum(packet2);
            Complex Convoluted(int i) => buffer1[i] * Complex.Conjugate(buffer2[i]);
            var avgFreq =
                        KeyIndexes.Sum(i => i * Convoluted(i).Magnitude)
                            /
                        KeyIndexes.Sum(i => Convoluted(i).Magnitude);
            var basePhase = KeyIndexes.SumC(i => i * Convoluted(i)).Phase;
            packet1.KeyVal = KeyIndexes.SumC(i => i * buffer1[i]);
            packet2.KeyVal = KeyIndexes.SumC(i => i * buffer2[i]);
            for (int shift = -1; shift <= 1; shift++)
            {
                var avgPhase = basePhase + 2 * shift * Math.PI;
                var diff = avgPhase / avgFreq;

                // If we are on one index, we're currently at
                // diff = phase / i
                // We know that t_diff = period * phase / 2pi
                // period = FFT_Period / i
                // t_diff = FFT_Period / i * phase / 2pi
                //        = FFT_Period / 2pi * phase / i
                // t_diff = FFT_Period / 2pi * diff
                var fftPeriod = DataSize * SampleTime;
                var timeDiff = diff * fftPeriod / (2 * Math.PI);
                diffs[shift + 1] = timeDiff;
            }

            // For debug purposes:
            var indivudalTimes = KeyIndexes.Select(i =>
                (buffer1[i].Phase - buffer2[i].Phase) * (DataSize / (double)i) / (2 * Math.PI) * SampleTime)
                .ToList();

            var (directCalc, directCalcPeriod) = GetTimeDifference2(packet1, packet2);

            var usedPower1 = KeyIndexes.Sum(i => (buffer1[i] * buffer1[i]).Magnitude) * 2;
            var usedPower2 = KeyIndexes.Sum(i => (buffer2[i] * buffer2[i]).Magnitude) * 2;
            var usedCrossPower = KeyIndexes.Sum(i => (buffer1[i] * buffer2[i]).Magnitude) * 2;
            var totalPower1 = buffer1.Skip(1).Sum(v => (v * v).Magnitude);
            var totalPower2 = buffer2.Skip(1).Sum(v => (v * v).Magnitude);

            var snr1 = usedPower1 / totalPower1;
            var snr2 = usedPower2 / totalPower2;
            var snrCross = usedCrossPower / (Math.Sqrt(totalPower1 * totalPower2) - usedCrossPower);

            return (diffs, snrCross);
        }

        public (double, double) GetTimeDifference2(SerialPacket packet1, SerialPacket packet2)
        {
            var minPeriod = DataSize / (KeyIndexes.Max() + 0.5);
            var maxPeriod = DataSize / (KeyIndexes.Min() - 0.5);
            minPeriod = Math.Round(minPeriod * 2) / 2;
            maxPeriod = Math.Round(maxPeriod * 2) / 2;
            double maxPower = 0;
            double bestDiff = 0;
            double bestPeriod = 0;
            var tr1 = GetSubTrace(packet1.Trace);
            var tr2 = GetSubTrace(packet2.Trace);
            var packet1Avg = tr1.Average(v=>v);
            var packet2Avg = tr2.Average(v=>v);
            var packet1TotalPower = tr1.Average(v => (v - packet1Avg) * (v - packet1Avg));
            var packet2TotalPower = tr2.Average(v => (v - packet2Avg) * (v - packet2Avg));
            double totalCrossPower = Math.Sqrt(packet1TotalPower * packet2TotalPower);

            for (double period = minPeriod; period <= maxPeriod + 0.1; period += 0.5)
            {
                var cycles = Math.Floor(DataSize / period);
                var ourDataSize = (int)Math.Round(period * cycles);
                var endIndex = (ourDataSize);
                var packet1CosComponent = Convolute(i => Math.Cos(2 * Math.PI * i / period), tr1[0..endIndex]) / ourDataSize;
                var packet1SinComponent = Convolute(i => Math.Sin(2 * Math.PI * i / period), tr1[0..endIndex]) / ourDataSize;
                var packet2CosComponent = Convolute(i => Math.Cos(2 * Math.PI * i / period), tr2[0..endIndex]) / ourDataSize;
                var packet2SinComponent = Convolute(i => Math.Sin(2 * Math.PI * i / period), tr2[0..endIndex]) / ourDataSize;
                var power1 = (packet1CosComponent * packet1CosComponent + packet1SinComponent * packet1SinComponent) * 2;
                var power2 = (packet2CosComponent * packet2CosComponent + packet2SinComponent * packet2SinComponent) * 2;
                var crossPower = Math.Sqrt(power1 * power2);
                var phase1 = Math.Atan2(packet1CosComponent, packet1SinComponent);
                var phase2 = Math.Atan2(packet2CosComponent, packet2SinComponent);
                var phaseDiff = Normalise(phase1 - phase2);
                var diff = phaseDiff * period / (2 * Math.PI);
                var timeDiff = diff * SampleTime;
                var trace1 = tr1[0..endIndex];
                var trace2 = tr2[0..endIndex];
                var snr1 = power1 / packet1TotalPower;
                var snr2 = power2 / packet2TotalPower;
                var snrCross = crossPower / totalCrossPower;
                if (crossPower > maxPower)
                {
                    maxPower = crossPower;
                    bestDiff = timeDiff;
                    bestPeriod = period;
                }
            }
            return (bestDiff, bestPeriod);
        }

        double Convolute(Func<int, double> func, Span<short> data)
        {
            double ret = 0;
            for (int i = 0; i < data.Length; i++)
            {
                ret += data[i] * func(i);
            }
            return ret;
        }

        public WindFrame? CreateFrame(Dictionary<(int, int), SerialPacket> recentPacketsLocal)
        {
            var infos = new Dictionary<(int, int), WindFrameInfo>();
            var temp = recentPacketsLocal.Values.First().Temperature_x10 / 10d;
            for (int ch1 = 0; ch1 < 3; ch1++)
                for (int ch2 = ch1 + 1; ch2 < 4; ch2++)
                {
                    var key1 = (ch1, ch2);
                    var key2 = (ch2, ch1);
                    var timeDiff = GetTimeDifference(recentPacketsLocal[key1], recentPacketsLocal[key2]);
                    if (timeDiff == null)
                        return null;
                    infos[key1] = new WindFrameInfo
                    {
                        Ch1 = recentPacketsLocal[key1],
                        Ch2 = recentPacketsLocal[key2],
                        TimeDiffs = timeDiff.Value.diffs,
                        SNR = timeDiff.Value.snr,
                        RadiusVecs = timeDiff.Value.diffs.Select(d => Calculator.GetRadiusVector(ch1, ch2, d, temp)).ToArray()
                    };
                }
            var windspeedTpl = Calculator.GetWind(infos.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.TimeDiffs), temp);
            var (wind, residual) = windspeedTpl;
            var (shortWind, _) = Calculator.GetWind(infos.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.TimeDiffs), temp, WindCalculator.WindCalcMethod.ShortAxis);
            var (longWind, _) = Calculator.GetWind(infos.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.TimeDiffs), temp, 
                    WindCalculator.WindCalcMethod.LongAxis, shortWind.Length());
            return new WindFrame
            {
                Source = infos,
                Wind = wind,
                ShortWind = shortWind,
                LongWind = longWind,
                WindResidual = residual,
                Time = infos.Values.Max(c => Max(c.Ch1.Timestamp, c.Ch2.Timestamp))
            };
        }

        DateTimeOffset Max(DateTimeOffset v1, DateTimeOffset v2)
        {
            return v1 > v2 ? v1 : v2;
        }

        double Normalise(double phase)
        {
            var initialPhase = phase;
            while (phase > Math.PI / 2)
                phase -= Math.PI;
            while (phase < -Math.PI / 2)
                phase += Math.PI;
            if (Math.Abs(phase) > 2)
            { }
            return phase;
        }
    }
}
