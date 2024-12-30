namespace PollSystem.Services.Interfaces
{
    public interface IPollQueryService
    {
        Task<Poll> GetPollAsync(Guid pollId);
        Task<List<Poll>> GetActivePollsAsync();
        Task<int> GetTotalVotesAsync(Guid pollId);
        Task<bool> HasUserVotedAsync(Guid pollId, string userId);
    }
} 