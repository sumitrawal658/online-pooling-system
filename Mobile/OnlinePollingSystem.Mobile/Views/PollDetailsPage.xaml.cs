using Microsoft.Maui.Controls;
using OnlinePollingSystem.Mobile.ViewModels;

namespace OnlinePollingSystem.Mobile.Views
{
    [QueryProperty(nameof(PollId), "pollId")]
    public partial class PollDetailsPage : ContentPage
    {
        private readonly PollDetailsViewModel _viewModel;

        public string PollId
        {
            set
            {
                if (int.TryParse(value, out int pollId))
                {
                    _viewModel.LoadPollAsync(pollId);
                }
            }
        }

        public PollDetailsPage(PollDetailsViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            BindingContext = _viewModel;
        }

        private async void OnVoteClicked(object sender, System.EventArgs e)
        {
            if (sender is Button button && button.BindingContext is PollOption option)
            {
                await _viewModel.VoteAsync(option.Id);
            }
        }

        private async void OnViewResultsClicked(object sender, System.EventArgs e)
        {
            await Shell.Current.GoToAsync($"{nameof(ResultsPage)}?pollId={_viewModel.Poll.Id}");
        }

        private async void OnViewCommentsClicked(object sender, System.EventArgs e)
        {
            await Shell.Current.GoToAsync($"{nameof(CommentsPage)}?pollId={_viewModel.Poll.Id}");
        }

        private async void OnShareClicked(object sender, System.EventArgs e)
        {
            await _viewModel.SharePollAsync();
        }
    }
} 