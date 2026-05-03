using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Ultrasonics
{
    static class OnceOff
    {
        public static async Task GenerateDrivingComparison()
        {
            var gpsFn = "D:/Downloads/WS80 Original FW Test.gpx";
            var gps = new GPXData(gpsFn);
            var movingParts = await WebData.Get(gps.Start, gps.End);
            var ultrasonic = await WebData.Get(gps.Start, gps.End, null,
                "https://sierragliding.us/api/station/67/data");
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("GPS Time,GPS Speed,Spinning Cup Time,Spinning Cup Speed,Ultrasonic Time,Ultrasonic Speed");
            var max = new[] { gps.Fixes.Count, movingParts.Data.Count, ultrasonic.Data.Count }.Max();
            for (int i = 0; i < max; i++)
            {
                if (i < gps.Fixes.Count)
                {
                    var fix = gps.Fixes[i];
                    if (fix.TimeUtc != null)
                    {
                        var ts = new DateTimeOffset(fix.TimeUtc.Value, TimeSpan.Zero).ToUnixTimeSeconds();
                        var speed = gps.GetSpeedAtTime(fix.TimeUtc.Value);
                        sb.Append($"{ts},{speed:F1},");
                    }
                    else
                        sb.Append(",,");
                }
                else
                    sb.Append(",,");

                if (i < movingParts.Data.Count)
                {
                    var packet = movingParts.Data[i];
                    sb.Append($"{packet.timestamp},{packet.windspeed / 3.6:F1},");
                }
                else
                    sb.Append(",,");

                if (i < ultrasonic.Data.Count)
                {
                    var packet = ultrasonic.Data[i];
                    sb.Append($"{packet.timestamp},{packet.windspeed / 3.6:F1},");
                }
                else
                    sb.Append(",,");

                sb.AppendLine();
            }
            File.WriteAllText("D:/Documents/WS80Test.csv", sb.ToString());
        }

        public static async Task GenerateRooftopComparison()
        {
            var start = DateTimeOffset.FromUnixTimeSeconds(1770413856);
            var end = DateTimeOffset.FromUnixTimeSeconds(1771018656);
            var seconds = (end - start).TotalSeconds;
            var movingParts = await WebData.Get(start, end, statlen:60, sample:12);
            var ultrasonic = await WebData.Get(start, end, statlen: 60,
                "https://sierragliding.us/api/station/67/data", 12);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Time,Moving Parts,Ultrasonic");
            foreach (var packet in movingParts.Data)
            {
                var time = DateTimeOffset.FromUnixTimeSeconds(packet.timestamp);
                var mp = movingParts.GetAtTime(time);
                var us = ultrasonic.GetAtTime(time);
                sb.AppendLine($"{time.ToUnixTimeSeconds()},{mp?.windspeed:F1},{us?.windspeed:F1}");
            }
            File.WriteAllText("D:/Documents/WSRooftop80Test.csv", sb.ToString());
        }

        public static void GenerateArrays()
        {
            int DataSize = 245;
            double Hann(int i) => Math.Sin(Math.PI * (i + 1) / (DataSize + 1)) * Math.Sin(Math.PI * (i + 1) / (DataSize + 1));
            StringBuilder hannStringBuilder = new StringBuilder();
            int windowSum = 0;
            for (int i = 0; i < DataSize; i++)
            {
                var val = (int)Math.Round(Hann(i) * 4096);
                windowSum += val;
                hannStringBuilder.Append($"{val},");
            }
            DataSize = 49;
            string hannString = hannStringBuilder.ToString();
            double period = 24.5;
            StringBuilder cosStringBuilder = new StringBuilder();
            StringBuilder sinStringBuilder = new StringBuilder();
            for (int i = 0; i < DataSize; i++)
            {
                cosStringBuilder.Append($"{(int)Math.Round(2048 * Math.Cos(i * 2 * Math.PI / period))},");
                sinStringBuilder.Append($"{(int)Math.Round(2048 * Math.Sin(i * 2 * Math.PI / period))},");
            }
            string cosString = cosStringBuilder.ToString();
            string sinString = sinStringBuilder.ToString();
        }

        public static uint GetPowerAsStm(SerialPacket packet)
        {
            var data = SingleUltProcessor.GetSubTrace(packet.Trace);
            int DataSize = data.Length;
            int average = (int)data.Average(d => d);
            double period = 24.5;
            int Hann(int i) => (int)(4096 * Math.Sin(Math.PI * (i + 2) / (DataSize + 3)) * Math.Sin(Math.PI * (i + 2) / (DataSize + 3)));
            int Sin(int i) => (int)(2048 * Math.Sin(2 * Math.PI * i / period));
            int Cos(int i) => (int)(2048 * Math.Cos(2 * Math.PI * i / period));
            
            int sinSum = 0;
            int cosSum = 0;
            for (int i = 0; i < data.Length; i++)
            {
                var windowedVal = (data[i] - average) * Hann(i) >> 4;
                var sinVal = windowedVal * Sin(i) >> 8;
                var cosVal = windowedVal * Cos(i) >> 8;
                sinSum += sinVal;
                cosSum += cosVal;
            }
            const int window_sum = 507782; // 2^19
            int cos16 = cosSum / window_sum; // maximum of 2^12. More likely 2^8 or less.
            int sin16 = sinSum / window_sum;
            uint power = (uint)(cos16 * cos16 + sin16 * sin16); // Maximum of 2^24
            return power;
        }
    }
}
