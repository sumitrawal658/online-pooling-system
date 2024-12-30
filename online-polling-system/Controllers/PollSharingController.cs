using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OnlinePollingSystem.Services.Polls;
using OnlinePollingSystem.Models;

namespace OnlinePollingSystem.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PollSharingController : ControllerBase
    {
        private readonly IPollSharingService _sharingService;
        private readonly ILogger<PollSharingController> _logger;

        public PollSharingController(
            IPollSharingService sharingService,
            ILogger<PollSharingController> logger)
        {
            _sharingService = sharingService;
            _logger = logger;
        }

        [HttpPost("{pollId}/share")]
        [Authorize]
        public async Task<ActionResult<string>> SharePoll(int pollId, [FromBody] SharePollRequest request)
        {
            try
            {
                var userId = User.GetUserId();
                var shareUrl = await _sharingService.GenerateSharableLinkAsync(pollId, userId, request.ShareMethod);
                return Ok(new { shareUrl });
            }
            catch (NotFoundException)
            {
                return NotFound("Poll not found");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sharing poll {PollId}", pollId);
                return StatusCode(500, "An error occurred while sharing the poll");
            }
        }

        [HttpGet("validate/{shareCode}")]
        public async Task<ActionResult<bool>> ValidateShareLink(string shareCode)
        {
            try
            {
                var isValid = await _sharingService.ValidateSharableLinkAsync(shareCode);
                return Ok(isValid);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating share code {ShareCode}", shareCode);
                return StatusCode(500, "An error occurred while validating the share link");
            }
        }

        [HttpGet("{pollId}/stats")]
        [Authorize]
        public async Task<ActionResult<PollSharingStats>> GetSharingStats(int pollId)
        {
            try
            {
                var stats = await _sharingService.GetSharingStatsAsync(pollId);
                return Ok(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting sharing stats for poll {PollId}", pollId);
                return StatusCode(500, "An error occurred while getting sharing statistics");
            }
        }

        [HttpGet("{pollId}/recent")]
        [Authorize]
        public async Task<ActionResult<List<PollSharing>>> GetRecentShares(int pollId, [FromQuery] int count = 10)
        {
            try
            {
                var shares = await _sharingService.GetRecentSharesAsync(pollId, count);
                return Ok(shares);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting recent shares for poll {PollId}", pollId);
                return StatusCode(500, "An error occurred while getting recent shares");
            }
        }

        [HttpGet("{pollId}/methods")]
        [Authorize]
        public async Task<ActionResult<Dictionary<string, int>>> GetShareMethodStats(int pollId)
        {
            try
            {
                var stats = await _sharingService.GetShareMethodStatsAsync(pollId);
                return Ok(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting share method stats for poll {PollId}", pollId);
                return StatusCode(500, "An error occurred while getting share method statistics");
            }
        }
    }

    public class SharePollRequest
    {
        public string ShareMethod { get; set; }
    }
} 