using Microsoft.EntityFrameworkCore;
using PollSystem.Data;
using PollSystem.Models;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using PollSystem.Exceptions;
using System.Diagnostics;

namespace PollSystem.Services
{
    public class PollService : IPollService
    {
        private readonly PollDbContext _context;
        private readonly ILogger<PollService> _logger;
        private readonly IHubContext<PollHub> _hubContext;
        private readonly GroupManager _groupManager;
        private readonly IErrorHandler _errorHandler;
        private readonly IMetricsCollector _metricsCollector;

        public PollService(
            PollDbContext context, 
            ILogger<PollService> logger,
            IHubContext<PollHub> hubContext,
            GroupManager groupManager,
            IErrorHandler errorHandler,
            IMetricsCollector metricsCollector)
        {
            _context = context;
            _logger = logger;
            _hubContext = hubContext;
            _groupManager = groupManager;
            _errorHandler = errorHandler;
            _metricsCollector = metricsCollector;
        }

        private async Task<T> TrackPerformanceAsync<T>(Func<Task<T>> operation, string operationName)
        {
            var stopwatch = Stopwatch.StartNew();
            var success = false;

            try
            {
                var result = await operation();
                success = true;
                return result;
            }
            finally
            {
                stopwatch.Stop();
                var duration = stopwatch.Elapsed;

                // Record metrics
                _metricsCollector.RecordOperationDuration(operationName, duration);
                _metricsCollector.IncrementOperationCounter(operationName, success);

                // Log performance data
                var logLevel = duration.TotalMilliseconds > 1000 ? LogLevel.Warning : LogLevel.Information;
                _logger.Log(logLevel,
                    "Operation {OperationName} completed in {Duration}ms (Success: {Success})",
                    operationName,
                    duration.TotalMilliseconds,
                    success);

                // Record detailed metrics if operation was slow
                if (duration.TotalMilliseconds > 1000)
                {
                    _metricsCollector.RecordSlowOperation(new SlowOperationData
                    {
                        OperationName = operationName,
                        Duration = duration,
                        Timestamp = DateTime.UtcNow,
                        Success = success,
                        Context = new Dictionary<string, object>
                        {
                            ["threadId"] = Environment.CurrentManagedThreadId,
                            ["memoryUsed"] = GC.GetTotalMemory(false)
                        }
                    });
                }
            }
        }

        private async Task<T> ExecuteWithTrackingAsync<T>(Func<Task<T>> operation, string operationName)
        {
            return await TrackPerformanceAsync(async () =>
            {
                return await ExecuteWithErrorHandlingAsync(operation, operationName);
            }, operationName);
        }

        private async Task<T> ExecuteWithErrorHandlingAsync<T>(Func<Task<T>> operation, string context)
        {
            try
            {
                return await operation();
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, $"Database error in {context}");
                throw new DatabaseException(
                    "Failed to update database",
                    DatabaseOperation.Write,
                    ex);
            }
            catch (Exception ex) when (ex is not PollSystemException)
            {
                _logger.LogError(ex, $"Unexpected error in {context}");
                throw new PollSystemException(
                    "An unexpected error occurred",
                    new ErrorMetadata(
                        ErrorCategory.System,
                        ErrorSeverity.Error,
                        true,
                        "UNEXPECTED_ERROR",
                        new Dictionary<string, object>
                        {
                            ["operation"] = context,
                            ["errorType"] = ex.GetType().Name
                        }),
                    ex);
            }
        }

        public async Task<Poll> CreatePollAsync(CreatePollRequest request, string creatorIp)
        {
            return await ExecuteWithTrackingAsync(async () =>
            {
                using var scope = _metricsCollector.CreateMetricsScope("CreatePoll");
                scope.AddTag("optionsCount", request.Options.Count);

                // Get or create user by IP
                var user = await GetOrCreateUserAsync(creatorIp);
                scope.AddTag("userId", user.UserId);

                var poll = new Poll
                {
                    PollId = Guid.NewGuid(),
                    Title = request.Title,
                    CreatedBy = user.UserId,
                    StartDate = request.StartDate,
                    EndDate = request.EndDate,
                    IsActive = true,
                    Options = request.Options.Select((text, index) => new PollOption
                    {
                        OptionId = Guid.NewGuid(),
                        OptionText = text,
                        DisplayOrder = index
                    }).ToList()
                };

                _context.Polls.Add(poll);
                await _context.SaveChangesAsync();
                scope.AddTag("pollId", poll.PollId);

                return await GetPollAsync(poll.PollId);
            }, "CreatePoll");
        }

        public async Task<Poll> GetPollAsync(Guid pollId)
        {
            return await ExecuteWithTrackingAsync(async () =>
            {
                using var scope = _metricsCollector.CreateMetricsScope("GetPoll");
                scope.AddTag("pollId", pollId);

                var poll = await _context.Polls
                    .Include(p => p.Options)
                    .ThenInclude(o => o.Votes)
                    .FirstOrDefaultAsync(p => p.PollId == pollId);

                if (poll == null)
                {
                    scope.AddTag("found", false);
                    throw new PollNotFoundException($"Poll with ID {pollId} not found", pollId);
                }

                scope.AddTag("found", true);
                scope.AddTag("optionsCount", poll.Options.Count);
                scope.AddTag("votesCount", poll.Options.Sum(o => o.Votes.Count));

                return poll;
            }, $"GetPoll:{pollId}");
        }

