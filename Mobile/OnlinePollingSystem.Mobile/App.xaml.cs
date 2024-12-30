using Microsoft.Maui;
using Microsoft.Maui.Controls;
using OnlinePollingSystem.Mobile.Services;
using OnlinePollingSystem.Mobile.Views;

namespace OnlinePollingSystem.Mobile
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();

            // Register services
            DependencyService.Register<IPollService, PollService>();
            DependencyService.Register<IAuthService, AuthService>();
            DependencyService.Register<INavigationService, NavigationService>();

            // Set main page
            MainPage = new AppShell();
        }

        protected override void OnStart()
        {
            // Handle when your app starts
        }

        protected override void OnSleep()
        {
            // Handle when your app sleeps
        }

        protected override void OnResume()
        {
            // Handle when your app resumes
        }
    }
} 