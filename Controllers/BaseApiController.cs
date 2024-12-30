using Microsoft.AspNetCore.Mvc;
using PollSystem.Exceptions;

namespace PollSystem.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public abstract class BaseApiController : ControllerBase
    {
        private readonly IErrorHandler _errorHandler;
        protected readonly ILogger _logger;

        protected BaseApiController(IErrorHandler errorHandler, ILogger logger)
        {
            _errorHandler = errorHandler;
            _logger = logger;
        }

        protected async Task<ActionResult<T>> ExecuteAsync<T>(Func<Task<T>> action, string context = null)
        {
            try
            {
                return await action();
            }
            catch (ValidationException ex)
            {
                await _errorHandler.HandleExceptionAsync(ex, context);
                return BadRequest(new ErrorResponse(ex.ErrorCode, ex.Message, ex.ErrorData));
            }
            catch (PollNotFoundException ex)
            {
                await _errorHandler.HandleExceptionAsync(ex, context);
                return NotFound(new ErrorResponse(ex.ErrorCode, ex.Message, ex.ErrorData));
            }
            catch (Exception ex)
            {
                await _errorHandler.HandleExceptionAsync(ex, context);
                throw; // Let the middleware handle it
            }
        }

        protected async Task<ActionResult> ExecuteAsync(Func<Task> action, string context = null)
        {
            try
            {
                await action();
                return Ok();
            }
            catch (Exception ex)
            {
                await _errorHandler.HandleExceptionAsync(ex, context);
                throw; // Let the middleware handle it
            }
        }
    }
} 