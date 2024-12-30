using Microsoft.Maui.Controls;
using OnlinePollingSystem.Mobile.Views;

namespace OnlinePollingSystem.Mobile
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();

            // Register routes for navigation
            Routing.RegisterRoute(nameof(PollDetailsPage), typeof(PollDetailsPage));
            Routing.RegisterRoute(nameof(CreatePollPage), typeof(CreatePollPage));
            Routing.RegisterRoute(nameof(VotePage), typeof(VotePage));
            Routing.RegisterRoute(nameof(ResultsPage), typeof(ResultsPage));
            Routing.RegisterRoute(nameof(CommentsPage), typeof(CommentsPage));
        }
    }
} 