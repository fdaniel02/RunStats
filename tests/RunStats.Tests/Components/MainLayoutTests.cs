using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using RunStats.Components.Layout;

namespace RunStats.Tests.Components;

public class MainLayoutTests : BunitContext
{
    IRenderedComponent<MainLayout> Render(string path)
    {
        ComponentFactories.AddStub<SyncButton>("<div class=\"sync\">Sync</div>");
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
    [InlineData("/trends", "Trends")]
    [InlineData("/heatmap", "Heatmap")]
    public void Highlights_the_current_screen(string path, string screen)
    {
        var layout = Render(path);

        var links = layout.FindAll(".app-nav a");
        Assert.Equal(["Dashboard", "Runs", "Trends", "Heatmap"], links.Select(l => l.TextContent));
        Assert.Equal(screen, Assert.Single(links, l => l.ClassList.Contains("active")).TextContent);
    }

    [Fact]
    public void The_menu_holds_the_screens_and_the_sync_button()
    {
        var menu = Render("/").Find(".menu");

        Assert.NotNull(menu.QuerySelector(".app-nav"));
        Assert.Equal("Sync", menu.QuerySelector(".sync")!.TextContent);
    }

    [Fact]
    public void The_menu_button_toggles_the_menu_checkbox()
    {
        var layout = Render("/");

        var toggle = layout.Find("input.menu-toggle");
        Assert.Equal(("checkbox", "Menu"), (toggle.GetAttribute("type"), toggle.GetAttribute("aria-label")));
        Assert.Equal(toggle.Id, layout.Find("label.menu-button").GetAttribute("for"));
    }
}
