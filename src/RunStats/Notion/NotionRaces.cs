using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace RunStats.Notion;

public sealed class NotionSettings
{
    public const string Section = "Notion";

    public string? Token { get; set; }
    public string? DataSourceId { get; set; }
}

public sealed record NotionRace(
    Guid Id, string Name, DateOnly Date, TimeOnly? StartTime, double? DistanceKm, string? Type, bool IsRegistered, string? City, string? Link,
    string? TargetTime);

public interface INotionRaces
{
    bool IsConfigured { get; }
    Task<IReadOnlyList<NotionRace>> GetFromAsync(DateOnly from, CancellationToken cancellationToken);
}

public class NotionRaces(HttpClient http, IOptions<NotionSettings> settings) : INotionRaces
{
    public const string ApiVersion = "2025-09-03";

    // Notion rejects a null start_cursor.
    static readonly JsonSerializerOptions QueryOptions = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public bool IsConfigured => !string.IsNullOrWhiteSpace(settings.Value.Token) && !string.IsNullOrWhiteSpace(settings.Value.DataSourceId);

    public async Task<IReadOnlyList<NotionRace>> GetFromAsync(DateOnly from, CancellationToken cancellationToken)
    {
        var races = new List<NotionRace>();
        string? cursor = null;
        do
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"data_sources/{Uri.EscapeDataString(settings.Value.DataSourceId!)}/query");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Value.Token);
            request.Content = JsonContent.Create(new
            {
                filter = new { property = "Datum", date = new { on_or_after = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) } },
                page_size = 100,
                start_cursor = cursor
            }, options: QueryOptions);
            using var response = await http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;
            races.AddRange(root.GetProperty("results").EnumerateArray().Select(NotionRaceMapper.FromPage).OfType<NotionRace>());
            cursor = root.GetProperty("has_more").GetBoolean() ? root.GetProperty("next_cursor").GetString() : null;
        }
        while (cursor is not null);
        return races;
    }
}
