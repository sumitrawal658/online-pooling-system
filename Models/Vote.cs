using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PollSystem.Models
{
    public class Vote
    {
        [Key]
        public Guid VoteId { get; set; }

        public Guid PollId { get; set; }

        public Guid OptionId { get; set; }

        public Guid UserId { get; set; }

        [Required]
        [MaxLength(45)]
        public string IpAddress { get; set; }

        public DateTime CreatedAt { get; set; }

        [ForeignKey("PollId")]
        public virtual Poll Poll { get; set; }

        [ForeignKey("OptionId")]
        public virtual PollOption Option { get; set; }

        [ForeignKey("UserId")]
        public virtual User User { get; set; }
    }
} 