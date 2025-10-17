namespace CocoroDock.Models
{
    public class ScheduledCommandSettings
    {
        public bool Enabled { get; set; }
        public string Command { get; set; } = string.Empty;
        public int IntervalMinutes { get; set; } = 60;
    }
}
