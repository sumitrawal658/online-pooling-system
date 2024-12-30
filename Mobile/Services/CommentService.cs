namespace PollSystem.Mobile.Services
{
    public class CommentService : ICommentService
    {
        private readonly IDatabaseService _databaseService;
        private readonly IApiService _apiService;
        private readonly IConnectivity _connectivity;

        public CommentService(
            IDatabaseService databaseService,
            IApiService apiService,
            IConnectivity connectivity)
        {
            _databaseService = databaseService;
            _apiService = apiService;
            _connectivity = connectivity;
        }

        public async Task<List<Comment>> GetCommentsForPollAsync(Guid pollId)
        {
            try
            {
                if (_connectivity.NetworkAccess == NetworkAccess.Internet)
                {
                    // Try to get from API first
                    var comments = await _apiService.GetCommentsAsync(pollId);
                    if (comments != null)
                    {
                        // Cache comments locally
                        await _databaseService.SaveCommentsAsync(comments);
                        return comments;
                    }
                }

                // Fallback to local database
                return await _databaseService.GetCommentsForPollAsync(pollId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting comments: {ex.Message}");
                return await _databaseService.GetCommentsForPollAsync(pollId);
            }
        }

        public async Task<Comment> AddCommentAsync(Guid pollId, string content, string authorName)
        {
            var comment = new Comment
            {
                PollId = pollId,
                Content = content,
                AuthorName = authorName,
                CreatedAt = DateTime.UtcNow,
                IsSynced = false
            };

            try
            {
                // Save locally first
                await _databaseService.SaveCommentAsync(comment);

                if (_connectivity.NetworkAccess == NetworkAccess.Internet)
                {
                    // Try to sync with API
                    var syncedComment = await _apiService.AddCommentAsync(comment);
                    if (syncedComment != null)
                    {
                        comment.IsSynced = true;
                        await _databaseService.SaveCommentAsync(comment);
                        return syncedComment;
                    }
                }

                return comment;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error adding comment: {ex.Message}");
                return comment;
            }
        }

        public async Task<Comment> UpdateCommentAsync(int commentId, string content)
        {
            try
            {
                var comment = await _databaseService.GetCommentAsync(commentId);
                if (comment == null) return null;

                comment.Content = content;
                comment.UpdatedAt = DateTime.UtcNow;
                comment.IsSynced = false;

                await _databaseService.SaveCommentAsync(comment);

                if (_connectivity.NetworkAccess == NetworkAccess.Internet)
                {
                    var syncedComment = await _apiService.UpdateCommentAsync(comment);
                    if (syncedComment != null)
                    {
                        comment.IsSynced = true;
                        await _databaseService.SaveCommentAsync(comment);
                        return syncedComment;
                    }
                }

                return comment;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating comment: {ex.Message}");
                throw;
            }
        }

        public async Task<bool> DeleteCommentAsync(int commentId)
        {
            try
            {
                var comment = await _databaseService.GetCommentAsync(commentId);
                if (comment == null) return false;

                comment.IsDeleted = true;
                comment.IsSynced = false;
                await _databaseService.SaveCommentAsync(comment);

                if (_connectivity.NetworkAccess == NetworkAccess.Internet)
                {
                    var success = await _apiService.DeleteCommentAsync(commentId);
                    if (success)
                    {
                        comment.IsSynced = true;
                        await _databaseService.SaveCommentAsync(comment);
                    }
                    return success;
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error deleting comment: {ex.Message}");
                return false;
            }
        }

        public async Task<int> GetCommentCountAsync(Guid pollId)
        {
            return await _databaseService.GetCommentCountAsync(pollId);
        }
    }
} 