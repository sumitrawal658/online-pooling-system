namespace PollSystem.Services.Network
{
    public class RetryPolicyConfiguration
    {
        public int MaxRetries { get; set; } = 3;
        public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);
        public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
        public double JitterFactor { get; set; } = 0.2;
        public bool UseExponentialBackoff { get; set; } = true;
    }
} 