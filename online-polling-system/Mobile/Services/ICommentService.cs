namespace PollSystem.Mobile.Services
{
    public interface ICommentService
    {
        Task<List<Comment>> GetCommentsForPollAsync(Guid pollId);
        Task<Comment> AddCommentAsync(Guid pollId, string content, string authorName);
        Task<Comment> UpdateCommentAsync(int commentId, string content);
        Task<bool> DeleteCommentAsync(int commentId);
        Task<int> GetCommentCountAsync(Guid pollId);
    }
} 