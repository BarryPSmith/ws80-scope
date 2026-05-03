using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Printing;
using System.Text;
using System.Threading.Tasks;

namespace Ultrasonics
{
    public class WindCalculator
    {
        public Dictionary<(int, int), double>? CalibrationData { get; set; }
        //public double SamplesPerCycle = 25;
        //public double EffeciveLength { get; set; } = 29E-3;
        public double ShortEffectiveLength { get; set; } = 16E-3;
        public double LongEffectiveLength { get; set; } = 37E-3;

        Dictionary<(int, int), double> channelDirections = new Dictionary<(int, int), double>()
            {
                {(0,1),45 },
                {(0,2), 135 },
                {(0,3),90 },
                {(1,2),180 },
                {(1,3),135 },
                {(2,3),45 }
            };

#if false
        public static Dictionary<(int, int), Vector2> channelRadiusVectors = new Dictionary<(int, int), Vector2>()
        {
            {(0,1), Vector2.Normalize(new Vector2(1, 1)) },
            {(0,2), Vector2.Normalize(new Vector2(1, -1)) },
            {(0,3), new Vector2(1, 0) },
            {(1,2), new Vector2(0, -1) },
            {(1,3), Vector2.Normalize(new Vector2(1, -1)) },
            {(2,3), Vector2.Normalize(new Vector2(1, 1)) }
        };

        public static Dictionary<(int, int), Vector2> channelTangentVectors = new Dictionary<(int, int), Vector2>()
        {
            {(0,1), Vector2.Normalize(new Vector2(1, -1)) },
            {(0,2), Vector2.Normalize(new Vector2(1, 1)) },
            {(0,3), new Vector2(0, 1) },
            {(1,2), new Vector2(1, 0) },
            {(1,3), Vector2.Normalize(new Vector2(1, 1)) },
            {(2,3), Vector2.Normalize(new Vector2(1, -1)) }
        };
#else
        public static Dictionary<(int, int), Vector2> channelRadiusVectors = new Dictionary<(int, int), Vector2>()
        {
            {(0,1), Vector2.Normalize(new Vector2(1 , -1)) },
            {(0,2), Vector2.Normalize(new Vector2(-1, -1)) },
            {(0,3),                   new Vector2(0 , -1) },
            {(1,2),                   new Vector2(-1,  0) },
            {(1,3), Vector2.Normalize(new Vector2(-1, -1)) },
            {(2,3), Vector2.Normalize(new Vector2(1 , -1)) }
        };

        public static Dictionary<(int, int), Vector2> channelTangentVectors = new Dictionary<(int, int), Vector2>()
        {
            {(0,1), Vector2.Normalize(new Vector2(1,  1)) },
            {(0,2), Vector2.Normalize(new Vector2(1, -1)) },
            {(0,3),                   new Vector2(1,  0) },
            {(1,2),                   new Vector2(0,  1) },
            {(1,3), Vector2.Normalize(new Vector2(1, -1)) },
            {(2,3), Vector2.Normalize(new Vector2(1,  1)) }
        };
#endif

        public enum WindCalcMethod
        {
            Both, ShortAxis, LongAxis
        }

