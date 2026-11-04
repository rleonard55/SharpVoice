using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text.Json;
using SharpVoice.Core;

namespace SharpVoice.Refiner;

public sealed class ProviderFailureException(ProviderFailure failure, string? safeDetail = null)
    : InvalidOperationException($"{failure.Provider} · {failure.Operation}: {safeDetail ?? failure.Explanation} Open AI & Models → Diagnostics for a sanitized report. Nothing was inserted.")
{
    public ProviderFailure Failure { get; } = failure;
}

internal sealed class ProviderHttpException(HttpStatusCode status, string? safeDetail = null) : Exception
{
    public int Status { get; } = (int)status;
    public string? SafeDetail { get; } = safeDetail;
}

internal static class ProviderOperation
{
    private static readonly object Recorded = new();
    private static readonly object AnswerIssue = new();
    internal static InvalidDataException RejectedAnswer(bool outputLimit = false)
    {
        var failure = new InvalidDataException("Provider did not complete the answer.");
        failure.Data[AnswerIssue] = outputLimit ? ProviderFailureKind.OutputLimit : ProviderFailureKind.ProviderRejected;
        return failure;
    }
    private static readonly AsyncLocal<CancellationToken?> RootCaller = new();
    public static async Task<T> RunAsync<T>(ProviderOperationKind operation, Func<CancellationToken, Task<T>> body, CancellationToken ct, TimeSpan? timeout = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout ?? TimeSpan.FromMinutes(3));
        var watch = Stopwatch.StartNew();
        var previousCaller = RootCaller.Value;
        var caller = previousCaller ?? ct;
        RootCaller.Value = caller;
        try
        {
            var result = await body(deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            return result;
        }
        catch (Exception ex) when (ex is ProviderFailureException or ProviderStreamException || ex.Data.Contains(Recorded)) { throw; } // A nested operation already owns this failure.
        catch (OperationCanceledException) when (caller.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or ProviderHttpException or OperationCanceledException or IOException or InvalidDataException or JsonException or AuthenticationException or SocketException
            || ex is InvalidOperationException && ex.Source == "System.Text.Json")
        {
            var kind = Classify(ex);
            var status = ex is ProviderHttpException rejected ? rejected.Status : ex is HttpRequestException request ? (int?)request.StatusCode : null;
            var code = ex is HttpRequestException network ? (int)network.HttpRequestError : ex is SocketException socket ? socket.ErrorCode : (int?)null;
            var failure = new ProviderFailure(operation, kind, status, code, watch.ElapsedMilliseconds);
            ProviderDiagnostics.Shared.Record(failure);
            // Do not retain the original exception, which can contain credentials, hostnames or provider content.
            if (ex is InvalidDataException or JsonException || ex is InvalidOperationException && ex.Source == "System.Text.Json")
            {
                var safe = new InvalidDataException($"{failure.Provider} · {failure.Operation}: {failure.Explanation} Open AI & Models → Diagnostics for a sanitized report. Nothing was inserted.");
                safe.Data[Recorded] = true;
                throw safe;
            }
            if (ex is IOException) throw new ProviderStreamException(failure);
            throw new ProviderFailureException(failure, (ex as ProviderHttpException)?.SafeDetail);
        }
        finally { RootCaller.Value = previousCaller; }
    }
    private static ProviderFailureKind Classify(Exception ex)
    {
        if (ex.Data[AnswerIssue] is ProviderFailureKind answerIssue) return answerIssue;
        if (ex is ProviderHttpException || ex is HttpRequestException { StatusCode: not null }) return ProviderFailureKind.Http;
        if (ex is OperationCanceledException) return ProviderFailureKind.Timeout;
        if (ex is AuthenticationException) return ProviderFailureKind.Tls;
        if (ex is HttpRequestException http)
        {
            return http.HttpRequestError switch
            {
                HttpRequestError.NameResolutionError => ProviderFailureKind.Dns,
                HttpRequestError.SecureConnectionError => ProviderFailureKind.Tls,
                HttpRequestError.ProxyTunnelError => ProviderFailureKind.Proxy,
                HttpRequestError.ResponseEnded => ProviderFailureKind.InterruptedResponse,
                HttpRequestError.InvalidResponse or HttpRequestError.HttpProtocolError => ProviderFailureKind.InvalidResponse,
                _ => http.InnerException is AuthenticationException ? ProviderFailureKind.Tls : ProviderFailureKind.Connection
            };
        }
        if (ex is SocketException socket) return socket.SocketErrorCode is SocketError.HostNotFound or SocketError.TryAgain or SocketError.NoData ? ProviderFailureKind.Dns : ProviderFailureKind.Connection;
        if (ex is JsonException or InvalidDataException or InvalidOperationException) return ProviderFailureKind.InvalidResponse;
        if (ex is IOException) return ProviderFailureKind.InterruptedResponse;
        return ProviderFailureKind.Unknown;
    }
}
