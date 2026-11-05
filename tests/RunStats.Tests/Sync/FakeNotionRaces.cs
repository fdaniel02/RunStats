using RunStats.Notion;

namespace RunStats.Tests.Sync;

public sealed class FakeNotionRaces : INotionRaces
{
    public bool IsConfigured { get; set; } = true;
    public List<NotionRace> Races { get; } = [];
    public Exception? Failure { get; set; }
    public DateOnly? RequestedFrom { get; private set; }

    public Task<IReadOnlyList<NotionRace>> GetFromAsync(DateOnly from, CancellationToken cancellationToken)
    {
        RequestedFrom = from;
        if (Failure is not null)
            throw Failure;
        return Task.FromResult<IReadOnlyList<NotionRace>>([.. Races]);
    }

    public static NotionRace Race(
        string name = "Zandvoort", Guid? id = null, DateOnly? date = null, TimeOnly? start = null, double? km = 16.1, string? type = "Normal",
        bool registered = true, string? city = "Zandvoort", string? link = null, string? target = null) =>
        new(id ?? Guid.NewGuid(), name, date ?? new DateOnly(2027, 3, 21), start, km, type, registered, city, link, target);
}
