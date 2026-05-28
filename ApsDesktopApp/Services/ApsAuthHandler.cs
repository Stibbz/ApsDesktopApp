using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace ApsDesktopApp.Services;

// Attaches to the data HttpClient. Injects the APS bearer token into every
// outgoing request and, if a request still returns 401 (token rejected
// server-side despite looking valid by the clock), forces one refresh and
// retries exactly once. Data methods never touch tokens as a result.
//
// ApsAuthService is resolved lazily from the container rather than constructor-
// injected: that service owns the HttpClient this handler is attached to, so
// constructor injection would form a DI cycle. Lazy resolution breaks it, and
// everything here is registered as a singleton so the lookup is cheap.
public class ApsAuthHandler : DelegatingHandler
{
    private const string Cat = "ApsAuthHandler";

    private readonly IServiceProvider _services;
    private readonly AppLogger        _log;

    public ApsAuthHandler(IServiceProvider services, AppLogger log)
    {
        _services = services;
        _log      = log;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var auth = _services.GetRequiredService<ApsAuthService>();

        _log.Debug(Cat, $"Attaching 3-legged token (forceRefresh=false) for {request.RequestUri?.AbsolutePath}");
        await ApplyTokenAsync(request, auth, forceRefresh: false, cancellationToken);
        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        // The token was rejected. Force a single refresh and retry once on a
        // fresh copy (a sent HttpRequestMessage cannot be reused).
        _log.Warn(Cat, $"401 received -- forcing token refresh and retrying {request.RequestUri?.AbsolutePath}");
        response.Dispose();
        using var retry = Clone(request);
        await ApplyTokenAsync(retry, auth, forceRefresh: true, cancellationToken);
        return await base.SendAsync(retry, cancellationToken);
    }

    private static async Task ApplyTokenAsync(
        HttpRequestMessage request, ApsAuthService auth, bool forceRefresh, CancellationToken ct)
    {
        var token = await auth.EnsureValidAccessTokenAsync(ct, forceRefresh);
        request.Headers.Authorization =
            token is null ? null : new AuthenticationHeaderValue("Bearer", token);
    }

    // Shallow-clones a request for the retry. Data Management reads are GET with
    // no body, so Content is null; reusing the reference is safe for that case.
    private static HttpRequestMessage Clone(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        clone.Content = request.Content;
        return clone;
    }
}
