using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace OnlinePollingSystem.Models.Validation
{
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class ShareMethodValidationAttribute : ValidationAttribute
    {
        private static readonly string[] ValidShareMethods = new[]
        {
            "direct_link",
            "email",
            "sms",
            "whatsapp",
            "telegram",
            "facebook",
            "twitter",
            "linkedin",
            "embed",
            "qr_code",
            "api"
        };

        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            if (value == null)
            {
                return new ValidationResult("Share method cannot be null.");
            }

            var shareMethod = value.ToString().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(shareMethod))
            {
                return new ValidationResult("Share method cannot be empty or whitespace.");
            }

            if (!ValidShareMethods.Contains(shareMethod))
            {
                return new ValidationResult(
                    $"Invalid share method. Valid methods are: {string.Join(", ", ValidShareMethods)}");
            }

            // Additional validation for specific share methods
            if (shareMethod == "api")
            {
                var entity = validationContext.ObjectInstance;
                var apiKeyProperty = entity.GetType().GetProperty("ApiKey");
                if (apiKeyProperty == null || apiKeyProperty.GetValue(entity) == null)
                {
                    return new ValidationResult("API key is required when using API share method.");
                }
            }

            if (shareMethod == "embed")
            {
                var entity = validationContext.ObjectInstance;
                var embedSettingsProperty = entity.GetType().GetProperty("EmbedSettings");
                if (embedSettingsProperty == null || embedSettingsProperty.GetValue(entity) == null)
                {
                    return new ValidationResult("Embed settings are required when using embed share method.");
                }
            }

            return ValidationResult.Success;
        }

        public static bool IsValidShareMethod(string shareMethod)
        {
            if (string.IsNullOrWhiteSpace(shareMethod))
            {
                return false;
            }

            return ValidShareMethods.Contains(shareMethod.ToLowerInvariant());
        }

        public static string[] GetValidShareMethods()
        {
            return ValidShareMethods.ToArray();
        }
    }
} 