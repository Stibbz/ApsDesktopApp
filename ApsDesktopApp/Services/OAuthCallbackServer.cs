using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ApsDesktopApp.Services;

// Captures the single OAuth redirect on http://localhost:{port}/callback.
public class OAuthCallbackServer
{
    private readonly int _port;

    public OAuthCallbackServer(int port) => _port = port;

    public class CallbackResult
    {
        public string? Code { get; init; }
        public string? State { get; init; }
        public string? Error { get; init; }
    }

    // Blocks (asynchronously) until the browser hits the callback or the token is cancelled.
    public async Task<CallbackResult> WaitForCallbackAsync(CancellationToken cancellationToken)
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{_port}/callback/");

        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            throw new InvalidOperationException(
                $"Could not start the local sign-in listener on port {_port}. " +
                "The port may be in use by another application. " +
                "Close it or change the callback port in Settings.", ex);
        }

        try
        {
            using var registration = cancellationToken.Register(() =>
            {
                try { listener.Stop(); } catch { /* already stopping */ }
            });

            var context = await listener.GetContextAsync();
            var request = context.Request;

            var code = request.QueryString["code"];
            var state = request.QueryString["state"];
            var error = request.QueryString["error"];

            await WriteBrowserResponseAsync(context.Response, error);

            return new CallbackResult { Code = code, State = state, Error = error };
        }
        finally
        {
            if (listener.IsListening)
                listener.Stop();
        }
    }

    private static async Task WriteBrowserResponseAsync(HttpListenerResponse response, string? error)
    {
        var message = error is null
            ? "<h2>Connected to Autodesk APS</h2><p>You can close this tab and return to the app.</p>"
            : $"<h2>Sign-in failed</h2><p>{WebUtility.HtmlEncode(error)}</p><p>You can close this tab.</p>";

        var html = $"<!DOCTYPE html><html><head><meta charset='utf-8'><title>APS Desktop App</title></head>" +
                   $"<body style='font-family:Segoe UI,sans-serif;text-align:center;margin-top:80px'>{message}</body></html>";

        var buffer = Encoding.UTF8.GetBytes(html);
        response.ContentType = "text/html";
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer);
        response.OutputStream.Close();
    }
}
