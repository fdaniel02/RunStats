using System.Net;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RunStats.Data;
using RunStats.Tests.Data;

namespace RunStats.Tests;

// Two app instances on one database stand in for the pod before and after a restart.
[Collection("SqlServer")]
public sealed class DataProtectionTests : IDisposable
{
    readonly WebApplicationFactory<Program> before;
    readonly WebApplicationFactory<Program> after;

    public DataProtectionTests(SqlServerFixture sqlServer)
    {
        var connectionString = sqlServer.ConnectionString(SqlServerFixture.NewDatabaseName());
        before = CreateApp(connectionString);
        after = CreateApp(connectionString);
    }

    // Without the app's own repository, both instances would fall back to this machine's shared key folder,
    // which a new pod doesn't have.
    static WebApplicationFactory<Program> CreateApp(string connectionString) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseEnvironment("Production")
            .UseSetting("ConnectionStrings:Default", connectionString)
            .ConfigureServices(s => s.PostConfigure<KeyManagementOptions>(o => o.XmlRepository ??= new InMemoryXmlRepository())));

    sealed class InMemoryXmlRepository : IXmlRepository
    {
        readonly List<XElement> elements = [];

        public IReadOnlyCollection<XElement> GetAllElements() => elements.ToList();

        public void StoreElement(XElement element, string friendlyName) => elements.Add(element);
    }

    [Fact]
    public async Task Keys_are_stored_in_the_database_and_survive_a_restart()
    {
        var token = before.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Protect("payload");

        var db = before.Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await db.CreateDbContextAsync(TestContext.Current.CancellationToken);
        Assert.NotEmpty(await context.DataProtectionKeys.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal("payload", after.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Unprotect(token));
    }

    [Fact]
    public async Task Antiforgery_cookie_from_before_a_restart_is_still_accepted()
    {
        using var beforeClient = before.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var afterClient = after.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var first = await beforeClient.GetAsync("/", TestContext.Current.CancellationToken);
        var cookie = AntiforgeryCookie(first) ?? throw new InvalidOperationException("The home page set no antiforgery cookie.");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Cookie", cookie);
        var second = await afterClient.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        // A cookie the app can't decrypt is replaced with a new one.
        Assert.Null(AntiforgeryCookie(second));
    }

    static string? AntiforgeryCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.Select(c => c.Split(';')[0]).FirstOrDefault(c => c.StartsWith(".AspNetCore.Antiforgery."))
            : null;

    public void Dispose()
    {
        before.Dispose();
        after.Dispose();
    }
}
