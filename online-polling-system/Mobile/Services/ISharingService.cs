namespace PollSystem.Mobile.Services
{
    public interface ISharingService
    {
        Task SharePollAsync(Poll poll);
        string GenerateShareableLink(Guid pollId);
        Task<bool> ShareTextAsync(string text);
        Task<bool> ShareLinkAsync(string uri, string title);
    }
} 