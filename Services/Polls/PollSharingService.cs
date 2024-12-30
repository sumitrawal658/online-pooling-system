using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using OnlinePollingSystem.Models;

namespace OnlinePollingSystem.Services.Polls
{
    public interface IPollSharingService
    {
        Task<string> GenerateSharableLinkAsync(int pollId, string userId, string shareMethod);
        Task<bool> ValidateSharableLinkAsync(string shareCode);
        Task<PollSharingStats> GetSharingStatsAsync(int pollId);
        Task<bool> TrackPollShareAsync(int pollId, string userId, string shareMethod);
        Task<bool> TrackShareAccessAsync(string shareCode);
        Task<List<PollSharing>> GetRecentSharesAsync(int pollId, int count = 10);
        Task<Dictionary<string, int>> GetShareMethodStatsAsync(int pollId);
    }

    public class PollSharingService : IPollSharingService
    {
        private readonly IConfiguration _configuration;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<PollSharingService> _logger;
        private readonly IUserService _userService;

        public PollSharingService(
            IConfiguration configuration,
            ApplicationDbContext context,
            ILogger<PollSharingService> logger,
            IUserService userService)
        {
            _configuration = configuration;
            _context = context;
            _logger = logger;
            _userService = userService;
        }

        public async Task<string> GenerateSharableLinkAsync(int pollId, string userId, string shareMethod)
        {
            try
            {
                var poll = await _context.Polls
                    .Include(p => p.CreatedByUser)
                    .FirstOrDefaultAsync(p => p.Id == pollId);

                if (poll == null)
                    throw new NotFoundException("Poll not found");

                // Validate share method
                if (!IsValidShareMethod(shareMethod))
                    throw new ArgumentException("Invalid share method");

                // Generate a unique share code
                var shareCode = await GenerateUniqueShareCodeAsync();
                
                // Create sharing record
                var sharing = new PollSharing
                {
                    PollId = pollId,
                    ShareCode = shareCode,
                    ShareMethod = shareMethod,
                    CreatedAt = DateTime.UtcNow,
                    SharedByUserId = userId,
                    AccessCount = 0
                };
                
                await _context.PollSharings.AddAsync(sharing);
                await _context.SaveChangesAsync();

                // Generate shareable URL
                var baseUrl = _configuration["Application:BaseUrl"];
                return $"{baseUrl}/polls/shared/{shareCode}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating sharable link for poll {PollId}", pollId);
                throw;
            }
        }

        public async Task<bool> ValidateSharableLinkAsync(string shareCode)
        {
            try
            {
                var sharing = await _context.PollSharings
                    .Include(ps => ps.Poll)
                    .FirstOrDefaultAsync(ps => ps.ShareCode == shareCode);

                if (sharing == null)
                    return false;

                // Check if poll is still active
                if (sharing.Poll.IsExpired)
                    return false;

                // Track access
                await TrackShareAccessAsync(shareCode);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating share code {ShareCode}", shareCode);
                throw;
            }
        }

        public async Task<PollSharingStats> GetSharingStatsAsync(int pollId)
        {
            try
            {
                var stats = await _context.PollSharings
                    .Where(ps => ps.PollId == pollId)
                    .GroupBy(ps => ps.ShareMethod)
                    .Select(g => new
                    {
                        Method = g.Key,
                        Count = g.Count(),
                        TotalAccess = g.Sum(ps => ps.AccessCount),
                        LastShared = g.Max(ps => ps.CreatedAt),
                        LastAccessed = g.Max(ps => ps.LastAccessedAt)
                    })
                    .ToListAsync();

                return new PollSharingStats
                {
                    TotalShares = stats.Sum(s => s.Count),
                    TotalAccess = stats.Sum(s => s.TotalAccess),
                    SharesByMethod = stats.ToDictionary(
                        s => s.Method,
                        s => new ShareMethodStats
                        {
                            ShareCount = s.Count,
                            AccessCount = s.TotalAccess,
                            LastShared = s.LastShared,
                            LastAccessed = s.LastAccessed
                        }
                    )
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting sharing stats for poll {PollId}", pollId);
                throw;
            }
        }

        public async Task<bool> TrackPollShareAsync(int pollId, string userId, string shareMethod)
        {
            try
            {
                var sharing = new PollSharing
                {
                    PollId = pollId,
                    SharedByUserId = userId,
                    ShareMethod = shareMethod,
                    CreatedAt = DateTime.UtcNow,
                    AccessCount = 0
                };

                await _context.PollSharings.AddAsync(sharing);
                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error tracking poll share for poll {PollId}", pollId);
                return false;
            }
        }

        public async Task<bool> TrackShareAccessAsync(string shareCode)
        {
            try
            {
                var sharing = await _context.PollSharings
                    .FirstOrDefaultAsync(ps => ps.ShareCode == shareCode);

                if (sharing == null)
                    return false;

                sharing.AccessCount++;
                sharing.LastAccessedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error tracking share access for code {ShareCode}", shareCode);
                return false;
            }
        }

        public async Task<List<PollSharing>> GetRecentSharesAsync(int pollId, int count = 10)
        {
            try
            {
                return await _context.PollSharings
                    .Include(ps => ps.SharedByUser)
                    .Where(ps => ps.PollId == pollId)
                    .OrderByDescending(ps => ps.CreatedAt)
                    .Take(count)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting recent shares for poll {PollId}", pollId);
                throw;
            }
        }

        public async Task<Dictionary<string, int>> GetShareMethodStatsAsync(int pollId)
        {
            try
            {
                return await _context.PollSharings
                    .Where(ps => ps.PollId == pollId)
                    .GroupBy(ps => ps.ShareMethod)
                    .ToDictionaryAsync(
                        g => g.Key,
                        g => g.Count()
                    );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting share method stats for poll {PollId}", pollId);
                throw;
            }
        }

        private async Task<string> GenerateUniqueShareCodeAsync()
        {
            while (true)
            {
                // Generate a random code
                var code = Convert.ToBase64String(Guid.NewGuid().ToByteArray())
                    .Replace("/", "_")
                    .Replace("+", "-")
                    .Substring(0, 8);

                // Check if code is unique
                if (!await _context.PollSharings.AnyAsync(ps => ps.ShareCode == code))
                    return code;
            }
        }

        private bool IsValidShareMethod(string shareMethod)
        {
            return shareMethod switch
            {
                ShareMethods.Direct => true,
                ShareMethods.Email => true,
                ShareMethods.Facebook => true,
                ShareMethods.Twitter => true,
                ShareMethods.WhatsApp => true,
                ShareMethods.Telegram => true,
                _ => false
            };
        }
    }

    public class PollSharingStats
    {
        public int TotalShares { get; set; }
        public int TotalAccess { get; set; }
        public Dictionary<string, ShareMethodStats> SharesByMethod { get; set; }
    }

    public class ShareMethodStats
    {
        public int ShareCount { get; set; }
        public int AccessCount { get; set; }
        public DateTime LastShared { get; set; }
        public DateTime? LastAccessed { get; set; }
    }
} 