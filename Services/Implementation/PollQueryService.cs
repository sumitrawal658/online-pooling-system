namespace PollSystem.Services.Implementation
{
    public class PollQueryService : IPollQueryService
    {
        private readonly PollDbContext _context;
        private readonly ILogger<PollQueryService> _logger;
        private readonly IMemoryCache _cache;

        public PollQueryService(
            PollDbContext context,
            ILogger<PollQueryService> logger,
            IMemoryCache cache)
        {
            _context = context;
            _logger = logger;
            _cache = cache;
        }

        public async Task<Poll> GetPollAsync(Guid pollId)
        {
            var cacheKey = $"poll_{pollId}";
            return await _cache.GetOrCreateAsync(cacheKey, async entry =>
            {
                entry.SlidingExpiration = TimeSpan.FromMinutes(5);
                return await _context.Polls
                    .Include(p => p.Options)
                    .ThenInclude(o => o.Votes)
                    .FirstOrDefaultAsync(p => p.PollId == pollId);
            });
        }

        public async Task<List<Poll>> GetActivePollsAsync()
        {
            return await _context.Polls
                .Include(p => p.Options)
                .Where(p => p.IsActive && 
                    (p.EndDate == null || p.EndDate > DateTime.UtcNow))
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();
        }

        // ... other query methods
    }
} 