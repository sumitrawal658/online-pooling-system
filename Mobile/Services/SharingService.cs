using System.Web;

namespace PollSystem.Mobile.Services
{
    public class SharingService : ISharingService
    {
        private readonly IShare _share;
        private readonly string _baseUrl;

        public SharingService(IShare share, IConfiguration configuration)
        {
            _share = share;
            _baseUrl = configuration["ShareSettings:BaseUrl"] ?? "https://yourapp.com/polls/";
        }

        public async Task SharePollAsync(Poll poll)
        {
            var shareLink = GenerateShareableLink(poll.PollId);
            var shareText = $"Vote on '{poll.Title}'\n{shareLink}";

            try
            {
                await ShareTextAsync(shareText);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error sharing poll: {ex.Message}");
                throw;
            }
        }

        public string GenerateShareableLink(Guid pollId)
        {
            // Generate a deep link URL
            return $"{_baseUrl}{pollId}";
        }

        public async Task<bool> ShareTextAsync(string text)
        {
            try
            {
                var shareRequest = new ShareTextRequest
                {
                    Text = text,
                    Title = "Share Poll"
                };

                var result = await _share.RequestAsync(shareRequest);
                return result.IsSuccessful;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error sharing text: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ShareLinkAsync(string uri, string title)
        {
            try
            {
                var shareRequest = new ShareTextRequest
                {
                    Uri = uri,
                    Title = title
                };

                var result = await _share.RequestAsync(shareRequest);
                return result.IsSuccessful;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error sharing link: {ex.Message}");
                return false;
            }
        }
    }
} 