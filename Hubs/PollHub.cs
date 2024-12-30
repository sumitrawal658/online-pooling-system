using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace PollSystem.Hubs
{
    public class PollHub : Hub
    {
        private readonly GroupManager _groupManager;
        private readonly ILogger<PollHub> _logger;

        public PollHub(GroupManager groupManager, ILogger<PollHub> logger)
        {
            _groupManager = groupManager;
            _logger = logger;
        }

        public override async Task OnConnectedAsync()
        {
            _logger.LogInformation($"Client connected: {Context.ConnectionId}");
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            _groupManager.ClearConnection(Context.ConnectionId);
            _logger.LogInformation($"Client disconnected: {Context.ConnectionId}");
            await base.OnDisconnectedAsync(exception);
        }

        public async Task JoinPollGroup(string pollId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, pollId);
            _groupManager.AddSubscription(pollId, Context.ConnectionId);
            _logger.LogInformation($"Client {Context.ConnectionId} joined group {pollId}");
        }

        public async Task LeavePollGroup(string pollId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, pollId);
            _groupManager.RemoveSubscription(pollId, Context.ConnectionId);
            _logger.LogInformation($"Client {Context.ConnectionId} left group {pollId}");
        }

        public async Task NotifyVote(string pollId)
        {
            var connections = _groupManager.GetConnectionIds(pollId);
            if (!connections.Any())
            {
                return;
            }

            await Clients.Groups(pollId).SendAsync("VoteReceived", pollId);
        }
    }
} 