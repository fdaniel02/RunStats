using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RunStats.Notion;

namespace RunStats.Tests.Notion;

public class NotionRacesTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    sealed class StubHandler : HttpMessageHandler
    {
        public Queue<HttpResponseMessage> Responses { get; } = new();
        public List<(HttpRequestMessage Request, JsonElement Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request, JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement));
            return Responses.Dequeue();
        }
    }

    readonly StubHandler handler = new();

    NotionRaces Client(string? token = "secret", string? dataSource = "source-id") => new(
        new HttpClient(handler) { BaseAddress = new Uri("https://notion.test/v1/") },
        Options.Create(new NotionSettings { Token = token, DataSourceId = dataSource }));

    static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    static string Page(string id, string name) => $$$"""
        {"id":"{{{id}}}","properties":{
          "Race":{"type":"title","title":[{"plain_text":"{{{name}}}"}]},
          "Datum":{"type":"date","date":{"start":"2027-05-09","end":null} } } }
        """;

    [Theory]
    [InlineData("secret", "source-id", true)]
    [InlineData(null, "source-id", false)]
    [InlineData("secret", " ", false)]
    public void It_needs_both_settings(string? token, string? dataSource, bool configured) =>
        Assert.Equal(configured, Client(token, dataSource).IsConfigured);

    [Fact]
    public async Task It_asks_for_races_from_a_date_with_the_token()
    {
        handler.Responses.Enqueue(Json("""{"results":[],"has_more":false,"next_cursor":null}"""));

        Assert.Empty(await Client().GetFromAsync(new DateOnly(2026, 11, 5), Ct));

        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/v1/data_sources/source-id/query", request.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer secret", request.Headers.Authorization!.ToString());
        var filter = body.GetProperty("filter");
        Assert.Equal("Datum", filter.GetProperty("property").GetString());
        Assert.Equal("2026-11-05", filter.GetProperty("date").GetProperty("on_or_after").GetString());
        Assert.False(body.TryGetProperty("start_cursor", out _));
    }

    [Fact]
    public async Task It_follows_the_pages_and_leaves_out_rows_that_cant_be_listed()
    {
        var first = "11111111-2222-4333-8444-555555555555";
        var second = "66666666-7777-4888-8999-aaaaaaaaaaaa";
        handler.Responses.Enqueue(Json($$$"""{"results":[{{{Page(first, "Zandvoort")}}},{"id":"{{{second}}}","properties":{}}],"has_more":true,"next_cursor":"next"}"""));
        handler.Responses.Enqueue(Json($$"""{"results":[{{Page(second, "Mudrun")}}],"has_more":false,"next_cursor":null}"""));

        var races = await Client().GetFromAsync(new DateOnly(2026, 11, 5), Ct);

        Assert.Equal(["Zandvoort", "Mudrun"], races.Select(r => r.Name));
        Assert.Equal("next", handler.Requests[1].Body.GetProperty("start_cursor").GetString());
    }

    [Fact]
    public async Task An_error_from_Notion_is_an_exception_without_the_token()
    {
        handler.Responses.Enqueue(Json("""{"object":"error","code":"unauthorized"}""", HttpStatusCode.Unauthorized));

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => Client().GetFromAsync(new DateOnly(2026, 11, 5), Ct));

        Assert.DoesNotContain("secret", error.Message, StringComparison.Ordinal);
    }
}
