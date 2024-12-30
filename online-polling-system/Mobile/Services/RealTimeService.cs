using Microsoft.AspNetCore.SignalR.Client;
using System.Diagnostics;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;

namespace PollSystem.Mobile.Services
{
    public class RealTimeService : BaseDisposableService, IRealTimeService
    {
        private readonly HubConnection _hubConnection;
        private readonly IConnectivity _connectivity;
        private readonly ConcurrentDictionary<string, DateTime> _groupSubscriptions;
        private readonly CancellationTokenSource _reconnectCts;
        private IDisposable _connectivitySubscription;

        public event EventHandler<Poll> OnPollUpdated;
        public event EventHandler<ConnectionStatus> OnConnectionStatusChanged;

        public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

        public RealTimeService(
            string hubUrl, 
            ILogger<RealTimeService> logger,
            IConnectivity connectivity) : base(logger)
        {
            _hubConnection = new HubConnectionBuilder()
                .WithUrl(hubUrl)
                .WithAutomaticReconnect()
                .Build();
            _connectivity = connectivity;
            _groupSubscriptions = new ConcurrentDictionary<string, DateTime>();
            _reconnectCts = new CancellationTokenSource();

            ConfigureHubEvents();
            ConfigureConnectivityEvents();
        }

        private void ConfigureHubEvents()
        {
            _hubConnection.Reconnecting += OnReconnecting;
            _hubConnection.Reconnected += OnReconnected;
            _hubConnection.Closed += OnConnectionClosed;

            _hubConnection.On<Poll>("PollUpdated", (poll) =>
            {
                OnPollUpdated?.Invoke(this, poll);
            });
        }

        private void ConfigureConnectivityEvents()
        {
            _connectivity.ConnectivityChanged += async (s, e) =>
            {
                if (e.NetworkAccess == NetworkAccess.Internet)
                {
                    await ReconnectAsync();
                }
            };
        }

        public async Task ConnectAsync()
        {
            if (_hubConnection.State == HubConnectionState.Connected)
                return;

            try
            {
                await _hubConnection.StartAsync();
                _logger.LogInformation("Connected to SignalR hub");
                OnConnectionStatusChanged?.Invoke(this, new ConnectionStatus(true, null));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error connecting to SignalR hub");
                OnConnectionStatusChanged?.Invoke(this, new ConnectionStatus(false, ex.Message));
                throw;
            }
        }

        public async Task DisconnectAsync()
        {
            if (_hubConnection.State == HubConnectionState.Disconnected)
                return;

            try
            {
                await _hubConnection.StopAsync();
                _logger.LogInformation("Disconnected from SignalR hub");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disconnecting from SignalR hub");
                throw;
            }
        }

        public async Task JoinPollGroupAsync(Guid pollId)
        {
            var groupName = pollId.ToString();
            if (_groupSubscriptions.TryAdd(groupName, DateTime.UtcNow))
            {
                try
                {
                    await _hubConnection.InvokeAsync("JoinPollGroup", groupName);
                    _logger.LogInformation($"Joined poll group: {groupName}");
                }
                catch (Exception ex)
                {
                    _groupSubscriptions.TryRemove(groupName, out _);
                    _logger.LogError(ex, $"Error joining poll group: {groupName}");
                    throw;
                }
            }
        }

        public async Task LeavePollGroupAsync(Guid pollId)
        {
            var groupName = pollId.ToString();
            if (!_activeGroups.Contains(groupName))
                return;

            try
            {
                await _hubConnection.InvokeAsync("LeavePollGroup", groupName);
                _activeGroups.Remove(groupName);
                _logger.LogInformation($"Left poll group: {groupName}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error leaving poll group: {groupName}");
                throw;
            }
        }

        private async Task OnReconnecting(Exception exception)
        {
            _logger.LogWarning(exception, "Attempting to reconnect to SignalR hub");
            OnConnectionStatusChanged?.Invoke(this, new ConnectionStatus(false, "Reconnecting..."));
        }

        private async Task OnReconnected(string connectionId)
        {
            _logger.LogInformation($"Reconnected to SignalR hub. Connection ID: {connectionId}");
            
            // Rejoin all active groups
            foreach (var groupName in _activeGroups.ToList())
            {
                try
                {
                    await _hubConnection.InvokeAsync("JoinPollGroup", groupName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error rejoining group {groupName} after reconnection");
                }
            }

            OnConnectionStatusChanged?.Invoke(this, new ConnectionStatus(true, null));
        }

        private async Task OnConnectionClosed(Exception exception)
        {
            _logger.LogError(exception, "SignalR connection closed");
            OnConnectionStatusChanged?.Invoke(this, new ConnectionStatus(false, "Connection closed"));

            if (!_isDisposed)
            {
                await ReconnectAsync();
            }
        }

        private async Task ReconnectAsync()
        {
            try
            {
                _reconnectCts.Cancel();
                _reconnectCts = new CancellationTokenSource();

                while (!IsConnected && !_isDisposed)
                {
                    try
                    {
                        await ConnectAsync();
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Reconnection attempt failed");
                        await Task.Delay(TimeSpan.FromSeconds(5), _reconnectCts.Token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Reconnection attempts cancelled");
            }
        }

        private async Task RejoinGroupsAsync()
        {
            var staleGroups = _groupSubscriptions
                .Where(kvp => DateTime.UtcNow - kvp.Value > TimeSpan.FromMinutes(30))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var group in staleGroups)
            {
                _groupSubscriptions.TryRemove(group, out _);
            }

            foreach (var group in _groupSubscriptions.Keys)
            {
                try
                {
                    await _hubConnection.InvokeAsync("JoinPollGroup", group);
                    _groupSubscriptions[group] = DateTime.UtcNow;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error rejoining group {group}");
                }
            }
        }

        protected override void DisposeManagedResources()
        {
            _reconnectCts?.Cancel();
            _connectivitySubscription?.Dispose();
            _groupSubscriptions.Clear();
            _hubConnection?.DisposeAsync().AsTask().Wait();
        }

        protected override async ValueTask DisposeAsyncCore()
        {
            if (_hubConnection != null)
            {
                try
                {
                    await DisconnectAsync();
                    await _hubConnection.DisposeAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disposing hub connection");
                }
            }
        }
    }

    public class CustomRetryPolicy : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext retryContext)
        {
            // Implement exponential backoff with maximum delay
            var maxDelay = TimeSpan.FromMinutes(2);
            var delay = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, retryContext.PreviousRetryCount), maxDelay.TotalSeconds));
            return delay;
        }
    }

    public class ConnectionStatus
    {
        public bool IsConnected { get; }
        public string Message { get; }

        public ConnectionStatus(bool isConnected, string message)
        {
            IsConnected = isConnected;
            Message = message;
        }
    }
} 