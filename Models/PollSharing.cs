using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using OnlinePollingSystem.Models.Validation;

namespace OnlinePollingSystem.Models
{
    public class PollSharing
    {
        [Key]
        public int Id { get; set; }

        [Required(ErrorMessage = "Poll ID is required.")]
        [ForeignKey("Poll")]
        public int PollId { get; set; }

        [Required(ErrorMessage = "Share code is required.")]
        [ShareCodeValidation]
        public string ShareCode { get; set; }

        [Required(ErrorMessage = "Share method is required.")]
        [ShareMethodValidation]
        public string ShareMethod { get; set; }

        [Required(ErrorMessage = "Creation date is required.")]
        [DataType(DataType.DateTime)]
        public DateTime CreatedAt { get; set; }

        [Required(ErrorMessage = "User ID is required.")]
        [ForeignKey("SharedByUser")]
        public string SharedByUserId { get; set; }

        [Required]
        [Range(0, int.MaxValue, ErrorMessage = "Access count must be a non-negative number.")]
        public int AccessCount { get; set; }

        [DataType(DataType.DateTime)]
        public DateTime? LastAccessedAt { get; set; }

        [Required]
        public bool IsActive { get; set; } = true;

        [DataType(DataType.DateTime)]
        [FutureDate(ErrorMessage = "Expiration date must be in the future.")]
        public DateTime? ExpiresAt { get; set; }

        [StringLength(500, ErrorMessage = "Custom message cannot exceed 500 characters.")]
        public string CustomMessage { get; set; }

        [Required(ErrorMessage = "Share URL is required.")]
        [UrlValidation(requireHttps: true)]
        public string ShareUrl { get; set; }

        // Tracking properties
        [StringLength(45, ErrorMessage = "IP address cannot exceed 45 characters.")]
        [RegularExpression(@"^(?:[0-9]{1,3}\.){3}[0-9]{1,3}$|^([0-9a-fA-F]{1,4}:){7}[0-9a-fA-F]{1,4}$", 
            ErrorMessage = "Invalid IP address format.")]
        public string LastAccessedByIp { get; set; }

        [StringLength(256, ErrorMessage = "User agent string cannot exceed 256 characters.")]
        public string LastAccessedUserAgent { get; set; }

        [StringLength(2048, ErrorMessage = "Referrer URL cannot exceed 2048 characters.")]
        [UrlValidation(requireHttps: false)]
        public string LastAccessedReferrer { get; set; }

        // Statistics
        [Range(0, int.MaxValue, ErrorMessage = "Unique visitors must be a non-negative number.")]
        public int UniqueVisitors { get; set; }

        [Range(0, int.MaxValue, ErrorMessage = "Conversion count must be a non-negative number.")]
        public int ConversionCount { get; set; }

        // Navigation properties
        public virtual Poll Poll { get; set; }
        public virtual ApplicationUser SharedByUser { get; set; }

        // Computed properties
        [NotMapped]
        public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value < DateTime.UtcNow;

        [NotMapped]
        public double ConversionRate => AccessCount > 0 ? (double)ConversionCount / AccessCount * 100 : 0;

        // Validation methods
        public bool IsValidForSharing()
        {
            if (!IsActive) return false;
            if (IsExpired) return false;
            if (Poll?.IsExpired ?? true) return false;
            return true;
        }
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class FutureDateAttribute : ValidationAttribute
    {
        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            if (value == null) return ValidationResult.Success;

            var date = (DateTime)value;
            return date > DateTime.UtcNow 
                ? ValidationResult.Success 
                : new ValidationResult("Date must be in the future.");
        }
    }

    public static class ShareMethods
    {
        public const string Direct = "DIRECT";
        public const string Email = "EMAIL";
        public const string Facebook = "FACEBOOK";
        public const string Twitter = "TWITTER";
        public const string WhatsApp = "WHATSAPP";
        public const string Telegram = "TELEGRAM";
        public const string LinkedIn = "LINKEDIN";
        public const string Reddit = "REDDIT";
        public const string QRCode = "QRCODE";

        public static bool IsValid(string method)
        {
            return method switch
            {
                Direct => true,
                Email => true,
                Facebook => true,
                Twitter => true,
                WhatsApp => true,
                Telegram => true,
                LinkedIn => true,
                Reddit => true,
                QRCode => true,
                _ => false
            };
        }

        public static string GetShareMethodDisplayName(string method)
        {
            return method switch
            {
                Direct => "Direct Link",
                Email => "Email Share",
                Facebook => "Facebook",
                Twitter => "Twitter",
                WhatsApp => "WhatsApp",
                Telegram => "Telegram",
                LinkedIn => "LinkedIn",
                Reddit => "Reddit",
                QRCode => "QR Code",
                _ => "Unknown"
            };
        }

        public static string GetShareMethodIcon(string method)
        {
            return method switch
            {
                Direct => "🔗",
                Email => "📧",
                Facebook => "👥",
                Twitter => "🐦",
                WhatsApp => "📱",
                Telegram => "📬",
                LinkedIn => "💼",
                Reddit => "👽",
                QRCode => "📱",
                _ => "❓"
            };
        }
    }

    public class ShareAccessLog
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [ForeignKey("PollSharing")]
        public int PollSharingId { get; set; }

        [Required]
        public DateTime AccessedAt { get; set; }

        public string AccessedByIp { get; set; }
        public string UserAgent { get; set; }
        public string Referrer { get; set; }
        public string Country { get; set; }
        public string City { get; set; }

        public bool ResultedInVote { get; set; }
        public DateTime? VotedAt { get; set; }

        // Navigation property
        public virtual PollSharing PollSharing { get; set; }
    }
} 