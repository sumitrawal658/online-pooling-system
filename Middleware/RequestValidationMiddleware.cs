using System.Text.RegularExpressions;

namespace PollSystem.Middleware
{
    public class RequestValidationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<RequestValidationMiddleware> _logger;

        public RequestValidationMiddleware(RequestDelegate next, ILogger<RequestValidationMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Validate Content-Type for POST requests
            if (HttpMethods.IsPost(context.Request.Method) && 
                !context.Request.HasJsonContentType())
            {
                context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
                await context.Response.WriteAsync("Unsupported Media Type. Please use application/json");
                return;
            }

            // Validate request size
            if (context.Request.ContentLength > 1024 * 1024) // 1MB limit
            {
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                await context.Response.WriteAsync("Request too large");
                return;
            }

            // Validate IP address
            var clientIp = context.Connection.RemoteIpAddress?.ToString();
            if (string.IsNullOrEmpty(clientIp) || !IsValidIpAddress(clientIp))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("Invalid IP address");
                return;
            }

            await _next(context);
        }

        private bool IsValidIpAddress(string ipAddress)
        {
            return Regex.IsMatch(ipAddress,
                @"^([0-9]{1,3}\.){3}[0-9]{1,3}$|^([0-9a-fA-F]{1,4}:){7}[0-9a-fA-F]{1,4}$");
        }
    }
} 