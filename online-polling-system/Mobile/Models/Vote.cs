using SQLite;
using SQLiteNetExtensions.Attributes;

namespace PollSystem.Mobile.Models
{
    [Table("Votes")]
    public class Vote
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [ForeignKey(typeof(Poll))]
        public Guid PollId { get; set; }

        [ForeignKey(typeof(PollOption))]
        public Guid OptionId { get; set; }

        public string DeviceId { get; set; }

        public DateTime CreatedAt { get; set; }

        // Many-to-One relationships
        [ManyToOne]
        public Poll Poll { get; set; }

        [ManyToOne]
        public PollOption Option { get; set; }

        // Local tracking
        public bool IsSynced { get; set; }
    }
} 