using System.Runtime.InteropServices;
using SharpVoice.Core;
using SharpVoice.Platform.Windows.Native;
using SharpVoice.UI;
internal static partial class WindowsHelpSmoke
{
    [DllImport("user32.dll",EntryPoint="IsWindowEnabled")] private static extern int ConnectedEnabled(nint hwnd);
    private static unsafe void CheckConnected(NativeDesktop desktop, Action<ThemeColors> changeTheme, string? output)
    {
        string Text(nint c){var buffer=new char[4096];fixed(char* p=buffer)return new string(buffer,0,Win32.GetWindowTextW(c,p,buffer.Length));}
        var reviewErrors=new List<string>();
        var approved=0;desktop.ConnectedConfirmRequested=_=>approved++;
        desktop.ShowConnected(new("Reading Gmail","Searching your selected integration"));Pump();
        if(desktop.ConnectedHandle==0||Win32.IsWindowVisible(desktop.ConnectedHandle)==0)throw new Exception("Connected requests have no visible branded popup.");
        desktop.FinishConnectedCancellation();
        if(ConnectedEnabled(Win32.GetDlgItem(desktop.ConnectedHandle,4607))==0)reviewErrors.Add("Canceled connected requests leave follow-up controls disabled.");
        desktop.SetConnectedRecording(true);desktop.SetConnectedRecording(false);
        if(ConnectedEnabled(Win32.GetDlgItem(desktop.ConnectedHandle,4608))==0)reviewErrors.Add("Empty or canceled recording prevents a new voice follow-up.");
        var change=ConnectedPolicy.Preview(ConnectedChecks.Send(),ConnectedChecks.Json(ConnectedChecks.Mail));
        desktop.ShowConnected(new("Review Required","Retrieved source email",change.Before,change.After,change));Pump();
        if(!Text(Win32.GetDlgItem(desktop.ConnectedHandle,4602)).Contains("Not Sent")||!Text(Win32.GetDlgItem(desktop.ConnectedHandle,4603)).Contains("audit@example.test"))throw new Exception("Connected diff hides the before state or recipients.");
        if(!Text(Win32.GetDlgItem(desktop.ConnectedHandle,4603)).Contains("Tuesday works.\r\nThanks!"))reviewErrors.Add("Multiline message body was not normalized for native review.");
        Win32.SetWindowTextW(Win32.GetDlgItem(desktop.ConnectedHandle,4606),"yes");
        Win32.SendMessageW(desktop.ConnectedHandle,0x111,4607,0);
        if(approved!=0)throw new Exception("Typed/voice-like confirmation bypassed the on-screen action.");
        Win32.SendMessageW(desktop.ConnectedHandle,0x111,4605,0);Win32.SendMessageW(desktop.ConnectedHandle,0x111,4605,0);
        if(approved!=1)throw new Exception("Connected review did not enforce one on-screen confirmation.");
        foreach(var palette in new[]{ThemeColors.Light,ThemeColors.DarkTheme})
        {changeTheme(palette);Win32.SendMessageW(desktop.SettingsHandle,0x1A,0,0);Pump();if(RenderHelp(desktop.ConnectedHandle,output is null?null:output+(palette.Dark?"-connected-dark.bmp":"-connected-light.bmp"),60)!=palette.Background)throw new Exception("Connected popup ignored the OS theme.");}
        desktop.ShowConnected(new("Review Required","source",change.Before,change.After,change));
        desktop.HideConnected();Win32.SendMessageW(desktop.ConnectedHandle,0x111,4605,0);
        if(approved!=1)throw new Exception("A closed connected preview approved a change.");
        var discoveries=0;AppConfig? saved=null;desktop.ConfigChanged=next=>saved=next;
        desktop.ConnectedDiscoveryRequested=(path,ct)=>{discoveries++;return Task.FromResult(new[]{ConnectedChecks.Send() with { Name="aaa_unsupported" },ConnectedChecks.Send()}.Concat(Enumerable.Range(0,70).Select(i=>ConnectedChecks.Send() with {Name=$"read_{i:D2}",ReadOnly=true})).ToArray());};
        Win32.SendMessageW(desktop.SettingsHandle,0x111,120,0);Pump();
        if(discoveries!=0||Win32.SendMessageW(desktop.ToolField("ConnectedAsk"),0x147,0,0)!=0)throw new Exception("Connected settings were enabled or accessed integrations on open.");
        Win32.SendMessageW(desktop.ToolsWindow,0x111,4471,0);
        for(var i=0;i<40;i++){Pump();if(Win32.SendMessageW(Win32.GetDlgItem(desktop.ToolsWindow,4101),0x18B,0,0)==71)break;}
        if(Win32.SendMessageW(Win32.GetDlgItem(desktop.ToolsWindow,4101),0x18B,0,0)!=71)throw new Exception("Unavailable tools were not hidden by default.");
        if(discoveries!=1)throw new Exception("Connected discovery did not run exactly once.");
        Win32.SendMessageW(Win32.GetDlgItem(desktop.ToolsWindow,4101),0x186,0,0);Win32.SendMessageW(desktop.ToolsWindow,0x111,4472,0);
        Win32.SendMessageW(desktop.ToolField("ConnectedAsk"),0x14E,1,0);Win32.SendMessageW(desktop.ToolsWindow,0x111,4474,0);
        if(saved is null||!saved.ConnectedAsk||saved.ConnectedTools.Single()!=ConnectedChecks.Send().Key)throw new Exception("Connected permission preferences were not saved explicitly.");
        Win32.SendMessageW(desktop.SettingsHandle,0x111,120,0);Pump();
        Win32.SendMessageW(desktop.ToolsWindow,0x111,4471,0);
        for(var i=0;i<40;i++){Pump();if(Win32.SendMessageW(Win32.GetDlgItem(desktop.ToolsWindow,4101),0x18B,0,0)==71)break;}
        Win32.SendMessageW(desktop.ToolsWindow,0x111,4477,0);
        if(Win32.SendMessageW(Win32.GetDlgItem(desktop.ToolsWindow,4101),0x18B,0,0)!=72)throw new Exception("Show Unavailable did not reveal hidden capabilities.");
        Win32.SendMessageW(desktop.ToolsWindow,0x111,4476,0);
        foreach(var palette in new[]{ThemeColors.Light,ThemeColors.DarkTheme})
        {changeTheme(palette);Win32.SendMessageW(desktop.SettingsHandle,0x1A,0,0);Pump();RenderHelp(desktop.ToolsWindow,output is null?null:output+(palette.Dark?"-permissions-dark.bmp":"-permissions-light.bmp"),60);}
        Win32.SendMessageW(desktop.ToolsWindow,0x111,4474,0);
        if(saved.ConnectedTools.Length!=71||saved.ConnectedTools.Any(x=>x.EndsWith("aaa_unsupported",StringComparison.Ordinal)))throw new Exception("Allow All did not save all supported tools, or granted an unavailable action.");
        desktop.ConnectedDiscoveryRequested=null;
        desktop.ConnectedConfirmRequested=null;
        if(reviewErrors.Count>0)throw new Exception(string.Join("; ",reviewErrors));
        Console.WriteLine("PASS branded connected activity and full before/after diff with one-shot on-screen approval.");
    }
}
