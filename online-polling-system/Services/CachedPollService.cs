using Microsoft.Extensions.Caching.Memory;
using PollSystem.Models;

namespace PollSystem.Services
{
    public class CachedPollService : IPollService
    {
        private readonly PollService _pollService;
        private readonly IMemoryCache _cache;
        private readonly ILogger<CachedPollService> _logger;

        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
        private const string PollCacheKeyPrefix = "Poll_";
        private const string ActivePollsCacheKey = "ActivePolls";

        public CachedPollService(
            PollService pollService,
            IMemoryCache cache,
            ILogger<CachedPollService> logger)
        {
            _pollService = pollService;
            _cache = cache;
            _logger = logger;
        }

        public async Task<Poll> CreatePollAsync(CreatePollRequest request, string creatorIp)
        {
            var poll = await _pollService.CreatePollAsync(request, creatorIp);
            await InvalidateActivePollsCache();
            return poll;
        }

        public async Task<Poll> GetPollAsync(Guid pollId)
        {
            var cacheKey = $"{PollCacheKeyPrefix}{pollId}";
            return await _cache.GetOrCreateAsync(cacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheDuration;
                return await _pollService.GetPollAsync(pollId);
            });
        }

        public async Task<List<Poll>> GetActivePollsAsync()
        {
            return await _cache.GetOrCreateAsync(ActivePollsCacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheDuration;
                return await _pollService.GetActivePollsAsync();
            });
        }

        public async Task<bool> VoteAsync(Guid pollId, Guid optionId, string voterIp)
        {
            var result = await _pollService.VoteAsync(pollId, optionId, voterIp);
            if (result)
            {
                await InvalidatePollCache(pollId);
                await InvalidateActivePollsCache();
            }
            return result;
        }

        public async Task<bool> DeletePollAsync(Guid pollId, string adminIp)
        {
            var result = await _pollService.DeletePollAsync(pollId, adminIp);
            if (result)
            {
                await InvalidatePollCache(pollId);
                await InvalidateActivePollsCache();
            }
            return result;
        }

        private Task InvalidatePollCache(Guid pollId)
        {
            var cacheKey = $"{PollCacheKeyPrefix}{pollId}";
            _cache.Remove(cacheKey);
            return Task.CompletedTask;
        }

        private Task InvalidateActivePollsCache()
        {
            _cache.Remove(ActivePollsCacheKey);
            return Task.CompletedTask;
        }
    }
} 