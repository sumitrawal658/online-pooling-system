namespace PollSystem.Extensions
{
    public static class ExceptionExtensions
    {
        public static PollSystemException ToPollSystemException(this Exception ex)
        {
            return ex switch
            {
                HttpRequestException httpEx => new NetworkException(
                    httpEx.Message,
                    GetNetworkStatus(httpEx.StatusCode),
                    httpEx),

                SQLiteException sqlEx => new DatabaseException(
                    sqlEx.Message,
                    GetDatabaseOperation(sqlEx.Result),
                    sqlEx),

                TaskCanceledException => new NetworkException(
                    "Operation timed out",
                    NetworkStatus.Timeout),

                PollSystemException => ex as PollSystemException,

                _ => new PollSystemException(
                    ex.Message,
                    "UNEXPECTED_ERROR",
                    new Dictionary<string, object>
                    {
                        { "ExceptionType", ex.GetType().Name }
                    },
                    ex)
            };

            static NetworkStatus GetNetworkStatus(HttpStatusCode? statusCode) => statusCode switch
            {
                HttpStatusCode.NotFound => NetworkStatus.ClientError,
                HttpStatusCode.BadRequest => NetworkStatus.ClientError,
                HttpStatusCode.InternalServerError => NetworkStatus.ServerError,
                HttpStatusCode.ServiceUnavailable => NetworkStatus.ServerError,
                HttpStatusCode.GatewayTimeout => NetworkStatus.Timeout,
                _ => NetworkStatus.Unknown
            };

            static DatabaseOperation GetDatabaseOperation(SQLite3.Result result) => result switch
            {
                SQLite3.Result.Error => DatabaseOperation.Unknown,
                SQLite3.Result.Busy => DatabaseOperation.Write,
                SQLite3.Result.Constraint => DatabaseOperation.Write,
                SQLite3.Result.NotFound => DatabaseOperation.Read,
                _ => DatabaseOperation.Unknown
            };
        }
    }
} 