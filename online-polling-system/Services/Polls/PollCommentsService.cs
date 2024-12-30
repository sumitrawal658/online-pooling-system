using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OnlinePollingSystem.Services.Polls
{
    public interface IPollCommentsService
    {
        Task<PollComment> AddCommentAsync(int pollId, string userId, string content);
        Task<PollComment> UpdateCommentAsync(int commentId, string userId, string content);
        Task DeleteCommentAsync(int commentId, string userId);
        Task<List<PollComment>> GetPollCommentsAsync(int pollId, int page = 1, int pageSize = 20);
        Task<bool> ModerateCommentAsync(int commentId, string moderatorId, bool isApproved);
    }

    public class PollCommentsService : IPollCommentsService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<PollCommentsService> _logger;
        private readonly IUserService _userService;

        public PollCommentsService(
            ApplicationDbContext context,
            ILogger<PollCommentsService> logger,
            IUserService userService)
        {
            _context = context;
            _logger = logger;
            _userService = userService;
        }

        public async Task<PollComment> AddCommentAsync(int pollId, string userId, string content)
        {
            try
            {
                var poll = await _context.Polls.FindAsync(pollId);
                if (poll == null)
                    throw new NotFoundException("Poll not found");

                var comment = new PollComment
                {
                    PollId = pollId,
                    UserId = userId,
                    Content = content,
                    CreatedAt = DateTime.UtcNow,
                    Status = CommentStatus.Pending
                };

                await _context.PollComments.AddAsync(comment);
                await _context.SaveChangesAsync();

                // Load user details
                comment.User = await _userService.GetUserDetailsAsync(userId);

                return comment;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding comment to poll {PollId}", pollId);
                throw;
            }
        }

        public async Task<PollComment> UpdateCommentAsync(int commentId, string userId, string content)
        {
            try
            {
                var comment = await _context.PollComments.FindAsync(commentId);
                if (comment == null)
                    throw new NotFoundException("Comment not found");

                if (comment.UserId != userId)
                    throw new UnauthorizedException("User not authorized to update this comment");

                comment.Content = content;
                comment.UpdatedAt = DateTime.UtcNow;
                comment.Status = CommentStatus.Pending; // Reset status for re-moderation

                await _context.SaveChangesAsync();

                // Load user details
                comment.User = await _userService.GetUserDetailsAsync(userId);

                return comment;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating comment {CommentId}", commentId);
                throw;
            }
        }

        public async Task DeleteCommentAsync(int commentId, string userId)
        {
            try
            {
                var comment = await _context.PollComments.FindAsync(commentId);
                if (comment == null)
                    throw new NotFoundException("Comment not found");

                if (comment.UserId != userId && !await _userService.IsModeratorAsync(userId))
                    throw new UnauthorizedException("User not authorized to delete this comment");

                _context.PollComments.Remove(comment);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting comment {CommentId}", commentId);
                throw;
            }
        }

        public async Task<List<PollComment>> GetPollCommentsAsync(int pollId, int page = 1, int pageSize = 20)
        {
            try
            {
                var comments = await _context.PollComments
                    .Include(c => c.User)
                    .Where(c => c.PollId == pollId && c.Status == CommentStatus.Approved)
                    .OrderByDescending(c => c.CreatedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                return comments;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting comments for poll {PollId}", pollId);
                throw;
            }
        }

        public async Task<bool> ModerateCommentAsync(int commentId, string moderatorId, bool isApproved)
        {
            try
            {
                if (!await _userService.IsModeratorAsync(moderatorId))
                    throw new UnauthorizedException("User not authorized to moderate comments");

                var comment = await _context.PollComments.FindAsync(commentId);
                if (comment == null)
                    throw new NotFoundException("Comment not found");

                comment.Status = isApproved ? CommentStatus.Approved : CommentStatus.Rejected;
                comment.ModeratedBy = moderatorId;
                comment.ModeratedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error moderating comment {CommentId}", commentId);
                throw;
            }
        }
    }

    public class PollComment
    {
        public int Id { get; set; }
        public int PollId { get; set; }
        public string UserId { get; set; }
        public string Content { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public CommentStatus Status { get; set; }
        public string ModeratedBy { get; set; }
        public DateTime? ModeratedAt { get; set; }
        public virtual ApplicationUser User { get; set; }
    }

    public enum CommentStatus
    {
        Pending,
        Approved,
        Rejected
    }
} 