using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using RunStats.Garmin;

namespace RunStats.Tests.Sync;

// Garmin as the gateway shows it: a list of activities and, per activity, the downloads.
public sealed class FakeGarminGateway : IGarminGateway
{
    public List<GarminActivity> Activities { get; } = [];
    public HashSet<long> WithoutFitFile { get; } = [];
    public HashSet<long> WithoutWeather { get; } = [];
    // What decoding an activity's FIT file returns; by default a few seconds along a short straight line.
    public Dictionary<long, IReadOnlyList<FitSample>> FitSamples { get; } = [];
    public ConcurrentQueue<string> Calls { get; } = new();
    // Called with the name of each call ("list 0", "detail 42", ...); an exception it returns is thrown.
    public Func<string, Exception?> Fail { get; set; } = _ => null;

    public static GarminActivity Activity(
        long id, string typeKey = "running", int typeId = 1, int parentTypeId = 17, string name = "Run", bool race = false,
        string startGmt = "2026-10-25 09:30:00", double distance = 5000, double duration = 1500)
    {
        var json = $$"""
            {"activityId":{{id}},"activityName":{{JsonSerializer.Serialize(name)}},"startTimeLocal":"{{Local(startGmt)}}","startTimeGMT":"{{startGmt}}",
            "activityType":{"typeId":{{typeId}},"typeKey":"{{typeKey}}","parentTypeId":{{parentTypeId}}},
            "eventType":{"typeId":{{(race ? 1 : 9)}},"typeKey":"{{(race ? "race" : "uncategorized")}}"},
            "distance":{{distance.ToString(CultureInfo.InvariantCulture)}},"duration":{{duration.ToString(CultureInfo.InvariantCulture)}}}
            """;
        using var document = JsonDocument.Parse(json);
        return GarminActivity.FromJson(document.RootElement);
    }

    static string Local(string startGmt) =>
        DateTime.ParseExact(startGmt, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture).AddHours(1).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    public static string DetailJson(long id) => $$$"""{"activityId":{{{id}}},"summaryDTO":{"averageHR":150.0,"averageRunCadence":170.0}}""";
    public static string SplitsJson(long id) => $$"""{"activityId":{{id}},"lapDTOs":[]}""";
    public static string HeartRateZonesJson(long id) => $$"""[{"zoneNumber":1,"activityId":{{id}}}]""";
    public static string WeatherJson(long id) => $$"""{"temp":12,"activityId":{{id}}}""";
    public static byte[] FitFile(long id) => Encoding.ASCII.GetBytes($"FIT{id}");

    public static IReadOnlyList<FitSample> StraightLine(int seconds) =>
    [
        .. Enumerable.Range(0, seconds).Select(i => new FitSample(
            i, 624_769_324 + i * 1000, -58_342_310, i * 3.0, 3.0, 5.0, 140 + i, 170.0, 1150.0, 240.0, 85.0, 7.0, 300, 18))
    ];

    public IEnumerable<string> DownloadCalls => Calls.Where(c => !c.StartsWith("list", StringComparison.Ordinal));

    T Call<T>(string name, Func<T> result)
    {
        Calls.Enqueue(name);
        if (Fail(name) is { } error)
            throw error;
        return result();
    }

    public Task<IReadOnlyList<GarminActivity>> GetActivitiesAsync(int start, int limit, CancellationToken cancellationToken) =>
        Task.FromResult(Call<IReadOnlyList<GarminActivity>>($"list {start}", () => [.. Activities.Skip(start).Take(limit)]));

    public Task<string> GetActivityAsync(long id, CancellationToken cancellationToken) =>
        Task.FromResult(Call($"detail {id}", () => DetailJson(id)));

    public Task<string?> GetSplitsAsync(long id, CancellationToken cancellationToken) =>
        Task.FromResult(Call<string?>($"splits {id}", () => SplitsJson(id)));

    public Task<string?> GetHeartRateZonesAsync(long id, CancellationToken cancellationToken) =>
        Task.FromResult(Call<string?>($"hr-zones {id}", () => HeartRateZonesJson(id)));

    public Task<string?> GetWeatherAsync(long id, CancellationToken cancellationToken) =>
        Task.FromResult(Call($"weather {id}", () => WithoutWeather.Contains(id) ? null : WeatherJson(id)));

    public Task<byte[]?> GetFitFileAsync(long id, CancellationToken cancellationToken) =>
        Task.FromResult(Call($"fit {id}", () => WithoutFitFile.Contains(id) ? null : FitFile(id)));

    public Task<IReadOnlyList<FitSample>> DecodeFitAsync(byte[] fitFile, CancellationToken cancellationToken)
    {
        var id = long.Parse(Encoding.ASCII.GetString(fitFile)[3..], CultureInfo.InvariantCulture);
        return Task.FromResult(Call($"decode {id}", () => FitSamples.GetValueOrDefault(id) ?? StraightLine(3)));
    }
}
