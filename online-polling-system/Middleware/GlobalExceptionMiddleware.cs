using System.Net;
using System.Text.Json;
using PollSystem.Exceptions;

namespace PollSystem.Middleware
{
    public class GlobalExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<GlobalExceptionMiddleware> _logger;
        private readonly IHostEnvironment _environment;

        public GlobalExceptionMiddleware(
            RequestDelegate next,
            ILogger<GlobalExceptionMiddleware> logger,
            IHostEnvironment environment)
        {
            _next = next;
            _logger = logger;
            _environment = environment;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(context, ex);
            }
        }

        private async Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            var (statusCode, errorResponse) = CreateErrorResponse(exception);
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)statusCode;

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(errorResponse, jsonOptions));
        }

        private (HttpStatusCode statusCode, ErrorResponse response) CreateErrorResponse(Exception exception)
        {
            _logger.LogError(exception, "An unhandled exception occurred");

            return exception switch
            {
                ValidationException validationEx => (
                    HttpStatusCode.BadRequest,
                    new ErrorResponse(
                        validationEx.ErrorCode,
                        validationEx.Message,
                        new Dictionary<string, object>
                        {
                            { "field", validationEx.Field },
                            { "attemptedValue", validationEx.AttemptedValue }
                        })),

                PollNotFoundException notFoundEx => (
                    HttpStatusCode.NotFound,
                    new ErrorResponse(
                        notFoundEx.ErrorCode,
                        notFoundEx.Message,
                        new Dictionary<string, object>
                        {
                            { "pollId", notFoundEx.PollId }
                        })),

                DuplicateVoteException duplicateEx => (
                    HttpStatusCode.Conflict,
                    new ErrorResponse(
                        duplicateEx.ErrorCode,
                        duplicateEx.Message,
                        new Dictionary<string, object>
                        {
                            { "pollId", duplicateEx.PollId },
                            { "userId", duplicateEx.UserId }
                        })),

                PollExpiredException expiredEx => (
                    HttpStatusCode.Gone,
                    new ErrorResponse(
                        expiredEx.ErrorCode,
                        expiredEx.Message,
                        new Dictionary<string, object>
                        {
                            { "pollId", expiredEx.PollId },
                            { "endDate", expiredEx.EndDate }
                        })),

                _ => (
                    HttpStatusCode.InternalServerError,
                    new ErrorResponse(
                        "INTERNAL_SERVER_ERROR",
                        _environment.IsDevelopment() ? exception.Message : "An unexpected error occurred",
                        _environment.IsDevelopment() ? new Dictionary<string, object>
                        {
                            { "stackTrace", exception.StackTrace },
                            { "type", exception.GetType().Name }
                        } : null))
            };
        }
    }

    public record ErrorResponse(
        string ErrorCode,
        string Message,
        Dictionary<string, object> Details = null);
} 