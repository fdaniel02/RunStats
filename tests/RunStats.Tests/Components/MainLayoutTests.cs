using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using RunStats.Components.Layout;

namespace RunStats.Tests.Components;

public class MainLayoutTests : BunitContext
{
    IRenderedComponent<MainLayout> Render(string path)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(path);
        return Render<MainLayout>(p => p.Add(l => l.Body, b => b.AddMarkupContent(0, "<p id=\"body\">Page</p>")));
    }

    [Fact]
    public void Shows_the_page_inside_main()
    {
        var layout = Render("/");

        Assert.Equal("Page", layout.Find("main > #body").TextContent);
    }

    [Theory]
    [InlineData("/", "Dashboard")]
    [InlineData("/runs", "Runs")]
    [InlineData("/heatmap", "Heatmap")]
    public void Highlights_the_current_screen(string path, string screen)
    {
        var layout = Render(path);

        var links = layout.FindAll(".app-nav a");
        Assert.Equal(["Dashboard", "Runs", "Heatmap"], links.Select(l => l.TextContent));
        Assert.Equal(screen, Assert.Single(links, l => l.ClassList.Contains("active")).TextContent);
    }
}
