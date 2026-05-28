using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace ApsDesktopApp.Services;

// Attaches to the Model Derivative HttpClient. Injects a 2-legged
// (client credentials) bearer token into every outgoing request and
// retries exactly once on 401 with a freshly fetched token.
public class TwoLeggedAuthHandler : DelegatingHandler
{
    private const string Cat = "TwoLeggedAuth";

    private readonly TwoLeggedTokenService _tokens;
    private readonly AppLogger             _log;

    public TwoLeggedAuthHandler(TwoLeggedTokenService tokens, AppLogger log)
    {
        _tokens = tokens;
        _log    = log;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _log.Debug(Cat, $"Attaching 2-legged token for {request.RequestUri?.AbsolutePath}");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await _tokens.GetTokenAsync(cancellationToken));

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        // Token was rejected server-side despite looking valid by the clock.
        // Invalidate the cache, re-fetch, and retry once on a fresh clone
        // (a sent HttpRequestMessage cannot be reused).
        _log.Warn(Cat, $"401 received -- invalidating cached token and retrying {request.RequestUri?.AbsolutePath}");
        response.Dispose();
        _tokens.Invalidate();
        using var retry = Clone(request);
        retry.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await _tokens.GetTokenAsync(cancellationToken));
        return await base.SendAsync(retry, cancellationToken);
    }

    private static HttpRequestMessage Clone(HttpRequestMessage req)
    {
        var clone = new HttpRequestMessage(req.Method, req.RequestUri);
        foreach (var header in req.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        clone.Content = req.Content;
        return clone;
    }
}
