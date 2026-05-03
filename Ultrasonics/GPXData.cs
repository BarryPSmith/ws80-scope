using Geo.Geodesy;
using Geo.Gps;
using Geo.Gps.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ultrasonics
{
    class GPXData
    {
        List<uint> _times;
        List<Waypoint> _fixes;
        public List<Waypoint> Fixes => _fixes;

        public DateTimeOffset Start => DateTimeOffset.FromUnixTimeSeconds(_times.First());
        public DateTimeOffset End => DateTimeOffset.FromUnixTimeSeconds(_times.Last());

        public GPXData(string fn)
        {
            var serialiser = new Gpx11Serializer();
            using var stream = new FileStream(fn, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            StreamWrapper sw = new StreamWrapper(stream);
            var gpsData = serialiser.DeSerialize(sw);
            var track = gpsData.Tracks.First();
            _fixes = track.GetAllFixes()
                .Where(f => f.TimeUtc != null)
                .ToList();
            _times = _fixes.Select(f => (uint) new DateTimeOffset(f.TimeUtc.Value, TimeSpan.Zero).ToUnixTimeSeconds())
                .ToList();
        }

        public double? GetSpeedAtTime(DateTimeOffset time)
        {
            var idx = _times.BinarySearch((uint)time.ToUnixTimeSeconds());
            int idx1, idx2;
            if (idx < 0)
            {
                if (~idx == 0 || ~idx == _times.Count)
                    return null;
                idx1 = ~idx - 1;
                idx2 = ~idx;
            }
            else
            {
                if (idx < _times.Count - 1)
                {
                    idx1 = idx;
                    idx2 = idx + 1;
                }
                else
                {
                    idx1 = idx - 1;
                    idx2 = idx;
                }
            }
            var pt1 = _fixes[idx1];
            var pt2 = _fixes[idx2];
            var dist = GeodeticCalculations.CalculateShortestLine(pt1.Coordinate, pt2.Coordinate)
                .Distance.SiValue;
            var timeDiff = _times[idx2] - _times[idx1];
            var speed = dist / timeDiff;
            return speed;
        }
    }
}
