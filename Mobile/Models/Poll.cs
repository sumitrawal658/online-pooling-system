using SQLite;
using SQLiteNetExtensions.Attributes;

namespace PollSystem.Mobile.Models
{
    [Table("Polls")]
    public class Poll
    {
        [PrimaryKey]
        public Guid PollId { get; set; }

        [MaxLength(200)]
        public string Title { get; set; }

        public DateTime StartDate { get; set; }

        public DateTime? EndDate { get; set; }

        public DateTime CreatedAt { get; set; }

        public bool IsActive { get; set; }

        // One-to-Many relationship with PollOptions
        [OneToMany(CascadeOperations = CascadeOperation.All)]
        public List<PollOption> Options { get; set; }

        // One-to-Many relationship with Votes
        [OneToMany(CascadeOperations = CascadeOperation.All)]
        public List<Vote> Votes { get; set; }

        // Local tracking
        public bool IsSynced { get; set; }
        
        public DateTime LastSyncedAt { get; set; }
    }

    public class PollOption
    {
        [PrimaryKey]
        public Guid OptionId { get; set; }
        public Guid PollId { get; set; }
        public string OptionText { get; set; }
        public int VoteCount { get; set; }
    }

    public class LocalVote
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        public Guid PollId { get; set; }
        public Guid OptionId { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsSynced { get; set; }
    }
} 