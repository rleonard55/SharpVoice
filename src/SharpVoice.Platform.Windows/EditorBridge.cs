using System.IO.Pipes;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using SharpVoice.Core;

namespace SharpVoice.Platform.Windows;

/// <summary>Local same-user editor channel. Contains no provider credentials.</summary>
public sealed partial class EditorBridge : IDisposable
{
    public static string DefaultDiscoveryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SharpVoice", "editor", "connection.json");
    private readonly string discoveryPath;
    private readonly Func<EditorFrame, CancellationToken, Task<EditorFrame>> handle;
    private readonly Action disconnected;
    private readonly CancellationTokenSource stop = new();
    private readonly EditorDiscovery discovery = new(EditorProtocol.Version, "SharpVoice.Editor." + Guid.NewGuid().ToString("N"), Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), Environment.ProcessId);
    private Task accept = Task.CompletedTask;
    private readonly List<Task> clients = [];
    private EditorWire? active;
    private string? activeClient;
    private readonly string hostId;
    private bool started;
    public EditorBridge(string discoveryPath, Func<EditorFrame, CancellationToken, Task<EditorFrame>> handle, Action disconnected, string? hostId = null)
    { (this.discoveryPath, this.handle, this.disconnected) = (discoveryPath, handle, disconnected); this.hostId = hostId ?? Guid.NewGuid().ToString("N"); }
    public void Start()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (started) throw new InvalidOperationException("Editor bridge already started.");
        var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(discoveryPath))!);
        directory.Create();
        using var identity = WindowsIdentity.GetCurrent(); var sid = identity.User ?? throw new InvalidOperationException("Windows identity unavailable.");
        var acl = new DirectorySecurity(); acl.SetAccessRuleProtection(true, false);
        acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(acl);
        var temp = discoveryPath + "." + Guid.NewGuid().ToString("N");
        try { File.WriteAllText(temp, JsonSerializer.Serialize(discovery, EditorJsonContext.Default.EditorDiscovery)); File.Move(temp, discoveryPath, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        started = true; accept = AcceptAsync();
    }
    private async Task AcceptAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(discovery.Pipe, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stop.Token);
                clients.RemoveAll(t => t.IsCompleted); clients.Add(ServeAsync(pipe));
            }
            catch (Exception) when (stop.IsCancellationRequested) { pipe?.Dispose(); break; }
            catch { pipe?.Dispose(); await Task.Delay(100, stop.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing); }
        }
    }
    private async Task ServeAsync(NamedPipeServerStream pipe)
    {
        using (pipe)
        using (var wire = new EditorWire(pipe))
        {
            var owned = false;
            try
            {
                using var handshake = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); handshake.CancelAfter(TimeSpan.FromSeconds(5));
                var hello = await wire.ReadAsync(handshake.Token);
                if (hello?.Type != "hello" || hello.Version != EditorProtocol.Version || hello.Token is null || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(hello.Token), Encoding.UTF8.GetBytes(discovery.Token))) return;
                owned = Interlocked.CompareExchange(ref active, wire, null) is null;
                if (!owned) { await wire.WriteAsync(new("error", Message: "Another VS Code window is connected. Close its SharpVoice session first."), handshake.Token); return; }
                var clientId = Guid.NewGuid().ToString("N"); activeClient = clientId;
                if (GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var peerPid) == 0) throw new IOException("Cannot identify the editor peer.");
                using var peer = Process.GetProcessById(checked((int)peerPid));
                var peerScope = peerPid.ToString("x") + "-" + peer.StartTime.ToUniversalTime().Ticks.ToString("x");
                await wire.WriteAsync(new("hello", Message: $"SharpVoice editor protocol {EditorProtocol.Version}", HostId: hostId, ClientId: clientId), handshake.Token);
                while (!stop.IsCancellationRequested)
                {
                    var frame = await wire.ReadAsync(stop.Token); if (frame is null) break;
                    if (frame.Version != EditorProtocol.Version || frame.Id.Length > 128 || frame.Token is not null || frame.HostId != hostId || frame.ClientId != clientId || frame.Workspace?.Length > 8192) throw new InvalidDataException("Invalid editor envelope.");
                    EditorFrame reply;
                    try { reply = await handle(frame with { ClientScope = peerScope }, stop.Token).WaitAsync(stop.Token); }
                    catch (Exception) when (stop.IsCancellationRequested) { break; }
                    catch (Exception ex) { reply = new("error", frame.Id, Message: ex.Message.Length > 2000 ? "Editor request failed." : ex.Message); }
                    await wire.WriteAsync(reply with { HostId = hostId, ClientId = clientId }, stop.Token);
                }
            }
            catch (Exception) { /* Malformed, disconnected and cancelled clients have no source/log side effects. */ }
            finally { if (owned && Interlocked.CompareExchange(ref active, null, wire) == wire) disconnected(); }
        }
    }
    public string? ConnectedClientId => Volatile.Read(ref active) is null ? null : Volatile.Read(ref activeClient);
    public async Task SendAsync(EditorFrame frame)
    {
        var wire = active ?? throw new IOException("VS Code disconnected.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); deadline.CancelAfter(TimeSpan.FromSeconds(5));
        var client = activeClient;
        if (frame.ClientId is not null && frame.ClientId != client) throw new IOException("Editor subscription expired.");
        await wire.WriteAsync(frame with { HostId = hostId, ClientId = client }, deadline.Token);
    }
    [LibraryImport("kernel32.dll", SetLastError = true)] private static partial int GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint pid);
    public void Dispose()
    {
        stop.Cancel(); Interlocked.Exchange(ref active, null)?.Dispose();
        try { accept.GetAwaiter().GetResult(); Task.WhenAll(clients).GetAwaiter().GetResult(); } catch { }
        if (started)
        {
            try { var saved = JsonSerializer.Deserialize(File.ReadAllText(discoveryPath), EditorJsonContext.Default.EditorDiscovery); if (saved?.Token == discovery.Token) File.Delete(discoveryPath); } catch { }
        }
        stop.Dispose();
    }
}

