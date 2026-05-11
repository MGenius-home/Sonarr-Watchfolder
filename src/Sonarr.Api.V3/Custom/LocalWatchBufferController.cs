using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Custom.LocalIngestor;
using NzbDrone.Core.Custom.LocalIngestor.Models;
using Sonarr.Http.REST.Attributes;

namespace Sonarr.Api.V3.Custom
{
    [V3ApiController]
    public class LocalWatchBufferController : Controller
    {
        private readonly ILocalWatchBufferRepository _repository;

        public LocalWatchBufferController(ILocalWatchBufferRepository repository)
        {
            _repository = repository;
        }

        [HttpGet]
        public List<LocalWatchBuffer> GetUnmapped()
        {
            return _repository.All()
                              .Where(c => c.Status == LocalWatchStatus.Unmapped)
                              .ToList();
        }
    }
}
