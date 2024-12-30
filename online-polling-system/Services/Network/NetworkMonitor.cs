using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PollSystem.Services.Network
{
    public class NetworkMonitor : INetworkMonitor, IDisposable
    {
        private readonly ILogger<NetworkMonitor> _logger;
        private readonly IConnectivity _connectivity;
        private readonly ConcurrentDictionary<string, NetworkRequestMetrics> _metrics;
        private readonly SemaphoreSlim _syncLock;
        private bool _isDisposed;

        public event EventHandler<NetworkStatusChangedEventArgs> NetworkStatusChanged;
        public NetworkAccess CurrentStatus => _connectivity.NetworkAccess;
        public IConnectionProfile ConnectionProfile => _connectivity.ConnectionProfiles.FirstOrDefault();

        public NetworkMonitor(ILogger<NetworkMonitor> logger, IConnectivity connectivity)
        {
            _logger = logger;
            _connectivity = connectivity;
            _metrics = new ConcurrentDictionary<string, NetworkRequestMetrics>();
            _syncLock = new SemaphoreSlim(1, 1);

            _connectivity.ConnectivityChanged += OnConnectivityChanged;
        }

        private void OnConnectivityChanged(object sender, ConnectivityChangedEventArgs e)
        {
            var args = new NetworkStatusChangedEventArgs(
                e.NetworkAccess,
                GetConnectionType(),
                e.NetworkAccess == NetworkAccess.Internet);

            _logger.LogInformation(
                "Network status changed: {Status}, Connection: {Connection}, IsConnected: {IsConnected}",
                args.Status,
                args.ConnectionType,
                args.IsConnected);

            NetworkStatusChanged?.Invoke(this, args);
        }

        public async Task<bool> WaitForConnectionAsync(TimeSpan timeout)
        {
            if (CurrentStatus == NetworkAccess.Internet)
                return true;

            try
            {
                var tcs = new TaskCompletionSource<bool>();
                using var cts = new CancellationTokenSource(timeout);
                
                void Handler(object s, NetworkStatusChangedEventArgs e)
                {
                    if (e.IsConnected)
                        tcs.TrySetResult(true);
                }

                NetworkStatusChanged += Handler;
                cts.Token.Register(() => tcs.TrySetResult(false));

                try
                {
                    return await tcs.Task;
                }
                finally
                {
                    NetworkStatusChanged -= Handler;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error waiting for network connection");
                return false;
            }
        }

        public async Task<NetworkMetrics> GetNetworkMetricsAsync()
        {
            await _syncLock.WaitAsync();
            try
            {
                var metrics = new NetworkMetrics
                {
                    CurrentStatus = CurrentStatus,
                    ConnectionType = GetConnectionType(),
                    Requests = _metrics.Values.ToList(),
                    LastUpdated = DateTime.UtcNow
                };

                // Clean up old metrics
                var oldMetrics = _metrics.Where(m => 
                    DateTime.UtcNow - m.Value.Timestamp > TimeSpan.FromHours(1))
                    .Select(m => m.Key)
                    .ToList();

                foreach (var key in oldMetrics)
                {
                    _metrics.TryRemove(key, out _);
                }

                return metrics;
            }
            finally
            {
                _syncLock.Release();
            }
        }

        public void TrackRequest(string endpoint, TimeSpan duration, bool isSuccess)
        {
            var metrics = _metrics.GetOrAdd(endpoint, _ => new NetworkRequestMetrics(endpoint));
            metrics.AddRequest(duration, isSuccess);

            _logger.LogDebug(
                "Network request tracked: {Endpoint}, Duration: {Duration}ms, Success: {Success}",
                endpoint,
                duration.TotalMilliseconds,
                isSuccess);
        }

        private string GetConnectionType()
        {
            return _connectivity.ConnectionProfiles.FirstOrDefault()?.ToString() ?? "Unknown";
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _connectivity.ConnectivityChanged -= OnConnectivityChanged;
            _syncLock?.Dispose();
            _metrics.Clear();
            _isDisposed = true;
        }
    }

    public class NetworkStatusChangedEventArgs : EventArgs
    {
        public NetworkAccess Status { get; }
        public string ConnectionType { get; }
        public bool IsConnected { get; }

        public NetworkStatusChangedEventArgs(NetworkAccess status, string connectionType, bool isConnected)
        {
            Status = status;
            ConnectionType = connectionType;
            IsConnected = isConnected;
        }
    }

    public class NetworkMetrics
    {
        public NetworkAccess CurrentStatus { get; set; }
        public string ConnectionType { get; set; }
        public List<NetworkRequestMetrics> Requests { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    public class NetworkRequestMetrics
    {
        public string Endpoint { get; }
        public int TotalRequests { get; private set; }
        public int SuccessfulRequests { get; private set; }
        public double AverageResponseTime { get; private set; }
        public DateTime Timestamp { get; private set; }

        private readonly Queue<double> _responseTimes;
        private const int MaxSamples = 100;

        public NetworkRequestMetrics(string endpoint)
        {
            Endpoint = endpoint;
            _responseTimes = new Queue<double>();
            Timestamp = DateTime.UtcNow;
        }

        public void AddRequest(TimeSpan duration, bool isSuccess)
        {
            TotalRequests++;
            if (isSuccess)
                SuccessfulRequests++;

            _responseTimes.Enqueue(duration.TotalMilliseconds);
            if (_responseTimes.Count > MaxSamples)
                _responseTimes.Dequeue();

            AverageResponseTime = _responseTimes.Average();
            Timestamp = DateTime.UtcNow;
        }
    }
} 