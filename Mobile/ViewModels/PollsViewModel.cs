using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PollSystem.Mobile.Models;
using PollSystem.Mobile.Services;

namespace PollSystem.Mobile.ViewModels
{
    public partial class PollsViewModel : BaseViewModel
    {
        private readonly IApiService _apiService;
        private readonly IDatabaseService _databaseService;
        private readonly IConnectivity _connectivity;
        private readonly IOfflineService _offlineService;
        private IDisposable _connectivitySubscription;

        [ObservableProperty]
        private ObservableCollection<Poll> _polls;

        [ObservableProperty]
        private bool _isRefreshing;

        [ObservableProperty]
        private bool _isOffline;

        [ObservableProperty]
        private DateTime _lastSyncTime;

        public PollsViewModel(
            IApiService apiService, 
            IDatabaseService databaseService,
            IOfflineService offlineService,
            IConnectivity connectivity)
        {
            _apiService = apiService;
            _databaseService = databaseService;
            _connectivity = connectivity;
            _offlineService = offlineService;
            Polls = new ObservableCollection<Poll>();
            
            // Subscribe to connectivity changes
            _connectivitySubscription = connectivity.ConnectivityChanged.Subscribe(async _ =>
            {
                if (connectivity.NetworkAccess == NetworkAccess.Internet)
                {
                    await SyncDataAsync();
                }
                IsOffline = connectivity.NetworkAccess != NetworkAccess.Internet;
            });
        }

        [RelayCommand]
        private async Task LoadPolls()
        {
            if (IsLoading) return;

            try
            {
                IsLoading = true;
                ClearError();

                if (_connectivity.NetworkAccess == NetworkAccess.Internet)
                {
                    // Online mode: Get from API and cache
                    var apiPolls = await _apiService.GetPollsAsync();
                    if (apiPolls != null)
                    {
                        foreach (var poll in apiPolls)
                        {
                            await _databaseService.SavePollAsync(poll);
                        }
                        UpdatePolls(apiPolls);
                    }
                }
                else
                {
                    // Offline mode: Load from local database
                    var localPolls = await _databaseService.GetPollsAsync();
                    UpdatePolls(localPolls);
                }
            }
            catch (Exception ex)
            {
                ShowError("Failed to load polls");
            }
            finally
            {
                IsLoading = false;
                IsRefreshing = false;
            }
        }

        [RelayCommand]
        private async Task Refresh()
        {
            IsRefreshing = true;
            await LoadPolls();
        }

        [RelayCommand]
        private async Task PollSelected(Poll poll)
        {
            if (poll == null) return;

            var parameters = new Dictionary<string, object>
            {
                { "Poll", poll }
            };

            await Shell.Current.GoToAsync($"polldetails", parameters);
        }

        [RelayCommand]
        private async Task CreatePoll()
        {
            await Shell.Current.GoToAsync("createpoll");
        }

        private void UpdatePolls(IEnumerable<Poll> newPolls)
        {
            Polls.Clear();
            foreach (var poll in newPolls.OrderByDescending(p => p.CreatedAt))
            {
                Polls.Add(poll);
            }
        }

        public async Task OnAppearing()
        {
            await LoadPolls();
        }

        private async Task SyncDataAsync()
        {
            try
            {
                IsLoading = true;
                await _offlineService.SyncPendingVotesAsync();
                await LoadPolls();
                LastSyncTime = await _offlineService.GetLastSyncTimeAsync();
            }
            finally
            {
                IsLoading = false;
            }
        }

        public override void Dispose()
        {
            _connectivitySubscription?.Dispose();
            base.Dispose();
        }
    }
} 