using PollSystem.Models;

namespace PollSystem.Services
{
    public interface IPollService
    {
        Task<Poll> CreatePollAsync(CreatePollRequest request, string creatorIp);
        Task<Poll> GetPollAsync(Guid pollId);
        Task<List<Poll>> GetActivePollsAsync();
        Task<bool> VoteAsync(Guid pollId, Guid optionId, string voterIp);
        Task<bool> DeletePollAsync(Guid pollId, string adminIp);
    }
} 