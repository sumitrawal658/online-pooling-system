using System.ComponentModel.DataAnnotations;

namespace PollSystem.Models
{
    public class User
    {
        [Key]
        public Guid UserId { get; set; }

        [MaxLength(45)]
        public string IpAddress { get; set; }

        public bool IsAdmin { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime LastActivityAt { get; set; }

        public virtual ICollection<Poll> CreatedPolls { get; set; }
        public virtual ICollection<Vote> Votes { get; set; }
        public virtual ICollection<Comment> Comments { get; set; }
    }
} 