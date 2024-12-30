namespace PollSystem.Exceptions
{
    public class PollSystemException : Exception
    {
        public ErrorMetadata Metadata { get; }

        public PollSystemException(
            string message,
            ErrorMetadata metadata,
            Exception innerException = null)
            : base(message, innerException)
        {
            Metadata = metadata;
        }

        public virtual bool IsRetryable => Metadata.IsRetryable;
        public virtual ErrorCategory Category => Metadata.Category;
        public virtual ErrorSeverity Severity => Metadata.Severity;
    }

    public class NetworkException : PollSystemException
    {
        public NetworkStatus Status { get; }

        public NetworkException(
            string message,
            NetworkStatus status,
            Exception innerException = null)
            : base(message,
                new ErrorMetadata(
                    ErrorCategory.Network,
                    GetSeverityFromStatus(status),
                    IsStatusRetryable(status),
                    $"NETWORK_{status.ToString().ToUpper()}",
                    new Dictionary<string, object>
                    {
                        ["NetworkStatus"] = status
                    }),
                innerException)
        {
            Status = status;
        }

        private static ErrorSeverity GetSeverityFromStatus(NetworkStatus status) => status switch
        {
            NetworkStatus.ServerError => ErrorSeverity.Critical,
            NetworkStatus.ClientError => ErrorSeverity.Error,
            NetworkStatus.Timeout => ErrorSeverity.Warning,
            _ => ErrorSeverity.Error
        };

        private static bool IsStatusRetryable(NetworkStatus status) => status switch
        {
            NetworkStatus.ServerError => true,
            NetworkStatus.Timeout => true,
            NetworkStatus.NoConnection => true,
            _ => false
        };
    }

    public class DatabaseException : PollSystemException
    {
        public DatabaseOperation Operation { get; }

        public DatabaseException(
            string message,
            DatabaseOperation operation,
            Exception innerException = null)
            : base(message,
                new ErrorMetadata(
                    ErrorCategory.Database,
                    GetSeverityFromOperation(operation),
                    IsOperationRetryable(operation),
                    $"DB_{operation.ToString().ToUpper()}",
                    new Dictionary<string, object>
                    {
                        ["Operation"] = operation
                    }),
                innerException)
        {
            Operation = operation;
        }

        private static ErrorSeverity GetSeverityFromOperation(DatabaseOperation operation) => operation switch
        {
            DatabaseOperation.Initialize => ErrorSeverity.Critical,
            DatabaseOperation.Write => ErrorSeverity.Error,
            DatabaseOperation.Read => ErrorSeverity.Warning,
            _ => ErrorSeverity.Error
        };

        private static bool IsOperationRetryable(DatabaseOperation operation) => operation switch
        {
            DatabaseOperation.Read => true,
            DatabaseOperation.Write => true,
            _ => false
        };
    }

    public class BusinessException : PollSystemException
    {
        public BusinessException(
            string message,
            string errorCode,
            ErrorSeverity severity = ErrorSeverity.Warning,
            Dictionary<string, object> context = null)
            : base(message,
                new ErrorMetadata(
                    ErrorCategory.Business,
                    severity,
                    false,
                    errorCode,
                    context),
                null)
        {
        }
    }

    public class SecurityException : PollSystemException
    {
        public SecurityException(
            string message,
            string errorCode,
            Dictionary<string, object> context = null)
            : base(message,
                new ErrorMetadata(
                    ErrorCategory.Security,
                    ErrorSeverity.Critical,
                    false,
                    errorCode,
                    context),
                null)
        {
        }
    }

    public class UIException : PollSystemException
    {
        public UIException(
            string message,
            string errorCode,
            Dictionary<string, object> context = null)
            : base(message,
                new ErrorMetadata(
                    ErrorCategory.UI,
                    ErrorSeverity.Warning,
                    false,
                    errorCode,
                    context),
                null)
        {
        }
    }
} 