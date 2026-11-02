using System.Net;
using System.Reflection;
using System.Text;
using SharpVoice.Core;
using Velopack;
using Velopack.Sources;

namespace SharpVoice.App;

internal sealed class GitHubReleaseUpdater : IReleaseUpdater, IDisposable
{
    internal const string RepositoryUrl = "https://github.com/rleonard55/SharpVoice";
    private readonly ReleaseDownloader downloader = new();
    private readonly UpdateManager manager;
    private UpdateInfo? candidate;
    public GitHubReleaseUpdater() => manager = new(new GithubSource(RepositoryUrl, null, false, downloader));
    public bool Supported => manager.IsInstalled;
    public string CurrentVersion => manager.CurrentVersion?.ToString() ?? typeof(GitHubReleaseUpdater).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "Development";
    public string? ReadyVersion => manager.UpdatePendingRestart is { } pending && manager.CurrentVersion is { } current && pending.Version.CompareTo(current) > 0 ? pending.Version.ToString() : null;
    public async Task<string?> CheckAsync(CancellationToken ct)
    {
        downloader.OperationToken = ct;
        ct.ThrowIfCancellationRequested();
        var found = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        candidate = found;
        return found?.TargetFullRelease.Version.ToString();
    }
    public async Task DownloadAsync(string version, Action<int> progress, CancellationToken ct)
    {
        if (candidate is null || candidate.TargetFullRelease.Version.ToString() != version) throw new InvalidOperationException("Check for this update before downloading it.");
        downloader.OperationToken = ct;
        await manager.DownloadUpdatesAsync(candidate, progress, ct).ConfigureAwait(false);
    }
    public void ApplyAndRestart()
    {
        if (ReadyVersion is null) throw new InvalidOperationException("No newer verified update is ready.");
        manager.ApplyUpdatesAndRestart(manager.UpdatePendingRestart, ["--startup"]);
    }
    public void Dispose() => downloader.Dispose();
}

/// <summary>Bounded public GitHub downloads; cancellation also covers SDK metadata requests.</summary>
internal sealed class ReleaseDownloader : IFileDownloader, IDisposable
{
    private readonly HttpClient http = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(15), AutomaticDecompression = DecompressionMethods.All,
        AllowAutoRedirect = true, MaxAutomaticRedirections = 8
    }) { Timeout = Timeout.InfiniteTimeSpan };
    internal CancellationToken OperationToken { get; set; }
    private static bool IsGitHub(Uri uri) => uri.Scheme == "https" && uri.Host is "github.com" or "api.github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com" or "github-releases.githubusercontent.com";
    private async Task<HttpResponseMessage> Open(string url, IDictionary<string, string>? headers, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !IsGitHub(uri)) throw new InvalidDataException("The update asset is not hosted by GitHub.");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("SharpVoice-Updater/0.1.0");
        if (headers is not null) foreach (var header in headers)
        {
            if (header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Public updates do not use account credentials.");
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode || response.RequestMessage?.RequestUri is not { } final || !IsGitHub(final))
        { response.Dispose(); throw new HttpRequestException("The public GitHub update feed is unavailable."); }
        return response;
    }
    public async Task<byte[]> DownloadBytes(string url, IDictionary<string, string>? headers = null, double timeout = 3)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(OperationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await Open(url, headers, deadline.Token).ConfigureAwait(false);
        await using var input = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
        using var bytes = new MemoryStream();
        var buffer = new byte[64 * 1024]; int count;
        while ((count = await input.ReadAsync(buffer, deadline.Token).ConfigureAwait(false)) != 0)
        {
            if (bytes.Length + count > 8 * 1024 * 1024) throw new InvalidDataException("Update metadata is too large.");
            bytes.Write(buffer, 0, count);
        }
        return bytes.ToArray();
    }
    public async Task<string> DownloadString(string url, IDictionary<string, string>? headers = null, double timeout = 3)
        => Encoding.UTF8.GetString(await DownloadBytes(url, headers, timeout).ConfigureAwait(false));
    public async Task DownloadFile(string url, string targetFile, Action<int>? progress = null, IDictionary<string, string>? headers = null, double timeout = 30, CancellationToken cancelToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(OperationToken, cancelToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(15));
        using var response = await Open(url, headers, deadline.Token).ConfigureAwait(false);
        const long maximum = 512L * 1024 * 1024;
        var length = response.Content.Headers.ContentLength;
        if (length > maximum) throw new InvalidDataException("The update package is too large.");
        await using var input = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
        await using var output = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, true);
        var buffer = new byte[64 * 1024]; long total = 0; int count;
        while ((count = await input.ReadAsync(buffer, deadline.Token).ConfigureAwait(false)) != 0)
        {
            total += count;
            if (total > maximum) throw new InvalidDataException("The update package is too large.");
            await output.WriteAsync(buffer.AsMemory(0, count), deadline.Token).ConfigureAwait(false);
            if (length > 0) progress?.Invoke((int)Math.Clamp(total * 100 / length.Value, 0, 100));
        }
        await output.FlushAsync(deadline.Token).ConfigureAwait(false);
        progress?.Invoke(100);
    }
    public void Dispose() => http.Dispose();
}
