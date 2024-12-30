using System.Net.Http.Json;
using PollSystem.Mobile.Models;
using Polly;
using Polly.Retry;
using Polly.Timeout;
using Polly.Wrap;
using PollSystem.Exceptions;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net.NetworkInformation;
using PollSystem.Services.Network;

namespace PollSystem.Mobile.Services
{
    public class ApiService : IApiService
    {
        private readonly HttpClient _httpClient;
        private readonly NetworkRetryPolicyService _retryPolicy;
        private readonly INetworkMonitor _networkMonitor;
        private readonly ILogger<ApiService> _logger;

        public ApiService(
            HttpClient httpClient,
            NetworkRetryPolicyService retryPolicy,
            INetworkMonitor networkMonitor,
            ILogger<ApiService> logger)
        {
            _httpClient = httpClient;
            _retryPolicy = retryPolicy;
            _networkMonitor = networkMonitor;
            _logger = logger;
        }

        public async Task<List<Poll>> GetPollsAsync()
        {
            return await _retryPolicy.CreatePolicy<List<Poll>>()
                .ExecuteAsync(async () =>
                {
                    try
                    {
                        var response = await _httpClient.GetAsync("polls");
                        response.EnsureSuccessStatusCode();
                        return await response.Content.ReadFromJsonAsync<List<Poll>>();
                    }
                    catch (HttpRequestException ex)
                    {
                        throw new NetworkException(
                            "Failed to fetch polls",
                            GetNetworkStatus(ex.StatusCode),
                            ex);
                    }
                });
        }

        public async Task<bool> SubmitVoteAsync(Guid pollId, Guid optionId)
        {
            return await _retryPolicy.CreatePolicy<bool>()
                .ExecuteAsync(async () =>
                {
                    try
                    {
                        var response = await _httpClient.PostAsJsonAsync(
                            "votes",
                            new { PollId = pollId, OptionId = optionId });

                        if (response.StatusCode == HttpStatusCode.Conflict)
                        {
                            throw new BusinessException(
                                "Duplicate vote",
                                "DUPLICATE_VOTE",
                                ErrorSeverity.Warning);
                        }

                        response.EnsureSuccessStatusCode();
                        return true;
                    }
                    catch (HttpRequestException ex) when (ex.StatusCode != HttpStatusCode.Conflict)
                    {
                        throw new NetworkException(
                            "Failed to submit vote",
                            GetNetworkStatus(ex.StatusCode),
                            ex);
                    }
                });
        }

        private NetworkStatus GetNetworkStatus(HttpStatusCode? statusCode) => statusCode switch
        {
            HttpStatusCode.NotFound => NetworkStatus.ClientError,
            HttpStatusCode.BadRequest => NetworkStatus.ClientError,
            HttpStatusCode.InternalServerError => NetworkStatus.ServerError,
            HttpStatusCode.ServiceUnavailable => NetworkStatus.ServerError,
            HttpStatusCode.GatewayTimeout => NetworkStatus.Timeout,
            _ => NetworkStatus.Unknown
        };
    }
} 