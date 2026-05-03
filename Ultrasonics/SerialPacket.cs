using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Ultrasonics
{
    public class SerialPacket
    {
        public byte OutputChannel { get; set; }
        public byte InputChannel { get; set; }
        public byte ExcitationCount { get; set; }
        public required short[] Trace { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public short Temperature_x10 { get; internal set; }
        public required short[] EdgeCaptures { get; set; }
        public Complex KeyVal { get; internal set; }
    }
}
