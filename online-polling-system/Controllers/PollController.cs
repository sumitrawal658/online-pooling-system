using Microsoft.AspNetCore.Mvc;
using PollSystem.Models;
using PollSystem.Services;
using PollSystem.Exceptions;

namespace PollSystem.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PollController : BaseApiController
    {
        private readonly IPollService _pollService;

        public PollController(
            IPollService pollService,
            IErrorHandler errorHandler,
            ILogger<PollController> logger)
            : base(errorHandler, logger)
        {
            _pollService = pollService;
        }

        [HttpGet("{id}")]
        public Task<ActionResult<Poll>> GetPoll(Guid id)
        {
            return ExecuteAsync(
                async () => await _pollService.GetPollAsync(id),
                $"GetPoll: {id}");
        }

        [HttpPost("{pollId}/vote")]
        public Task<ActionResult<bool>> Vote(Guid pollId, [FromBody] VoteRequest request)
        {
            return ExecuteAsync(
                async () => await _pollService.VoteAsync(pollId, request.OptionId, request.VoterId),
                $"Vote: {pollId}");
        }
    }
} 