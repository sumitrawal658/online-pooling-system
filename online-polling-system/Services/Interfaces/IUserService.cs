namespace PollSystem.Services.Interfaces
{
    public interface IUserService
    {
        Task<User> GetOrCreateUserAsync(string identifier);
        Task<bool> IsAdminAsync(string identifier);
        Task<UserActivity> GetUserActivityAsync(string identifier);
    }
} 