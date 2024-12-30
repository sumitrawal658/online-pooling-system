namespace PollSystem.Services
{
    public interface IBaseService
    {
        Task InitializeAsync();
        void Dispose();
    }
} 