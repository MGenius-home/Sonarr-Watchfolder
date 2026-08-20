using System;
using System.Collections.Generic;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;

namespace NzbDrone.Core.Custom.RemoteSync
{
    public interface IRemoteSonarrClient
    {
        List<RemoteSeries> GetSeries();
    }

    public class RemoteSonarrClient : IRemoteSonarrClient
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        public RemoteSonarrClient(IHttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public List<RemoteSeries> GetSeries()
        {
            var baseUrl = Environment.GetEnvironmentVariable("SONARR_SOURCE_URL");
            var apiKey = Environment.GetEnvironmentVariable("SONARR_SOURCE_API_KEY");

            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.Warn("Remote sync skipped: SONARR_SOURCE_URL or SONARR_SOURCE_API_KEY not set");
                return new List<RemoteSeries>();
            }

            var request = new HttpRequestBuilder(baseUrl.TrimEnd('/'))
                .Resource("/api/v3/series")
                .AddQueryParam("apikey", apiKey)
                .Build();

            request.LogHttpError = true;
            request.SuppressHttpError = false;

            var response = _httpClient.Execute(request);

            return Json.Deserialize<List<RemoteSeries>>(response.Content);
        }
    }
}
