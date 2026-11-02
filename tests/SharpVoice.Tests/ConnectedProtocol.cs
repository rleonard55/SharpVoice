using System.Text.Json;
using SharpVoice.Core;
using SharpVoice.Platform.Windows;
internal static class ConnectedProtocol
{
    public static async Task Serve()
    {
        string? line;
        while((line=await Console.In.ReadLineAsync()) is not null)
        {
            var request=ConnectedJson.Parse(line);if(!request.TryGetProperty("id",out var id))continue;
            var method=request.GetProperty("method").GetString();
            var result=method switch
            {
                "initialize"=>"{\"protocolVersion\":\"2024-11-05\",\"capabilities\":{\"tools\":{}},\"serverInfo\":{\"name\":\"SharpVoiceSyntheticFixture\",\"version\":\"1\"}}",
                "tools/list"=>"{\"tools\":[{\"name\":\"fixture_read\",\"description\":\"Synthetic local result only\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}},\"annotations\":{\"readOnlyHint\":true,\"destructiveHint\":false}}]}",
                "tools/call"=>"{\"content\":[{\"type\":\"text\",\"text\":\"SHARPVOICE_SYNTHETIC_ONLY\"}]}",
                _=>"{}"
            };
            Console.WriteLine(ConnectedJson.Build(w=>{w.WriteStartObject();w.WriteString("jsonrpc","2.0");w.WritePropertyName("id");id.WriteTo(w);w.WritePropertyName("result");ConnectedJson.Parse(result).WriteTo(w);w.WriteEndObject();}));
        }
    }
    public static async Task Probe(string executable,string directory)
    {
        directory=Path.GetFullPath(directory);Directory.CreateDirectory(directory);
        static string Quote(string s)=>ConnectedJson.Build(w=>w.WriteStringValue(s));
        File.WriteAllText(Path.Combine(directory,"config.toml"),"[mcp_servers.sharpvoice_fixture]\ncommand = "+Quote(Environment.ProcessPath!)+"\nargs = [\"--connected-fake-mcp\"]\n");
        var start=new System.Diagnostics.ProcessStartInfo(executable){WorkingDirectory=directory};start.Environment["CODEX_HOME"]=directory;start.ArgumentList.Add("app-server");start.ArgumentList.Add("--listen");start.ArgumentList.Add("stdio://");
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var rpc=new CodexRpc(start);await using var gateway=await CodexGateway.OpenAsync(rpc,timeout.Token);
        var tool=gateway.Tools.SingleOrDefault(x=>x.Name=="fixture_read")??throw new Exception("Isolated Codex did not expose its synthetic MCP tool.");
        var result=await gateway.InvokeAsync(tool,ConnectedJson.Parse("{}"),new HashSet<string>{tool.Key},false,timeout.Token);
        if(!result.GetRawText().Contains("SHARPVOICE_SYNTHETIC_ONLY"))throw new Exception("Isolated Codex gateway lost the actual tool result.");
        Console.WriteLine("PASS real Windows Codex app-server catalog and explicit synthetic MCP call, isolated CODEX_HOME, no inference turn or live accounts.");
    }
}
