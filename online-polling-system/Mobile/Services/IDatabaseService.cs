using PollSystem.Mobile.Models;

namespace PollSystem.Mobile.Services
{
    public interface IDatabaseService
    {
        Task InitializeAsync();
        Task<List<Poll>> GetPollsAsync();
        Task<Poll> GetPollAsync(Guid pollId);
        Task SavePollAsync(Poll poll);
        Task SavePollsAsync(IEnumerable<Poll> polls);
        Task SaveVoteAsync(LocalVote vote);
        Task<List<LocalVote>> GetUnsyncedVotesAsync();
        Task<bool> HasVotedAsync(Guid pollId);
        Task ClearOldDataAsync(int daysToKeep = 30);
    }
} 