using System;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace OnlinePollingSystem.Models.Validation
{
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class ShareCodeValidationAttribute : ValidationAttribute
    {
        private const int RequiredLength = 8;
        private static readonly Regex ValidCharactersRegex = new Regex(@"^[A-Za-z0-9_-]{8}$", RegexOptions.Compiled);
        private static readonly string[] RestrictedWords = new[]
        {
            "admin", "root", "system", "test", "demo", "sample",
            "password", "login", "user", "guest"
        };

        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            if (value == null)
            {
                return new ValidationResult("Share code cannot be null.");
            }

            var shareCode = value.ToString();

            if (string.IsNullOrWhiteSpace(shareCode))
            {
                return new ValidationResult("Share code cannot be empty or whitespace.");
            }

            if (shareCode.Length != RequiredLength)
            {
                return new ValidationResult($"Share code must be exactly {RequiredLength} characters long.");
            }

            if (!ValidCharactersRegex.IsMatch(shareCode))
            {
                return new ValidationResult("Share code can only contain letters, numbers, underscores, and hyphens.");
            }

            // Check for restricted words
            if (ContainsRestrictedWord(shareCode))
            {
                return new ValidationResult("Share code contains a restricted word.");
            }

            // Check for sequential or repeated characters
            if (HasSequentialOrRepeatedCharacters(shareCode))
            {
                return new ValidationResult("Share code cannot contain sequential or repeated characters.");
            }

            return ValidationResult.Success;
        }

        private bool ContainsRestrictedWord(string shareCode)
        {
            var lowerCode = shareCode.ToLowerInvariant();
            return RestrictedWords.Any(word => lowerCode.Contains(word));
        }

        private bool HasSequentialOrRepeatedCharacters(string shareCode)
        {
            // Check for 3 or more repeated characters
            for (int i = 0; i < shareCode.Length - 2; i++)
            {
                if (shareCode[i] == shareCode[i + 1] && shareCode[i] == shareCode[i + 2])
                {
                    return true;
                }
            }

            // Check for sequential characters (both ascending and descending)
            for (int i = 0; i < shareCode.Length - 2; i++)
            {
                if ((shareCode[i + 1] == shareCode[i] + 1 && shareCode[i + 2] == shareCode[i] + 2) ||
                    (shareCode[i + 1] == shareCode[i] - 1 && shareCode[i + 2] == shareCode[i] - 2))
                {
                    return true;
                }
            }

            return false;
        }
    }
} 