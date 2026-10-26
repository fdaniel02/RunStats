using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace RunStats.Garmin;

public interface IGarminGateway
{
    Task<IReadOnlyList<GarminActivity>> GetActivitiesAsync(int start, int limit, CancellationToken cancellationToken);
    Task<string> GetActivityAsync(long id, CancellationToken cancellationToken);
    Task<string?> GetSplitsAsync(long id, CancellationToken cancellationToken);
    Task<string?> GetHeartRateZonesAsync(long id, CancellationToken cancellationToken);
    Task<string?> GetWeatherAsync(long id, CancellationToken cancellationToken);
    Task<byte[]?> GetFitFileAsync(long id, CancellationToken cancellationToken);
    Task<IReadOnlyList<FitSample>> DecodeFitAsync(byte[] fitFile, CancellationToken cancellationToken);
}

// The gateway waits after a rate limit or a failed login; until then it answers 503 with Retry-After.
public class GarminUnavailableException(string reason, TimeSpan? retryAfter)
    : Exception($"Garmin is unavailable: {reason}" + (retryAfter is { } r ? $" (retry after {r.TotalSeconds:0} s)" : ""))
{
    public string Reason { get; } = reason;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

public class InvalidFitFileException(string message) : Exception(message);

public class GarminGatewayClient(HttpClient http) : IGarminGateway
{
    public async Task<IReadOnlyList<GarminActivity>> GetActivitiesAsync(int start, int limit, CancellationToken cancellationToken)
    {
        var json = await GetStringAsync($"activities?start={start}&limit={limit}", cancellationToken)
            ?? throw new HttpRequestException("The gateway didn't find the activity list.", null, HttpStatusCode.NotFound);
        using var document = JsonDocument.Parse(json);
        return [.. document.RootElement.EnumerateArray().Select(GarminActivity.FromJson)];
    }

    public async Task<string> GetActivityAsync(long id, CancellationToken cancellationToken) =>
        await GetStringAsync($"activities/{id}", cancellationToken)
        ?? throw new HttpRequestException($"Garmin doesn't know activity {id}.", null, HttpStatusCode.NotFound);

    public Task<string?> GetSplitsAsync(long id, CancellationToken cancellationToken) =>
        GetStringAsync($"activities/{id}/splits", cancellationToken);

    public Task<string?> GetHeartRateZonesAsync(long id, CancellationToken cancellationToken) =>
        GetStringAsync($"activities/{id}/hr-zones", cancellationToken);

    public Task<string?> GetWeatherAsync(long id, CancellationToken cancellationToken) =>
        GetStringAsync($"activities/{id}/weather", cancellationToken);

    public async Task<byte[]?> GetFitFileAsync(long id, CancellationToken cancellationToken)
    {
        using var response = await SendAsync($"activities/{id}/fit", cancellationToken);
        return response is null ? null : await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FitSample>> DecodeFitAsync(byte[] fitFile, CancellationToken cancellationToken)
    {
        using var content = new ByteArrayContent(fitFile);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.ant.fit");
        using var response = await http.PostAsync("fit/samples", content, cancellationToken);
        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
            throw new InvalidFitFileException(await ReasonAsync(response, cancellationToken));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<FitSample>>(cancellationToken) ?? [];
    }

    // Missing data (404) and Garmin's JSON null both become null.
    async Task<string?> GetStringAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(path, cancellationToken);
        if (response is null)
            return null;
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return json.Trim() == "null" ? null : json;
    }

    async Task<HttpResponseMessage?> SendAsync(string path, CancellationToken cancellationToken)
    {
        var response = await http.GetAsync(path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            return null;
        }
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            using (response)
                throw new GarminUnavailableException(await ReasonAsync(response, cancellationToken), response.Headers.RetryAfter?.Delta);
        }
        try
        {
            response.EnsureSuccessStatusCode();
        }
        catch
        {
            response.Dispose();
            throw;
        }
        return response;
    }

    static async Task<string> ReasonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return document.RootElement.GetProperty("error").GetString() ?? "no reason given";
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return "no reason given";
        }
    }
}
