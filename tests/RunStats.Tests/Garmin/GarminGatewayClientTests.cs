using System.Net;
using System.Net.Http.Headers;
using System.Text;
using RunStats.Garmin;

namespace RunStats.Tests.Garmin;

public class GarminGatewayClientTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    readonly StubHandler handler = new();
    readonly GarminGatewayClient client;

    public GarminGatewayClientTests() =>
        client = new GarminGatewayClient(new HttpClient(handler) { BaseAddress = new Uri("http://gateway:8080/") });

    sealed class StubHandler : HttpMessageHandler
    {
        public Dictionary<string, Func<HttpResponseMessage>> Responses { get; } = [];
        public List<string> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            Requested.Add(path);
            return Task.FromResult(Responses.TryGetValue(path, out var response) ? response() : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    static Func<HttpResponseMessage> Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        () => new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Activities_are_requested_by_page_and_parsed()
    {
        handler.Responses["/activities?start=100&limit=50"] = Json("""
            [{"activityId":7,"activityName":"Run","startTimeLocal":"2026-10-25 10:30:00","startTimeGMT":"2026-10-25 09:30:00",
              "activityType":{"typeId":1,"typeKey":"running","parentTypeId":17},"distance":5000,"duration":1500}]
            """);

        var activities = await client.GetActivitiesAsync(100, 50, Ct);

        Assert.Equal(7, Assert.Single(activities).Id);
    }

    [Fact]
    public async Task Missing_activity_list_is_an_error()
    {
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetActivitiesAsync(0, 100, Ct));

        Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
    }

    [Fact]
    public async Task Activity_detail_is_returned_as_it_came()
    {
        handler.Responses["/activities/7"] = Json("""{"activityId":7,"summaryDTO":{}}""");

        Assert.Equal("""{"activityId":7,"summaryDTO":{}}""", await client.GetActivityAsync(7, Ct));
    }

    [Fact]
    public async Task Unknown_activity_is_an_error()
    {
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetActivityAsync(7, Ct));

        Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
        Assert.Contains("activity 7", error.Message);
    }

    [Fact]
    public async Task Splits_heart_rate_zones_and_weather_come_from_their_endpoints()
    {
        handler.Responses["/activities/7/splits"] = Json("""{"lapDTOs":[]}""");
        handler.Responses["/activities/7/hr-zones"] = Json("[]");
        handler.Responses["/activities/7/weather"] = Json("""{"temp":12}""");

        Assert.Equal("""{"lapDTOs":[]}""", await client.GetSplitsAsync(7, Ct));
        Assert.Equal("[]", await client.GetHeartRateZonesAsync(7, Ct));
        Assert.Equal("""{"temp":12}""", await client.GetWeatherAsync(7, Ct));
    }

    [Fact]
    public async Task Missing_or_null_optional_data_is_null()
    {
        handler.Responses["/activities/7/weather"] = Json(" null ");

        Assert.Null(await client.GetWeatherAsync(7, Ct));
        Assert.Null(await client.GetSplitsAsync(7, Ct));
        Assert.Null(await client.GetFitFileAsync(7, Ct));
    }

    [Fact]
    public async Task Fit_file_comes_back_as_bytes()
    {
        byte[] fit = [0x0E, 0x10, 0x2E, 0x46, 0x49, 0x54];
        handler.Responses["/activities/7/fit"] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(fit) };

        Assert.Equal(fit, await client.GetFitFileAsync(7, Ct));
    }

    [Fact]
    public async Task Service_unavailable_says_why_and_when_to_retry()
    {
        handler.Responses["/activities/7"] = () =>
        {
            var response = Json("""{"error":"Garmin rate limit reached"}""", HttpStatusCode.ServiceUnavailable)();
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(287));
            return response;
        };

        var error = await Assert.ThrowsAsync<GarminUnavailableException>(() => client.GetActivityAsync(7, Ct));

        Assert.Equal("Garmin rate limit reached", error.Reason);
        Assert.Equal(TimeSpan.FromSeconds(287), error.RetryAfter);
        Assert.Equal("Garmin is unavailable: Garmin rate limit reached (retry after 287 s)", error.Message);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"detail":"x"}""")]
    [InlineData("[]")]
    public async Task Service_unavailable_without_a_reason(string body)
    {
        handler.Responses["/activities/7"] = Json(body, HttpStatusCode.ServiceUnavailable);

        var error = await Assert.ThrowsAsync<GarminUnavailableException>(() => client.GetActivityAsync(7, Ct));

        Assert.Equal("no reason given", error.Reason);
        Assert.Null(error.RetryAfter);
        Assert.Equal("Garmin is unavailable: no reason given", error.Message);
    }

    [Fact]
    public async Task Other_errors_fail_with_their_status()
    {
        handler.Responses["/activities/7/fit"] = Json("""{"error":"Garmin Connect request failed"}""", HttpStatusCode.BadGateway);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetFitFileAsync(7, Ct));

        Assert.Equal(HttpStatusCode.BadGateway, error.StatusCode);
    }
}
