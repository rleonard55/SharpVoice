using SharpVoice.Core;
using SharpVoice.Platform.Windows.Native;
using SharpVoice.UI;
internal static partial class WindowsHelpSmoke
{
    private static unsafe void CheckGeminiApi(NativeDesktop desktop,Action<ThemeColors> changeTheme,string? output)
    {
        var window=desktop.SettingsHandle;Win32.ShowWindow(window,4);var tabs=Win32.GetDlgItem(window,1014);
        Win32.SendMessageW(tabs,0x130C,1,0);var notice=new Win32.Notification {Window=tabs,Id=1014,Code=-551};Win32.SendMessageW(window,0x4E,1014,(nint)(&notice));
        var backend=Win32.GetDlgItem(window,1002);
        Win32.SendMessageW(backend,0x14E,4,0);Win32.SendMessageW(window,0x111,(1u<<16)|1002,backend);Win32.SetWindowTextW(Win32.GetDlgItem(window,1003),"gemini-3.8-flash-medium");
        Win32.SendMessageW(backend,0x14E,5,0);Win32.SendMessageW(window,0x111,(1u<<16)|1002,backend);
        if(Win32.IsWindowVisible(Win32.GetDlgItem(window,102))==0||Win32.IsWindowVisible(Win32.GetDlgItem(window,103))!=0)throw new Exception("Gemini API key management was hidden or showed an OAuth sign-out.");
        var key="";var saves=0;var removals=0;var probes=0;var validProbe=false;
        desktop.HasGeminiApiKey=()=>key.Length!=0;
        desktop.SaveGeminiApiKeyRequested=value=>{key=value;saves++;};desktop.RemoveGeminiApiKeyRequested=()=>{key="";removals++;};
        Win32.SendMessageW(window,0x111,102,0);var input=desktop.ToolField("GeminiKey");
        if(input==0||Win32.SendMessageW(input,0xD2,0,0)!=0x2022||Win32.GetWindowTextLengthW(input)!=0)throw new Exception("API key field was not masked and initially empty.");
        Win32.SetWindowTextW(input,"fixture-google-ui-key");
        foreach(var palette in new[]{ThemeColors.Light,ThemeColors.DarkTheme})
        {changeTheme(palette);Win32.SendMessageW(window,0x1A,0,0);Pump();RenderHelp(desktop.ToolsWindow,output is null?null:output+(palette.Dark?"-gemini-key-dark.bmp":"-gemini-key-light.bmp"),60);}
        if(saves!=0)throw new Exception("Opening key settings persisted a credential.");
        Win32.SendMessageW(desktop.ToolsWindow,0x111,4491,0);
        if(saves!=1||key!="fixture-google-ui-key"||desktop.ToolsWindow!=0)throw new Exception("Explicit key save failed or did not close the form.");
        Win32.SendMessageW(window,0x111,102,0);
        if(Win32.GetWindowTextLengthW(desktop.ToolField("GeminiKey"))!=0)throw new Exception("Saved key was revealed on reopen.");
        Win32.SendMessageW(desktop.ToolsWindow,0x111,4205,0);
        if(key!="fixture-google-ui-key"||saves!=1)throw new Exception("Cancel replaced the key.");
        desktop.ConnectionTestRequested=(provider,model,ct)=>{probes++;validProbe=provider=="gemini_api"&&model=="auto";if(provider!="gemini_api"||model!="auto")throw new Exception("Connection probe used the wrong draft provider/model.");return Task.FromResult(new ConnectionTestResult("gemini-fixture-version",TimeSpan.FromMilliseconds(10)));};
        Win32.SendMessageW(window,0x111,112,0);Pump();
        if(probes!=1||!validProbe)throw new Exception("Google API connection test was not exposed.");
        Win32.SendMessageW(window,0x111,102,0);Win32.SendMessageW(desktop.ToolsWindow,0x111,4492,0);
        if(removals!=1||key.Length!=0)throw new Exception("Remove Saved Key did not remove only the API credential.");
        desktop.HasGeminiApiKey=null;desktop.SaveGeminiApiKeyRequested=null;desktop.RemoveGeminiApiKeyRequested=null;desktop.ConnectionTestRequested=null;
        Win32.SendMessageW(backend,0x14E,0,0);Win32.SendMessageW(window,0x111,(1u<<16)|1002,backend);
        Console.WriteLine("PASS Google API provider, masked add/replace key, cancel, remove, connection test and OS themes without real credentials.");
    }
}
