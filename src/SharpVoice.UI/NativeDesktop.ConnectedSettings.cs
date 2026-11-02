using SharpVoice.Core;
using SharpVoice.Platform.Windows.Native;
namespace SharpVoice.UI;
public sealed partial class NativeDesktop
{
    public Func<string,CancellationToken,Task<ConnectedTool[]>>? ConnectedDiscoveryRequested {get;set;}
    private readonly List<ConnectedTool> connectedCatalog=[];private readonly HashSet<string> connectedAllowed=new(StringComparer.Ordinal);
    private readonly List<ConnectedTool> connectedVisible=[];private bool connectedShowUnavailable;
    private CancellationTokenSource? connectedDiscovery;private long connectedDiscoveryGeneration;
    private void OpenConnectedSettings()
    {
        StartToolsWindow("connected","Connected Ask AI");connectedCatalog.Clear();connectedVisible.Clear();connectedShowUnavailable=false;connectedAllowed.Clear();connectedAllowed.UnionWith(config.ConnectedTools);
        Win32.SetWindowTextW(toolsNotice,"Use existing Windows Codex connections. Reads show activity; supported changes require a complete diff and on-screen approval.");
        ToolCombo("ConnectedAsk","Connected Ask AI",104,["Off (Default)","On (Use Selected Codex Tools)"]);Win32.SendMessageW(toolFields["ConnectedAsk"],0x14E,config.ConnectedAsk?1u:0u,0);
        ToolEdit("ConnectedPath","Windows Codex Executable (Blank: Auto-Detect)",190);Win32.SetWindowTextW(toolFields["ConnectedPath"],config.ConnectedCodexPath);
        ToolEdit("ConnectedState","Connection and Permissions",276,96,multiline:true,readOnly:true);
        ToolEdit("ConnectedTool","Selected Capability",414,96,multiline:true,readOnly:true);
        Win32.SetWindowTextW(toolFields["ConnectedState"],"Off by default. Discover reads tool metadata only. ChatGPT account sign-in supplies answers; Codex supplies your existing tools. WSL profiles are separate.");
        ToolButton("Allow All Available Tools",4476,288,550,250);ToolButton("Allow / Remove Tool",4472,550,550,258);
        ToolButton("Discover / Test Connection",4471,288,596,250);ToolButton("Clear Permissions",4473,550,596,258);
        ToolButton("ChatGPT Account Settings",4475,288,642,250);ToolButton("Show Unavailable (0)",4477,550,642,258);
        ToolButton("Save Changes",4474,24,692,180);ToolButton("Cancel",4205,640,692,168);RenderConnectedCatalog();FinishToolsWindow();RefreshConnectedAccount();
    }
    private void RefreshConnectedAccount()
    {
        if(toolsKind!="connected")return;
        foreach(var button in toolButtons.Where(x=>x.Value==4475))
        {Win32.SetWindowTextW(button.Key,IsChatGptSignedIn?.Invoke()==true?"ChatGPT Sign Out":"ChatGPT Sign In");Win32.EnableWindow(button.Key,IsAuthenticationBusy?.Invoke()==true?0:1);}
    }
    private void ConnectedAccount()
    {
        if(IsAuthenticationBusy?.Invoke()==true)return;
        if(IsChatGptSignedIn?.Invoke()==true)SignOutRequested?.Invoke();else SignInRequested?.Invoke();
        RefreshConnectedAccount();
    }
    private void RenderConnectedCatalog(int selected=-1)
    {
        Win32.SendMessageW(toolsList,0x184,0,0);
        connectedVisible.Clear();connectedVisible.AddRange(connectedCatalog.Where(x=>connectedShowUnavailable||x.Supported));
        foreach(var tool in connectedVisible)AddToolListItem((connectedAllowed.Contains(tool.Key)?"✓ ":"  ")+(tool.CanRead?"Read · ":tool.CreationLabel is not null?"Review · ":"Unavailable · ")+tool.Key);
        if(selected>=0&&selected<connectedVisible.Count)Win32.SendMessageW(toolsList,0x186,(nuint)selected,0);
        foreach(var button in toolButtons)
        {
            if(button.Value==4477){Win32.SetWindowTextW(button.Key,$"{(connectedShowUnavailable?"Hide":"Show")} Unavailable ({connectedCatalog.Count(x=>!x.Supported)})");Win32.EnableWindow(button.Key,connectedCatalog.Any(x=>!x.Supported)?1:0);}
            if(button.Value==4476)Win32.EnableWindow(button.Key,connectedCatalog.Any(x=>x.Supported&&!connectedAllowed.Contains(x.Key))?1:0);
        }
        SelectConnectedTool();
    }
    private void SelectConnectedTool()
    {
        if(toolsKind!="connected")return;var i=(int)Win32.SendMessageW(toolsList,0x188,0,0);
        var detail=i<0||i>=connectedVisible.Count?connectedCatalog.Count==0?"Discover tools to choose individual capabilities or Allow All Available Tools.":"Choose a tool. Unavailable actions are hidden by default; use Show Unavailable to inspect them.":connectedVisible[i].Key+"\r\n"+connectedVisible[i].Description+"\r\n"+(connectedVisible[i].CanRead?"Read Only":connectedVisible[i].CreationLabel is not null?"Complete Creation Diff + Confirmation Required":"Unavailable: No Verified Change Adapter");
        Win32.SetWindowTextW(toolFields["ConnectedTool"],detail);
    }
    private void ToggleConnectedTool()
    {
        var i=(int)Win32.SendMessageW(toolsList,0x188,0,0);if(i<0||i>=connectedVisible.Count)return;var tool=connectedVisible[i];
        if(connectedAllowed.Contains(tool.Key))connectedAllowed.Remove(tool.Key);
        else if(tool.Supported&&connectedAllowed.Count<AppConfig.MaximumConnectedTools)connectedAllowed.Add(tool.Key);
        else{Win32.SetWindowTextW(toolFields["ConnectedState"],"This tool is unavailable, or the permission limit has been reached. Nothing was enabled.");return;}
        RenderConnectedCatalog(i);
    }
    private void AllowAllConnectedTools()
    {
        var available=connectedCatalog.Where(x=>x.Supported).Select(x=>x.Key).ToArray();
        if(available.Length==0){Win32.SetWindowTextW(toolFields["ConnectedState"],"Discover available tools first. No permissions were changed.");return;}
        if(connectedAllowed.Union(available,StringComparer.Ordinal).Count()>AppConfig.MaximumConnectedTools)
        {Win32.SetWindowTextW(toolFields["ConnectedState"],"Clear old permissions before allowing this catalog. No permissions were changed.");return;}
        connectedAllowed.UnionWith(available);RenderConnectedCatalog();
        Win32.SetWindowTextW(toolFields["ConnectedState"],$"All {available.Length} available tools are allowed in this draft. Save Changes to apply. Changes still require a complete diff and on-screen confirmation. Newly discovered tools need permission separately.");
    }
    private void ToggleUnavailableConnectedTools()
    {
        var i=(int)Win32.SendMessageW(toolsList,0x188,0,0);var key=i>=0&&i<connectedVisible.Count?connectedVisible[i].Key:null;
        connectedShowUnavailable=!connectedShowUnavailable;RenderConnectedCatalog();
        var selected=connectedVisible.FindIndex(x=>x.Key==key);if(selected>=0){Win32.SendMessageW(toolsList,0x186,(nuint)selected,0);SelectConnectedTool();}
    }
    private void CancelConnectedDiscovery()
    {
        ++connectedDiscoveryGeneration;var old=connectedDiscovery;connectedDiscovery=null;old?.Cancel();if(disposed)old?.Dispose();
    }
    private void DiscoverConnected()
    {
        if(toolsKind!="connected"||connectedDiscovery is not null)return;
        var owner=new CancellationTokenSource(TimeSpan.FromSeconds(90));connectedDiscovery=owner;var id=++connectedDiscoveryGeneration;var handler=ConnectedDiscoveryRequested;var path=Text(toolFields["ConnectedPath"]);
        Win32.SetWindowTextW(toolFields["ConnectedState"],"Discovering the selected Windows Codex profile. No email contents or actions are requested…");
        _=CompleteConnectedDiscoveryAsync(Task.Run(()=>handler is null?Task.FromException<ConnectedTool[]>(new InvalidOperationException("Connected discovery unavailable.")):handler(path,owner.Token)),owner,id);
    }
    private async Task CompleteConnectedDiscoveryAsync(Task<ConnectedTool[]> task,CancellationTokenSource owner,long id)
    {
        ConnectedTool[]? result=null;string? error=null;
        try{result=await task.ConfigureAwait(false);}catch(OperationCanceledException){error="Discovery canceled or timed out.";}catch(Exception ex){error=ex is InvalidOperationException or InvalidDataException?ex.Message:"Could not discover Codex tools. Check its Windows installation and sign-in.";}
        var canceled=owner.IsCancellationRequested;
        if(disposed){owner.Dispose();return;}
        Post(()=>
        {
            if(id!=connectedDiscoveryGeneration||!ReferenceEquals(owner,connectedDiscovery)||toolsKind!="connected"){owner.Dispose();return;}
            connectedDiscovery=null;owner.Dispose();
            if(result is not null&&!canceled)
            {
                connectedCatalog.Clear();connectedCatalog.AddRange(result.OrderBy(x=>x.Server,StringComparer.Ordinal).ThenBy(x=>x.Name,StringComparer.Ordinal));RenderConnectedCatalog();
                Win32.SetWindowTextW(toolFields["ConnectedState"],result.Length==0?"No connected tools with inspectable schemas are exposed by this Windows Codex profile. Enable connections in Codex first; ChatGPT-only or WSL connections may differ.":$"Runtime: {(Text(toolFields["ConnectedPath"]).Trim() is {Length:>0} selectedPath ? selectedPath : SharpVoice.Platform.Windows.CodexRpc.FindExecutable()) ?? "Select a Windows Codex executable"}\r\nProfile: {Environment.GetEnvironmentVariable("CODEX_HOME") ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex")}\r\n{result.Count(x=>x.Supported)} available · {result.Count(x=>!x.Supported)} unavailable (hidden by default). {connectedAllowed.Count} permissions in this draft. Choose individual tools or Allow All Available Tools, then Save Changes. Changes still require review.");
            }
            else Win32.SetWindowTextW(toolFields["ConnectedState"],error??"Discovery canceled.");
        });
    }
    private void SaveConnectedSettings()
    {
        if(active)throw new InvalidOperationException("Finish the current operation first.");
        var next=config with{ConnectedAsk=Win32.SendMessageW(toolFields["ConnectedAsk"],0x147,0,0)==1,ConnectedCodexPath=Text(toolFields["ConnectedPath"]).Trim(),ConnectedTools=connectedAllowed.Order(StringComparer.Ordinal).ToArray()};
        if(next.ConnectedAsk&&next.ConnectedTools.Length==0)throw new InvalidOperationException("Discover and allow at least one tool before enabling Connected Ask.");
        next.Validate();ConfigChanged?.Invoke(next);config=next;CloseTools();
    }
}
