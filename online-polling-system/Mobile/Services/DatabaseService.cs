using SQLite;
using PollSystem.Mobile.Models;
using Microsoft.Extensions.Logging;
using System.Threading;

namespace PollSystem.Mobile.Services
{
    public class DatabaseService : BaseDisposableService, IDatabaseService
    {
        private readonly SQLiteAsyncConnection _database;
        private readonly SemaphoreSlim _semaphore;
        private bool _isInitialized;

        public DatabaseService(ILogger<DatabaseService> logger) : base(logger)
        {
            var dbPath = Path.Combine(FileSystem.AppDataDirectory, "polls.db");
            _database = new SQLiteAsyncConnection(dbPath, SQLiteOpenFlags.Create | SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.SharedCache);
            _semaphore = new SemaphoreSlim(1, 1);
        }

        protected override async ValueTask DisposeAsyncCore()
        {
            if (_database != null)
            {
                try
                {
                    await _database.CloseAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error closing database connection");
                }
            }
        }

        protected override void DisposeManagedResources()
        {
            _semaphore?.Dispose();
        }

        public async Task InitializeAsync()
        {
            ThrowIfDisposed();
            
            try
            {
                await _semaphore.WaitAsync();
                if (_isInitialized) return;

                await _database.CreateTablesAsync(CreateFlags.None,
                    typeof(Poll),
                    typeof(PollOption),
                    typeof(Vote));

                _isInitialized = true;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<List<Poll>> GetPollsAsync()
        {
            await InitializeAsync();
            return await _database.GetAllWithChildrenAsync<Poll>(recursive: true);
        }

        public async Task<Poll> GetPollAsync(Guid pollId)
        {
            await InitializeAsync();
            var poll = await _database.Table<Poll>()
                .FirstOrDefaultAsync(p => p.PollId == pollId);

            if (poll != null)
            {
                poll.Options = await _database.Table<PollOption>()
                    .Where(o => o.PollId == pollId)
                    .ToListAsync();
            }

            return poll;
        }

        public async Task SavePollAsync(Poll poll)
        {
            await InitializeAsync();
            await _database.RunInTransactionAsync(async (transaction) =>
            {
                await transaction.InsertOrReplaceAsync(poll);
                foreach (var option in poll.Options)
                {
                    await transaction.InsertOrReplaceAsync(option);
                }
            });
        }

        public async Task SavePollsAsync(IEnumerable<Poll> polls)
        {
            await InitializeAsync();
            await _database.RunInTransactionAsync(async (transaction) =>
            {
                foreach (var poll in polls)
                {
                    await transaction.InsertOrReplaceAsync(poll);
                    foreach (var option in poll.Options)
                    {
                        await transaction.InsertOrReplaceAsync(option);
                    }
                }
            });
        }

        public async Task SaveVoteAsync(LocalVote vote)
        {
            await InitializeAsync();
            if (vote.Id == 0)
                await _database.InsertAsync(vote);
            else
                await _database.UpdateAsync(vote);
        }

        public async Task<List<LocalVote>> GetUnsyncedVotesAsync()
        {
            await InitializeAsync();
            return await _database.Table<LocalVote>()
                .Where(v => !v.IsSynced)
                .OrderBy(v => v.CreatedAt)
                .ToListAsync();
        }

        public async Task<bool> HasVotedAsync(Guid pollId)
        {
            await InitializeAsync();
            var vote = await _database.Table<LocalVote>()
                .FirstOrDefaultAsync(v => v.PollId == pollId);
            return vote != null;
        }

        public async Task ClearOldDataAsync(int daysToKeep = 30)
        {
            await InitializeAsync();
            var cutoffDate = DateTime.UtcNow.AddDays(-daysToKeep);
            await _database.RunInTransactionAsync(async (transaction) =>
            {
                // Delete old synced votes
                await transaction.Table<LocalVote>()
                    .Where(v => v.IsSynced && v.CreatedAt < cutoffDate)
                    .DeleteAsync();

                // Delete old inactive polls
                var oldPolls = await transaction.Table<Poll>()
                    .Where(p => !p.IsActive && p.CreatedAt < cutoffDate)
                    .ToListAsync();

                foreach (var poll in oldPolls)
                {
                    await transaction.Table<PollOption>()
                        .Where(o => o.PollId == poll.PollId)
                        .DeleteAsync();
                    await transaction.DeleteAsync(poll);
                }
            });
        }

        public async Task CompactDatabaseAsync()
        {
            await _database.ExecuteAsync("VACUUM");
        }

        public async Task BackupDatabaseAsync(string backupPath)
        {
            if (File.Exists(backupPath))
                File.Delete(backupPath);

            var dbPath = _database.DatabasePath;
            File.Copy(dbPath, backupPath);
        }

        public async Task<int> GetDatabaseSizeAsync()
        {
            var info = new FileInfo(_database.DatabasePath);
            return (int)info.Length;
        }

        public async Task ClearAllDataAsync()
        {
            await _database.DropTableAsync<Vote>();
            await _database.DropTableAsync<PollOption>();
            await _database.DropTableAsync<Poll>();
            _isInitialized = false;
            await InitializeAsync();
        }

        public async Task<Poll> GetPollWithRelationsAsync(Guid pollId)
        {
            await InitializeAsync();
            return await _database.GetWithChildrenAsync<Poll>(pollId, recursive: true);
        }

        public async Task SavePollWithRelationsAsync(Poll poll)
        {
            await InitializeAsync();
            await _database.InsertOrReplaceWithChildrenAsync(poll, recursive: true);
        }

        public async Task DeletePollAsync(Guid pollId)
        {
            await InitializeAsync();
            var poll = await GetPollWithRelationsAsync(pollId);
            if (poll != null)
            {
                await _database.DeleteAsync(poll, recursive: true);
            }
        }

        public async Task<bool> HasVotedAsync(Guid pollId, string deviceId)
        {
            await InitializeAsync();
            return await _database.Table<Vote>()
                .Where(v => v.PollId == pollId && v.DeviceId == deviceId)
                .CountAsync() > 0;
        }

        public async Task<List<Vote>> GetVotesForPollAsync(Guid pollId)
        {
            await InitializeAsync();
            return await _database.Table<Vote>()
                .Where(v => v.PollId == pollId)
                .ToListAsync();
        }

        public async Task<Dictionary<Guid, int>> GetVoteCountsForPollAsync(Guid pollId)
        {
            await InitializeAsync();
            var votes = await _database.Table<Vote>()
                .Where(v => v.PollId == pollId)
                .ToListAsync();

            return votes.GroupBy(v => v.OptionId)
                .ToDictionary(g => g.Key, g => g.Count());
        }
    }
} 