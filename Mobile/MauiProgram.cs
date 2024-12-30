using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using PollSystem.Services.Interfaces;
using PollSystem.Services.Implementation;
using PollSystem.Mobile.Services;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using Polly.Timeout;
using Polly.Wrap;
using System;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Serilog;
using PollSystem.Services.Network;
using PollSystem.Services.Device;
using PollSystem.Services.Logging;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Register Services
        builder.Services.AddSingleton<IApiService, ApiService>();
        builder.Services.AddSingleton<IDatabaseService, DatabaseService>();
        builder.Services.AddSingleton<IConnectivity>(Connectivity.Current);
        builder.Services.AddSingleton<IOfflineService, OfflineService>();
        builder.Services.AddSingleton<IPreferences>(Preferences.Default);
        builder.Services.AddSingleton<IShare>(Share.Default);
        builder.Services.AddSingleton<ISharingService, SharingService>();
        builder.Services.AddSingleton<ICommentService, CommentService>();
        builder.Services.AddSingleton<IRealTimeService>(sp =>
        {
            var hubUrl = $"{builder.Configuration["ApiSettings:BaseUrl"]}/pollHub";
            var logger = sp.GetRequiredService<ILogger<RealTimeService>>();
            return new RealTimeService(hubUrl, logger);
        });
        builder.Services.AddSingleton<IPollQueryService, PollQueryService>();
        builder.Services.AddSingleton<IPollCommandService, PollCommandService>();
        builder.Services.AddSingleton<IUserService, UserService>();
        builder.Services.AddSingleton<PollServiceFacade>();
        builder.Services.AddSingleton<IErrorHandler, ErrorHandler>();
        builder.Services.AddSingleton<RetryPolicyService>();
        builder.Services.AddSingleton<StructuredLoggingService>();
        builder.Services.AddSingleton<INetworkMonitor, NetworkMonitor>();
        builder.Services.AddSingleton<IDeviceInfoService, DeviceInfoService>();
        builder.Services.AddSingleton<RetryPolicyConfiguration>();
        builder.Services.AddSingleton<NetworkRetryPolicyService>();
        builder.Services.AddSingleton<DetailedLoggingService>();
        builder.Services.AddSingleton<ContextAwareLoggingService>();

        // Configure HttpClient
        builder.Services.AddHttpClient<IApiService, ApiService>()
            .SetHandlerLifetime(TimeSpan.FromMinutes(5))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            });

        // Register ViewModels
        builder.Services.AddTransient<PollsViewModel>();
        builder.Services.AddTransient<PollDetailsViewModel>();
        builder.Services.AddTransient<CreatePollViewModel>();
        builder.Services.AddTransient<CommentsViewModel>();

        // Register Pages
        builder.Services.AddTransient<PollsPage>();
        builder.Services.AddTransient<PollDetailsPage>();
        builder.Services.AddTransient<CreatePollPage>();

        // Configure LiveCharts
        LiveCharts.Configure(config =>
            config.AddSkiaSharp()
                  .AddDefaultMappers()
                  .AddLightTheme());

        // Configure logging
        builder.Logging
            .AddDebug()
            .AddConsole()
            .AddSerilog(new LoggerConfiguration()
                .WriteTo.File("logs/app.log", 
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
                .CreateLogger());

        // Configure retry policy
        builder.Services.Configure<RetryPolicyConfiguration>(config =>
        {
            config.MaxRetries = 3;
            config.InitialDelay = TimeSpan.FromSeconds(1);
            config.MaxDelay = TimeSpan.FromSeconds(30);
            config.Timeout = TimeSpan.FromSeconds(10);
            config.JitterFactor = 0.2;
            config.UseExponentialBackoff = true;
        });

        return builder.Build();
    }
} 