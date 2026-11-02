using System.IO.Pipes;
using System.Text.Json;
using SharpVoice.Core;
using SharpVoice.Platform.Windows;

internal static class EditorBridgeChecks
{
    public static Task ConversationSelection()
    {
        var binding = new EditorWorkspaceBinding(); var store = new ConversationStore();
        var general = new ConversationScope("general", "", "fixture"); var code = general with { Domain = "code", Binding = "peer|workspace|file.cs" };
        var first = binding.ResolveConversation(store, general);
        if (binding.ConversationId != first.Id || store.Get(binding.ConversationId) is null) throw new Exception("Fresh generic panel Ask has no selected thread.");
        var attached = binding.ResolveConversation(store, code);
        if (attached.Id == first.Id) throw new Exception("Document Ask reused general context.");
        var returned = binding.ResolveConversation(store, general);
        if (binding.ConversationId != first.Id || returned.Id != first.Id) throw new Exception("Returning to general Ask retained an attached/code thread ID.");
        var latest = store.New(general); binding.ResolveConversation(store, general);
        if (binding.ConversationId != latest.Id) throw new Exception("Panel ignored the shared native active conversation.");
        return Task.CompletedTask;
    }
    public static Task Binding()
    {
        var binding = new EditorWorkspaceBinding(); binding.Bind("client-a", "peer-a", "workspace-a");
        binding.Mode="edit"; binding.IncludeFile=true; binding.Context=EditorChecks.Context();
        binding.Bind("client-b", "peer-a", "workspace-a");
        if(binding.Context is null)throw new Exception("Same authenticated peer reconnect discarded its document scope.");
        binding.Bind("client-c", "peer-b", "workspace-b");
        if(binding.Context is not null||binding.IncludeFile||binding.Mode!="ask")throw new Exception("A new editor peer inherited the previous window's document attachment.");
        binding.Mode="edit";binding.Context=EditorChecks.Context();binding.Bind("client-c","peer-b","workspace-c");
        if(binding.Context is not null||binding.Mode!="ask")throw new Exception("Workspace switch inherited old document context.");
        return Task.CompletedTask;
    }
    public static async Task Recovery()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "sharpvoice-editor-recovery-" + Guid.NewGuid());
        var path = Path.Combine(root, "connection.json");
        var clients = new List<NamedPipeClientStream>();
        try
        {
            using var bridge = new EditorBridge(path, (frame, _) => Task.FromResult(new EditorFrame("ack", frame.Id)), () => { }); bridge.Start();
            var discovery = JsonSerializer.Deserialize(File.ReadAllText(path), EditorJsonContext.Default.EditorDiscovery)!;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            for (var i = 0; i < 4; i++) { var client = new NamedPipeClientStream(".", discovery.Pipe, PipeDirection.InOut, PipeOptions.Asynchronous); clients.Add(client); await client.ConnectAsync(deadline.Token); }
            await Task.Delay(6000, deadline.Token);
            foreach (var client in clients) client.Dispose(); clients.Clear();
            using var valid = new NamedPipeClientStream(".", discovery.Pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
            await valid.ConnectAsync(deadline.Token); using var wire = new EditorWire(valid);
            await wire.WriteAsync(new("hello", Token: discovery.Token), deadline.Token);
            if ((await wire.ReadAsync(deadline.Token))?.Type != "hello") throw new Exception("Pipe did not recover after exhausted handshake instances.");
        }
        finally { foreach (var client in clients) client.Dispose(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    public static async Task Channel()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "sharpvoice-editor-" + Guid.NewGuid());
        var path = Path.Combine(root, "connection.json"); var calls = 0;
        try
        {
            using (var bridge = new EditorBridge(path, (frame, _) => { calls++; return Task.FromResult(new EditorFrame("ack", frame.Id)); }, () => { }))
            {
                bridge.Start(); var discovery = JsonSerializer.Deserialize(File.ReadAllText(path), EditorJsonContext.Default.EditorDiscovery)!;
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using (var bad = new NamedPipeClientStream(".", discovery.Pipe, PipeDirection.InOut, PipeOptions.Asynchronous))
                { await bad.ConnectAsync(deadline.Token); using var wire = new EditorWire(bad); await wire.WriteAsync(new("hello", Token: "wrong"), deadline.Token); if (await wire.ReadAsync(deadline.Token) is not null) throw new Exception("Bad capability accepted."); }
                using (var old = new NamedPipeClientStream(".", discovery.Pipe, PipeDirection.InOut, PipeOptions.Asynchronous))
                { await old.ConnectAsync(deadline.Token);using var wire = new EditorWire(old);await wire.WriteAsync(new("hello", Token:discovery.Token, Version:1),deadline.Token);if(await wire.ReadAsync(deadline.Token) is not null)throw new Exception("Old plugin protocol was accepted."); }
                using var client = new NamedPipeClientStream(".", discovery.Pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
                await client.ConnectAsync(deadline.Token); using var good = new EditorWire(client);
                await good.WriteAsync(new("hello", Token: discovery.Token), deadline.Token);
                var hello = await good.ReadAsync(deadline.Token);
                if (hello?.Type != "hello" || string.IsNullOrEmpty(hello.HostId) || string.IsNullOrEmpty(hello.ClientId)) throw new Exception("Handshake failed to bind the workspace host/client.");
                await good.WriteAsync(new("stop", "request", HostId: hello.HostId, ClientId: hello.ClientId), deadline.Token);
                if ((await good.ReadAsync(deadline.Token))?.Id != "request" || calls != 1) throw new Exception("Request routing failed.");
                using var excess = new MemoryStream(new byte[EditorValidation.MaxFrame + 1].Select(_ => (byte)'x').ToArray());
                using var large = new EditorWire(excess);
                try { await large.ReadAsync(deadline.Token); throw new Exception("Oversized frame accepted."); } catch (InvalidDataException) { }
            }
            if (File.Exists(path)) throw new Exception("Stale discovery retained.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
