using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ultrasonics
{
    internal class EdgeUltProcessor : SingleUltProcessor
    {
        const double ProcessorFrequency = 32E6;
        const double SignalFrequency = 40E3;
        const double ProcCyclesPerSignalCycle = ProcessorFrequency / SignalFrequency; // 800
        const int MinEdgeCapture = 6000;
        const int CaptureCount = 7;
#if false
        public override void ProcessSerialPacket(SerialPacket inPacket)
        {
            var relevantCaptures = inPacket.EdgeCaptures.Where(v => v > MinEdgeCapture)
                .Take(CaptureCount)
                .ToList(); ;

            if (!relevantCaptures.Any())
                return;

            // Super naive: Ignore variable frequency.
            var averageCycle = relevantCaptures.Average(c => c);
            var cycleOffset = averageCycle % ProcCyclesPerSignalCycle;
            var phase = cycleOffset / ProcCyclesPerSignalCycle * 2 * Math.PI;
            var temp = inPacket.Temperature_x10 / 10d;

            var packet = new ProcessedPacket
            {
                Source = inPacket,
                Phase = phase,
                Temperature = temp
            };

            var key = (inPacket.OutputChannel, inPacket.InputChannel);
            _recentPackets[key] = packet;
            NotifyIfFrameAvailable();
        }
#endif
        public override (double[], double)? GetTimeDifference(SerialPacket packet1, SerialPacket packet2)
        {
            int maxCaptures = Math.Min(packet1.EdgeCaptures.Count(v => v > MinEdgeCapture && v != 0),
                packet2.EdgeCaptures.Count(v => v > MinEdgeCapture && v != 0));
            if (maxCaptures < 3)
                return null;
            if (maxCaptures > CaptureCount)
                maxCaptures = CaptureCount;
            var avg1 = packet1.EdgeCaptures
                .Where(v => v > MinEdgeCapture && v != 0)
                .Take(maxCaptures)
                .Average(v => v);
            var avg2 = packet2.EdgeCaptures
                .Where(v => v > MinEdgeCapture && v != 0)
                .Take(maxCaptures)
                .Average(v => v);
            var avgDiff = avg1 - avg2;
            while (avgDiff > ProcCyclesPerSignalCycle / 2)
                avgDiff -= ProcCyclesPerSignalCycle;
            while (avgDiff < -ProcCyclesPerSignalCycle / 2)
                avgDiff += ProcCyclesPerSignalCycle;
            var timeDiff = avgDiff / ProcessorFrequency;
            return (new[] { timeDiff }, 0);
        }
    }
}
