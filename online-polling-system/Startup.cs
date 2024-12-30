using Microsoft.Extensions.DependencyInjection;
using Services.IPollService;
using Services.PollService;
using Microsoft.EntityFrameworkCore;
using PollSystem.Data;
using Microsoft.Extensions.Caching.Memory;
using PollSystem.Middleware;
using Microsoft.AspNetCore.RateLimiting;

public void ConfigureServices(IServiceCollection services)
{
    // Add DbContext
    services.AddDbContext<PollDbContext>(options =>
        options.UseSqlServer(
            Configuration.GetConnectionString("DefaultConnection"),
            b => b.MigrationsAssembly("PollSystem")));

    // Add Memory Cache
    services.AddMemoryCache();

    // Add Services
    services.AddScoped<PollService>();
    services.AddScoped<IPollService, CachedPollService>();

    // Add rate limiting
    services.AddRateLimiting();
}

public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
{
    // Add custom middleware
    app.UseMiddleware<ErrorHandlingMiddleware>();
    app.UseMiddleware<RequestValidationMiddleware>();

    // Add rate limiting middleware
    app.UseRateLimiting();

    // Existing configurations...
}

private static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRateLimiting(this IServiceCollection services)
    {
        return services.AddMemoryCache()
            .Configure<IpRateLimitOptions>(options =>
            {
                options.GeneralRules = new List<RateLimitRule>
                {
                    new RateLimitRule
                    {
                        Endpoint = "*",
                        Period = "1m",
                        Limit = 60
                    },
                    new RateLimitRule
                    {
                        Endpoint = "POST:/api/poll/*/vote",
                        Period = "1m",
                        Limit = 1
                    }
                };
            })
            .AddSingleton<IRateLimitCounterStore, MemoryCacheRateLimitCounterStore>()
            .AddSingleton<IIpPolicyStore, MemoryCacheIpPolicyStore>()
            .AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
    }
} 