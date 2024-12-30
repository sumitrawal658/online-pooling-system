using Microsoft.Maui.Controls;
using OnlinePollingSystem.Mobile.ViewModels;

namespace OnlinePollingSystem.Mobile.Views
{
    public partial class MainPage : ContentPage
    {
        private readonly MainViewModel _viewModel;

        public MainPage(MainViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            BindingContext = _viewModel;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await _viewModel.LoadPollsAsync();
        }

        private async void OnPollSelected(object sender, SelectedItemChangedEventArgs e)
        {
            if (e.SelectedItem is Poll poll)
            {
                // Deselect item
                ((ListView)sender).SelectedItem = null;

                // Navigate to poll details
                await Shell.Current.GoToAsync($"{nameof(PollDetailsPage)}?pollId={poll.Id}");
            }
        }

        private async void OnCreatePollClicked(object sender, System.EventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(CreatePollPage));
        }

        private async void OnRefreshRequested(object sender, System.EventArgs e)
        {
            await _viewModel.LoadPollsAsync();
        }
    }
} 