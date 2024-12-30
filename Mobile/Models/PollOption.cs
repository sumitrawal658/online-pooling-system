using SQLite;
using SQLiteNetExtensions.Attributes;

namespace PollSystem.Mobile.Models
{
    [Table("PollOptions")]
    public class PollOption
    {
        [PrimaryKey]
        public Guid OptionId { get; set; }

        [ForeignKey(typeof(Poll))]
        public Guid PollId { get; set; }

        [MaxLength(200)]
        public string OptionText { get; set; }

        public int VoteCount { get; set; }

        public int DisplayOrder { get; set; }

        // Many-to-One relationship with Poll
        [ManyToOne]
        public Poll Poll { get; set; }

        // One-to-Many relationship with Votes
        [OneToMany(CascadeOperations = CascadeOperation.All)]
        public List<Vote> Votes { get; set; }

        // Local tracking
        public bool IsSynced { get; set; }
    }
} 