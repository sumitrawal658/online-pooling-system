using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PollSystem.Models
{
    public class PollOption
    {
        [Key]
        public Guid OptionId { get; set; }

        public Guid PollId { get; set; }

        [Required]
        [MaxLength(200)]
        public string OptionText { get; set; }

        public int DisplayOrder { get; set; }

        public DateTime CreatedAt { get; set; }

        [ForeignKey("PollId")]
        public virtual Poll Poll { get; set; }

        public virtual ICollection<Vote> Votes { get; set; }
    }
} 