using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using PollSystem.Mobile.Views;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        RegisterRoutes();
    }

    private void RegisterRoutes()
    {
        Routing.RegisterRoute("polls", typeof(PollsPage));
        Routing.RegisterRoute("polls/details", typeof(PollDetailsPage));
        
        // Register URI scheme for deep linking
        Microsoft.Maui.Handlers.WebViewHandler.AddGlobalHandler("polls", OnPollDeepLink);
    }

    private async Task OnPollDeepLink(Uri uri)
    {
        try
        {
            var segments = uri.Segments;
            if (segments.Length > 1 && Guid.TryParse(segments[1].TrimEnd('/'), out Guid pollId))
            {
                var parameters = new Dictionary<string, object>
                {
                    { "PollId", pollId }
                };
                await Shell.Current.GoToAsync($"polls/details", parameters);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error handling deep link: {ex.Message}");
        }
    }
} 