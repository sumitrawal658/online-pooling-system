using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlinePollingSystem.Models
{
    public class AdminActivity
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [ForeignKey("AdminUser")]
        public string AdminUserId { get; set; }

        [Required]
        public DateTime Timestamp { get; set; }

        [Required]
        [StringLength(50)]
        public string ActionType { get; set; }  // e.g., "DeletePoll", "ModerateComment", "UpdateSettings"

        [Required]
        [StringLength(255)]
        public string Description { get; set; }

        [StringLength(50)]
        public string EntityType { get; set; }  // e.g., "Poll", "Comment", "User"

        public string EntityId { get; set; }

        [StringLength(2048)]
        public string AdditionalData { get; set; }  // JSON string for any additional data

        public bool WasSuccessful { get; set; }

        [StringLength(1024)]
        public string ErrorMessage { get; set; }

        public string IpAddress { get; set; }
        public string UserAgent { get; set; }

        // Navigation property
        public virtual ApplicationUser AdminUser { get; set; }

        public AdminActivity()
        {
            Timestamp = DateTime.UtcNow;
            WasSuccessful = true;
        }

        public static class ActionTypes
        {
            public const string CreatePoll = "CreatePoll";
            public const string UpdatePoll = "UpdatePoll";
            public const string DeletePoll = "DeletePoll";
            public const string ModerateComment = "ModerateComment";
            public const string DeleteComment = "DeleteComment";
            public const string UpdateSettings = "UpdateSettings";
            public const string ManageUser = "ManageUser";
            public const string ViewDashboard = "ViewDashboard";
            public const string ExportData = "ExportData";
            public const string SystemConfig = "SystemConfig";
        }

        public static class EntityTypes
        {
            public const string Poll = "Poll";
            public const string Comment = "Comment";
            public const string User = "User";
            public const string Setting = "Setting";
            public const string System = "System";
            public const string Dashboard = "Dashboard";
        }
    }
} 