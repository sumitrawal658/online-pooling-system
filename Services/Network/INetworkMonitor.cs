namespace PollSystem.Services.Network
{
    public interface INetworkMonitor
    {
        event EventHandler<NetworkStatusChangedEventArgs> NetworkStatusChanged;
        NetworkAccess CurrentStatus { get; }
        IConnectionProfile ConnectionProfile { get; }
        Task<bool> WaitForConnectionAsync(TimeSpan timeout);
        Task<NetworkMetrics> GetNetworkMetricsAsync();
        void TrackRequest(string endpoint, TimeSpan duration, bool isSuccess);
    }
} 