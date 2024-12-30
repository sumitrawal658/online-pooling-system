namespace PollSystem.Mobile.Services
{
    public interface IRealTimeService : IDisposable
    {
        bool IsConnected { get; }
        Task ConnectAsync();
        Task DisconnectAsync();
        Task JoinPollGroupAsync(Guid pollId);
        Task LeavePollGroupAsync(Guid pollId);
        event EventHandler<Poll> OnPollUpdated;
        event EventHandler<ConnectionStatus> OnConnectionStatusChanged;
    }
} 