using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Maui.Controls;
using OnlinePollingSystem.Mobile.Services;

namespace OnlinePollingSystem.Mobile.ViewModels
{
    public class MainViewModel : BaseViewModel
    {
        private readonly IPollService _pollService;
        private bool _isRefreshing;

        public ObservableCollection<Poll> Polls { get; }
        public ICommand RefreshCommand { get; }

        public bool IsRefreshing
        {
            get => _isRefreshing;
            set => SetProperty(ref _isRefreshing, value);
        }

        public MainViewModel(IPollService pollService)
        {
            _pollService = pollService;
            Polls = new ObservableCollection<Poll>();
            RefreshCommand = new Command(async () => await LoadPollsAsync());
        }

        public async Task LoadPollsAsync()
        {
            if (IsBusy)
                return;

            try
            {
                IsBusy = true;
                IsRefreshing = true;

                var polls = await _pollService.GetPollsAsync();
                Polls.Clear();

                foreach (var poll in polls)
                {
                    // Enhance poll with UI-specific properties
                    poll.StatusColor = GetStatusColor(poll.Status);
                    poll.TimeRemaining = GetTimeRemaining(poll.EndDate);
                    Polls.Add(poll);
                }
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlert("Error", "Failed to load polls", "OK");
            }
            finally
            {
                IsBusy = false;
                IsRefreshing = false;
            }
        }

        private Color GetStatusColor(string status)
        {
            return status switch
            {
                "Active" => Colors.Green,
                "Expired" => Colors.Red,
                "Draft" => Colors.Gray,
                _ => Colors.Black
            };
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