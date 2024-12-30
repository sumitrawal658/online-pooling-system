using System;

namespace PollSystem.Exceptions
{
    public class PollNotFoundException : PollSystemException
    {
        public Guid PollId { get; }

        public PollNotFoundException(string message, Guid pollId)
            : base(message,
                new ErrorMetadata(
                    ErrorCategory.Business,
                    ErrorSeverity.Warning,
                    false,
                    "POLL_NOT_FOUND",
                    new Dictionary<string, object>
                    {
                        ["pollId"] = pollId
                    }))
        {
            PollId = pollId;
        }
    }

    public class DuplicateVoteException : PollSystemException
    {
        public Guid PollId { get; }
        public Guid UserId { get; }

        public DuplicateVoteException(string message, Guid pollId, Guid userId)
            : base(message,
                new ErrorMetadata(
                    ErrorCategory.Business,
                    ErrorSeverity.Warning,
                    false,
                    "DUPLICATE_VOTE",
                    new Dictionary<string, object>
                    {
                        ["pollId"] = pollId,
                        ["userId"] = userId
                    }))
        {
            PollId = pollId;
            UserId = userId;
        }
    }

    public class PollExpiredException : PollSystemException
    {
        public Guid PollId { get; }
        public DateTime EndDate { get; }

        public PollExpiredException(string message, Guid pollId, DateTime endDate)
            : base(message,
                new ErrorMetadata(
                    ErrorCategory.Business,
                    ErrorSeverity.Warning,
                    false,
                    "POLL_EXPIRED",
                    new Dictionary<string, object>
                    {
                        ["pollId"] = pollId,
                        ["endDate"] = endDate
                    }))
        {
            PollId = pollId;
            EndDate = endDate;
        }
    }
} 