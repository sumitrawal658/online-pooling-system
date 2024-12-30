using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PollSystem.Models
{
    public class Poll
    {
        [Key]
        public Guid PollId { get; set; }

        [Required]
        [MaxLength(200)]
        public string Title { get; set; }

        public Guid CreatedBy { get; set; }

        [Required]
        public DateTime StartDate { get; set; }

        public DateTime? EndDate { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        public bool IsActive { get; set; }

        [ForeignKey("CreatedBy")]
        public virtual User Creator { get; set; }

        public virtual ICollection<PollOption> Options { get; set; }
        public virtual ICollection<Vote> Votes { get; set; }
        public virtual ICollection<Comment> Comments { get; set; }
    }
}

public class PollOption
{
    public Guid OptionId { get; set; }
    public Guid PollId { get; set; }
    public string OptionText { get; set; }
    public int VoteCount { get; set; }
}

public class CreatePollRequest
{
    public string Title { get; set; }
    public List<string> Options { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}

public class VoteRequest
{
    public Guid PollId { get; set; }
    public Guid OptionId { get; set; }
} 