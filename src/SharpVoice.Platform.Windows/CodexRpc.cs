using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SharpVoice.Core;
namespace SharpVoice.Platform.Windows;

public interface IConnectedRpc : IAsyncDisposable
{
    Task<JsonElement> CallAsync(string method,JsonElement parameters,CancellationToken ct);
}
/// <summary>Private sequential JSON-RPC transport. Never starts agent inference or edits Codex configuration.</summary>
public sealed class CodexRpc : IConnectedRpc
{
    private readonly Process child;private readonly string? directory;
    private readonly SemaphoreSlim gate=new(1,1);private readonly CancellationTokenSource stopped=new();private int disposed;private int sequence;private readonly Task diagnostics;
    private readonly char[] buffer=new char[4096];private int offset,available;
    private const int ResponseLimit=1_048_576;
    private static readonly string[] Methods=["initialize","thread/start","mcpServerStatus/list","mcpServer/tool/call","app/installed","app/list","app/read"];
    public static string? FindExecutable()
    {
        foreach(var folder in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator).Select(x=>x.Trim(' ','"')))
        {if(!Path.IsPathFullyQualified(folder))continue;var p=Path.Combine(folder,"codex.exe");if(File.Exists(p))return p;}
        var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".vscode","extensions");
        if(Directory.Exists(root))foreach(var dir in Directory.EnumerateDirectories(root,"openai.chatgpt-*").OrderDescending())
        {var p=Path.Combine(dir,"bin","windows-x86_64","codex.exe");if(File.Exists(p))return p;}
        return null;
    }
    public static CodexRpc Start(string? executable=null)
    {
        executable=string.IsNullOrWhiteSpace(executable)?FindExecutable():executable;
        if(executable is null||!Path.IsPathFullyQualified(executable)||!File.Exists(executable)||!executable.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Install or select the Windows Codex CLI. WSL profiles are separate and are not supported by this connection.");
        var root=Path.Combine(Path.GetTempPath(),"SharpVoice-Codex-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var start=new ProcessStartInfo(executable){WorkingDirectory=root};start.ArgumentList.Add("app-server");start.ArgumentList.Add("--listen");start.ArgumentList.Add("stdio://");
        try{return new(start,root);}catch{Directory.Delete(root,true);throw;}
    }
    internal CodexRpc(ProcessStartInfo start,string? directory=null)
    {
        this.directory=directory;start.UseShellExecute=false;start.CreateNoWindow=true;start.RedirectStandardInput=true;start.RedirectStandardOutput=true;start.RedirectStandardError=true;
        start.StandardInputEncoding=new UTF8Encoding(false);start.StandardOutputEncoding=Encoding.UTF8;
        child=Process.Start(start)??throw new InvalidOperationException("Codex could not start.");diagnostics=DrainAsync();
    }
    private async Task DrainAsync()
    {try{var chars=new char[2048];var total=0;int n;while((n=await child.StandardError.ReadAsync(chars,stopped.Token))!=0){if((total+=n)>131072){Abort();break;}}}catch(Exception){}}
    private void Abort(){stopped.Cancel();try{if(!child.HasExited)child.Kill(true);}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}}
    private async Task<string> ReadLineAsync(string method,CancellationToken ct)
    {
        // Catalog pages contain every tool schema for a server, including aggregated app tools.
        // The server-count pagination limit does not bound an individual server's tool catalog.
        var limit=method=="mcpServerStatus/list"?ConnectedCatalogLimits.ResponseCharacters:ResponseLimit;
        var line=new StringBuilder();
        while(true)
        {
            if(offset==available)
            {
                available=await child.StandardOutput.ReadAsync(buffer,ct);offset=0;
                if(available==0)throw new IOException("Codex disconnected.");
            }
            var newline=Array.IndexOf(buffer,'\n',offset,available-offset);
            var count=(newline<0?available:newline)-offset;
            if(line.Length+count>limit)throw new InvalidDataException(method=="mcpServerStatus/list"
                ?ConnectedCatalogLimits.TooLarge
                :"The Codex response for "+method+" is too large. Try a narrower request with fewer results.");
            line.Append(buffer,offset,count);offset+=count;
            if(newline>=0){offset++;return line.ToString().TrimEnd('\r');}
        }
    }
    public async Task<JsonElement> CallAsync(string method,JsonElement parameters,CancellationToken ct)
    {
        if(!Methods.Contains(method))throw new InvalidOperationException("This Codex operation is not available to Connected Ask.");
        await gate.WaitAsync(ct);
        try
        {
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct,stopped.Token);deadline.CancelAfter(TimeSpan.FromSeconds(90));var token=deadline.Token;var id=++sequence;
            var request=ConnectedJson.Build(w=>{w.WriteStartObject();w.WriteString("method",method);w.WriteNumber("id",id);w.WritePropertyName("params");parameters.WriteTo(w);w.WriteEndObject();});
            if(request.Length>262144)throw new InvalidOperationException("Connected request is too large.");
            await child.StandardInput.WriteLineAsync(request.AsMemory(),token);await child.StandardInput.FlushAsync(token);
            var messages=0;
            while(++messages<=10000)
            {
                var line=await ReadLineAsync(method,token);if(line.Length==0)continue;
                // Real tool schemas can nest beyond the shallow planner/tool-result format.
                // Keep both the larger size and depth allowances confined to catalog discovery.
                using var document=JsonDocument.Parse(line,new JsonDocumentOptions{MaxDepth=method=="mcpServerStatus/list"?ConnectedCatalogLimits.JsonDepth:24});
                var message=document.RootElement;
                if(message.TryGetProperty("method",out _)&&message.TryGetProperty("id",out var serverId))
                {
                    // No external runtime prompt, shell approval or unknown request can bypass our own review.
                    var denial=ConnectedJson.Build(w=>{w.WriteStartObject();w.WritePropertyName("id");serverId.WriteTo(w);w.WritePropertyName("error");w.WriteStartObject();w.WriteNumber("code",-32601);w.WriteString("message","SharpVoice does not grant runtime permissions or implicit tool approvals.");w.WriteEndObject();w.WriteEndObject();});
                    await child.StandardInput.WriteLineAsync(denial.AsMemory(),token);await child.StandardInput.FlushAsync(token);continue;
                }
                if(!message.TryGetProperty("id",out var reply)||!reply.TryGetInt32(out var value)||value!=id)continue;
                if(message.TryGetProperty("error",out _))throw new InvalidOperationException("Codex could not complete "+method+". Check its sign-in, connection permissions and version.");
                if(!message.TryGetProperty("result",out var result))throw new InvalidDataException("Codex returned an incomplete response.");
                if(method=="initialize"){await child.StandardInput.WriteLineAsync("{\"method\":\"initialized\"}".AsMemory(),token);await child.StandardInput.FlushAsync(token);}
                return result.Clone();
            }
            throw new InvalidDataException("Codex sent too many intermediate messages.");
        }
        catch{Abort();ct.ThrowIfCancellationRequested();throw;}
        finally{gate.Release();}
    }
    public async ValueTask DisposeAsync()
    {
        if(Interlocked.Exchange(ref disposed,1)!=0)return;
        Abort();try{await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));}catch(Exception){}
        try{await diagnostics.WaitAsync(TimeSpan.FromSeconds(3));}catch(Exception){}child.Dispose();
        if(directory is not null)try{Directory.Delete(directory,true);}catch(IOException){}catch(UnauthorizedAccessException){}
    }
}
