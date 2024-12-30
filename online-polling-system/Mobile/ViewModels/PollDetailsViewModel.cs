using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PollSystem.Mobile.Services;
using PollSystem.Mobile.Models;
using Xamarin.Essentials;

public partial class PollDetailsViewModel : BaseViewModel
{
    private readonly IPollService _pollService;
    private readonly IRealTimeService _realTimeService;

    [ObservableProperty]
    private Poll poll;

    [ObservableProperty]
    private PollResultsViewModel resultsViewModel;

    [ObservableProperty]
    private string connectionStatus;

    public PollDetailsViewModel(
        IPollService pollService,
        IRealTimeService realTimeService)
    {
        _pollService = pollService;
        _realTimeService = realTimeService;
        _realTimeService.OnPollUpdated += HandlePollUpdate;
        _realTimeService.OnConnectionStatusChanged += HandleConnectionStatusChanged;
        ResultsViewModel = new PollResultsViewModel();
    }

    public async Task InitializeAsync(Guid pollId)
    {
        try
        {
            IsBusy = true;
            await _realTimeService.ConnectAsync();
            Poll = await _pollService.GetPollAsync(pollId);
            await _realTimeService.JoinPollGroupAsync(pollId);
            UpdateChart();
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", "Failed to load poll", "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void HandlePollUpdate(object sender, Poll updatedPoll)
    {
        if (updatedPoll.PollId == Poll?.PollId)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                Poll = updatedPoll;
                UpdateChart();
            });
        }
    }

    private void UpdateChart()
    {
        if (Poll != null)
        {
            ResultsViewModel.UpdateResults(Poll);
        }
    }

    private void HandleConnectionStatusChanged(object sender, ConnectionStatus status)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ConnectionStatus = status.IsConnected ? "Connected" : status.Message;
            if (!status.IsConnected)
            {
                ShowError("Connection lost. Attempting to reconnect...");
            }
            else
            {
                ClearError();
            }
        });
    }

    public override void Dispose()
    {
        if (Poll != null)
        {
            _realTimeService.LeavePollGroupAsync(Poll.PollId).ConfigureAwait(false);
        }
        _realTimeService.DisconnectAsync().ConfigureAwait(false);
        _realTimeService.OnPollUpdated -= HandlePollUpdate;
        _realTimeService.OnConnectionStatusChanged -= HandleConnectionStatusChanged;
        base.Dispose();
    }
} 