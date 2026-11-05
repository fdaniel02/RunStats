using System.Globalization;
using System.Text.Json;

namespace RunStats.Notion;

public static class NotionRaceMapper
{
    const int MaxLinkLength = 2000;

    // A row without a name or a date can't be listed, so it's left out.
    public static NotionRace? FromPage(JsonElement page)
    {
        if (!page.TryGetProperty("properties", out var properties)
            || !page.TryGetProperty("id", out var id) || !Guid.TryParse(id.GetString(), out var pageId)
            || Text(properties, "Race", "title") is not { } name
            || Start(properties) is not { } start)
            return null;
        return new NotionRace(
            pageId,
            name,
            start.Date,
            start.Time,
            Value(properties, "Distance", "number")?.GetDouble(),
            Value(properties, "Type", "select")?.GetProperty("name").GetString(),
            Value(properties, "Registered", "checkbox")?.GetBoolean() ?? false,
            Text(properties, "City", "rich_text"),
            WebLink(Value(properties, "Link", "url")?.GetString()),
            Text(properties, "Target Time", "rich_text"));
    }

    // Only web addresses: the field takes any text, and the page puts it in an href.
    static string? WebLink(string? text) =>
        text is { Length: <= MaxLinkLength } && Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? text
            : null;

    // A date without a time comes as 2027-05-09. With a time it has the offset of the race's day, as in
    // 2027-03-21T13:30:00.000+01:00, so the clock time as written is the time to show.
    static (DateOnly Date, TimeOnly? Time)? Start(JsonElement properties)
    {
        if (Value(properties, "Datum", "date") is not { } date || !date.TryGetProperty("start", out var start) || start.GetString() is not { } text)
            return null;
        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return (day, null);
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var moment))
            return (DateOnly.FromDateTime(moment.DateTime), TimeOnly.FromDateTime(moment.DateTime));
        return null;
    }

    static string? Text(JsonElement properties, string name, string type)
    {
        if (Value(properties, name, type) is not { ValueKind: JsonValueKind.Array } parts)
            return null;
        var text = string.Concat(parts.EnumerateArray().Select(p => p.GetProperty("plain_text").GetString())).Trim();
        return text.Length > 0 ? text : null;
    }

    static JsonElement? Value(JsonElement properties, string name, string type) =>
        properties.TryGetProperty(name, out var property) && property.TryGetProperty(type, out var value) && value.ValueKind != JsonValueKind.Null
            ? value
            : null;
}