        public async Task<List<Poll>> GetActivePollsAsync()
        {
            return await ExecuteWithTrackingAsync(async () =>
            {
                using var scope = _metricsCollector.CreateMetricsScope("GetActivePolls");

                var polls = await _context.Polls
                    .Include(p => p.Options)
                    .ThenInclude(o => o.Votes)
                    .Where(p => p.IsActive && 
                        (p.EndDate == null || p.EndDate > DateTime.UtcNow))
                    .OrderByDescending(p => p.CreatedAt)
                    .ToListAsync();

                scope.AddTag("pollCount", polls.Count);
                scope.AddTag("totalVotes", polls.Sum(p => p.Options.Sum(o => o.Votes.Count)));

                return polls;
            }, "GetActivePolls");
        }

        public async Task<bool> VoteAsync(Guid pollId, Guid optionId, string voterIp)
        {
            return await ExecuteWithTrackingAsync(async () =>
            {
                using var scope = _metricsCollector.CreateMetricsScope("Vote");
                scope.AddTag("pollId", pollId);
                scope.AddTag("optionId", optionId);

                var poll = await _context.Polls
                    .Include(p => p.Options)
                    .FirstOrDefaultAsync(p => p.PollId == pollId);

                if (poll == null)
                {
                    scope.AddTag("error", "PollNotFound");
                    throw new PollNotFoundException(
                        $"Poll with ID {pollId} not found",
                        pollId);
                }

                if (!poll.IsActive)
                {
                    scope.AddTag("error", "PollInactive");
                    throw new BusinessException(
                        "Poll is not active",
                        "POLL_INACTIVE",
                        ErrorSeverity.Warning,
                        new Dictionary<string, object>
                        {
                            ["pollId"] = pollId,
                            ["status"] = "inactive"
                        });
                }

                if (poll.EndDate.HasValue && poll.EndDate < DateTime.UtcNow)
                {
                    scope.AddTag("error", "PollExpired");
                    throw new PollExpiredException(
                        "Poll has expired",
                        pollId,
                        poll.EndDate.Value);
                }

                var user = await GetOrCreateUserAsync(voterIp);
                scope.AddTag("userId", user.UserId);

                // Check if user already voted
                var existingVote = await _context.Votes
                    .FirstOrDefaultAsync(v => v.PollId == pollId && v.UserId == user.UserId);

                if (existingVote != null)
                {
                    scope.AddTag("error", "DuplicateVote");
                    throw new DuplicateVoteException(
                        "User has already voted in this poll",
                        pollId,
                        user.UserId);
                }

                var transactionStopwatch = Stopwatch.StartNew();
                try
                {
                    await _context.Database.BeginTransactionAsync();

                    // Record vote
                    var vote = new Vote
                    {
                        VoteId = Guid.NewGuid(),
                        PollId = pollId,
                        OptionId = optionId,
                        UserId = user.UserId,
                        IpAddress = voterIp
                    };

                    _context.Votes.Add(vote);

                    // Update vote count efficiently
                    await _context.Database.ExecuteSqlRawAsync(
                        "UPDATE PollOptions SET VoteCount = VoteCount + 1 WHERE OptionId = @p0",
                        optionId);

                    await _context.SaveChangesAsync();
                    await _context.Database.CommitTransactionAsync();

                    // Record transaction duration
                    scope.AddMetric("transactionDuration", transactionStopwatch.Elapsed.TotalMilliseconds);

                    // Only notify clients who are actually subscribed
                    var connections = _groupManager.GetConnectionIds(pollId.ToString());
                    if (connections.Any())
                    {
                        var notificationStopwatch = Stopwatch.StartNew();
                        var updatedPoll = await GetPollAsync(pollId);
                        await _hubContext.Clients.Groups(pollId.ToString())
                            .SendAsync("PollUpdated", updatedPoll);
                        scope.AddMetric("notificationDuration", notificationStopwatch.Elapsed.TotalMilliseconds);
                        scope.AddTag("notifiedClients", connections.Count);
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    await _context.Database.RollbackTransactionAsync();
                    scope.AddTag("error", "DatabaseError");
                    scope.AddTag("errorType", ex.GetType().Name);
                    throw new DatabaseException(
                        "Failed to process vote",
                        DatabaseOperation.Write,
                        ex);
                }
                finally
                {
                    transactionStopwatch.Stop();
                }
            }, $"Vote:{pollId}");
        }

        public async Task<bool> DeletePollAsync(Guid pollId, string adminIp)
        {
            return await ExecuteWithErrorHandlingAsync(async () =>
            {
                var admin = await _context.Users
                    .FirstOrDefaultAsync(u => u.IpAddress == adminIp && u.IsAdmin);

                if (admin == null)
                {
                    throw new SecurityException(
                        "Unauthorized access",
                        "UNAUTHORIZED_ACCESS",
                        new Dictionary<string, object>
                        {
                            ["pollId"] = pollId,
                            ["ipAddress"] = adminIp
                        });
                }

                var poll = await _context.Polls.FindAsync(pollId);
                if (poll == null)
                {
                    throw new PollNotFoundException($"Poll with ID {pollId} not found", pollId);
                }

                poll.IsActive = false;
                await _context.SaveChangesAsync();

                return true;
            }, $"DeletePoll: {pollId}");
        }

        private async Task<User> GetOrCreateUserAsync(string ipAddress)
        {
            return await ExecuteWithErrorHandlingAsync(async () =>
            {
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.IpAddress == ipAddress);

                if (user == null)
                {
                    user = new User
                    {
                        UserId = Guid.NewGuid(),
                        IpAddress = ipAddress,
                        LastActivityAt = DateTime.UtcNow
                    };
                    _context.Users.Add(user);
                    await _context.SaveChangesAsync();
                }
                else
                {
                    user.LastActivityAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                }

                return user;
            }, $"GetOrCreateUser: {ipAddress}");
        }
    }
} 