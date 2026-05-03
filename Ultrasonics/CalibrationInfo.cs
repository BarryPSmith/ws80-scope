namespace Ultrasonics
{
    public class CalibrationInfo
    {
        public DateTimeOffset Start { get; set; }
        public DateTimeOffset End { get; set; }
        public override string ToString()
        {
            return $"{Start:G} - {End:G}";
        }
    }
}
