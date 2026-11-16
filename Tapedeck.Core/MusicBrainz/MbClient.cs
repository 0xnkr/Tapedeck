using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;

namespace Tapedeck.Core.MusicBrainz;

/// <summary>
/// The MusicBrainz web service. At most one request per second for the whole app, as the service asks,
/// with a User-Agent that names the app. A busy server (503) is asked again a few times with growing pauses.
/// </summary>
public sealed class MbClient(HttpClient http, ILogger<MbClient>? logger = null)
{
    private readonly ILogger log = (ILogger?)logger ?? NullLogger.Instance;

    private const string BASE_URL = "https://musicbrainz.org/ws/2/";
    private const string CONTACT = "https://github.com/0xnkr/Tapedeck";

    /// <summary>The strict search reads this many recordings, so the album version of a much covered song is among them.</summary>
    public const int STRICT_LIMIT = 100;

    public const int FALLBACK_LIMIT = 25;

    public const int MAX_GENRES = 3;

    private static readonly TimeSpan MIN_INTERVAL = TimeSpan.FromMilliseconds(1100);
    private static readonly TimeSpan[] RETRY_DELAYS = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(16)];

    private static readonly ProductInfoHeaderValue USER_AGENT = new(
        "Tapedeck", (Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0)).ToString(3));

    // shared by all instances: the limit applies per app, not per client
    private static readonly SemaphoreSlim GATE = new(1, 1);
    private static DateTime LAST_REQUEST = DateTime.MinValue;

    public async Task<IReadOnlyList<MbRecording>> SearchRecordingsAsync(string lucene, int limit, CancellationToken ct = default)
    {
        var json = await this.GetAsync($"recording?fmt=json&limit={limit}&query={Uri.EscapeDataString(lucene)}", ct);
        return json is null ? [] : MbJson.ParseRecordingSearch(json);
    }

    /// <summary>The voted genres of an entity, most votes first.</summary>
    /// <param name="entity"><c>release-group</c>, <c>recording</c> or <c>artist</c>.</param>
    public async Task<IReadOnlyList<string>> GetGenresAsync(string entity, string id, CancellationToken ct = default)
    {
        var json = await this.GetAsync($"{entity}/{Uri.EscapeDataString(id)}?fmt=json&inc=genres", ct);
        return json is null ? [] : MbJson.ParseGenres(json, MAX_GENRES);
    }

    /// <returns>The response body, or <c>null</c> for 404.</returns>
    private async Task<string?> GetAsync(string relativeUrl, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var response = await this.SendThrottledAsync(relativeUrl, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;

            var busy = response.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests;
            if (busy && attempt < RETRY_DELAYS.Length)
            {
                this.log.LogWarning("MusicBrainz is busy ({Status}), asking again in {Seconds} s", (int)response.StatusCode,
                    RETRY_DELAYS[attempt].TotalSeconds);
                await Task.Delay(RETRY_DELAYS[attempt], ct);
                continue;
            }

            if (!response.IsSuccessStatusCode)
                this.log.LogWarning("MusicBrainz answered {Status} for {Url}", (int)response.StatusCode, relativeUrl);
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
            request.Headers.UserAgent.Add(USER_AGENT);
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
