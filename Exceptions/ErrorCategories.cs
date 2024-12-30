namespace PollSystem.Exceptions
{
    public enum ErrorCategory
    {
        Network,
        Database,
        Validation,
        Security,
        Business,
        System,
        UI
    }

    public enum ErrorSeverity
    {
        Critical,
        Error,
        Warning,
        Info
    }

    public class ErrorMetadata
    {
        public ErrorCategory Category { get; set; }
        public ErrorSeverity Severity { get; set; }
        public bool IsRetryable { get; set; }
        public string ErrorCode { get; set; }
        public Dictionary<string, object> Context { get; set; }

        public ErrorMetadata(
            ErrorCategory category,
            ErrorSeverity severity,
            bool isRetryable,
            string errorCode,
            Dictionary<string, object> context = null)
        {
            Category = category;
            Severity = severity;
            IsRetryable = isRetryable;
            ErrorCode = errorCode;
            Context = context ?? new Dictionary<string, object>();
        }
    }
} 