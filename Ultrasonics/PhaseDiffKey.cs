using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ultrasonics
{
    struct PhaseDiffKey
    {
        public int CycleCount, Ch1, Ch2;
        public override string ToString()
        {
            return $"{CycleCount}, {Ch1}, {Ch2}";
        }
        public PhaseDiffKey(int cycleCount, int ch1, int ch2)
        {
            this.CycleCount = cycleCount;
            this.Ch1 = ch1;
            this.Ch2 = ch2;
        }
    }
}
