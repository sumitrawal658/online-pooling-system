namespace PollSystem.Extensions
{
    public static class ExceptionHandlingExtensions
    {
        public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder app)
        {
            return app.UseMiddleware<GlobalExceptionMiddleware>();
        }

        public static IServiceCollection AddExceptionHandling(this IServiceCollection services)
        {
            services.AddScoped<IErrorHandler, ErrorHandler>();
            return services;
        }
    }
} 