using PollSystem.Mobile.Models;
using SQLite;

namespace PollSystem.Mobile.Services
{
    public class OfflineService : IOfflineService
    {
        private readonly IDatabaseService _databaseService;
        private readonly IApiService _apiService;
        private readonly IConnectivity _connectivity;
        private readonly IPreferences _preferences;
        private const string LastSyncKey = "LastSyncTime";

        public OfflineService(
            IDatabaseService databaseService,
            IApiService apiService,
            IConnectivity connectivity,
            IPreferences preferences)
        {
            _databaseService = databaseService;
            _apiService = apiService;
            _connectivity = connectivity;
            _preferences = preferences;
        }

        public async Task SyncPendingVotesAsync()
        {
            if (_connectivity.NetworkAccess != NetworkAccess.Internet)
                return;

            var pendingVotes = await _databaseService.GetUnsyncedVotesAsync();
            foreach (var vote in pendingVotes)
            {
                try
                {
                    var success = await _apiService.SubmitVoteAsync(vote.PollId, vote.OptionId);
                    if (success)
                    {
                        vote.IsSynced = true;
                        await _databaseService.SaveVoteAsync(vote);
                    }
                }
                catch (Exception ex)
                {
                    // Log error but continue with other votes
                    Debug.WriteLine($"Failed to sync vote: {ex.Message}");
                }
            }

            await MarkDataAsSyncedAsync();
        }

        public async Task SavePendingVoteAsync(Guid pollId, Guid optionId)
        {
            var vote = new LocalVote
            {
                PollId = pollId,
                OptionId = optionId,
                CreatedAt = DateTime.UtcNow,
                IsSynced = false
            };

            await _databaseService.SaveVoteAsync(vote);
        }

        public async Task<bool> IsDataSyncedAsync()
        {
            var unsyncedVotes = await _databaseService.GetUnsyncedVotesAsync();
            return !unsyncedVotes.Any();
        }

        public Task MarkDataAsSyncedAsync()
        {
            _preferences.Set(LastSyncKey, DateTime.UtcNow.Ticks);
            return Task.CompletedTask;
        }

        public Task<DateTime> GetLastSyncTimeAsync()
        {
            var ticks = _preferences.Get(LastSyncKey, 0L);
            return Task.FromResult(new DateTime(ticks));
        }
    }
} 