        public (Vector2 wind, double residual) GetWind(Dictionary<(int ch1, int ch2), double[]> timeDiffs,
            double temperature, WindCalcMethod windCalcMethod = WindCalcMethod.Both, double? guess = null)
        {
            // I know this looks like just a simple average, but it's actually a least squares fit to the wind lines.
            IEnumerable<KeyValuePair<(int ch1, int ch2), double[]>> timeDiffsToUse = timeDiffs;
            var divisor = 3;
            int shiftStart = timeDiffs[(0, 3)].Length / 2;
            int shiftEnd = shiftStart + 1;
            switch (windCalcMethod)
            {
                case WindCalcMethod.ShortAxis:
                    timeDiffsToUse = timeDiffs.Where(kvp => kvp.Key != (0, 3) && kvp.Key != (1, 2));
                    divisor = 2;
                    break;
                case WindCalcMethod.LongAxis:
                    timeDiffsToUse = timeDiffs.Where(kvp => kvp.Key == (0, 3) || kvp.Key == (1, 2));
                    divisor = 1;
                    break;
                case WindCalcMethod.Both:
                    shiftStart = 0;
                    shiftEnd = timeDiffs[(0, 3)].Length;
                    break;
            }
            if (guess != null)
            {
                shiftStart = 0;
                shiftEnd = timeDiffs[(0, 3)].Length;
            }
            double lowestGuessDiff = double.PositiveInfinity;
            double lowestResidual = double.PositiveInfinity;
            Vector2 bestWind = Vector2.Zero;
            for (int shift03 = shiftStart; shift03 < shiftEnd; shift03++)
                for (int shift12 = shiftStart; shift12 < shiftEnd; shift12++)
                {
                    Vector2 windSum = Vector2.Zero;
                    var radius03 = GetRadiusVector(0, 3, timeDiffs[(0, 3)][shift03], temperature);
                    var radius12 = GetRadiusVector(1, 2, timeDiffs[(1, 2)][shift12], temperature);

                    foreach (var kvp in timeDiffsToUse)
                    {
                        double value = GetValue(kvp.Key, kvp.Value, shift03, shift12);
                        var radiusVector = GetRadiusVector(kvp.Key.ch1, kvp.Key.ch2, value, temperature);
                        windSum += radiusVector;
                    }
                    var wind = windSum / divisor;
                    double residual = 0;
                    foreach (var kvp in timeDiffsToUse)
                    {
                        double value = GetValue(kvp.Key, kvp.Value, shift03, shift12);
                        var radiusVector = GetRadiusVector(kvp.Key.ch1, kvp.Key.ch2, value, temperature);
                        var diff = wind - radiusVector;
                        var dist = diff.LengthSquared() - (diff * channelTangentVectors[(kvp.Key.ch1, kvp.Key.ch2)]).LengthSquared();
                        residual += dist;
                    }
                    residual = Math.Sqrt(residual / timeDiffsToUse.Count());

                    if (residual < lowestResidual)
                    {
                        bestWind = wind;
                        lowestResidual = residual;
                    }

                    if (guess.HasValue)
                    {
                        double diff = Math.Abs(wind.Length() - guess.Value);
                        if (diff < lowestGuessDiff)
                        {
                            lowestGuessDiff = diff;
                            bestWind = wind;
                        }
                    }
                }

            return (bestWind, lowestResidual);
        }


        private double GetValue((int ch1, int ch2) key, double[] values, int shift03, int shift12)
        {
            switch (key)
            {
                case (0, 3):
                    return values[shift03];
                case (1, 2):
                    return values[shift12];
                default:
                    return values[values.Length / 2];
            }
        }

        public Vector2 GetRadiusVector(int ch1, int ch2, double timeDiff, double temperature)
        {
            if (CalibrationData != null && CalibrationData.ContainsKey((ch1, ch2)))
                timeDiff -= CalibrationData[(ch1, ch2)];
            var speed1 = GetSpeed(ch1, ch2, timeDiff, temperature);
            return (float)speed1 * channelRadiusVectors[(ch1, ch2)];
        }

        public double GetSpeed(int ch1, int ch2, double timeDiff, double temperature)
        {
            /*double cycleDiff = phaseDiff * SamplesPerCycle / (2 * Math.PI);
            var timeDiff = cycleDiff * 1E-6;*/
            double speedOfSound = 331.5 + temperature * 0.61;
            double effectiveLength;
            if ((ch1 == 0 && ch2 == 3) ||
                (ch1 == 1 && ch2 == 2))
                effectiveLength = LongEffectiveLength;
            else
                effectiveLength = ShortEffectiveLength;
            var speed = 1 / 2d * speedOfSound * speedOfSound * timeDiff / effectiveLength;
            return speed;
        }

    }
}
