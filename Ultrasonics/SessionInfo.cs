namespace Ultrasonics
{
    public class SessionInfo
    {
        public string? Name { get; set; }
        public DateTimeOffset Start { get; set; }
        public DateTimeOffset End { get; set; }
        public override string ToString()
        {
            return $"{Name} ({Start:G} - {End:G})";
        }
    }
}
