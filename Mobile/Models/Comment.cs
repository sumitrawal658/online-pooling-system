using SQLite;
using SQLiteNetExtensions.Attributes;

namespace PollSystem.Mobile.Models
{
    [Table("Comments")]
    public class Comment
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [ForeignKey(typeof(Poll))]
        public Guid PollId { get; set; }

        public string Content { get; set; }

        public string AuthorName { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public bool IsDeleted { get; set; }

        // Local tracking
        public bool IsSynced { get; set; }

        [ManyToOne]
        public Poll Poll { get; set; }
    }
} 