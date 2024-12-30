using System;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using OnlinePollingSystem.Mobile.Services;

namespace OnlinePollingSystem.Mobile.ViewModels
{
    public class PollDetailsViewModel : BaseViewModel
    {
        private readonly IPollService _pollService;
        private Poll _poll;
        private bool _canVote;

        public Poll Poll
        {
            get => _poll;
            set => SetProperty(ref _poll, value);
        }

        public bool CanVote
        {
            get => _canVote;
            set => SetProperty(ref _canVote, value);
        }

        public PollDetailsViewModel(IPollService pollService)
        {
            _pollService = pollService;
            Title = "Poll Details";
        }

        public async Task LoadPollAsync(int pollId)
        {
            if (IsBusy)
                return;

            try
            {
                IsBusy = true;

                Poll = await _pollService.GetPollDetailsAsync(pollId);
                UpdatePollStatus();
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlert("Error", "Failed to load poll details", "OK");
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task VoteAsync(int optionId)
        {
            if (IsBusy)
                return;

            try
            {
                IsBusy = true;

                var success = await _pollService.VoteAsync(Poll.Id, optionId);
                if (success)
                {
                    // Refresh poll details to update vote counts
                    await LoadPollAsync(Poll.Id);
                    await Shell.Current.DisplayAlert("Success", "Your vote has been recorded", "OK");
                }
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlert("Error", "Failed to record your vote", "OK");
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task SharePollAsync()
        {
            if (IsBusy)
                return;

            try
            {
                IsBusy = true;

                var shareUrl = await _pollService.GetPollShareLinkAsync(Poll.Id);
                await Share.RequestAsync(new ShareTextRequest
                {
                    Text = $"Check out this poll: {Poll.Title}",
                    Uri = shareUrl
                });
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlert("Error", "Failed to share poll", "OK");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void UpdatePollStatus()
        {
            if (Poll == null)
                return;

            // Check if poll is active and user hasn't voted
            CanVote = !Poll.IsExpired && !Poll.HasUserVoted;

            // Update time remaining
            Poll.TimeRemaining = GetTimeRemaining(Poll.EndDate);
        }

        private string GetTimeRemaining(DateTime endDate)
        {
            var timeSpan = endDate - DateTime.UtcNow;

            if (timeSpan.TotalDays > 1)
                return $"{(int)timeSpan.TotalDays} days left";
            if (timeSpan.TotalHours > 1)
                return $"{(int)timeSpan.TotalHours} hours left";
            if (timeSpan.TotalMinutes > 1)
                return $"{(int)timeSpan.TotalMinutes} minutes left";
            if (timeSpan.TotalSeconds > 0)
                return "Ending soon";

            return "Expired";
        }
    }
} 