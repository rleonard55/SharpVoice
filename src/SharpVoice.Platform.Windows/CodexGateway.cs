using System.Text.Json;
using SharpVoice.Core;
namespace SharpVoice.Platform.Windows;

public sealed class CodexGateway : IAsyncDisposable
{
    private readonly IConnectedRpc rpc;private readonly string thread;
    public IReadOnlyList<ConnectedTool> Tools { get; }
    private CodexGateway(IConnectedRpc rpc,string thread,List<ConnectedTool> tools)=>(this.rpc,this.thread,Tools)=(rpc,thread,tools);
    public static async Task<CodexGateway> OpenAsync(IConnectedRpc rpc,CancellationToken ct)
    {
        await rpc.CallAsync("initialize",ConnectedJson.Parse("{\"clientInfo\":{\"name\":\"sharpvoice\",\"title\":\"SharpVoice\",\"version\":\"0.1\"},\"capabilities\":{\"experimentalApi\":true}}"),ct);
        var start=await rpc.CallAsync("thread/start",ConnectedJson.Parse("{\"ephemeral\":true,\"environments\":[],\"sandbox\":\"read-only\",\"approvalPolicy\":\"on-request\",\"approvalsReviewer\":\"user\",\"baseInstructions\":\"SharpVoice explicit MCP gateway. No autonomous inference turns.\"}"),ct);
        var id=start.GetProperty("thread").GetProperty("id").GetString();if(string.IsNullOrWhiteSpace(id)||id.Length>512)throw new InvalidDataException("Codex returned no gateway thread.");
        var tools=new List<ConnectedTool>();var seen=new HashSet<string>(StringComparer.Ordinal);string? cursor=null;var cursors=new HashSet<string>(StringComparer.Ordinal);var retainedCharacters=0;
        for(var page=0;page<20;page++)
        {
            var args=ConnectedJson.Build(w=>{w.WriteStartObject();w.WriteString("threadId",id);w.WriteString("detail","toolsAndAuthOnly");w.WriteNumber("limit",50);if(cursor is not null)w.WriteString("cursor",cursor);w.WriteEndObject();});
            var response=await rpc.CallAsync("mcpServerStatus/list",ConnectedJson.Parse(args),ct);
            foreach(var server in response.GetProperty("data").EnumerateArray())
            {
                if(!server.TryGetProperty("runtimeStatus",out var status)||status.GetString()!="connected"||server.TryGetProperty("toolsError",out var error)&&error.ValueKind!=JsonValueKind.Null)continue;
                var name=server.GetProperty("name").GetString()!;if(name.Length>256||name.Contains("::"))continue;
                foreach(var entry in server.GetProperty("tools").EnumerateObject())
                {
                    var tool=entry.Value;var toolName=tool.GetProperty("name").GetString()!;
                    if(toolName.Length>256||toolName.Contains("::")||!tool.TryGetProperty("inputSchema",out var schema)||schema.ValueKind!=JsonValueKind.Object||schema.GetRawText().Length>32000)continue;
                    var read=false;var destructive=false;
                    if(tool.TryGetProperty("annotations",out var annotations)&&annotations.ValueKind==JsonValueKind.Object)
                    {read=annotations.TryGetProperty("readOnlyHint",out var hint)&&hint.ValueKind==JsonValueKind.True;destructive=annotations.TryGetProperty("destructiveHint",out hint)&&hint.ValueKind==JsonValueKind.True;}
                    var description=tool.TryGetProperty("description",out var d)&&d.ValueKind==JsonValueKind.String?d.GetString()!:toolName;
                    if(description.Length>4000)description=description[..4000];
                    var candidate=new ConnectedTool(name,toolName,description,schema.Clone(),read,destructive);
                    if(!seen.Add(candidate.Key))throw new InvalidDataException("Codex returned duplicate tool identities.");
                    retainedCharacters+=name.Length+toolName.Length+description.Length+schema.GetRawText().Length;
                    if(tools.Count>=ConnectedCatalogLimits.Tools||retainedCharacters>ConnectedCatalogLimits.RetainedCharacters)throw new InvalidDataException(ConnectedCatalogLimits.TooLarge);
                    tools.Add(candidate);
                }
            }
            cursor=response.TryGetProperty("nextCursor",out var next)&&next.ValueKind==JsonValueKind.String?next.GetString():null;
            if(cursor is null)return new(rpc,id,tools);
            if(!cursors.Add(cursor))throw new InvalidDataException("Codex repeated a catalog page.");
        }
        throw new InvalidDataException("Codex catalog exceeded its page limit.");
    }
    public async Task<JsonElement> InvokeAsync(ConnectedTool tool,JsonElement arguments,IReadOnlySet<string> allowed,bool confirmed,CancellationToken ct)
    {
        var current=Tools.SingleOrDefault(x=>x.Key==tool.Key)??throw new InvalidOperationException("This tool is not in the current Codex catalog.");
        if(!allowed.Contains(current.Key)||!current.Supported)throw new InvalidOperationException("This integration action is not enabled in SharpVoice.");
        if(arguments.ValueKind!=JsonValueKind.Object||arguments.GetRawText().Length>32000)throw new InvalidOperationException("Invalid connected-tool arguments.");
        if(!current.CanRead){if(!confirmed)throw new InvalidOperationException("Review and confirm this change on screen first.");ConnectedPolicy.Preview(current,arguments);}
        var parameters=ConnectedJson.Build(w=>{w.WriteStartObject();w.WriteString("threadId",thread);w.WriteString("server",current.Server);w.WriteString("tool",current.Name);w.WritePropertyName("arguments");arguments.WriteTo(w);w.WriteEndObject();});
        return await rpc.CallAsync("mcpServer/tool/call",ConnectedJson.Parse(parameters),ct);
    }
    public ValueTask DisposeAsync()=>rpc.DisposeAsync();
}
