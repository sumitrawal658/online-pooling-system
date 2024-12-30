using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using OnlinePollingSystem.Mobile.Models;

namespace OnlinePollingSystem.Mobile.Services
{
    public interface IPollService
    {
        Task<List<Poll>> GetPollsAsync();
        Task<Poll> GetPollDetailsAsync(int pollId);
        Task<Poll> CreatePollAsync(CreatePollRequest request);
        Task<bool> VoteAsync(int pollId, int optionId);
        Task<PollResults> GetPollResultsAsync(int pollId);
        Task<List<PollComment>> GetPollCommentsAsync(int pollId, int page = 1);
        Task<PollComment> AddCommentAsync(int pollId, string content);
    }

    public class PollService : IPollService
    {
        private readonly HttpClient _httpClient;
        private readonly IAuthService _authService;
        private readonly string _baseUrl;

        public PollService(IAuthService authService)
        {
            _authService = authService;
            _httpClient = new HttpClient();
            _baseUrl = "https://api.pollingapp.com"; // Configure in settings
        }

        public async Task<List<Poll>> GetPollsAsync()
        {
            try
            {
                var token = await _authService.GetTokenAsync();
                _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                var response = await _httpClient.GetAsync($"{_baseUrl}/api/polls");
                response.EnsureSuccessStatusCode();

                return await response.Content.ReadFromJsonAsync<List<Poll>>();
            }
            catch (Exception ex)
            {
                // Log error
                throw new ServiceException("Failed to get polls", ex);
            }
        }

        public async Task<Poll> GetPollDetailsAsync(int pollId)
        {
            try
            {
                var token = await _authService.GetTokenAsync();
                _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                var response = await _httpClient.GetAsync($"{_baseUrl}/api/polls/{pollId}");
                response.EnsureSuccessStatusCode();

                return await response.Content.ReadFromJsonAsync<Poll>();
            }
            catch (Exception ex)
            {
                // Log error
                throw new ServiceException($"Failed to get poll details for poll {pollId}", ex);
            }
        }

        public async Task<Poll> CreatePollAsync(CreatePollRequest request)
        {
            try
            {
                var token = await _authService.GetTokenAsync();
                _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/polls", request);
                response.EnsureSuccessStatusCode();

                return await response.Content.ReadFromJsonAsync<Poll>();
            }
            catch (Exception ex)
            {
                // Log error
                throw new ServiceException("Failed to create poll", ex);
            }
        }

        public async Task<bool> VoteAsync(int pollId, int optionId)
        {
            try
            {
                var token = await _authService.GetTokenAsync();
                _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                var request = new { OptionId = optionId };
                var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/polls/{pollId}/vote", request);
                response.EnsureSuccessStatusCode();

                return true;
            }
            catch (Exception ex)
            {
                // Log error
                throw new ServiceException($"Failed to vote on poll {pollId}", ex);
            }
        }

        public async Task<PollResults> GetPollResultsAsync(int pollId)
        {
            try
            {
                var token = await _authService.GetTokenAsync();
                _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                var response = await _httpClient.GetAsync($"{_baseUrl}/api/polls/{pollId}/results");
                response.EnsureSuccessStatusCode();

                return await response.Content.ReadFromJsonAsync<PollResults>();
            }
            catch (Exception ex)
            {
                // Log error
                throw new ServiceException($"Failed to get results for poll {pollId}", ex);
            }
        }

        public async Task<List<PollComment>> GetPollCommentsAsync(int pollId, int page = 1)
        {
            try
            {
                var token = await _authService.GetTokenAsync();
                _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                var response = await _httpClient.GetAsync($"{_baseUrl}/api/polls/{pollId}/comments?page={page}");
                response.EnsureSuccessStatusCode();

                return await response.Content.ReadFromJsonAsync<List<PollComment>>();
            }
            catch (Exception ex)
            {
                // Log error
                throw new ServiceException($"Failed to get comments for poll {pollId}", ex);
            }
        }

        public async Task<PollComment> AddCommentAsync(int pollId, string content)
        {
            try
            {
                var token = await _authService.GetTokenAsync();
                _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                var request = new { Content = content };
                var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/polls/{pollId}/comments", request);
                response.EnsureSuccessStatusCode();

                return await response.Content.ReadFromJsonAsync<PollComment>();
            }
            catch (Exception ex)
            {
                // Log error
                throw new ServiceException($"Failed to add comment to poll {pollId}", ex);
            }
        }
    }

    public class ServiceException : Exception
    {
        public ServiceException(string message, Exception innerException = null) 
            : base(message, innerException)
        {
        }
    }
} 