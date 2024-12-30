namespace PollSystem.Services
{
    public interface IErrorHandler
    {
        Task HandleExceptionAsync(Exception ex, string context = null);
        Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> action, int maxRetries = 3);
        Task LogErrorAsync(string message, Exception ex = null, IDictionary<string, object> properties = null);
    }
} 