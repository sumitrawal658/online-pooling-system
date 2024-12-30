namespace PollSystem.Services.Implementation
{
    public class PollCommandService : IPollCommandService
    {
        private readonly PollDbContext _context;
        private readonly ILogger<PollCommandService> _logger;
        private readonly IHubContext<PollHub> _hubContext;
        private readonly IUserService _userService;
        private readonly IMemoryCache _cache;

        public PollCommandService(
            PollDbContext context,
            ILogger<PollCommandService> logger,
            IHubContext<PollHub> hubContext,
            IUserService userService,
            IMemoryCache cache)
        {
            _context = context;
            _logger = logger;
            _hubContext = hubContext;
            _userService = userService;
            _cache = cache;
        }

        public async Task<bool> VoteAsync(Guid pollId, Guid optionId, string voterId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var user = await _userService.GetOrCreateUserAsync(voterId);
                if (await HasVotedAsync(pollId, user.UserId))
                {
                    return false;
                }

                var vote = new Vote
                {
                    VoteId = Guid.NewGuid(),
                    PollId = pollId,
                    OptionId = optionId,
                    UserId = user.UserId,
                    CreatedAt = DateTime.UtcNow
                };

                _context.Votes.Add(vote);
                await UpdateVoteCountAsync(optionId);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                // Invalidate cache
                _cache.Remove($"poll_{pollId}");

                // Notify clients
                await NotifyVoteUpdateAsync(pollId);

                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error processing vote");
                throw;
            }
        }

        private async Task NotifyVoteUpdateAsync(Guid pollId)
        {
            var updatedPoll = await _context.Polls
                .Include(p => p.Options)
                .FirstOrDefaultAsync(p => p.PollId == pollId);

            if (updatedPoll != null)
            {
                await _hubContext.Clients.Group(pollId.ToString())
                    .SendAsync("PollUpdated", updatedPoll);
            }
        }

        // ... other command methods
    }
} 