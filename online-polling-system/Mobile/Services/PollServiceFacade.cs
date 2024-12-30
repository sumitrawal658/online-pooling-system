namespace PollSystem.Mobile.Services
{
    public class PollServiceFacade : BaseDisposableService
    {
        private readonly IPollQueryService _queryService;
        private readonly IPollCommandService _commandService;
        private readonly IRealTimeService _realTimeService;
        private readonly IConnectivity _connectivity;
        private readonly IDatabaseService _databaseService;
        private readonly List<IDisposable> _disposables;
        private readonly SemaphoreSlim _syncSemaphore;
        private readonly IErrorHandler _errorHandler;
        private readonly ContextAwareLoggingService _logging;

        public PollServiceFacade(
            IPollQueryService queryService,
            IPollCommandService commandService,
            IRealTimeService realTimeService,
            IConnectivity connectivity,
            IDatabaseService databaseService,
            IErrorHandler errorHandler,
            ILogger<PollServiceFacade> logger) : base(logger)
        {
            _disposables = new List<IDisposable>();
            _syncSemaphore = new SemaphoreSlim(1, 1);

            // Add disposable services to the list
            if (realTimeService is IDisposable disposableRealTime)
                _disposables.Add(disposableRealTime);
            if (databaseService is IDisposable disposableDatabase)
                _disposables.Add(disposableDatabase);

            _queryService = queryService;
            _commandService = commandService;
            _realTimeService = realTimeService;
            _connectivity = connectivity;
            _databaseService = databaseService;
            _errorHandler = errorHandler;
            _logging = new ContextAwareLoggingService(logger);
        }

        protected override void DisposeManagedResources()
        {
            foreach (var disposable in _disposables)
            {
                try
                {
                    disposable.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error disposing {disposable.GetType().Name}");
                }
            }
            _disposables.Clear();
            _syncSemaphore?.Dispose();
        }

        protected override async ValueTask DisposeAsyncCore()
        {
            foreach (var disposable in _disposables.OfType<IAsyncDisposable>())
            {
                try
                {
                    await disposable.DisposeAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error async disposing {disposable.GetType().Name}");
                }
            }
        }

        public async Task<Poll> GetPollAsync(Guid pollId)
        {
            using var operation = _logging.BeginOperation("GetPoll");
            OperationContext.Current.SetData("PollId", pollId);
            
            try
            {
                var poll = await _errorHandler.ExecuteWithRetryAsync(async () =>
                {
                    using var subOperation = _logging.BeginOperation("FetchPoll");
                    
                    if (_connectivity.NetworkAccess == NetworkAccess.Internet)
                    {
                        var result = await _queryService.GetPollAsync(pollId);
                        if (result != null)
                        {
                            OperationContext.Current.SetData("PollTitle", result.Title);
                            await _databaseService.SavePollAsync(result);
                            
                            _logging.LogWithContext(
                                LogLevel.Information,
                                "Poll retrieved from network",
                                additionalData: new Dictionary<string, object>
                                {
                                    ["OptionsCount"] = result.Options.Count,
                                    ["Source"] = "Network"
                                });
                            
                            return result;
                        }
                    }

                    using var cacheOperation = _logging.BeginOperation("FetchFromCache");
                    var cachedPoll = await _databaseService.GetPollAsync(pollId);
                    
                    if (cachedPoll != null)
                    {
                        OperationContext.Current.SetData("PollTitle", cachedPoll.Title);
                        _logging.LogWithContext(
                            LogLevel.Information,
                            "Poll retrieved from cache",
                            additionalData: new Dictionary<string, object>
                            {
                                ["OptionsCount"] = cachedPoll.Options.Count,
                                ["Source"] = "Cache"
                            });
                    }
                    
                    return cachedPoll;
                });

                return poll;
            }
            catch (Exception ex)
            {
                _logging.LogWithContext(
                    LogLevel.Error,
                    "Failed to retrieve poll",
                    ex,
                    new Dictionary<string, object>
                    {
                        ["NetworkStatus"] = _connectivity.NetworkAccess
                    });
                throw;
            }
            finally
            {
                OperationContext.Reset();
            }
        }

        public async Task<bool> VoteAsync(Guid pollId, Guid optionId)
        {
            return await _errorHandler.ExecuteWithRetryAsync(async () =>
            {
                try
                {
                    var deviceId = await SecureStorage.GetAsync("device_id");
                    var success = await _commandService.VoteAsync(pollId, optionId, deviceId);

                    if (success)
                    {
                        await _databaseService.SaveVoteAsync(new LocalVote
                        {
                            PollId = pollId,
                            OptionId = optionId,
                            CreatedAt = DateTime.UtcNow,
                            IsSynced = true
                        });
                    }

                    return success;
                }
                catch (Exception ex)
                {
                    await _errorHandler.HandleExceptionAsync(ex, $"Vote: {pollId}/{optionId}");
                    throw;
                }
            });
        }

        // ... other facade methods
    }
} 