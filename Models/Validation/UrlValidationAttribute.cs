using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace OnlinePollingSystem.Models.Validation
{
    /// <summary>
    /// Validates URLs with configurable options for security and format requirements.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class UrlValidationAttribute : ValidationAttribute
    {
        private readonly int _maxLength;
        private readonly bool _requireHttps;
        private readonly bool _allowLocalhost;
        private readonly bool _allowQueryParams;
        private readonly bool _allowFragments;
        private readonly string[] _allowedDomains;
        private readonly string[] _blockedDomains;
        private readonly bool _allowSubdomains;
        private readonly bool _isUserSubmission;
        private readonly bool _validateSensitiveParams;
        
        private static readonly Regex IpAddressRegex = new Regex(
            @"^(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)$",
            RegexOptions.Compiled);

        private static readonly string[] SensitiveParamNames = new[]
        {
            // Authentication related
            "password", "passwd", "pwd", "pass",
            "token", "auth", "authentication",
            "apikey", "api_key", "api-key",
            "access_token", "accesstoken",
            "refresh_token", "refreshtoken",
            "id_token", "idtoken",
            "session", "sessionid", "session-id",
            
            // Security related
            "secret", "private", "secure",
            "key", "salt", "hash",
            "credentials", "creds",
            "signature", "sign", "digest",
            
            // Personal information
            "ssn", "social", "social-security",
            "dob", "birthdate", "birth-date",
            "pin", "passcode", "security-code",
            
            // Financial information
            "card", "cc", "cvv", "cvc",
            "account", "acct", "routing",
            "invoice", "payment", "transaction",
            
            // OAuth related
            "client_secret", "client-secret",
            "code_verifier", "code-challenge",
            "state", "nonce", "callback"
        };

        private static readonly Regex SensitiveValuePattern = new Regex(
            @"(?i)(bearer\s+|basic\s+|token[=:]\s*|key[=:]\s*|password[=:]\s*|secret[=:]\s*)[a-z0-9+/=_\-]{8,}",
            RegexOptions.Compiled);

        /// <summary>
        /// Initializes a new instance of the UrlValidationAttribute with specified configuration options.
        /// </summary>
        public UrlValidationAttribute(
            int maxLength = 2048,
            bool requireHttps = true,
            bool allowLocalhost = false,
            bool allowQueryParams = true,
            bool allowFragments = true,
            string[] allowedDomains = null,
            string[] blockedDomains = null,
            bool allowSubdomains = true,
            bool isUserSubmission = false,
            bool validateSensitiveParams = true)
        {
            _maxLength = maxLength;
            _requireHttps = requireHttps;
            _allowLocalhost = allowLocalhost;
            _allowQueryParams = allowQueryParams;
            _allowFragments = allowFragments;
            _allowedDomains = allowedDomains?.Select(d => d.ToLowerInvariant()).ToArray();
            _blockedDomains = blockedDomains?.Select(d => d.ToLowerInvariant()).ToArray();
            _allowSubdomains = allowSubdomains;
            _isUserSubmission = isUserSubmission;
            _validateSensitiveParams = validateSensitiveParams;
        }

        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            if (value == null)
            {
                return new ValidationResult("URL cannot be null.");
            }

            var url = value.ToString().Trim();

            if (string.IsNullOrWhiteSpace(url))
            {
                return new ValidationResult("URL cannot be empty or whitespace.");
            }

            if (url.Length > _maxLength)
            {
                return new ValidationResult($"URL cannot be longer than {_maxLength} characters.");
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uriResult))
            {
                return new ValidationResult("Invalid URL format. Please provide a valid absolute URL.");
            }

            // Always enforce HTTPS for user submissions
            if ((_requireHttps || _isUserSubmission) && uriResult.Scheme != Uri.UriSchemeHttps)
            {
                return new ValidationResult("URL must use HTTPS for security.");
            }

            // Stricter validation for user submissions
            if (_isUserSubmission)
            {
                var userSubmissionResult = ValidateUserSubmission(uriResult);
                if (!userSubmissionResult.IsValid)
                {
                    return new ValidationResult(userSubmissionResult.ErrorMessage);
                }
            }

            // Localhost validation
            if (!_allowLocalhost && (
                uriResult.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                uriResult.Host.Equals("127.0.0.1") ||
                uriResult.Host.Equals("::1")))
            {
                return new ValidationResult("Localhost URLs are not allowed.");
            }

            // IP address validation
            if (IpAddressRegex.IsMatch(uriResult.Host))
            {
                return new ValidationResult("Direct IP addresses are not allowed in URLs. Please use a domain name.");
            }

            // Query parameters validation
            if (!_allowQueryParams && !string.IsNullOrEmpty(uriResult.Query))
            {
                return new ValidationResult("Query parameters are not allowed in this URL.");
            }

            // Sensitive parameters validation
            if (_validateSensitiveParams && !string.IsNullOrEmpty(uriResult.Query))
            {
                var sensitiveParamResult = ValidateSensitiveParameters(uriResult);
                if (!sensitiveParamResult.IsValid)
                {
                    return new ValidationResult(sensitiveParamResult.ErrorMessage);
                }
            }

            // Fragment validation
            if (!_allowFragments && !string.IsNullOrEmpty(uriResult.Fragment))
            {
                return new ValidationResult("URL fragments (hashtags) are not allowed.");
            }

            // Domain validation
            var domainValidationResult = ValidateDomain(uriResult.Host);
            if (!domainValidationResult.IsValid)
            {
                return new ValidationResult(domainValidationResult.ErrorMessage);
            }

            // Check for potentially malicious content
            var maliciousCheckResult = CheckForMaliciousContent(url);
            if (!maliciousCheckResult.IsValid)
            {
                return new ValidationResult(maliciousCheckResult.ErrorMessage);
            }

            return ValidationResult.Success;
        }

        private (bool IsValid, string ErrorMessage) ValidateUserSubmission(Uri uri)
        {
            // Enforce maximum length for user submissions
            if (uri.ToString().Length > 1024)
            {
                return (false, "User submitted URLs cannot exceed 1024 characters.");
            }

            // Check for suspicious URL patterns
            var suspiciousPatterns = new[]
            {
                "redirect", "return_to", "return_url", "goto", "next",
                "target", "redir", "destination", "dest", "jump"
            };

            var queryParams = System.Web.HttpUtility.ParseQueryString(uri.Query);
            foreach (string key in queryParams.Keys)
            {
                if (suspiciousPatterns.Any(pattern => 
                    key.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return (false, "URL contains suspicious redirect parameters.");
                }
            }

            // Validate path length
            if (uri.AbsolutePath.Length > 255)
            {
                return (false, "URL path is too long.");
            }

            // Validate number of query parameters
            if (queryParams.Count > 10)
            {
                return (false, "URL contains too many query parameters.");
            }

            // Validate query parameter lengths
            foreach (string key in queryParams.Keys)
            {
                if (key.Length > 50 || queryParams[key]?.Length > 200)
                {
                    return (false, "URL contains query parameters that are too long.");
                }
            }

            return (true, string.Empty);
        }

        private (bool IsValid, string ErrorMessage) ValidateSensitiveParameters(Uri uri)
        {
            try
            {
                var queryParams = System.Web.HttpUtility.ParseQueryString(uri.Query);
                
                // Check parameter names
                foreach (string key in queryParams.Keys)
                {
                    // Check for sensitive parameter names
                    if (SensitiveParamNames.Any(sensitive => 
                        key.IndexOf(sensitive, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        return (false, $"URL contains sensitive parameter name: {key}");
                    }

                    // Check parameter values for sensitive patterns
                    string value = queryParams[key];
                    if (!string.IsNullOrEmpty(value))
                    {
                        // Check for potential token patterns
                        if (SensitiveValuePattern.IsMatch(value))
                        {
                            return (false, "URL contains what appears to be a sensitive token or key.");
                        }

                        // Check for Base64 encoded values that might contain sensitive data
                        if (IsLikelyBase64(value) && value.Length > 20)
                        {
                            return (false, "URL contains suspicious Base64 encoded data.");
                        }

                        // Check for common sensitive value patterns
                        if (ContainsSensitiveValuePatterns(value))
                        {
                            return (false, "URL contains sensitive information in parameter values.");
                        }
                    }
                }

                // Check URL path for sensitive information
                string path = uri.AbsolutePath.ToLowerInvariant();
                if (SensitiveParamNames.Any(sensitive => path.Contains(sensitive)))
                {
                    return (false, "URL path contains sensitive information.");
                }

                // Check for sensitive information in fragments
                if (!string.IsNullOrEmpty(uri.Fragment))
                {
                    string fragment = uri.Fragment.TrimStart('#');
                    if (SensitiveParamNames.Any(sensitive => 
                        fragment.IndexOf(sensitive, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        return (false, "URL fragment contains sensitive information.");
                    }
                }

                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                // Log the exception if you have logging configured
                return (false, "Error validating URL parameters.");
            }
        }

        private bool IsLikelyBase64(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;

            // Check if the string matches base64 pattern
            bool matchesPattern = Regex.IsMatch(value, @"^[a-zA-Z0-9+/]*={0,2}$");
            
            // Additional check: length should be multiple of 4
            bool validLength = value.Length % 4 == 0;
            
            return matchesPattern && validLength;
        }

        private bool ContainsSensitiveValuePatterns(string value)
        {
            // Check for credit card number pattern
            if (Regex.IsMatch(value, @"\b\d{4}[- ]?\d{4}[- ]?\d{4}[- ]?\d{4}\b"))
                return true;

            // Check for SSN pattern
            if (Regex.IsMatch(value, @"\b\d{3}[-]?\d{2}[-]?\d{4}\b"))
                return true;

            // Check for date of birth pattern
            if (Regex.IsMatch(value, @"\b\d{2}[-/]\d{2}[-/]\d{4}\b"))
                return true;

            // Check for common password patterns
            if (Regex.IsMatch(value, @"(?i)pass.*=|auth.*=|secret.*="))
                return true;

            return false;
        }

        private (bool IsValid, string ErrorMessage) ValidateDomain(string domain)
        {
            domain = domain.ToLowerInvariant();

            // Basic domain format validation
            if (!IsValidDomainFormat(domain))
            {
                return (false, "Invalid domain name format.");
            }

            // Check blocked domains
            if (_blockedDomains != null)
            {
                if (_blockedDomains.Any(blocked =>
                    domain.Equals(blocked) || (_allowSubdomains && domain.EndsWith($".{blocked}"))))
                {
                    return (false, "This domain is not allowed.");
                }
            }

            // Check allowed domains
            if (_allowedDomains != null && _allowedDomains.Length > 0)
            {
                bool isAllowed = _allowedDomains.Any(allowed =>
                    domain.Equals(allowed) || (_allowSubdomains && domain.EndsWith($".{allowed}")));

                if (!isAllowed)
                {
                    return (false, "This domain is not in the list of allowed domains.");
                }
            }

            return (true, string.Empty);
        }

        private bool IsValidDomainFormat(string domain)
        {
            // Basic domain validation rules
            if (domain.Length > 255) return false;
            
            // Check each domain part
            var parts = domain.Split('.');
            if (parts.Length < 2) return false;

            foreach (var part in parts)
            {
                if (part.Length > 63 || part.Length == 0) return false;
                if (!part.All(c => char.IsLetterOrDigit(c) || c == '-')) return false;
                if (part.StartsWith("-") || part.EndsWith("-")) return false;
            }

            // Ensure the top-level domain is not all numeric
            var tld = parts[parts.Length - 1];
            return !tld.All(char.IsDigit);
        }

        private (bool IsValid, string ErrorMessage) CheckForMaliciousContent(string url)
        {
            var maliciousPatterns = new[]
            {
                // Script injection patterns
                "javascript:", "data:", "vbscript:", "file:",
                "<script", "</script>", "onclick=", "onerror=",
                "onload=", "onmouseover=", "onfocus=", "onblur=",
                
                // Encoded script patterns
                "%3Cscript", "%3C/script%3E", "%3Cimg",
                "\\x3Cscript", "\\x3C/script\\x3E",
                
                // SQL injection patterns
                "union+select", "union/**/select", "union all select",
                
                // Shell command injection
                "|", "||", ";", "&&", "`", "$(",
                
                // Path traversal
                "../", "..\\", "%2e%2e%2f", "%252e%252e%252f"
            };

            foreach (var pattern in maliciousPatterns)
            {
                if (url.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return (false, $"URL contains potentially unsafe content: {pattern}");
                }
            }

            return (true, string.Empty);
        }
    }
} 