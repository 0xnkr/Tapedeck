using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tapedeck.Core.Lyrics;

/// <summary>The lyrics lookups; <see cref="LrclibClient"/> asks LRCLIB.</summary>
public interface ILyricsSource
{
    /// <summary>The record LRCLIB has for exactly this track; <c>null</c> when it has none.</summary>
    Task<LrclibRecord?> GetAsync(LyricsRequest request, CancellationToken ct = default);

    /// <summary>Up to 20 records with a similar title and artist.</summary>
    Task<IReadOnlyList<LrclibRecord>> SearchAsync(LyricsRequest request, CancellationToken ct = default);
}

/// <summary>
/// LRCLIB (lrclib.net), a free lyrics database without a key. One request per second for the whole app, below
/// what it allows identified clients; when it asks to slow down, the wait it names is kept a few times.
/// </summary>
public sealed class LrclibClient(HttpClient http, ILogger<LrclibClient>? logger = null) : ILyricsSource
{
    private readonly ILogger log = (ILogger?)logger ?? NullLogger.Instance;

    private const string BASE_URL = "https://lrclib.net/api/";
    private const string CONTACT = "https://github.com/0xnkr/Tapedeck";
    private const int MAX_RETRIES = 3;

    private static readonly TimeSpan MIN_INTERVAL = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DEFAULT_RETRY = TimeSpan.FromSeconds(10);

    // shared by all instances: the limit applies per app, not per client
    private static readonly SemaphoreSlim GATE = new(1, 1);
    private static DateTime LAST_REQUEST = DateTime.MinValue;

    public async Task<LrclibRecord?> GetAsync(LyricsRequest request, CancellationToken ct = default)
    {
        var query = $"get?artist_name={Uri.EscapeDataString(request.Artist)}&track_name={Uri.EscapeDataString(request.Title)}";
        if (request.Album is { } album)
            query += $"&album_name={Uri.EscapeDataString(album)}";
        if (request.DurationS is { } duration)
            query += $"&duration={duration.ToString(CultureInfo.InvariantCulture)}";
        return await this.GetAsync(query, ct) is { } json ? LrclibJson.ParseRecord(json) : null;
    }

    public async Task<IReadOnlyList<LrclibRecord>> SearchAsync(LyricsRequest request, CancellationToken ct = default)
    {
        var query = $"search?artist_name={Uri.EscapeDataString(request.Artist)}&track_name={Uri.EscapeDataString(request.Title)}";
        return await this.GetAsync(query, ct) is { } json ? LrclibJson.ParseSearch(json) : [];
    }

    /// <returns>The response body, or <c>null</c> for 404.</returns>
    private async Task<string?> GetAsync(string relativeUrl, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var response = await this.SendThrottledAsync(relativeUrl, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;

            var busy = response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable;
            if (busy && attempt < MAX_RETRIES)
            {
                var wait = response.Headers.RetryAfter?.Delta ?? DEFAULT_RETRY;
                this.log.LogWarning("LRCLIB is busy ({Status}), asking again in {Seconds} s", (int)response.StatusCode, wait.TotalSeconds);
                await Task.Delay(wait, ct);
                continue;
            }

            if (!response.IsSuccessStatusCode)
                this.log.LogWarning("LRCLIB answered {Status}", (int)response.StatusCode);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(ct);
        }
    }

    private async Task<HttpResponseMessage> SendThrottledAsync(string relativeUrl, CancellationToken ct)
    {
        await GATE.WaitAsync(ct);
        try
        {
            var wait = LAST_REQUEST + MIN_INTERVAL - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, ct);

            using var request = new HttpRequestMessage(HttpMethod.Get, BASE_URL + relativeUrl);
            request.Headers.UserAgent.Add(UserAgent.PRODUCT);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue($"( {CONTACT} )"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            try
            {
                return await http.SendAsync(request, ct);
            }
            finally
            {
                LAST_REQUEST = DateTime.UtcNow;
            }
        }
        finally
        {
            GATE.Release();
        }
    }
}
