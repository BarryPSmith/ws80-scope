namespace Ultrasonics
{
    class WebData
    {
        List<uint> _timestamps;
        List<SierraGlidingPacket> _data;
        public List<SierraGlidingPacket> Data => _data;

        public DateTimeOffset Start { get; private set; }
        public DateTimeOffset End { get; private set; }
        public WebData(List<SierraGlidingPacket> data) 
        {
            _data = data;
            _timestamps = data.Select(d => d.timestamp).ToList();
        }
        public static async Task<WebData> Get(DateTimeOffset start, DateTimeOffset end,
            double? statlen = null,
            string url = "https://sierragliding.us/api/station/72/data",
            int sample = 1)
        {
            return new WebData(await WebSource.GetRangeAsync(start, end, statlen, url, sample))
            {
                Start = start,
                End = end
            };
        }

        public SierraGlidingPacket? GetAtTime(DateTimeOffset time)
        {
            // Technically, we should probably return the next one.
            // But our SG stations tend to have a 1 second lag in them.
            // So...
            var ts = (uint) time.ToUnixTimeSeconds();
            var bs = _timestamps.BinarySearch(ts);
            if (bs >= 0)
                return _data[bs];
            var idxPlus1 = ~bs;
            if (idxPlus1 == _timestamps.Count())
                return null;
            var ret = _data[idxPlus1];
            if (ret.timestamp - ts > 5)
                return null;
            return ret;
        }
    }
}
