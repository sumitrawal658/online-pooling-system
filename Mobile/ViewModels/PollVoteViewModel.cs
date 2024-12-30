using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PollSystem.Mobile.Models;
using PollSystem.Mobile.Services;

namespace PollSystem.Mobile.ViewModels
{
    public partial class PollVoteViewModel : ObservableObject
    {
        private readonly ApiService _apiService;
        private readonly DatabaseService _databaseService;

        [ObservableProperty]
        private Poll _poll;

        [ObservableProperty]
        private PollOption _selectedOption;

        public bool CanVote => SelectedOption != null;

        public PollVoteViewModel(Poll poll, ApiService apiService, DatabaseService databaseService)
        {
            _poll = poll;
            _apiService = apiService;
            _databaseService = databaseService;
        }

        [RelayCommand]
        private async Task Vote()
        {
            if (SelectedOption == null) return;

            var vote = new LocalVote
            {
                PollId = Poll.PollId,
                OptionId = SelectedOption.OptionId,
                CreatedAt = DateTime.UtcNow,
                IsSynced = false
            };

            // Save vote locally
            await _databaseService.SaveVoteAsync(vote);

            // Try to sync with server
            var success = await _apiService.SubmitVoteAsync(vote.PollId, vote.OptionId);
            if (success)
            {
                vote.IsSynced = true;
                await _databaseService.SaveVoteAsync(vote);
            }

            // Update UI regardless of sync status
            SelectedOption.VoteCount++;
            await Shell.Current.GoToAsync("..");
        }
    }
} 