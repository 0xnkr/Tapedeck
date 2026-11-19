using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tapedeck.Core.LastFm;

/// <summary>
/// The Last.fm web service with the user's own API key. At most 4 requests per second for the whole app, below the
/// limit Last.fm asks for; a busy service or the rate limit is asked again a few times with growing pauses.
/// The key never reaches the log.
/// </summary>
public sealed class LastFmClient(HttpClient http, ILogger<LastFmClient>? logger = null) : ILastFmTags
{
    private readonly ILogger log = (ILogger?)logger ?? NullLogger.Instance;

    private const string BASE_URL = "https://ws.audioscrobbler.com/2.0/";

    private static readonly TimeSpan MIN_INTERVAL = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan[] RETRY_DELAYS = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8)];

    // shared by all instances: the limit applies per app, not per client
    private static readonly SemaphoreSlim GATE = new(1, 1);
    private static DateTime LAST_REQUEST = DateTime.MinValue;

    /// <summary>The tags listeners gave the artist, strongest first; empty for an artist Last.fm does not know.</summary>
    /// <exception cref="LastFmException">An invalid key or another error than an unknown artist.</exception>
    public async Task<IReadOnlyList<LastFmTag>> GetArtistTagsAsync(string apiKey, string artist, CancellationToken ct = default)
    {
        var query = $"?method=artist.gettoptags&autocorrect=1&format=json&artist={Uri.EscapeDataString(artist)}&api_key={Uri.EscapeDataString(apiKey)}";
        for (var attempt = 0; ; attempt++)
        {
            string body;
            HttpStatusCode status;
            using (var response = await this.SendThrottledAsync(query, ct))
            {
                status = response.StatusCode;
                body = await response.Content.ReadAsStringAsync(ct);
            }

            try
            {
                // a server error comes as an HTML page from Last.fm or a proxy in between, not as a JSON error
                if (status is HttpStatusCode.TooManyRequests || (int)status >= 500)
                    throw new LastFmException(LastFmException.TEMPORARILY_UNAVAILABLE, $"HTTP {(int)status}");
                return LastFmJson.ParseTopTags(body);
            }
            catch (LastFmException ex) when (ex.Code == LastFmException.NOT_FOUND)
            {
                return [];
            }
            catch (LastFmException ex) when (ex.Code is LastFmException.RATE_LIMIT or LastFmException.TEMPORARILY_UNAVAILABLE
                                             && attempt < RETRY_DELAYS.Length)
            {
                this.log.LogWarning("Last.fm is busy ({Error}), asking again in {Seconds} s", ex.Message, RETRY_DELAYS[attempt].TotalSeconds);
                await Task.Delay(RETRY_DELAYS[attempt], ct);
            }
        }
    }

    private async Task<HttpResponseMessage> SendThrottledAsync(string query, CancellationToken ct)
    {
        await GATE.WaitAsync(ct);
        try
        {
            var wait = LAST_REQUEST + MIN_INTERVAL - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, ct);

            using var request = new HttpRequestMessage(HttpMethod.Get, BASE_URL + query);
            request.Headers.UserAgent.Add(UserAgent.PRODUCT);
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
