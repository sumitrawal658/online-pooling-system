using PollSystem.Mobile.Models;

namespace PollSystem.Mobile.Services
{
    public interface IOfflineService
    {
        Task SyncPendingVotesAsync();
        Task<bool> IsDataSyncedAsync();
        Task MarkDataAsSyncedAsync();
        Task SavePendingVoteAsync(Guid pollId, Guid optionId);
        Task<DateTime> GetLastSyncTimeAsync();
    }
} 