using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace SharpVoice.Refiner.Auth;

internal enum OAuthCallbackOutcome { Success, Declined, Invalid, Failed }

internal static class OAuthCallbackPage
{
    private const string CleanHistory = "try { history.replaceState(null, '', '/auth/callback'); } catch {}";
    private static readonly string Template = LoadTemplate();
    private static readonly string ScriptHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(CleanHistory)));

    internal static string Render(OAuthCallbackOutcome outcome)
    {
        var (status, heading, description, next) = outcome switch
        {
            OAuthCallbackOutcome.Success => ("Connected", "Sign-in successful", "Your ChatGPT account is connected to SharpVoice. You're ready to put your voice to work.", "Return to SharpVoice"),
            OAuthCallbackOutcome.Declined => ("Cancelled", "Sign-in cancelled", "No changes were made to your ChatGPT connection. You can start again whenever you're ready.", "Return to SharpVoice"),
            OAuthCallbackOutcome.Invalid => ("Invalid link", "Invalid sign-in link", "We couldn't verify this sign-in link. Please use the sign-in window opened by SharpVoice.", "Return to your sign-in window"),
            OAuthCallbackOutcome.Failed => ("Not completed", "Sign-in failed", "SharpVoice couldn't finish connecting your ChatGPT account. Return to the app and try signing in again.", "Return to SharpVoice"),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome))
        };
        return Template.Replace("{{OUTCOME}}", outcome == OAuthCallbackOutcome.Success ? "success" : "failure")
            .Replace("{{STATUS}}", WebUtility.HtmlEncode(status)).Replace("{{HEADING}}", WebUtility.HtmlEncode(heading))
            .Replace("{{DESCRIPTION}}", WebUtility.HtmlEncode(description)).Replace("{{NEXT}}", WebUtility.HtmlEncode(next))
            .Replace("{{ICON}}", outcome == OAuthCallbackOutcome.Success ? "<path d=\"m11 21 6 6 13-14\"/>" : "<path d=\"m13 13 14 14m0-14L13 27\"/>")
            .Replace("{{CLEAN_HISTORY}}", CleanHistory);
    }

    internal static async Task WriteAsync(Stream stream, int status, OAuthCallbackOutcome outcome, CancellationToken ct)
    {
        var body = Encoding.UTF8.GetBytes(Render(outcome));
        var reason = status switch { 200 => "OK", 400 => "Bad Request", 500 => "Internal Server Error", _ => throw new ArgumentOutOfRangeException(nameof(status)) };
        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {reason}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\nCache-Control: no-store\r\nReferrer-Policy: no-referrer\r\nX-Content-Type-Options: nosniff\r\nContent-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; script-src 'sha256-{ScriptHash}'; img-src data:; base-uri 'none'; form-action 'none'; frame-ancestors 'none'\r\n\r\n");
        await stream.WriteAsync(header, ct);
        await stream.WriteAsync(body, ct);
    }

    internal static async Task TryWriteAsync(Stream stream, int status, OAuthCallbackOutcome outcome)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { await WriteAsync(stream, status, outcome, timeout.Token); }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
    }

    private static string LoadTemplate()
    {
        using var resource = typeof(OAuthCallbackPage).Assembly.GetManifestResourceStream("SharpVoice.Auth.CallbackPage.html")
            ?? throw new InvalidOperationException("The sign-in result page is missing.");
        using var reader = new StreamReader(resource, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
