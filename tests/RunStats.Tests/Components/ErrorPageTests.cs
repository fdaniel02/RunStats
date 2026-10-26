using System.Diagnostics;
using Bunit;
using Microsoft.AspNetCore.Http;
using RunStats.Components.Pages;

namespace RunStats.Tests.Components;

public class ErrorPageTests : BunitContext
{
    [Fact]
    public void Shows_the_trace_id_of_the_failed_request()
    {
        using var activity = new Activity("Request").Start();

        var page = Render<Error>();

        Assert.Equal(activity.Id, page.Find("code").TextContent);
    }

    [Fact]
    public void Falls_back_to_the_request_id_without_a_trace()
    {
        using var noTrace = SuppressCurrentActivity();
        var http = new DefaultHttpContext { TraceIdentifier = "0HN7:00000001" };

        var page = Render<Error>(p => p.AddCascadingValue(http));

        Assert.Equal("0HN7:00000001", page.Find("code").TextContent);
    }

    [Fact]
    public void Leaves_out_the_request_id_when_there_is_none()
    {
        using var noTrace = SuppressCurrentActivity();

        var page = Render<Error>();

        Assert.Empty(page.FindAll("code"));
    }

    // Activity.Current is async-local; the test runner may have one running around the test.
    static Restore SuppressCurrentActivity()
    {
        var previous = Activity.Current;
        Activity.Current = null;
        return new Restore(() => Activity.Current = previous);
    }

    sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}
