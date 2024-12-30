using SQLite;
using PollSystem.Mobile.Models;

namespace PollSystem.Mobile.Services
{
    public class DatabaseInitializer
    {
        private readonly SQLiteAsyncConnection _database;
        private readonly ILogger<DatabaseInitializer> _logger;

        public DatabaseInitializer(SQLiteAsyncConnection database, ILogger<DatabaseInitializer> logger)
        {
            _database = database;
            _logger = logger;
        }

        public async Task InitializeAsync()
        {
            try
            {
                // Create tables
                await _database.CreateTablesAsync(CreateFlags.None,
                    typeof(Poll),
                    typeof(PollOption),
                    typeof(Vote));

                // Create indexes
                await CreateIndexesAsync();

                // Add any initial data if needed
                await SeedDataAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing database");
                throw;
            }
        }

        private async Task CreateIndexesAsync()
        {
            // Create composite index for unique votes per device and poll
            await _database.ExecuteAsync(@"
                CREATE UNIQUE INDEX IF NOT EXISTS IX_Votes_DevicePoll 
                ON Votes (DeviceId, PollId)");

            // Create index for poll options
            await _database.ExecuteAsync(@"
                CREATE INDEX IF NOT EXISTS IX_PollOptions_PollId 
                ON PollOptions (PollId)");

            // Create index for votes
            await _database.ExecuteAsync(@"
                CREATE INDEX IF NOT EXISTS IX_Votes_PollOption 
                ON Votes (PollId, OptionId)");

            // Create index for active polls
            await _database.ExecuteAsync(@"
                CREATE INDEX IF NOT EXISTS IX_Polls_Active 
                ON Polls (IsActive, EndDate)");
        }

        private async Task SeedDataAsync()
        {
            // Add any initial data if needed
            var count = await _database.Table<Poll>().CountAsync();
            if (count == 0)
            {
                // Example seed data for testing
                if (IsDevelopment())
                {
                    var poll = new Poll
                    {
                        PollId = Guid.NewGuid(),
                        Title = "Sample Poll",
                        StartDate = DateTime.UtcNow,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow,
                        IsSynced = true
                    };

                    await _database.InsertAsync(poll);

                    var options = new[]
                    {
                        new PollOption
                        {
                            OptionId = Guid.NewGuid(),
                            PollId = poll.PollId,
                            OptionText = "Option 1",
                            DisplayOrder = 0,
                            IsSynced = true
                        },
                        new PollOption
                        {
                            OptionId = Guid.NewGuid(),
                            PollId = poll.PollId,
                            OptionText = "Option 2",
                            DisplayOrder = 1,
                            IsSynced = true
                        }
                    };

                    await _database.InsertAllAsync(options);
                }
            }
        }

        private bool IsDevelopment()
        {
            #if DEBUG
                return true;
            #else
                return false;
            #endif
        }
    }
} 