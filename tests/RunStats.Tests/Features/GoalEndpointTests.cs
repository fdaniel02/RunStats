using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RunStats.Data;
using RunStats.Garmin;
using RunStats.Tests.Data;
using RunStats.Tests.Sync;

namespace RunStats.Tests.Features;

// The whole app: the year page's form, the antiforgery check and the database together.
[Collection("SqlServer")]
public sealed partial class GoalEndpointTests : IDisposable
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    readonly WebApplicationFactory<Program> factory;
    readonly HttpClient client;

    public GoalEndpointTests(SqlServerFixture sqlServer)
    {
        var connectionString = sqlServer.ConnectionString(SqlServerFixture.NewDatabaseName());
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseEnvironment("Production")
            .UseSetting("ConnectionStrings:Default", connectionString)
            .ConfigureServices(s =>
            {
                s.RemoveAll<IGarminGateway>();
                s.AddSingleton<IGarminGateway>(new FakeGarminGateway());
            }));
        client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
    }

    int ThisYear => factory.Services.GetRequiredService<Clock>().Today.Year;

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();

    // The year page sets the antiforgery cookie on the client and puts the matching token in its forms.
    async Task<string> TokenAsync()
    {
        var page = await client.GetStringAsync($"/years/{ThisYear}", Ct);
        return TokenPattern().Match(page).Groups[1].Value;
    }

    Task<HttpResponseMessage> PostAsync(int year, params (string Name, string Value)[] fields) =>
        client.PostAsync($"/years/{year}/goal", new FormUrlEncodedContent(fields.Select(f => KeyValuePair.Create(f.Name, f.Value))), Ct);

    async Task<double?> GoalAsync(int year)
    {
        await using var db = await factory.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(Ct);
        return (await db.YearGoals.FindAsync([year], Ct))?.DistanceMeters;
    }

    [Fact]
    public async Task Saving_sets_and_changes_the_goal_and_goes_back_to_the_year()
    {
        var token = await TokenAsync();

        var response = await PostAsync(ThisYear, ("__RequestVerificationToken", token), ("km", "1500"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/years/{ThisYear}", response.Headers.Location!.ToString());
        Assert.Equal(1_500_000, await GoalAsync(ThisYear));

        await PostAsync(ThisYear, ("__RequestVerificationToken", token), ("km", "1250.5"));
        Assert.Equal(1_250_500, await GoalAsync(ThisYear));
        Assert.Contains("of 1250.5 km", await client.GetStringAsync($"/years/{ThisYear}", Ct));
    }

    [Fact]
    public async Task Removing_deletes_the_goal()
    {
        var token = await TokenAsync();
        await PostAsync(ThisYear, ("__RequestVerificationToken", token), ("km", "1500"));

        var response = await PostAsync(ThisYear, ("__RequestVerificationToken", token), ("remove", "true"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null(await GoalAsync(ThisYear));
    }

    [Fact]
    public async Task Without_the_antiforgery_token_nothing_is_saved()
    {
        await TokenAsync();

        var response = await PostAsync(ThisYear, ("km", "1500"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(await GoalAsync(ThisYear));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("20001")]
    [InlineData("")]
    public async Task A_distance_out_of_range_is_rejected(string km)
    {
        var token = await TokenAsync();

        var response = await PostAsync(ThisYear, ("__RequestVerificationToken", token), ("km", km));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(await GoalAsync(ThisYear));
    }

    [Fact]
    public async Task A_year_far_off_is_not_found()
    {
        var token = await TokenAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(1999, ("__RequestVerificationToken", token), ("km", "1500"))).StatusCode);
    }
}
