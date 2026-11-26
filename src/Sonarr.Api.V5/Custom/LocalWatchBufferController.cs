using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Custom.LocalIngestor;
using NzbDrone.Core.Custom.LocalIngestor.Models;
using NzbDrone.Core.Messaging.Commands;
using Sonarr.Http;

namespace Sonarr.Api.V5.Custom
{
    [V5ApiController("localwatchbuffer")]
    public class LocalWatchBufferController : Controller
    {
        private readonly ILocalWatchBufferRepository _repository;
        private readonly IManageCommandQueue _commandQueueManager;

        public LocalWatchBufferController(ILocalWatchBufferRepository repository,
                                          IManageCommandQueue commandQueueManager)
        {
            _repository = repository;
            _commandQueueManager = commandQueueManager;
        }

        [HttpGet]
        public ActionResult<List<LocalWatchBuffer>> GetItems([FromQuery] string? status = null)
        {
            var items = _repository.All();

            if (string.IsNullOrWhiteSpace(status))
            {
                items = items.Where(c => c.Status == LocalWatchStatus.Unmapped || c.Status == LocalWatchStatus.Failed);
            }
            else if (!string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
            {
                if (!Enum.TryParse<LocalWatchStatus>(status, true, out var parsedStatus))
                {
                    return BadRequest($"Invalid status '{status}'");
                }

                items = items.Where(c => c.Status == parsedStatus);
            }

            return items.OrderByDescending(c => c.LastSeen).ToList();
        }

        [HttpPost("{id:int}/retry")]
        public ActionResult<LocalWatchBuffer> Retry(int id)
        {
            var entry = _repository.Find(id);
            if (entry == null)
            {
                return NotFound();
            }

            entry.Status = LocalWatchStatus.Pending;
            entry.Failures = 0;
            entry.Reason = "Queued for retry";
            _repository.Update(entry);

            return Ok(entry);
        }

        [HttpPost("{id:int}/ignore")]
        public ActionResult<LocalWatchBuffer> Ignore(int id)
        {
            var entry = _repository.Find(id);
            if (entry == null)
            {
                return NotFound();
            }

            entry.Status = LocalWatchStatus.Ignored;
            entry.Reason = "Ignored by user";
            _repository.Update(entry);

            return Ok(entry);
        }

        [HttpPost("scan")]
        public IActionResult TriggerScan()
        {
            _commandQueueManager.Push(new LocalWatchScanCommand());
            return Accepted(new { queued = true });
        }
    }
}
