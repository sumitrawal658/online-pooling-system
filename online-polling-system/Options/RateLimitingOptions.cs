namespace PollSystem.Options
{
    public class RateLimitingOptions
    {
        public int PerSecond { get; set; } = 10;
        public int PerMinute { get; set; } = 60;
        public int PerHour { get; set; } = 600;
        public Dictionary<string, RateLimitRule> EndpointRules { get; set; }
    }

    public class RateLimitRule
    {
        public int Limit { get; set; }
        public string Period { get; set; }
    }
} 