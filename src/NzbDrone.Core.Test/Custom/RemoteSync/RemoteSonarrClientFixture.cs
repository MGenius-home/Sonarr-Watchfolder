using System;
using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Custom.RemoteSync;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.Custom.RemoteSync
{
    [TestFixture]
    public class RemoteSonarrClientFixture : CoreTest<RemoteSonarrClient>
    {
        [TearDown]
        public void TearDown()
        {
            Environment.SetEnvironmentVariable("SONARR_SOURCE_URL", null);
            Environment.SetEnvironmentVariable("SONARR_SOURCE_API_KEY", null);
        }

        [Test]
        public void should_return_empty_list_when_env_vars_are_missing()
        {
            Environment.SetEnvironmentVariable("SONARR_SOURCE_URL", null);
            Environment.SetEnvironmentVariable("SONARR_SOURCE_API_KEY", null);

            var result = Subject.GetSeries();

            result.Should().BeEmpty();
            Mocker.GetMock<IHttpClient>().Verify(c => c.Execute(It.IsAny<HttpRequest>()), Times.Never());
        }

        [Test]
        public void should_send_api_key_in_header_and_deserialize_series()
        {
            Environment.SetEnvironmentVariable("SONARR_SOURCE_URL", "http://sonarr:8989/");
            Environment.SetEnvironmentVariable("SONARR_SOURCE_API_KEY", "secret-key");

            HttpRequest captured = null;
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Execute(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(req => captured = req)
                .Returns((HttpRequest req) => new HttpResponse(
                    req,
                    new HttpHeader(),
                    "[{\"tvdbId\":123,\"title\":\"My Show\",\"titleSlug\":\"my-show\",\"qualityProfileId\":2,\"monitored\":true,\"seasonFolder\":false,\"seriesType\":\"anime\",\"added\":\"2026-08-01T00:00:00Z\"}]",
                    HttpStatusCode.OK));

            var result = Subject.GetSeries();

            captured.Should().NotBeNull();
            captured.Headers["X-Api-Key"].Should().Be("secret-key");
            captured.Url.FullUri.Should().Be("http://sonarr:8989/api/v3/series");
            captured.Url.FullUri.Should().NotContain("apikey");

            result.Should().HaveCount(1);
            result[0].TvdbId.Should().Be(123);
            result[0].SeriesType.Should().Be(SeriesTypes.Anime);
            result[0].SeasonFolder.Should().BeFalse();
        }

        [Test]
        public void should_return_empty_list_when_response_json_is_null()
        {
            Environment.SetEnvironmentVariable("SONARR_SOURCE_URL", "http://sonarr:8989");
            Environment.SetEnvironmentVariable("SONARR_SOURCE_API_KEY", "secret-key");

            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Execute(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest req) => new HttpResponse(req, new HttpHeader(), "null", HttpStatusCode.OK));

            var result = Subject.GetSeries();

            result.Should().NotBeNull().And.BeEmpty();
        }
    }
}
