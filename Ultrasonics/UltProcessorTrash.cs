#if false
using Aelian.FFT;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ultrasonics
{
    internal class UltProcessorTrash
    {
        public List<DataStruct> ReadFile(string srcFn)
        {
            var datas = new List<DataStruct>();
            var rawData = File.ReadAllLines(srcFn);
            int errorCount = 0;
            foreach (var line in rawData.Skip(1))
            {
                try
                {
                    var elems = line.Split(',');
                    if (elems.Length < 1027)
                        continue;
                    var progId = elems[0];
                    var outChannel = int.Parse(elems[1]);
                    var inChannel = int.Parse(elems[2]);
                    var cycleCount = int.Parse(elems[3]);

                    var trace = elems[startIndex..(startIndex + dataSize)];

                    var complexBuffer = trace.Select(e => new System.Numerics.Complex(double.Parse(e), 0)).ToArray();
                    FastFourierTransform.FFT(complexBuffer, true, FftFlags.None);

                    double maxVal = 0;
                    int maxIndex = 0;
                    for (int i = 3; i < complexBuffer.Length / 2; i++)
                    {
                        if (complexBuffer[i].Magnitude > maxVal)
                        {
                            maxVal = complexBuffer[i].Magnitude;
                            maxIndex = i;
                        }
                    }

                    if (KeyIndex != null)
                        maxIndex = KeyIndex;

                    var phase = complexBuffer[maxIndex].Phase;

                    datas.Add(new DataStruct
                    {
                        inChannel = inChannel,
                        outChannel = outChannel,
                        cycleCount = cycleCount,
                        phase = phase,
                    });
                }
                catch
                {
                    errorCount++;
                }
            }
            Console.WriteLine($"Error Count: {errorCount}");
            return datas;
        }

        public List<List<DataStruct>> GroupData(List<DataStruct> datas)
        {
            List<List<DataStruct>> groupedData = new List<List<DataStruct>>();
            for (int i = 0; i < datas.Count; i++)
            {
                var cur = datas[i];
                var curCycles = cur.cycleCount;
                List<DataStruct> curGroup = new List<DataStruct>();
                for (; i < datas.Count && datas[i].cycleCount == curCycles; i++)
                    curGroup.Add(datas[i]);
                groupedData.Add(curGroup);
                i--;
            }
            return groupedData;
        }

        public List<Dictionary<PhaseDiffKey, double>> GetPhaseDifferences(List<List<DataStruct>> groupedData)
        {
            List<Dictionary<PhaseDiffKey, double>> phaseDifferences = new List<Dictionary<PhaseDiffKey, double>>();

            foreach (var grp in groupedData)
            {
                var cyc = grp[0].cycleCount;
                Dictionary<PhaseDiffKey, double> curDifferences = new Dictionary<PhaseDiffKey, double>();
                for (int ch1 = 0; ch1 < 4; ch1++)
                    for (int ch2 = ch1 + 1; ch2 < 4; ch2++)
                    {
                        var dir1 = grp.FirstOrDefault(d => d.inChannel == ch1 && d.outChannel == ch2);
                        var dir2 = grp.FirstOrDefault(d => d.outChannel == ch1 && d.inChannel == ch2);
                        if (dir1.Equals(default(DataStruct)) || dir2.Equals(default(DataStruct)))
                        {
                            continue;
                        }
                        var phaseDiff = Normalise(dir1.phase - dir2.phase);
                        curDifferences.Add(new PhaseDiffKey(cyc, ch1, ch2), phaseDiff);

                    }
                if (curDifferences.Any())
                    phaseDifferences.Add(curDifferences);
            }
            return phaseDifferences;
        }

        public Dictionary<PhaseDiffKey, double> GetAveragePhaseDiffs(List<Dictionary<PhaseDiffKey, double>> phaseDifferences)
        {
            Dictionary<PhaseDiffKey, double> averagePhaseDiffs = new Dictionary<PhaseDiffKey, double>();

            foreach (var key in phaseDifferences.SelectMany(d => d.Keys).Distinct())
                averagePhaseDiffs[key] = phaseDifferences.Where(d => d.ContainsKey(key))
                    .Average(d => d[key]);

            return averagePhaseDiffs;
        }

        Dictionary<PhaseDiffKey, double> GetStandardDeviations(List<Dictionary<PhaseDiffKey, double>> phaseDifferences)
        {
            Dictionary<PhaseDiffKey, double> stdDevs = new Dictionary<PhaseDiffKey, double>();
            foreach (var key in phaseDifferences.SelectMany(d => d.Keys).Distinct())
                stdDevs[key] = SampleStdDev(phaseDifferences.Where(d => d.ContainsKey(key))
                    .Select(d => d[key]));
            return stdDevs;
        }

        double SampleStdDev(IEnumerable<double> inVals)
        {
            var avg = inVals.Average(x => x);
            var sum = inVals.Sum(x => (x - avg) * (x - avg));
            return Math.Sqrt(sum / (inVals.Count() - 1));
        }


    }
}
#endif