public sealed class EditorWire(Stream stream) : IDisposable
{
    private readonly SemaphoreSlim writing = new(1, 1);
    private readonly byte[] buffer = new byte[4096];
    private int offset, count;
    public async Task<EditorFrame?> ReadAsync(CancellationToken ct)
    {
        using var line = new MemoryStream();
        while (true)
        {
            if (offset == count) { count = await stream.ReadAsync(buffer, ct); offset = 0; if (count == 0) { if (line.Length != 0) throw new InvalidDataException("Incomplete editor frame."); return null; } }
            var end = Array.IndexOf(buffer, (byte)'\n', offset, count - offset);
            var take = (end < 0 ? count : end) - offset;
            if (line.Length + take > EditorValidation.MaxFrame) throw new InvalidDataException("Editor frame too large.");
            line.Write(buffer, offset, take); offset += take;
            if (end < 0) continue;
            offset++;
            return JsonSerializer.Deserialize(line.GetBuffer().AsSpan(0, (int)line.Length), EditorJsonContext.Default.EditorFrame) ?? throw new InvalidDataException("Empty editor frame.");
        }
    }
    public async Task WriteAsync(EditorFrame frame, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(frame, EditorJsonContext.Default.EditorFrame);
        if (bytes.Length > EditorValidation.MaxFrame) throw new InvalidDataException("Editor frame too large.");
        await writing.WaitAsync(ct);
        try { await stream.WriteAsync(bytes, ct); await stream.WriteAsync("\n"u8.ToArray(), ct); await stream.FlushAsync(ct); }
        finally { writing.Release(); }
    }
    public void Dispose() { stream.Dispose(); /* In-flight writers own their semaphore until completion. */ }
}
