using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace PollSystem.Extensions
{
    public static class LoggingExtensions
    {
        public static ILogger WithContext(this ILogger logger, Action<LogContext> configureContext)
        {
            var context = LogContext.Current;
            configureContext(context);
            return logger;
        }

        public static ILogger WithUserId(this ILogger logger, string userId)
        {
            LogContext.Current.UserId = userId;
            return logger;
        }

        public static ILogger WithData(this ILogger logger, string key, object value)
        {
            LogContext.Current.AdditionalData[key] = value;
            return logger;
        }

        public static void LogOperationStart(
            this ILogger logger,
            string message,
            [CallerMemberName] string operation = null)
        {
            var scope = new Dictionary<string, object>
            {
                ["Operation"] = operation,
                ["StartTime"] = DateTime.UtcNow
            };

            using (logger.BeginScope(scope))
            {
                logger.LogInformation(
                    "Starting operation {Operation}: {Message}",
                    operation,
                    message);
            }
        }

        public static void LogOperationEnd(
            this ILogger logger,
            string message,
            TimeSpan duration,
            [CallerMemberName] string operation = null)
        {
            var scope = new Dictionary<string, object>
            {
                ["Operation"] = operation,
                ["Duration"] = duration.TotalMilliseconds
            };

            using (logger.BeginScope(scope))
            {
                logger.LogInformation(
                    "Completed operation {Operation} in {Duration}ms: {Message}",
                    operation,
                    duration.TotalMilliseconds,
                    message);
            }
        }
    }
} 