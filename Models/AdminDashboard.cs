using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlinePollingSystem.Models
{
    public class AdminDashboard
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public DateTime LastUpdated { get; set; }

        // Poll Statistics
        public int TotalPolls { get; set; }
        public int ActivePolls { get; set; }
        public int ExpiredPolls { get; set; }
        public int TotalVotes { get; set; }
        public int TodayVotes { get; set; }

        // User Engagement
        public int UniqueVoters { get; set; }
        public int AverageVotesPerPoll { get; set; }
        public double AverageOptionsPerPoll { get; set; }

        // Time-based Stats
        public int PollsCreatedToday { get; set; }
        public int PollsExpiringToday { get; set; }
        public int PollsExpiredToday { get; set; }

        // Sharing Stats
        public int TotalShares { get; set; }
        public Dictionary<string, int> SharesByMethod { get; set; }
        public int UniqueShareClicks { get; set; }

        // Comment Stats
        public int TotalComments { get; set; }
        public int PendingModeration { get; set; }
        public int FlaggedComments { get; set; }

        // System Health
        public double AverageResponseTime { get; set; }
        public int ErrorCount24h { get; set; }
        public double SystemUptime { get; set; }

        // Most Active
        [NotMapped]
        public List<Poll> TopPolls { get; set; }
        
        [NotMapped]
        public List<string> MostActiveUsers { get; set; }

        // Audit
        [Required]
        public DateTime CreatedAt { get; set; }
        
        [Required]
        [ForeignKey("AdminUser")]
        public string AdminUserId { get; set; }
        
        public virtual ApplicationUser AdminUser { get; set; }

        public AdminDashboard()
        {
            LastUpdated = DateTime.UtcNow;
            CreatedAt = DateTime.UtcNow;
            SharesByMethod = new Dictionary<string, int>();
            TopPolls = new List<Poll>();
            MostActiveUsers = new List<string>();
        }
    }

    public class AdminDashboardSettings
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public bool AutoRefresh { get; set; } = true;

        [Range(15, 3600)]
        public int RefreshIntervalSeconds { get; set; } = 300;

        public bool ShowSystemHealth { get; set; } = true;
        public bool ShowUserEngagement { get; set; } = true;
        public bool ShowCommentStats { get; set; } = true;
        public bool ShowShareStats { get; set; } = true;

        [Range(5, 50)]
        public int TopPollsCount { get; set; } = 10;

        [Range(5, 50)]
        public int MostActiveUsersCount { get; set; } = 10;

        [Required]
        [ForeignKey("AdminUser")]
        public string AdminUserId { get; set; }
        
        public virtual ApplicationUser AdminUser { get; set; }
    }
} 