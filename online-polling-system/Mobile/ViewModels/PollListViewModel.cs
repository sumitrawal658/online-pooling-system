using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PollSystem.Mobile.Models;
using PollSystem.Mobile.Services;

namespace PollSystem.Mobile.ViewModels
{
    public partial class PollListViewModel : ObservableObject
    {
        private readonly ApiService _apiService;
        private readonly DatabaseService _databaseService;

        [ObservableProperty]
        private List<Poll> _polls;

        [ObservableProperty]
        private bool _isRefreshing;

        public PollListViewModel(ApiService apiService, DatabaseService databaseService)
        {
            _apiService = apiService;
            _databaseService = databaseService;
            LoadPolls();
        }

        [RelayCommand]
        private async Task Refresh()
        {
            IsRefreshing = true;
            await LoadPolls();
            IsRefreshing = false;
        }

        private async Task LoadPolls()
        {
            // Try to get polls from API
            var apiPolls = await _apiService.GetPollsAsync();
            
            if (apiPolls != null)
            {
                // Save polls locally
                foreach (var poll in apiPolls)
                {
                    await _databaseService.SavePollAsync(poll);
                }
                Polls = apiPolls;
            }
            else
            {
                // Load from local database if API fails
                Polls = await _databaseService.GetPollsAsync();
            }

            // Try to sync any pending votes
            await SyncPendingVotes();
        }

        private async Task SyncPendingVotes()
        {
            var unsyncedVotes = await _databaseService.GetUnsyncedVotesAsync();
            foreach (var vote in unsyncedVotes)
            {
                var success = await _apiService.SubmitVoteAsync(vote.PollId, vote.OptionId);
                if (success)
                {
                    vote.IsSynced = true;
                    await _databaseService.SaveVoteAsync(vote);
                }
            }
        }
    }
} 