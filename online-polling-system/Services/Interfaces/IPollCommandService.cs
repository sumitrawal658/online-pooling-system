namespace PollSystem.Services.Interfaces
{
    public interface IPollCommandService
    {
        Task<Poll> CreatePollAsync(CreatePollRequest request, string creatorId);
        Task<bool> VoteAsync(Guid pollId, Guid optionId, string voterId);
        Task<bool> DeletePollAsync(Guid pollId, string adminId);
        Task<bool> UpdatePollStatusAsync(Guid pollId, bool isActive);
    }
} 